using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.Editor;

public sealed class FileContextProvider : IFileContextProvider, IFileContextProvider<string>
{
    private readonly IMetadataService _mds;
    private readonly IToolchainService _cargoService;
    private readonly IBuildOutputSink _outputPane;
    private readonly ISettingsService _settingsService;

    public FileContextProvider(IMetadataService mds, IToolchainService cargoService, IBuildOutputSink outputPane, ISettingsService settingsService)
    {
        _mds = mds;
        _cargoService = cargoService;
        _outputPane = outputPane;
        _settingsService = settingsService;
    }

    public Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, string context, CancellationToken cancellationToken)
    {
        return GetContextsForFileAsync(filePath, cancellationToken);
    }

    public async Task<IReadOnlyCollection<FileContext>> GetContextsForFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fp = (PathEx)filePath;
        var package = await _mds.GetContainingPackageAsync(fp, cancellationToken);
        if (package == null)
        {
            return await Task.FromResult(FileContext.EmptyFileContexts);
        }

        var args = await GetBuildTargetInfoForBuildActionAsync(fp);

        if (fp.IsManifest())
        {
            return package.GetProfiles()
                .SelectMany(
                    profile => new[]
                    {
                        new FileContext(
                            FileContextProviderFactory.ProviderTypeGuid,
                            BuildContextTypes.BuildContextTypeGuid,
                            new BuildFileContext(
                                _cargoService,
                                new BuildTargetInfo
                                {
                                    Profile = profile,
                                    WorkspaceRoot = package.WorkspaceRoot,
                                    ManifestPath = fp,
                                    AdditionalBuildArgs = args.AdditionalBuildArgs,
                                    AdditionalTestDiscoveryArguments = args.AdditionalTestDiscoveryArguments,
                                    AdditionalTestExecutionArguments = args.AdditionalTestExecutionArguments,
                                    TestExecutionEnvironment = args.TestExecutionEnvironment,
                                },
                                _outputPane),
                            new[] { (string)fp },
                            displayName: profile),
                        new FileContext(
                            FileContextProviderFactory.ProviderTypeGuid,
                            BuildContextTypes.CleanContextTypeGuid,
                            new CleanFileContext(_cargoService, new BuildTargetInfo { Profile = profile, WorkspaceRoot = package.WorkspaceRoot, ManifestPath = fp }, _outputPane),
                            new[] { (string)fp },
                            displayName: profile),
                    })
                .ToList();
        }
        else if (fp.IsRustFile())
        {
            var target = package.GetTargets().Where(t => t.SourcePath == fp && t.IsRunnable).FirstOrDefault();
            if (target != null)
            {
                return package.GetProfiles().SelectMany(p => GetBuildActions(target, p, args.AdditionalBuildArgs)).ToList();
            }
        }

        return FileContext.EmptyFileContexts;
    }

    private async Task<(string AdditionalBuildArgs, string AdditionalTestDiscoveryArguments, string AdditionalTestExecutionArguments, string TestExecutionEnvironment)> GetBuildTargetInfoForBuildActionAsync(PathEx filePath)
    {
        return (
            AdditionalBuildArgs: await _settingsService.GetAsync(SettingsInfo.TypeAdditionalBuildArguments, filePath),
            AdditionalTestDiscoveryArguments: await _settingsService.GetAsync(SettingsInfo.TypeAdditionalTestDiscoveryArguments, filePath),
            AdditionalTestExecutionArguments: await _settingsService.GetAsync(SettingsInfo.TypeAdditionalTestExecutionArguments, filePath),
            TestExecutionEnvironment: await _settingsService.GetAsync(SettingsInfo.TypeTestExecutionEnvironment, filePath));
    }

    private IEnumerable<FileContext> GetBuildActions(Workspace.Target target, string profile, string additionalBuildArgs)
    {
        var action = new[]
        {
            new FileContext(
                providerType: FileContextProviderFactory.ProviderTypeGuid,
                contextType: BuildContextTypes.BuildContextTypeGuid,
                context:
                    new BuildFileContext(
                        _cargoService,
                        new BuildTargetInfo
                        {
                            Profile = profile,
                            WorkspaceRoot = target.Parent.WorkspaceRoot,
                            ManifestPath = target.Parent.ManifestPath,
                            AdditionalBuildArgs = $"{target.AdditionalBuildArgs} {additionalBuildArgs}".Trim(),
                        },
                        _outputPane),
                inputFiles: new[] { (string)target.SourcePath },
                displayName: profile),
        };

        return action;
    }
}
