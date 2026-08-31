using PublishTool.Commands;
using Spectre.Console;

namespace PublishTool;

public static class ArgumentParser
{
    public static Result<ICommand> Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new PublishCommand(new PublishCommand.Options { WorkingDirectory = Directory.GetCurrentDirectory() });
        }

        return args[0] switch
        {
            "publish" => ParsePublishCommand(args),
            "version" => new VersionCommand(new VersionCommand.Options()),
            "config" => ParseConfigCommand(args),
            _ => ParsePublishCommand(args)
        };
    }

    private static Result<ICommand> ParsePublishCommand(string[] args)
    {
        var options = new PublishCommand.Options();
        for (int i = args[0] == "publish" ? 1 : 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-dir":
                    if (i + 1 >= args.Length)
                    {
                        return "Missing working dir path after -dir";
                    }

                    try
                    {
                        options.WorkingDirectory = Path.GetFullPath(Path.GetRelativePath(options.WorkingDirectory, args[i++ + 1]));
                    }
                    catch
                    {
                        return "Invalid working dir path after -dir";
                    }

                    continue;
                case "-a":
                    options.All = true;
                    continue;
                default:
                    return $"Unknown argument: {args[i]}";
            }
        }

        return new PublishCommand(options);
    }

    private static Result<ICommand> ParseConfigCommand(string[] args)
    {
        var options = new ConfigCommand.Options();
        for (int i = args[0] == "config" ? 1 : 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-dir":
                    if (i + 1 >= args.Length)
                    {
                        return "Missing working dir path after -dir";
                    }

                    try
                    {
                        options.WorkingDirectory = Path.GetFullPath(Path.GetRelativePath(options.WorkingDirectory, args[i++ + 1]));
                    }
                    catch
                    {
                        return "Invalid working dir path after -dir";
                    }

                    continue;
                default:
                    return $"Unknown argument: {args[i]}";
            }
        }

        return new ConfigCommand(options);
    }
}