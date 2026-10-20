using Lassommoir.Domain;

namespace Lassommoir.Application;

/// <summary>
/// Kunci string session.json desktop ke enum backend. Kosong/null diperlakukan seperti desktop
/// (file lama tanpa field terbaca Idle / tanpa keputusan); kunci lain ditolak.
/// </summary>
public static class DesktopKeys
{
    // taskStateKey() di TaskItem.h
    private static readonly Dictionary<string, TaskState> TaskStates = new()
    {
        [""] = TaskState.Idle,
        ["idle"] = TaskState.Idle,
        ["review"] = TaskState.AwaitingReview,
        ["failed"] = TaskState.Failed,
    };

    // RunState di AgentTypes.h; tidak ada di session.json, dikirim bridge desktop bersama snapshot
    private static readonly Dictionary<string, RunState> RunStates = new()
    {
        [""] = RunState.Idle,
        ["idle"] = RunState.Idle,
        ["queued"] = RunState.Queued,
        ["running"] = RunState.Running,
    };

    // namespace ReviewDecision di TaskItem.h
    private static readonly Dictionary<string, ReviewDecision?> Decisions = new()
    {
        [""] = null,
        ["approved"] = ReviewDecision.Approved,
        ["revise"] = ReviewDecision.Revise,
        ["sent_back"] = ReviewDecision.SentBack,
    };

    public static bool TryParseTaskState(string? key, out TaskState state) => TaskStates.TryGetValue(key ?? "", out state);

    public static bool TryParseRunState(string? key, out RunState state) => RunStates.TryGetValue(key ?? "", out state);

    public static bool TryParseDecision(string? key, out ReviewDecision? decision) => Decisions.TryGetValue(key ?? "", out decision);

    public static TaskState ParseTaskState(string? key) =>
        TryParseTaskState(key, out var state) ? state : throw Unknown("state", key);

    public static RunState ParseRunState(string? key) =>
        TryParseRunState(key, out var state) ? state : throw Unknown("runState", key);

    public static ReviewDecision? ParseDecision(string? key) =>
        TryParseDecision(key, out var decision) ? decision : throw Unknown("decision", key);

    private static FormatException Unknown(string field, string? key) => new($"{field} desktop tidak dikenal: '{key}'.");
}
