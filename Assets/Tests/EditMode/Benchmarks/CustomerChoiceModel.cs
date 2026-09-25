// PROTOTYPE, test-only: a candidate server-side model of individual customer choice (GDD section 23) used to measure its
// cost at the section 28 target of 1,000 customers. Customers are plain arrays, score nearby restaurants only when they get
// hungry (or give up on a queue), and never touch GameObjects, pathing, networking or persistence. Every value here is
// synthetic; this is not a gameplay contract, and the real customer system remains undecided.
using System;

namespace FoodFactoryGame.Benchmarks.Tests
{
    // Which wait time a deciding customer sees: the queue right now, or an estimate each restaurant publishes periodically.
    public enum WaitSignal { Live, Published }

    // How a customer turns scores into a choice: always the best, or randomly weighted by score (multinomial logit).
    public enum ChoiceRule { BestScore, Logit }

    public sealed class CustomerChoiceSettings
    {
        public int Customers = 1000;
        public int Restaurants = 20;
        public int Cuisines = 5;
        public float WorldMetres = 2000f;
        public float NearbyMetres = 600f;
        public float TravelMetresPerSecond = 5f;
        public float TickSeconds = 0.1f;
        public float PublishSeconds = 5f;
        public WaitSignal Wait = WaitSignal.Published;
        public ChoiceRule Rule = ChoiceRule.Logit;
        public float Temperature = 1f;
        public bool LunchRush;
        public ulong Seed = 12345;
    }

    public sealed class CustomerChoiceModel
    {
        private const byte Home = 0;
        private const byte Travelling = 1;
        private const byte Queued = 2;
        private const byte Ordering = 3;
        private const byte Eating = 4;

        private readonly CustomerChoiceSettings _settings;
        private ulong _rng;
        private double _now;
        private double _nextPublish;

        // Customers.
        private readonly float[] _cx, _cy, _priceSensitivity, _patience;
        private readonly float[] _likes; // [customer * cuisines + cuisine], 0..1
        private readonly byte[] _state;
        private readonly double[] _until; // hungry at, arrival, queue deadline, order ready, or finished eating
        private readonly int[] _target;
        private readonly int[] _ticket;

        // Restaurants.
        private readonly float[] _rx, _ry, _reputation, _serviceSeconds, _publishedWait;
        private readonly int[] _cuisine, _priceCents, _servers, _inService, _activeQueue, _served, _peakQueue;
        private readonly long[] _revenueCents;

        // One ring per restaurant of (customer, ticket). Abandoned entries are skipped lazily when they reach the head.
        private readonly int[] _ringCustomer, _ringTicket, _ringHead, _ringCount;
        private readonly int _ringCapacity;

        // Scratch for a decision, sized once so a tick does not allocate.
        private readonly int[] _candidates;
        private readonly double[] _weights;

        public long Decisions { get; private set; }
        public long Evaluations { get; private set; }
        public long Abandoned { get; private set; }
        public long StayedHome { get; private set; }
        public double Now => _now;

        public CustomerChoiceModel(CustomerChoiceSettings settings)
        {
            _settings = settings;
            _rng = settings.Seed == 0 ? 1UL : settings.Seed;
            var n = settings.Customers;
            var m = settings.Restaurants;

            _cx = new float[n];
            _cy = new float[n];
            _priceSensitivity = new float[n];
            _patience = new float[n];
            _likes = new float[n * settings.Cuisines];
            _state = new byte[n];
            _until = new double[n];
            _target = new int[n];
            _ticket = new int[n];

            _rx = new float[m];
            _ry = new float[m];
            _reputation = new float[m];
            _serviceSeconds = new float[m];
            _publishedWait = new float[m];
            _cuisine = new int[m];
            _priceCents = new int[m];
            _servers = new int[m];
            _inService = new int[m];
            _activeQueue = new int[m];
            _served = new int[m];
            _peakQueue = new int[m];
            _revenueCents = new long[m];

            _ringCapacity = n + 1;
            _ringCustomer = new int[m * _ringCapacity];
            _ringTicket = new int[m * _ringCapacity];
            _ringHead = new int[m];
            _ringCount = new int[m];

            _candidates = new int[m];
            _weights = new double[m];

            for (var r = 0; r < m; r++)
            {
                _rx[r] = NextFloat() * settings.WorldMetres;
                _ry[r] = NextFloat() * settings.WorldMetres;
                _reputation[r] = NextFloat();
                _serviceSeconds[r] = 10f + 20f * NextFloat();
                _cuisine[r] = (int)(NextFloat() * settings.Cuisines);
                _priceCents[r] = 500 + (int)(1500 * NextFloat());
                _servers[r] = 1 + (int)(4 * NextFloat());
            }

            for (var c = 0; c < n; c++)
            {
                _cx[c] = NextFloat() * settings.WorldMetres;
                _cy[c] = NextFloat() * settings.WorldMetres;
                _priceSensitivity[c] = 0.5f + NextFloat();
                _patience[c] = 60f + 240f * NextFloat();
                for (var k = 0; k < settings.Cuisines; k++)
                    _likes[c * settings.Cuisines + k] = NextFloat();
                _state[c] = Home;
                _until[c] = settings.LunchRush ? 0 : 600 * NextFloat();
                _target[c] = -1;
            }
        }

