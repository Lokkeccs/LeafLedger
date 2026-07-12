using System.Diagnostics.Metrics;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public sealed class ReportingRefreshMetricsTests
{
    [Fact]
    public void Refresh_pass_records_duration_staleness_and_rows_measurements()
    {
        using var services = new ServiceCollection()
            .AddMetrics()
            .BuildServiceProvider();
        using var listener = new MeterListener();
        var measurements = new HashSet<string>(StringComparer.Ordinal);
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "LeafLedger.Reporting")
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.Start();

        var metrics = new ReportingRefreshMetrics(services.GetRequiredService<IMeterFactory>());
        metrics.RecordRefresh(TimeSpan.FromMilliseconds(4), TimeSpan.FromMilliseconds(9), 3);

        Assert.Contains("leafledger.reporting.refresh.duration", measurements);
        Assert.Contains("leafledger.reporting.staleness", measurements);
        Assert.Contains("leafledger.reporting.refresh.rows", measurements);
    }
}