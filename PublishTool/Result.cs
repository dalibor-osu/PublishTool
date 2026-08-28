namespace PublishTool;

public class Result<T> {
  private readonly T? _value = default;
  private readonly string? _error = null;

  public bool IsSuccess => _error == null;

  public T Value => _value ?? throw new InvalidOperationException("Accessed Value of invalid result");
  public string Error => _error ?? throw new InvalidOperationException("Accessed Error of valid result");

  public Result(T value) {
    _value = value;
  }

  public Result(string error) {
    _error = error;
  }

  public static implicit operator Result<T>(T value) {
    return new Result<T>(value);
  }

  public static implicit operator Result<T>(string error) {
    return new Result<T>(error);
  }
}