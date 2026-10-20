using System.Security.Claims;
using Lassommoir.Api.Auth;
using Lassommoir.Application;
using Lassommoir.Domain;
using Lassommoir.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lassommoir.Api.Endpoints;

/// <summary>
/// Dipanggil PWA mobile: membaca cermin board device yang dipasangkan dan mengirim command.
/// Command hanya masuk antrean; desktop yang menjalankan atau menolaknya.
/// </summary>
public static class MobileEndpoints
{
    private const int RecentCommandCount = 20;

    public static RouteGroupBuilder MapMobileEndpoints(this RouteGroupBuilder api)
    {
        var mobile = api.MapGroup("").RequireAuthorization(AuthRoles.Mobile).WithTags("Mobile");

        mobile.MapGet("/projects", GetProjects)
            .WithSummary("Daftar project di desktop yang dipasangkan");

        mobile.MapGet("/projects/{id:guid}/tasks", GetProjectTasks)
            .WithSummary("Task satu project (board)");

        mobile.MapPost("/projects/{id:guid}/commands", PostProjectCommand)
            .WithSummary("Kirim command CreateTask ke desktop");

        mobile.MapGet("/tasks/{id:guid}", GetTask)
            .WithSummary("Detail task beserta riwayat run dan command terbaru");

        mobile.MapPost("/tasks/{id:guid}/commands", PostTaskCommand)
            .WithSummary("Kirim command untuk satu task ke desktop");

        mobile.MapGet("/commands/{id:guid}", GetCommand)
            .WithSummary("Status command, termasuk alasan bila ditolak desktop");

        return api;
    }

    private static async Task<IResult> GetProjects(ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        var projects = await db.ProjectsOf(user.GetDeviceId())
            .OrderBy(p => p.Title)
            .Select(p => new ProjectResponse(p.Id, p.LocalId, p.Title))
            .ToListAsync(ct);

        return Results.Ok(projects);
    }

    private static async Task<IResult> GetProjectTasks(Guid id, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        if (!await db.ProjectsOf(user.GetDeviceId()).AnyAsync(p => p.Id == id, ct))
        {
            return Results.NotFound();
        }

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.ProjectId == id)
            .OrderBy(t => t.LocalId)
            .ToListAsync(ct);

        return Results.Ok(tasks.Select(TaskResponse.From).ToList());
    }

    private static async Task<IResult> GetTask(Guid id, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        var task = await db.TasksOf(user.GetDeviceId()).AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
        {
            return Results.NotFound();
        }

        var runs = await db.StageRuns.AsNoTracking()
            .Where(r => r.TaskId == id)
            .OrderBy(r => r.Sequence)
            .ToListAsync(ct);

        var commands = await db.Commands.AsNoTracking()
            .Where(c => c.TaskId == id)
            .OrderByDescending(c => c.CreatedAt)
            .Take(RecentCommandCount)
            .ToListAsync(ct);

        return Results.Ok(new TaskDetailResponse(
            TaskResponse.From(task),
            runs.Select(StageRunResponse.From).ToList(),
            commands.Select(CommandResponse.From).ToList()));
    }

    private static async Task<IResult> PostTaskCommand(Guid id, CommandRequest request, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        var errors = CommandRules.Validate(request, forProject: false);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var deviceId = user.GetDeviceId();
        var task = await db.TasksOf(deviceId).AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
        {
            return Results.NotFound();
        }

        return await EnqueueAsync(db, request, deviceId, task.ProjectId, task.Id, ct);
    }

    private static async Task<IResult> PostProjectCommand(Guid id, CommandRequest request, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        var errors = CommandRules.Validate(request, forProject: true);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var deviceId = user.GetDeviceId();
        if (!await db.ProjectsOf(deviceId).AnyAsync(p => p.Id == id, ct))
        {
            return Results.NotFound();
        }

        return await EnqueueAsync(db, request, deviceId, id, taskId: null, ct);
    }

    private static async Task<IResult> GetCommand(Guid id, ClaimsPrincipal user, LassommoirDbContext db, CancellationToken ct)
    {
        var deviceId = user.GetDeviceId();
        var command = await db.Commands.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.DeviceId == deviceId, ct);

        return command is null ? Results.NotFound() : Results.Ok(CommandResponse.From(command));
    }

    private static async Task<IResult> EnqueueAsync(
        LassommoirDbContext db, CommandRequest request, Guid deviceId, Guid projectId, Guid? taskId, CancellationToken ct)
    {
        var pending = await db.Commands.CountAsync(c => c.DeviceId == deviceId && c.State == CommandState.Pending, ct);
        if (pending >= InputLimits.MaxPendingCommandsPerDevice)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Antrean command penuh.",
                detail: "Desktop belum mengambil command sebelumnya.");
        }

        var command = new Command
        {
            DeviceId = deviceId,
            ProjectId = projectId,
            TaskId = taskId,
            Type = request.Type!.Value,
            Payload = CommandRules.SerializePayload(request),
        };
        db.Commands.Add(command);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/commands/{command.Id}", CommandResponse.From(command));
    }
}
