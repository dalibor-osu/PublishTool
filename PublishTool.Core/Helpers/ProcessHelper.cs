using System.Diagnostics;
using System.Text;

namespace PublishTool.Helpers;

public static class ProcessHelper
{
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string file,
        string arguments,
        string? workingDirectory = null,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process();
        process.StartInfo = psi;

        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => Collect(output, e.Data, onOutputLine);
        process.ErrorDataReceived += (_, e) => Collect(error, e.Data, onOutputLine);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var killOnCancel = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may have exited in the meantime, nothing to do
            }
        });

        await process.WaitForExitAsync(cancellationToken);

        lock (output)
        {
            lock (error)
            {
                return (process.ExitCode, output.ToString(), error.ToString());
            }
        }
    }

    private static void Collect(StringBuilder buffer, string? line, Action<string>? onLine)
    {
        if (line is null)
        {
            return;
        }

        lock (buffer)
        {
            buffer.AppendLine(line);
        }

        onLine?.Invoke(line);
    }
}
