using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace KS.RustAnalyzer.Editor;

public class FileScanner : IFileScanner, IFileScannerUpToDateCheck
{
    private readonly IMetadataService _mds;

    public FileScanner(IMetadataService mds)
    {
        _mds = mds;
    }

    public async Task<T> ScanContentAsync<T>(string filePath, CancellationToken cancellationToken)
        where T : class
    {
        var package = await _mds.GetContainingPackageAsync((PathEx)filePath, cancellationToken);
        if (package == null)
        {
            return null;
        }

        if (typeof(T) == FileScannerTypeConstants.FileDataValuesType)
        {
            var ret = GetFileDataValues(package, (PathEx)filePath);
            return await Task.FromResult((T)(IReadOnlyCollection<FileDataValue>)ret);
        }
        else if (typeof(T) == FileScannerTypeConstants.FileReferenceInfoType)
        {
            var ret = GetFileReferenceInfos(package, (PathEx)filePath);
            return await Task.FromResult((T)(IReadOnlyCollection<FileReferenceInfo>)ret);
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    public virtual async Task<bool> IsUpToDateAsync(DateTimeOffset? lastScanTimestamp, string filePath, FileScannerType scannerType, CancellationToken cancellationToken)
    {
        if (await IsValidFileAsync(filePath))
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(filePath);
                return lastScanTimestamp.HasValue && lastWrite < lastScanTimestamp.Value.UtcDateTime;
            }
            catch (Exception exc) when (exc is IOException || exc is UnauthorizedAccessException)
            {
                // We have already loaded the file in VS,
                // so any I/O related exceptions are very unlikely
                // and we def. don't want to crash VS on that.
            }
        }

        return false;
    }

    private Task<bool> IsValidFileAsync(string filePath)
    {
        var ext = ((PathEx)filePath).GetExtension();
        return (ext.Equals(Constants.RustFileExtension) || ext.Equals(Constants.ManifestFileExtension)).ToTask();
    }

    private List<FileDataValue> GetFileDataValues(Workspace.Package package, PathEx filePath)
    {
        var allFileDataValues = new List<FileDataValue>();

        // For binaries.
        if (package.ManifestPath == filePath)
        {
            if (package.IsPackage)
            {
                foreach (var target in package.GetTargets().Where(t => t.IsRunnable))
                {
                    var launchSettings = new PropertySettings
                    {
                        [LaunchConfigurationConstants.NameKey] = target.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.DebugTypeKey] = LaunchConfigurationConstants.NativeOptionKey,
                        [LaunchConfigurationConstants.ProjectKey] = (string)package.FullPath,
                        [LaunchConfigurationConstants.ProjectTargetKey] = target.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.ProgramKey] = (string)package.FullPath,
                    };

                    allFileDataValues.Add(
                        new FileDataValue(
                            type: DebugLaunchActionContext.ContextTypeGuid,
                            name: DebugLaunchActionContext.IsDefaultStartupProjectEntry,
                            value: launchSettings,
                            target: null,
                            context: null));

                    var fileDataValuesForAllProfiles1 = package.GetProfiles().Select(
                        profile =>
                            new FileDataValue(
                                type: BuildConfigurationContext.ContextTypeGuid,
                                name: BuildConfigurationContext.DataValueName,
                                value: null,
                                target: target.GetPath(profile),
                                context: profile));

                    allFileDataValues.AddRange(fileDataValuesForAllProfiles1);
                }
            }

            var fileDataValuesForAllProfiles = package.GetProfiles().Select(
            profile =>
                new FileDataValue(
                    type: BuildConfigurationContext.ContextTypeGuid,
                    name: BuildConfigurationContext.DataValueName,
                    value: null,
                    target: null,
                    context: profile));

            allFileDataValues.AddRange(fileDataValuesForAllProfiles);
        }

        // For examples.
        var forExamples = package.GetTargets()
            .Where(t => t.IsExample())
            .Where(t => t.SourcePath == filePath)
            .SelectMany(
                t =>
                {
                    var allFileDataValues = new List<FileDataValue>();

                    var launchSettings = new PropertySettings
                    {
                        [LaunchConfigurationConstants.NameKey] = t.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.DebugTypeKey] = LaunchConfigurationConstants.NativeOptionKey,
                        [LaunchConfigurationConstants.ProjectKey] = (string)t.SourcePath,
                        [LaunchConfigurationConstants.ProjectTargetKey] = t.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.ProgramKey] = (string)package.FullPath,
                    };

                    allFileDataValues.Add(
                        new FileDataValue(
                            type: DebugLaunchActionContext.ContextTypeGuid,
                            name: DebugLaunchActionContext.IsDefaultStartupProjectEntry,
                            value: launchSettings,
                            target: null,
                            context: null));

                    var fileDataValuesForAllProfiles1 = package.GetProfiles().Select(
                        profile =>
                            new FileDataValue(
                                type: BuildConfigurationContext.ContextTypeGuid,
                                name: BuildConfigurationContext.DataValueName,
                                value: null,
                                target: t.GetPath(profile),
                                context: profile));

                    allFileDataValues.AddRange(fileDataValuesForAllProfiles1);

                    var fileDataValuesForAllProfiles = package.GetProfiles().Select(
                    profile =>
                        new FileDataValue(
                            type: BuildConfigurationContext.ContextTypeGuid,
                            name: BuildConfigurationContext.DataValueName,
                            value: null,
                            target: null,
                            context: profile));

                    allFileDataValues.AddRange(fileDataValuesForAllProfiles);
                    return allFileDataValues;
                });

        allFileDataValues.AddRange(forExamples);

        return allFileDataValues;
    }

    private static List<FileReferenceInfo> GetFileReferenceInfos(Workspace.Package package, PathEx filePath)
    {
        var allFileRefInfos = new List<FileReferenceInfo>();

        // For binaries.
        if (package.ManifestPath == filePath && package.IsPackage)
        {
            var targets = package.GetTargets();
            var refInfos = package.GetProfiles()
                .SelectMany(p => targets.Select(t => (Target: t, Profile: p)))
                .Where(x => !x.Target.IsExample())
                .Select(x =>
                    new FileReferenceInfo(
                        relativePath: x.Target.GetPathRelativeTo(x.Profile, filePath),
                        target: x.Target.GetPath(x.Profile),
                        context: x.Profile,
                        referenceType: (int)FileReferenceInfoType.Output));

            allFileRefInfos.AddRange(refInfos);
        }

        // For examples.
        var forExamples = package.GetTargets()
            .Where(t => t.IsExample())
            .Where(t => t.SourcePath == filePath)
            .SelectMany(t => package.GetProfiles().Select(p => (Target: t, Profile: p)))
            .Select(x =>
                new FileReferenceInfo(
                    relativePath: x.Target.GetPathRelativeTo(x.Profile, filePath),
                    target: x.Target.GetPath(x.Profile),
                    context: x.Profile,
                    referenceType: (int)FileReferenceInfoType.Output));

        allFileRefInfos.AddRange(forExamples);

        return allFileRefInfos;
    }
}
