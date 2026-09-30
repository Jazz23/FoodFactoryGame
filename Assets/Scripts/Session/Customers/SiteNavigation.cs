// Builds the local walkable NavMesh for a generated lot at runtime (decision 0030), where no baked NavMesh exists: a flat floor
// over the lot and a street band in front of it, with the shell's ground-floor walls built in as obstacles so paths use the
// doors. It is rebuilt when the drawn site, its size or its buildings change; placed equipment carves it by itself
// (EquipmentPresenter's obstacles). Presentation only: customer figures and employees walk on it, the simulation never reads it.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;
using UnityEngine.AI;

namespace FoodFactoryGame.Session.Customers
{
    [DisallowMultipleComponent]
    public sealed class SiteNavigation : MonoBehaviour
    {
        private const float FloorThickness = 0.1f;

        [SerializeField] private SessionRoot session;

        private NavMeshDataInstance _instance;
        private NavMeshData _data;
        private string _builtFor = "";

        // What the current NavMesh was built from; empty when none is built.
        public string BuiltFor => _builtFor;

        private void Update()
        {
            var subscription = session.ClientSubscription;
            var site = subscription?.Latest;
            var siteId = subscription?.SiteId;
            var layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == siteId);
            var outward = SiteStreet.Outward(layout, site?.Buildings);
            var key = outward == null ? "" : Key(layout, site, outward.Value);
            if (key == _builtFor) return;
            Remove();
            if (key != "") Build(layout, site, outward.Value);
            _builtFor = key;
        }

        private static string Key(SiteLayout layout, GoodsSnapshot site, Vector2Int outward)
        {
            var text = new StringBuilder($"{layout.SiteId}:{layout.Width}x{layout.Depth}:{outward}");
            foreach (var building in site.Buildings.Where(x => x.SiteId == layout.SiteId).OrderBy(x => x.Id, System.StringComparer.Ordinal))
            {
                text.Append($"|{building.Id}:{building.CellX},{building.CellZ},{building.Width},{building.Depth},{building.Floors}");
                foreach (var door in building.Doors) text.Append($";{door.X},{door.Z}");
            }
            return text.ToString();
        }

        private void Build(SiteLayout layout, GoodsSnapshot site, Vector2Int outward)
        {
            var sources = new List<NavMeshBuildSource>();
            var width = layout.Width * SiteGrid.CellSize;
            var depth = layout.Depth * SiteGrid.CellSize;
            AddBox(sources, Vector3.down * FloorThickness * 0.5f, new Vector3(width, FloorThickness, depth));
            var along = outward.x == 0 ? width : depth;
            var bandSize = outward.x == 0
                ? new Vector3(along + 2 * SiteStreet.Reach, FloorThickness, SiteStreet.Band)
                : new Vector3(SiteStreet.Band, FloorThickness, along + 2 * SiteStreet.Reach);
            var bandCenter = new Vector3(outward.x * (width + SiteStreet.Band) * 0.5f, -FloorThickness * 0.5f,
                outward.y * (depth + SiteStreet.Band) * 0.5f);
            AddBox(sources, bandCenter, bandSize);
            foreach (var building in site.Buildings.Where(x => x.SiteId == layout.SiteId))
                for (var x = building.CellX; x < building.CellX + building.Width; x++)
                for (var z = building.CellZ; z < building.CellZ + building.Depth; z++)
                    if (SiteGrid.IsWall(building, x, z))
                        AddBox(sources, SiteGridSpace.FootprintCenter(layout, x, z, 1, 1) + Vector3.up * SiteGridSpace.LevelHeight * 0.5f,
                            new Vector3(SiteGrid.CellSize, SiteGridSpace.LevelHeight, SiteGrid.CellSize));
            var settings = NavMesh.GetSettingsByID(0);
            var extent = Mathf.Max(width, depth) + 2 * (SiteStreet.Reach + SiteStreet.Band);
            var bounds = new Bounds(Vector3.up * SiteGridSpace.LevelHeight * 0.5f, new Vector3(extent, SiteGridSpace.LevelHeight + 2f, extent));
            _data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            _instance = NavMesh.AddNavMeshData(_data);
        }

        private static void AddBox(List<NavMeshBuildSource> sources, Vector3 center, Vector3 size) =>
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box, size = size, transform = Matrix4x4.Translate(center), area = 0
            });

        private void Remove()
        {
            if (_instance.valid) _instance.Remove();
            _instance = default;
            if (_data != null) Destroy(_data);
            _data = null;
        }

        private void OnDisable()
        {
            Remove();
            _builtFor = "";
        }
    }
}
