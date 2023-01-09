using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using KS.RustAnalyzer.VS;

namespace KS.RustAnalyzer.Cargo;

public class CargoExeRunner
{
    public static Task<bool> CompileFileAsync(string filePath, RustOutputPane outputPane, string workingDir = null)
    {
        throw new NotImplementedException();
    }

    public static async Task<bool> CompileProjectAsync(string filePath, string profile, RustOutputPane outputPane)
    {
        if (!RustHelpers.IsCargoFile(filePath) || !Path.IsPathRooted(filePath))
        {
            throw new ArgumentException($"{nameof(filePath)} has to be a rooted cargo file.");
        }

        return await CompileAsync($"build --manifest-path \"{filePath}\" --profile {profile} --message-format short", workingDir: Path.GetDirectoryName(filePath), redirector: new CompileRedirector(outputPane));
    }

    private static async Task<bool> CompileAsync(string arguments, string workingDir, ProcessOutputRedirector redirector)
    {
        Debug.Assert(!string.IsNullOrEmpty(arguments), $"{nameof(arguments)} should not be empty.");

        redirector?.WriteLine($"=== Build started: {RustConstants.PathToCargo} {arguments} ===");

        using (var process = ProcessOutputProcessor.Run(
            RustConstants.PathToCargo,
            new[] { arguments },
            workingDir,
            env: null,
            visible: false,
            redirector: redirector,
            quoteArgs: false,
            outputEncoding: Encoding.UTF8))
        {
            var whnd = process.WaitHandle;
            if (whnd == null)
            {
                // Process failed to start, and any exception message has
                // already been sent through the redirector
                redirector.WriteErrorLine(string.Format("Error - Failed to start '{0}'", RustConstants.PathToCargo));
                return false;
            }
            else
            {
                var finished = await Task.Run(() => whnd.WaitOne());
                if (finished)
                {
                    Debug.Assert(process.ExitCode.HasValue, "cargo.exe process has not really exited");

                    // there seems to be a case when we're signalled as completed, but the
                    // process hasn't actually exited
                    process.Wait();

                    redirector.WriteErrorLine($"==== Build completed ====");

                    return process.ExitCode == 0;
                }
                else
                {
                    process.Kill();
                    redirector.WriteErrorLine($"====  Build canceled ====");

                    return false;
                }
            }
        }
    }

    private sealed class CompileRedirector : ProcessOutputRedirector
    {
        private readonly RustOutputPane _outputPane;

        public CompileRedirector(RustOutputPane outputPane)
        {
            _outputPane = outputPane;
        }

        public override void WriteErrorLine(string line)
        {
            _outputPane.WriteLine(line, OutputWindowTarget.Cargo);
        }

        public override void WriteLine(string line)
        {
            _outputPane.WriteLine(line, OutputWindowTarget.Cargo);
        }
    }
}