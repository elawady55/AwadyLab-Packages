using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace SampleApp.ServiceB.Metrics;

/// <summary>
/// Thread-safe metrics collector tracking execution counts, latencies, retries, and failures
/// for notifications processed in Service B. Also integrates with <see cref="Meter"/> for OpenTelemetry.
/// </summary>
public sealed class ServiceBMetrics
{
    private static readonly Meter Meter = new("SampleApp.ServiceB.Mediator", "1.0.0");

    private static readonly Counter<long> ExecutionsCounter = Meter.CreateCounter<long>(
        "mediator_executions_total", "count", "Total notifications processed");

    private static readonly Counter<long> RetriesCounter = Meter.CreateCounter<long>(
        "mediator_retries_total", "count", "Total retry attempts recorded");

    private static readonly Counter<long> DeadLettersCounter = Meter.CreateCounter<long>(
        "mediator_dead_letters_total", "count", "Total notifications routed to dead-letter sink");

    private static readonly Histogram<double> DurationHistogram = Meter.CreateHistogram<double>(
        "mediator_execution_duration_ms", "ms", "Notification handler execution duration");

    private static readonly UpDownCounter<long> InFlightCounter = Meter.CreateUpDownCounter<long>(
        "mediator_in_flight", "count", "Active notifications currently in-flight");

    private long _totalExecutions;
    private long _successfulExecutions;
    private long _failedExecutions;
    private long _retryAttempts;
    private long _deadLetteredExecutions;
    private long _inFlightExecutions;

    private readonly object _timingLock = new();
    private double _totalDurationMs;
    private double _minDurationMs = double.MaxValue;
    private double _maxDurationMs;

    private readonly ConcurrentDictionary<string, NotificationTypeMetrics> _byNotification = new();

    public void RecordExecutionStart(string notificationType)
    {
        Interlocked.Increment(ref _inFlightExecutions);
        InFlightCounter.Add(1, new KeyValuePair<string, object?>("notification_type", notificationType));
    }

    public void RecordExecutionCompleted(string notificationType, double durationMs, bool success)
    {
        Interlocked.Decrement(ref _inFlightExecutions);
        Interlocked.Increment(ref _totalExecutions);

        if (success)
            Interlocked.Increment(ref _successfulExecutions);
        else
            Interlocked.Increment(ref _failedExecutions);

        lock (_timingLock)
        {
            _totalDurationMs += durationMs;
            if (durationMs < _minDurationMs) _minDurationMs = durationMs;
            if (durationMs > _maxDurationMs) _maxDurationMs = durationMs;
        }

        var status = success ? "success" : "failure";
        InFlightCounter.Add(-1, new KeyValuePair<string, object?>("notification_type", notificationType));
        ExecutionsCounter.Add(1,
            new KeyValuePair<string, object?>("notification_type", notificationType),
            new KeyValuePair<string, object?>("status", status));
        DurationHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("notification_type", notificationType),
            new KeyValuePair<string, object?>("status", status));

        var stats = _byNotification.GetOrAdd(notificationType, _ => new NotificationTypeMetrics());
        stats.RecordExecution(durationMs, success);
    }

    public void RecordRetry(string notificationType, int attempt, bool willRetry)
    {
        Interlocked.Increment(ref _retryAttempts);
        RetriesCounter.Add(1,
            new KeyValuePair<string, object?>("notification_type", notificationType),
            new KeyValuePair<string, object?>("attempt", attempt));

        var stats = _byNotification.GetOrAdd(notificationType, _ => new NotificationTypeMetrics());
        stats.RecordRetry();
    }

    public void RecordDeadLetter(string notificationType)
    {
        Interlocked.Increment(ref _deadLetteredExecutions);
        DeadLettersCounter.Add(1, new KeyValuePair<string, object?>("notification_type", notificationType));

        var stats = _byNotification.GetOrAdd(notificationType, _ => new NotificationTypeMetrics());
        stats.RecordDeadLetter();
    }

    public MetricsSnapshot GetSnapshot()
    {
        long total = Interlocked.Read(ref _totalExecutions);
        long success = Interlocked.Read(ref _successfulExecutions);
        long failed = Interlocked.Read(ref _failedExecutions);
        long retries = Interlocked.Read(ref _retryAttempts);
        long deadLetter = Interlocked.Read(ref _deadLetteredExecutions);
        long inFlight = Interlocked.Read(ref _inFlightExecutions);

        double totalDur, minDur, maxDur;
        lock (_timingLock)
        {
            totalDur = _totalDurationMs;
            minDur = _minDurationMs == double.MaxValue ? 0 : _minDurationMs;
            maxDur = _maxDurationMs;
        }

        var avgDur = total > 0 ? totalDur / total : 0;
        var successRate = total > 0 ? Math.Round((double)success / total * 100.0, 2) : 100.0;

        var perNotification = _byNotification.ToDictionary(
            k => k.Key,
            v => v.Value.GetSnapshot()
        );

        return new MetricsSnapshot
        {
            Summary = new MetricsSummary
            {
                TotalExecutions = total,
                SuccessfulExecutions = success,
                FailedExecutions = failed,
                RetryAttempts = retries,
                DeadLetteredExecutions = deadLetter,
                InFlightExecutions = inFlight,
                SuccessRatePercentage = successRate,
                AverageExecutionTimeMs = Math.Round(avgDur, 2),
                MinExecutionTimeMs = Math.Round(minDur, 2),
                MaxExecutionTimeMs = Math.Round(maxDur, 2),
                TotalExecutionTimeMs = Math.Round(totalDur, 2)
            },
            ByNotification = perNotification
        };
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalExecutions, 0);
        Interlocked.Exchange(ref _successfulExecutions, 0);
        Interlocked.Exchange(ref _failedExecutions, 0);
        Interlocked.Exchange(ref _retryAttempts, 0);
        Interlocked.Exchange(ref _deadLetteredExecutions, 0);
        Interlocked.Exchange(ref _inFlightExecutions, 0);

        lock (_timingLock)
        {
            _totalDurationMs = 0;
            _minDurationMs = double.MaxValue;
            _maxDurationMs = 0;
        }

        _byNotification.Clear();
    }
}

