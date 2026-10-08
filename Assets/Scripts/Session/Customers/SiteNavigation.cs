// Builds a local walkable NavMesh for each drawn generated lot at runtime (decisions 0030, 0031), where no baked NavMesh exists: a flat floor
// over the lot and a street band in front of it, with the shell's ground-floor walls built in as obstacles so paths use the
// doors, at the site's place in the scene. It is rebuilt when the site, its size or its buildings change, and removed when the site stops being drawn; placed equipment carves it by itself
// (EquipmentPresenter's obstacles). Presentation only: customer figures and employees walk on it, the simulation never reads it.
// On a host it is also built for every generated lot that has employees (decision 0039), read from the server world, so an
// employee keeps walking at a restaurant far from the local camera; how far the player is never decides whether one can work.
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

        private sealed class Built
        {
            public NavMeshDataInstance Instance;
            public NavMeshData Data;
            public string Key;
        }

        private readonly Dictionary<string, Built> _built = new();
        private int _shown = -1;
        // Host only: generated lots with employees, from the server world, rechecked every StaffedCheckSeconds.
        private const float StaffedCheckSeconds = 2f;
        private readonly List<(SiteLayout Layout, GoodsSnapshot Snapshot)> _staffed = new();
        private float _nextStaffedCheck;

        // What the current site's NavMesh was built from; empty when none is built.
        public string BuiltFor => BuiltForSite(session.ClientSiteId);

        // What a drawn site's NavMesh was built from; empty when none is built.
        public string BuiltForSite(string siteId) => siteId != null && _built.TryGetValue(siteId, out var built) ? built.Key : "";

        private void Update()
        {
            var drawn = session.DrawnSites;
            var staffedChanged = Time.unscaledTime >= _nextStaffedCheck && RefreshStaffed();
            if ((drawn == null || drawn.Version == _shown) && !staffedChanged) return;
            if (drawn != null) _shown = drawn.Version;
            var keys = new Dictionary<string, (string Key, SiteLayout Layout, GoodsSnapshot Snapshot, Vector2Int Outward)>();
            // A drawn site's baseline is the client's; a staffed one that is not drawn uses the server's view of it.
            var sources = (drawn?.Sites.Select(x => (x.SiteId, x.Layout, x.Snapshot)) ?? Enumerable.Empty<(string, SiteLayout, GoodsSnapshot)>())
                .Concat(_staffed.Select(x => (x.Layout.SiteId, x.Layout, x.Snapshot)));
            foreach (var (siteId, layout, snapshot) in sources)
            {
                if (keys.ContainsKey(siteId)) continue;
                var outward = SiteStreet.Outward(layout, snapshot.Buildings);
                if (outward != null) keys[siteId] = (Key(layout, snapshot, outward.Value), layout, snapshot, outward.Value);
            }
            foreach (var siteId in _built.Keys.Where(x => !keys.TryGetValue(x, out var key) || key.Key != _built[x].Key).ToList())
            {
                Remove(_built[siteId]);
                _built.Remove(siteId);
            }
            foreach (var (siteId, (key, layout, snapshot, outward)) in keys)
                if (!_built.ContainsKey(siteId)) _built[siteId] = Build(layout, snapshot, outward, key);
        }

        // Host only: rereads which sites have employees and their buildings. True when that changed what would be built.
        private bool RefreshStaffed()
        {
            _nextStaffedCheck = Time.unscaledTime + StaffedCheckSeconds;
            var world = session.ServerWorld;
            var before = string.Join("|", _staffed.Select(x => x.Layout.SiteId + Signature(x.Snapshot)));
            _staffed.Clear();
            if (world != null)
                foreach (var employee in world.Employees().GroupBy(x => x.SiteId).Select(x => x.First()))
                {
                    // The employee's own view: it is granted its site.
                    var view = world.View(employee.Id, employee.SiteId);
                    var layout = view?.SiteLayouts.FirstOrDefault(x => x.SiteId == employee.SiteId);
                    if (layout != null) _staffed.Add((layout, view));
                }
            return before != string.Join("|", _staffed.Select(x => x.Layout.SiteId + Signature(x.Snapshot)));
        }

        private static string Signature(GoodsSnapshot site) =>
            string.Join(",", site.Buildings.Select(x => $"{x.Id}:{x.CellX},{x.CellZ},{x.Width},{x.Depth},{x.Structures?.Count}"));

        private static string Key(SiteLayout layout, GoodsSnapshot site, Vector2Int outward)
        {
            var text = new StringBuilder($"{layout.SiteId}:{layout.Width}x{layout.Depth}:{outward}@{SiteGridSpace.Origin(layout)}");
            foreach (var building in site.Buildings.Where(x => x.SiteId == layout.SiteId).OrderBy(x => x.Id, System.StringComparer.Ordinal))
            {
                text.Append($"|{building.Id}:{building.CellX},{building.CellZ},{building.Width},{building.Depth},{building.Floors}");
                foreach (var door in building.Doors) text.Append($";{door.X},{door.Z}");
                // Interior walls, their doors and windows (decision 0034) change where figures can walk.
                foreach (var piece in building.Structures ?? new System.Collections.Generic.List<GoodsStructure>())
                    if (piece.Kind is GoodsWorld.PartitionStructure or GoodsWorld.DoorStructure or GoodsWorld.WindowStructure)
                        text.Append($";{piece.Kind[0]}{piece.X},{piece.Z},{piece.Axis}");
            }
            return text.ToString();
        }

        // Sources and bounds are in scene space around the site's origin (decision 0031).
        private Built Build(SiteLayout layout, GoodsSnapshot site, Vector2Int outward, string key)
        {
            var sources = new List<NavMeshBuildSource>();
            var origin = SiteGridSpace.Origin(layout);
            var width = layout.Width * SiteGrid.CellSize;
            var depth = layout.Depth * SiteGrid.CellSize;
            AddBox(sources, origin + Vector3.down * FloorThickness * 0.5f, new Vector3(width, FloorThickness, depth));
            var along = outward.x == 0 ? width : depth;
            var bandSize = outward.x == 0
                ? new Vector3(along + 2 * SiteStreet.Reach, FloorThickness, SiteStreet.Band)
                : new Vector3(SiteStreet.Band, FloorThickness, along + 2 * SiteStreet.Reach);
            var bandCenter = origin + new Vector3(outward.x * (width + SiteStreet.Band) * 0.5f, -FloorThickness * 0.5f,
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
            var bounds = new Bounds(origin + Vector3.up * SiteGridSpace.LevelHeight * 0.5f, new Vector3(extent, SiteGridSpace.LevelHeight + 2f, extent));
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            return new Built { Data = data, Instance = NavMesh.AddNavMeshData(data), Key = key };
        }

        private static void AddBox(List<NavMeshBuildSource> sources, Vector3 center, Vector3 size) =>
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box, size = size, transform = Matrix4x4.Translate(center), area = 0
            });

        private void Remove(Built built)
        {
            if (built.Instance.valid) built.Instance.Remove();
            if (built.Data != null) Destroy(built.Data);
        }

        private void OnDisable()
        {
            foreach (var built in _built.Values) Remove(built);
            _built.Clear();
            _shown = -1;
        }
    }
}
