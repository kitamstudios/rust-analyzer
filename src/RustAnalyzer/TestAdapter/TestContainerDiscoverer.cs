using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.VSIntegration.Contracts;
using ILogger = KS.RustAnalyzer.TestAdapter.Common.ILogger;

namespace KS.RustAnalyzer.TestAdapter;

[Export(typeof(ITestContainerDiscoverer))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TestContainerDiscoverer : ITestContainerDiscoverer
{
    private readonly ConcurrentDictionary<PathEx, TestContainer> _testContainersCache = new();

    private readonly IVsFolderWorkspaceService _workspaceFactory;
    private readonly TL _tl;
    private IWorkspace _currentWorkspace;

    [ImportingConstructor]
    public TestContainerDiscoverer([Import] SVsServiceProvider serviceProvider, [Import] ITelemetryService t, [Import] ILogger l)
    {
        _workspaceFactory = VS.GetRequiredService<SComponentModel, IComponentModel>()
            .GetService<IVsFolderWorkspaceService>();
        _tl = new TL
        {
            T = t,
            L = l,
        };

        RustAnalyzerPackage.JTF.RunAsync(
            async () =>
            {
                _currentWorkspace = _workspaceFactory.CurrentWorkspace;
                await ActiveWorkspaceChangedEventHandlerAsync(this, new EventArgs());
                _workspaceFactory.OnActiveWorkspaceChanged += ActiveWorkspaceChangedEventHandlerAsync;
            }).FireAndForget();
    }

    public event EventHandler TestContainersUpdated;

    public Uri ExecutorUri => new(Constants.ExecutorUriString);

    public IEnumerable<ITestContainer> TestContainers => _testContainersCache.Values;

    private async Task ActiveWorkspaceChangedEventHandlerAsync(object sender, EventArgs eventArgs)
    {
        _testContainersCache.Clear();

        UnloadOldWorkspace();

        await LoadNewWorkspaceAsync();
    }

    private async Task LoadNewWorkspaceAsync()
    {
        if (_workspaceFactory.CurrentWorkspace == null)
        {
            return;
        }

        _currentWorkspace = _workspaceFactory.CurrentWorkspace;
        _tl.L.WriteLine("TestContainerDiscoverer loading new workspace at '{0}'.", _currentWorkspace.Location);
        _tl.T.TrackEvent("TcdLoadWorkspace", ("Location", _currentWorkspace.Location));
        var mds = _currentWorkspace.GetService<IMetadataService>();
        var packages = await mds.GetCachedPackagesAsync(default);
        packages.ForEach(p => PackageAddedEventHandler(this, p));

        mds.PackageAdded += PackageAddedEventHandler;
        mds.PackageRemoved += PackageRemovedEventHandler;
        mds.TestContainerUpdated += TestContainerUpdatedEventHandler;
    }

    private void UnloadOldWorkspace()
    {
        _tl.L.WriteLine("Unloading workspace at '{0}'.", _currentWorkspace?.Location);
        if (_currentWorkspace == null)
        {
            return;
        }

        var mds = _currentWorkspace.GetService<IMetadataService>();
        mds.TestContainerUpdated -= TestContainerUpdatedEventHandler;
        mds.PackageRemoved -= PackageRemovedEventHandler;
        mds.PackageAdded -= PackageAddedEventHandler;
    }

    private void PackageAddedEventHandler(object sender, Workspace.Package e)
    {
        _tl.L.WriteLine("TCD: Package Added EventHandler: '{0}'", e.ManifestPath);
        GetTestContainers(e).ForEach(c => TestContainerUpdatedEventHandler(this, c));
    }

    private void PackageRemovedEventHandler(object sender, Workspace.Package e)
    {
        _tl.L.WriteLine("TCD: Package Removed EventHandler: '{0}'", e.ManifestPath);
        GetTestContainers(e).ForEach(c => TestContainerUpdatedEventHandler(this, c));
    }

    private void TestContainerUpdatedEventHandler(object sender, PathEx e)
    {
        _tl.L.WriteLine("TCD: TestContainer Updated EventHandler: '{0}'", e);
        if (e.FileExists())
        {
            TryAddTestContainer(e);
        }
        else
        {
            TryRemoveTestContainer(e);
        }

        TestContainersUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void TryAddTestContainer(PathEx container)
    {
        if (!_testContainersCache.TryAdd(container, new TestContainer(container, this, _tl)))
        {
            _tl.L.WriteError("TCD: Failed to add '{0}'", container);
        }
    }

    private void TryRemoveTestContainer(PathEx container)
    {
        if (!_testContainersCache.TryRemove(container, out _))
        {
            _tl.L.WriteError("TCD: Failed to remove container {0}.", container);
        }
    }

    private IEnumerable<PathEx> GetTestContainers(Workspace.Package e)
        => e.GetTestContainers(_currentWorkspace?.GetProfile(e.ManifestPath) ?? e.GetProfiles().First()).Select(x => x.Container);
}
