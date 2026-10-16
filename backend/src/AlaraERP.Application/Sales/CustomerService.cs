using AlaraERP.Application.Common;
using AlaraERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Sales;

public sealed class CustomerService(IAlaraDbContext db)
{
    public async Task<PagedResult<CustomerDto>> ListAsync(CustomerListQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var customers = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = Text.ContainsPattern(query.Search);
            var digits = Customer.NormalizePhoneKey(query.Search);
            customers = customers.Where(c => EF.Functions.Like(c.Name.ToLower(), term, "\\")
                || (c.Email != null && EF.Functions.Like(c.Email.ToLower(), term, "\\"))
                || (c.City != null && EF.Functions.Like(c.City.ToLower(), term, "\\"))
                || (digits.Length >= 3 && c.NormalizedPhone.Contains(digits)));
        }
        if (query.IsActive is { } active) customers = customers.Where(c => c.IsActive == active);

        var total = await customers.CountAsync(ct);
        var rows = await Project(customers.OrderByDescending(c => c.IsActive).ThenBy(c => c.Name).ThenBy(c => c.Id))
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<CustomerDto>(rows, page.SafePage, page.SafePageSize, total);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Customers.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Customer", id);

    public async Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken ct)
    {
        var customer = new Customer(request.Name, request.Phone, request.Email, request.City, request.Address, request.Notes);
        await EnsurePhoneFreeAsync(customer.NormalizedPhone, null, ct);
        if (!request.IsActive) customer.SetActive(false);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return await GetAsync(customer.Id, ct);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, CustomerRequest request, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Customer", id);
        customer.Update(request.Name, request.Phone, request.Email, request.City, request.Address, request.Notes);
        await EnsurePhoneFreeAsync(customer.NormalizedPhone, id, ct);
        customer.SetActive(request.IsActive);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task EnsurePhoneFreeAsync(string normalizedPhone, Guid? excludeId, CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(c => c.NormalizedPhone == normalizedPhone && c.Id != excludeId, ct))
            throw new ConflictException("customers.duplicate_phone", "A customer with this phone number already exists.");
    }

    private IQueryable<CustomerDto> Project(IQueryable<Customer> customers) => customers.Select(c => new CustomerDto(
        c.Id, c.Name, c.Phone, c.Email, c.City, c.Address, c.Notes, c.IsActive,
        db.Orders.Count(o => o.CustomerId == c.Id &&
            (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered)),
        db.Orders.Where(o => o.CustomerId == c.Id &&
            (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered))
            .Sum(o => (decimal?)o.GrandTotal) ?? 0,
        db.Orders.Where(o => o.CustomerId == c.Id && o.PaymentStatus == PaymentStatus.Paid &&
            (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered))
            .Sum(o => (decimal?)o.GrandTotal) ?? 0,
        db.Orders.Where(o => o.CustomerId == c.Id &&
            (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered))
            .Average(o => (decimal?)o.GrandTotal) ?? 0,
        db.Orders.Where(o => o.CustomerId == c.Id &&
            (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered))
            .Min(o => o.ConfirmedAt),
        db.Orders.Where(o => o.CustomerId == c.Id &&
            (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered))
            .Max(o => o.ConfirmedAt),
        c.CreatedAt, c.UpdatedAt));
}
