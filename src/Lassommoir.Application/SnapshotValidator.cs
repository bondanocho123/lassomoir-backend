using Lassommoir.Domain;

namespace Lassommoir.Application;

/// <summary>Seluruh snapshot divalidasi dulu; bila ada satu kesalahan, tidak ada yang disimpan.</summary>
public static class SnapshotValidator
{
    private static readonly string StageError = $"Stage wajib salah satu: {string.Join(", ", Stages.All)}.";

    public static Dictionary<string, string[]> Validate(SnapshotRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Projects is null)
        {
            errors["projects"] = ["Projects wajib diisi."];
            return errors;
        }

        if (request.Projects.Count > InputLimits.MaxProjects)
        {
            errors["projects"] = [$"Maksimal {InputLimits.MaxProjects} project."];
            return errors;
        }

        var projectIds = new HashSet<string>(StringComparer.Ordinal);
        for (var p = 0; p < request.Projects.Count; p++)
        {
            var project = request.Projects[p];
            var projectKey = $"projects[{p}]";
            if (project is null)
            {
                errors[projectKey] = ["Project tidak boleh null."];
                continue;
            }

            if (string.IsNullOrWhiteSpace(project.ProjectId))
            {
                errors[$"{projectKey}.projectId"] = ["ProjectId wajib diisi."];
            }
            else if (!projectIds.Add(project.ProjectId))
            {
                errors[$"{projectKey}.projectId"] = [$"ProjectId '{project.ProjectId}' muncul lebih dari sekali."];
            }

            InputLimits.Check(errors, $"{projectKey}.projectId", project.ProjectId, InputLimits.ShortText);
            InputLimits.Check(errors, $"{projectKey}.title", project.Title, InputLimits.ShortText);

            var tasks = project.Tasks ?? [];
            if (tasks.Count > InputLimits.MaxTasksPerProject)
            {
                errors[$"{projectKey}.tasks"] = [$"Maksimal {InputLimits.MaxTasksPerProject} task per project."];
                continue;
            }

            ValidateTasks(tasks, projectKey, errors);
        }

        return errors;
    }

    private static void ValidateTasks(IReadOnlyList<SnapshotTask> tasks, string projectKey, Dictionary<string, string[]> errors)
    {
        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        for (var t = 0; t < tasks.Count; t++)
        {
            var task = tasks[t];
            var taskKey = $"{projectKey}.tasks[{t}]";
            if (task is null)
            {
                errors[taskKey] = ["Task tidak boleh null."];
                continue;
            }

            if (string.IsNullOrWhiteSpace(task.Id))
            {
                errors[$"{taskKey}.id"] = ["Id wajib diisi."];
            }
            else if (!taskIds.Add(task.Id))
            {
                errors[$"{taskKey}.id"] = [$"Id task '{task.Id}' muncul lebih dari sekali."];
            }

            if (!Stages.IsValid(task.Stage))
            {
                errors[$"{taskKey}.stage"] = [StageError];
            }

            if (!DesktopKeys.TryParseTaskState(task.State, out _))
            {
                errors[$"{taskKey}.state"] = ["State wajib idle, review, atau failed."];
            }

            if (!DesktopKeys.TryParseRunState(task.RunState, out _))
            {
                errors[$"{taskKey}.runState"] = ["RunState wajib idle, queued, atau running."];
            }

            InputLimits.Check(errors, $"{taskKey}.id", task.Id, InputLimits.ShortText);
            InputLimits.Check(errors, $"{taskKey}.category", task.Category, InputLimits.ShortText);
            InputLimits.Check(errors, $"{taskKey}.title", task.Title, InputLimits.ShortText);
            InputLimits.Check(errors, $"{taskKey}.subtext", task.Subtext, InputLimits.LongText);
            InputLimits.CheckList(errors, $"{taskKey}.attachments", task.Attachments, InputLimits.MaxListItems, InputLimits.ShortText);

            var runs = task.Runs ?? [];
            if (runs.Count > InputLimits.MaxRunsPerTask)
            {
                errors[$"{taskKey}.runs"] = [$"Maksimal {InputLimits.MaxRunsPerTask} run per task."];
                continue;
            }

            for (var r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                var runKey = $"{taskKey}.runs[{r}]";
                if (run is null)
                {
                    errors[runKey] = ["Run tidak boleh null."];
                    continue;
                }

                if (!Stages.IsValid(run.Stage))
                {
                    errors[$"{runKey}.stage"] = [StageError];
                }

                if (!DesktopKeys.TryParseDecision(run.Decision, out _))
                {
                    errors[$"{runKey}.decision"] = ["Decision wajib kosong, approved, revise, atau sent_back."];
                }

                InputLimits.Check(errors, $"{runKey}.outcome", run.Outcome, InputLimits.ShortText);
                InputLimits.Check(errors, $"{runKey}.sessionId", run.SessionId, InputLimits.ShortText);
                InputLimits.Check(errors, $"{runKey}.message", run.Message, InputLimits.LongText);
                InputLimits.Check(errors, $"{runKey}.reviewNote", run.ReviewNote, InputLimits.LongText);
                InputLimits.CheckList(errors, $"{runKey}.deniedTools", run.DeniedTools, InputLimits.MaxListItems, InputLimits.ShortText);
            }
        }
    }
}
