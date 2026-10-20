namespace Lassommoir.Domain;

// Desktop (lassomoir) otoritatif: Project, TaskItem, dan StageRun adalah cermin session.json-nya.
// Mobile hanya mengirim Command, yang dieksekusi (atau ditolak) TaskManager desktop.

/// <summary>Status task di stage-nya (TaskState di TaskItem.h). DONE adalah stage, bukan state.</summary>
public enum TaskState
{
    Idle,
    AwaitingReview,
    Failed,
}

/// <summary>Status run runtime (RunState di AgentTypes.h); tidak disimpan di session.json.</summary>
public enum RunState
{
    Idle,
    Queued,
    Running,
}

/// <summary>Keputusan review atas satu run (ReviewDecision di TaskItem.h).</summary>
public enum ReviewDecision
{
    Approved,
    Revise,
    SentBack,
}

/// <summary>Intent TaskManager desktop yang boleh dikirim dari mobile.</summary>
public enum CommandType
{
    Run,
    Cancel,
    Approve,
    RequestRevision,
    SendBack,
    Move,
    CreateTask,
}

public enum CommandState
{
    Pending,
    Delivered,
    Completed,
    Rejected,
    Failed,
}

public class Device
{
    public Guid Id { get; set; } = Ids.NewId();
    public string Name { get; set; } = "";
    /// <summary>SHA-256 token desktop; token mentah hanya ditampilkan sekali saat pairing.</summary>
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PairedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>Klien mobile yang dipasangkan desktop; hanya melihat dan memerintah device itu.</summary>
public class MobileClient
{
    public Guid Id { get; set; } = Ids.NewId();
    public Guid DeviceId { get; set; }
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
}

public class Project
{
    public Guid Id { get; set; } = Ids.NewId();
    public Guid DeviceId { get; set; }
    /// <summary>projectId desktop (mis. "spacewar"), unik per device.</summary>
    public string LocalId { get; set; } = "";
    public string Title { get; set; } = "";
}

public class TaskItem
{
    public Guid Id { get; set; } = Ids.NewId();
    public Guid ProjectId { get; set; }
    /// <summary>id task desktop (timestamp ms, mis. "1791808370495"), unik per project.</summary>
    public string LocalId { get; set; } = "";
    /// <summary>Salah satu <see cref="Stages.All"/>.</summary>
    public string Stage { get; set; } = Stages.Waiting;
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtext { get; set; } = "";
    /// <summary>Nama file lampiran di desktop; isinya tidak dicerminkan.</summary>
    public List<string> Attachments { get; set; } = [];
    public TaskState State { get; set; } = TaskState.Idle;
    public RunState RunState { get; set; } = RunState.Idle;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Kapan baris ini terakhir diperbarui dari snapshot desktop.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Satu run agent yang sudah selesai beserta keputusan review-nya (StageRun di TaskItem.h).</summary>
public class StageRun
{
    public Guid Id { get; set; } = Ids.NewId();
    public Guid TaskId { get; set; }
    /// <summary>Posisi di riwayat run desktop, mulai 0 (run terlama dulu).</summary>
    public int Sequence { get; set; }
    public string Stage { get; set; } = "";
    public DateTime FinishedAt { get; set; }
    public bool Success { get; set; }
    public string Outcome { get; set; } = "";
    /// <summary>Dokumen/ringkasan dari agent: bahan yang direview pengguna.</summary>
    public string Message { get; set; } = "";
    public string SessionId { get; set; } = "";
    public long DurationMs { get; set; }
    public long TotalTokens { get; set; }
    public double CostUsd { get; set; }
    public List<string> DeniedTools { get; set; } = [];
    public ReviewDecision? Decision { get; set; }
    public string ReviewNote { get; set; } = "";
}

/// <summary>Append-only: UPDATE dan DELETE ditolak trigger database.</summary>
public class TaskEvent
{
    public Guid Id { get; set; } = Ids.NewId();
    public Guid TaskId { get; set; }
    public string Type { get; set; } = "";
    public string? Payload { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Command
{
    public Guid Id { get; set; } = Ids.NewId();
    public Guid DeviceId { get; set; }
    public Guid ProjectId { get; set; }
    /// <summary>Kosong untuk <see cref="CommandType.CreateTask"/>.</summary>
    public Guid? TaskId { get; set; }
    public CommandType Type { get; set; }
    public string? Payload { get; set; }
    public CommandState State { get; set; } = CommandState.Pending;
    public string? Result { get; set; }
    /// <summary>Alasan dari desktop bila command ditolak.</summary>
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
