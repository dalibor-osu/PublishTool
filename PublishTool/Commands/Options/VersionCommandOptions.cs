using System.Diagnostics.CodeAnalysis;

namespace PublishTool.Commands.Options;

[SuppressMessage("Minor Code Smell", "S2094:Classes should not be empty", Justification = "Every command needs an options type for the generated parser")]
public class VersionCommandOptions;