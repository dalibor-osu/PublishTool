namespace PublishTool.Commands;

public interface ICommand
{
    public bool UsesAlternateScreen { get; }
    public Task<int> ExecuteAsync(CancellationToken ct);
}