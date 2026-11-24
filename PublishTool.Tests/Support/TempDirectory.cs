namespace PublishTool.Tests.Support;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PublishTool.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string Write(string relativePath, string content)
    {
        string path = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string Read(string relativePath) => File.ReadAllText(Combine(relativePath.Split('/')));

    public bool Exists(string relativePath) => File.Exists(Combine(relativePath.Split('/')));

    public void Dispose() => DeployPreparer.TryDelete(Path);
}
