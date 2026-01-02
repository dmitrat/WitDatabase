using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutWit.Database.Samples.BlazorWasm.Data;
using OutWit.Database.Samples.BlazorWasm.Models;

namespace OutWit.Database.Samples.BlazorWasm.ViewModels;

/// <summary>
/// ViewModel for the Notes page.
/// </summary>
public partial class NotesViewModel : ObservableObject
{
    private readonly NoteRepository _repository;

    public NotesViewModel(NoteRepository repository)
    {
        _repository = repository;
    }

    #region Observable Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredNotes))]
    [NotifyPropertyChangedFor(nameof(PinnedNotes))]
    [NotifyPropertyChangedFor(nameof(UnpinnedNotes))]
    private List<Note> _notes = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredNotes))]
    [NotifyPropertyChangedFor(nameof(PinnedNotes))]
    [NotifyPropertyChangedFor(nameof(UnpinnedNotes))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredNotes))]
    [NotifyPropertyChangedFor(nameof(PinnedNotes))]
    [NotifyPropertyChangedFor(nameof(UnpinnedNotes))]
    private NoteColor? _selectedColor;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDialogOpen;

    [ObservableProperty]
    private Note? _editingNote;

    [ObservableProperty]
    private bool _isNewNote;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _newTag = string.Empty;

    #endregion

    #region Computed Properties

    public List<Note> FilteredNotes
    {
        get
        {
            var result = Notes.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.ToLowerInvariant();
                result = result.Where(n =>
                    n.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    n.Content.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    n.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }

            if (SelectedColor.HasValue)
            {
                result = result.Where(n => n.Color == SelectedColor.Value);
            }

            return result.ToList();
        }
    }

    public List<Note> PinnedNotes => FilteredNotes.Where(n => n.IsPinned).ToList();
    public List<Note> UnpinnedNotes => FilteredNotes.Where(n => !n.IsPinned).ToList();

    public int TotalCount => Notes.Count;
    public int PinnedCount => Notes.Count(n => n.IsPinned);

    public List<string> AllTags => Notes.SelectMany(n => n.Tags).Distinct().OrderBy(t => t).ToList();

    #endregion

    #region Commands

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            Notes = await _repository.GetAllAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load notes: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenNewDialog()
    {
        EditingNote = new Note();
        IsNewNote = true;
        NewTag = string.Empty;
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditDialog(Note note)
    {
        // Clone the note for editing
        EditingNote = new Note
        {
            Id = note.Id,
            Title = note.Title,
            Content = note.Content,
            Color = note.Color,
            IsPinned = note.IsPinned,
            Tags = new List<string>(note.Tags),
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt
        };
        IsNewNote = false;
        NewTag = string.Empty;
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDialog()
    {
        IsDialogOpen = false;
        EditingNote = null;
        NewTag = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (EditingNote == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            if (IsNewNote)
            {
                await _repository.CreateAsync(EditingNote);
            }
            else
            {
                await _repository.UpdateAsync(EditingNote);
            }

            await LoadAsync();
            CloseDialog();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save note: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(Note note)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            await _repository.DeleteAsync(note.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete note: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task TogglePinAsync(Note note)
    {
        try
        {
            note.IsPinned = !note.IsPinned;
            await _repository.UpdateAsync(note);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to update note: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddTag()
    {
        if (EditingNote == null || string.IsNullOrWhiteSpace(NewTag)) return;
        
        var tag = NewTag.Trim();
        if (!EditingNote.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            EditingNote.Tags.Add(tag);
            OnPropertyChanged(nameof(EditingNote));
        }
        NewTag = string.Empty;
    }

    [RelayCommand]
    private void RemoveTag(string tag)
    {
        if (EditingNote == null) return;
        EditingNote.Tags.Remove(tag);
        OnPropertyChanged(nameof(EditingNote));
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedColor = null;
    }

    #endregion
}
