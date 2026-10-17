namespace Lassommoir.Domain;

public enum TaskState
{
    Pending,
    Running,
    InReview,
    Done,
    Failed,
}

public enum CommandState
{
    Pending,
    Delivered,
    Completed,
    Failed,
}

public enum ReviewVerdict
{
    Approved,
    ChangesRequested,
    Rejected,
}

public class Device
{
    public Guid Id { get; set; } = Ids.NewId();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }
}

public class TaskItem
{
    public Guid Id { get; set; } = Ids.NewId();
    public Guid DeviceId { get; set; }
    public string Title { get; set; } = "";
    public TaskState State { get; set; } = TaskState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
    public Guid TaskId { get; set; }
    public Guid DeviceId { get; set; }
    public string Type { get; set; } = "";
    public string? Payload { get; set; }
    public CommandState State { get; set; } = CommandState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Review
{
    public Guid Id { get; set; } = Ids.NewId();
    public Guid TaskId { get; set; }
    public ReviewVerdict Verdict { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
