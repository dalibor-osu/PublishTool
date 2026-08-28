using Spectre.Console;
using Spectre.Console.Rendering;

namespace PublishTool.Console;

sealed class ExpandingDescriptionColumn(int reserved) : ProgressColumn {
  protected override bool NoWrap => true;

  public override int? GetColumnWidth(RenderOptions options)
    => Math.Max(10, options.ConsoleSize.Width - reserved);

  public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    => new Markup(task.Description ?? string.Empty).Overflow(Overflow.Ellipsis).LeftJustified();
}