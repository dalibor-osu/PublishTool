using System.Text.RegularExpressions;
using PublishTool.Configuration;

namespace PublishTool;

public static partial class ProjectScanner
{
    public static List<DotnetProject> Scan(DirectorySettings? settings, string path, bool all = false)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        string[] ignoredDirectories = settings?.IgnoredDirectories ?? [];
        string[] publishableProjects = settings?.PublishableProjects ?? [];

        var projects = new List<DotnetProject>();
        foreach (string file in Directory.GetFiles(path, "*.csproj", new EnumerationOptions { RecurseSubdirectories = true }))
        {
            bool isPublishable = all || publishableProjects.Any(pp => file.EndsWith($"{pp}.csproj"));
            bool isIgnored = ignoredDirectories.Any(dir => file.Contains(dir));

            if (!isPublishable || isIgnored)
            {
                continue;
            }

            string name = Path.GetFileNameWithoutExtension(file);
            projects.Add(new DotnetProject { AbsolutePath = file, Name = name, Kind = DetectKind(file) });
        }

        return [.. projects.OrderBy(p => p.Name)];
    }

    private static ProjectKind DetectKind(string projectPath)
    {
        string xml = File.ReadAllText(projectPath);

        if (SdkRegex().IsMatch(xml))
        {
            return ProjectKind.Sdk;
        }

        if (xml.Contains("Microsoft.WebApplication.targets", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectKind.LegacyWeb;
        }

        return ProjectKind.LegacyLibrary;
    }

    [GeneratedRegex(@"<Project\s+[^>]*Sdk\s*=")]
    private static partial Regex SdkRegex();
}