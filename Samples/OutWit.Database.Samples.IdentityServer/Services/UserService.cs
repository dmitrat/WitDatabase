using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OutWit.Database.Samples.IdentityServer.Data;
using OutWit.Database.Samples.IdentityServer.Models;

namespace OutWit.Database.Samples.IdentityServer.Services;

/// <summary>
/// Service for managing application users.
/// </summary>
public class UserService
{
    #region Fields

    private readonly UserManager<ApplicationUser> m_userManager;
    private readonly RoleManager<ApplicationRole> m_roleManager;
    private readonly ApplicationDbContext m_context;
    private readonly ILogger<UserService> m_logger;

    #endregion

    #region Constructors

    public UserService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ApplicationDbContext context,
        ILogger<UserService> logger)
    {
        m_userManager = userManager;
        m_roleManager = roleManager;
        m_context = context;
        m_logger = logger;
    }

    #endregion

    #region Read Operations

    public async Task<List<ApplicationUser>> GetAllUsersAsync()
    {
        return await m_context.Users
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();
    }

    public async Task<ApplicationUser?> GetUserByIdAsync(int id)
    {
        return await m_userManager.FindByIdAsync(id.ToString());
    }

    public async Task<ApplicationUser?> GetUserByEmailAsync(string email)
    {
        return await m_userManager.FindByEmailAsync(email);
    }

    public async Task<List<string>> GetUserRolesAsync(ApplicationUser user)
    {
        return (await m_userManager.GetRolesAsync(user)).ToList();
    }

    public async Task<List<ApplicationRole>> GetAllRolesAsync()
    {
        return await m_context.Roles.ToListAsync();
    }

    public async Task<int> GetUserCountAsync()
    {
        return await m_context.Users.CountAsync();
    }

    public async Task<int> GetActiveUserCountAsync()
    {
        return await m_context.Users.CountAsync(u => u.IsActive);
    }

    #endregion

    #region Write Operations

    public async Task<(bool Success, string[] Errors)> CreateUserAsync(
        string email,
        string password,
        string firstName,
        string? lastName = null,
        string? role = null)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName ?? string.Empty,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await m_userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            return (false, result.Errors.Select(e => e.Description).ToArray());
        }

        if (!string.IsNullOrEmpty(role) && await m_roleManager.RoleExistsAsync(role))
        {
            await m_userManager.AddToRoleAsync(user, role);
        }

        m_logger.LogInformation("Created user: {Email}", email);
        return (true, []);
    }

    public async Task<(bool Success, string[] Errors)> UpdateUserAsync(ApplicationUser user)
    {
        user.UpdatedAt = DateTime.UtcNow;
        var result = await m_userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return (false, result.Errors.Select(e => e.Description).ToArray());
        }

        m_logger.LogInformation("Updated user: {Email}", user.Email);
        return (true, []);
    }

    public async Task<(bool Success, string[] Errors)> DeleteUserAsync(int userId)
    {
        var user = await m_userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, ["User not found"]);
        }

        var result = await m_userManager.DeleteAsync(user);

        if (!result.Succeeded)
        {
            return (false, result.Errors.Select(e => e.Description).ToArray());
        }

        m_logger.LogInformation("Deleted user: {Email}", user.Email);
        return (true, []);
    }

    public async Task UpdateLastLoginAsync(ApplicationUser user)
    {
        user.LastLoginAt = DateTime.UtcNow;
        await m_userManager.UpdateAsync(user);
    }

    #endregion
}
