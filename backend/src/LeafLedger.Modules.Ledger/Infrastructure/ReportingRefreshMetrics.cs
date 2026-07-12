using System.Diagnostics.Metrics;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public sealed class ReportingRefreshMetrics
{
    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _staleness;
    private readonly Counter<long> _rows;

    public ReportingRefreshMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("LeafLedger.Reporting");
        _duration = meter.CreateHistogram<double>("leafledger.reporting.refresh.duration", "ms");
        _staleness = meter.CreateHistogram<double>("leafledger.reporting.staleness", "ms");
        _rows = meter.CreateCounter<long>("leafledger.reporting.refresh.rows", "rows");
    }

    public void RecordRefresh(TimeSpan duration, TimeSpan staleness, long rows)
    {
        _duration.Record(duration.TotalMilliseconds);
        _staleness.Record(staleness.TotalMilliseconds);
        _rows.Add(rows);
    }
}