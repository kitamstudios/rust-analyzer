using System;
using System.ComponentModel.Composition;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace KS.RustAnalyzer.Infrastructure;

[Export(typeof(ILogger))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class OutputWindowLogger : ILogger
{
    private static readonly Guid OuputWidowPaneGuid = new("9142a5bb-c829-4d2a-87e3-9c7b545edf30");
    private static readonly string OuputWidowPaneName = Vsix.Name;
    private IVsOutputWindowPane _pane;

    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    public SVsServiceProvider ServiceProvider { get; set; }

    public void WriteLine(string format, params object[] args)
    {
        try
        {
            RustAnalyzerPackage.JTF.RunAsync(async () =>
            {
                await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();
                WriteCore(format, args);
            }).FireAndForget();
        }
        catch (Exception e)
        {
            T.TrackException(e);
        }
    }

    public void WriteError(string format, params object[] args)
    {
        try
        {
            RustAnalyzerPackage.JTF.RunAsync(async () =>
            {
                await RustAnalyzerPackage.JTF.SwitchToMainThreadAsync();
                WriteCore("[ERROR]: " + format, args);
            }).FireAndForget();
        }
        catch (Exception e)
        {
            T.TrackException(e);
        }
    }

    private void WriteCore(string format, object[] args)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (EnsurePane())
        {
            _pane.OutputStringThreadSafe($"{DateTime.Now:yyMMdd.HH.mm.ss.fff} - {string.Format(format, args)}\n");
        }
    }

    private bool EnsurePane()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_pane == null)
        {
            var outputWindow = ServiceProvider.GetService<SVsOutputWindow, IVsOutputWindow>();
            Guid guid = OuputWidowPaneGuid;
            outputWindow.CreatePane(ref guid, OuputWidowPaneName, 1, 1);
            outputWindow.GetPane(ref guid, out _pane);
        }

        return _pane != null;
    }
}
