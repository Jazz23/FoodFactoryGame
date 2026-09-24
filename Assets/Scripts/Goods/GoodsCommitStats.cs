// Measures what each committed goods save costs, so the JSON-payload-or-relational-tables question (decision 0012) is decided
// from numbers. Report-only and in memory: nothing here is persisted or changes save behaviour. Only commits count; a save
// that fails or rolls back is not recorded.
using System.Globalization;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
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
        private bool _warnedSlow;
        private bool _warnedLarge;

        public long Commits { get { lock (_gate) return _commits; } }
        public double LastMilliseconds { get { lock (_gate) return _lastMilliseconds; } }
        public double MaxMilliseconds { get { lock (_gate) return _maxMilliseconds; } }
        public double AverageMilliseconds { get { lock (_gate) return _commits == 0 ? 0 : _totalMilliseconds / _commits; } }
        public int LastPayloadBytes { get { lock (_gate) return _lastPayloadBytes; } }

        internal void Record(double milliseconds, int payloadBytes)
        {
            lock (_gate)
            {
                _commits++;
                _totalMilliseconds += milliseconds;
                if (milliseconds > _maxMilliseconds) _maxMilliseconds = milliseconds;
                _lastMilliseconds = milliseconds;
                _lastPayloadBytes = payloadBytes;
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
