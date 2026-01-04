using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.Samples.IdentityServer.Data;
using OutWit.Database.Samples.IdentityServer.Models;

namespace OutWit.Database.Samples.IdentityServer.Services;

/// <summary>
/// Hosted service that initializes the database with migrations and seed data.
/// </summary>
public class DatabaseInitializerService : IHostedService
{
    #region Constants

    private const string DEFAULT_ADMIN_EMAIL = "admin@example.com";
    private const string DEFAULT_ADMIN_PASSWORD = "Admin123!";
    private const string DEFAULT_ADMIN_FIRST_NAME = "Admin";
    private const string ADMIN_ROLE = "Administrator";
    private const string USER_ROLE = "User";

    #endregion

    #region Fields

    private readonly IServiceProvider m_serviceProvider;
    private readonly ILogger<DatabaseInitializerService> m_logger;

    #endregion

    #region Constructors

    public DatabaseInitializerService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializerService> logger)
    {
        m_serviceProvider = serviceProvider;
        m_logger = logger;
    }

    #endregion

    #region IHostedService

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        m_logger.LogInformation("Initializing database...");

        using var scope = m_serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        await ApplyMigrationsAsync(context, cancellationToken);
        await SeedRolesAsync(roleManager);
        await SeedAdminUserAsync(userManager);

        m_logger.LogInformation("Database initialization completed");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    #endregion

    #region Migrations

    private async Task ApplyMigrationsAsync(ApplicationDbContext context, CancellationToken cancellationToken)
    {
        m_logger.LogInformation("Applying database migrations...");

        try
        {
            await context.Database.MigrateAsync(cancellationToken);
            m_logger.LogInformation("Database migrations applied successfully");
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to apply database migrations");
            throw;
        }
    }

    #endregion

    #region Seed Data

    private async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        var roles = new[] { ADMIN_ROLE, USER_ROLE };

        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var role = new ApplicationRole(roleName)
                {
                    Description = roleName == ADMIN_ROLE 
                        ? "Full system administrator access" 
                        : "Standard user access",
                    CreatedAt = DateTime.UtcNow
                };

                var result = await roleManager.CreateAsync(role);
                if (result.Succeeded)
                {
                    m_logger.LogInformation("Created role: {RoleName}", roleName);
                }
                else
                {
                    m_logger.LogWarning("Failed to create role {RoleName}: {Errors}", 
                        roleName, string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }

    private async Task SeedAdminUserAsync(UserManager<ApplicationUser> userManager)
    {
        var adminUser = await userManager.FindByEmailAsync(DEFAULT_ADMIN_EMAIL);

        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = DEFAULT_ADMIN_EMAIL,
                Email = DEFAULT_ADMIN_EMAIL,
                EmailConfirmed = true,
                FirstName = DEFAULT_ADMIN_FIRST_NAME,
                LastName = string.Empty,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(adminUser, DEFAULT_ADMIN_PASSWORD);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, ADMIN_ROLE);
                m_logger.LogInformation("Created admin user: {Email} with password: {Password}", 
                    DEFAULT_ADMIN_EMAIL, DEFAULT_ADMIN_PASSWORD);
            }
            else
            {
                m_logger.LogWarning("Failed to create admin user: {Errors}", 
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
        else
        {
            m_logger.LogInformation("Admin user already exists: {Email}", DEFAULT_ADMIN_EMAIL);
        }
    }

    #endregion
}
