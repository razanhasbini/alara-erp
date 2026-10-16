using AlaraERP.Application.Common;
using AlaraERP.Application.Identity;
using AlaraERP.Application.Inventory;
using AlaraERP.Infrastructure.Notifications;
using AlaraERP.Infrastructure.Seeding;
using AlaraERP.Infrastructure.Storage;
using AlaraERP.Infrastructure.Identity;
using AlaraERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlaraERP.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AuditInterceptor>();

        services.AddDbContext<AlaraDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "Connection string 'Default' is not configured. See README → Database setup.");
            DbContextOptionsConfigurator.Configure(options, connectionString)
                .AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
        });

        services
            .AddIdentityCore<AppUser>(o =>
            {
                var simpleDevelopmentPasswords = configuration.GetValue<bool>("Auth:SimpleDevelopmentPasswords");
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = simpleDevelopmentPasswords ? 8 : 10;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = !simpleDevelopmentPasswords;
                o.Password.RequireUppercase = !simpleDevelopmentPasswords;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = simpleDevelopmentPasswords ? 1 : 4;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AlaraDbContext>()
            .AddSignInManager<AlaraSignInManager>()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();
        services.TryAddScoped<ISecurityStampValidator, SecurityStampValidator<AppUser>>();

        services.AddScoped<IAlaraDbContext>(sp => sp.GetRequiredService<AlaraDbContext>());
        services.AddScoped<IDocumentNumbers, DocumentNumbers>();
        services.AddScoped<IStockLocks, StockLocks>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<AdminBootstrapper>();
        services.AddScoped<UserProvisioner>();
        services.AddSingleton<DatabaseMigrator>();
        services.AddScoped<CatalogImporter>();
        services.AddHttpClient<AlaraERP.Application.Notifications.IWhatsAppSender, MetaWhatsAppSender>(c => c.Timeout = TimeSpan.FromSeconds(20));

        return services;
    }
}
