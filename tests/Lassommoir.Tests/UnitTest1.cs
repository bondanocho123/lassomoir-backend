using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lassommoir.Tests;

public class ApiConventionsTests
{
    private const string PwaOrigin = "http://localhost:5173";

    private static WebApplicationFactory<Program> CreateFactory(string environment = "Development") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Cors:AllowedOrigins:0", PwaOrigin);
            builder.ConfigureServices(services =>
                services.AddTransient<IStartupFilter, ThrowingStartupFilter>());
        });

    // Menambah middleware di akhir pipeline: route yang tidak cocok /boom akan melempar exception.
    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((context, nextMiddleware) =>
                context.Request.Path == "/boom"
                    ? throw new InvalidOperationException("rahasia-internal")
                    : nextMiddleware());
        };
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails404()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/tidak-ada");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("title", out _));
        Assert.True(json.RootElement.TryGetProperty("type", out _));
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetails500_WithoutLeakingInProduction()
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/boom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("rahasia-internal", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(json.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task UnhandledException_KeepsCorsHeaderForAllowedOrigin()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Add("Origin", PwaOrigin);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(PwaOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task VersionedEndpoint_Returns200_AndOldPathReturns404()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var ok = await client.GetAsync("/api/v1/weatherforecast");
        var old = await client.GetAsync("/weatherforecast");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.Equal("application/problem+json", old.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Preflight_FromAllowedOrigin_ReturnsAllowOrigin()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/weatherforecast");
        request.Headers.Add("Origin", PwaOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await client.SendAsync(request);

        Assert.Equal(PwaOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.True(response.Headers.Contains("Access-Control-Allow-Methods"));
        Assert.True(response.Headers.Contains("Access-Control-Allow-Headers"));
    }

    [Fact]
    public async Task Request_FromUnknownOrigin_GetsNoAllowOrigin()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/weatherforecast");
        request.Headers.Add("Origin", "http://evil.example");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
