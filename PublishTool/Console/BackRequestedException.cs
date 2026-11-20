namespace PublishTool.Console;

public class BackRequestedException : Exception
{
    public BackRequestedException() : base("The user asked to go back.")
    {
    }

    public BackRequestedException(string message) : base(message)
    {
    }

    public BackRequestedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
