using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthApi.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthApi.Services;

public class JwtOptions
{
  public string Issuer { get; set; } = string.Empty;
  public string Audience { get; set; } = string.Empty;
  public string Key { get; set; } = string.Empty;
}

public class JwtTokenService
{
  private readonly JwtOptions _options;

  public JwtTokenService(IOptions<JwtOptions> options)
  {
    _options = options.Value;
  }

  public string CreateToken(AuthUser user, UserPub userPub)
  {
    // Handle null checks if necessary, though basic usage assumes existing user/pub
    var claims = new List<Claim>
    {
      new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
      new(JwtRegisteredClaimNames.Email, user.Email),
      new("role", userPub.Role.ToString()),
      new("pub_id", userPub.PubId.ToString()),
      new(ClaimTypes.Role, userPub.Role.ToString())
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
      issuer: _options.Issuer,
      audience: _options.Audience,
      claims: claims,
      expires: DateTime.UtcNow.AddHours(8), // reasonable expiry
      signingCredentials: creds
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
  }
}
