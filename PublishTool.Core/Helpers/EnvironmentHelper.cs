using System.Runtime.InteropServices;

namespace PublishTool.Helpers;

public static class EnvironmentHelper
{
    public static bool IsDevVersion => BuildInfo.Version == "dev";

    public static string GetCurrentOsShortcut()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "win";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "osx";
        }

        throw new PlatformNotSupportedException("Unsupported operating system");
    }

    public static string GetCurrentArchitecture() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => throw new PlatformNotSupportedException("Unsupported architecture")
        };

    public static string GetPlatformArchiveExtension() => GetCurrentOsShortcut() switch
    {
        "win" => "zip",
        "linux" => "tar.gz",
        "osx" => "tar.gz",
        _ => throw new PlatformNotSupportedException("Unsupported operating system")
    };
}