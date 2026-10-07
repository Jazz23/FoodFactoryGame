// Runs restaurant building (decision 0034) in the real WorldGen scene as a host with a loopback-UDP remote teammate, on a new
// generated world in a unique temporary directory: build-mode orders (removing and redrawing an outer wall, interior wall,
// furnishing, wall decor, a sale)
// reach the server and both clients and charge or refund exactly; a teammate's refused order changes nothing; register staffing
// replicates and ends when the player disconnects; restaurant docks placed in the yard serve one truck at a time and deliver to
// a teammate's view. An Explicit test renders the build-mode and decorated-restaurant captures for visual acceptance. The
// application's saves are never opened.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using FoodFactoryGame.Session.WorldMap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class RestaurantBuildingSessionTests
    {
        private const string ScenePath = "Assets/Scenes/WorldGen.unity";
        // TEST-ONLY seed of the isolated world (the one the WorldGen session tests use).
        private const string Seed = "piece-two";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private WorldLayoutPresenter _map;
        private NetworkManager _remote;
        private DevAuthenticator _remoteAuth;
        private ClientSiteSubscription _remoteSite;
        private ushort _port;
        private readonly Dictionary<string, GoodsOutcome> _results = new();

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 30f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input.Setup();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryRestaurantPlay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _map = UnityEngine.Object.FindAnyObjectByType<WorldLayoutPresenter>();
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"), IdentityPath = Path.Combine(_directory, "host.db"), DisplayName = "Host",
                Address = "127.0.0.1", WorldSeed = Seed
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            _root.NetworkManager.TransportManager.Transport.SetPort(_port);
            _results.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                _remoteSite?.Reset();
                if (_remote != null)
                {
                    _remote.ClientManager.StopConnection();
                    UnityEngine.Object.Destroy(_remote.gameObject);
                }
                _remote = null;
                if (_root != null) _root.Shutdown();
                _root = null;
                yield return null;
                if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
                _directory = null;
            }
            finally
            {
                _input.TearDown();
            }
        }

        private PropertyOffer Start => _root.StartOffer;
        private BuildMode Build => UnityEngine.Object.FindAnyObjectByType<BuildMode>();
        private EquipmentInteraction Interaction => UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
        private GoodsBuilding Shell(GoodsSnapshot site) => site.Buildings.Single(x => x.SiteId == Start.SiteId);

        private IEnumerator StartHost()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && _map.Generated, "host baseline and map", 90f);
            _root.ClientSubscription.ResultReceived += x => _results[x.RequestId] = x;
            yield return Until(() => LocalAvatar() != null, "local avatar");
        }

        private IEnumerator StartRemote()
        {
            CreateRemote();
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest != null; }, "remote baseline of the starting site");
        }

        private IEnumerator OpenBuild()
        {
            yield return Until(() => { if (!Build.Active) Interaction.OpenBuild(); return Build.Active; }, "build mode open");
            yield return null;
        }

        // Money is only exchanged for recorded structure and furnishings, so cash plus everything recorded as charged is constant.
        private long Ledger(GoodsSnapshot state) => state.Companies.Single().Cash + state.Buildings.SelectMany(x => x.Structures).Sum(x => x.ChargedCents)
            + state.Equipment.Sum(x => x.ChargedCents);

        private GoodsSnapshot RemoteLatest()
        {
            _remoteSite.Tick();
            return _remoteSite.Latest;
        }

        // Every door of a shell: its perimeter doors, or with free walls (decision 0036) its door records.
        private static IEnumerable<(int X, int Z)> DoorCells(GoodsBuilding shell) =>
            shell.Doors.Select(d => (d.X, d.Z)).Concat(shell.Structures.Where(s => s.Kind == GoodsWorld.DoorStructure).Select(s => (s.X, s.Z))).Distinct();

        // Interior cells (strictly inside, no wall) whose footprint the build preview accepts for the chosen item.
        private (int X, int Z) FreeInterior(GoodsSnapshot site, int width, int depth, Func<(int X, int Z), bool> extra = null)
        {
            var shell = Shell(site);
            return Enumerable.Range(0, Start.Width * Start.Depth).Select(i => (X: i % Start.Width, Z: i / Start.Width))
                .Where(c => SiteGrid.InsideInterior(shell, c.X, c.Z, width, depth) && SiteGrid.CellProblem(site, Start.SiteId, c.X, c.Z, width, depth, null) == null
                    && (extra == null || extra(c)))
                .OrderByDescending(c => DoorCells(shell).Min(d => Mathf.Abs(d.X - c.X) + Mathf.Abs(d.Z - c.Z))).First();
        }

        [UnityTest]
        public IEnumerator BuildModeOrdersReachTheServerAndATeammate()
        {
            yield return StartHost();
            yield return StartRemote();
            var startLedger = Ledger(_root.ServerWorld.Snapshot());
            var before = Shell(_root.ClientSite);
            yield return OpenBuild();
            var build = Build;
            Assert.That(build.Window.resolvedStyle.display, Is.EqualTo(UnityEngine.UIElements.DisplayStyle.Flex), "The build panel shows.");
            var rig = LocalAvatar().CameraRig;
            Assert.That((rig.TopDown, rig.BuildFocus.HasValue), Is.EqualTo((true, true)), "Build mode looks straight down on the lot.");

            // Slice 1 with free walls (decision 0036): a right click takes out an outer wall cell like any other wall (it came with
            // the building, so it refunds nothing), and the Wall tool draws it back.
            var outer = Enumerable.Range(before.CellX + 2, before.Width - 4).Select(x => (X: x, Z: before.CellZ + before.Depth - 1))
                .First(c => SiteGrid.IsWall(before, c.X, c.Z) && SiteGrid.WindowAt(before, c.X, c.Z) == null
                    && _root.ClientSite.Equipment.All(e => e.Layer != SiteGrid.WallLayer || !SiteGrid.Contains(e, c.X, c.Z, 1, 1)));
            build.RemoveAt(outer);
            yield return Until(() => Shell(_root.ClientSite).FreeWalls && !SiteGrid.IsWall(Shell(_root.ClientSite), outer.X, outer.Z), "host sees the opening");
            Assert.That(build.LastRejection, Is.Null);
            yield return Until(() => Shell(RemoteLatest()).FreeWalls && !SiteGrid.IsWall(Shell(RemoteLatest()), outer.X, outer.Z), "the teammate sees the opening");
            Assert.That(SiteGrid.InteriorCells(Shell(_root.ClientSite)), Is.Empty, "An open shell encloses nothing.");
            build.SelectTool(BuildTool.Wall, "plaster");
            build.Drag(outer, outer);
            Assert.That(build.HasPending, Is.True, "A drawn wall waits for Confirm.");
            Assert.That(build.PendingPreview.Problem, Is.Null, build.PendingPreview.Problem);
            var charge = build.PendingPreview.NetCents;
            Assert.That(charge, Is.GreaterThan(0));
            build.Confirm();
            yield return Until(() => SiteGrid.IsWall(Shell(_root.ClientSite), outer.X, outer.Z), "host sees the outer wall again");
            Assert.That(build.LastRejection, Is.Null);
            yield return Until(() => SiteGrid.IsWall(Shell(RemoteLatest()), outer.X, outer.Z), "the teammate sees the outer wall again");
            Assert.That((Shell(_root.ClientSite).CellX, Shell(_root.ClientSite).CellZ, Shell(_root.ClientSite).Width, Shell(_root.ClientSite).Depth),
                Is.EqualTo((before.CellX, before.CellZ, before.Width, before.Depth)), "The footprint follows the walls.");
            Assert.That(SiteGrid.InteriorCells(Shell(_root.ClientSite)).Count(), Is.EqualTo((before.Width - 2) * (before.Depth - 2)), "The room is closed again.");

            // An interior wall of two cells, drawn and confirmed.
            var site = _root.ClientSite;
            var wall = FreeInterior(site, 1, 2);
            build.SelectTool(BuildTool.Wall, "brick");
            build.Drag(wall, (wall.X, wall.Z + 1));
            Assert.That(build.PendingPreview.Problem, Is.Null, build.PendingPreview.Problem);
            build.Confirm();
            yield return Until(() => Shell(RemoteLatest()).Structures.Count(s => s.Kind == GoodsWorld.PartitionStructure && s.Style == "brick") == 2, "the teammate sees the interior wall");
            var buildings = UnityEngine.Object.FindAnyObjectByType<BuildingPresenter>();
            yield return Until(() => buildings.ShellOf(Shell(_root.ClientSite).Id)?.GetComponentsInChildren<Transform>().Any(t => t.name.StartsWith("RT_Wall_Brick")) == true,
                "the wall is drawn with the brick kit models");

            // Slice 3 and 4: a bistro table placed with one click, and art on the back wall.
            site = _root.ClientSite;
            var table = FreeInterior(site, 1, 1);
            build.SelectOffer("supplier-rt-table-bistro");
            build.PressAt(table);
            build.ReleaseAt(table);
            yield return Until(() => _root.ClientSite.Equipment.Any(x => x.Kind == "rt-table-bistro"), "the host sees the table");
            var placed = _root.ClientSite.Equipment.Single(x => x.Kind == "rt-table-bistro");
            Assert.That((placed.CellX, placed.CellZ, placed.Seats, placed.ChargedCents), Is.EqualTo((table.X, table.Z, 2, 6000L)));
            yield return Until(() => RemoteLatest().Equipment.Any(x => x.Id == placed.Id), "the teammate sees the table");
            var shell = Shell(_root.ClientSite);
            var art = Enumerable.Range(shell.CellX + 1, shell.Width - 2).Select(x => (X: x, Z: shell.CellZ + shell.Depth - 1))
                .Concat(Enumerable.Range(shell.CellX + 1, shell.Width - 2).Select(x => (X: x, Z: shell.CellZ)))
                .First(c => SiteGrid.IsWall(shell, c.X, c.Z) && SiteGrid.CellProblem(_root.ClientSite, Start.SiteId, c.X, c.Z, 1, 1, null, 0, SiteGrid.WallLayer) == null);
            build.SelectOffer("supplier-rt-wall-art");
            build.PressAt(art);
            build.ReleaseAt(art);
            yield return Until(() => RemoteLatest().Equipment.Any(x => x.Kind == "rt-wall-art"), "the teammate sees the wall art");
            Assert.That(RestaurantRules.Ambience(_root.ClientSite, Start.SiteId), Is.GreaterThan(0), "Decor raises the restaurant's ambience.");

            // A refused order from the teammate (an interior wall over the table) changes nothing for anyone.
            var refusedBefore = Shell(_root.ServerWorld.Snapshot());
            _remoteSite.ResultReceived += x => _results[x.RequestId] = x;
            _remoteSite.Bridge.RequestShellOrder("remote-wall", new ShellOrder
            {
                Kind = ShellOrder.Partition, BuildingId = shell.Id, Style = "plaster", Cells = { new GridCell { X = table.X, Z = table.Z } }
            });
            yield return Until(() => _results.ContainsKey("remote-wall"), "the teammate's refusal");
            Assert.That((_results["remote-wall"].Accepted, _results["remote-wall"].Reason), Is.EqualTo((false, "blocked")));
            Assert.That(JsonUtility.ToJson(Shell(_root.ServerWorld.Snapshot())), Is.EqualTo(JsonUtility.ToJson(refusedBefore)));

            // Selling the table with a right click refunds exactly its price.
            var cashBefore = _root.ClientSite.Companies.Single().Cash;
            build.RemoveAt(table);
            yield return Until(() => _root.ClientSite.Equipment.All(x => x.Id != placed.Id), "the table is sold");
            Assert.That(_root.ClientSite.Companies.Single().Cash, Is.EqualTo(cashBefore + 6000));
            yield return Until(() => RemoteLatest().Equipment.All(x => x.Id != placed.Id), "the teammate sees the sale");
            Assert.That(Ledger(_root.ServerWorld.Snapshot()), Is.EqualTo(startLedger), "Every order charged or refunded exactly once.");
            var saved = GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot();
            Assert.That(JsonUtility.ToJson(Shell(saved)), Is.EqualTo(JsonUtility.ToJson(Shell(_root.ServerWorld.Snapshot()))), "The shell is committed.");

            Interaction.CloseScreen();
            yield return null;
            Assert.That(rig.BuildFocus.HasValue, Is.False, "Leaving build mode returns the camera to the avatar.");
        }

        [UnityTest]
        public IEnumerator RegisterStaffingReplicatesAndEndsWhenThePlayerLeaves()
        {
            yield return StartHost();
            yield return StartRemote();
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            var me = _root.Authenticator.LocalPlayerId;
            Interaction.OpenMachine(GeneratedWorld.StartCounterId);
            yield return Until(() => Interaction.Screen == InteractionScreen.Machine, "the register screen");
            hud.ClickStaff(GeneratedWorld.StartCounterId, true);
            yield return Until(() => RemoteLatest().Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId).StaffId == me, "the teammate sees the host at the register");
            Interaction.CloseScreen();

            // The teammate takes over the register; then it disconnects and the register is unstaffed for everyone.
            var remoteId = _remoteAuth.LocalPlayerId;
            _remoteSite.ResultReceived += x => _results[x.RequestId] = x;
            _remoteSite.Bridge.RequestStaff("remote-staff", GeneratedWorld.StartCounterId, remoteId);
            yield return Until(() => _results.ContainsKey("remote-staff"), "the teammate's staffing");
            Assert.That(_results["remote-staff"].Accepted, Is.True, _results["remote-staff"].Reason);
            yield return Until(() => _root.ClientSite.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId).StaffId == remoteId, "the host sees the teammate at the register");
            _remote.ClientManager.StopConnection();
            yield return Until(() => _root.ClientSite.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId).StaffId == "", "the register is released on disconnect");
            Assert.That(GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot().Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId).StaffId, Is.Empty);
        }

        [UnityTest]
        public IEnumerator RestaurantDocksInTheYardServeOneTruckAtATime()
        {
            yield return StartHost();
            yield return StartRemote();
            var bridge = _root.ClientSubscription.Bridge;
            var diner = _map.Offers.Values.Where(x => x.ForSale && x.Category == GoodsWorld.RestaurantKind)
                .OrderBy(x => Mathf.Abs(x.LotX - Start.LotX) + Mathf.Abs(x.LotZ - Start.LotZ)).First();
            bridge.RequestBuyProperty("buy-diner", Start.SiteId, diner.LotId);
            yield return Until(() => _results.ContainsKey("buy-diner"), "the second restaurant");
            Assert.That(_results["buy-diner"].Accepted, Is.True, _results["buy-diner"].Reason);
            _root.ClientSubscription.Watch(diner.SiteId);
            _remoteSite.Watch(diner.SiteId);
            yield return Until(() => _root.ClientSubscription.Remote(diner.SiteId) != null, "the second site's baseline");

            // A dock in each yard, on the street side, through the build-mode order (one charge, placed at once).
            string PlaceDock(PropertyOffer lot, GoodsSnapshot snapshot, string request)
            {
                var template = _root.Offers.Single(x => x.Id == "supplier-dock").Equipment.CreateTemplate();
                var shell = snapshot.Buildings.Single(x => x.SiteId == lot.SiteId);
                // The yard runs along whichever side faces the street, so the dock lies along it (either rotation).
                var cell = Enumerable.Range(0, lot.Width * lot.Depth * 2).Select(i => (X: i / 2 % lot.Width, Z: i / 2 / lot.Width, Rotation: i % 2))
                    .Where(c => !SiteGrid.Overlaps(c.X, c.Z, c.Rotation == 0 ? 2 : 1, c.Rotation == 0 ? 1 : 2, shell.CellX - 1, shell.CellZ - 1, shell.Width + 2, shell.Depth + 2))
                    .First(c => GoodsWorld.FurnishProblem(snapshot, lot.SiteId, template, new[] { new GridPlacement { X = c.X, Z = c.Z, Rotation = c.Rotation } }, 0, lot) == null);
                bridge.RequestBuyAndPlace(request, new FurnishOrder
                    { SiteId = lot.SiteId, OfferId = "supplier-dock", Placements = { new GridPlacement { X = cell.X, Z = cell.Z, Rotation = cell.Rotation } } });
                return $"buy:{_root.Authenticator.LocalPlayerId}:{request}:0";
            }
            var pickup = PlaceDock(Start, _root.ClientSite, "dock-start");
            var dropoff = PlaceDock(diner, _root.ClientSubscription.Remote(diner.SiteId), "dock-diner");
            yield return Until(() => _results.ContainsKey("dock-start") && _results.ContainsKey("dock-diner"), "both docks");
            Assert.That((_results["dock-start"].Accepted, _results["dock-diner"].Accepted), Is.EqualTo((true, true)),
                _results["dock-start"].Reason + " / " + _results["dock-diner"].Reason);

            // Two trucks on one route; TEST-ONLY goods straight into the pickup dock's outgoing buffer on the server.
            bridge.RequestCreateRoute("route", pickup, dropoff, Array.Empty<string>());
            bridge.RequestPurchase("truck-1", Start.SiteId, "supplier-truck");
            bridge.RequestPurchase("truck-2", Start.SiteId, "supplier-truck");
            yield return Until(() => _results.ContainsKey("route") && _results.ContainsKey("truck-1") && _results.ContainsKey("truck-2"), "route and trucks");
            Assert.That(_results["route"].Accepted && _results["truck-1"].Accepted && _results["truck-2"].Accepted, Is.True);
            var route = GoodsWorld.RouteIdFor(_root.Authenticator.LocalPlayerId, "route");
            _root.ServerWorld.Bootstrap(new GoodsLot { Id = "test-crates", ItemId = "crate", OwnerId = Start.SiteId, LocationId = pickup + ":in", Quantity = 8, SpoilAfterSeconds = 1_000_000 });
            // TEST-ONLY: trucks drive ten times faster; the dock rule is checked on every server snapshot, one clock second at a time.
            bridge.ClockRate = 10f;
            bridge.RequestAssignTruck("assign-1", $"buy:{_root.Authenticator.LocalPlayerId}:truck-1", route);
            bridge.RequestAssignTruck("assign-2", $"buy:{_root.Authenticator.LocalPlayerId}:truck-2", route);
            var docked = 0;
            yield return Until(() =>
            {
                var live = _root.ServerWorld.Snapshot();
                docked = Math.Max(docked, live.Trucks.Count(x => x.Docked));
                Assert.That(live.Trucks.Count(x => x.Docked && x.State == TruckState.Loading), Is.LessThanOrEqualTo(1), "One truck at the pickup dock.");
                var remote = _remoteSite.Remote(diner.SiteId);
                _remoteSite.Tick();
                return remote != null && remote.Lots.Where(x => x.LocationId == dropoff + ":out").Sum(x => x.Quantity) == 8;
            }, "the teammate sees all crates delivered", 120f);
            Assert.That(docked, Is.GreaterThanOrEqualTo(1));
            var state = _root.ServerWorld.Snapshot();
            Assert.That(state.Lots.Where(x => x.ItemId == "crate").Sum(x => x.Quantity), Is.EqualTo(8), "Nothing lost or duplicated.");
        }

        // Visual acceptance (slices 2 and 4): build mode with its grid, a ghost and the panel, then the decorated restaurant from
        // above. Writes PNGs to docs/verification/restaurant-building-20261001/. Runs only on request: it is ignored unless the
        // file Temp/restaurant-captures.flag exists (the Pipeline test runner does not run [Explicit] tests).
        [UnityTest]
        public IEnumerator BuildModeAndDecoratedRestaurantCaptures()
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "..", "Temp", "restaurant-captures.flag")))
                Assert.Ignore("Captures run on request: create Temp/restaurant-captures.flag.");
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "verification", "restaurant-building-20261001"));
            Directory.CreateDirectory(output);
            yield return StartHost();
            var world = _root.ServerWorld;
            var me = _root.Authenticator.LocalPlayerId;
            var site = Start.SiteId;
            var shell = Shell(world.Snapshot());
            // Furnish through the same server orders build mode sends (TEST-ONLY layout of the capture).
            var placed = new List<string>();
            void Furnish(string offer, int x, int z, int rotation = 0)
            {
                var result = world.BuyAndPlaceDurably(me, Guid.NewGuid().ToString("N"), new FurnishOrder
                    { SiteId = site, OfferId = offer, Placements = { new GridPlacement { X = x, Z = z, Rotation = rotation } } }, _root.Options.WorldPath);
                placed.Add($"{offer}@{x},{z}:{(result.Accepted ? "ok" : result.Reason)}");
            }
            var x0 = shell.CellX + 1;
            var z0 = shell.CellZ + 1;
            var x1 = shell.CellX + shell.Width - 2;
            var z1 = shell.CellZ + shell.Depth - 2;
            Assert.That(world.OrderShellDurably(me, "finish", new ShellOrder { Kind = ShellOrder.WallFinish, BuildingId = shell.Id, Style = "wainscot" }, _root.Options.WorldPath).Accepted, Is.True);
            // A corner room off the east wall: a brick interior wall with a glazed door and a serving hatch.
            var roomZ = z0 + 2;
            var room = new ShellOrder { Kind = ShellOrder.Partition, BuildingId = shell.Id, Style = "brick" };
            for (var x = x1 - 2; x <= x1; x++) room.Cells.Add(new GridCell { X = x, Z = roomZ });
            placed.Add("room:" + world.OrderShellDurably(me, "room", room, _root.Options.WorldPath).Reason);
            placed.Add("room-door:" + world.OrderShellDurably(me, "room-door", new ShellOrder
                { Kind = ShellOrder.Door, BuildingId = shell.Id, X = x1 - 2, Z = roomZ, Style = "glazed" }, _root.Options.WorldPath).Reason);
            placed.Add("hatch:" + world.OrderShellDurably(me, "hatch", new ShellOrder
                { Kind = ShellOrder.Window, BuildingId = shell.Id, X = x1 - 1, Z = roomZ, Axis = 0, Style = "hatch" }, _root.Options.WorldPath).Reason);
            shell = Shell(world.Snapshot());
            var floor = new FurnishOrder { SiteId = site, OfferId = "supplier-rt-floor-oak" };
            for (var x = x0; x <= x1; x++)
            for (var z = z0; z <= z1; z++)
                if (!SiteGrid.IsWall(shell, x, z) && world.Snapshot().Equipment.All(e => e.Layer != SiteGrid.FloorLayer || !SiteGrid.Contains(e, x, z, 1, 1)))
                    floor.Placements.Add(new GridPlacement { X = x, Z = z });
            placed.Add("floor:" + world.BuyAndPlaceDurably(me, "floor", floor, _root.Options.WorldPath).Reason);
            var occupied = new HashSet<(int, int)>(world.Snapshot().Equipment.Where(e => string.IsNullOrEmpty(e.Layer) && e.State == EquipmentState.Placed)
                .SelectMany(e => Enumerable.Range(0, SiteGrid.Footprint(e.Width, e.Depth, e.Rotation).Width)
                    .SelectMany(dx => Enumerable.Range(0, SiteGrid.Footprint(e.Width, e.Depth, e.Rotation).Depth).Select(dz => (e.CellX + dx, e.CellZ + dz)))));
            var free = Enumerable.Range(x0, x1 - x0 + 1).SelectMany(x => Enumerable.Range(z0, z1 - z0 + 1).Select(z => (X: x, Z: z)))
                .Where(c => !occupied.Contains(c) && !SiteGrid.IsWall(shell, c.X, c.Z) && !(c.X >= x1 - 2 && c.Z <= roomZ + 1)
                    && !DoorCells(shell).Any(d => Mathf.Abs(d.X - c.X) + Mathf.Abs(d.Z - c.Z) <= 1)).ToList();
            var tables = free.Where(c => (c.X - x0) % 3 == 1 && (c.Z - z0) % 3 == 1).Take(3).ToList();
            foreach (var (x, z) in tables)
            {
                Furnish("supplier-rt-table-bistro", x, z);
                Furnish("supplier-rt-chair-spindle", x - 1, z, 1);
                Furnish("supplier-rt-chair-spindle", x + 1, z, 3);
                Furnish("supplier-rt-vase", x, z);
                Furnish("supplier-rt-pendant", x, z);
            }
            foreach (var (x, z) in free.Where(c => c.X == x0 || c.X == x1).Take(4)) Furnish("supplier-rt-plant-floor", x, z);
            foreach (var x in Enumerable.Range(shell.CellX + 1, shell.Width - 2).Where(x => x % 2 == 0).Take(4))
                Furnish(x % 4 == 0 ? "supplier-rt-wall-art" : "supplier-rt-sconce", x, shell.CellZ + shell.Depth - 1, 2);
            Debug.Log("[Capture] " + string.Join(" ", placed));
            Assert.That(world.OrderShellDurably(me, "window", new ShellOrder { Kind = ShellOrder.Window, BuildingId = shell.Id, X = shell.CellX, Z = z0 + 1, Axis = 1, Style = "mullioned" },
                _root.Options.WorldPath).Accepted, Is.True);
            yield return new WaitForSeconds(2f);
            yield return OpenBuild();
            var build = Build;
            build.SelectOffer("supplier-rt-booth");
            var booth = _root.Offers.Single(x => x.Id == "supplier-rt-booth").Equipment.CreateTemplate();
            var current = _root.ClientSite;
            build.ForcedHover = Enumerable.Range(0, Start.Width * Start.Depth).Select(i => (X: i % Start.Width, Z: i / Start.Width))
                .Where(c => SiteGrid.InsideInterior(shell, c.X, c.Z, 2, 1))
                .First(c => GoodsWorld.FurnishProblem(current, site, booth, new[] { new GridPlacement { X = c.X, Z = c.Z } }, 0, Start) == null);
            yield return new WaitForSeconds(1.5f);
            var camera = LocalAvatar().CameraRig.GetComponentInChildren<Camera>();
            yield return new WaitForEndOfFrame();
            var screen = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, "build-mode-screen.png"), screen.EncodeToPNG());
            UnityEngine.Object.Destroy(screen);
            Capture(camera, Path.Combine(output, "build-mode-world.png"));
            build.ForcedHover = null;
            Interaction.CloseScreen();
            yield return new WaitForSeconds(1f);
            LocalAvatar().Teleport(SiteGridSpace.FootprintCenter(_root.ClientSite.SiteLayouts.Single(), shell.CellX + shell.Width / 2, shell.CellZ + 1, 1, 1));
            yield return new WaitForSeconds(2f);
            Capture(camera, Path.Combine(output, "decorated-top-down.png"));
            File.WriteAllText(Path.Combine(output, "capture-log.txt"), string.Join("\n", placed) + $"\nambience {RestaurantRules.Ambience(_root.ClientSite, site)}\n");
            Assert.That(RestaurantRules.Ambience(_root.ClientSite, site), Is.GreaterThan(20));
        }

        private static void Capture(Camera camera, string path)
        {
            var texture = new RenderTexture(1600, 900, 24);
            var previous = camera.targetTexture;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = previous;
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(image);
        }

        // The host's own avatar: a remote client in the same process owns one too, so ownership alone is ambiguous.
        private PlayerAvatar LocalAvatar() =>
            UnityEngine.Object.FindObjectsByType<PlayerAvatar>().FirstOrDefault(x => x.IsOwner && x.NetworkManager == _root.NetworkManager);

        private void CreateRemote()
        {
            var go = new GameObject("restaurant-test-remote");
            go.SetActive(false);
#if UNITY_EDITOR
            LogAssert.Expect(LogType.Error, new Regex("^SpawnablePrefabs is null on restaurant-test-remote\\."));
#endif
            _remote = go.AddComponent<NetworkManager>();
            _remote.SpawnablePrefabs = _root.NetworkManager.SpawnablePrefabs;
            typeof(NetworkManager).GetField("_persistence", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_remote, NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_remote, false);
            _remoteAuth = go.AddComponent<DevAuthenticator>();
            go.SetActive(true);
            _remote.ServerManager.SetAuthenticator(_remoteAuth);
            var tugboat = go.GetComponent<Tugboat>();
            tugboat.SetPort(_port);
            tugboat.SetClientAddress("127.0.0.1");
            _remoteAuth.SetClientCredentials("Remote", ClientIdentity.LoadOrCreate(Path.Combine(_directory, "remote.db")));
            _remoteSite = new ClientSiteSubscription(_remote);
            _remoteAuth.ClientJoinAnswered += answer => { if (answer.Accepted) _remoteSite.SetPrimary(answer.SiteId); };
        }
    }
}
