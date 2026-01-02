using OutWit.Database.Samples.BlazorWasm.Models;

namespace OutWit.Database.Samples.BlazorWasm.Data;

/// <summary>
/// Repository for Contact entities.
/// </summary>
public class ContactRepository
{
    private readonly DatabaseService _db;

    public ContactRepository(DatabaseService db)
    {
        _db = db;
    }

    public async Task<List<Contact>> GetAllAsync(CancellationToken ct = default)
    {
        var contacts = await _db.GetAllAsync<Contact>(DatabaseService.ContactsPrefix, ct);
        return contacts.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ToList();
    }

    public async Task<Contact?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.GetByIdAsync<Contact>(DatabaseService.ContactsPrefix, id, ct);
    }

    public async Task<Contact> CreateAsync(Contact contact, CancellationToken ct = default)
    {
        contact.Id = await _db.GetNextIdAsync("contact", ct);
        contact.CreatedAt = DateTime.UtcNow;
        return await _db.SaveAsync(DatabaseService.ContactsPrefix, contact.Id, contact, ct);
    }

    public async Task<Contact> UpdateAsync(Contact contact, CancellationToken ct = default)
    {
        contact.UpdatedAt = DateTime.UtcNow;
        return await _db.SaveAsync(DatabaseService.ContactsPrefix, contact.Id, contact, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        return await _db.DeleteAsync(DatabaseService.ContactsPrefix, id, ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        return await _db.CountAsync(DatabaseService.ContactsPrefix, ct);
    }

    public async Task<List<Contact>> GetFavoritesAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.Where(c => c.IsFavorite).ToList();
    }

    public async Task<List<Contact>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await GetAllAsync(ct);

        var all = await GetAllAsync(ct);
        var q = query.ToLowerInvariant();
        
        return all.Where(c =>
            c.FirstName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.LastName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.Email.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            c.Company.Contains(q, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await _db.ClearAsync(DatabaseService.ContactsPrefix, ct);
    }
}
