using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.VS;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using CommunityVS = Community.VisualStudio.Toolkit.VS;
using Constants = KS.RustAnalyzer.TestAdapter.Constants;

namespace KS.RustAnalyzer;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.FolderOpened_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuids.RustAnalyzerString)]
public sealed class RustAnalyzerPackage : ToolkitPackage
{
    private TL _tl;
    private IPreReqsCheckService _preReqs;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var cmServiceProvider = (IComponentModel)await GetServiceAsync(typeof(SComponentModel));
        _tl = new TL
        {
            L = cmServiceProvider?.GetService<ILogger>(),
            T = cmServiceProvider?.GetService<ITelemetryService>(),
        };
        _preReqs = cmServiceProvider?.GetService<IPreReqsCheckService>();
    }

    protected override async Task OnAfterPackageLoadedAsync(CancellationToken cancellationToken)
    {
        await base.OnAfterPackageLoadedAsync(cancellationToken);

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        await ReleaseSummaryNotification.ShowAsync(this, _tl);
        await VsVersionCheck.ShowAsync(_tl);
        await SearchAndDisableIncompatibleExtensionsAsync();
        await CargoCheck.ShowAsync(_preReqs, _tl);
    }

    #region Handling incompatible extensions

    private async Task SearchAndDisableIncompatibleExtensionsAsync()
    {
        _tl.L.WriteLine("Searching and disabling incompatible extensions.");

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
                var mbRet = await CommunityVS.MessageBox
                    .ShowAsync(
                        $"{Vsix.Name} has detected the followiing incompatible extensions:\r\n\r\n{string.Join("\r\n", incompatibleExtensions.Select(x => x.Id))}",
                        $"- OK: Disable the above and restart VS. (You can enable them back later from Extensions > Manage Extensions.)\r\n- Cancel: Disable {Vsix.Name} and restart VS.");
                if (mbRet == VSConstants.MessageBoxResult.IDOK)
                {
                    _tl.T.TrackEvent("DisableIncompatExts", ("Extensions", string.Join(",", incompatibleExtensions.Select(x => x.Id))));
                    foreach (var e in incompatibleExtensions)
                    {
                        exMgr.Disable(e.Extension);
                    }
                }
                else
                {
                    _tl.T.TrackEvent("DisableThisExt");
                    var thisExtension = allExtensionIds[Vsix.Id];
                    exMgr.Disable(thisExtension);
                }

                await CommunityVS.Shell.RestartAsync();
            }
        }
        catch (Exception e)
        {
            _tl.L.WriteLine("Failed in searching and disabling incompatible extensions. Ex: {0}", e);
            _tl.T.TrackException(e);
        }
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
        private const string ActionContextGetHelp = "get_help";
        private const string DismissedRegKeyName = "release_notes_dismissed";

        public static async Task ShowAsync(IServiceProvider sp, TL tl)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            tl.L.WriteLine("Attempting to show release notes...");
            if (HasBeenDismissedByUser(sp))
            {
                tl.L.WriteLine("... Not showing release notes as it has already been dismissed by the user.");
                return;
            }

            var actionItems = new[]
            {
                new InfoBarHyperlink("Release notes", ActionContextReleaseNotes),
                new InfoBarHyperlink("Get help!", ActionContextGetHelp),
                new InfoBarHyperlink("Dismiss", ActionContextDismiss),
            };
            var model = new InfoBarModel(
                textSpans: new[] { new InfoBarTextSpan($"{Vsix.Name} updated: {Constants.ReleaseSummary}"), },
                actionItems,
                image: KnownMonikers.StatusInformation,
                isCloseButtonVisible: true);
            var infoBar = await CommunityVS.InfoBar.CreateAsync(model);
            infoBar.ActionItemClicked += (s, ea) => InfoBar_ActionItemClicked(s, ea, sp, tl);
            await infoBar.TryShowInfoBarUIAsync();
        }

        private static void InfoBar_ActionItemClicked(object sender, InfoBarActionItemEventArgs e, IServiceProvider sp, TL tl)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (e.ActionItem.ActionContext is not string actionContext)
            {
                return;
            }

            switch (actionContext)
            {
                case ActionContextReleaseNotes:
                    VsShellUtilities.OpenSystemBrowser(Constants.ReleaseNotesUrl);
                    break;

                case ActionContextGetHelp:
                    VsShellUtilities.OpenSystemBrowser(Constants.DiscordUrl);
                    break;

                case ActionContextDismiss:
                    MarkDismissedByUser(sp);
                    (sender as InfoBar)?.Close();
                    break;

                default:
                    break;
            }

            tl.T.TrackEvent("InfoBarAction", ("Context", actionContext));
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

    #region VsVersionCheck

    public static class VsVersionCheck
    {
        public static async Task ShowAsync(TL tl)
        {
            tl.L.WriteLine("Doing VS version check...");
            var version = await CommunityVS.Shell.GetVsVersionAsync();
            if (version == null)
            {
                var msg = "CommunityVS.Shell.GetVsVersionAsync() returned null.";
                tl.L.WriteError(msg);
                tl.T.TrackException(new InvalidOperationException(msg));
                return;
            }

            if (version <= Constants.MinimumRequiredVsVersion)
            {
                tl.L.WriteLine("Version check failed. Minimum {0}, found {1}.", Constants.MinimumRequiredVsVersion, version);
                tl.T.TrackException(new InvalidOperationException("VsVersion check failed."), new[] { ("Minimum", Constants.MinimumRequiredVsVersion.ToString()), ("Found", version.ToString()) });
                await VsCommon.ShowMessageBoxAsync(
                    $"This package requires a minumum of Visual Studio 2022 v{Constants.MinimumRequiredVsVersion}. However current version is v{version}.",
                    "rust-analyzer will fail randomly. Please apply the latest Visual Studio 2022 updates.");
            }
        }
    }

    #endregion

    #region CargoCheck

    public static class CargoCheck
    {
        private const string RustInstallUrl = "https://www.rust-lang.org/tools/install";

        public static async Task ShowAsync(IPreReqsCheckService preReqs, TL tl)
        {
            tl.L.WriteLine("Doing Cargo check...");

            if (!preReqs.Satisfied())
            {
                tl.T.TrackException(new InvalidOperationException("Cargo check failed."));
                await VsCommon.ShowMessageBoxAsync(
                    $"{Constants.CargoExe} is not found in path. Install from {RustInstallUrl}.", "Pressing OK will open the url and restart the IDE.");
                VsShellUtilities.OpenSystemBrowser(RustInstallUrl);
                await CommunityVS.Shell.RestartAsync();
            }
        }
    }

    #endregion
}
