namespace PublishTool.Deployment;

public static class ConfigFiles
{
    private static readonly string[] Names = ["web.config", "appsettings.json"];
    private static readonly string[] Suffixes = [".exe.config", ".dll.config"];
    private const string SettingsPrefix = "appsettings.";
    private const string SettingsSuffix = ".json";

    public static bool IsConfigFile(string manifestPath)
    {
        string name = manifestPath[(manifestPath.LastIndexOfAny(['/', '\\']) + 1)..];
        return Names.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase))
            || Suffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase))
            || (name.StartsWith(SettingsPrefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(SettingsSuffix, StringComparison.OrdinalIgnoreCase));
    }
}
