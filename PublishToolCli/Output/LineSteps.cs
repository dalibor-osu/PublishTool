using System.Diagnostics;
using PublishTool.Progress;

namespace PublishToolCli.Output;

public sealed class LineSteps(CliOutput output) : IStepList
{
    private readonly List<LineStep> _steps = [];

    public IStep Add(string name)
    {
        var step = new LineStep(name, output);
        _steps.Add(step);
        return step;
    }

    public List<CliStep> ToResult() =>
        [.. _steps.Select(s => new CliStep(s.Name, s.State.ToString().ToLowerInvariant(), s.Detail))];
}

public sealed class LineStep(string name, CliOutput output) : IStep
{
    private const int TailLength = 15;

    private readonly Lock _lock = new();
    private readonly Queue<string> _tail = new();
    private readonly List<string> _errors = [];
    private readonly Stopwatch _stopwatch = new();

    public string Name { get; set; } = name;

    public StepState State { get; private set; } = StepState.Pending;

    public string? Detail { get; private set; }

    public bool IsFinished => State is not (StepState.Pending or StepState.Running);

    public void Start()
    {
        State = StepState.Running;
        _stopwatch.Start();
        output.Line($"[start] {Name}");
    }

    public void AppendLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (output.Verbose)
        {
            output.Line($"        {trimmed}");
            return;
        }

        lock (_lock)
        {
            _tail.Enqueue(trimmed);
            while (_tail.Count > TailLength)
            {
                _tail.Dequeue();
            }

            if (trimmed.Contains(": error ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("error", StringComparison.OrdinalIgnoreCase))
            {
                _errors.Add(trimmed);
            }
        }
    }

    public void Finish(StepState state, string? detail = null)
    {
        _stopwatch.Stop();
        State = state;
        Detail = detail;
        string elapsed = _stopwatch.Elapsed > TimeSpan.Zero ? $" ({_stopwatch.Elapsed.TotalSeconds:0.0}s)" : string.Empty;
        output.Line($"[{Label(state)}] {Name}{(detail is null ? string.Empty : $": {detail}")}{elapsed}");
        if (state == StepState.Failed && !output.Verbose)
        {
            WriteFailureOutput();
        }
    }

    private void WriteFailureOutput()
    {
        string[] lines;
        lock (_lock)
        {
            lines = _errors.Count > 0 ? [.. _errors.TakeLast(TailLength)] : [.. _tail];
        }

        foreach (string line in lines)
        {
            output.Line($"        {line}");
        }
    }

    private static string Label(StepState state) => state switch
    {
        StepState.Succeeded => "done",
        StepState.Failed => "failed",
        StepState.Skipped => "skipped",
        StepState.Cancelled => "cancelled",
        _ => state.ToString().ToLowerInvariant()
    };
}
