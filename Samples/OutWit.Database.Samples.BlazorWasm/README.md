# WitDatabase Blazor WebAssembly Sample

A modern Blazor WebAssembly application demonstrating WitDatabase with IndexedDB storage, MudBlazor UI, and MVVM architecture.

## Features

- **WitDatabase with IndexedDB** - Full persistence in browser storage
- **MudBlazor UI** - Material Design components
- **MVVM Architecture** - Using CommunityToolkit.Mvvm
- **Contact Management** - CRUD operations with categories and favorites
- **Notes App** - Rich note-taking with colors, tags, and pinning

## Screenshots

### Dashboard
- Database statistics
- Quick actions (seed data, clear)
- Connection status

### Contacts
- Contact cards with avatars
- Category filtering
- Favorites
- Search functionality

### Notes
- Color-coded notes
- Pinned notes section
- Tags support
- Rich text content

## Running the Sample

### Prerequisites

- .NET 9.0 or .NET 10.0 SDK
- Modern browser with IndexedDB support

### Run with .NET CLI

```bash
cd Samples/OutWit.Database.Samples.BlazorWasm
dotnet run
```

Then open https://localhost:5001 in your browser.

### Run from Visual Studio

1. Set `OutWit.Database.Samples.BlazorWasm` as the startup project
2. Press F5 or click "Start"

## Architecture

```
???????????????????????????????????????????????????
?                   UI Layer                      ?
?  (Razor Pages with MudBlazor Components)        ?
???????????????????????????????????????????????????
?               ViewModel Layer                    ?
?  (CommunityToolkit.Mvvm - Observable Objects)   ?
???????????????????????????????????????????????????
?              Repository Layer                    ?
?  (ContactRepository, NoteRepository)            ?
???????????????????????????????????????????????????
?              Service Layer                       ?
?  (DatabaseService - Generic CRUD)               ?
???????????????????????????????????????????????????
?              WitDatabase Core                    ?
?  (Key-Value Store with Async API)               ?
???????????????????????????????????????????????????
?              IndexedDB Storage                   ?
?  (Browser Persistence via JS Interop)           ?
???????????????????????????????????????????????????
```

## Key Code Examples

### Database Initialization

```csharp
// In DatabaseService.cs
public async Task InitializeAsync(CancellationToken cancellationToken = default)
{
    _database = await new WitDatabaseBuilder()
        .WithIndexedDbStorage(DATABASE_NAME, _jsRuntime)
        .WithBTree()
        .WithTransactions()
        .BuildAsync(cancellationToken);
}
```

### Generic CRUD Operations

```csharp
// Save entity
public async Task<T> SaveAsync<T>(string prefix, int id, T entity, CancellationToken ct = default)
{
    var key = Encoding.UTF8.GetBytes($"{prefix}{id:D10}");
    var value = JsonSerializer.SerializeToUtf8Bytes(entity, JsonOptions);
    
    await _database!.PutAsync(key, value, ct);
    await _database.FlushAsync(ct);
    
    return entity;
}

// Get all entities with prefix
public async Task<List<T>> GetAllAsync<T>(string prefix, CancellationToken ct = default)
{
    var results = new List<T>();
    var startKey = Encoding.UTF8.GetBytes(prefix);
    var endKey = Encoding.UTF8.GetBytes(prefix + "\x7F");
    
    await foreach (var (_, value) in _database!.ScanAsync(startKey, endKey, ct))
    {
        var entity = JsonSerializer.Deserialize<T>(value, JsonOptions);
        if (entity != null) results.Add(entity);
    }
    
    return results;
}
```

### ViewModel with MVVM Toolkit

```csharp
public partial class ContactsViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    private List<Contact> _contacts = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    private string _searchText = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        Contacts = await _repository.GetAllAsync();
        IsLoading = false;
    }
}
```

### Blazor Page with ViewModel

```razor
@page "/contacts"
@inject ContactsViewModel ViewModel

<MudText Typo="Typo.h4">Contacts</MudText>

@foreach (var contact in ViewModel.FilteredContacts)
{
    <MudCard>...</MudCard>
}

@code {
    protected override async Task OnInitializedAsync()
    {
        ViewModel.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
```

## Project Structure

```
OutWit.Database.Samples.BlazorWasm/
??? Data/
?   ??? DatabaseService.cs      # Generic DB operations
?   ??? ContactRepository.cs    # Contact-specific operations
?   ??? NoteRepository.cs       # Note-specific operations
??? Models/
?   ??? Contact.cs              # Contact entity
?   ??? Note.cs                 # Note entity with colors
??? ViewModels/
?   ??? DashboardViewModel.cs   # Dashboard logic
?   ??? ContactsViewModel.cs    # Contacts page logic
?   ??? NotesViewModel.cs       # Notes page logic
??? Pages/
?   ??? Index.razor             # Dashboard page
?   ??? Contacts.razor          # Contacts page
?   ??? Notes.razor             # Notes page
?   ??? About.razor             # About page
??? App.razor                   # App root with theme
??? MainLayout.razor            # Layout with navigation
??? Program.cs                  # DI configuration
??? wwwroot/
    ??? index.html              # HTML entry point
```

## Browser Support

| Browser | Minimum Version |
|---------|-----------------|
| Chrome | 80+ |
| Edge | 80+ |
| Firefox | 75+ |
| Safari | 14+ |

## Technologies Used

- **.NET 9.0 / 10.0** - Target frameworks
- **Blazor WebAssembly** - Client-side web UI
- **MudBlazor 8.x** - Material Design components
- **CommunityToolkit.Mvvm** - MVVM pattern support
- **WitDatabase.Core.IndexedDb** - Database with browser storage

## License

MIT License - See LICENSE file for details.
