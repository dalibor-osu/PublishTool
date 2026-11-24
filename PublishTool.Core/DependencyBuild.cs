using System.Text;

namespace PublishTool;

public static class DependencyBuild
{
    private const string RuntimeProperties = "RuntimeIdentifier=win-x64;SelfContained=false";

    public static string WriteTraversalProject(IReadOnlyList<DotnetProject> projects, string configuration)
    {
        string dir = Path.Combine(Path.GetTempPath(), "PublishTool");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"publish-{Guid.NewGuid():N}.proj");

        var sb = new StringBuilder();
        sb.AppendLine("<Project DefaultTargets=\"Build\">");
        sb.AppendLine("  <ItemGroup>");
        foreach (var project in projects)
        {
            string include = EscapeMsBuild(project.AbsolutePath);
            string additional = project.Kind == ProjectKind.Sdk ? RuntimeProperties : string.Empty;
            string restore = project.Kind == ProjectKind.Sdk ? "true" : "false";
            sb.AppendLine($"    <PublishProject Include=\"{include}\" AdditionalProperties=\"{additional}\" NeedsRestore=\"{restore}\" />");
        }
        sb.AppendLine("  </ItemGroup>");

        sb.AppendLine("  <Target Name=\"Restore\">");
        sb.AppendLine($"    <MSBuild Projects=\"@(PublishProject)\" Condition=\"'%(PublishProject.NeedsRestore)' == 'true'\" Targets=\"Restore\" Properties=\"Configuration={configuration};RestoreForce=true\" BuildInParallel=\"false\" />");
        sb.AppendLine("  </Target>");
        sb.AppendLine("  <Target Name=\"Build\">");
        sb.AppendLine($"    <MSBuild Projects=\"@(PublishProject)\" Targets=\"Build\" Properties=\"Configuration={configuration}\" BuildInParallel=\"true\" />");
        sb.AppendLine("  </Target>");
        sb.AppendLine("</Project>");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    public static string BuildCommand(string traversalProjectPath) =>
        $"\"{traversalProjectPath}\" /restore /t:Build /m /nr:false /v:minimal /nologo";

    public static void TryDelete(string traversalProjectPath)
    {
        try
        {
            File.Delete(traversalProjectPath);
        }
        catch
        {
            // A leftover temp file is harmless
        }
    }

    private static string EscapeMsBuild(string value) => value
        .Replace("%", "%25")
        .Replace("$", "%24")
        .Replace("@", "%40")
        .Replace("'", "%27")
        .Replace(";", "%3B")
        .Replace("?", "%3F")
        .Replace("*", "%2A")
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
