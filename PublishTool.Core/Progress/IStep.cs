namespace PublishTool.Progress;

public enum StepState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}

public interface IStep
{
    public string Name { get; set; }

    public StepState State { get; }

    public bool IsFinished { get; }

    public void Start();

    public void AppendLine(string line);

    public void Finish(StepState state, string? detail = null);
}

public interface IStepList
{
    public IStep Add(string name);
}
