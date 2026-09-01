namespace PublishTool.Helpers;

public static class EnvironmentHelper
{
    public static bool IsDevVersion => BuildInfo.Version == "dev";
}