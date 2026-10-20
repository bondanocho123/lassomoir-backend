using System.Security.Claims;
using System.Text.Encodings.Web;
using Lassommoir.Application;
using Lassommoir.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lassommoir.Api.Auth;

/// <summary>Role sekaligus nama policy otorisasi.</summary>
public static class AuthRoles
{
    /// <summary>Pemegang Auth:AdminToken; hanya untuk pairing/pencabutan device.</summary>
    public const string Admin = "admin";
    public const string Desktop = "desktop";
    public const string Mobile = "mobile";
}

/// <summary>
/// "Authorization: Bearer &lt;token&gt;". Token dicocokkan lewat hash SHA-256-nya ke device (desktop)
/// atau mobile client yang belum dicabut; mobile ikut ditolak bila device-nya dicabut.
/// </summary>
public class TokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LassommoirDbContext db,
    IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Token";
    public const string DeviceIdClaim = "device_id";
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header[BearerPrefix.Length..].Trim();
        if (token.Length == 0)
        {
            return AuthenticateResult.Fail("Token kosong.");
        }

        var adminToken = configuration["Auth:AdminToken"];
        if (!string.IsNullOrEmpty(adminToken) && Tokens.Matches(token, adminToken))
        {
            return Success(AuthRoles.Admin, deviceId: null);
        }

        var hash = Tokens.Hash(token);
        var ct = Context.RequestAborted;

        var device = await db.Devices.AsNoTracking()
            .SingleOrDefaultAsync(d => d.TokenHash == hash && d.RevokedAt == null, ct);
        if (device is not null)
        {
            return Success(AuthRoles.Desktop, device.Id);
        }

        var client = await db.MobileClients.AsNoTracking()
            .SingleOrDefaultAsync(c => c.TokenHash == hash && c.RevokedAt == null
                && db.Devices.Any(d => d.Id == c.DeviceId && d.RevokedAt == null), ct);
        if (client is not null)
        {
            return Success(AuthRoles.Mobile, client.DeviceId);
        }

        return AuthenticateResult.Fail("Token tidak dikenal atau sudah dicabut.");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }

    private static AuthenticateResult Success(string role, Guid? deviceId)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (deviceId is not null)
        {
            claims.Add(new Claim(DeviceIdClaim, deviceId.Value.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>Device milik desktop, atau device yang dipasangkan dengan mobile.</summary>
    public static Guid GetDeviceId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst(TokenAuthenticationHandler.DeviceIdClaim)!.Value);
}
