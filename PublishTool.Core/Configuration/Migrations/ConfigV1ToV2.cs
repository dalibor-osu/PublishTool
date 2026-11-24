using PublishTool.Configuration.Versions;

namespace PublishTool.Configuration.Migrations;

public sealed class ConfigV1ToV2 : ConfigMigrator<ConfigV1, Config>
{
    public override int From => 1;

    public override Config Migrate(ConfigV1 source)
    {
        var config = new Config();
        foreach (var (path, publish) in source.PublishDirectories)
        {
            config.For(path).PublishDirectory = publish;
        }

        foreach (var (path, shared) in source.SharedDirectories)
        {
            config.For(path).SharedDirectory = shared;
        }

        foreach (var (path, deploy) in source.DeployDirectories)
        {
            config.For(path).DeployDirectory = deploy;
        }

        foreach (var (path, ignored) in source.IgnoredDirectories)
        {
            config.For(path).IgnoredDirectories = ignored;
        }

        foreach (var (path, projects) in source.PublishableProjects)
        {
            config.For(path).PublishableProjects = projects;
        }

        return config;
    }
}
