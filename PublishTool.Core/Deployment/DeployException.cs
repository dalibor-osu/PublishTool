namespace PublishTool.Deployment;

public sealed class DeployException(string message, Exception? inner = null) : Exception(message, inner);
