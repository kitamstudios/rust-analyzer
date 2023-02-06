using System;
using System.Diagnostics;
using EnsureThat;

namespace KS.RustAnalyzer.TestAdapter.Common;

[DebuggerDisplay("{_path}")]
public readonly struct PathEx : IEquatable<PathEx>
{
    public const StringComparison DefaultComparison = StringComparison.OrdinalIgnoreCase;
    public static readonly StringComparer DefaultComparer = StringComparer.OrdinalIgnoreCase;

    private readonly string _path;

    public PathEx(string path)
    {
        EnsureArg.IsNotNull(path, nameof(path));

        _path = path.Replace("/", @"\");
    }

    // TODO: MS: make his explicit otherwise it is randomly opting into string functions.
    public static implicit operator string(PathEx p) => p._path;

    // NOTE: No implicit cast as it can throw.
    // TODO: MS: Should this be an implicit nullable?
    public static explicit operator PathEx(string p) => new (p);

    public static bool operator ==(PathEx left, PathEx right) => left.Equals(right);

    public static bool operator !=(PathEx left, PathEx right) => !(left == right);

    public override bool Equals(object obj) => obj is PathEx p && Equals(p);

    public override int GetHashCode()
    {
        return 2090457805 + DefaultComparer.GetHashCode(_path);
    }

    public override string ToString() => _path.ToUpperInvariant();

    public bool Equals(PathEx other)
    {
        return _path.Equals(other._path, DefaultComparison);
    }
}
