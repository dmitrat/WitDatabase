using Microsoft.AspNetCore.Identity;

namespace OutWit.Database.Samples.IdentityServer.Models;

/// <summary>
/// Application user extending ASP.NET Core Identity user.
/// </summary>
public class ApplicationUser : IdentityUser<int>
{
    #region Properties

    /// <summary>
    /// User's first name.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// User's last name.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// User's display name.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(LastName) 
        ? FirstName 
        : $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// User's avatar URL (optional).
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Date when the user was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date when the user was last modified.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Date when the user last logged in.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Indicates if the user is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    #endregion
}
