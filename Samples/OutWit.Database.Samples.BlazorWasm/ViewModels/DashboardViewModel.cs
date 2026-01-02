using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutWit.Database.Samples.BlazorWasm.Data;

namespace OutWit.Database.Samples.BlazorWasm.ViewModels;

/// <summary>
/// ViewModel for the Dashboard/Home page.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly ContactRepository _contactRepository;
    private readonly NoteRepository _noteRepository;
    private readonly DatabaseService _databaseService;

    public DashboardViewModel(
        ContactRepository contactRepository, 
        NoteRepository noteRepository,
        DatabaseService databaseService)
    {
        _contactRepository = contactRepository;
        _noteRepository = noteRepository;
        _databaseService = databaseService;
    }

    #region Observable Properties

    [ObservableProperty]
    private int _contactCount;

    [ObservableProperty]
    private int _favoriteContactCount;

    [ObservableProperty]
    private int _noteCount;

    [ObservableProperty]
    private int _pinnedNoteCount;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDatabaseInitialized;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _statusMessage = "Initializing...";

    #endregion

    #region Commands

    [RelayCommand]
    private async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            StatusMessage = "Connecting to IndexedDB...";

            await _databaseService.InitializeAsync();
            IsDatabaseInitialized = true;
            StatusMessage = "Database ready!";

            await LoadStatisticsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to initialize database: {ex.Message}";
            StatusMessage = "Initialization failed";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadStatisticsAsync()
    {
        if (!IsDatabaseInitialized) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var contacts = await _contactRepository.GetAllAsync();
            ContactCount = contacts.Count;
            FavoriteContactCount = contacts.Count(c => c.IsFavorite);

            var notes = await _noteRepository.GetAllAsync();
            NoteCount = notes.Count;
            PinnedNoteCount = notes.Count(n => n.IsPinned);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load statistics: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SeedSampleDataAsync()
    {
        if (!IsDatabaseInitialized) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            StatusMessage = "Creating sample data...";

            // Create sample contacts
            var sampleContacts = new[]
            {
                new Models.Contact { FirstName = "John", LastName = "Doe", Email = "john.doe@example.com", Phone = "+1 555-0101", Company = "Acme Inc", Category = Models.ContactCategory.Work },
                new Models.Contact { FirstName = "Jane", LastName = "Smith", Email = "jane.smith@example.com", Phone = "+1 555-0102", Company = "TechCorp", Category = Models.ContactCategory.Work, IsFavorite = true },
                new Models.Contact { FirstName = "Bob", LastName = "Johnson", Email = "bob.j@personal.com", Phone = "+1 555-0103", Category = Models.ContactCategory.Friend, IsFavorite = true },
                new Models.Contact { FirstName = "Alice", LastName = "Williams", Email = "alice.w@family.com", Phone = "+1 555-0104", Category = Models.ContactCategory.Family },
                new Models.Contact { FirstName = "Charlie", LastName = "Brown", Email = "charlie@example.com", Phone = "+1 555-0105", Category = Models.ContactCategory.Personal }
            };

            foreach (var contact in sampleContacts)
            {
                await _contactRepository.CreateAsync(contact);
            }

            // Create sample notes
            var sampleNotes = new[]
            {
                new Models.Note { Title = "Welcome to WitDatabase!", Content = "This is a sample note demonstrating WitDatabase with IndexedDB storage in Blazor WebAssembly.", Color = Models.NoteColor.Blue, IsPinned = true, Tags = ["welcome", "demo"] },
                new Models.Note { Title = "Shopping List", Content = "- Milk\n- Eggs\n- Bread\n- Cheese\n- Butter", Color = Models.NoteColor.Yellow, Tags = ["shopping", "todo"] },
                new Models.Note { Title = "Meeting Notes", Content = "Discussed Q4 goals:\n1. Improve performance\n2. Add new features\n3. Better documentation", Color = Models.NoteColor.Green, Tags = ["work", "meeting"] },
                new Models.Note { Title = "Ideas", Content = "Future features to consider:\n- Dark mode support\n- Export/Import\n- Cloud sync\n- Tags autocomplete", Color = Models.NoteColor.Purple, Tags = ["ideas", "future"] },
                new Models.Note { Title = "Important Reminder", Content = "Don't forget to backup your data regularly!", Color = Models.NoteColor.Red, IsPinned = true, Tags = ["important"] }
            };

            foreach (var note in sampleNotes)
            {
                await _noteRepository.CreateAsync(note);
            }

            StatusMessage = "Sample data created!";
            await LoadStatisticsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to seed sample data: {ex.Message}";
            StatusMessage = "Seeding failed";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ClearAllDataAsync()
    {
        if (!IsDatabaseInitialized) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;
            StatusMessage = "Clearing all data...";

            await _contactRepository.ClearAllAsync();
            await _noteRepository.ClearAllAsync();

            StatusMessage = "All data cleared!";
            await LoadStatisticsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to clear data: {ex.Message}";
            StatusMessage = "Clear failed";
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion
}
