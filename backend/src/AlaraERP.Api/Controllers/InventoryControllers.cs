using AlaraERP.Api.Security;
using AlaraERP.Application.Common;
using AlaraERP.Application.Inventory;
using Microsoft.AspNetCore.Mvc;
using static AlaraERP.Application.Common.Security.Permissions;

namespace AlaraERP.Api.Controllers;

[Route("api/warehouses")]
public sealed class WarehousesController(WarehouseService warehouses) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Inventory.View)]
    public Task<IReadOnlyList<WarehouseDto>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        warehouses.ListAsync(includeInactive, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Inventory.View)]
    public Task<WarehouseDto> Get(Guid id, CancellationToken ct) => warehouses.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Warehouses.Manage)]
    [ProducesResponseType<WarehouseDto>(201)]
    public async Task<IActionResult> Create(WarehouseRequest request, CancellationToken ct)
    {
        var warehouse = await warehouses.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = warehouse.Id }, warehouse);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Warehouses.Manage)]
    public Task<WarehouseDto> Update(Guid id, WarehouseRequest request, CancellationToken ct) => warehouses.UpdateAsync(id, request, ct);
}

[Route("api/inventory")]
public sealed class InventoryController(
    StockQueryService queries, StockOperationsService operations, InventorySettingsService settings) : ApiControllerBase
{
    /// <summary>Stock per individual product (bundles have no stock of their own).</summary>
    [HttpGet]
    [HasPermission(Inventory.View)]
    public Task<PagedResult<StockRowDto>> List([FromQuery] StockListQuery query, CancellationToken ct) =>
        queries.ListStockAsync(query, ct);

    [HttpGet("products/{productId:guid}")]
    [HasPermission(Inventory.View)]
    public Task<ProductStockDto> ProductStock(Guid productId, CancellationToken ct) => queries.GetProductStockAsync(productId, ct);

    /// <summary>Sellable quantities for products and bundles, e.g. while building an order.</summary>
    [HttpGet("availability")]
    [HasPermission(Inventory.View)]
    public async Task<ActionResult<IReadOnlyList<AvailabilityDto>>> Availability(
        [FromQuery] Guid[] productIds, [FromQuery] Guid? warehouseId, CancellationToken ct)
    {
        if (productIds.Length is 0 or > 200)
            return ProblemWithCode(400, "validation_failed", "Pass between 1 and 200 productIds.");
        return Ok(await queries.GetAvailabilityAsync(productIds, warehouseId, ct));
    }

    [HttpGet("settings/{productId:guid}")]
    [HasPermission(Inventory.View)]
    public Task<InventorySettingsDto> GetSettings(Guid productId, CancellationToken ct) => settings.GetAsync(productId, ct);

    [HttpPut("settings/{productId:guid}")]
    [HasPermission(Inventory.ManageThresholds)]
    public Task<InventorySettingsDto> UpdateSettings(Guid productId, UpdateInventorySettingsRequest request, CancellationToken ct) =>
        settings.UpdateAsync(productId, request, ct);

    [HttpGet("receipts")]
    [HasPermission(Inventory.View)]
    public Task<PagedResult<ReceiptSummaryDto>> Receipts([FromQuery] Guid? warehouseId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        queries.ListReceiptsAsync(warehouseId, search, page, pageSize, ct);

    [HttpGet("receipts/{id:guid}")]
    [HasPermission(Inventory.View)]
    public Task<ReceiptDto> Receipt(Guid id, CancellationToken ct) => queries.GetReceiptAsync(id, ct);

    [HttpPost("receipts")]
    [HasPermission(Inventory.Receive)]
    [ProducesResponseType<ReceiptDto>(201)]
    public async Task<IActionResult> Receive(ReceiveStockRequest request, CancellationToken ct)
    {
        var receipt = await operations.ReceiveAsync(request, ct);
        return CreatedAtAction(nameof(Receipt), new { id = receipt.Id }, receipt);
    }

    [HttpPost("adjustments")]
    [HasPermission(Inventory.Adjust)]
    public Task<MovementDto> Adjust(AdjustStockRequest request, CancellationToken ct) => operations.AdjustAsync(request, ct);

    /// <summary>Fixes a miscount: sets the units on hand to the counted number and records the difference.</summary>
    [HttpPost("counts")]
    [HasPermission(Inventory.Adjust)]
    public Task<ProductStockDto> CorrectCount(CorrectCountRequest request, CancellationToken ct) => operations.CorrectCountAsync(request, ct);

    /// <summary>Takes whole bundles out of stock (PR, gifts, damage): deducts each component, never a "bundle stock".</summary>
    [HttpPost("bundle-withdrawals")]
    [HasPermission(Inventory.Adjust)]
    public Task<IReadOnlyList<MovementDto>> WithdrawBundle(WithdrawBundleRequest request, CancellationToken ct) =>
        operations.WithdrawBundleAsync(request, ct);

    [HttpGet("transfers")]
    [HasPermission(Inventory.View)]
    public Task<PagedResult<TransferDto>> Transfers([FromQuery] Guid? warehouseId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        queries.ListTransfersAsync(warehouseId, page, pageSize, ct);

    [HttpGet("transfers/{id:guid}")]
    [HasPermission(Inventory.View)]
    public Task<TransferDto> Transfer(Guid id, CancellationToken ct) => queries.GetTransferAsync(id, ct);

    [HttpPost("transfers")]
    [HasPermission(Inventory.Transfer)]
    [ProducesResponseType<TransferDto>(201)]
    public async Task<IActionResult> CreateTransfer(TransferStockRequest request, CancellationToken ct)
    {
        var transfer = await operations.TransferAsync(request, ct);
        return CreatedAtAction(nameof(Transfer), new { id = transfer.Id }, transfer);
    }

    [HttpGet("movements")]
    [HasPermission(Inventory.View)]
    public Task<PagedResult<MovementDto>> Movements([FromQuery] MovementQuery query, CancellationToken ct) =>
        queries.ListMovementsAsync(query, ct);
}



/// <summary>Packaging boxes. Kept apart from selling: orders never change box counts.</summary>
[Route("api/boxes")]
public sealed class BoxesController(BoxService boxes) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Boxes.View)]
    public Task<IReadOnlyList<BoxDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => boxes.ListAsync(includeInactive, ct);

    /// <summary>Boxes made for these products (or for products inside these bundles), e.g. while entering an order.</summary>
    [HttpGet("for-products")]
    [HasPermission(Boxes.View)]
    public Task<IReadOnlyList<BoxDto>> ForProducts([FromQuery] Guid[] productIds, CancellationToken ct) => boxes.ForProductsAsync(productIds, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Boxes.View)]
    public Task<BoxDto> Get(Guid id, CancellationToken ct) => boxes.GetAsync(id, ct);

    [HttpGet("{id:guid}/history")]
    [HasPermission(Boxes.View)]
    public Task<IReadOnlyList<BoxMovementDto>> History(Guid id, CancellationToken ct) => boxes.HistoryAsync(id, ct);

    [HttpPost]
    [HasPermission(Boxes.Manage)]
    [ProducesResponseType<BoxDto>(201)]
    public async Task<IActionResult> Create(SaveBoxRequest request, CancellationToken ct)
    {
        var box = await boxes.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = box.Id }, box);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Boxes.Manage)]
    public Task<BoxDto> Update(Guid id, SaveBoxRequest request, CancellationToken ct) => boxes.UpdateAsync(id, request, ct);

    /// <summary>Add boxes delivered, take out boxes used or damaged, or set the count to what was counted.</summary>
    [HttpPost("{id:guid}/count")]
    [HasPermission(Boxes.Manage)]
    public Task<BoxDto> ChangeCount(Guid id, ChangeBoxCountRequest request, CancellationToken ct) => boxes.ChangeCountAsync(id, request, ct);

    [HttpPut("{id:guid}/active")]
    [HasPermission(Boxes.Manage)]
    public Task<BoxDto> SetActive(Guid id, SetBoxActiveRequest request, CancellationToken ct) => boxes.SetActiveAsync(id, request.IsActive, ct);
}
