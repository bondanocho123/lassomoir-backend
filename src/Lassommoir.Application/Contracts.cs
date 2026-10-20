using System.Text.Json;
using Lassommoir.Domain;

namespace Lassommoir.Application;

// Bentuk request/response /api/v1.

// --- Pairing ---

public record PairingRequest(string Name);

/// <summary>Token mentah hanya ada di respons ini; server menyimpan hash-nya saja.</summary>
public record IssuedTokenResponse(Guid Id, string Name, string Token);

// --- Snapshot desktop ---
// Field mengikuti session.json (FileManager::saveTasks / taskToJson), jadi isi session.json satu project
// bisa dikirim apa adanya sebagai satu elemen "projects". Field yang tidak dikenal (schemaVersion,
// workingDirectory, tuning, branch, ...) diabaikan. RunState tidak ada di session.json; bridge menambahkannya.

public record SnapshotRequest(IReadOnlyList<SnapshotProject> Projects);

public record SnapshotProject(string ProjectId, string? Title, IReadOnlyList<SnapshotTask>? Tasks);

public record SnapshotTask(
    string Id,
    string Stage,
    string? Category,
    string? Title,
    string? Subtext,
    IReadOnlyList<string>? Attachments,
    string? State,
    string? RunState,
    IReadOnlyList<SnapshotRun>? Runs);

public record SnapshotRun(
    string Stage,
    DateTime FinishedAt,
    bool Success,
    string? Outcome,
    string? Message,
    string? SessionId,
    long DurationMs,
    long TotalTokens,
    double CostUsd,
    IReadOnlyList<string>? DeniedTools,
    string? Decision,
    string? ReviewNote);

/// <summary>Pemetaan id lokal desktop ke id backend, untuk dipakai di /tasks/{id}/events.</summary>
public record SnapshotResponse(IReadOnlyList<SnapshotProjectResult> Projects);

public record SnapshotProjectResult(string LocalId, Guid Id, IReadOnlyList<SnapshotTaskResult> Tasks);

public record SnapshotTaskResult(string LocalId, Guid Id);

public record TaskEventRequest(string Type, JsonElement? Payload);

// --- Command ---

/// <summary>
/// Perintah dari mobile. Field yang dipakai tergantung Type: Approve/RequestRevision (note),
/// SendBack (toStage, note), Move (toStage), CreateTask (title, category, subtext).
/// </summary>
public record CommandRequest(
    CommandType? Type,
    string? Note = null,
    string? ToStage = null,
    string? Title = null,
    string? Category = null,
    string? Subtext = null);

public record CommandPayload(
    string? Note = null,
    string? ToStage = null,
    string? Title = null,
    string? Category = null,
    string? Subtext = null);

/// <summary>Hasil eksekusi dari desktop: Completed, atau Rejected beserta alasan dari TaskManager.</summary>
public record CommandResultRequest(CommandState? State, string? Result, string? Reason);

public record CommandResponse(
    Guid Id,
    Guid ProjectId,
    Guid? TaskId,
    CommandType Type,
    CommandPayload? Payload,
    CommandState State,
    string? Result,
    string? Reason,
    DateTime CreatedAt,
    DateTime? CompletedAt)
{
    public static CommandResponse From(Command command) => new(
        command.Id,
        command.ProjectId,
        command.TaskId,
        command.Type,
        CommandRules.DeserializePayload(command.Payload),
        command.State,
        command.Result,
        command.Reason,
        command.CreatedAt,
        command.CompletedAt);
}

/// <summary>Command untuk desktop, lengkap dengan id lokal project/task-nya.</summary>
public record DeliveredCommand(
    Guid Id,
    CommandType Type,
    Guid ProjectId,
    string ProjectLocalId,
    Guid? TaskId,
    string? TaskLocalId,
    CommandPayload? Payload,
    DateTime CreatedAt)
{
    public static DeliveredCommand From(Command command, string projectLocalId, string? taskLocalId) => new(
        command.Id,
        command.Type,
        command.ProjectId,
        projectLocalId,
        command.TaskId,
        taskLocalId,
        CommandRules.DeserializePayload(command.Payload),
        command.CreatedAt);
}

// --- Board (mobile) ---

public record ProjectResponse(Guid Id, string LocalId, string Title);

public record TaskResponse(
    Guid Id,
    Guid ProjectId,
    string LocalId,
    string Stage,
    string Category,
    string Title,
    string Subtext,
    IReadOnlyList<string> Attachments,
    TaskState State,
    RunState RunState,
    DateTime UpdatedAt)
{
    public static TaskResponse From(TaskItem task) => new(
        task.Id,
        task.ProjectId,
        task.LocalId,
        task.Stage,
        task.Category,
        task.Title,
        task.Subtext,
        task.Attachments,
        task.State,
        task.RunState,
        task.UpdatedAt);
}

public record StageRunResponse(
    int Sequence,
    string Stage,
    DateTime FinishedAt,
    bool Success,
    string Outcome,
    string Message,
    string SessionId,
    long DurationMs,
    long TotalTokens,
    double CostUsd,
    IReadOnlyList<string> DeniedTools,
    ReviewDecision? Decision,
    string ReviewNote)
{
    public static StageRunResponse From(StageRun run) => new(
        run.Sequence,
        run.Stage,
        run.FinishedAt,
        run.Success,
        run.Outcome,
        run.Message,
        run.SessionId,
        run.DurationMs,
        run.TotalTokens,
        run.CostUsd,
        run.DeniedTools,
        run.Decision,
        run.ReviewNote);
}

/// <summary>Detail task: riwayat run (kronologis) dan command terbaru (terbaru dulu).</summary>
public record TaskDetailResponse(
    TaskResponse Task,
    IReadOnlyList<StageRunResponse> Runs,
    IReadOnlyList<CommandResponse> Commands);
