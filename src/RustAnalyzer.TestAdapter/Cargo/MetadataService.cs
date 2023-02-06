using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

// TODO: MS: Implement change detection for the _loadedPackages.
public sealed class MetadataService : IMetadataService, IDisposable
{
    private readonly ICargoService _cargoService;
    private readonly PathEx _workspaceRoot;
    private readonly TL _tl;
    private readonly SemaphoreSlim _loadedPackagesLocker = new (1, 1);
    private IDictionary<PathEx, Workspace.Package> _loadedPackages = new Dictionary<PathEx, Workspace.Package>();
    private bool _disposedValue;

    public MetadataService(ICargoService cargoService, PathEx workspaceRoot, TL tl)
    {
        // TODO: MS: subscribe to file chagne notifications and outdating caches.
        _cargoService = cargoService;
        _workspaceRoot = workspaceRoot;
        _tl = tl;
    }

    public void Dispose()
    {
        // NOTE: Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method.
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public async Task<Workspace.Package> GetPackageAsync(PathEx manifestPath, CancellationToken ct)
    {
        if (_loadedPackages.TryGetValue(manifestPath, out var package))
        {
            return package;
        }

        await _loadedPackagesLocker.WaitAsync(ct);
        try
        {
            if (_loadedPackages.TryGetValue(manifestPath, out package))
            {
                return package;
            }

            return _loadedPackages[manifestPath] = await GetPackageAsyncCore(manifestPath, ct);
        }
        finally
        {
            _loadedPackagesLocker.Release();
        }
    }

    public async Task<Workspace.Package> GetContainingPackageAsync(PathEx filePath, CancellationToken ct)
    {
        if (!filePath.TryGetParentManifestOrThisUnderWorkspace(_workspaceRoot, out PathEx? manifest))
        {
            return null;
        }

        Ensure.That(manifest).IsNotNull();
        return await GetPackageAsync(manifest.Value, ct);
    }

    private async Task<Workspace.Package> GetPackageAsyncCore(PathEx manifestPath, CancellationToken ct)
    {
        // TODO: _workspaceRoot may not have a Cargo.toml file if a folder with multiple workspaces are opened.
        // TODO: w is null when running under the debugger. some timing issue for sure.
        var w = await _cargoService.GetWorkspaceAsync(manifestPath, ct);
        var p = w.Packages.FirstOrDefault(p => p.ManifestPath == manifestPath);
        if (p != null)
        {
            return p;
        }

        // NOTE: Means this is the root Workspace Cargo.toml.
        var p1 =
            new Workspace.Package
            {
                ManifestPath = manifestPath,
                Name = "<root>",
            };
        w.Packages.Add(p1);
        return p1;
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // NOTE: Dispose managed state (managed objects).
            }

            _loadedPackages = null;
            _disposedValue = true;
        }
    }
}