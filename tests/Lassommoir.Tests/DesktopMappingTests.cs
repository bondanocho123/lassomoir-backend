using Lassommoir.Application;
using Lassommoir.Domain;

namespace Lassommoir.Tests;

public class DesktopMappingTests
{
    [Theory]
    [InlineData("idle", TaskState.Idle)]
    [InlineData("review", TaskState.AwaitingReview)]
    [InlineData("failed", TaskState.Failed)]
    [InlineData("", TaskState.Idle)]
    [InlineData(null, TaskState.Idle)]
    public void TaskStateKey_MapsLikeDesktop(string? key, TaskState expected)
    {
        Assert.Equal(expected, DesktopKeys.ParseTaskState(key));
    }

    [Theory]
    [InlineData("idle", RunState.Idle)]
    [InlineData("queued", RunState.Queued)]
    [InlineData("running", RunState.Running)]
    [InlineData(null, RunState.Idle)]
    public void RunStateKey_MapsLikeDesktop(string? key, RunState expected)
    {
        Assert.Equal(expected, DesktopKeys.ParseRunState(key));
    }

    [Theory]
    [InlineData("approved", ReviewDecision.Approved)]
    [InlineData("revise", ReviewDecision.Revise)]
    [InlineData("sent_back", ReviewDecision.SentBack)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DecisionKey_MapsLikeDesktop(string? key, ReviewDecision? expected)
    {
        Assert.Equal(expected, DesktopKeys.ParseDecision(key));
    }

    // DONE adalah stage, bukan state; desktop tidak punya keputusan "rejected".
    [Fact]
    public void UnknownKeys_AreRejected()
    {
        Assert.False(DesktopKeys.TryParseTaskState("done", out _));
        Assert.False(DesktopKeys.TryParseTaskState("Review", out _));
        Assert.False(DesktopKeys.TryParseRunState("done", out _));
        Assert.False(DesktopKeys.TryParseDecision("rejected", out _));
        Assert.Throws<FormatException>(() => DesktopKeys.ParseTaskState("pending"));
    }

    [Fact]
    public void Stages_FollowDesktopPipeline()
    {
        Assert.Equal(new[] { "WAITING", "SPECIFIER", "CODER", "CLEANER", "ARCHITECT", "HARDENER", "QA", "DONE" }, Stages.All);
        Assert.True(Stages.IsValid("QA"));
        Assert.False(Stages.IsValid("qa"));
        Assert.False(Stages.IsValid(null));
    }

    [Fact]
    public void Snapshot_WithUnknownStageStateOrDecision_IsInvalid()
    {
        var request = new SnapshotRequest([
            new SnapshotProject("spacewar", null, [
                new SnapshotTask("1", "REVIEW", null, null, null, null, "done", "idle", [
                    new SnapshotRun("CODER", DateTime.UtcNow, true, null, null, null, 0, 0, 0, null, "rejected", null),
                ]),
                new SnapshotTask("1", "CODER", null, null, null, null, "idle", null, null),
            ]),
        ]);

        var errors = SnapshotValidator.Validate(request);

        Assert.Contains("projects[0].tasks[0].stage", errors.Keys);
        Assert.Contains("projects[0].tasks[0].state", errors.Keys);
        Assert.Contains("projects[0].tasks[0].runs[0].decision", errors.Keys);
        Assert.Contains("projects[0].tasks[1].id", errors.Keys);
    }

    [Theory]
    [InlineData(CommandType.SendBack, null, false, "toStage")]
    [InlineData(CommandType.Move, "BELUM", false, "toStage")]
    [InlineData(CommandType.CreateTask, null, false, "type")]
    [InlineData(CommandType.Approve, null, true, "type")]
    [InlineData(CommandType.CreateTask, null, true, "title")]
    public void InvalidCommand_IsRejectedBeforeQueueing(CommandType type, string? toStage, bool forProject, string field)
    {
        var errors = CommandRules.Validate(new CommandRequest(type, ToStage: toStage), forProject);

        Assert.Contains(field, errors.Keys);
    }

    [Fact]
    public void SendBack_KeepsTargetStageInPayload()
    {
        var request = new CommandRequest(CommandType.SendBack, Note: "kurang tes", ToStage: Stages.Coder);

        Assert.Empty(CommandRules.Validate(request, forProject: false));
        var payload = CommandRules.DeserializePayload(CommandRules.SerializePayload(request));
        Assert.Equal(new CommandPayload(Note: "kurang tes", ToStage: Stages.Coder), payload);
    }

    [Theory]
    [InlineData(CommandState.Rejected, null, "reason")]
    [InlineData(CommandState.Failed, "x", "state")]
    [InlineData(CommandState.Delivered, null, "state")]
    [InlineData(null, null, "state")]
    public void InvalidCommandResult_IsRejected(CommandState? state, string? reason, string field)
    {
        var errors = CommandRules.ValidateResult(new CommandResultRequest(state, null, reason));

        Assert.Contains(field, errors.Keys);
    }
}
