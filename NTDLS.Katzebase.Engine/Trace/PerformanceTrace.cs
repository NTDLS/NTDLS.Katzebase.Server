﻿using NTDLS.Katzebase.Client.Payloads;
using NTDLS.Katzebase.Client.Types;
using static NTDLS.Katzebase.Client.KbConstants;

namespace NTDLS.Katzebase.Engine.Trace
{
    internal class PerformanceTrace
    {
        internal KbInsensitiveDictionary<KbMetric> Metrics { get; private set; } = new();

        internal enum PerformanceTraceCumulativeMetricType
        {
            IndexSearch,
            IndexDistillation,
            AcquireTransaction,
            Optimization,
            Lock,
            Sampling,
            CacheRead,
            DeferredWrite,
            DeferredRead,
            CacheWrite,
            IORead,
            IOWrite,
            Serialize,
            Deserialize,
            GetPBuf,
            ThreadCreation,
            ThreadQueue,
            ThreadDeQueue,
            ThreadReady,
            ThreadCompletion,
            Sorting,
            Evaluate,
            Rollback,
            Commit,
            Recording
        }

        internal enum PerformanceTraceDescreteMetricType
        {
            ThreadCount,
            TransactionDuration
        }

        internal PerformanceTraceDurationTracker CreateDurationTracker(PerformanceTraceCumulativeMetricType type)
            => new(this, type, $"{type}");

        internal PerformanceTraceDurationTracker CreateDurationTracker(PerformanceTraceCumulativeMetricType type, string supplementalType)
            => new(this, type, $"{type}:{supplementalType}");

        internal PerformanceTraceDurationTracker CreateDurationTracker<T>(PerformanceTraceCumulativeMetricType type)
            => new(this, type, $"{type}:{typeof(T).Name}");

        public void AccumulateDuration(PerformanceTraceDurationTracker item)
        {

            lock (Metrics)
            {
                if (Metrics.ContainsKey(item.Key))
                {
                    var lookup = Metrics[item.Key];
                    lookup.Value += item.Duration;
                    lookup.Count++;
                }
                else
                {
                    var lookup = new KbMetric(KbMetricType.Cumulative, item.Key, item.Duration);
                    lookup.Count++;
                    Metrics.Add(item.Key, lookup);
                }
            }
        }

        public void AddDescreteMetric(PerformanceTraceDescreteMetricType type, double value)
        {
            lock (Metrics)
            {
                var key = $"{type}";

                if (Metrics.ContainsKey(key))
                {
                    var lookup = Metrics[key];
                    lookup.Value = value;
                    lookup.Count++;
                }
                else
                {
                    var lookup = new KbMetric(KbMetricType.Descrete, key, value);
                    lookup.Count++;
                    Metrics.Add(key, lookup);
                }
            }
        }

        internal KbMetricCollection ToCollection()
        {
            var result = new KbMetricCollection();
            result.AddRange(Metrics.Select(o => o.Value));
            return result;
        }
    }
}
