namespace PublishTool.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionAttribute(params string[] aliases) : Attribute
{
    public string[] Aliases { get; } = aliases;
    public string? Description { get; init; }
    public string? ValueName { get; init; }
    public string? Parser { get; init; }
}