public sealed class NotificationTypeMetrics
{
    private long _total;
    private long _success;
    private long _failed;
    private long _retries;
    private long _deadLetter;

    private readonly object _lock = new();
    private double _totalDur;
    private double _minDur = double.MaxValue;
    private double _maxDur;
    private DateTime? _lastExecutionUtc;

    public void RecordExecution(double durationMs, bool success)
    {
        Interlocked.Increment(ref _total);
        if (success)
            Interlocked.Increment(ref _success);
        else
            Interlocked.Increment(ref _failed);

        lock (_lock)
        {
            _totalDur += durationMs;
            if (durationMs < _minDur) _minDur = durationMs;
            if (durationMs > _maxDur) _maxDur = durationMs;
            _lastExecutionUtc = DateTime.UtcNow;
        }
    }

    public void RecordRetry() => Interlocked.Increment(ref _retries);

    public void RecordDeadLetter() => Interlocked.Increment(ref _deadLetter);

    public NotificationMetricsSnapshot GetSnapshot()
    {
        long total = Interlocked.Read(ref _total);
        long success = Interlocked.Read(ref _success);
        long failed = Interlocked.Read(ref _failed);
        long retries = Interlocked.Read(ref _retries);
        long deadLetter = Interlocked.Read(ref _deadLetter);

        double totalDur, minDur, maxDur;
        DateTime? lastUtc;
        lock (_lock)
        {
            totalDur = _totalDur;
            minDur = _minDur == double.MaxValue ? 0 : _minDur;
            maxDur = _maxDur;
            lastUtc = _lastExecutionUtc;
        }

        var avg = total > 0 ? totalDur / total : 0;

        return new NotificationMetricsSnapshot
        {
            Total = total,
            Success = success,
            Failed = failed,
            RetryAttempts = retries,
            DeadLettered = deadLetter,
            AverageExecutionTimeMs = Math.Round(avg, 2),
            MinExecutionTimeMs = Math.Round(minDur, 2),
            MaxExecutionTimeMs = Math.Round(maxDur, 2),
            LastExecutionUtc = lastUtc
        };
    }
}

public sealed record MetricsSnapshot
{
    public required MetricsSummary Summary { get; init; }
    public required Dictionary<string, NotificationMetricsSnapshot> ByNotification { get; init; }
}

public sealed record MetricsSummary
{
    public required long TotalExecutions { get; init; }
    public required long SuccessfulExecutions { get; init; }
    public required long FailedExecutions { get; init; }
    public required long RetryAttempts { get; init; }
    public required long DeadLetteredExecutions { get; init; }
    public required long InFlightExecutions { get; init; }
    public required double SuccessRatePercentage { get; init; }
    public required double AverageExecutionTimeMs { get; init; }
    public required double MinExecutionTimeMs { get; init; }
    public required double MaxExecutionTimeMs { get; init; }
    public required double TotalExecutionTimeMs { get; init; }
}

public sealed record NotificationMetricsSnapshot
{
    public required long Total { get; init; }
    public required long Success { get; init; }
    public required long Failed { get; init; }
    public required long RetryAttempts { get; init; }
    public required long DeadLettered { get; init; }
    public required double AverageExecutionTimeMs { get; init; }
    public required double MinExecutionTimeMs { get; init; }
    public required double MaxExecutionTimeMs { get; init; }
    public required DateTime? LastExecutionUtc { get; init; }
}
