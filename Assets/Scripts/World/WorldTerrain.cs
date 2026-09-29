// The land surface of a generated world (layout format 2): heights in centimetres on a square grid of samples covering the
// map, sampled bilinearly. It is the height of roads, rails, bridge decks, entrances and trees; a river's channel is cut below
// it only in presentation, so a road over a river simply follows this surface as a bridge deck. The integer sampler is the
// generator's and validator's; the float sampler is for presentation and agrees with it to within rounding.
using System;
using System.Collections.Generic;

namespace FoodFactoryGame.World
{
    public sealed class WorldTerrain
    {
        // Metres between samples; 0 means flat land at height 0 (format 1 layouts).
        public int Spacing;
        // Samples per side. The grid is centred on the origin: sample 0 sits at -(Samples - 1) * Spacing / 2 on both axes.
        public int Samples;
        // Row-major by Z then X: HeightsCm[z * Samples + x].
        public int[] HeightsCm = Array.Empty<int>();

        public static WorldTerrain Flat() => new();

        public bool IsFlat => Spacing == 0 || Samples < 2;

        public int Origin => -(Samples - 1) * Spacing / 2;

        public int Sample(int x, int z) =>
            HeightsCm[Math.Clamp(z, 0, Samples - 1) * Samples + Math.Clamp(x, 0, Samples - 1)];

        // Bilinear height in centimetres at a point given in doubled metres (half-metre precision), rounded down. Points past the
        // grid take the nearest edge sample.
        public int HeightCm(int doubledX, int doubledZ)
        {
            if (IsFlat) return 0;
            var cell = 2L * Spacing;
            var px = Math.Clamp(doubledX - 2L * Origin, 0, cell * (Samples - 1));
            var pz = Math.Clamp(doubledZ - 2L * Origin, 0, cell * (Samples - 1));
            var ix = (int)Math.Min(px / cell, Samples - 2);
            var iz = (int)Math.Min(pz / cell, Samples - 2);
            var fx = px - ix * cell;
            var fz = pz - iz * cell;
            var sum = Sample(ix, iz) * (cell - fx) * (cell - fz) + Sample(ix + 1, iz) * fx * (cell - fz)
                      + Sample(ix, iz + 1) * (cell - fx) * fz + Sample(ix + 1, iz + 1) * fx * fz;
            return (int)WorldGeometry.FloorDiv(sum, cell * cell);
        }

        // Height of a whole-metre cell's centre.
        public int CellHeightCm(int x, int z) => HeightCm(2 * x + 1, 2 * z + 1);

        // Height in metres at a point in metres (presentation).
        public float Height(float x, float z)
        {
            if (IsFlat) return 0f;
            var span = Spacing * (Samples - 1);
            var px = Math.Clamp(x - Origin, 0f, span) / Spacing;
            var pz = Math.Clamp(z - Origin, 0f, span) / Spacing;
            var ix = Math.Min((int)px, Samples - 2);
            var iz = Math.Min((int)pz, Samples - 2);
            var fx = px - ix;
            var fz = pz - iz;
            var h = Sample(ix, iz) * (1 - fx) * (1 - fz) + Sample(ix + 1, iz) * fx * (1 - fz) + Sample(ix, iz + 1) * (1 - fx) * fz
                    + Sample(ix + 1, iz + 1) * fx * fz;
            return h / 100f;
        }

        // The same land turned a quarter clockwise about the origin (WorldGeometry.TurnPoint).
        public WorldTerrain Turned()
        {
            if (IsFlat) return this;
            var turned = new WorldTerrain { Spacing = Spacing, Samples = Samples, HeightsCm = new int[HeightsCm.Length] };
            for (var z = 0; z < Samples; z++)
            for (var x = 0; x < Samples; x++)
                turned.HeightsCm[z * Samples + x] = Sample(Samples - 1 - z, x);
            return turned;
        }
    }

    // Junction rules shared by presentation and any later traffic model.
    public static class WorldJunctions
    {
        public static int Rank(RoadKind kind) => kind switch
        {
            RoadKind.Arterial => 2,
            RoadKind.Local => 1,
            _ => 0
        };

        // Whether traffic arriving on a road of kind `approach` must stop at a node with this control, given the kinds of every
        // segment meeting there.
        public static bool Stops(JunctionControl control, RoadKind approach, IEnumerable<RoadKind> kindsAtNode)
        {
            if (control != JunctionControl.StopSign) return false;
            var highest = 0;
            var lowest = int.MaxValue;
            foreach (var kind in kindsAtNode)
            {
                highest = Math.Max(highest, Rank(kind));
                lowest = Math.Min(lowest, Rank(kind));
            }
            return highest == lowest || Rank(approach) < highest;
        }
    }
}
