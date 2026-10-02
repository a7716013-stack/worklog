using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using WorkJournal.Web.Models;
namespace WorkJournal.Web.Security;

public class ApplicationClaimsFactory(UserManager<ApplicationUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("display_name", user.DisplayName ?? user.Email ?? "使用者"));
        if (Uri.TryCreate(user.PictureUrl, UriKind.Absolute, out var picture) && picture.Scheme == "https")
            identity.AddClaim(new Claim("picture", picture.AbsoluteUri));
        return identity;
    }
}
