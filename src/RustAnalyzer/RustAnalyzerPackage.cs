using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using CommunityVS = Community.VisualStudio.Toolkit.VS;

namespace KS.RustAnalyzer;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.FolderOpened_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuids.RustAnalyzerString)]
public sealed class RustAnalyzerPackage : ToolkitPackage
{
    private ILogger _l;

    private ITelemetryService _t;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var cmServiceProvider = (IComponentModel)await GetServiceAsync(typeof(SComponentModel));
        _l = cmServiceProvider?.GetService<ILogger>();
        _t = cmServiceProvider?.GetService<ITelemetryService>();
    }

    protected override async Task OnAfterPackageLoadedAsync(CancellationToken cancellationToken)
    {
        await base.OnAfterPackageLoadedAsync(cancellationToken);

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        await ReleaseSummaryNotification.ShowAsync(this, _l, _t);
        await SearchAndDisableIncompatibleExtensionsAsync();
    }

    #region Handling incompatible extensions

    private async Task SearchAndDisableIncompatibleExtensionsAsync()
    {
        _l?.WriteLine("Searching and disabling incompatible extensions.");

        try
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var exMgrAssembly = Assembly.LoadWithPartialName("Microsoft.VisualStudio.ExtensionManager");
#pragma warning restore CS0618 // Type or member is obsolete
            var exMgrType = exMgrAssembly.GetType("Microsoft.VisualStudio.ExtensionManager.SVsExtensionManager");
            dynamic exMgr = GetGlobalService(exMgrType);

            var allExtensions = exMgr.GetInstalledExtensions() as IEnumerable<dynamic>;
            var allExtensionIds = allExtensions.ToDictionary(x => x.Identifier as string);
            var incompatibleExtensions = AreIncompatibleExtensionsInstalled(allExtensionIds);
            if (incompatibleExtensions.Count != 0)
            {
                var mbRet = await new MessageBox()
                    .ShowAsync(
                        $"{Vsix.Name} has detected the followiing incompatible extensions:\r\n\r\n{string.Join("\r\n", incompatibleExtensions.Select(x => x.Id))}",
                        $"- OK: Disable the above and restart VS. (You can enable them back later from Extensions > Manage Extensions.)\r\n- Cancel: Disable {Vsix.Name} and restart VS.");
                if (mbRet == VSConstants.MessageBoxResult.IDOK)
                {
                    _t?.TrackEvent("DisableIncompatExts", ("Extensions", string.Join(",", incompatibleExtensions.Select(x => x.Id))));
                    foreach (var e in incompatibleExtensions)
                    {
                        exMgr.Disable(e.Extension);
                    }
                }
                else
                {
                    _t?.TrackEvent("DisableThisExt");
                    var thisExtension = allExtensionIds[Vsix.Id];
                    exMgr.Disable(thisExtension);
                }

                await RestartProcessAsync();
            }
        }
        catch (Exception e)
        {
            _l?.WriteLine("Failed in searching and disabling incompatible extensions. Ex: {0}", e);
            _t?.TrackException(e);
        }
    }

    private async Task RestartProcessAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

        var args = Environment.GetCommandLineArgs();
        Process.Start(args[0], string.Join(" ", args.Skip(1)));
        await Task.Delay(2000);

        (GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE).Quit();
    }

    private static IReadOnlyList<(string Id, dynamic Extension)> AreIncompatibleExtensionsInstalled(IDictionary<string, dynamic> allExtensions)
    {
        var incompatibleExtensions = new[]
        {
            "SourceGear.Rust.0c9f177a-b25e-4f25-9a35-b9049b4f9c9c",
            "VS_RustAnalyzer.c5a2b628-2a68-4643-808e-0838e3fb240b",
        };

        var installedIncompatibleExtensions = incompatibleExtensions
            .Aggregate(
                new List<(string, dynamic)>(),
                (acc, e) =>
                {
                    if (allExtensions.ContainsKey(e) && allExtensions[e].State.ToString() != "Disabled")
                    {
                        acc.Add((e, allExtensions[e]));
                    }

                    return acc;
                });

        return installedIncompatibleExtensions;
    }

    #endregion

    #region Release summary

    public static class ReleaseSummaryNotification
    {
        private const string ActionContextReleaseNotes = "release_notes";
        private const string ActionContextDismiss = "dismiss";
        private const string DismissedRegKeyName = "release_notes_dismissed";

        public static async Task ShowAsync(IServiceProvider sp, ILogger l, ITelemetryService t)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            l.WriteLine("Attempting to show release notes...");
            if (HasBeenDismissedByUser(sp))
            {
                l.WriteLine("... Not showing release notes as it has already been dismissed by the user.");
                return;
            }

            var model = new InfoBarModel(
                textSpans: new[] { new InfoBarTextSpan($"{Vsix.Name} updated: Featuring improved build & debug experience for large OSS projects + general bug fixes."), },
                actionItems: new[] { new InfoBarHyperlink("Release notes", ActionContextReleaseNotes), new InfoBarHyperlink("Dismiss", ActionContextDismiss), },
                image: KnownMonikers.StatusInformation,
                isCloseButtonVisible: true);
            var infoBar = await CommunityVS.InfoBar.CreateAsync(model);
            infoBar.ActionItemClicked += (s, ea) => InfoBar_ActionItemClicked(s, ea, sp, t);
            await infoBar.TryShowInfoBarUIAsync();
        }

        private static void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e, IServiceProvider sp, ITelemetryService t)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (e.ActionItem.ActionContext is not string actionContext)
            {
                return;
            }

            switch (actionContext)
            {
                case ActionContextReleaseNotes:
                    VsShellUtilities.OpenSystemBrowser($"https://github.com/kitamstudios/rust-analyzer.vs/releases/{Vsix.Version}");
                    break;

                case ActionContextDismiss:
                    MarkDismissedByUser(sp);
                    (sender as InfoBar)?.Close();
                    break;

                default:
                    break;
            }

            t.TrackEvent("InfoBarAction", ("Context", actionContext));
        }

        private static void MarkDismissedByUser(IServiceProvider sp)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (GetPackageRegistryRoot(sp, out string regRoot))
            {
                Registry.SetValue(regRoot, DismissedRegKeyName, Vsix.Version);
            }
        }

        private static bool HasBeenDismissedByUser(IServiceProvider sp)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (GetPackageRegistryRoot(sp, out string regRoot))
            {
                return Registry.GetValue(regRoot, DismissedRegKeyName, null)?.ToString() == Vsix.Version;
            }

            return false;
        }

        private static bool GetPackageRegistryRoot(IServiceProvider sp, out string packageRegistryRoot)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            packageRegistryRoot = null;
            if (sp.GetService(typeof(SLocalRegistry)) is ILocalRegistry2 localReg && ErrorHandler.Succeeded(localReg.GetLocalRegistryRoot(out var localRegRoot)))
            {
                packageRegistryRoot = Path.Combine("HKEY_CURRENT_USER", localRegRoot, Vsix.Name);
                return true;
            }

            return false;
        }
    }

    #endregion
}
