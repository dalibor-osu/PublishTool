using Spectre.Console;
using Spectre.Console.Rendering;

namespace PublishTool.Console;

internal static class JobDisplayRunner
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);
    private static readonly string Escape = ((char)27).ToString();
    private static readonly ControlCode CursorHome = new(Escape + "[H");
    private static readonly ControlCode EraseBelow = new(Escape + "[J");

    public static async Task RunAsync(JobDisplay display, Task work, bool live)
    {
        if (!live)
        {
            await work;
            display.Stop();
            AnsiConsole.Write(display);
            return;
        }

        AnsiConsole.Cursor.Hide();
        try
        {
            while (await Task.WhenAny(work, Task.Delay(RefreshInterval, CancellationToken.None)) != work)
            {
                Draw(display);
            }

            await work;
            display.Stop();
            Draw(display);
        }
        finally
        {
            AnsiConsole.Cursor.Show();
        }
    }

    private static void Draw(JobDisplay display)
    {
        AnsiConsole.Write(CursorHome);
        AnsiConsole.Write(display);
        AnsiConsole.Write(EraseBelow);
    }
}
