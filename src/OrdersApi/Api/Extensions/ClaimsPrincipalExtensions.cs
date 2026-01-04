using System.Security.Claims;

namespace OrdersApi.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetPubId(this ClaimsPrincipal user)
    {
        // "pub_id" הוא השם של ה-Claim כפי שהגדרנו ב-AuthApi
        var idClaim = user.FindFirst("pub_id")?.Value;

        if (Guid.TryParse(idClaim, out var pubId))
        {
            return pubId;
        }

        throw new UnauthorizedAccessException("User context is missing a valid pub_id claim.");
    }
}