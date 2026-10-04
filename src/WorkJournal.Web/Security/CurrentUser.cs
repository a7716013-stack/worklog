using System.Security.Claims;
namespace WorkJournal.Web.Security;

public interface ICurrentUser { string Id { get; } }
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Id => accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        ? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("請先登入。")
        : throw new UnauthorizedAccessException("請先登入。");
}
