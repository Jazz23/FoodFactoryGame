// Explicit seeded PRNG for world generation (xoshiro256** seeded through SplitMix64). Generation never uses System.Random,
// Guid or hash-set order: every draw comes from here, and each generation phase forks its own stream by a fixed label so a
// change to one phase cannot shift another phase's draws.
using System;
using System.Collections.Generic;
using System.Text;

namespace FoodFactoryGame.World
{
    public sealed class WorldRandom
    {
        private ulong _s0, _s1, _s2, _s3;

        public WorldRandom(ulong seed)
        {
            var state = seed;
            _s0 = SplitMix(ref state);
            _s1 = SplitMix(ref state);
            _s2 = SplitMix(ref state);
            _s3 = SplitMix(ref state);
        }

        // An independent stream for one named phase, fixed by the parent seed and the label only.
        public static WorldRandom Fork(ulong seed, string label) => new(Mix(seed ^ Fnv1a(label)));

        public ulong NextULong()
        {
            var result = RotateLeft(_s1 * 5, 7) * 9;
            var t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);
            return result;
        }

        // Uniform in [0, bound) without modulo bias.
        public int Next(int bound)
        {
            if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
            var limit = ulong.MaxValue - ulong.MaxValue % (ulong)bound;
            ulong value;
            do value = NextULong(); while (value >= limit);
            return (int)(value % (ulong)bound);
        }

        // Uniform in [min, max], both inclusive.
        public int Range(int min, int max) => max < min ? throw new ArgumentOutOfRangeException(nameof(max)) : min + Next(max - min + 1);

        public bool Chance(int percent) => Next(100) < percent;

        // Index chosen with probability proportional to its non-negative weight; -1 when every weight is zero.
        public int Weighted(IReadOnlyList<int> weights)
        {
            var total = 0;
            foreach (var weight in weights) total += Math.Max(0, weight);
            if (total == 0) return -1;
            var roll = Next(total);
            for (var index = 0; index < weights.Count; index++)
            {
                roll -= Math.Max(0, weights[index]);
                if (roll < 0) return index;
            }
            return weights.Count - 1;
        }

        // The seed of retry attempt n: attempt 0 is the seed itself.
        public static ulong DeriveSeed(ulong seed, int attempt) => attempt == 0 ? seed : Mix(seed + 0x9E3779B97F4A7C15UL * (ulong)attempt);

        // FNV-1a over UTF-8: a stable string hash (string.GetHashCode is randomized per process).
        public static ulong Fnv1a(string text)
        {
            var hash = 14695981039346656037UL;
            foreach (var value in Encoding.UTF8.GetBytes(text ?? ""))
            {
                hash ^= value;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        public static ulong Mix(ulong value)
        {
            var state = value;
            return SplitMix(ref state);
        }

        private static ulong SplitMix(ref ulong state)
        {
            var z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong RotateLeft(ulong value, int bits) => (value << bits) | (value >> (64 - bits));
    }
}
