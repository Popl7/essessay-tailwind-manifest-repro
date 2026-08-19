using System.Diagnostics.Metrics;

namespace Essessay.Services;

/// <summary>
/// What this application knows and no generic instrumentation can see.
///
/// ASP.NET Core and Kestrel already publish request durations, active requests and
/// connection counts through the same <see cref="Meter"/> plumbing — nothing here
/// repeats them. What is missing is the board: a request that stays open for an hour is
/// one request to the built-in meters, and how many are held, whether reconnecting
/// clients are catching up or being resynced from scratch, and how often the limiter
/// turns someone away are the numbers that say whether the backplane is healthy.
///
/// Built on <see cref="IMeterFactory"/>, which is in the framework, so this adds no
/// dependency. `dotnet-counters monitor --counters Essessay` reads it as it stands, and
/// adding an OpenTelemetry exporter later would export it without touching a call site.
/// </summary>
public sealed class EssessayMetrics
{
    public const string MeterName = "Essessay";

    private readonly UpDownCounter<long> _activeStreams;
    private readonly Counter<long> _patchesSent;
    private readonly Counter<long> _resumes;
    private readonly Counter<long> _rejections;

    /// <summary>
    /// Exposed so a test can filter a MeterListener by this exact instance rather than
    /// by name — every host creates its own Meter named "Essessay", so name alone
    /// doesn't distinguish one running app's measurements from another's.
    /// </summary>
    public Meter Meter { get; }

    public EssessayMetrics(IMeterFactory factory)
    {
        var meter = Meter = factory.Create(MeterName);

        _activeStreams = meter.CreateUpDownCounter<long>("essessay.board.streams.active",
            unit: "{stream}", description: "Board SSE streams this instance is holding open.");

        _patchesSent = meter.CreateCounter<long>("essessay.board.patches.sent",
            unit: "{patch}", description: "Element patches written to a client.");

        _resumes = meter.CreateCounter<long>("essessay.board.resumes",
            unit: "{resume}", description: "Drains of the patch log, tagged by whether the cursor still reached.");

        _rejections = meter.CreateCounter<long>("essessay.ratelimit.rejections",
            unit: "{request}", description: "Requests refused by a rate limit policy.");
    }

    public void StreamOpened() => _activeStreams.Add(1);

    public void StreamClosed() => _activeStreams.Add(-1);

    public void PatchSent() => _patchesSent.Add(1);

    /// <summary>
    /// <paramref name="resynced"/> is the interesting half: it means the client was
    /// further behind than the log reaches and had to be sent the whole board. A rising
    /// share of those says the log is too short for how long clients go away.
    /// </summary>
    public void Resumed(bool resynced) =>
        _resumes.Add(1, new KeyValuePair<string, object?>("outcome", resynced ? "resynced" : "replayed"));

    public void Rejected(string policy) =>
        _rejections.Add(1, new KeyValuePair<string, object?>("policy", policy));
}
