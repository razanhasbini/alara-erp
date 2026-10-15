using AlaraERP.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Infrastructure.Persistence;

internal sealed class DocumentNumbers(AlaraDbContext db) : IDocumentNumbers
{
    public Task<string> NextOrderNumberAsync(CancellationToken ct) => NextAsync("AL", Schemas.Sales, AlaraDbContext.OrderNumberSequence, ct);
    public Task<string> NextReceiptNumberAsync(CancellationToken ct) => NextAsync("RC", Schemas.Inventory, AlaraDbContext.ReceiptNumberSequence, ct);
    public Task<string> NextTransferNumberAsync(CancellationToken ct) => NextAsync("TR", Schemas.Inventory, AlaraDbContext.TransferNumberSequence, ct);
    public Task<string> NextRemittanceNumberAsync(CancellationToken ct) => NextAsync("RM", Schemas.Sales, AlaraDbContext.RemittanceNumberSequence, ct);

    private async Task<string> NextAsync(string prefix, string schema, string sequence, CancellationToken ct)
    {
        var qualified = $"{schema}.{sequence}"; // constants only — never user input
        var value = await db.Database.SqlQuery<long>($"SELECT nextval({qualified}::regclass) AS \"Value\"").SingleAsync(ct);
        return $"{prefix}-{value:D6}";
    }
}
