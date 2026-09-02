using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace PublishTool.Generators;

internal static class Emitter
{
    private const string ResultType = "global::PublishTool.Result<global::PublishTool.Commands.ICommand>";

    private static readonly Lazy<Template> ParsersTemplate = new(() => Load("CommandParsers.sbncs"));
    private static readonly Lazy<Template> HelpTemplate = new(() => Load("Help.sbncs"));

    public static string Emit(ImmutableArray<CommandModel> commands)
    {
        var views = commands
            .OrderBy(command => command.CommandName, StringComparer.Ordinal)
            .Select(CommandView.From)
            .ToList();

        foreach (var view in views)
        {
            view.Help = Render(HelpTemplate.Value, globals => globals["Command"] = view).TrimEnd();
        }

        return Render(ParsersTemplate.Value, globals =>
        {
            globals["ResultType"] = ResultType;
            globals["Commands"] = views;
            globals["DefaultCommand"] = views.FirstOrDefault(view => view.IsDefault);
        });
    }

    private static string Render(Template template, Action<ScriptObject> configure)
    {
        var globals = new ScriptObject();
        globals.Import("literal", new Func<string, string>(Literal));
        configure(globals);

        var context = new TemplateContext { MemberRenamer = member => member.Name, StrictVariables = true };
        context.TryGetMember = TryGetMember;
        context.PushGlobal(globals);

        return template.Render(context).Replace("\r\n", "\n");
    }

    private static bool TryGetMember(TemplateContext context, SourceSpan span, object target, string member, out object value)
    {
        var accessor = context.GetMemberAccessor(target);
        if (!accessor.HasMember(context, span, target, member))
        {
            throw new ScriptRuntimeException(span, $"'{target.GetType().Name}' has no member '{member}'");
        }

        bool found = accessor.TryGetValue(context, span, target, member, out object? memberValue);
        value = memberValue!;
        return found;
    }

    private static Template Load(string fileName)
    {
        var assembly = typeof(Emitter).GetTypeInfo().Assembly;
        string resourceName = $"PublishTool.Generators.Templates.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Template '{resourceName}' is missing from the generator assembly");
        using var reader = new StreamReader(stream);

        var template = Template.Parse(reader.ReadToEnd(), fileName);
        if (template.HasErrors)
        {
            throw new InvalidOperationException($"Template '{fileName}' failed to parse: {string.Join("; ", template.Messages)}");
        }

        return template;
    }

    private static string Literal(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (char character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    private sealed class CommandView
    {
        public string Name { get; private set; } = string.Empty;
        public string MethodSuffix { get; private set; } = string.Empty;
        public string CommandTypeName { get; private set; } = string.Empty;
        public string OptionsTypeName { get; private set; } = string.Empty;
        public string? Description { get; private set; }
        public bool IsDefault { get; private set; }
        public List<OptionView> Options { get; private set; } = new();
        public string Help { get; set; } = string.Empty;

        public static CommandView From(CommandModel model) => new()
        {
            Name = model.CommandName,
            MethodSuffix = model.MethodSuffix,
            CommandTypeName = model.CommandTypeName,
            OptionsTypeName = model.OptionsTypeName,
            Description = model.Description,
            IsDefault = model.IsDefault,
            Options = model.Options.Select(OptionView.From).ToList()
        };
    }

    private sealed class OptionView
    {
        public string PropertyName { get; private set; } = string.Empty;
        public string Kind { get; private set; } = string.Empty;
        public string ValueName { get; private set; } = string.Empty;
        public string? Description { get; private set; }
        public string? ParserMethod { get; private set; }
        public List<string> Aliases { get; private set; } = new();

        public static OptionView From(OptionModel model) => new()
        {
            PropertyName = model.PropertyName,
            Kind = model.Kind.ToString(),
            ValueName = model.ValueName,
            Description = model.Description,
            ParserMethod = model.ParserMethod,
            Aliases = model.Aliases.ToList()
        };
    }
}