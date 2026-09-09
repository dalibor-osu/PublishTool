namespace PublishTool.Configuration;

public sealed class ConfigException(string message, Exception? inner = null) : Exception(message, inner);
