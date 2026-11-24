using System.Runtime.Versioning;
using System.Security.Principal;

namespace PublishTool.Helpers;

[SupportedOSPlatform("windows")]
public static class WindowsAccount
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
