namespace PublishTool.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public bool IsDefault { get; init; }
    public string? Description { get; init; }
}