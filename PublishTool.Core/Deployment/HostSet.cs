namespace PublishTool.Deployment;

public sealed record HostSet(bool Iis, IReadOnlyList<string> Services)
{
    public bool IsEmpty => !Iis && Services.Count == 0;

    public static HostSet Needed(IEnumerable<JournalTarget> targets)
    {
        var list = targets.ToList();
        return new HostSet(
            list.Exists(t => string.IsNullOrWhiteSpace(t.ServiceName)),
            [.. list.Select(t => t.ServiceName).OfType<string>().Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    public HostSet Running(IHostController hosts) => new(Iis && hosts.IsIisRunning(), [.. Services.Where(hosts.IsServiceRunning)]);

    public HostSet Union(HostSet other) => new(Iis || other.Iis, [.. Services.Union(other.Services, StringComparer.OrdinalIgnoreCase)]);

    public string Describe()
    {
        var parts = new List<string>();
        if (Iis)
        {
            parts.Add("IIS");
        }

        parts.AddRange(Services.Select(s => $"service {s}"));
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    public async Task StopAsync(IHostController hosts, Action<string> report, CancellationToken ct)
    {
        foreach (string service in Services)
        {
            await hosts.StopServiceAsync(service, report, ct);
        }

        if (Iis)
        {
            await hosts.StopIisAsync(report, ct);
        }
    }

    public async Task StartAsync(IHostController hosts, Action<string> report, CancellationToken ct)
    {
        var errors = new List<string>();
        if (Iis)
        {
            await TryStart(errors, "IIS", () => hosts.StartIisAsync(report, ct));
        }

        foreach (string service in Services)
        {
            await TryStart(errors, $"service {service}", () => hosts.StartServiceAsync(service, report, ct));
        }

        if (errors.Count > 0)
        {
            throw new DeployException(string.Join("; ", errors));
        }
    }

    private static async Task TryStart(List<string> errors, string what, Func<Task> start)
    {
        try
        {
            await start();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Logger.LogError($"Starting {what} failed:\n{e}");
            errors.Add($"starting {what} failed: {e.Message}");
        }
    }
}
