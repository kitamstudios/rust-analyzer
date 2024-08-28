using System;
using System.ComponentModel.Composition;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;

namespace KS.RustAnalyzer.Editor;

[ExportFileContextProvider(
    type: ProviderType,
    priority: ProviderPriority.Normal,
    supportedGetContextsTypes: new[] { typeof(string) },
    supportedContextTypeGuids: new[] { BuildContextTypes.BuildContextType, BuildContextTypes.CleanContextType, })]
public sealed class FileContextProviderFactory : IWorkspaceProviderFactory<IFileContextProvider>
{
    public static readonly Guid ProviderTypeGuid = new(ProviderType);

    private const string ProviderType = "{72D3FCEF-0000-4266-B8DD-D3ED06E35A2B}";

    [Import]
    public IBuildOutputSink OutputPane { get; set; }

    [Import]
    public ILogger L { get; set; }

    [Import]
    public ITelemetryService T { get; set; }

    [Import]
    public IToolchainService CargoService { get; set; }

    [Import]
    public IPreReqsCheckService PreReqs { get; set; }

    public IFileContextProvider CreateProvider(IWorkspace workspaceContext)
    {
        T.TrackEvent(
            "Create Context Provider",
            new[] { ("Location", workspaceContext.Location) });
        L.WriteLine("Creating {0}.", GetType().Name);

        return new FileContextProvider(workspaceContext.GetService<IMetadataService>(), CargoService, OutputPane, workspaceContext.GetService<ISettingsService>());
    }
}
