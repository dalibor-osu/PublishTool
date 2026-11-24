namespace PublishTool;

public static class RemovedSwitches
{
    public static string? Find(string[] args)
    {
        string command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "publish";
        if (command == "publish" && args.Contains("-a"))
        {
            return "publish -a was removed, PublishTool is interactive only. For unattended runs use 'PublishToolCli publish', which "
                + "publishes all publishable projects in Release unless --projects and --configuration say otherwise.";
        }

        if (command is "deploy" or "rollback" && args.Any(a => a is "-y" or "--yes"))
        {
            return $"{command} -y was removed, PublishTool is interactive only. For unattended runs use 'PublishToolCli {command} --yes' "
                + "(warnings have to be accepted with --accept <code>).";
        }

        return null;
    }
}
