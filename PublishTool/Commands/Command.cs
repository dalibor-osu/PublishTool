namespace PublishTool.Commands;

public abstract class Command<TOptions>(TOptions options) : ICommand
{
    public TOptions Options { get; protected set; } = options;

    public abstract bool UsesAlternateScreen { get; }
    public abstract Task<int> ExecuteAsync(CancellationToken ct);
}