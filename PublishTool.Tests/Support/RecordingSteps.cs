using PublishTool.Progress;

namespace PublishTool.Tests.Support;

public sealed class RecordingSteps : IStepList
{
    public List<RecordedStep> Steps { get; } = [];

    public IStep Add(string name)
    {
        var step = new RecordedStep { Name = name };
        Steps.Add(step);
        return step;
    }
}

public sealed class RecordedStep : IStep
{
    public string Name { get; set; } = string.Empty;

    public StepState State { get; private set; } = StepState.Pending;

    public string? Detail { get; private set; }

    public List<string> Lines { get; } = [];

    public bool IsFinished => State is not (StepState.Pending or StepState.Running);

    public void Start() => State = StepState.Running;

    public void AppendLine(string line) => Lines.Add(line);

    public void Finish(StepState state, string? detail = null)
    {
        State = state;
        Detail = detail;
    }
}
