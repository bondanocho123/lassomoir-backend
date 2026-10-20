// Konvensi API:
// - Error: semua respons 4xx/5xx berbentuk RFC 7807 application/problem+json (dengan traceId).
//   Untuk error validasi gunakan Results.ValidationProblem(...) (status 400, field "errors").
// - Versioning: URL path. Semua endpoint bisnis didaftarkan lewat group `api` (/api/v1).
//   Endpoint infrastruktur (/openapi, /health) berada di luar prefix.
// - Auth: semua endpoint /api/v1 wajib "Authorization: Bearer <token>" (TokenAuthenticationHandler).
//   Token device untuk desktop, token mobile untuk PWA, Auth:AdminToken hanya untuk pairing device.
// - Enum dikirim dan diterima sebagai string (mis. "AwaitingReview").
// - Dokumentasi kontrak: dokumen OpenAPI di /openapi/v1.json (hanya di Development).
// - CORS: policy "Pwa", origin dibaca dari Cors:AllowedOrigins. Kosong = tidak ada origin diizinkan.
using System.Text.Json.Serialization;
using Lassommoir.Api.Auth;
using Lassommoir.Api.Endpoints;
using Lassommoir.Infrastructure;
using Lassommoir.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

const string PwaCorsPolicy = "Pwa";
const string MigrateOnStartupKey = "Database:MigrateOnStartup";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "L'Assommoir API";
        document.Info.Version = "v1";
        document.Info.Description = "Kontrak API backend L'Assommoir.";
        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddPersistence(builder.Configuration);

builder.Services.AddAuthentication(TokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(TokenAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthRoles.Admin, policy => policy.RequireRole(AuthRoles.Admin))
    .AddPolicy(AuthRoles.Desktop, policy => policy.RequireRole(AuthRoles.Desktop))
    .AddPolicy(AuthRoles.Mobile, policy => policy.RequireRole(AuthRoles.Mobile));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var allowCredentials = builder.Configuration.GetValue<bool>("Cors:AllowCredentials");

if (allowCredentials && allowedOrigins.Contains("*"))
{
    throw new InvalidOperationException("Cors:AllowedOrigins tidak boleh berisi '*' bila Cors:AllowCredentials aktif.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(PwaCorsPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();

        if (allowCredentials)
        {
            policy.AllowCredentials();
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(PwaCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api/v1").RequireAuthorization();

api.MapPairingEndpoints();
api.MapDesktopEndpoints();
api.MapMobileEndpoints();

var migrateOnStartup = app.Configuration.GetValue(MigrateOnStartupKey, app.Environment.IsDevelopment());
if (migrateOnStartup)
{
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<LassommoirDbContext>().Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Migrasi database gagal saat startup.");
    }
}

app.Run();

public partial class Program;
