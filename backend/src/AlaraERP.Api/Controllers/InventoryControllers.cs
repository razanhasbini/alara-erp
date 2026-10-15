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


