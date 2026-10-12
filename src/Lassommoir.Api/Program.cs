// Konvensi API:
// - Error: semua respons 4xx/5xx berbentuk RFC 7807 application/problem+json (dengan traceId).
//   Untuk error validasi gunakan Results.ValidationProblem(...) (status 400, field "errors").
// - Versioning: URL path. Semua endpoint bisnis didaftarkan lewat group `api` (/api/v1).
//   Endpoint infrastruktur (/openapi, /health) berada di luar prefix.
// - Dokumentasi kontrak: dokumen OpenAPI di /openapi/v1.json (hanya di Development).
// - CORS: policy "Pwa", origin dibaca dari Cors:AllowedOrigins. Kosong = tidak ada origin diizinkan.
using Lassommoir.Infrastructure;

const string PwaCorsPolicy = "Pwa";

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
builder.Services.AddPersistence(builder.Configuration);

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

var api = app.MapGroup("/api/v1");

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

api.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithSummary("Ramalan cuaca contoh")
.WithTags("Weather");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public partial class Program;
