using System.IO;
using System.Linq;
using System.Reflection;
using System.Web;

namespace FrameworkWebApp
{
    public sealed class VersionHandler : IHttpHandler
    {
        public bool IsReusable => true;

        public void ProcessRequest(HttpContext context)
        {
            string root = HttpRuntime.AppDomainAppPath;
            string version = File.ReadAllText(Path.Combine(root, "version.txt")).Trim();
            string build = typeof(VersionHandler).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "AppVersion")?.Value ?? "?";
            string[] files = Directory.Exists(Path.Combine(root, "Content"))
                ? Directory.GetFiles(Path.Combine(root, "Content")).Select(Path.GetFileName).ToArray()
                : new string[0];
            context.Response.ContentType = "text/plain";
            context.Response.Write($"FrameworkWebApp version.txt={version} build={build} Content=[{string.Join(", ", files)}]");
        }
    }
}
