namespace PublishTool.Commands;

public interface ICommand
{
    public Task<int> ExecuteAsync(CancellationToken ct);
}

public interface ICommand<out TOptions> : ICommand
{
    public TOptions Options { get; }
}
