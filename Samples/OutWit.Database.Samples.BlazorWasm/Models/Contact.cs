namespace OutWit.Database.Samples.BlazorWasm.Models;

/// <summary>
/// Contact entity stored in WitDatabase.
/// </summary>
public class Contact
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public ContactCategory Category { get; set; } = ContactCategory.Personal;
    public bool IsFavorite { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(LastName) 
        ? FirstName 
        : $"{FirstName} {LastName}".Trim();
    
    public string Initials => string.IsNullOrWhiteSpace(FullName) 
        ? "?" 
        : string.Concat(FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => char.ToUpper(s[0])));
}

/// <summary>
/// Contact category enumeration.
/// </summary>
public enum ContactCategory
{
    Personal,
    Work,
    Family,
    Friend,
    Other
}
