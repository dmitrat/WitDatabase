using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OutWit.Database.Samples.IdentityServer.Models;
using OutWit.Database.Samples.IdentityServer.Services;

namespace OutWit.Database.Samples.IdentityServer.Controllers;

/// <summary>
/// Controller for handling user authentication.
/// </summary>
public class AccountController : Controller
{
    #region Fields

    private readonly SignInManager<ApplicationUser> m_signInManager;
    private readonly UserManager<ApplicationUser> m_userManager;
    private readonly UserService m_userService;
    private readonly ILogger<AccountController> m_logger;

    #endregion

    #region Constructors

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        UserService userService,
        ILogger<AccountController> logger)
    {
        m_signInManager = signInManager;
        m_userManager = userManager;
        m_userService = userService;
        m_logger = logger;
    }

    #endregion

    #region Login

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["ReturnUrl"] = model.ReturnUrl;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await m_userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated.");
            return View(model);
        }

        var result = await m_signInManager.PasswordSignInAsync(
            model.Email, 
            model.Password, 
            model.RememberMe, 
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await m_userService.UpdateLastLoginAsync(user);
            m_logger.LogInformation("User {Email} logged in", model.Email);
            
            return LocalRedirect(model.ReturnUrl ?? "/");
        }

        if (result.IsLockedOut)
        {
            m_logger.LogWarning("User {Email} account locked out", model.Email);
            ModelState.AddModelError(string.Empty, "Account locked out. Please try again later.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View(model);
    }

    #endregion

    #region Logout

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await m_signInManager.SignOutAsync();
        m_logger.LogInformation("User logged out");
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> LogoutConfirm()
    {
        await m_signInManager.SignOutAsync();
        m_logger.LogInformation("User logged out");
        return RedirectToAction("Index", "Home");
    }

    #endregion

    #region Access Denied

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

    #endregion
}
