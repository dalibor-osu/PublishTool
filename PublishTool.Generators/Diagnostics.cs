using Microsoft.CodeAnalysis;

namespace PublishTool.Generators;

internal static class Diagnostics
{
    private const string Category = "PublishTool.Cli";

    public static readonly DiagnosticDescriptor DuplicateAlias = new(
        "PT0001",
        "Duplicate option alias",
        "Alias '{0}' is used by more than one option of command '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor UnsupportedOptionType = new(
        "PT0002",
        "Unsupported option type",
        "Option '{0}' has type '{1}', which the parser generator cannot parse (supported: bool, string, int)",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MissingAlias = new(
        "PT0003",
        "Option without an alias",
        "Option '{0}' declares no alias, add at least one (for example [Option(\"-x\")])",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MissingOptionsType = new(
        "PT0004",
        "Command without an options type",
        "Command '{0}' does not implement ICommand<TOptions>, so its options type cannot be determined",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor InvalidParserMethod = new(
        "PT0007",
        "Invalid option parser method",
        "Option '{0}' names parser '{1}', which has to be a non private static method with the signature "
        + "bool {1}(string value, {2} options, out string? error)",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ReservedAlias = new(
        "PT0008",
        "Reserved option alias",
        "Option '{0}' uses alias '{1}', which every command gets for printing help",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor TemplateFailure = new(
        "PT0006",
        "Parser template failed",
        "The parser template could not be rendered: {0}",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor OptionNotSettable = new(
        "PT0005",
        "Option is not settable",
        "Option '{0}' needs an accessible setter for the generated parser to assign it",
        Category,
        DiagnosticSeverity.Error,
        true);
}