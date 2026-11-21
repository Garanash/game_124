using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Realmforge.Server.Core.Auth;

public sealed class TokenOptions
{
    public const string SectionName = "Auth";
    public const int MinSigningKeyBytes = 32;

    public string Issuer { get; set; } = "realmforge";
    public string SigningKey { get; set; } = string.Empty;
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(12);
    /// <summary>Zone tickets are short-lived: they only need to survive the hop from gateway to zone server.</summary>
    public TimeSpan ZoneTicketLifetime { get; set; } = TimeSpan.FromMinutes(2);
}

public sealed record ZoneTicket(Guid AccountId, Guid CharacterId, string ZoneId, Guid? InstanceId);

/// <summary>Issues account session tokens (for the HTTP API) and zone tickets (for the game connection).</summary>
public sealed class TokenService
{
    public const string SessionAudience = "api";
    public const string ZoneAudience = "zone";
    public const string CharacterClaim = "chr";
    public const string ZoneClaim = "zone";
    public const string InstanceClaim = "inst";

    private readonly TokenOptions _options;
    private readonly TimeProvider _time;
    private readonly SymmetricSecurityKey _key;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public TokenService(TokenOptions options, TimeProvider time)
    {
        if (Encoding.UTF8.GetByteCount(options.SigningKey) < TokenOptions.MinSigningKeyBytes)
        {
            throw new ArgumentException($"Auth:SigningKey must be at least {TokenOptions.MinSigningKeyBytes} bytes.");
        }
        _options = options;
        _time = time;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
    }

    public SymmetricSecurityKey SigningKey => _key;

    public string IssueSession(Guid accountId)
    {
        return Issue(SessionAudience, _options.SessionLifetime, new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()));
    }

    public string IssueZoneTicket(ZoneTicket ticket)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, ticket.AccountId.ToString()),
            new(CharacterClaim, ticket.CharacterId.ToString()),
            new(ZoneClaim, ticket.ZoneId),
        };
        if (ticket.InstanceId.HasValue)
        {
            claims.Add(new Claim(InstanceClaim, ticket.InstanceId.Value.ToString()));
        }
        return Issue(ZoneAudience, _options.ZoneTicketLifetime, claims.ToArray());
    }

    public ZoneTicket? ValidateZoneTicket(string token)
    {
        ClaimsPrincipal? principal = Validate(token, ZoneAudience);
        if (principal == null
            || !Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out Guid accountId)
            || !Guid.TryParse(principal.FindFirst(CharacterClaim)?.Value, out Guid characterId))
        {
            return null;
        }
        Guid? instanceId = Guid.TryParse(principal.FindFirst(InstanceClaim)?.Value, out Guid parsed) ? parsed : null;
        return new ZoneTicket(accountId, characterId, principal.FindFirst(ZoneClaim)?.Value ?? string.Empty, instanceId);
    }

    public TokenValidationParameters ValidationParameters(string audience)
    {
        return new TokenValidationParameters
        {
            ValidIssuer = _options.Issuer,
            ValidAudience = audience,
            IssuerSigningKey = _key,
            ClockSkew = TimeSpan.FromSeconds(30),
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                DateTime now = _time.GetUtcNow().UtcDateTime;
                return (notBefore == null || notBefore <= now.AddSeconds(30)) && expires > now;
            },
        };
    }

    private string Issue(string audience, TimeSpan lifetime, params Claim[] claims)
    {
        DateTime now = _time.GetUtcNow().UtcDateTime;
        var token = new JwtSecurityToken(
            _options.Issuer,
            audience,
            claims,
            notBefore: now,
            expires: now + lifetime,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return _handler.WriteToken(token);
    }

    private ClaimsPrincipal? Validate(string token, string audience)
    {
        try
        {
            return _handler.ValidateToken(token, ValidationParameters(audience), out _);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }
}
