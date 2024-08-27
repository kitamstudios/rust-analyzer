using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace KS.RustAnalyzer.TestAdapter.Common;

public interface ITelemetryService
{
    void TrackEvent(string eventName, params (string Key, string Value)[] properties);

    void TrackException(Exception e, [CallerMemberName] string siteName = null);

    void TrackException(Exception e, (string Key, string Value)[] properties, [CallerMemberName] string siteName = null);
}

[Export(typeof(ITelemetryService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class TelemetryService : ITelemetryService
{
    private readonly TelemetryClient _telemetryClient;

    public TelemetryService()
    {
        var configuration = TelemetryConfiguration.CreateDefault();
        var builder = configuration.DefaultTelemetrySink.TelemetryProcessorChainBuilder;
        builder.Use((next) => new FilterTelemetryProcessor(next));
        builder.Build();

        configuration.TelemetryInitializers.Add(new DefaultPropertiesTelemetryInitializer());
        configuration.ConnectionString = "InstrumentationKey=e2b04606-c5f2-41e5-b906-c6310a0c1900;IngestionEndpoint=https://eastus-8.in.applicationinsights.azure.com/;LiveEndpoint=https://eastus.livediagnostics.monitor.azure.com/";
        _telemetryClient = new TelemetryClient(configuration);
    }

    public void TrackEvent(string eventName, params (string Key, string Value)[] properties)
    {
        var propsDict = CreatePropsDict(properties);
        _telemetryClient.TrackEvent(eventName, propsDict);
    }

    public void TrackException(Exception e, [CallerMemberName] string siteName = null)
    {
        var propsDict = CreatePropsDict(new[] { ("Site", siteName) });
        _telemetryClient.TrackException(e, propsDict);
    }

    public void TrackException(Exception e, (string Key, string Value)[] properties, [CallerMemberName] string siteName = null)
    {
        var propsDict = CreatePropsDict(properties.Concat(new[] { ("Site", siteName) }).ToArray());
        _telemetryClient.TrackException(e, propsDict);
    }

    private static Dictionary<string, string> CreatePropsDict((string Key, string Value)[] properties)
    {
        return properties.Aggregate(
            new Dictionary<string, string>(),
            (acc, e) =>
            {
                acc.Add(e.Key, e.Value);
                return acc;
            });
    }

    public class DefaultPropertiesTelemetryInitializer : ITelemetryInitializer
    {
        private static readonly string SessionId = Guid.NewGuid().ToString();

        public void Initialize(ITelemetry telemetry)
        {
            telemetry.Context.Component.Version = Vsix.Version;
            telemetry.Context.User.Id = Environment.ExpandEnvironmentVariables("%USERNAME%@%COMPUTERNAME%.%USERDOMAIN%");
            telemetry.Context.Session.Id = SessionId;
        }
    }

    public class FilterTelemetryProcessor : ITelemetryProcessor
    {
        public FilterTelemetryProcessor(ITelemetryProcessor next)
        {
            Next = next;
        }

        private ITelemetryProcessor Next { get; set; }

        public void Process(ITelemetry item)
        {
            if (IsExperimentalInstance() || IsTelemetryDisabled())
            {
                return;
            }

            Next.Process(item);
        }

        private static bool IsExperimentalInstance()
        {
            var env = System.Diagnostics.Process.GetCurrentProcess().StartInfo.Environment;
            return env.TryGetValue("VSROOTSUFFIX", out string rootSuffix) && rootSuffix.Equals("exp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTelemetryDisabled()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUSTANALYZER_TELEMETRY_DISABLED"));
        }
    }
}
