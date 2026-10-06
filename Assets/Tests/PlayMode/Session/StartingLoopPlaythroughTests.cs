// P0 starting-loop baseline (docs/starting-loop-p0-plan.md): plays the opening loop of a fresh generated world (WorldGen scene)
// as a host on an isolated save, once through the client's bridge requests (Pass R) and once through virtual Keyboard/Mouse
// devices and UI Toolkit pointer events (Pass I, StartingLoopPlaythroughTests.Input.cs), and records what happened at each step
// S1-S13. It is an audit, not an acceptance test: a step failure is recorded and the pass carries on with the steps that do not
// depend on it, and the records (steps, conservation ledger, console, captures, figure samples) go to
// docs/verification/starting-loop-baseline-20261006/<pass>-<seed>/. Every step boundary checks that item units, company cash
// and recorded charges change only by what the step explains (purchases, bakes, sales); anything else is a blocker. Runs only
// on request: it is ignored unless Temp/starting-loop.flag exists, one test per run. The application's saves are never opened.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Goods.Network;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Customers;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Logistics;
using FoodFactoryGame.Session.Player;
using FoodFactoryGame.Session.WorldMap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed partial class StartingLoopPlaythroughTests
    {
        private const string ScenePath = "Assets/Scenes/WorldGen.unity";
        // TEST-ONLY seeds: the one earlier records use, and one more for layout variance.
        private const string FirstSeed = "piece-two";
        private const string SecondSeed = "p0-second";
        private const string RunFolder = "starting-loop-baseline-20261006";
        // Real seconds S7 watches for sales (plan: 10 real minutes). A flag file line "watch=<seconds>" shortens it, for harness
        // debugging only; recorded runs use the full watch.
        private static float SalesWatchSeconds
        {
            get
            {
                var line = File.Exists(Flag) ? File.ReadAllLines(Flag).FirstOrDefault(x => x.StartsWith("watch=", StringComparison.Ordinal)) : null;
                return line != null && float.TryParse(line.Substring(6), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 600f;
            }
        }

        private readonly InputTestFixture _inputFixture = new InputTestFixture();
        private readonly Dictionary<string, GoodsOutcome> _results = new();
        private readonly List<StepRecord> _steps = new();
        private readonly List<string> _ledger = new();
        private readonly List<string> _console = new();
        private readonly List<string> _figureLog = new();
        private readonly object _logLock = new();
        private readonly List<(LogType Type, string Text, string Step)> _logs = new();
        private int _logsSeen;
        private string _directory;
        private string _output;
        private string _seed;
        private bool _devices;
        private SessionRoot _root;
        private WorldLayoutPresenter _map;
        private StepRecord _current;
        private Books _books;
        private int _requests;
        private bool _ignoredFailingMessages;
        private bool _setUp;

        // State carried between steps.
        private string _ovenId;
        private string _startDockId;
        private string _dinerDockId;
        private string _dinerCounterId;
        private PropertyOffer _diner;
        private string _truckId;
        private string _routeId;

        private sealed class StepFailure : Exception
        {
            public StepFailure(string message) : base(message) { }
        }

        private sealed class StepRecord
        {
            public string Id;
            public string Action;
            public string Result = "pass";
            public string Notes = "";
            public float Seconds;
            public readonly List<string> Values = new();
        }

        // Company cash, recorded charges and item units at a step boundary (decision 0012 cash, 0034 charges).
        private sealed class Books
        {
            public long Cash;
            public long Charged;
            public long Served;
            public int Outcomes;
            public readonly Dictionary<string, long> Units = new();
            public readonly HashSet<string> Equipment = new();
            public readonly HashSet<string> Properties = new();
            public readonly Dictionary<string, string> LotItems = new();
        }

        private static string Flag => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "starting-loop.flag"));

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!File.Exists(Flag)) Assert.Ignore("The P0 playthrough runs on request: create Temp/starting-loop.flag.");
            _inputFixture.Setup();
            _setUp = true;
            _ignoredFailingMessages = LogAssert.ignoreFailingMessages;
            // Errors are findings here, recorded per step, not test failures.
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceivedThreaded += OnLog;
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryStartingLoop", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _map = UnityEngine.Object.FindAnyObjectByType<WorldLayoutPresenter>();
            Assert.That(_root != null && _map != null && _root.GeneratesWorld, Is.True, "WorldGen generates worlds.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                ReleaseDevices();
                if (_root != null) _root.Shutdown();
                _root = null;
                yield return null;
                if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
                _directory = null;
            }
            finally
            {
                if (_setUp)
                {
                    Application.logMessageReceivedThreaded -= OnLog;
                    LogAssert.ignoreFailingMessages = _ignoredFailingMessages;
                    _inputFixture.TearDown();
                }
                _setUp = false;
            }
        }

        [UnityTest, Timeout(5_400_000)]
        public IEnumerator PassRequestsFirstSeed() => Play(false, FirstSeed);

        [UnityTest, Timeout(5_400_000)]
        public IEnumerator PassRequestsSecondSeed() => Play(false, SecondSeed);

        [UnityTest, Timeout(5_400_000)]
        public IEnumerator PassInputFirstSeed() => Play(true, FirstSeed);

        [UnityTest, Timeout(5_400_000)]
        public IEnumerator PassInputSecondSeed() => Play(true, SecondSeed);

        private IEnumerator Play(bool devices, string seed)
        {
            _devices = devices;
            _seed = seed;
            // A flag file line "folder=<name>" writes the records to docs/verification/<name>/ instead (later evidence runs).
            var folder = File.ReadAllLines(Flag).FirstOrDefault(x => x.StartsWith("folder=", StringComparison.Ordinal))?.Substring(7) ?? RunFolder;
            _output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "verification", folder, $"pass-{(devices ? "i" : "r")}-{seed}"));
            Directory.CreateDirectory(_output);
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"), IdentityPath = Path.Combine(_directory, "host.db"), DisplayName = "Host",
                Address = "127.0.0.1", WorldSeed = seed
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _root.NetworkManager.TransportManager.Transport.SetPort((ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port);
            if (devices) AddDevices();
            Header();

            yield return Step("S1", "Create the world, host joins", S1());
            yield return Step("S2", "Walk in through the front door", S2(), "S1");
            yield return Step("S3", "Supplier: buy an oven and dough", S3(), "S1");
            yield return Step("S4", "Place the oven inside the shell", S4(), "S3");
            yield return Step("S5", "Load dough, bake, take bread out", S5(), "S4");
            yield return Step("S6", "Put bread into the register, work it", S6(), "S5");
            yield return Step("S7", "Wait for sales", S7(), "S6");
            yield return Step("S8", "Watch the customer figures", S8(), "S7");
            yield return Step("S9", "Register empty, then unstaffed", S9(), "S6");
            yield return Step("S10", "Build mode: table, decor, wall and door", S10(), "S1");
            yield return Step("S11", "Place a dock on the apron", S11(), "S1");
            yield return Step("S12", "Second restaurant, carry goods, truck route", S12(), "S11");
            yield return Step("S13", "Restart the host mid-loop", S13(), "S1");
            WriteRecord();
            var failed = _steps.Where(x => x.Result != "pass").Select(x => $"{x.Id} {x.Result}: {x.Notes}").ToList();
            Assert.That(failed, Is.Empty, $"Steps not passing (records in {_output}).");
        }

        // Runs one step's coroutine, turning any exception into a recorded failure; then checks the books and the console.
        private IEnumerator Step(string id, string action, IEnumerator body, params string[] needs)
        {
            var record = new StepRecord { Id = id, Action = action };
            _steps.Add(record);
            // Harness debugging only: a flag file line "only=S1,S11,S12" runs just those steps.
            var only = File.ReadAllLines(Flag).FirstOrDefault(x => x.StartsWith("only=", StringComparison.Ordinal))?.Substring(5).Split(',');
            if (only != null && !only.Contains(id))
            {
                record.Result = "skipped (debug)";
                yield break;
            }
            var blocker = needs.FirstOrDefault(x => _steps.Any(s => s.Id == x && s.Result != "pass" && !s.Result.StartsWith("skipped", StringComparison.Ordinal)));
            if (blocker != null)
            {
                record.Result = "blocked";
                record.Notes = $"needs {blocker}";
                Debug.Log($"[P0] {id} blocked by {blocker}");
                yield break;
            }
            _current = record;
            Debug.Log($"[P0] {id} start ({(_devices ? "input" : "requests")}, seed {_seed})");
            var started = Time.realtimeSinceStartup;
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            while (stack.Count > 0)
            {
                bool moved;
                object yielded = null;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) yielded = stack.Peek().Current;
                }
                catch (Exception error)
                {
                    record.Result = "fail";
                    record.Notes = error is StepFailure ? error.Message : error.GetType().Name + ": " + error.Message + " @ " + error.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
                    break;
                }
                if (!moved)
                {
                    stack.Pop();
                    continue;
                }
                if (yielded is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return yielded;
            }
            if (_devices) ReleaseDevices(false);
            if (record.Result != "pass") yield return Capture($"failed-{id.ToLowerInvariant()}");
            record.Seconds = Time.realtimeSinceStartup - started;
            Boundary(record);
            Debug.Log($"[P0] {id} {record.Result} in {record.Seconds:F1} s {record.Notes}");
            _current = null;
        }

        private static StepFailure Failure(string message) => new(message);

        private void Check(bool condition, string message)
        {
            if (!condition) throw Failure(message);
        }

        private void Note(string value)
        {
            _current?.Values.Add(value);
            Debug.Log($"[P0] {_current?.Id}: {value}");
        }

        // A soft observation that marks the step failed but lets it carry on.
        private void Expect(bool condition, string message)
        {
            if (condition || _current == null) return;
            _current.Result = "fail";
            _current.Notes = (_current.Notes + " " + message).Trim();
            Debug.Log($"[P0] {_current.Id} expectation failed: {message}");
        }

        private static IEnumerator Until(Func<bool> predicate, string what, float timeout = 30f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            if (!predicate()) throw new StepFailure($"Timed out after {timeout:F0} s waiting for {what}.");
        }

        private static IEnumerator Seconds(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private PropertyOffer Start => _root.StartOffer;
        private string Me => _root.Authenticator.LocalPlayerId;
        private string Inventory => GoodsWorld.InventoryLocationId(Me);
        private GoodsSnapshot Server => _root.ServerWorld.Snapshot();
        private GoodsSnapshot Client => _root.ClientSite;
        private GoodsNetworkBridge Bridge => _root.ClientSubscription.Bridge;
        private EquipmentInteraction Interaction => UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
        private PlayerHud Hud => UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
        private BuildMode Build => UnityEngine.Object.FindAnyObjectByType<BuildMode>();
        private BuildingPresenter Buildings => UnityEngine.Object.FindAnyObjectByType<BuildingPresenter>();
        private OfferAsset Offer(string id) => _root.Offers.First(x => x != null && x.Id == id);
        private RecipeAsset Sale => _root.Recipes.First(x => x != null && x.IsSale);
        private RecipeAsset Bake => _root.Recipes.First(x => x != null && !x.IsSale && x.StationKind == "oven");
        private GoodsBuilding Shell(GoodsSnapshot site, string siteId = null) => site.Buildings.Single(x => x.SiteId == (siteId ?? Start.SiteId));
        private SiteLayout LayoutOf(string siteId) => Server.SiteLayouts.Single(x => x.SiteId == siteId);

        private PlayerAvatar LocalAvatar() =>
            UnityEngine.Object.FindObjectsByType<PlayerAvatar>().FirstOrDefault(x => x.IsOwner && x.NetworkManager == _root.NetworkManager);

        private long Units(GoodsSnapshot site, string locationId, string itemId) =>
            site.Lots.Where(x => x.LocationId == locationId && x.ItemId == itemId).Sum(x => (long)x.Quantity);

        private string Held(GoodsSnapshot site) =>
            string.Join(", ", site.Lots.Where(x => x.LocationId == Inventory).GroupBy(x => x.ItemId).Select(x => $"{x.Key} x{x.Sum(y => y.Quantity)}")
                .Concat(site.Equipment.Where(x => x.State == EquipmentState.Held && x.HolderId == Me).Select(x => $"machine {x.Kind}")));

        private static string Cash(long cents) => PlayerHud.FormatCash(cents);

        // Pass R: one bridge request and its reply.
        private IEnumerator Request(string label, Action<string> send, float timeout = 30f)
        {
            var id = $"p0-{++_requests}-{label}";
            send(id);
            yield return Until(() => _results.ContainsKey(id), $"the server's reply to {label}", timeout);
            _lastRequest = id;
        }

        private string _lastRequest;
        private GoodsOutcome Last => _results[_lastRequest];

        // ---------------------------------------------------------------- shared start and observation

        private IEnumerator StartHost()
        {
            var began = Time.realtimeSinceStartup;
            Check(_root.Begin(SessionMode.Host), "Begin(Host) refused.");
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && _map.Generated, "host baseline and map", 120f);
            _root.ClientSubscription.ResultReceived += x => _results[x.RequestId] = x;
            yield return Until(() => LocalAvatar() != null, "local avatar");
            // Playable: the avatar stands on ground and the crosshair (or free pointer) is live.
            yield return Until(() => Interaction.PointerLocked || (LocalAvatar().CameraRig.TopDown), "controls live", 30f);
            Note($"time to playable {Time.realtimeSinceStartup - began:F1} s (Begin to avatar with live controls)");
        }

        private IEnumerator S1()
        {
            yield return StartHost();
            _books = Take(Server);
            var site = Server;
            var company = site.Companies.Single();
            Note($"seed {_seed}; start lot {Start.LotId}, site {Start.SiteId}, building {Start.BuildingId} {Start.BuildingWidth}x{Start.BuildingDepth} at ({Start.BuildingX},{Start.BuildingZ}) in lot {Start.Width}x{Start.Depth}");
            Note($"cash {Cash(company.Cash)} (expected {Cash(GeneratedWorld.StartingCash)})");
            Expect(company.Cash == GeneratedWorld.StartingCash, "starting cash differs");
            var equipment = site.Equipment.Where(x => x.SiteId == Start.SiteId).Select(x => $"{x.Id}:{x.Kind}@({x.CellX},{x.CellZ})").ToList();
            Note("start equipment " + string.Join(", ", equipment));
            Expect(site.Equipment.Select(x => x.Id).OrderBy(x => x).SequenceEqual(new[] { GeneratedWorld.StartCounterId, GeneratedWorld.StartTableId, GeneratedWorld.StartDockId }.OrderBy(x => x)),
                "start equipment is not exactly start-counter, start-table and start-dock");
            var avatar = LocalAvatar().transform.position;
            var cell = SiteGridSpace.AnchorAt(LayoutOf(Start.SiteId), avatar, 1, 1);
            Note($"spawn {avatar} = cell ({cell.X},{cell.Z}); inside building: {SiteGrid.Overlaps(cell.X, cell.Z, 1, 1, Start.BuildingX, Start.BuildingZ, Start.BuildingWidth, Start.BuildingDepth)}");
            Note("starter inventory " + Held(site));
            Note($"doors {string.Join(" ", Shell(site).Doors.Select(d => $"({d.X},{d.Z})"))}; street side {SiteStreet.Outward(LayoutOf(Start.SiteId), site.Buildings)}");
            Note($"districts {site.Districts.Count}, competitors {site.Competitors.Count}");
            yield return Capture("s1-start");
        }

        // The building's front door (the one on its street side) and the unit vector out of it, in scene space.
        private (GridCell Door, Vector3 Outward) FrontDoor(string siteId)
        {
            var layout = LayoutOf(siteId);
            var shell = Shell(Server, siteId);
            var street = SiteStreet.Outward(layout, Server.Buildings) ?? new Vector2Int(0, -1);
            Vector2Int Facing(GridCell d) => d.Z == shell.CellZ ? new Vector2Int(0, -1) : d.Z == shell.CellZ + shell.Depth - 1 ? new Vector2Int(0, 1)
                : d.X == shell.CellX ? new Vector2Int(-1, 0) : new Vector2Int(1, 0);
            var doors = shell.Doors.Concat(shell.Structures.Where(s => s.Kind == GoodsWorld.DoorStructure).Select(s => new GridCell { X = s.X, Z = s.Z })).ToList();
            var door = doors.FirstOrDefault(d => Facing(d) == street) ?? doors.First();
            var facing = Facing(door);
            return (door, new Vector3(facing.x, 0f, facing.y));
        }

        private Vector3 CellPoint(string siteId, int x, int z) => SiteGridSpace.FootprintCenter(LayoutOf(siteId), x, z, 1, 1);

        private bool Indoors => Buildings.LocalBuildingId != null;

        private IEnumerator S2()
        {
            var (door, outward) = FrontDoor(Start.SiteId);
            var doorPoint = CellPoint(Start.SiteId, door.X, door.Z);
            Note($"front door cell ({door.X},{door.Z}) facing {outward}");
            var swings = UnityEngine.Object.FindObjectsByType<DoorSwing>().OrderBy(x => (x.Doorway - doorPoint).sqrMagnitude).FirstOrDefault();
            var maxAngle = 0f;
            if (_devices)
            {
                yield return WalkTo(doorPoint + outward * 2f, 0.6f, "outside the front door");
                yield return WalkTo(doorPoint - outward * 2.5f, 0.6f, "inside the front door",
                    () => maxAngle = Mathf.Max(maxAngle, swings != null ? Mathf.Abs(swings.Angle) : 0f));
            }
            else
            {
                // No request walks: Pass R moves the avatar as the other session tests do.
                LocalAvatar().Teleport(doorPoint + outward * 2f);
                yield return Seconds(0.5f);
                LocalAvatar().Teleport(doorPoint - outward * 2.5f);
                for (var t = 0f; t < 2f; t += Time.unscaledDeltaTime)
                {
                    maxAngle = Mathf.Max(maxAngle, swings != null ? Mathf.Abs(swings.Angle) : 0f);
                    yield return null;
                }
            }
            yield return Until(() => Indoors, "the avatar inside the building", 5f);
            yield return Seconds(1f);
            var rig = LocalAvatar().CameraRig;
            Note($"door swing found {swings != null}, largest angle {maxAngle:F0} deg");
            Note($"indoors: building {Buildings.LocalBuildingId}, camera top-down {rig.TopDown}, pointer locked {Interaction.PointerLocked}, crosshair aims with {(Interaction.PointerLocked ? "the centre" : "the free pointer")}");
            Expect(rig.TopDown, "the camera is not top-down indoors (decision 0019)");
            Expect(swings == null || maxAngle > 10f, "the front door did not swing open");
            yield return Capture("s2-indoors");
        }

        private IEnumerator S3()
        {
            var listed = _root.Offers.Where(x => x != null && x.Truck == null && (x.Equipment == null || string.IsNullOrEmpty(x.Equipment.Category))).ToList();
            Note("supplier window offers: " + string.Join("; ", listed.Select(x => $"{x.Id} {x.Quantity} {(x.Equipment != null ? x.Equipment.Kind : x.ItemId)} {Cash(x.PriceCents)}")));
            var before = Server.Companies.Single().Cash;
            if (_devices) yield return BuyWithDevices(new[] { "supplier-oven", "supplier-dough-5" });
            else
            {
                yield return Request("buy-oven", id => Bridge.RequestPurchase(id, Start.SiteId, "supplier-oven"));
                Check(Last.Accepted, "oven purchase refused: " + Last.Reason);
                yield return Request("buy-dough", id => Bridge.RequestPurchase(id, Start.SiteId, "supplier-dough-5"));
                Check(Last.Accepted, "dough purchase refused: " + Last.Reason);
            }
            yield return Until(() => Client.Equipment.Any(x => x.HolderId == Me && x.Kind == "oven"), "the held oven in the client's baseline");
            var after = Server.Companies.Single().Cash;
            var spent = before - after;
            Note($"debit {Cash(spent)} (expected {Cash(Offer("supplier-oven").PriceCents + Offer("supplier-dough-5").PriceCents)})");
            Expect(spent == Offer("supplier-oven").PriceCents + Offer("supplier-dough-5").PriceCents, "debit differs from the two prices");
            Note("goods land in: " + Held(Server) + $" (location {Inventory})");
            _ovenId = Server.Equipment.First(x => x.HolderId == Me && x.Kind == "oven").Id;
        }

        // Every interior anchor of the 3x3 oven and the ghost's verdict there (the same pure rule the server applies).
        private (int X, int Z) OvenAnchor()
        {
            var site = Server;
            var oven = site.Equipment.Single(x => x.Id == _ovenId);
            var shell = Shell(site);
            var verdicts = new Dictionary<string, int>();
            var valid = new List<(int X, int Z)>();
            for (var z = shell.CellZ - 1; z <= shell.CellZ + shell.Depth; z++)
            for (var x = shell.CellX - 1; x <= shell.CellX + shell.Width; x++)
            {
                if (!SiteGrid.InsideInterior(shell, x, z, oven.Width, oven.Depth)) continue;
                var problem = SiteGrid.PlacementProblem(site, oven, x, z, 0, 0) ?? "ok";
                verdicts[problem] = verdicts.GetValueOrDefault(problem) + 1;
                if (problem == "ok") valid.Add((x, z));
            }
            Note($"oven {oven.Width}x{oven.Depth} interior anchors: " + string.Join(", ", verdicts.Select(x => $"{x.Key} {x.Value}")));
            Check(valid.Count > 0, "the 3x3 oven fits nowhere inside the starting shell");
            // As far from the doors as possible, so it never blocks the way in (the rule the session sale test uses).
            return valid.OrderByDescending(c => shell.Doors.Min(d => Mathf.Abs(d.X - (c.X + 1)) + Mathf.Abs(d.Z - (c.Z + 1)))).First();
        }

        private IEnumerator S4()
        {
            var anchor = OvenAnchor();
            if (_devices) yield return PlaceHeldWithDevices("oven", anchor);
            else
            {
                yield return Request("place-oven", id => Bridge.RequestPlace(id, _ovenId, anchor.X, anchor.Z, 0));
                Check(Last.Accepted, "placement refused: " + Last.Reason);
            }
            yield return Until(() => Server.Equipment.Any(x => x.Id == _ovenId && x.State == EquipmentState.Placed), "the oven placed");
            var placed = Server.Equipment.Single(x => x.Id == _ovenId);
            Note($"oven placed at ({placed.CellX},{placed.CellZ}) rotation {placed.Rotation}");
            yield return Capture("s4-oven");
        }

        private IEnumerator S5()
        {
            var oven = Server.Equipment.Single(x => x.Id == _ovenId);
            var dough = Units(Server, Inventory, "dough");
            Note($"dough held {dough}; recipe {Bake.Id}: {Bake.Inputs[0].quantity} {Bake.Inputs[0].itemId} -> {Bake.OutputQuantity} {Bake.OutputItemId} in {Bake.DurationSeconds} s");
            var loaded = Time.realtimeSinceStartup;
            if (_devices) yield return LoadOvenWithDevices(oven);
            else
                foreach (var lot in Server.Lots.Where(x => x.LocationId == Inventory && x.ItemId == "dough").ToList())
                {
                    yield return Request("load-oven", id => Bridge.RequestTransfer(id, lot.Id, oven.InputLocationId, lot.Quantity));
                    Check(Last.Accepted, "dough into the oven refused: " + Last.Reason);
                }
            var inOven = Units(Server, oven.InputLocationId, "dough") + Server.Jobs.Where(x => x.StationId == oven.Id).SelectMany(x => x.Inputs).Sum(x => (long)x.Quantity);
            Note($"dough in the oven {inOven}");
            yield return Until(() => Server.Jobs.Any(x => x.StationId == oven.Id) || Units(Server, oven.OutputLocationId, "bread") > 0, "the oven to start by itself", 10f);
            var started = Time.realtimeSinceStartup;
            Note($"auto-start after {started - loaded:F1} s");
            yield return Until(() => Units(Server, oven.OutputLocationId, "bread") > 0, "the first bread", 60f);
            Note($"first bread after {Time.realtimeSinceStartup - started:F1} s");
            yield return Until(() => !Server.Jobs.Any(x => x.StationId == oven.Id) && Units(Server, oven.InputLocationId, "dough") == 0
                || Server.Jobs.Any(x => x.StationId == oven.Id && x.State == StationJobState.Blocked), "the oven to finish or stop", 30f + 15f * inOven);
            var site = Server;
            var bread = site.Lots.Where(x => x.LocationId == oven.OutputLocationId && x.ItemId == "bread").ToList();
            Note($"oven output {bread.Sum(x => x.Quantity)} bread after {Time.realtimeSinceStartup - started:F1} s; dough left in input {Units(site, oven.InputLocationId, "dough")}; blocked {site.Jobs.Any(x => x.StationId == oven.Id && x.State == StationJobState.Blocked)}");
            Note("spoil timers: " + string.Join(", ", bread.Select(x => $"{x.Quantity} bread {x.SpoilAfterSeconds - x.ExposureSeconds} s left of {x.SpoilAfterSeconds}")));
            if (_devices) yield return TakeOvenOutputWithDevices(oven);
            else
                foreach (var lot in bread)
                {
                    yield return Request("take-bread", id => Bridge.RequestTransfer(id, lot.Id, Inventory, lot.Quantity));
                    Check(Last.Accepted, "taking bread refused: " + Last.Reason);
                }
            yield return Until(() => Units(Server, Inventory, "bread") > 0, "bread in hand");
            Note("held after baking: " + Held(Server));
            Expect(Units(Server, oven.OutputLocationId, "bread") == 0, "bread left in the oven output");
        }

        private IEnumerator S6()
        {
            var counter = Server.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId);
            // Everything held: the slot UI moves whole stacks, so both passes stock all of it (S9 takes back what is left).
            var stock = Units(Server, Inventory, "bread");
            Check(stock > 0, "no bread to stock the register");
            if (_devices) yield return StockAndStaffWithDevices(counter, (int)stock);
            else
            {
                var left = stock;
                foreach (var lot in Server.Lots.Where(x => x.LocationId == Inventory && x.ItemId == "bread").ToList())
                {
                    if (left <= 0) break;
                    var take = (int)Math.Min(left, lot.Quantity);
                    left -= take;
                    yield return Request("stock-register", id => Bridge.RequestTransfer(id, lot.Id, counter.InputLocationId, take));
                    Check(Last.Accepted, "bread onto the register refused: " + Last.Reason);
                }
                yield return Request("staff", id => Bridge.RequestStaff(id, counter.Id, Me));
                Check(Last.Accepted, "staffing refused: " + Last.Reason);
            }
            yield return Until(() => Server.Equipment.Single(x => x.Id == counter.Id).StaffId == Me, "the register staffed by the host");
            Note($"register stocked with {Units(Server, counter.InputLocationId, "bread")} bread, staffed by the host");
            yield return Capture("s6-register");

            // Does staffing persist while the player walks away and uses the oven?
            var oven = Server.Equipment.Single(x => x.Id == _ovenId);
            if (_devices) yield return UseOvenAndWalkOutWithDevices(oven);
            else
            {
                var (door, outward) = FrontDoor(Start.SiteId);
                // Using the oven: its screen opened and closed (nothing is left to bake).
                Interaction.OpenMachine(oven.Id);
                yield return Seconds(1f);
                Interaction.CloseScreen();
                LocalAvatar().Teleport(CellPoint(Start.SiteId, door.X, door.Z) + outward * 3f);
                yield return Seconds(2f);
                LocalAvatar().Teleport(CellPoint(Start.SiteId, door.X, door.Z) - outward * 2.5f);
                yield return Seconds(2f);
            }
            var staff = Server.Equipment.Single(x => x.Id == counter.Id).StaffId;
            Note($"after walking out of the door and using the oven, register staff = '{staff}'");
            Expect(staff == Me, "staffing did not persist while the player walked away or used the oven");
        }

        // ---------------------------------------------------------------- S7/S8: sales and figures

        private readonly Dictionary<string, List<(float T, Vector3 P, bool Active)>> _figures = new();
        // Customers that chose the starting restaurant during S7; only their figures are judged in S8.
        private readonly HashSet<string> _ourCustomers = new();

        private IEnumerator S7()
        {
            var site = Start.SiteId;
            var began = Time.realtimeSinceStartup;
            var clockBegan = Server.ClockSeconds;
            var cash0 = Server.Companies.Single().Cash;
            var served0 = Served(Server, site);
            var walked0 = WalkedOut(Server, site);
            var arrivals = _ourCustomers;
            var shell = Shell(Server);
            var layout = LayoutOf(Start.SiteId);
            var emptyNoted = false;
            float? firstArrival = null, firstSale = null, firstSeat = null;
            var captured = false;
            var frames = new List<float>();
            var presenter = UnityEngine.Object.FindAnyObjectByType<CustomerPresenter>();
            var nextSample = 0f;
            while (Time.realtimeSinceStartup - began < SalesWatchSeconds)
            {
                var now = Time.realtimeSinceStartup - began;
                if (frames.Count < 300) frames.Add(Time.unscaledDeltaTime);
                SampleFigures(presenter, now);
                if (now >= nextSample)
                {
                    nextSample = now + 2f;
                    var state = Server;
                    foreach (var customer in state.Customers.Where(x => x.RestaurantId == site)) arrivals.Add(customer.Id);
                    if (firstArrival == null && arrivals.Count > 0) firstArrival = now;
                    if (firstSale == null && Served(state, site) > served0) firstSale = now;
                    if (firstSeat == null && state.Customers.Any(x => x.RestaurantId == site && x.State == CustomerState.Eating && x.TableId != "")) firstSeat = now;
                    if (!emptyNoted && Units(state, GeneratedWorld.StartCounterId + ":in", "bread") == 0)
                    {
                        emptyNoted = true;
                        _figureLog.Add($"register empty at {now:F0} s");
                    }
                }
                if (!captured && FigureInside(presenter, shell, layout))
                {
                    captured = true;
                    yield return Capture("s7-customers-inside");
                    yield return CaptureOverview("s7-overview", Start.SiteId);
                }
                yield return null;
            }
            var end = Server;
            var sales = Served(end, site) - served0;
            var cash = end.Companies.Single().Cash - cash0;
            Note($"watched {SalesWatchSeconds:F0} real s = {end.ClockSeconds - clockBegan} clock s");
            Note($"first arrival {Show(firstArrival)}, first sale {Show(firstSale)}, first seated diner {Show(firstSeat)} (real s after staffing)");
            Note($"customers that chose this restaurant {arrivals.Count}, sales {sales}, walk-outs {WalkedOut(end, site) - walked0}");
            Note($"cash delta {Cash(cash)}; sales x {Cash(Sale.SaleCents)} = {Cash(sales * Sale.SaleCents)}");
            Expect(cash == sales * Sale.SaleCents, "cash delta is not sales x price");
            Note(FrameSummary("S7", frames));
            if (!captured)
            {
                yield return Capture("s7-no-customer-inside");
                yield return CaptureOverview("s7-overview", Start.SiteId);
            }
            Expect(sales > 0, "no sale in 10 real minutes");
        }

        private static string Show(float? seconds) => seconds.HasValue ? $"{seconds.Value:F0} s" : "never";

        private long Served(GoodsSnapshot state, string siteId) => state.Diners.Where(x => x.RestaurantId == siteId).Sum(x => x.Served);
        private long WalkedOut(GoodsSnapshot state, string siteId) => state.Diners.Where(x => x.RestaurantId == siteId).Sum(x => x.WalkedOut);

        private static string FrameSummary(string step, List<float> frames)
        {
            if (frames.Count == 0) return $"{step} frame time: no frames";
            var sorted = frames.OrderBy(x => x).ToList();
            return $"{step} frame time over {frames.Count} frames: mean {frames.Average() * 1000f:F1} ms, p95 {sorted[(int)(sorted.Count * 0.95f)] * 1000f:F1} ms, max {sorted.Last() * 1000f:F1} ms (Editor)";
        }

        private void SampleFigures(CustomerPresenter presenter, float now)
        {
            if (presenter == null) return;
            foreach (Transform child in presenter.transform)
            {
                if (!child.name.StartsWith("Customer ", StringComparison.Ordinal)) continue;
                if (!_figures.TryGetValue(child.name, out var samples)) _figures[child.name] = samples = new List<(float, Vector3, bool)>();
                if (samples.Count == 0 || now - samples[samples.Count - 1].T >= 0.2f) samples.Add((now, child.position, child.gameObject.activeSelf));
            }
        }

        private bool FigureInside(CustomerPresenter presenter, GoodsBuilding shell, SiteLayout layout)
        {
            if (presenter == null) return false;
            foreach (Transform child in presenter.transform)
            {
                if (!child.gameObject.activeSelf || !child.name.StartsWith("Customer ", StringComparison.Ordinal)) continue;
                var (x, z) = SiteGridSpace.AnchorAt(layout, child.position, 1, 1);
                if (SiteGrid.IsInterior(shell, x, z)) return true;
            }
            return false;
        }

        private IEnumerator S8()
        {
            var shell = Shell(Server);
            var layout = LayoutOf(Start.SiteId);
            var doors = new HashSet<(int, int)>(shell.Doors.Select(d => (d.X, d.Z)).Concat(shell.Structures.Where(s => s.Kind == GoodsWorld.DoorStructure).Select(s => (s.X, s.Z))));
            var counter = Server.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId);
            int entered = 0, byDoor = 0, clipped = 0, nearCounter = 0, hidden = 0;
            var lines = new List<string>();
            foreach (var (name, samples) in _figures.Where(x => _ourCustomers.Contains(x.Key.Substring("Customer ".Length))))
            {
                var cells = samples.Select(s => (s.T, Cell: SiteGridSpace.AnchorAt(layout, s.P, 1, 1), s.Active)).ToList();
                var wall = cells.Where(c => c.Active && SiteGrid.IsWall(shell, c.Cell.X, c.Cell.Z) && !doors.Contains(c.Cell)).ToList();
                var insideAt = cells.FindIndex(c => c.Active && SiteGrid.IsInterior(shell, c.Cell.X, c.Cell.Z));
                var through = "";
                if (insideAt > 0)
                {
                    entered++;
                    var crossing = cells.Take(insideAt + 1).Skip(Math.Max(0, insideAt - 3)).Select(c => c.Cell).ToList();
                    var nearDoor = crossing.Any(c => doors.Any(d => Math.Abs(d.Item1 - c.X) + Math.Abs(d.Item2 - c.Z) <= 1));
                    if (nearDoor) byDoor++;
                    through = nearDoor ? "door" : $"not by a door, cells {string.Join(" ", crossing.Select(c => $"({c.X},{c.Z})"))}";
                }
                if (wall.Count > 0) clipped++;
                if (cells.Any(c => c.Active && Math.Abs(c.Cell.X - (counter.CellX + 1)) <= 1 && c.Cell.Z >= counter.CellZ - 2 && c.Cell.Z < counter.CellZ)) nearCounter++;
                if (cells.Any(c => !c.Active)) hidden++;
                lines.Add($"{name}: {samples.Count} samples {samples.First().T:F0}-{samples.Last().T:F0} s; entered {(insideAt > 0 ? through : "no")}; wall-cell samples {wall.Count}"
                    + (wall.Count > 0 ? $" first at ({wall[0].Cell.X},{wall[0].Cell.Z}) t={wall[0].T:F0}" : "") + $"; hidden {cells.Count(c => !c.Active)}");
            }
            File.WriteAllLines(Path.Combine(_output, "figures.txt"), lines.Concat(_figureLog));
            Note($"figures drawn {_figures.Count} (all restaurants), of customers who chose this restaurant {lines.Count} (of {_ourCustomers.Count} such customers); entered the shell {entered} (by a door {byDoor}); stood at the counter's ordering cells {nearCounter}; hidden on arrival (seated) {hidden}; with samples in a wall cell {clipped}");
            Check(lines.Count > 0, "no figure of a customer of this restaurant was drawn during S7");
            Expect(entered == byDoor, "a figure entered the shell not by a door");
            Expect(clipped == 0, "a figure stood in a wall cell");
            yield break;
        }

        // ---------------------------------------------------------------- S9: what the player is told

        private IEnumerator S9()
        {
            var counter = Server.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId);
            // Empty: the bread left in the register comes back to the player (the register would otherwise take hours to empty).
            if (_devices) yield return EmptyRegisterWithDevices(counter);
            else
                foreach (var lot in Server.Lots.Where(x => x.LocationId == counter.InputLocationId).ToList())
                {
                    yield return Request("empty-register", id => Bridge.RequestTransfer(id, lot.Id, Inventory, lot.Quantity));
                    Check(Last.Accepted, "taking bread off the register refused: " + Last.Reason);
                }
            yield return Until(() => Units(Server, counter.InputLocationId, "bread") == 0, "the register empty");
            yield return Seconds(1f);
            yield return ReadRegisterScreen("empty, staffed");
            if (_devices) yield return ClickStaffButtonWithDevices(counter, false);
            else
            {
                yield return Request("leave-register", id => Bridge.RequestStaff(id, counter.Id, ""));
                Check(Last.Accepted, "leaving the register refused: " + Last.Reason);
            }
            yield return Until(() => Server.Equipment.Single(x => x.Id == counter.Id).StaffId == "", "the register unstaffed");
            yield return Seconds(1f);
            yield return ReadRegisterScreen("empty, unstaffed");
            if (Interaction.Screen != InteractionScreen.None) Interaction.CloseScreen();
            yield return null;
            Note($"world HUD line with no screen open: '{Interaction.Status}'");
        }

        // The register screen's texts as the player sees them (opening a screen is presentation only).
        private IEnumerator ReadRegisterScreen(string label)
        {
            if (!_devices && Interaction.Screen != InteractionScreen.Machine) Interaction.OpenMachine(GeneratedWorld.StartCounterId);
            if (_devices && Interaction.Screen != InteractionScreen.Machine) yield return OpenMachineWithDevices(GeneratedWorld.StartCounterId);
            yield return Until(() => Hud.ScreenRoot.Q<Label>("hud-progress-label") != null, "the register screen");
            yield return null;
            var progress = Hud.ScreenRoot.Q<Label>("hud-progress-label")?.text;
            var staff = Hud.ScreenRoot.Q<Label>("hud-staff-label")?.text;
            Note($"register screen ({label}): '{progress}' / '{staff}'; status line '{Interaction.Status}'");
            yield return Capture("s9-" + label.Replace(", ", "-"));
        }

        // ---------------------------------------------------------------- S10: build mode

        private IEnumerator S10()
        {
            var site = Server;
            var shell = Shell(site);
            var ambience = RestaurantRules.Ambience(site, Start.SiteId);
            var ledger = site.Companies.Single().Cash;
            var table = FreeInterior(site, 1, 1, null);
            // Wall art on the back (north) wall, as the restaurant session test hangs it.
            var art = Enumerable.Range(shell.CellX + 1, shell.Width - 2).Select(x => (X: x, Z: shell.CellZ + shell.Depth - 1))
                .First(c => SiteGrid.IsWall(shell, c.X, c.Z) && SiteGrid.CellProblem(site, Start.SiteId, c.X, c.Z, 1, 1, null, 0, SiteGrid.WallLayer) == null);
            var wall = FreeInterior(site, 1, 2, c => !SiteGrid.Overlaps(c.X, c.Z, 1, 3, table.X, table.Z - 1, 1, 3));
            Note($"chosen cells: bistro table ({table.X},{table.Z}), wall art ({art.X},{art.Z}), interior wall ({wall.X},{wall.Z})-({wall.X},{wall.Z + 1}), door in it at ({wall.X},{wall.Z})");
            if (_devices) yield return BuildWithDevices(table, art, wall);
            else yield return BuildWithRequests(shell, table, art, wall);
            site = Server;
            var tableId = site.Equipment.FirstOrDefault(x => x.Kind == "rt-table-bistro")?.Id;
            Check(tableId != null, "the bistro table was not placed");
            Note($"cash {Cash(ledger)} -> {Cash(site.Companies.Single().Cash)}; ambience {ambience} -> {RestaurantRules.Ambience(site, Start.SiteId)} (wall art sold back at the end)");
            Note("build outcomes: " + string.Join("; ", site.Outcomes.Where(x => x.PlayerId == Me && x.Cents != 0).Select(x => $"{x.Reason} {Cash(x.Cents)}")));
            var walkable = RestaurantRules.Walkable(site, Start.SiteId);
            var reached = RestaurantRules.Reached(walkable, RestaurantRules.StreetCells(LayoutOf(Start.SiteId), Start));
            foreach (var piece in site.Equipment.Where(x => x.SiteId == Start.SiteId && GoodsWorld.IsTable(x) && x.State == EquipmentState.Placed))
                Note($"seat reachability: {piece.Id} ({piece.Kind}, {piece.Seats} seats) reached from the street: {RestaurantRules.Touches(reached, piece)}");
            Note($"register reached from the street: {RestaurantRules.Touches(reached, site.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId))}");
            yield return CaptureOverview("s10-build", Start.SiteId);
        }

        // Interior cells (no wall) the build preview accepts for a width x depth piece, farthest from the doors first.
        private (int X, int Z) FreeInterior(GoodsSnapshot site, int width, int depth, Func<(int X, int Z), bool> extra)
        {
            var shell = Shell(site);
            var doors = shell.Doors.Select(d => (d.X, d.Z)).ToList();
            var counter = site.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId);
            return Enumerable.Range(0, Start.Width * Start.Depth).Select(i => (X: i % Start.Width, Z: i / Start.Width))
                .Where(c => SiteGrid.InsideInterior(shell, c.X, c.Z, width, depth) && SiteGrid.CellProblem(site, Start.SiteId, c.X, c.Z, width, depth, null) == null
                    // Keep clear of the register's ordering rows so customers still reach it.
                    && !SiteGrid.Overlaps(c.X, c.Z, width, depth, counter.CellX - 1, counter.CellZ - 2, counter.Width + 2, counter.Depth + 3)
                    && (extra == null || extra(c)))
                .OrderByDescending(c => doors.Min(d => Mathf.Abs(d.X - c.X) + Mathf.Abs(d.Z - c.Z))).First();
        }

        private IEnumerator BuildWithRequests(GoodsBuilding shell, (int X, int Z) table, (int X, int Z) art, (int X, int Z) wall)
        {
            yield return Request("table", id => Bridge.RequestBuyAndPlace(id, new FurnishOrder
                { SiteId = Start.SiteId, OfferId = "supplier-rt-table-bistro", Placements = { new GridPlacement { X = table.X, Z = table.Z } } }));
            Note($"table: {(Last.Accepted ? "accepted" : Last.Reason)} {Cash(Last.Cents)}");
            yield return Request("art", id => Bridge.RequestBuyAndPlace(id, new FurnishOrder
                { SiteId = Start.SiteId, OfferId = "supplier-rt-wall-art", Placements = { new GridPlacement { X = art.X, Z = art.Z, Rotation = 2 } } }));
            Note($"wall art: {(Last.Accepted ? "accepted" : Last.Reason)} {Cash(Last.Cents)}");
            Note($"ambience with the art {RestaurantRules.Ambience(Server, Start.SiteId)}");
            var partition = new ShellOrder { Kind = ShellOrder.Partition, BuildingId = shell.Id, Style = "plaster" };
            partition.Cells.Add(new GridCell { X = wall.X, Z = wall.Z });
            partition.Cells.Add(new GridCell { X = wall.X, Z = wall.Z + 1 });
            yield return Request("wall", id => Bridge.RequestShellOrder(id, partition));
            Note($"interior wall: {(Last.Accepted ? "accepted" : Last.Reason)} {Cash(Last.Cents)}");
            yield return Request("door", id => Bridge.RequestShellOrder(id, new ShellOrder { Kind = ShellOrder.Door, BuildingId = shell.Id, X = wall.X, Z = wall.Z, Style = GoodsWorld.DoorStyles[0] }));
            Note($"door in it: {(Last.Accepted ? "accepted" : Last.Reason)} {Cash(Last.Cents)}");
            var artId = Server.Equipment.FirstOrDefault(x => x.Kind == "rt-wall-art")?.Id;
            if (artId != null)
            {
                yield return Request("sell-art", id => Bridge.RequestSell(id, artId));
                Note($"wall art sold: {(Last.Accepted ? "accepted" : Last.Reason)} {Cash(Last.Cents)}");
            }
        }

        // ---------------------------------------------------------------- S11: dock on the apron

        private (int X, int Z, int Rotation) DockCell(PropertyOffer lot, GoodsSnapshot snapshot, bool record)
        {
            var template = Offer("supplier-dock").Equipment.CreateTemplate();
            var shell = snapshot.Buildings.Single(x => x.SiteId == lot.SiteId);
            var verdicts = new Dictionary<string, int>();
            (int X, int Z, int Rotation)? first = null;
            for (var rotation = 0; rotation < 2; rotation++)
            for (var z = 0; z < lot.Depth; z++)
            for (var x = 0; x < lot.Width; x++)
            {
                var (w, d) = SiteGrid.Footprint(template.Width, template.Depth, rotation);
                if (SiteGrid.Overlaps(x, z, w, d, shell.CellX, shell.CellZ, shell.Width, shell.Depth)) continue;
                var problem = GoodsWorld.FurnishProblem(snapshot, lot.SiteId, template, new[] { new GridPlacement { X = x, Z = z, Rotation = rotation } }, 0, lot) ?? "ok";
                verdicts[problem] = verdicts.GetValueOrDefault(problem) + 1;
                if (problem == "ok" && first == null) first = (x, z, rotation);
            }
            if (record) Note($"dock {template.Width}x{template.Depth} on {lot.LotId} outside the shell: " + string.Join(", ", verdicts.Select(x => $"{x.Key} {x.Value}")));
            Check(first.HasValue, $"no outdoor cell of {lot.LotId} accepts a dock");
            return first.Value;
        }

        private IEnumerator S11()
        {
            var cell = DockCell(Start, Server, true);
            var before = Server.Equipment.Where(x => x.Kind == GoodsWorld.DockKind).Select(x => x.Id).ToList();
            if (_devices) yield return BuildItemWithDevices("supplier-dock", Start.SiteId, (cell.X, cell.Z), cell.Rotation);
            else
            {
                yield return Request("dock", id => Bridge.RequestBuyAndPlace(id, new FurnishOrder
                    { SiteId = Start.SiteId, OfferId = "supplier-dock", Placements = { new GridPlacement { X = cell.X, Z = cell.Z, Rotation = cell.Rotation } } }));
                Check(Last.Accepted, "dock refused: " + Last.Reason);
            }
            yield return Until(() => Server.Equipment.Any(x => x.Kind == GoodsWorld.DockKind && !before.Contains(x.Id) && x.State == EquipmentState.Placed), "the dock placed");
            var dock = Server.Equipment.First(x => x.Kind == GoodsWorld.DockKind && !before.Contains(x.Id));
            _startDockId = dock.Id;
            Note($"dock {dock.Id} at ({dock.CellX},{dock.CellZ}) rotation {dock.Rotation}; street reach {RestaurantRules.ReachesStreet(Server, dock, Start)}; charged {Cash(dock.ChargedCents)}");
            yield return CaptureOverview("s11-dock", Start.SiteId);
        }

        // ---------------------------------------------------------------- S12: second restaurant and a truck

        private PropertyOffer NearestForSale() => _map.Offers.Values.Where(x => x.ForSale && x.Category == GoodsWorld.RestaurantKind)
            .OrderBy(x => Mathf.Abs(x.LotX + x.Width / 2f - (Start.LotX + Start.Width / 2f)) + Mathf.Abs(x.LotZ + x.Depth / 2f - (Start.LotZ + Start.Depth / 2f)))
            .First();

        private IEnumerator S12()
        {
            _diner = NearestForSale();
            var distance = Vector3.Distance(SitePlacement.Active.SiteOrigin(Start.SiteId), SitePlacement.Active.SiteOrigin(_diner.SiteId));
            Note($"nearest restaurant for sale {_diner.LotId} ({_diner.BuildingId}), {Cash(_diner.PriceCents)}, {distance:F0} m from the start site");
            var cash = Server.Companies.Single().Cash;
            Check(cash >= _diner.PriceCents, "the company cannot afford the nearest restaurant");
            if (_devices) yield return BuyPropertyWithDevices(_diner);
            else
            {
                yield return Request("buy-diner", id => Bridge.RequestBuyProperty(id, Start.SiteId, _diner.LotId));
                Check(Last.Accepted, "purchase refused: " + Last.Reason);
            }
            yield return Until(() => Server.Properties.Any(x => x.LotId == _diner.LotId), "the purchase committed");
            Note($"bought for {Cash(cash - Server.Companies.Single().Cash)}");
            yield return Until(() => _root.DrawnSites.Find(_diner.SiteId) != null, "the bought site drawn", 30f);

            // Walk over carrying goods.
            var carried = Server.Lots.Where(x => x.LocationId == Inventory).Select(x => (x.ItemId, x.Quantity)).OrderBy(x => x.ItemId).ToList();
            Note("carrying " + string.Join(", ", carried.Select(x => $"{x.ItemId} x{x.Quantity}")));
            var walkStart = Time.realtimeSinceStartup;
            if (_devices) yield return WalkToSiteWithDevices(_diner);
            else LocalAvatar().Teleport(SessionRoot.ApronSpawn(_diner, 0).position);
            yield return Until(() => _root.ClientSiteId == _diner.SiteId, "entering the bought restaurant", 30f);
            Note($"entered {_diner.SiteId} after {Time.realtimeSinceStartup - walkStart:F0} s; rejection {_root.SiteEntry.LastRejection ?? "none"}");
            yield return Until(() => Server.Locations.Single(x => x.Id == Inventory).SiteId == _diner.SiteId, "the inventory moved with the player");
            var arrived = Server.Lots.Where(x => x.LocationId == Inventory).Select(x => (x.ItemId, x.Quantity)).OrderBy(x => x.ItemId).ToList();
            Expect(arrived.SequenceEqual(carried), "the carried goods changed on the way");

            // A dock and a staffed, stocked register at the second site.
            var cell = DockCell(_diner, Server, true);
            var docks = Server.Equipment.Where(x => x.Kind == GoodsWorld.DockKind).Select(x => x.Id).ToList();
            if (_devices) yield return BuildItemWithDevices("supplier-dock", _diner.SiteId, (cell.X, cell.Z), cell.Rotation);
            else
            {
                yield return Request("diner-dock", id => Bridge.RequestBuyAndPlace(id, new FurnishOrder
                    { SiteId = _diner.SiteId, OfferId = "supplier-dock", Placements = { new GridPlacement { X = cell.X, Z = cell.Z, Rotation = cell.Rotation } } }));
                Check(Last.Accepted, "second dock refused: " + Last.Reason);
            }
            yield return Until(() => Server.Equipment.Any(x => x.Kind == GoodsWorld.DockKind && !docks.Contains(x.Id) && x.State == EquipmentState.Placed), "the second dock placed");
            _dinerDockId = Server.Equipment.First(x => x.Kind == GoodsWorld.DockKind && !docks.Contains(x.Id)).Id;
            yield return PlaceDinerRegister();
            yield return CaptureOverview("s12-diner", _diner.SiteId);

            // Back home, goods onto the start dock, a truck and a route.
            var back = Time.realtimeSinceStartup;
            if (_devices) yield return WalkToSiteWithDevices(Start);
            else LocalAvatar().Teleport(SessionRoot.ApronSpawn(Start, 0).position);
            yield return Until(() => _root.ClientSiteId == Start.SiteId, "back at the start site", 30f);
            Note($"back home after {Time.realtimeSinceStartup - back:F0} s; register at the second site staffed by '{Server.Equipment.Single(x => x.Id == _dinerCounterId).StaffId}' after leaving");
            // The cargo is a fresh pack of dough: the slot UI moves whole stacks, so the bread stays for the register (S13).
            yield return ShipDough("ship");
            var shipped = (int)Units(Server, _startDockId + ":in", "dough");
            Note($"{shipped} dough on the start dock's outgoing");
            var trucks = Server.Trucks.Select(x => x.Id).ToList();
            var routes = Server.Routes.Select(x => x.Id).ToList();
            if (_devices) yield return TruckRouteWithDevices(_startDockId, _dinerDockId);
            else
            {
                yield return Request("truck", id => Bridge.RequestPurchase(id, Start.SiteId, "supplier-truck"));
                Check(Last.Accepted, "truck refused: " + Last.Reason);
                yield return Request("route", id => Bridge.RequestCreateRoute(id, _startDockId, _dinerDockId, Array.Empty<string>()));
                Check(Last.Accepted, "route refused: " + Last.Reason);
                var route = GoodsWorld.RouteIdFor(Me, _lastRequest);
                yield return Request("assign", id => Bridge.RequestAssignTruck(id, Server.Trucks.First(x => !trucks.Contains(x.Id)).Id, route));
                Check(Last.Accepted, "assignment refused: " + Last.Reason);
            }
            yield return Until(() => Server.Trucks.Any(x => !trucks.Contains(x.Id)) && Server.Routes.Any(x => !routes.Contains(x.Id)), "truck and route", 10f);
            _truckId = Server.Trucks.First(x => !trucks.Contains(x.Id)).Id;
            _routeId = Server.Routes.First(x => !routes.Contains(x.Id)).Id;
            yield return Until(() => Server.Trucks.Single(x => x.Id == _truckId).RouteId == _routeId, "the truck on its route", 10f);
            var dispatched = Time.realtimeSinceStartup;
            var frames = new List<float>();
            float? docked = null, loaded = null;
            while (Time.realtimeSinceStartup - dispatched < 300f && Units(Server, _dinerDockId + ":out", "dough") < shipped)
            {
                var truck = Server.Trucks.Single(x => x.Id == _truckId);
                if (docked == null && truck.Docked) docked = Time.realtimeSinceStartup - dispatched;
                if (loaded == null && truck.State == TruckState.ToDropoff) loaded = Time.realtimeSinceStartup - dispatched;
                if (frames.Count < 300) frames.Add(Time.unscaledDeltaTime);
                yield return null;
            }
            var delivered = Units(Server, _dinerDockId + ":out", "dough");
            Note($"truck: docked at the start {Show(docked)}, left loaded {Show(loaded)}, {delivered}/{shipped} dough delivered after {Time.realtimeSinceStartup - dispatched:F0} s; state {Server.Trucks.Single(x => x.Id == _truckId).State}");
            Note(FrameSummary("S12", frames));
            Expect(delivered == shipped, "the shipped dough did not arrive at the second dock in 5 minutes");
            var dinerSales = Served(Server, _diner.SiteId);
            Note($"sales at the unviewed second site so far {dinerSales}; its register staffed by '{Server.Equipment.Single(x => x.Id == _dinerCounterId).StaffId}', stocked {Units(Server, _dinerCounterId + ":in", "bread")}");
            yield return CaptureOverview("s12-sites", Start.SiteId);
        }

        // A pack of dough bought from the supplier and put on the start dock's Outgoing, all of it, for the route's truck.
        private IEnumerator ShipDough(string label)
        {
            if (_devices)
            {
                yield return BuyWithDevices(new[] { "supplier-dough-5" });
                yield return LoadDockWithDevices(_startDockId, "dough");
            }
            else
            {
                yield return Request(label + "-buy", id => Bridge.RequestPurchase(id, Start.SiteId, "supplier-dough-5"));
                Check(Last.Accepted, "dough purchase refused: " + Last.Reason);
                foreach (var lot in Server.Lots.Where(x => x.LocationId == Inventory && x.ItemId == "dough").ToList())
                {
                    yield return Request(label, id => Bridge.RequestTransfer(id, lot.Id, _startDockId + ":in", lot.Quantity));
                    Check(Last.Accepted, "dough onto the dock refused: " + Last.Reason);
                }
            }
        }

        // A counter bought, placed inside the second shell and worked (not stocked: the slot UI moves whole stacks), then left when
        // the player goes.
        private IEnumerator PlaceDinerRegister()
        {
            var counters = Server.Equipment.Where(x => x.Kind == GoodsWorld.CounterKind).Select(x => x.Id).ToList();
            if (_devices) yield return BuyWithDevices(new[] { "supplier-counter" });
            else
            {
                yield return Request("diner-counter", id => Bridge.RequestPurchase(id, _diner.SiteId, "supplier-counter"));
                Check(Last.Accepted, "counter purchase refused: " + Last.Reason);
            }
            yield return Until(() => Server.Equipment.Any(x => x.Kind == GoodsWorld.CounterKind && !counters.Contains(x.Id)), "the held counter");
            var counter = Server.Equipment.First(x => x.Kind == GoodsWorld.CounterKind && !counters.Contains(x.Id));
            _dinerCounterId = counter.Id;
            var site = Server;
            var shell = Shell(site, _diner.SiteId);
            var anchor = Enumerable.Range(0, _diner.Width * _diner.Depth).Select(i => (X: i % _diner.Width, Z: i / _diner.Width))
                .Where(c => SiteGrid.InsideInterior(shell, c.X, c.Z, counter.Width, counter.Depth) && SiteGrid.PlacementProblem(site, counter, c.X, c.Z, 0, 0) == null
                    && SiteGrid.IsInterior(shell, c.X, c.Z - 2))
                .OrderByDescending(c => shell.Doors.Min(d => Mathf.Abs(d.X - c.X) + Mathf.Abs(d.Z - c.Z))).FirstOrDefault();
            Check(anchor != default, "no cell in the second shell takes a counter");
            if (_devices) yield return PlaceHeldWithDevices(GoodsWorld.CounterKind, anchor);
            else
            {
                yield return Request("place-diner-counter", id => Bridge.RequestPlace(id, counter.Id, anchor.X, anchor.Z, 0));
                Check(Last.Accepted, "counter placement refused: " + Last.Reason);
            }
            yield return Until(() => Server.Equipment.Single(x => x.Id == counter.Id).State == EquipmentState.Placed, "the second register placed");
            if (_devices) yield return StockAndStaffWithDevices(Server.Equipment.Single(x => x.Id == counter.Id), 0);
            else
            {
                yield return Request("staff-diner", id => Bridge.RequestStaff(id, counter.Id, Me));
                Check(Last.Accepted, "staffing the second register refused: " + Last.Reason);
            }
            Note($"second register {counter.Id} staffed by '{Server.Equipment.Single(x => x.Id == counter.Id).StaffId}', stocked {Units(Server, counter.Id + ":in", "bread")}");
        }

        // ---------------------------------------------------------------- S13: restart mid-loop

        private IEnumerator S13()
        {
            var counter = Server.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId);
            // Bread in the register and the register worked again; a truck driving if S12 made one.
            if (Units(Server, Inventory, "bread") > 0 && _root.ClientSiteId == Start.SiteId)
            {
                if (_devices) yield return StockAndStaffWithDevices(counter, (int)Units(Server, Inventory, "bread"));
                else
                {
                    var lot = Server.Lots.First(x => x.LocationId == Inventory && x.ItemId == "bread");
                    yield return Request("restock", id => Bridge.RequestTransfer(id, lot.Id, counter.InputLocationId, lot.Quantity));
                    yield return Request("restaff", id => Bridge.RequestStaff(id, counter.Id, Me));
                }
            }
            if (_truckId != null && _startDockId != null && _root.ClientSiteId == Start.SiteId)
            {
                yield return ShipDough("reship");
                yield return Until(() => Server.Trucks.Single(x => x.Id == _truckId).State == TruckState.ToDropoff, "the truck driving with the dough", 120f);
            }
            yield return Seconds(2f);
            // The books up to here, so the restart's own effect is judged alone below.
            CheckBooks("S13 before restart", _current);
            var before = Server;
            var truck = before.Trucks.FirstOrDefault(x => x.Id == _truckId);
            var queued = before.Customers.Count(x => x.RestaurantId == Start.SiteId && x.State == CustomerState.Queued);
            Note($"before restart: revision {before.Revision}, clock {before.ClockSeconds}, register bread {Units(before, counter.InputLocationId, "bread")}, staff '{before.Equipment.Single(x => x.Id == counter.Id).StaffId}', "
                + $"customers here {before.Customers.Count(x => x.RestaurantId == Start.SiteId)} (queued {queued}), truck {(truck == null ? "none" : $"{truck.State} on road {truck.OnRoad}")}");
            if (queued == 0) Note("no customer was queued at the restart (none could be forced without test-only seeding)");
            // The committed save the restart reads; the live world may be up to one tick commit ahead (decision 0016).
            var committed = GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot();
            Note($"committed revision {committed.Revision} (live {before.Revision}); clock lag {before.ClockSeconds - committed.ClockSeconds} s");
            _root.Shutdown();
            yield return Until(() => _root.CanBegin, "the session stopped", 30f);
            _results.Clear();
            yield return StartHost();
            var after = Server;
            Note($"after restart: revision {after.Revision}, clock {after.ClockSeconds}, client site {_root.ClientSiteId}");
            string Lots(GoodsSnapshot s) => string.Join(" ", s.Lots.GroupBy(x => x.ItemId).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Sum(y => y.Quantity)}"));
            Note($"units committed {Lots(committed)} | after {Lots(after)}");
            Note($"cash committed {Cash(committed.Companies.Single().Cash)} | after {Cash(after.Companies.Single().Cash)}");
            Note($"equipment committed {committed.Equipment.Count} | after {after.Equipment.Count}; trucks {committed.Trucks.Count} | {after.Trucks.Count}; customers {committed.Customers.Count} | {after.Customers.Count}");
            var afterTruck = after.Trucks.FirstOrDefault(x => x.Id == _truckId);
            if (afterTruck != null) Note($"truck after restart {afterTruck.State}, on road {afterTruck.OnRoad}, leg {afterTruck.LegSegmentId}");
            var staffAfter = after.Equipment.Single(x => x.Id == counter.Id).StaffId;
            Note($"register staff after restart '{staffAfter}' (0035: cleared)");
            Expect(string.IsNullOrEmpty(staffAfter), "staffing survived the restart");
            Expect(committed.Equipment.Select(x => x.Id).OrderBy(x => x).SequenceEqual(after.Equipment.Select(x => x.Id).OrderBy(x => x)), "equipment changed across the restart");
            // Which state did the restart resume: the live one at shutdown, or the last committed one (up to 10 s older)?
            var live = Take(before);
            var saved = Take(committed);
            var resumed = Take(after);
            string Same(Books a, Books b) => a.Cash == b.Cash && a.Charged == b.Charged && a.Served == b.Served
                && a.Units.OrderBy(x => x.Key).SequenceEqual(b.Units.OrderBy(x => x.Key)) ? "same" : "differs";
            Note($"resumed vs live at shutdown: {Same(resumed, live)}; vs last commit: {Same(resumed, saved)} (customers may have bought in between)");
            // The boundary judges the restart against the committed state: what a restart can promise (decision 0016).
            _books = saved;
        }

        // ---------------------------------------------------------------- books, console, captures, record

        private Books Take(GoodsSnapshot state)
        {
            var books = new Books();
            var company = state.Companies.Single(x => x.Id == GeneratedWorld.CompanyId);
            books.Cash = company.Cash;
            books.Charged = state.Equipment.Sum(x => x.ChargedCents) + state.Buildings.SelectMany(x => x.Structures).Sum(x => x.ChargedCents);
            books.Served = state.Diners.Where(x => company.SiteIds.Contains(x.RestaurantId)).Sum(x => x.Served);
            books.Outcomes = state.Outcomes.Count;
            foreach (var lot in state.Lots)
            {
                books.Units[lot.ItemId] = books.Units.GetValueOrDefault(lot.ItemId) + lot.Quantity;
                books.LotItems[lot.Id] = lot.ItemId;
            }
            // A running job holds its inputs out of the lots until it finishes (decision 0004).
            foreach (var lot in state.Jobs.Where(x => !x.IsSale).SelectMany(x => x.Inputs)) books.Units[lot.ItemId] = books.Units.GetValueOrDefault(lot.ItemId) + lot.Quantity;
            foreach (var piece in state.Equipment) books.Equipment.Add(piece.Id);
            foreach (var property in state.Properties) books.Properties.Add(property.LotId);
            return books;
        }

        // Conservation: cash plus recorded charges moves only by sales, goods packs, trucks and property; item units only by
        // packs, sales and bakes; equipment disappears only when sold. Anything else is a blocker.
        private void Boundary(StepRecord record)
        {
            CheckBooks(record.Id, record);
            ConsoleSince(record);
        }

        private void CheckBooks(string label, StepRecord record)
        {
            var problems = new List<string>();
            if (_root != null && _root.ServerWorld != null && _books != null)
            {
                var state = Server;
                var now = Take(state);
                var prev = _books;
                // A restart reloads the committed save, whose outcome list may be shorter than the live one was.
                var outcomes = state.Outcomes.Skip(Math.Min(prev.Outcomes, state.Outcomes.Count)).Where(x => x.Accepted).ToList();
                long spend = 0;
                var expected = new Dictionary<string, long>();
                var packs = 0;
                foreach (var outcome in outcomes.Where(x => x.Reason == "bought"))
                {
                    if (!string.IsNullOrEmpty(outcome.EquipmentId))
                    {
                        if (state.Trucks.Any(x => x.Id == outcome.EquipmentId)) spend += Offer("supplier-truck").PriceCents;
                        continue;
                    }
                    var item = now.LotItems.GetValueOrDefault(outcome.MovedLotId ?? "") ?? prev.LotItems.GetValueOrDefault(outcome.MovedLotId ?? "") ?? "dough";
                    var offer = _root.Offers.First(x => x != null && x.Equipment == null && x.Truck == null && x.ItemId == item);
                    spend += offer.PriceCents;
                    expected[item] = expected.GetValueOrDefault(item) + offer.Quantity;
                    packs++;
                }
                foreach (var lot in now.Properties.Where(x => !prev.Properties.Contains(x))) spend += SitePlacement.Active.OfferOf(lot)?.PriceCents ?? 0;
                var sales = now.Served - prev.Served;
                var revenue = sales * Sale.SaleCents;
                foreach (var input in Sale.Inputs) expected[input.itemId] = expected.GetValueOrDefault(input.itemId) - sales * input.quantity;
                var ledger = now.Cash + now.Charged - prev.Cash - prev.Charged;
                if (ledger != revenue - spend)
                    problems.Add($"cash+charges moved {Cash(ledger)}, explained {Cash(revenue - spend)} (sales {sales}, packs {packs}, other spend)");
                // Bakes: dough becomes bread one for one per the oven recipe.
                var input0 = Bake.Inputs[0];
                var residual = now.Units.Keys.Union(prev.Units.Keys).Union(expected.Keys)
                    .ToDictionary(x => x, x => now.Units.GetValueOrDefault(x) - prev.Units.GetValueOrDefault(x) - expected.GetValueOrDefault(x));
                var dough = residual.GetValueOrDefault(input0.itemId);
                var bakes = dough < 0 && -dough % input0.quantity == 0 ? -dough / input0.quantity : 0;
                residual[input0.itemId] = dough + bakes * input0.quantity;
                residual[Bake.OutputItemId] = residual.GetValueOrDefault(Bake.OutputItemId) - bakes * Bake.OutputQuantity;
                foreach (var (item, delta) in residual.Where(x => x.Value != 0)) problems.Add($"{item} units changed by {delta} with no explanation");
                var sold = new HashSet<string>(outcomes.Where(x => x.Reason == "sold").Select(x => x.EquipmentId));
                foreach (var gone in prev.Equipment.Where(x => !now.Equipment.Contains(x) && !sold.Contains(x))) problems.Add($"equipment {gone} disappeared unsold");
                _ledger.Add($"{label}: cash {Cash(prev.Cash)} -> {Cash(now.Cash)}, charges {Cash(prev.Charged)} -> {Cash(now.Charged)}, sales {sales} ({Cash(revenue)}), "
                    + $"spend {Cash(spend)} ({packs} packs), bakes {bakes}, units {string.Join(" ", now.Units.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"))}"
                    + (problems.Count > 0 ? " | UNEXPLAINED: " + string.Join("; ", problems) : " | conserved"));
                _books = now;
            }
            if (problems.Count > 0)
            {
                record.Result = "fail";
                record.Notes = (record.Notes + $" BLOCKER conservation ({label}): " + string.Join("; ", problems)).Trim();
            }
        }

        private void ConsoleSince(StepRecord record)
        {
            List<(LogType Type, string Text, string Step)> fresh;
            lock (_logLock)
            {
                fresh = _logs.Skip(_logsSeen).ToList();
                _logsSeen = _logs.Count;
            }
            var errors = fresh.Count(x => x.Type is LogType.Error or LogType.Exception or LogType.Assert);
            var warnings = fresh.Count(x => x.Type == LogType.Warning);
            record.Values.Add($"console since the last step: {errors} errors, {warnings} warnings");
            foreach (var line in fresh.Where(x => x.Type != LogType.Log || x.Text.StartsWith("[Goods] commits", StringComparison.Ordinal)))
                _console.Add($"{record.Id} {line.Type}: {line.Text.Split('\n')[0]}");
            var commits = fresh.LastOrDefault(x => x.Text.StartsWith("[Goods] commits", StringComparison.Ordinal)).Text;
            if (commits != null) record.Values.Add(commits.Split('\n')[0]);
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            lock (_logLock) _logs.Add((type, condition ?? "", _current?.Id ?? ""));
        }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_output, name + ".png"), texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
            Note($"capture {name}.png");
        }

        // A view from above the site, framed on its lot, rendered by a temporary camera (no HUD).
        private IEnumerator CaptureOverview(string name, string siteId)
        {
            var layout = LayoutOf(siteId);
            var centre = SiteGridSpace.FootprintCenter(layout, 0, 0, layout.Width, layout.Depth);
            var camera = new GameObject("p0-capture").AddComponent<Camera>();
            camera.farClipPlane = 2000f;
            var size = Mathf.Max(layout.Width, layout.Depth) * SiteGrid.CellSize;
            camera.transform.SetPositionAndRotation(centre + new Vector3(0f, size * 1.1f + 6f, -size * 0.6f), Quaternion.Euler(60f, 0f, 0f));
            yield return null;
            for (var pass = 0; pass < 2; pass++)
            {
                var target = new RenderTexture(1600, 900, 24);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                image.Apply();
                if (pass == 1) File.WriteAllBytes(Path.Combine(_output, name + ".png"), image.EncodeToPNG());
                RenderTexture.active = null;
                camera.targetTexture = null;
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(image);
                yield return null;
            }
            UnityEngine.Object.Destroy(camera.gameObject);
            Note($"capture {name}.png (overview)");
        }

        private void Header()
        {
            _ledger.Clear();
            _console.Clear();
            Debug.Log($"[P0] pass {(_devices ? "I (simulated input)" : "R (requests)")}, seed {_seed}, save {_directory}, output {_output}");
        }

        private void WriteRecord()
        {
            var text = new StringBuilder();
            text.AppendLine($"# P0 {(_devices ? "Pass I (simulated input)" : "Pass R (requests)")}, seed `{_seed}`");
            text.AppendLine();
            text.AppendLine($"Run {DateTime.UtcNow:u}; isolated save `{_directory}`; Unity {Application.unityVersion}; screen {Screen.width}x{Screen.height}.");
            text.AppendLine();
            text.AppendLine("| Step | Action | Result | Seconds | Notes |");
            text.AppendLine("|---|---|---|---|---|");
            foreach (var step in _steps) text.AppendLine($"| {step.Id} | {step.Action} | {step.Result} | {step.Seconds.ToString("F1", CultureInfo.InvariantCulture)} | {step.Notes.Replace("|", "/")} |");
            text.AppendLine();
            foreach (var step in _steps)
            {
                text.AppendLine($"## {step.Id} {step.Action}: {step.Result}");
                foreach (var value in step.Values) text.AppendLine($"- {value.Replace("\n", " ")}");
                text.AppendLine();
            }
            File.WriteAllText(Path.Combine(_output, "steps.md"), text.ToString());
            File.WriteAllLines(Path.Combine(_output, "conservation.txt"), _ledger);
            File.WriteAllLines(Path.Combine(_output, "console.txt"), _console);
        }
    }
}
