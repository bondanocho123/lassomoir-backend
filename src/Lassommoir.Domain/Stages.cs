namespace Lassommoir.Domain;

/// <summary>Stage pipeline desktop, urut seperti StageCatalog::standard() di lassomoir.</summary>
public static class Stages
{
    public const string Waiting = "WAITING";
    public const string Specifier = "SPECIFIER";
    public const string Coder = "CODER";
    public const string Cleaner = "CLEANER";
    public const string Architect = "ARCHITECT";
    public const string Hardener = "HARDENER";
    public const string Qa = "QA";
    public const string Done = "DONE";

    public static readonly IReadOnlyList<string> All = [Waiting, Specifier, Coder, Cleaner, Architect, Hardener, Qa, Done];

    public static bool IsValid(string? stage) => stage is not null && All.Contains(stage);
}
