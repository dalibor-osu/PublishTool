namespace PublishTool.Commands;

public interface ICommand
{
    public bool UsesAlternateScreen { get; }
    public Task<int> ExecuteAsync(CancellationToken ct);
}

public interface ICommand<out TOptions> : ICommand
{
    public TOptions Options { get; }
}