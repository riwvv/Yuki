using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Yuki.Mcp.SystemCommand.Tools;

internal sealed record ProcessRunResult(
    int ExitCode,
    string Output,
    string Error,
    bool TimedOut,
    bool Started,
    string? StartFailure);

internal static class ProcessRunner {
    private static readonly Lazy<Encoding> ConsoleEncoding = new(() => {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try {
            var oemCodePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return oemCodePage > 0 ? Encoding.GetEncoding(oemCodePage) : Encoding.UTF8;
        }
        catch {
            return Encoding.UTF8;
        }
    });

    internal static ProcessStartInfo CreateStartInfo(ProcessSpec spec) {
        var startInfo = new ProcessStartInfo {
            FileName = spec.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // ipconfig/tasklist/ping пишут в OEM-кодировке консоли, а не в UTF-8.
            // Без явной кодировки русский вывод превращается в «кракозябры».
            StandardOutputEncoding = ConsoleEncoding.Value,
            StandardErrorEncoding = ConsoleEncoding.Value,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        // ArgumentList, а не Arguments: значения не объединяются в одну строку
        // и не интерпретируются cmd.exe / powershell.exe как код.
        foreach (var argument in spec.Arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    internal static async Task<ProcessRunResult> RunAsync(ProcessSpec spec) {
        Process? process;
        try {
            process = Process.Start(CreateStartInfo(spec));
        }
        catch (Exception ex) {
            return new ProcessRunResult(-1, string.Empty, string.Empty, false, false, ex.Message);
        }

        if (process is null)
            return new ProcessRunResult(-1, string.Empty, string.Empty, false, false, "Не удалось запустить процесс");

        using (process) {
            using var timeout = new CancellationTokenSource(spec.Timeout);
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            try {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) {
                try {
                    process.Kill(entireProcessTree: true);
                }
                catch {
                    // Процесс уже успел завершиться — это нормально.
                }

                await process.WaitForExitAsync();
                return new ProcessRunResult(
                    -1,
                    await ReadSafeAsync(outputTask),
                    await ReadSafeAsync(errorTask),
                    TimedOut: true,
                    Started: true,
                    StartFailure: null);
            }

            return new ProcessRunResult(
                process.ExitCode,
                await ReadSafeAsync(outputTask),
                await ReadSafeAsync(errorTask),
                TimedOut: false,
                Started: true,
                StartFailure: null);
        }
    }

    private static async Task<string> ReadSafeAsync(Task<string> task) {
        try {
            return await task;
        }
        catch {
            return string.Empty;
        }
    }
}
