using AlaraERP.Application.Catalog;
using AlaraERP.Application.Common;
using AlaraERP.Application.Dashboard;
using AlaraERP.Application.Inventory;
using AlaraERP.Application.Inventory.Contracts;
using AlaraERP.Application.Sales;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlaraERP.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.Configure<ErpOptions>(configuration.GetSection(ErpOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<BusinessClock>();

        // Catalog
        services.AddScoped<CategoryService>();
        services.AddScoped<ProductService>();
        services.AddScoped<ProductImageService>();

        // Inventory
        services.AddScoped<WarehouseService>();
        services.AddScoped<IWarehouseDirectory>(sp => sp.GetRequiredService<WarehouseService>());
        services.AddScoped<InventorySettingsService>();
        services.AddScoped<StockLedger>();
        services.AddScoped<StockQueryService>();
        services.AddScoped<StockOperationsService>();
        services.AddScoped<IStockReservationService, StockReservationService>();

        // Sales
        services.AddScoped<OrderService>();
        services.AddScoped<DeliveryService>();

        // Dashboard
        services.AddScoped<DashboardService>();
        return services;
    }
}
