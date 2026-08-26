namespace PublishTool;

public enum ProjectKind
{
    Sdk,
    LegacyWeb,
    LegacyLibrary
}

public class DotnetProject
{
    public required string AbsolutePath { get; init; }
    public required string Name { get; init; }
    public required ProjectKind Kind { get; init; }
}