        // Advances the world by one server tick. Allocation-free once constructed.
        public void Step()
        {
            _now += _settings.TickSeconds;
            var n = _settings.Customers;

            for (var c = 0; c < n; c++)
            {
                if (_until[c] > _now)
                    continue;

                switch (_state[c])
                {
                    case Home:
                        Decide(c);
                        break;
                    case Travelling:
                        Enqueue(c, _target[c]);
                        break;
                    case Queued:
                        // Out of patience: leave the queue and choose again shortly.
                        _activeQueue[_target[c]]--;
                        _state[c] = Home;
                        _target[c] = -1;
                        _until[c] = _now + 30;
                        Abandoned++;
                        break;
                    case Ordering:
                        var r = _target[c];
                        _revenueCents[r] += _priceCents[r];
                        _served[r]++;
                        _inService[r]--;
                        _state[c] = Eating;
                        _until[c] = _now + 60 + 240 * NextFloat();
                        break;
                    case Eating:
                        _state[c] = Home;
                        _target[c] = -1;
                        _until[c] = _now + 120 + 480 * NextFloat();
                        break;
                }
            }

            for (var r = 0; r < _settings.Restaurants; r++)
                StartService(r);

            if (_now >= _nextPublish)
            {
                for (var r = 0; r < _settings.Restaurants; r++)
                    _publishedWait[r] = LiveWait(r);
                _nextPublish = _now + _settings.PublishSeconds;
            }
        }

        private void Decide(int c)
        {
            Decisions++;
            var s = _settings;
            var nearbySq = s.NearbyMetres * s.NearbyMetres;
            var count = 0;
            var best = -1;
            var bestUtility = 0.0; // staying home scores 0
            var total = 1.0; // exp(0) for staying home

            for (var r = 0; r < s.Restaurants; r++)
            {
                var dx = _rx[r] - _cx[c];
                var dy = _ry[r] - _cy[c];
                var distSq = dx * dx + dy * dy;
                if (distSq > nearbySq)
                    continue;

                Evaluations++;
                var wait = s.Wait == WaitSignal.Live ? LiveWait(r) : _publishedWait[r];
                var utility = 2.0 * _likes[c * s.Cuisines + _cuisine[r]]
                              - 0.3 * _priceSensitivity[c] * _priceCents[r] / 100.0
                              - 1.5 * Math.Sqrt(distSq) / 1000.0
                              - wait / 60.0
                              + _reputation[r]
                              + 2.0;

                if (s.Rule == ChoiceRule.BestScore)
                {
                    if (utility > bestUtility)
                    {
                        bestUtility = utility;
                        best = r;
                    }
                }
                else
                {
                    _candidates[count] = r;
                    _weights[count] = Math.Exp(utility / s.Temperature);
                    total += _weights[count];
                    count++;
                }
            }

            if (s.Rule == ChoiceRule.Logit)
            {
                // The first 1.0 of the draw is staying home; a remainder lost to rounding goes to the last candidate.
                var pick = NextFloat() * total - 1.0;
                for (var i = 0; i < count && pick >= 0; i++)
                {
                    pick -= _weights[i];
                    if (pick < 0 || i == count - 1)
                        best = _candidates[i];
                }
            }

            if (best < 0)
            {
                StayedHome++;
                _until[c] = _now + 120 + 480 * NextFloat();
                return;
            }

            var tx = _rx[best] - _cx[c];
            var ty = _ry[best] - _cy[c];
            _state[c] = Travelling;
            _target[c] = best;
            _until[c] = _now + Math.Sqrt(tx * tx + ty * ty) / s.TravelMetresPerSecond;
        }

