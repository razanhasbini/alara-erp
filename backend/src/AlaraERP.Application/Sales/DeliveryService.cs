using AlaraERP.Application.Common;
using AlaraERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Sales;

/// <summary>Delivery companies and the cash they collect on ÀLara Line's behalf.</summary>
public sealed class DeliveryService(IAlaraDbContext db, IDocumentNumbers numbers, BusinessClock clock, ICurrentUser currentUser, IUserDirectory users)
{
    public async Task<IReadOnlyList<DeliveryCompanyDto>> ListCompaniesAsync(bool includeInactive, CancellationToken ct) =>
        await Project(db.DeliveryCompanies.AsNoTracking().Where(d => includeInactive || d.IsActive))
            .OrderByDescending(d => d.IsActive).ThenBy(d => d.Name).ToListAsync(ct);

    public async Task<DeliveryCompanyDto> GetCompanyAsync(Guid id, CancellationToken ct) =>
        await Project(db.DeliveryCompanies.AsNoTracking().Where(d => d.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Delivery company", id);

    public async Task<DeliveryCompanyDto> CreateCompanyAsync(DeliveryCompanyRequest request, CancellationToken ct)
    {
        await EnsureNameFreeAsync(request.Name, null, ct);
        var company = new DeliveryCompany(request.Name, request.Phone);
        company.Update(request.Name, request.Phone, request.IsActive);
        db.DeliveryCompanies.Add(company);
        await db.SaveChangesAsync(ct);
        return await GetCompanyAsync(company.Id, ct);
    }

    public async Task<DeliveryCompanyDto> UpdateCompanyAsync(Guid id, DeliveryCompanyRequest request, CancellationToken ct)
    {
        var company = await db.DeliveryCompanies.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Delivery company", id);
        await EnsureNameFreeAsync(request.Name, id, ct);
        company.Update(request.Name, request.Phone, request.IsActive);
        await db.SaveChangesAsync(ct);
        return await GetCompanyAsync(id, ct);
    }

    /// <summary>Delivered COD orders whose cash is still with the courier.</summary>
    public async Task<IReadOnlyList<RemittanceOrderDto>> AwaitingRemittanceAsync(Guid deliveryCompanyId, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.DeliveryCompanyId == deliveryCompanyId && o.PaymentStatus == PaymentStatus.CollectedByCourier
                        && o.CourierRemittanceId == null)
            .OrderBy(o => o.DeliveredAt)
            .Select(o => new RemittanceOrderDto(o.Id, o.OrderNumber, o.RecipientName, o.DeliveredAt, o.GrandTotal, o.CourierFee))
            .ToListAsync(ct);

    public async Task<RemittanceDto> RecordRemittanceAsync(CreateRemittanceRequest request, CancellationToken ct)
    {
        var company = await db.DeliveryCompanies.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DeliveryCompanyId, ct)
                      ?? throw new NotFoundException("Delivery company", request.DeliveryCompanyId);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var orders = await db.Orders.Where(o => request.OrderIds.Contains(o.Id)).ToListAsync(ct);
        var missing = request.OrderIds.Except(orders.Select(o => o.Id)).FirstOrDefault();
        if (missing != Guid.Empty) throw new NotFoundException("Order", missing);

        var number = await numbers.NextRemittanceNumberAsync(ct);
        var remittance = new CourierRemittance(number, company.Id, orders.OrderBy(o => o.OrderNumber).ToList(),
            request.ReceivedAt ?? clock.UtcNow, request.Notes, currentUser.UserId, clock.UtcNow);
        db.CourierRemittances.Add(remittance);
        await db.SaveChangesAsync(ct); // order xmin tokens stop the same order being remitted twice concurrently
        await tx.CommitAsync(ct);
        return await GetRemittanceAsync(remittance.Id, ct);
    }

    public async Task<PagedResult<RemittanceDto>> ListRemittancesAsync(Guid? deliveryCompanyId, int pageNumber, int pageSize, CancellationToken ct)
    {
        var page = new PageRequest { Page = pageNumber, PageSize = pageSize };
        var q = db.CourierRemittances.AsNoTracking();
        if (deliveryCompanyId is { } id) q = q.Where(r => r.DeliveryCompanyId == id);
        var total = await q.CountAsync(ct);
        var ids = await q.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Skip(page.Skip).Take(page.SafePageSize).Select(r => r.Id).ToListAsync(ct);
        var items = new List<RemittanceDto>();
        foreach (var rid in ids) items.Add(await GetRemittanceAsync(rid, ct, includeOrders: false));
        return new PagedResult<RemittanceDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<RemittanceDto> GetRemittanceAsync(Guid id, CancellationToken ct) => await GetRemittanceAsync(id, ct, includeOrders: true);

    private async Task<RemittanceDto> GetRemittanceAsync(Guid id, CancellationToken ct, bool includeOrders)
    {
        var r = await db.CourierRemittances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Remittance", id);
        var company = await db.DeliveryCompanies.Where(d => d.Id == r.DeliveryCompanyId).Select(d => d.Name).FirstAsync(ct);
        var names = await users.GetDisplayNamesAsync([r.RecordedByUserId], ct);
        var orders = includeOrders
            ? await db.Orders.AsNoTracking().Where(o => o.CourierRemittanceId == id).OrderBy(o => o.OrderNumber)
                .Select(o => new RemittanceOrderDto(o.Id, o.OrderNumber, o.RecipientName, o.DeliveredAt, o.GrandTotal, o.CourierFee))
                .ToListAsync(ct)
            : [];
        return new RemittanceDto(r.Id, r.RemittanceNumber, r.DeliveryCompanyId, company, r.AmountCollected, r.CourierFees,
            r.NetAmount, r.OrderCount, r.ReceivedAt, r.Notes,
            r.RecordedByUserId is { } u ? names.GetValueOrDefault(u) : null, r.CreatedAt, orders);
    }

    private async Task EnsureNameFreeAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var normalized = name.Trim().ToLower();
        if (await db.DeliveryCompanies.AnyAsync(d => d.Name.ToLower() == normalized && d.Id != excludeId, ct))
            throw new ConflictException("delivery_companies.duplicate_name", $"A delivery company named '{name.Trim()}' already exists.");
    }

    private IQueryable<DeliveryCompanyDto> Project(IQueryable<DeliveryCompany> q) => q.Select(d => new DeliveryCompanyDto(
        d.Id, d.Name, d.Phone, d.IsActive,
        db.Orders.Count(o => o.DeliveryCompanyId == d.Id && o.PaymentStatus == PaymentStatus.CollectedByCourier),
        db.Orders.Where(o => o.DeliveryCompanyId == d.Id && o.PaymentStatus == PaymentStatus.CollectedByCourier).Sum(o => (decimal?)o.GrandTotal) ?? 0,
        db.Orders.Where(o => o.DeliveryCompanyId == d.Id && o.PaymentStatus == PaymentStatus.CollectedByCourier).Sum(o => o.CourierFee) ?? 0));
}
