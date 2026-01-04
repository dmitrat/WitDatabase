namespace OutWit.Database.Samples.IdentityServer.Models;

/// <summary>
/// View model for displaying user in the list.
/// </summary>
public class UserViewModel
{
    #region Properties

    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public List<string> Roles { get; set; } = [];

    #endregion
}
