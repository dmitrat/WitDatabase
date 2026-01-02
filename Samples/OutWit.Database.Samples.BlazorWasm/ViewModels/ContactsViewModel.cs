using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutWit.Database.Samples.BlazorWasm.Data;
using OutWit.Database.Samples.BlazorWasm.Models;

namespace OutWit.Database.Samples.BlazorWasm.ViewModels;

/// <summary>
/// ViewModel for the Contacts page.
/// </summary>
public partial class ContactsViewModel : ObservableObject
{
    private readonly ContactRepository _repository;

    public ContactsViewModel(ContactRepository repository)
    {
        _repository = repository;
    }

    #region Observable Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    private List<Contact> _contacts = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    private ContactCategory? _selectedCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredContacts))]
    private bool _showFavoritesOnly;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDialogOpen;

    [ObservableProperty]
    private Contact? _editingContact;

    [ObservableProperty]
    private bool _isNewContact;

    [ObservableProperty]
    private string? _errorMessage;

    #endregion

    #region Computed Properties

    public List<Contact> FilteredContacts
    {
        get
        {
            var result = Contacts.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.ToLowerInvariant();
                result = result.Where(c =>
                    c.FirstName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    c.LastName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    c.Email.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    c.Company.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            if (SelectedCategory.HasValue)
            {
                result = result.Where(c => c.Category == SelectedCategory.Value);
            }

            if (ShowFavoritesOnly)
            {
                result = result.Where(c => c.IsFavorite);
            }

            return result.ToList();
        }
    }

    public int TotalCount => Contacts.Count;
    public int FavoritesCount => Contacts.Count(c => c.IsFavorite);

    #endregion

    #region Commands

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            Contacts = await _repository.GetAllAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load contacts: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenNewDialog()
    {
        EditingContact = new Contact();
        IsNewContact = true;
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditDialog(Contact contact)
    {
        // Clone the contact for editing
        EditingContact = new Contact
        {
            Id = contact.Id,
            FirstName = contact.FirstName,
            LastName = contact.LastName,
            Email = contact.Email,
            Phone = contact.Phone,
            Company = contact.Company,
            Category = contact.Category,
            IsFavorite = contact.IsFavorite,
            CreatedAt = contact.CreatedAt,
            UpdatedAt = contact.UpdatedAt
        };
        IsNewContact = false;
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDialog()
    {
        IsDialogOpen = false;
        EditingContact = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (EditingContact == null) return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            if (IsNewContact)
            {
                await _repository.CreateAsync(EditingContact);
            }
            else
            {
                await _repository.UpdateAsync(EditingContact);
            }

            await LoadAsync();
            CloseDialog();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save contact: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(Contact contact)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            await _repository.DeleteAsync(contact.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete contact: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(Contact contact)
    {
        try
        {
            contact.IsFavorite = !contact.IsFavorite;
            await _repository.UpdateAsync(contact);
            OnPropertyChanged(nameof(FilteredContacts));
            OnPropertyChanged(nameof(FavoritesCount));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to update contact: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedCategory = null;
        ShowFavoritesOnly = false;
    }

    #endregion
}
