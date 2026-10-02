using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WorkJournal.Web.Models;
using WorkJournal.Web.Security;
namespace WorkJournal.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users,
    IConfiguration configuration) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null, bool failed = false)
    {
        ViewData["ReturnUrl"] = SafeReturn(returnUrl);
        ViewData["Configured"] = GoogleAuthSettings.IsConfigured(configuration);
        ViewData["Failed"] = failed;
        return View();
    }
    [AllowAnonymous, HttpPost]
    public IActionResult GoogleLogin(string? returnUrl = null)
    {
        if (!GoogleAuthSettings.IsConfigured(configuration)) return RedirectToAction(nameof(Login));
        if (!IsCanonicalOrigin()) return Redirect(GoogleAuthSettings.Origin(configuration) + "/Account/Login");
        var callback = Url.Action(nameof(GoogleCallback), "Account", new { returnUrl = SafeReturn(returnUrl) })!;
        return Challenge(signIn.ConfigureExternalAuthenticationProperties(GoogleAuthSettings.LoginScheme, callback), GoogleAuthSettings.LoginScheme);
    }
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = null)
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info is null || info.LoginProvider != GoogleAuthSettings.LoginScheme) return Failed();
        try
        {
            var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user is null)
            {
                var email = info.Principal.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrWhiteSpace(email) || !string.Equals(info.Principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase)) return Failed();
                // Do not auto-link by email: only the immutable provider subject proves account ownership.
                if (await users.FindByEmailAsync(email) is not null) return Failed();
                user = new ApplicationUser { UserName = "google_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(info.ProviderKey))), Email = email, EmailConfirmed = true,
                    DisplayName = Limit(info.Principal.FindFirstValue(ClaimTypes.Name), 200), PictureUrl = Picture(info.Principal.FindFirstValue("picture")) };
                if (!(await users.CreateAsync(user)).Succeeded) return Failed();
                if (!(await users.AddLoginAsync(user, info)).Succeeded)
                { await users.DeleteAsync(user); return Failed(); }
            }
            var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);
            if (!result.Succeeded) return Failed();
            return LocalRedirect(SafeReturn(returnUrl));
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException) { return Failed(); }
        finally { await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme); }
    }
    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        // Invalidate outstanding Calendar consent flows as well as the current cookie.
        var user = await users.GetUserAsync(User);
        if (user is not null) await users.UpdateSecurityStampAsync(user);
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }
    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() => StatusCode(403, "您沒有權限存取此頁面。");
    private string SafeReturn(string? value) => Url.IsLocalUrl(value) ? value! : "/WorkLogs";
    private IActionResult Failed() => RedirectToAction(nameof(Login), new { failed = true });
    private bool IsCanonicalOrigin() => string.Equals($"{Request.Scheme}://{Request.Host}", GoogleAuthSettings.Origin(configuration), StringComparison.OrdinalIgnoreCase) && !Request.PathBase.HasValue;
    private static string? Limit(string? value, int max) => value?[..Math.Min(value.Length, max)];
    private static string? Picture(string? value) => value is { Length: <= 2048 } && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? value : null;
}
