using OutWit.Database.Samples.BlazorWasm.Models;

namespace OutWit.Database.Samples.BlazorWasm.Data;

/// <summary>
/// Repository for Note entities.
/// </summary>
public class NoteRepository
{
    private readonly DatabaseService _db;

    public NoteRepository(DatabaseService db)
    {
        _db = db;
    }

    public async Task<List<Note>> GetAllAsync(CancellationToken ct = default)
    {
        var notes = await _db.GetAllAsync<Note>(DatabaseService.NotesPrefix, ct);
        // Pinned notes first, then by creation date descending
        return notes
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .ToList();
    }

    public async Task<Note?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.GetByIdAsync<Note>(DatabaseService.NotesPrefix, id, ct);
    }

    public async Task<Note> CreateAsync(Note note, CancellationToken ct = default)
    {
        note.Id = await _db.GetNextIdAsync("note", ct);
        note.CreatedAt = DateTime.UtcNow;
        return await _db.SaveAsync(DatabaseService.NotesPrefix, note.Id, note, ct);
    }

    public async Task<Note> UpdateAsync(Note note, CancellationToken ct = default)
    {
        note.UpdatedAt = DateTime.UtcNow;
        return await _db.SaveAsync(DatabaseService.NotesPrefix, note.Id, note, ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        return await _db.DeleteAsync(DatabaseService.NotesPrefix, id, ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        return await _db.CountAsync(DatabaseService.NotesPrefix, ct);
    }

    public async Task<List<Note>> GetPinnedAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.Where(n => n.IsPinned).ToList();
    }

    public async Task<List<Note>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return await GetAllAsync(ct);

        var all = await GetAllAsync(ct);
        var q = query.ToLowerInvariant();
        
        return all.Where(n =>
            n.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            n.Content.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            n.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }

    public async Task<List<Note>> GetByColorAsync(NoteColor color, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.Where(n => n.Color == color).ToList();
    }

    public async Task<List<string>> GetAllTagsAsync(CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.SelectMany(n => n.Tags).Distinct().OrderBy(t => t).ToList();
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await _db.ClearAsync(DatabaseService.NotesPrefix, ct);
    }
}
