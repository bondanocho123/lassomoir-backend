namespace Lassommoir.Domain;

public static class Ids
{
    public static Guid NewId() => Guid.CreateVersion7();
}
