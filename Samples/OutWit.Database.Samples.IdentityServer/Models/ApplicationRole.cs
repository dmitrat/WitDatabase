using Microsoft.AspNetCore.Identity;

namespace OutWit.Database.Samples.IdentityServer.Models;

/// <summary>
/// Application role extending ASP.NET Core Identity role.
/// </summary>
public class ApplicationRole : IdentityRole<int>
{
    #region Constructors

    public ApplicationRole()
    {
    }

    public ApplicationRole(string roleName) : base(roleName)
    {
    }

    #endregion

    #region Properties

    /// <summary>
    /// Description of the role.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Date when the role was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    #endregion
}
