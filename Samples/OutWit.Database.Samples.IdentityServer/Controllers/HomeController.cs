using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OutWit.Database.Samples.IdentityServer.Controllers;

/// <summary>
/// Home controller for the application.
/// </summary>
public class HomeController : Controller
{
    #region Index

    [AllowAnonymous]
    public IActionResult Index()
    {
        return View();
    }

    #endregion

    #region About

    [AllowAnonymous]
    public IActionResult About()
    {
        return View();
    }

    #endregion

    #region Error

    [AllowAnonymous]
    public IActionResult Error()
    {
        return View();
    }

    #endregion
}
