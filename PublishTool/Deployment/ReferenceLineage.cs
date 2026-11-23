namespace PublishTool.Deployment;

public sealed class ReferenceLineage
{
    public const int CurrentFormat = 1;
    public const string FileName = "publishtool-lineage.json";

    public int Format { get; set; } = CurrentFormat;

    public Dictionary<string, string> Projects { get; set; } = [];
}
