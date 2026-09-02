using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PublishTool.Generators;

internal enum OptionKind
{
    Flag,
    String,
    Int,
    Custom
}

internal sealed class LocationInfo : IEquatable<LocationInfo>
{
    private readonly string _filePath;
    private readonly TextSpan _textSpan;
    private readonly LinePositionSpan _lineSpan;

    private LocationInfo(string filePath, TextSpan textSpan, LinePositionSpan lineSpan)
    {
        _filePath = filePath;
        _textSpan = textSpan;
        _lineSpan = lineSpan;
    }

    public static LocationInfo? From(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
        {
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new LocationInfo(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(_filePath, _textSpan, _lineSpan);

    public bool Equals(LocationInfo? other) =>
        other != null && _filePath == other._filePath && _textSpan == other._textSpan && _lineSpan.Equals(other._lineSpan);

    public override bool Equals(object? obj) => Equals(obj as LocationInfo);

    public override int GetHashCode() => _filePath.GetHashCode() ^ _textSpan.GetHashCode();
}

internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    private readonly DiagnosticDescriptor _descriptor;
    private readonly LocationInfo? _location;
    private readonly string?[] _messageArgs;

    public DiagnosticInfo(DiagnosticDescriptor descriptor, LocationInfo? location, params string?[] messageArgs)
    {
        _descriptor = descriptor;
        _location = location;
        _messageArgs = messageArgs;
    }

    public bool IsError => _descriptor.DefaultSeverity == DiagnosticSeverity.Error;

    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(_descriptor, _location?.ToLocation(), _messageArgs);

    public bool Equals(DiagnosticInfo? other) =>
        other != null
        && _descriptor.Id == other._descriptor.Id
        && Equals(_location, other._location)
        && _messageArgs.SequenceEqual(other._messageArgs);

    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    public override int GetHashCode() => _descriptor.Id.GetHashCode();
}

internal sealed class OptionModel : IEquatable<OptionModel>
{
    public OptionModel(string propertyName, OptionKind kind, IReadOnlyList<string> aliases, string? description, string valueName,
        string? parserMethod)
    {
        PropertyName = propertyName;
        Kind = kind;
        Aliases = aliases;
        Description = description;
        ValueName = valueName;
        ParserMethod = parserMethod;
    }

    public string PropertyName { get; }
    public OptionKind Kind { get; }
    public IReadOnlyList<string> Aliases { get; }
    public string? Description { get; }
    public string ValueName { get; }
    public string? ParserMethod { get; }

    public bool Equals(OptionModel? other) =>
        other != null
        && PropertyName == other.PropertyName
        && Kind == other.Kind
        && Description == other.Description
        && ValueName == other.ValueName
        && ParserMethod == other.ParserMethod
        && Aliases.SequenceEqual(other.Aliases);

    public override bool Equals(object? obj) => Equals(obj as OptionModel);

    public override int GetHashCode() => PropertyName.GetHashCode() ^ (int)Kind;
}

internal sealed class CommandModel : IEquatable<CommandModel>
{
    public CommandModel(
        string commandName,
        string methodSuffix,
        string commandTypeName,
        string optionsTypeName,
        bool isDefault,
        string? description,
        IReadOnlyList<OptionModel> options,
        IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        CommandName = commandName;
        MethodSuffix = methodSuffix;
        CommandTypeName = commandTypeName;
        OptionsTypeName = optionsTypeName;
        IsDefault = isDefault;
        Description = description;
        Options = options;
        Diagnostics = diagnostics;
    }

    public string CommandName { get; }
    public string MethodSuffix { get; }
    public string CommandTypeName { get; }
    public string OptionsTypeName { get; }
    public bool IsDefault { get; }
    public string? Description { get; }
    public IReadOnlyList<OptionModel> Options { get; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    public bool Equals(CommandModel? other) =>
        other != null
        && CommandName == other.CommandName
        && MethodSuffix == other.MethodSuffix
        && CommandTypeName == other.CommandTypeName
        && OptionsTypeName == other.OptionsTypeName
        && IsDefault == other.IsDefault
        && Description == other.Description
        && Options.SequenceEqual(other.Options)
        && Diagnostics.SequenceEqual(other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as CommandModel);

    public override int GetHashCode() => CommandName.GetHashCode() ^ CommandTypeName.GetHashCode();
}