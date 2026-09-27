// Turns the seed a world's creator types into the generator's 64-bit seed. A whole number is used as is (negative numbers wrap
// to their two's-complement value); any other text hashes with FNV-1a, so "Sunny Valley" is as repeatable as 42. A blank entry
// means random: the creator gets a fresh value, which is then stored with the world like a typed one.
using System;
using System.Globalization;
using System.Security.Cryptography;

namespace FoodFactoryGame.World
{
    public static class WorldSeed
    {
        public static bool IsBlank(string text) => string.IsNullOrWhiteSpace(text);

        public static ulong FromText(string text)
        {
            var trimmed = (text ?? "").Trim();
            if (ulong.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var unsigned)) return unsigned;
            if (long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var signed)) return unchecked((ulong)signed);
            return WorldRandom.Fnv1a(trimmed);
        }

        // Not reproducible by design: only the choice of a random seed uses the system's randomness.
        public static ulong Random()
        {
            var bytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        // The requested text to record and the seed to generate from.
        public static (string Requested, ulong Seed) Resolve(string text)
        {
            if (!IsBlank(text)) return (text.Trim(), FromText(text));
            var seed = Random();
            return (seed.ToString(CultureInfo.InvariantCulture), seed);
        }
    }
}
