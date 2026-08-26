namespace PublishTool;

public enum ProjectKind {
  Sdk,
  LegacyWeb,
  LegacyLibrary
}

public class DotnetProject {
  public required string AbsolutePath { get; set; }
  public required string Name { get; set; }
  public ProjectKind Kind { get; set; }
}