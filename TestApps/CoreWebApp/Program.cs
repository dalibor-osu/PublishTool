using System.Reflection;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () =>
{
    string version = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "version.txt")).Trim();
    string build = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "AppVersion")?.Value ?? "?";
    string[] files = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot"))
        ? Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "wwwroot")).Select(Path.GetFileName).OfType<string>().ToArray()
        : [];
    return Results.Text($"CoreWebApp version.txt={version} build={build} wwwroot=[{string.Join(", ", files)}]");
});

app.Run();
