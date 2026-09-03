using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PublishTool.Console;

internal enum JobState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

internal sealed class JobRow(string name, int tailLength)
{
    private readonly Lock _lock = new();
    private readonly Queue<string> _tail = new();
    private readonly Queue<string> _errorTail = new();
    private readonly Stopwatch _stopwatch = new();

    public string Name { get; set; } = name;
    public JobState State { get; private set; } = JobState.Pending;
    public string? Detail { get; private set; }
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public bool IsFinished => State is not (JobState.Pending or JobState.Running);

    public void Start()
    {
        State = JobState.Running;
        _stopwatch.Start();
    }

    public void Finish(JobState state, string? detail = null)
    {
        _stopwatch.Stop();
        lock (_lock)
        {
            if (state == JobState.Failed && _errorTail.Count > 0)
            {
                _tail.Clear();
                foreach (string line in _errorTail)
                {
                    _tail.Enqueue(line);
                }
            }
        }

        Detail = detail;
        State = state;
    }

    public void AppendLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            Push(_tail, trimmed);
            if (trimmed.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("error", StringComparison.OrdinalIgnoreCase))
            {
                Push(_errorTail, trimmed);
            }
        }
    }

    public string[] Tail()
    {
        lock (_lock)
        {
            return [.. _tail];
        }
    }

    private void Push(Queue<string> queue, string line)
    {
        queue.Enqueue(line);
        while (queue.Count > tailLength)
        {
            queue.Dequeue();
        }
    }
}

internal sealed class JobDisplay(int tailLength = 5) : IRenderable
{
    private static readonly Style TailStyle = new(Color.Grey);
    private static readonly Style TimeStyle = new(Color.Grey);
    private static readonly Style PendingStyle = new(Color.Grey);
    private static readonly Style RunningStyle = Style.Plain;
    private static readonly Style SuccessStyle = new(Color.Green);
    private static readonly Style FailureStyle = new(Color.Red);
    private static readonly Style WarningStyle = new(Color.Yellow);

    private readonly List<JobRow> _rows = [];
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly Spinner _spinner = Spinner.Known.Ascii;

    public JobRow Add(string name)
    {
        var row = new JobRow(name, tailLength);
        _rows.Add(row);
        return row;
    }

    public void Stop() => _total.Stop();

    public Measurement Measure(RenderOptions options, int maxWidth) => new(Math.Min(maxWidth, 40), maxWidth);

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        int maxLines = Math.Max(_rows.Count + 1, options.ConsoleSize.Height - 2);
        int tailPerRow = _rows.Count == 0
            ? 0
            : Math.Clamp((maxLines - _rows.Count - 1) / _rows.Count, 0, tailLength);

        var lines = new List<Line>();
        foreach (var row in _rows)
        {
            lines.Add(new Line(Icon(row, options.Unicode), [Header(row)], row.State == JobState.Pending ? null : Format(row.Elapsed)));
            if (ShowsTail(row))
            {
                string[] tail = row.Tail();
                for (int i = 0; i < tailPerRow; i++)
                {
                    int index = tail.Length - tailPerRow + i;
                    string text = index >= 0 ? "  " + tail[index] : string.Empty;
                    lines.Add(new Line(null, [(text, TailStyle)], null));
                }
            }
        }

        lines.Add(new Line(null, Summary(), Format(_total.Elapsed)));

        int width = Math.Max(1, maxWidth - 1);
        int timeWidth = lines.Max(l => l.Time?.Length ?? 0);
        int textWidth = Math.Max(1, width - 2 - (timeWidth > 0 ? timeWidth + 2 : 0));
        string ellipsis = options.Unicode ? "…" : "...";

        var segments = new List<Segment>();
        foreach (var line in lines)
        {
            if (line.Icon is { } icon)
            {
                segments.Add(new Segment(icon.Glyph, icon.Style));
            }
            else
            {
                segments.Add(Segment.Padding(1));
            }

            segments.Add(Segment.Padding(1));

            int used = AppendTruncated(segments, line.Parts, textWidth, ellipsis);
            if (timeWidth > 0)
            {
                segments.Add(Segment.Padding(textWidth - used + 2 + timeWidth - (line.Time?.Length ?? 0)));
                if (line.Time is not null)
                {
                    segments.Add(new Segment(line.Time, TimeStyle));
                }
            }

            segments.Add(Segment.LineBreak);
        }

