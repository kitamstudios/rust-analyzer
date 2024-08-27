using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

namespace KS.RustAnalyzer.Infrastructure;

public static class VsCommon
{
    public static void ShowMessageBox(string line1)
    {
        ShowMessageBox(line1, string.Empty);
    }

    public static void ShowMessageBox(string line1, string line2)
    {
        CommunityVS.MessageBox.Show(
            line1.AddPrefixToMessage(),
            line2,
            OLEMSGICON.OLEMSGICON_CRITICAL,
            OLEMSGBUTTON.OLEMSGBUTTON_OK);
    }

    public static Task ShowMessageBoxAsync(string line1)
    {
        return ShowMessageBoxAsync(line1, string.Empty);
    }

    public static Task ShowMessageBoxAsync(string line1, string line2)
    {
        return CommunityVS.MessageBox.ShowAsync(
            line1.AddPrefixToMessage(),
            line2,
            OLEMSGICON.OLEMSGICON_CRITICAL,
            OLEMSGBUTTON.OLEMSGBUTTON_OK);
    }

    public static async Task ShowInfoBarAsync(bool success, string message)
    {
        var model = new InfoBarModel(
                textSpans: new[] { new InfoBarTextSpan($"{Vsix.Name}: {message}"), },
                Array.Empty<InfoBarHyperlink>(),
                image: success ? KnownMonikers.StatusInformation : KnownMonikers.StatusError,
                isCloseButtonVisible: true);
        var infoBar = await CommunityVS.InfoBar.CreateAsync(model);
        await infoBar.TryShowInfoBarUIAsync();
    }

    public static string GetFullName(this VSITEMSELECTION item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (ErrorHandler.Succeeded(item.pHier.GetCanonicalName(item.itemid, out var name)))
        {
            return name;
        }

        return string.Empty;
    }

    // NOTE: Stolen + modifed from https://github.com/microsoft/nodejstools/blob/349dc94b55cc3a88a7c35d600c3ac6d954aa662e/Nodejs/Product/Nodejs/NodejsProject.cs#L197.
    public static IEnumerable<VSITEMSELECTION> GetSelectedItems()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var monitorSelection = CommunityVS.GetRequiredService<SVsShellMonitorSelection, IVsMonitorSelection>();

        var hierarchyPtr = IntPtr.Zero;
        var selectionContainer = IntPtr.Zero;
        try
        {
            ErrorHandler.ThrowOnFailure(monitorSelection.GetCurrentSelection(out hierarchyPtr, out var selectionItemId, out var multiItemSelect, out selectionContainer));

            if (selectionItemId != VSConstants.VSITEMID_NIL && hierarchyPtr != IntPtr.Zero)
            {
                var hierarchy = Marshal.GetObjectForIUnknown(hierarchyPtr) as IVsHierarchy;

                if (selectionItemId != VSConstants.VSITEMID_SELECTION)
                {
                    yield return new VSITEMSELECTION() { itemid = selectionItemId, pHier = hierarchy };
                }
                else if (multiItemSelect != null)
                {
                    ErrorHandler.ThrowOnFailure(multiItemSelect.GetSelectionInfo(out var numberOfSelectedItems, out var isSingleHierarchyInt));

                    var vsItemSelections = new VSITEMSELECTION[numberOfSelectedItems];
                    ErrorHandler.ThrowOnFailure(multiItemSelect.GetSelectedItems(0, numberOfSelectedItems, vsItemSelections));

                    foreach (var vsItemSelection in vsItemSelections)
                    {
                        yield return new VSITEMSELECTION() { itemid = vsItemSelection.itemid, pHier = hierarchy };
                    }
                }
            }
        }
        finally
        {
            if (hierarchyPtr != IntPtr.Zero)
            {
                Marshal.Release(hierarchyPtr);
            }

            if (selectionContainer != IntPtr.Zero)
            {
                Marshal.Release(selectionContainer);
            }
        }
    }

    public static string GetProfile(this IComponentModel2 @this, PathEx manifestPath)
    {
        var w = @this.GetService<IVsFolderWorkspaceService>().CurrentWorkspace;
        return w.GetProfile(manifestPath);
    }

    public static string GetProfile(this IWorkspace @this, PathEx manifestPath)
    {
        var projCfgSvc = @this.GetService<IProjectConfigurationService>();
        var profile = projCfgSvc.GetActiveProjectBuildConfiguration(new ProjectTargetFileContext(manifestPath));
        return profile;
    }

    private static string AddPrefixToMessage(this string @this) => $"[{Vsix.Name} v{Vsix.Version}]\n\n{@this}";
}