// Presentation only: how much a map point (layout metres) sounds like the city, the farmland or a factory, read from the shown
// world layout. The three weights always sum to 1 so the ambience beds crossfade without a dip or a swell: a factory nearby
// takes over first, then farmland (a farm's fields nearby, or simply being out of town), and the city has the rest.
using System.Collections.Generic;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session.Audio
{
    public readonly struct AmbienceMix
    {
        public readonly float City;
        public readonly float Farm;
        public readonly float Factory;

        public AmbienceMix(float city, float farm, float factory)
        {
            City = city;
            Farm = farm;
            Factory = factory;
        }

        // The mix where no layout is shown (dev sites without a map): the city bed, as before areas existed.
        public static AmbienceMix CityOnly => new(1f, 0f, 0f);
    }

    public sealed class AudioZones
    {
        // Distances (metres) over which each area fades in: full at the near edge, gone at the far edge.
        private const float CityFadeNear = 0f;
        private const float CityFadeFar = 80f;
        private const float FarmFadeNear = 10f;
        private const float FarmFadeFar = 90f;
        private const float FactoryFadeNear = 6f;
        private const float FactoryFadeFar = 50f;

        private readonly List<WorldRect> _city = new();
        private readonly List<WorldRect> _farms = new();
        private readonly List<WorldRect> _factories = new();

        public WorldLayout Layout { get; }

        public AudioZones(WorldLayout layout)
        {
            Layout = layout;
            foreach (var district in layout.Districts) _city.AddRange(district.Areas);
            if (_city.Count == 0 && layout.CityHalfSize > 0)
                _city.Add(new WorldRect(-layout.CityHalfSize, -layout.CityHalfSize, 2 * layout.CityHalfSize, 2 * layout.CityHalfSize));
            foreach (var building in layout.Buildings)
            {
                if (building.Category == BuildingCategory.Farm) _farms.Add(building.Footprint);
                else if (building.Category == BuildingCategory.Factory) _factories.Add(building.Footprint);
            }
        }

        public AmbienceMix MixAt(Vector2 map)
        {
            var city = Near(_city, map, CityFadeNear, CityFadeFar);
            var farm = Mathf.Max(Near(_farms, map, FarmFadeNear, FarmFadeFar), 1f - city);
            var factory = Near(_factories, map, FactoryFadeNear, FactoryFadeFar);
            var rest = 1f - factory;
            return new AmbienceMix(rest * (1f - farm), rest * farm, factory);
        }

        // 1 within near metres of the closest rectangle, 0 beyond far, smooth between; 0 when there are none.
        private static float Near(List<WorldRect> rects, Vector2 map, float near, float far)
        {
            var best = float.MaxValue;
            foreach (var rect in rects)
            {
                var dx = Mathf.Max(0f, Mathf.Max(rect.X - map.x, map.x - (rect.X + rect.Width)));
                var dz = Mathf.Max(0f, Mathf.Max(rect.Z - map.y, map.y - (rect.Z + rect.Depth)));
                var distance = dx * dx + dz * dz;
                if (distance < best) best = distance;
            }
            if (best == float.MaxValue) return 0f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(near, far, Mathf.Sqrt(best)));
        }
    }
}
