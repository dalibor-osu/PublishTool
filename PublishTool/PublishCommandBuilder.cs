namespace PublishTool;

public class PublishCommandBuilder(DotnetProject project, string configuration, string publishDir)
{
    public DotnetProject Project { get; } = project;
    public string Configuration { get; } = configuration;
    public string PublishDir { get; } = publishDir;

    public string BuildCommand()
    {
        string dir = PublishDir.TrimEnd('\\');

        string command = Project.Kind switch
        {
            ProjectKind.Sdk =>
                $"\"{Project.AbsolutePath}\" /t:Restore;Publish /p:Configuration={Configuration} /p:RuntimeIdentifier=win-x64 /p:SelfContained=false /p:PublishDir=\"{dir}\\\\\"",
            ProjectKind.LegacyWeb =>
                $"\"{Project.AbsolutePath}\" /t:Build /p:Configuration={Configuration} /p:DeployOnBuild=true /p:DeployTarget=WebPublish /p:WebPublishMethod=FileSystem /p:publishUrl=\"{dir}\" /p:DeleteExistingFiles=true",
            ProjectKind.LegacyLibrary =>
                $"\"{Project.AbsolutePath}\" /t:Build /p:Configuration={Configuration} /p:OutDir=\"{dir}\\\\\"",
            _ => throw new NotSupportedException($"Unsupported project kind: {Project.Kind}")
        };

        command += " /v:minimal /nologo";
        return command;
    }
}