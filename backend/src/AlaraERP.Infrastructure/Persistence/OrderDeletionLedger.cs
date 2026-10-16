using AlaraERP.Application.Sales;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Infrastructure.Persistence;

internal sealed class OrderDeletionLedger(AlaraDbContext db) : IOrderDeletionLedger
{
    public Task EraseAsync(Guid orderId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"CALL sales.erase_test_order_ledger({orderId})", ct);
}
