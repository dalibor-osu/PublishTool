using Spectre.Console;
using Spectre.Console.Rendering;

namespace PublishTool.Console;

public class BackKeyConsole(IAnsiConsole inner) : IAnsiConsole
{
    public Profile Profile => inner.Profile;

    public IAnsiConsoleCursor Cursor => inner.Cursor;

    public IAnsiConsoleInput Input { get; } = new BackKeyInput(inner.Input);

    public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;

    public RenderPipeline Pipeline => inner.Pipeline;

    public void Clear(bool home) => inner.Clear(home);

    public void Write(IRenderable renderable) => inner.Write(renderable);

    public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
}

public class BackKeyInput(IAnsiConsoleInput inner) : IAnsiConsoleInput
{
    public bool IsKeyAvailable() => inner.IsKeyAvailable();

    public ConsoleKeyInfo? ReadKey(bool intercept) => ThrowIfBack(inner.ReadKey(intercept));

    public async Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) =>
        ThrowIfBack(await inner.ReadKeyAsync(intercept, cancellationToken));

    private static ConsoleKeyInfo? ThrowIfBack(ConsoleKeyInfo? key) =>
        key?.Key is ConsoleKey.Escape or ConsoleKey.LeftArrow ? throw new BackRequestedException() : key;
}