        return segments;
    }

    private static int AppendTruncated(List<Segment> segments, IReadOnlyList<(string Text, Style Style)> parts, int width, string ellipsis)
    {
        int total = parts.Sum(p => new Segment(p.Text).CellCount());
        if (total <= width)
        {
            segments.AddRange(parts.Select(p => new Segment(p.Text, p.Style)));
            return total;
        }

        int budget = Math.Max(0, width - ellipsis.Length);
        int used = 0;
        foreach (var (text, style) in parts)
        {
            var kept = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                int cell = new Segment(c.ToString()).CellCount();
                if (used + cell > budget)
                {
                    break;
                }

                kept.Append(c);
                used += cell;
            }

            if (kept.Length > 0)
            {
                segments.Add(new Segment(kept.ToString(), style));
            }

            if (used >= budget)
            {
                break;
            }
        }

        segments.Add(new Segment(ellipsis, TailStyle));
        return used + ellipsis.Length;
    }

    private static bool ShowsTail(JobRow row) => row.State is JobState.Running or JobState.Failed;

    private (string Glyph, Style Style) Icon(JobRow row, bool unicode) => row.State switch
    {
        JobState.Pending => (unicode ? "○" : "-", PendingStyle),
        JobState.Running => (SpinnerFrame(), WarningStyle),
        JobState.Succeeded => (unicode ? "✔" : "+", SuccessStyle),
        JobState.Failed => (unicode ? "✘" : "x", FailureStyle),
        _ => (unicode ? "•" : "~", WarningStyle)
    };

    private static (string Text, Style Style) Header(JobRow row)
    {
        string suffix = row.State switch
        {
            JobState.Succeeded => " - done",
            JobState.Failed => " - failed",
            JobState.Skipped => " - skipped",
            JobState.Cancelled => " - cancelled",
            _ => string.Empty
        };
        if (row.Detail is not null)
        {
            suffix += $", {row.Detail}";
        }

        var style = row.State switch
        {
            JobState.Pending => PendingStyle,
            JobState.Running => RunningStyle,
            JobState.Succeeded => SuccessStyle,
            JobState.Failed => FailureStyle,
            _ => WarningStyle
        };

        return (row.Name + suffix, style);
    }

    private List<(string Text, Style Style)> Summary()
    {
        int finished = _rows.Count(r => r.IsFinished);
        int failed = _rows.Count(r => r.State == JobState.Failed);
        int notRun = _rows.Count(r => r.State is JobState.Skipped or JobState.Cancelled);

        var parts = new List<(string, Style)> { ("Total ", new Style(decoration: Decoration.Bold)) };
        if (finished < _rows.Count)
        {
            parts.Add(($"{finished}/{_rows.Count} finished", PendingStyle));
            return parts;
        }

        if (failed == 0 && notRun == 0)
        {
            parts.Add(("all done", SuccessStyle));
            return parts;
        }

        parts.Add(($"{_rows.Count - failed - notRun} done", SuccessStyle));
        if (failed > 0)
        {
            parts.Add((", ", Style.Plain));
            parts.Add(($"{failed} failed", FailureStyle));
        }

        if (notRun > 0)
        {
            parts.Add((", ", Style.Plain));
            parts.Add(($"{notRun} not run", WarningStyle));
        }

        return parts;
    }

    private string SpinnerFrame()
    {
        long frame = (long)(_total.ElapsedMilliseconds / _spinner.Interval.TotalMilliseconds);
        return _spinner.Frames[(int)(frame % _spinner.Frames.Count)];
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    private readonly record struct Line((string Glyph, Style Style)? Icon, IReadOnlyList<(string Text, Style Style)> Parts, string? Time);
}
