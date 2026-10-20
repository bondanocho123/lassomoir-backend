using System.Security.Claims;
using Lassommoir.Api.Auth;
using Lassommoir.Application;
using Lassommoir.Domain;
using Lassommoir.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lassommoir.Api.Endpoints;

/// <summary>
/// Dipanggil bridge desktop: mendorong cermin state (snapshot) dan mengambil command lewat polling,
/// karena PC di balik NAT tidak bisa dihubungi server.
/// </summary>
public static class DesktopEndpoints
{
    public static RouteGroupBuilder MapDesktopEndpoints(this RouteGroupBuilder api)
    {
        var desktop = api.MapGroup("").RequireAuthorization(AuthRoles.Desktop).WithTags("Desktop");

        desktop.MapPut("/devices/me/snapshot", PutSnapshot)
            .WithSummary("Upsert project, task, dan run dari session.json desktop");

        desktop.MapGet("/devices/me/commands", GetCommands)
            .WithSummary("Ambil command; state=Pending (bawaan) sekaligus menandainya Delivered");

        desktop.MapPost("/commands/{id:guid}/result", PostCommandResult)
            .WithSummary("Laporkan hasil command: Completed, atau Rejected beserta alasan");

        desktop.MapPost("/tasks/{id:guid}/events", PostTaskEvent)
            .WithSummary("Tambahkan event audit task (append-only)");

        return api;
    }

    private static async Task<IResult> PutSnapshot(SnapshotRequest request, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        var errors = SnapshotValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var deviceId = user.GetDeviceId();
        var now = DateTime.UtcNow;
        var projects = await db.ProjectsOf(deviceId).ToDictionaryAsync(p => p.LocalId, ct);
        var results = new List<SnapshotProjectResult>();

        foreach (var source in request.Projects)
        {
            var tasks = new Dictionary<string, TaskItem>();
            var runs = Array.Empty<StageRun>().ToLookup(r => r.TaskId);

            if (projects.TryGetValue(source.ProjectId, out var project))
            {
                var projectId = project.Id;
                tasks = await db.Tasks.Where(t => t.ProjectId == projectId).ToDictionaryAsync(t => t.LocalId, ct);
                runs = (await db.StageRuns
                        .Where(r => db.Tasks.Any(t => t.Id == r.TaskId && t.ProjectId == projectId))
                        .ToListAsync(ct))
                    .ToLookup(r => r.TaskId);
            }
            else
            {
                project = new Project { DeviceId = deviceId, LocalId = source.ProjectId };
                db.Projects.Add(project);
            }

            project.Title = string.IsNullOrWhiteSpace(source.Title) ? source.ProjectId : source.Title;

            var taskResults = new List<SnapshotTaskResult>();
            foreach (var sourceTask in source.Tasks ?? [])
            {
                if (!tasks.TryGetValue(sourceTask.Id, out var task))
                {
                    task = new TaskItem { ProjectId = project.Id, LocalId = sourceTask.Id, CreatedAt = now };
                    db.Tasks.Add(task);
                }

                task.Stage = sourceTask.Stage;
                task.Category = sourceTask.Category ?? "";
                task.Title = sourceTask.Title ?? "";
                task.Subtext = sourceTask.Subtext ?? "";
                task.Attachments = sourceTask.Attachments?.ToList() ?? [];
                task.State = DesktopKeys.ParseTaskState(sourceTask.State);
                task.RunState = DesktopKeys.ParseRunState(sourceTask.RunState);
                task.UpdatedAt = now;

                SyncRuns(db, task.Id, runs[task.Id].OrderBy(r => r.Sequence).ToList(), sourceTask.Runs ?? []);
                taskResults.Add(new SnapshotTaskResult(task.LocalId, task.Id));
            }

            results.Add(new SnapshotProjectResult(project.LocalId, project.Id, taskResults));
        }

        await TouchDeviceAsync(db, deviceId, now, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new SnapshotResponse(results));
    }

