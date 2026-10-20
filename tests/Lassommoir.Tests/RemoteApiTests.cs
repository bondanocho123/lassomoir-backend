using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lassommoir.Application;
using Lassommoir.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Lassommoir.Tests;

/// <summary>Alur remote lewat HTTP: pairing, snapshot desktop, board mobile, command, dan auth.</summary>
public class RemoteApiTests : IDisposable
{
    private const string AdminToken = "test-admin-token";
    private const string SnapshotUrl = "/api/v1/devices/me/snapshot";
    private const string CommandsUrl = "/api/v1/devices/me/commands";

    // Isi session.json desktop (FileManager::saveTasks) apa adanya, ditambah runState dari bridge.
    private const string SessionJson = """
        {
          "projects": [
            {
              "schemaVersion": 1,
              "projectId": "spacewar",
              "workingDirectory": "E:/Projects/spacewar",
              "tasks": [
                {
                  "id": "1791808370495",
                  "projectId": "spacewar",
                  "stage": "CODER",
                  "category": "utility",
                  "title": "Migrasi awal",
                  "subtext": "Terapkan saat startup.",
                  "attachments": ["skema.png"],
                  "state": "review",
                  "runState": "idle",
                  "runs": [
                    {
                      "stage": "SPECIFIER", "finishedAt": "2026-10-16T09:00:00Z", "success": true,
                      "outcome": "success", "message": "# Spesifikasi", "sessionId": "s1",
                      "durationMs": 154000, "totalTokens": 47300, "costUsd": 0.42,
                      "deniedTools": [], "decision": "approved", "reviewNote": "ok"
                    },
                    {
                      "stage": "CODER", "finishedAt": "2026-10-16T10:00:00Z", "success": true,
                      "outcome": "success", "message": "# Hasil coder", "sessionId": "s2",
                      "durationMs": 1000, "totalTokens": 10, "costUsd": 0.01,
                      "deniedTools": ["Bash"], "decision": "", "reviewNote": ""
                    }
                  ],
                  "tuning": { "CODER": { "model": "opus", "effort": "high" } }
                }
              ]
            }
          ]
        }
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lassommoir-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RemoteApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Auth:AdminToken", AdminToken);
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Snapshot_IsReadableFromMobile_WithDesktopKeysMapped()
    {
        var (desktop, mobile) = await PairAsync();

        var snapshot = await ReadAsync<SnapshotResponse>(await SendAsync(HttpMethod.Put, SnapshotUrl, desktop, SessionJson));
        var project = Assert.Single(snapshot.Projects);
        var taskId = Assert.Single(project.Tasks).Id;

        var projects = await ReadAsync<List<ProjectResponse>>(await SendAsync(HttpMethod.Get, "/api/v1/projects", mobile));
        Assert.Equal(new ProjectResponse(project.Id, "spacewar", "spacewar"), Assert.Single(projects));

        var board = await ReadAsync<List<TaskResponse>>(await SendAsync(HttpMethod.Get, $"/api/v1/projects/{project.Id}/tasks", mobile));
        Assert.Equal(taskId, Assert.Single(board).Id);

        var detail = await ReadAsync<TaskDetailResponse>(await SendAsync(HttpMethod.Get, $"/api/v1/tasks/{taskId}", mobile));
        Assert.Equal("1791808370495", detail.Task.LocalId);
        Assert.Equal(Stages.Coder, detail.Task.Stage);
        Assert.Equal("utility", detail.Task.Category);
        Assert.Equal(TaskState.AwaitingReview, detail.Task.State);
        Assert.Equal(RunState.Idle, detail.Task.RunState);
        Assert.Equal("skema.png", Assert.Single(detail.Task.Attachments));
        Assert.Collection(detail.Runs,
            run =>
            {
                Assert.Equal(Stages.Specifier, run.Stage);
                Assert.Equal(ReviewDecision.Approved, run.Decision);
                Assert.Equal("ok", run.ReviewNote);
            },
            run =>
            {
                Assert.Equal("# Hasil coder", run.Message);
                Assert.Null(run.Decision);
                Assert.Equal("Bash", Assert.Single(run.DeniedTools));
            });
    }

    [Fact]
    public async Task Snapshot_Resent_UpdatesSameRows_AndDropsRemovedRuns()
    {
        var (desktop, mobile) = await PairAsync();
        var taskId = await PutSnapshotAsync(desktop, SessionJson);

        var session = JsonNode.Parse(SessionJson)!;
        var task = session["projects"]![0]!["tasks"]![0]!;
        task["title"] = "Judul baru";
        task["state"] = "idle";
        task["runState"] = "running";
        task["runs"]!.AsArray().RemoveAt(1);

        Assert.Equal(taskId, await PutSnapshotAsync(desktop, session.ToJsonString()));

        var detail = await ReadAsync<TaskDetailResponse>(await SendAsync(HttpMethod.Get, $"/api/v1/tasks/{taskId}", mobile));
        Assert.Equal("Judul baru", detail.Task.Title);
        Assert.Equal(TaskState.Idle, detail.Task.State);
        Assert.Equal(RunState.Running, detail.Task.RunState);
        Assert.Equal(Stages.Specifier, Assert.Single(detail.Runs).Stage);
    }

    [Fact]
    public async Task Snapshot_WithUnknownState_Returns400_AndSavesNothing()
    {
        var (desktop, mobile) = await PairAsync();
        var session = JsonNode.Parse(SessionJson)!;
        session["projects"]![0]!["tasks"]![0]!["state"] = "done";

        var response = await SendAsync(HttpMethod.Put, SnapshotUrl, desktop, session.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ReadAsync<List<ProjectResponse>>(await SendAsync(HttpMethod.Get, "/api/v1/projects", mobile)));
    }

    [Fact]
    public async Task CommandRejectedByDesktop_ShowsReasonToMobile()
    {
        var (desktop, mobile) = await PairAsync();
        var taskId = await PutSnapshotAsync(desktop, SessionJson);

        var queued = await ReadAsync<CommandResponse>(
            await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/commands", mobile, new { type = "Approve", note = "Lanjut" }),
            HttpStatusCode.Created);
        Assert.Equal(CommandState.Pending, queued.State);

        var delivered = Assert.Single(await ReadAsync<List<DeliveredCommand>>(await SendAsync(HttpMethod.Get, CommandsUrl, desktop)));
        Assert.Equal(queued.Id, delivered.Id);
        Assert.Equal(CommandType.Approve, delivered.Type);
        Assert.Equal("spacewar", delivered.ProjectLocalId);
        Assert.Equal("1791808370495", delivered.TaskLocalId);
        Assert.Equal("Lanjut", delivered.Payload?.Note);
        Assert.Empty(await ReadAsync<List<DeliveredCommand>>(await SendAsync(HttpMethod.Get, CommandsUrl, desktop)));

        var rejection = new { state = "Rejected", reason = "Task tidak sedang menunggu review" };
        var resultUrl = $"/api/v1/commands/{queued.Id}/result";
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Post, resultUrl, desktop, rejection)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Post, resultUrl, desktop, rejection)).StatusCode);

        var seen = await ReadAsync<CommandResponse>(await SendAsync(HttpMethod.Get, $"/api/v1/commands/{queued.Id}", mobile));
        Assert.Equal(CommandState.Rejected, seen.State);
        Assert.Equal("Task tidak sedang menunggu review", seen.Reason);
        Assert.NotNull(seen.CompletedAt);
    }

    [Fact]
    public async Task InvalidCommand_Returns400_AndIsNotQueued()
    {
        var (desktop, mobile) = await PairAsync();
        var taskId = await PutSnapshotAsync(desktop, SessionJson);

        var response = await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/commands", mobile, new { type = "SendBack", note = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ReadAsync<List<DeliveredCommand>>(await SendAsync(HttpMethod.Get, CommandsUrl, desktop)));
    }

    [Fact]
    public async Task UnknownOrRevokedToken_Returns401()
    {
        var device = await PairDeviceAsync("PC kerja");
        var mobile = await PairMobileAsync(device.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/projects", "bukan-token")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/v1/projects", mobile)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/v1/devices/{device.Id}", AdminToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, CommandsUrl, device.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/v1/projects", mobile)).StatusCode);
    }

    [Fact]
    public async Task Token_OnlyOpensEndpointsOfItsRole()
    {
        var (desktop, mobile) = await PairAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Put, SnapshotUrl, mobile, SessionJson)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/v1/projects", desktop)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, "/api/v1/devices", desktop, new { name = "lain" })).StatusCode);
    }

    [Fact]
    public async Task OtherDevicesTask_IsNotFound()
    {
        var (desktop, _) = await PairAsync();
        var taskId = await PutSnapshotAsync(desktop, SessionJson);
        var (otherDesktop, otherMobile) = await PairAsync("PC lain");

        var payload = new { type = "run_started" };
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/events", desktop, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/events", otherDesktop, payload)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/v1/tasks/{taskId}", otherMobile)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/commands", otherMobile, new { type = "Run" })).StatusCode);
    }

    [Fact]
    public async Task PollingWithFinishedState_DoesNotOverwriteCommandState()
    {
        var (desktop, mobile) = await PairAsync();
        var taskId = await PutSnapshotAsync(desktop, SessionJson);
        var queued = await ReadAsync<CommandResponse>(
            await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/commands", mobile, new { type = "Approve" }),
            HttpStatusCode.Created);
        await ReadAsync<List<DeliveredCommand>>(await SendAsync(HttpMethod.Get, CommandsUrl, desktop));
        var resultUrl = $"/api/v1/commands/{queued.Id}/result";
        Assert.Equal(HttpStatusCode.NoContent,
            (await SendAsync(HttpMethod.Post, resultUrl, desktop, new { state = "Rejected", reason = "tidak boleh" })).StatusCode);

        var rejected = await ReadAsync<List<DeliveredCommand>>(await SendAsync(HttpMethod.Get, $"{CommandsUrl}?state=Rejected", desktop));

        Assert.Single(rejected);
        var seen = await ReadAsync<CommandResponse>(await SendAsync(HttpMethod.Get, $"/api/v1/commands/{queued.Id}", mobile));
        Assert.Equal(CommandState.Rejected, seen.State);
        Assert.Equal(HttpStatusCode.Conflict,
            (await SendAsync(HttpMethod.Post, resultUrl, desktop, new { state = "Completed" })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentPolling_DeliversEachCommandOnce()
    {
        var (desktop, mobile) = await PairAsync();
        var taskId = await PutSnapshotAsync(desktop, SessionJson);
        for (var i = 0; i < 5; i++)
        {
            await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/commands", mobile, new { type = "Run" });
        }

        var polls = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
            await ReadAsync<List<DeliveredCommand>>(await SendAsync(HttpMethod.Get, CommandsUrl, desktop))));

        var ids = polls.SelectMany(p => p.Select(c => c.Id)).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct().Count());
    }

    [Fact]
    public async Task OversizedInput_Returns400()
    {
        var (desktop, mobile) = await PairAsync();
        var taskId = await PutSnapshotAsync(desktop, SessionJson);

        var session = JsonNode.Parse(SessionJson)!;
        session["projects"]![0]!["tasks"]![0]!["title"] = new string('x', InputLimits.ShortText + 1);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Put, SnapshotUrl, desktop, session.ToJsonString())).StatusCode);

        var longNote = new { type = "Approve", note = new string('x', InputLimits.LongText + 1) };
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/commands", mobile, longNote)).StatusCode);

        var longEvent = new { type = new string('x', InputLimits.Name + 1) };
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/events", desktop, longEvent)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(HttpMethod.Post, "/api/v1/devices", AdminToken, new { name = new string('x', InputLimits.Name + 1) })).StatusCode);
    }

    [Fact]
    public async Task PendingQueue_IsCapped()
    {
        var (desktop, mobile) = await PairAsync();
        var taskId = await PutSnapshotAsync(desktop, SessionJson);
        for (var i = 0; i < InputLimits.MaxPendingCommandsPerDevice; i++)
        {
            Assert.Equal(HttpStatusCode.Created,
                (await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/commands", mobile, new { type = "Run" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await SendAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/commands", mobile, new { type = "Run" })).StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = body switch
            {
                null => null,
                string json => new StringContent(json, Encoding.UTF8, "application/json"),
                _ => JsonContent.Create(body),
            },
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<IssuedTokenResponse> PairDeviceAsync(string name) =>
        await ReadAsync<IssuedTokenResponse>(
            await SendAsync(HttpMethod.Post, "/api/v1/devices", AdminToken, new { name }), HttpStatusCode.Created);

    private async Task<string> PairMobileAsync(string desktopToken) =>
        (await ReadAsync<IssuedTokenResponse>(
            await SendAsync(HttpMethod.Post, "/api/v1/devices/me/mobile-clients", desktopToken, new { name = "HP" }), HttpStatusCode.Created)).Token;

    private async Task<(string Desktop, string Mobile)> PairAsync(string name = "PC kerja")
    {
        var device = await PairDeviceAsync(name);
        var mobile = await PairMobileAsync(device.Token);
        return (device.Token, mobile);
    }

    private async Task<Guid> PutSnapshotAsync(string desktopToken, string json)
    {
        var response = await SendAsync(HttpMethod.Put, SnapshotUrl, desktopToken, json);
        var snapshot = await ReadAsync<SnapshotResponse>(response);
        return snapshot.Projects[0].Tasks[0].Id;
    }
}
