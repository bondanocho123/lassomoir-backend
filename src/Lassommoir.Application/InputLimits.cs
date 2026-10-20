namespace Lassommoir.Application;

/// <summary>Batas panjang/jumlah input; SQLite tidak menegakkan MaxLength, jadi dicek di sini.</summary>
public static class InputLimits
{
    public const int Name = 200;
    public const int ShortText = 500;
    public const int LongText = 100_000;
    public const int MaxProjects = 100;
    public const int MaxTasksPerProject = 1_000;
    public const int MaxRunsPerTask = 200;
    public const int MaxListItems = 100;
    public const int MaxPendingCommandsPerDevice = 100;

    public static bool TooLong(string? value, int max) => value is not null && value.Length > max;

    public static void Check(Dictionary<string, string[]> errors, string key, string? value, int max)
    {
        if (TooLong(value, max))
        {
            errors[key] = [$"Maksimal {max} karakter."];
        }
    }

    public static void CheckList(Dictionary<string, string[]> errors, string key, IReadOnlyList<string>? values, int maxItems, int maxLength)
    {
        if (values is null)
        {
            return;
        }

        if (values.Count > maxItems)
        {
            errors[key] = [$"Maksimal {maxItems} item."];
        }
        else if (values.Any(v => v is null || v.Length > maxLength))
        {
            errors[key] = [$"Setiap item wajib terisi dan maksimal {maxLength} karakter."];
        }
    }
}
