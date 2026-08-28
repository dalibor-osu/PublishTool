namespace PublishTool.Commands;

public interface ICommand {
  public Task<int> ExecuteAsync(CancellationToken ct);
}