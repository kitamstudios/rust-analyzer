using System.Linq;
using System.Threading.Tasks;
using ApprovalTests;
using ApprovalTests.Namers;
using ApprovalTests.Reporters;
using ApprovalTests.Reporters.TestFrameworks;
using FluentAssertions;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using KS.RustAnalyzer.Tests.Common;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Moq;
using Xunit;

namespace KS.RustAnalyzer.TestAdapter.UnitTests;

public class TestExecutorTests
{
    private readonly IToolChainService _tcs = new ToolChainService(TestHelpers.TL.T, TestHelpers.TL.L);

    [Theory]
    [InlineData(@"hello_world", "hello_world.rusttests", "bench")] // No tests.
    [InlineData(@"hello_library", "hello_lib.rusttests", "bench")] // Has tests.
    [UseReporter(typeof(XUnit2Reporter))]
    public async Task RunTestsTestsAsync(string workspaceRelRoot, string containerName, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var tps = workspaceRelRoot.GetTestPaths(profile);
        var tcPath = tps.TargetPath + (PathEx)containerName;
        tps.TargetPath.CleanTestContainers();

        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, profile, additionalTestExecutionArguments: "--exclude-should-panic", testExecutionEnvironment: "ENV_VAR_1=ENV_VAR_1_VALUE\0\0");
        var fh = new SpyFrameworkHandle();
        new TestExecutor().RunTests(new[] { (string)tcPath }, Mock.Of<IRunContext>(), fh);

        var normalizedStr = fh.Results
            .OrderBy(x => x.TestCase.FullyQualifiedName).ThenBy(x => x.TestCase.LineNumber)
            .SerializeAndNormalizeObject();
        Approvals.Verify(normalizedStr);
    }

    [Theory]
    [InlineData(@"workspace_with_tests", new[] { "add_one|tests.fibonacci_test.case_2", "adder|tests.it_works_failing", "adder|tests1.tests1.it_works_skipped2" }, "test")]
    public async Task RunSelectedTestsFromMultiplePackagesMultipleFilesTestsAsync(string workspaceRelRoot, string[] tests, string profile)
    {
        NamerFactory.AdditionalInformation = workspaceRelRoot.ReplaceInvalidChars();
        var tps = workspaceRelRoot.GetTestPaths(profile);
        tps.TargetPath.CleanTestContainers();

        var testCases = tests.Select(t => t.Split('|')).Select(x => new TestCase { Source = $"{tps.TargetPath + x[0]}{Constants.TestsContainerExtension}", FullyQualifiedName = x[1], });

        await _tcs.DoBuildAsync(tps.WorkspacePath, tps.ManifestPath, profile);
        var fh = new SpyFrameworkHandle();
        new TestExecutor().RunTests(testCases, Mock.Of<IRunContext>(), fh);

        fh.Results.Select(r => $"{((PathEx)r.TestCase.Source).GetFileNameWithoutExtension()}|{r.DisplayName}").Should().BeEquivalentTo(tests);
    }
}
