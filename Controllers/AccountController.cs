using System.Security.Claims;
using CampeonatoMagic.Models;
using CampeonatoMagic.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CampeonatoMagic.Controllers;

[Route("cuenta")]
public class AccountController(AdminAccount admin) : Controller
{
    [HttpGet("entrar")]
    public IActionResult Login(string? returnUrl)
    {
        ViewBag.Configured = admin.Read() is not null;
        return View(new LoginForm { ReturnUrl = returnUrl });
    }
    [HttpPost("entrar"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginForm form)
    {
        ViewBag.Configured = admin.Read() is not null;
        if (!ModelState.IsValid || !admin.Verify(form.Username, form.Password))
        {
            ModelState.AddModelError("", "Usuario o contraseña incorrectos.");
            return View(form);
        }
        var credential = admin.Read()!;
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, credential.Username), new Claim("stamp", credential.Stamp)], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return LocalRedirect(Url.IsLocalUrl(form.ReturnUrl) ? form.ReturnUrl! : Url.Action("Index", "Home")!);
    }
    [HttpPost("salir"), Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}
