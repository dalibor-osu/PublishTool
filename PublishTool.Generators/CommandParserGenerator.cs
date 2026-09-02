using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PublishTool.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class CommandParserGenerator : IIncrementalGenerator
{
    private const string CommandAttribute = "PublishTool.Attributes.CommandAttribute";
    private const string OptionAttribute = "PublishTool.Attributes.OptionAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commands = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                CommandAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => Create(attributeContext))
            .Where(static model => model != null)
            .Select(static (model, _) => model!)
            .Collect();

        context.RegisterSourceOutput(commands, static (sourceContext, models) =>
        {
            foreach (var model in models)
            {
                foreach (var diagnostic in model.Diagnostics)
                {
                    sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic());
                }
            }

            var parsable = models.Where(static model => !model.HasErrors).ToImmutableArray();
            if (parsable.Length == 0)
            {
                return;
            }

            try
            {
                sourceContext.AddSource("CommandParsers.g.cs", Emitter.Emit(parsable));
            }
            catch (Exception exception)
            {
                sourceContext.ReportDiagnostic(Diagnostic.Create(Diagnostics.TemplateFailure, null, exception.Message));
            }
        });
    }

    private static CommandModel? Create(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol commandType)
        {
            return null;
        }

        var attribute = context.Attributes[0];
        string commandName = attribute.ConstructorArguments.FirstOrDefault().Value as string ?? string.Empty;
        bool isDefault = false;
        string? description = null;

        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case "IsDefault":
                    isDefault = argument.Value.Value is true;
                    break;
                case "Description":
                    description = argument.Value.Value as string;
                    break;
            }
        }

        var diagnostics = new List<DiagnosticInfo>();
        var optionsType = FindOptionsType(commandType);
        if (optionsType == null)
        {
            diagnostics.Add(new DiagnosticInfo(Diagnostics.MissingOptionsType, LocationInfo.From(commandType), commandType.Name));
            return new CommandModel(commandName, string.Empty, string.Empty, string.Empty, isDefault, description,
                new List<OptionModel>(), diagnostics);
        }

        var options = CollectOptions(optionsType, commandName, diagnostics);

        return new CommandModel(
            commandName,
            MethodSuffix(commandType.Name),
            commandType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            optionsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            isDefault,
            description,
            options,
            diagnostics);
    }

    private static List<OptionModel> CollectOptions(INamedTypeSymbol optionsType, string commandName, List<DiagnosticInfo> diagnostics)
    {
        var options = new List<OptionModel>();
        var seenAliases = new Dictionary<string, string>();

        foreach (var member in optionsType.GetMembers())
        {
            if (member is not IPropertySymbol property)
            {
                continue;
            }

            var optionAttribute = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == OptionAttribute);
            if (optionAttribute == null)
            {
                continue;
            }

            var location = LocationInfo.From(property);
            var aliases = ReadAliases(optionAttribute);
            if (aliases.Count == 0)
            {
                diagnostics.Add(new DiagnosticInfo(Diagnostics.MissingAlias, location, property.Name));
                continue;
            }

            bool reserved = false;
            foreach (string alias in aliases)
            {
                if (Emitter.HelpAliases.Contains(alias))
                {
                    diagnostics.Add(new DiagnosticInfo(Diagnostics.ReservedAlias, location, property.Name, alias));
                    reserved = true;
                }

                if (seenAliases.TryGetValue(alias, out string? owner) && owner != property.Name)
                {
                    diagnostics.Add(new DiagnosticInfo(Diagnostics.DuplicateAlias, location, alias, commandName));
                }

                seenAliases[alias] = property.Name;
            }

            if (reserved)
            {
                continue;
            }

            string? valueName = null;
            string? optionDescription = null;
            string? parserMethod = null;
            foreach (var argument in optionAttribute.NamedArguments)
            {
                switch (argument.Key)
                {
                    case "ValueName":
                        valueName = argument.Value.Value as string;
                        break;
                    case "Description":
                        optionDescription = argument.Value.Value as string;
                        break;
                    case "Parser":
                        parserMethod = argument.Value.Value as string;
                        break;
                }
            }

            OptionKind kind;
            if (parserMethod != null)
            {
                if (!HasValidParserMethod(optionsType, parserMethod))
                {
                    diagnostics.Add(new DiagnosticInfo(Diagnostics.InvalidParserMethod, location, property.Name, parserMethod,
                        optionsType.Name));
                    continue;
                }

                kind = OptionKind.Custom;
            }
            else
            {
                var detected = GetOptionKind(property.Type);
                if (detected == null)
                {
                    diagnostics.Add(new DiagnosticInfo(Diagnostics.UnsupportedOptionType, location, property.Name,
                        property.Type.ToDisplayString()));
                    continue;
                }

                if (property.SetMethod == null || property.SetMethod.DeclaredAccessibility == Accessibility.Private)
                {
                    diagnostics.Add(new DiagnosticInfo(Diagnostics.OptionNotSettable, location, property.Name));
                    continue;
                }

                kind = detected.Value;
            }

            options.Add(new OptionModel(
                property.Name,
                kind,
                aliases,
                optionDescription,
                valueName ?? property.Name.ToLowerInvariant(),
                parserMethod));
        }

        return options;
    }

    private static List<string> ReadAliases(AttributeData attribute)
    {
        var aliases = new List<string>();
        if (attribute.ConstructorArguments.Length == 0)
        {
            return aliases;
        }

        var argument = attribute.ConstructorArguments[0];
        if (argument.Kind == TypedConstantKind.Array)
        {
            foreach (var value in argument.Values)
            {
                if (value.Value is string alias && alias.Length > 0)
                {
                    aliases.Add(alias);
                }
            }
        }
        else if (argument.Value is string single && single.Length > 0)
        {
            aliases.Add(single);
        }

        return aliases;
    }

    private static bool HasValidParserMethod(INamedTypeSymbol optionsType, string methodName) =>
        optionsType.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Any(method => method is
                           {
                               IsStatic: true,
                               Parameters.Length: 3,
                               ReturnType.SpecialType: SpecialType.System_Boolean
                           }
                           && method.DeclaredAccessibility != Accessibility.Private
                           && method.Parameters[0].Type.SpecialType == SpecialType.System_String
                           && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, optionsType)
                           && method.Parameters[2].RefKind == RefKind.Out
                           && method.Parameters[2].Type.SpecialType == SpecialType.System_String);

    // The options type is the type argument of the ICommand<TOptions> interface
    private static INamedTypeSymbol? FindOptionsType(INamedTypeSymbol commandType) =>
        commandType.AllInterfaces
            .FirstOrDefault(candidate => candidate is { Name: "ICommand", IsGenericType: true, TypeArguments.Length: 1 })?
            .TypeArguments[0] as INamedTypeSymbol;

    private static OptionKind? GetOptionKind(ITypeSymbol type)
    {
        var underlying = type;
        if (underlying is INamedTypeSymbol { IsGenericType: true } named
            && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        {
            underlying = named.TypeArguments[0];
        }

        return underlying.SpecialType switch
        {
            SpecialType.System_Boolean => OptionKind.Flag,
            SpecialType.System_String => OptionKind.String,
            SpecialType.System_Int32 => OptionKind.Int,
            _ => null
        };
    }

    private static string MethodSuffix(string commandTypeName) =>
        commandTypeName.EndsWith("Command") && commandTypeName.Length > "Command".Length
            ? commandTypeName.Substring(0, commandTypeName.Length - "Command".Length)
            : commandTypeName;
}