        private void Enqueue(int c, int r)
        {
            if (_ringCount[r] == _ringCapacity)
                CompactRing(r);

            var slot = r * _ringCapacity + (_ringHead[r] + _ringCount[r]) % _ringCapacity;
            _ticket[c]++;
            _ringCustomer[slot] = c;
            _ringTicket[slot] = _ticket[c];
            _ringCount[r]++;
            _activeQueue[r]++;
            if (_activeQueue[r] > _peakQueue[r])
                _peakQueue[r] = _activeQueue[r];

            _state[c] = Queued;
            _until[c] = _now + _patience[c];
        }

        private void StartService(int r)
        {
            while (_inService[r] < _servers[r] && _ringCount[r] > 0)
            {
                var slot = r * _ringCapacity + _ringHead[r];
                var c = _ringCustomer[slot];
                var live = _ringTicket[slot] == _ticket[c] && _state[c] == Queued && _target[c] == r;
                _ringHead[r] = (_ringHead[r] + 1) % _ringCapacity;
                _ringCount[r]--;
                if (!live)
                    continue;

                _activeQueue[r]--;
                _inService[r]++;
                _state[c] = Ordering;
                _until[c] = _now + _serviceSeconds[r];
            }
        }

        // Drops abandoned entries in place. Live entries never exceed the customer count, so this always frees a slot.
        private void CompactRing(int r)
        {
            var kept = 0;
            var baseIndex = r * _ringCapacity;
            for (var i = 0; i < _ringCount[r]; i++)
            {
                var from = baseIndex + (_ringHead[r] + i) % _ringCapacity;
                var c = _ringCustomer[from];
                if (_ringTicket[from] != _ticket[c] || _state[c] != Queued || _target[c] != r)
                    continue;

                var to = baseIndex + (_ringHead[r] + kept) % _ringCapacity;
                _ringCustomer[to] = c;
                _ringTicket[to] = _ringTicket[from];
                kept++;
            }

            _ringCount[r] = kept;
        }

        private float LiveWait(int r) => _activeQueue[r] * _serviceSeconds[r] / _servers[r];

        private float NextFloat()
        {
            // xorshift64*: deterministic across runtimes, unlike System.Random.
            _rng ^= _rng >> 12;
            _rng ^= _rng << 25;
            _rng ^= _rng >> 27;
            return ((_rng * 2685821657736338717UL) >> 40) / (float)(1UL << 24);
        }

        public CustomerChoiceSummary Summarize()
        {
            var summary = new CustomerChoiceSummary { Decisions = Decisions, Evaluations = Evaluations, Abandoned = Abandoned, StayedHome = StayedHome };
            for (var r = 0; r < _settings.Restaurants; r++)
            {
                summary.Served += _served[r];
                summary.RevenueCents += _revenueCents[r];
                summary.PeakQueue = Math.Max(summary.PeakQueue, _peakQueue[r]);
            }

            return summary;
        }

        // Returns null when consistent, else the first broken invariant: queue and service counts match customer states,
        // and revenue equals exactly one payment per completed order.
        public string CheckInvariants()
        {
            var queued = new int[_settings.Restaurants];
            var ordering = new int[_settings.Restaurants];
            for (var c = 0; c < _settings.Customers; c++)
            {
                if (_state[c] == Queued)
                    queued[_target[c]]++;
                else if (_state[c] == Ordering)
                    ordering[_target[c]]++;
                else if (_state[c] > Eating)
                    return $"customer {c} has unknown state {_state[c]}";
            }

            for (var r = 0; r < _settings.Restaurants; r++)
            {
                if (queued[r] != _activeQueue[r])
                    return $"restaurant {r}: {queued[r]} customers queued but active queue is {_activeQueue[r]}";
                if (ordering[r] != _inService[r])
                    return $"restaurant {r}: {ordering[r]} customers ordering but {_inService[r]} in service";
                if (_inService[r] > _servers[r])
                    return $"restaurant {r}: {_inService[r]} in service with {_servers[r]} servers";
                if (_revenueCents[r] != (long)_served[r] * _priceCents[r])
                    return $"restaurant {r}: revenue {_revenueCents[r]} for {_served[r]} orders at {_priceCents[r]}";
            }

            return null;
        }
    }

    public struct CustomerChoiceSummary
    {
        public long Decisions, Evaluations, Abandoned, StayedHome, Served, RevenueCents;
        public int PeakQueue;

        public override string ToString() =>
            $"decisions={Decisions} evaluations={Evaluations} served={Served} abandoned={Abandoned} stayedHome={StayedHome} peakQueue={PeakQueue} revenue=${RevenueCents / 100.0:F2}";
    }
}
