using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OutWit.Database.Samples.IdentityServer.Models;
using OutWit.Database.Samples.IdentityServer.Services;

namespace OutWit.Database.Samples.IdentityServer.Controllers;

/// <summary>
/// Controller for managing users.
/// </summary>
[Authorize(Roles = "Administrator")]
public class UsersController : Controller
{
    #region Fields

    private readonly UserService m_userService;
    private readonly ILogger<UsersController> m_logger;

    #endregion

    #region Constructors

    public UsersController(
        UserService userService,
        ILogger<UsersController> logger)
    {
        m_userService = userService;
        m_logger = logger;
    }

    #endregion

    #region List

    public async Task<IActionResult> Index()
    {
        var users = await m_userService.GetAllUsersAsync();
        var viewModels = new List<UserViewModel>();

        foreach (var user in users)
        {
            var roles = await m_userService.GetUserRolesAsync(user);
            viewModels.Add(new UserViewModel
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                Roles = roles
            });
        }

        return View(viewModels);
    }

    #endregion

    #region Create

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateRolesAsync();
        return View(new CreateUserViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateRolesAsync();
            return View(model);
        }

        var (success, errors) = await m_userService.CreateUserAsync(
            model.Email,
            model.Password,
            model.FirstName,
            model.LastName,
            model.Role);

        if (!success)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            await PopulateRolesAsync();
            return View(model);
        }

        TempData["Success"] = $"User '{model.Email}' created successfully.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateRolesAsync()
    {
        var roles = await m_userService.GetAllRolesAsync();
        ViewBag.Roles = new SelectList(roles, "Name", "Name");
    }

    #endregion

    #region Delete

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var (success, errors) = await m_userService.DeleteUserAsync(id);
        
        if (!success)
        {
            TempData["Error"] = string.Join(", ", errors);
        }
        else
        {
            TempData["Success"] = "User deleted successfully.";
        }

        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Toggle Status

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var user = await m_userService.GetUserByIdAsync(id);
        if (user == null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = !user.IsActive;
        var (success, errors) = await m_userService.UpdateUserAsync(user);

        if (!success)
        {
            TempData["Error"] = string.Join(", ", errors);
        }
        else
        {
            TempData["Success"] = $"User {(user.IsActive ? "activated" : "deactivated")} successfully.";
        }

        return RedirectToAction(nameof(Index));
    }

    #endregion
}
