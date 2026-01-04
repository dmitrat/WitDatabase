using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.Samples.IdentityServer.Data;
using OutWit.Database.Samples.IdentityServer.Models;
using OutWit.Database.Samples.IdentityServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add MVC services
builder.Services.AddControllersWithViews();

// Configure WitDatabase with Entity Framework Core
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=identity_server.witdb";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseWitDb(connectionString);

    // Register OpenIddict entity sets
    options.UseOpenIddict<int>();
});

// Configure ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Configure OpenIddict
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        // Use Entity Framework Core stores
        options.UseEntityFrameworkCore()
            .UseDbContext<ApplicationDbContext>()
            .ReplaceDefaultEntities<int>();
    })
    .AddServer(options =>
    {
        // Enable authorization code flow
        options.AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow();

        // Enable client credentials flow (for machine-to-machine)
        options.AllowClientCredentialsFlow();

        // Set token endpoints
        options.SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token");

        // Register scopes
        options.RegisterScopes("openid", "profile", "email", "roles");

        // Use development encryption/signing keys (for demo only)
        options.AddDevelopmentEncryptionCertificate()
            .AddDevelopmentSigningCertificate();

        // Enable ASP.NET Core integration
        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// Configure authentication cookie
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

// Register application services
builder.Services.AddScoped<UserService>();

// Register database initializer
builder.Services.AddHostedService<DatabaseInitializerService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

Console.WriteLine("==============================================");
Console.WriteLine("  WitDatabase + OpenIddict Identity Server");
Console.WriteLine("==============================================");
Console.WriteLine();
Console.WriteLine("Default admin credentials:");
Console.WriteLine("  Email:    admin@example.com");
Console.WriteLine("  Password: Admin123!");
Console.WriteLine();
Console.WriteLine("OpenIddict Endpoints:");
Console.WriteLine("  Authorization: /connect/authorize");
Console.WriteLine("  Token:         /connect/token");
Console.WriteLine();
Console.WriteLine("==============================================");

app.Run();
