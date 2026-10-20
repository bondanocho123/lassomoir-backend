using System.Text.Json;
using System.Text.Json.Serialization;
using Lassommoir.Domain;

namespace Lassommoir.Application;

/// <summary>
/// Validasi bentuk command sebelum masuk antrean. Apakah command boleh dijalankan (mis. Approve saat
/// task tidak menunggu review) tetap diputuskan TaskManager desktop, yang melapor balik lewat Rejected.
/// </summary>
public static class CommandRules
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <param name="forProject">true untuk /projects/{id}/commands (hanya CreateTask), false untuk /tasks/{id}/commands.</param>
    public static Dictionary<string, string[]> Validate(CommandRequest request, bool forProject)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Type is not { } type || !Enum.IsDefined(type))
        {
            errors["type"] = [$"Type wajib salah satu: {string.Join(", ", Enum.GetNames<CommandType>())}."];
            return errors;
        }

        if (forProject != (type == CommandType.CreateTask))
        {
            errors["type"] = [forProject
                ? "Hanya CreateTask yang dikirim ke /projects/{id}/commands."
                : "CreateTask dikirim ke /projects/{id}/commands."];
            return errors;
        }

        switch (type)
        {
            case CommandType.SendBack or CommandType.Move when !Stages.IsValid(request.ToStage):
                errors["toStage"] = [$"ToStage wajib salah satu: {string.Join(", ", Stages.All)}."];
                break;
            case CommandType.CreateTask when string.IsNullOrWhiteSpace(request.Title):
                errors["title"] = ["Title wajib diisi."];
                break;
        }

        InputLimits.Check(errors, "note", request.Note, InputLimits.LongText);
        InputLimits.Check(errors, "title", request.Title, InputLimits.ShortText);
        InputLimits.Check(errors, "category", request.Category, InputLimits.ShortText);
        InputLimits.Check(errors, "subtext", request.Subtext, InputLimits.LongText);

        return errors;
    }

    public static Dictionary<string, string[]> ValidateResult(CommandResultRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        InputLimits.Check(errors, "result", request.Result, InputLimits.LongText);
        InputLimits.Check(errors, "reason", request.Reason, InputLimits.ShortText);

        if (request.State is not (CommandState.Completed or CommandState.Rejected))
        {
            errors["state"] = ["State wajib Completed atau Rejected."];
        }
        else if (request.State == CommandState.Rejected && string.IsNullOrWhiteSpace(request.Reason))
        {
            errors["reason"] = ["Reason wajib diisi bila command ditolak."];
        }

        return errors;
    }

    public static bool IsFinished(CommandState state) =>
        state is CommandState.Completed or CommandState.Rejected or CommandState.Failed;

    /// <summary>Hanya field yang relevan untuk Type yang disimpan.</summary>
    public static string? SerializePayload(CommandRequest request)
    {
        CommandPayload? payload = request.Type switch
        {
            CommandType.Approve or CommandType.RequestRevision => new(Note: request.Note),
            CommandType.SendBack => new(Note: request.Note, ToStage: request.ToStage),
            CommandType.Move => new(ToStage: request.ToStage),
            CommandType.CreateTask => new(Title: request.Title, Category: request.Category, Subtext: request.Subtext),
            _ => null,
        };

        return payload is null ? null : JsonSerializer.Serialize(payload, PayloadJson);
    }

    public static CommandPayload? DeserializePayload(string? payload) =>
        payload is null ? null : JsonSerializer.Deserialize<CommandPayload>(payload, PayloadJson);
}
