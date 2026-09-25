// Measures what each committed goods save costs, so the JSON-payload-or-relational-tables question (decision 0012) is decided
// from numbers. Report-only and in memory: nothing here is persisted or changes save behaviour. Only saves that write a new
// revision count; a save that fails, rolls back, or finds its revision already stored is not recorded. Phase time excludes
// waits for the save lock or another writer. COMMIT includes SQLite bookkeeping and the WAL disk sync, not just fsync.
using System.Globalization;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    public readonly struct GoodsSaveTimings
    {
        public readonly double CopyMilliseconds;
        public readonly double ValidationMilliseconds;
        public readonly double JsonMilliseconds;
        public readonly double TransactionMilliseconds;
        public readonly double CommitAndSyncMilliseconds;

        public GoodsSaveTimings(double copy, double validation, double json, double transaction, double commitAndSync)
        {
            CopyMilliseconds = copy;
            ValidationMilliseconds = validation;
            JsonMilliseconds = json;
            TransactionMilliseconds = transaction;
            CommitAndSyncMilliseconds = commitAndSync;
        }

        public double TotalMilliseconds => CopyMilliseconds + ValidationMilliseconds + JsonMilliseconds
            + TransactionMilliseconds + CommitAndSyncMilliseconds;
    }

    public sealed class GoodsCommitStats
    {
        // Decision 0012 revisit signals: past either, the whole-world payload should move to relational tables.
        public const double SlowCommitMilliseconds = 50;
        public const int LargePayloadBytes = 1024 * 1024;

        private readonly object _gate = new();
        private long _commits;
        private double _totalMilliseconds;
        private double _maxMilliseconds;
        private double _lastMilliseconds;
        private int _lastPayloadBytes;
        private GoodsSaveTimings _lastTimings;
        private bool _warnedSlow;
        private bool _warnedLarge;

        public long Commits { get { lock (_gate) return _commits; } }
        public double LastMilliseconds { get { lock (_gate) return _lastMilliseconds; } }
        public double MaxMilliseconds { get { lock (_gate) return _maxMilliseconds; } }
        public double AverageMilliseconds { get { lock (_gate) return _commits == 0 ? 0 : _totalMilliseconds / _commits; } }
        public int LastPayloadBytes { get { lock (_gate) return _lastPayloadBytes; } }
        public GoodsSaveTimings LastTimings { get { lock (_gate) return _lastTimings; } }

        // The server calls this when it starts serving a world, so the counters and one-time warnings describe that world
        // only, not saves made earlier in the same process (Editor tests, a previous session).
        public void Reset()
        {
            lock (_gate)
            {
                _commits = 0;
                _totalMilliseconds = _maxMilliseconds = _lastMilliseconds = 0;
                _lastPayloadBytes = 0;
                _lastTimings = default;
                _warnedSlow = _warnedLarge = false;
            }
        }

        internal void Record(GoodsSaveTimings timings, int payloadBytes)
        {
            lock (_gate)
            {
                var milliseconds = timings.TotalMilliseconds;
                _commits++;
                _totalMilliseconds += milliseconds;
                if (milliseconds > _maxMilliseconds) _maxMilliseconds = milliseconds;
                _lastMilliseconds = milliseconds;
                _lastPayloadBytes = payloadBytes;
                _lastTimings = timings;
                // Once per session each, so a slow disk or a large world does not flood the log.
                if (!_warnedSlow && milliseconds > SlowCommitMilliseconds)
                {
                    _warnedSlow = true;
                    Debug.LogWarning($"[Goods] A world commit took {milliseconds:F1} ms (> {SlowCommitMilliseconds} ms); see decision 0012.");
                }
                if (!_warnedLarge && payloadBytes > LargePayloadBytes)
                {
                    _warnedLarge = true;
                    Debug.LogWarning($"[Goods] The world payload is {payloadBytes / 1024} KB (> {LargePayloadBytes / 1024} KB); see decision 0012.");
                }
            }
        }

        public string Summary()
        {
            lock (_gate)
                return string.Format(CultureInfo.InvariantCulture, "[Goods] commits={0} avg={1:F1}ms max={2:F1}ms payload={3:F1}KB",
                    _commits, _commits == 0 ? 0 : _totalMilliseconds / _commits, _maxMilliseconds, _lastPayloadBytes / 1024.0);
        }
    }
}
