using System.Security.Claims;
using Lassommoir.Api.Auth;
using Lassommoir.Application;
using Lassommoir.Domain;
using Lassommoir.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lassommoir.Api.Endpoints;

/// <summary>
/// Pairing: admin (Auth:AdminToken) mendaftarkan desktop, lalu desktop memasangkan mobile-nya sendiri.
/// Token mentah hanya ada di respons 201; yang disimpan hanya hash-nya.
/// </summary>
public static class PairingEndpoints
{
    public static RouteGroupBuilder MapPairingEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/devices").RequireAuthorization(AuthRoles.Admin).WithTags("Pairing");

        admin.MapPost("/", PairDevice)
            .WithSummary("Daftarkan desktop dan terbitkan token device");

        admin.MapDelete("/{id:guid}", RevokeDevice)
            .WithSummary("Cabut token desktop beserta semua mobile-nya");

        var desktop = api.MapGroup("/devices/me/mobile-clients").RequireAuthorization(AuthRoles.Desktop).WithTags("Pairing");

        desktop.MapPost("/", PairMobileClient)
            .WithSummary("Pasangkan mobile dengan desktop ini dan terbitkan token mobile");

        desktop.MapDelete("/{id:guid}", RevokeMobileClient)
            .WithSummary("Cabut token mobile");

        return api;
    }

    private static async Task<IResult> PairDevice(PairingRequest request, LassommoirDbContext db, CancellationToken ct)
    {
        var errors = ValidateName(request);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        var token = Tokens.Generate();
        var device = new Device { Name = request.Name.Trim(), TokenHash = Tokens.Hash(token), PairedAt = DateTime.UtcNow };
        db.Devices.Add(device);
        await db.SaveChangesAsync(ct);

        return Results.Json(new IssuedTokenResponse(device.Id, device.Name, token), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> RevokeDevice(Guid id, LassommoirDbContext db, CancellationToken ct)
    {
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
        {
            return Results.NotFound();
        }

        device.RevokedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> PairMobileClient(PairingRequest request, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        var errors = ValidateName(request);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        var token = Tokens.Generate();
        var client = new MobileClient { DeviceId = user.GetDeviceId(), Name = request.Name.Trim(), TokenHash = Tokens.Hash(token) };
        db.MobileClients.Add(client);
        await db.SaveChangesAsync(ct);

        return Results.Json(new IssuedTokenResponse(client.Id, client.Name, token), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> RevokeMobileClient(Guid id, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        var deviceId = user.GetDeviceId();
        var client = await db.MobileClients.SingleOrDefaultAsync(c => c.Id == id && c.DeviceId == deviceId, ct);
        if (client is null)
        {
            return Results.NotFound();
        }

        client.RevokedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]>? ValidateName(PairingRequest request) =>
        string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > InputLimits.Name
            ? new Dictionary<string, string[]> { ["name"] = [$"Name wajib diisi, maksimal {InputLimits.Name} karakter."] }
            : null;
}
