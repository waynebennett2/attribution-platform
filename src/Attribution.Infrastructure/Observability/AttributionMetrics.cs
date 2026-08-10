using System.Diagnostics.Metrics;

namespace Attribution.Infrastructure.Observability;

// FR-041: operational metrics (ingestion lag, allocation failures, attribution match
// rate, API latency — Constitution Principle VII) sufficient to trace a call end to end.
// Uses the .NET built-in System.Diagnostics.Metrics API rather than adding an OpenTelemetry
// SDK dependency now: any OTel-compatible collector or `dotnet-counters` can attach to a
// named Meter without further code here — exporter wiring is a deployment-time concern.
public static class AttributionMetrics
{
    public const string MeterName = "AttributionPlatform";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> AllocationSuccesses =
        Meter.CreateCounter<long>("attribution.allocation.successes", description: "FR-003 successful allocations");

    public static readonly Counter<long> AllocationFailures =
        Meter.CreateCounter<long>("attribution.allocation.failures", description: "FR-011 fallback-to-default events");

    public static readonly Histogram<double> AllocationLatencyMs =
        Meter.CreateHistogram<double>("attribution.allocation.latency_ms", unit: "ms", description: "SC-004 allocation latency");

    public static readonly Counter<long> CallsAttributed =
        Meter.CreateCounter<long>("attribution.calls.attributed", description: "FR-018 attributed calls");

    public static readonly Counter<long> CallsUnattributed =
        Meter.CreateCounter<long>("attribution.calls.unattributed", description: "FR-020 unattributed calls");

    public static readonly Counter<long> CallsAmbiguous =
        Meter.CreateCounter<long>("attribution.calls.ambiguous", description: "FR-021 ambiguous calls");

    public static readonly Counter<long> IngestionErrors =
        Meter.CreateCounter<long>("attribution.ingestion.errors", description: "FR-016 poll/upsert failures");

    public static readonly Counter<long> PublicationFailures =
        Meter.CreateCounter<long>("attribution.publication.failures", description: "FR-028 failed publication attempts");
}
