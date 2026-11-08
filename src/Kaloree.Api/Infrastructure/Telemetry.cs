using System.Diagnostics.Metrics;

namespace Kaloree.Api.Infrastructure;

public static class Telemetry
{
    public const string MeterName = "Kaloree.Backend";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    public static readonly Histogram<double> RequestMilliseconds = Meter.CreateHistogram<double>("kaloree.http.duration", "ms");
    public static readonly Counter<long> PhotoAttempts = Meter.CreateCounter<long>("kaloree.photo.attempts");
    public static readonly Counter<long> PhotoFailures = Meter.CreateCounter<long>("kaloree.photo.failures");
}
