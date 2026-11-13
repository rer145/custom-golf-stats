using System.Security.Claims;

namespace CustomGolfStats.Api.Infrastructure;

public static class CurrentUser
{
    /// <summary>The signed-in account's id (the NameIdentifier claim). Throws when the principal isn't signed in.</summary>
    public static Guid GetAccountId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("The request has no signed-in account.");
    }
}