    // Run desktop tidak punya id: dicocokkan menurut urutan. Run yang tidak ada lagi di desktop dibuang.
    private static void SyncRuns(LassommoirDbContext db, Guid taskId, List<StageRun> existing, IReadOnlyList<SnapshotRun> source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var run = i < existing.Count ? existing[i] : new StageRun { TaskId = taskId, Sequence = i };
            if (i >= existing.Count)
            {
                db.StageRuns.Add(run);
            }

            var sourceRun = source[i];
            run.Stage = sourceRun.Stage;
            run.FinishedAt = sourceRun.FinishedAt;
            run.Success = sourceRun.Success;
            run.Outcome = sourceRun.Outcome ?? "";
            run.Message = sourceRun.Message ?? "";
            run.SessionId = sourceRun.SessionId ?? "";
            run.DurationMs = sourceRun.DurationMs;
            run.TotalTokens = sourceRun.TotalTokens;
            run.CostUsd = sourceRun.CostUsd;
            run.DeniedTools = [.. sourceRun.DeniedTools ?? []];
            run.Decision = DesktopKeys.ParseDecision(sourceRun.Decision);
            run.ReviewNote = sourceRun.ReviewNote ?? "";
        }

        db.StageRuns.RemoveRange(existing.Skip(source.Count));
    }

    private static async Task<IResult> GetCommands(ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct, CommandState state = CommandState.Pending)
    {
        var deviceId = user.GetDeviceId();
        var rows = await (
                from command in db.Commands
                where command.DeviceId == deviceId && command.State == state
                join project in db.Projects on command.ProjectId equals project.Id
                join task in db.Tasks on command.TaskId equals (Guid?)task.Id into tasks
                from task in tasks.DefaultIfEmpty()
                orderby command.CreatedAt
                select new { command, project.LocalId, TaskLocalId = task == null ? null : task.LocalId })
            .ToListAsync(ct);

        // Hanya Pending yang berpindah ke Delivered, dan klaimnya atomik: bila dua polling berbarengan,
        // hanya satu yang berhasil mengubah baris sehingga command tidak terkirim ganda.
        var delivered = new List<DeliveredCommand>();
        foreach (var row in rows)
        {
            if (state == CommandState.Pending)
            {
                var id = row.command.Id;
                var claimed = await db.Commands
                    .Where(c => c.Id == id && c.State == CommandState.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.State, CommandState.Delivered), ct);
                if (claimed == 0)
                {
                    continue;
                }
            }

            delivered.Add(DeliveredCommand.From(row.command, row.LocalId, row.TaskLocalId));
        }

        await TouchDeviceAsync(db, deviceId, DateTime.UtcNow, ct);
        await db.SaveChangesAsync(ct);

        return Results.Ok(delivered);
    }

    private static async Task<IResult> PostCommandResult(Guid id, CommandResultRequest request, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        var errors = CommandRules.ValidateResult(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var deviceId = user.GetDeviceId();
        var command = await db.Commands.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.DeviceId == deviceId, ct);
        if (command is null)
        {
            return Results.NotFound();
        }

        // Update bersyarat: dua laporan berbarengan tidak bisa saling menimpa.
        var newState = request.State!.Value;
        var updated = CommandRules.IsFinished(command.State)
            ? 0
            : await db.Commands
                .Where(c => c.Id == id && c.State != CommandState.Completed && c.State != CommandState.Rejected && c.State != CommandState.Failed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.State, newState)
                    .SetProperty(c => c.Result, request.Result)
                    .SetProperty(c => c.Reason, request.Reason)
                    .SetProperty(c => c.CompletedAt, DateTime.UtcNow), ct);

        if (updated == 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Command sudah selesai.",
                detail: $"State command saat ini {command.State}.");
        }

        return Results.NoContent();
    }

    private static async Task<IResult> PostTaskEvent(Guid id, TaskEventRequest request, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > InputLimits.Name)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["type"] = [$"Type wajib diisi, maksimal {InputLimits.Name} karakter."] });
        }

        var payload = request.Payload?.GetRawText();
        if (InputLimits.TooLong(payload, InputLimits.LongText))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["payload"] = [$"Maksimal {InputLimits.LongText} karakter."] });
        }

        if (!await db.TasksOf(user.GetDeviceId()).AnyAsync(t => t.Id == id, ct))
        {
            return Results.NotFound();
        }

        db.TaskEvents.Add(new TaskEvent { TaskId = id, Type = request.Type, Payload = payload });
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    // Snapshot dan polling sekaligus menjadi heartbeat desktop.
    private static async Task TouchDeviceAsync(LassommoirDbContext db, Guid deviceId, DateTime now, CancellationToken ct)
    {
        var device = await db.Devices.SingleAsync(d => d.Id == deviceId, ct);
        device.LastSeenAt = now;
    }
}
