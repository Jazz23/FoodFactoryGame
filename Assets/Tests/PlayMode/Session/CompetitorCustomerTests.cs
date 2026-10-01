// Decision 0033 in the real WorldGen scene, as a host with a loopback-UDP remote client where needed, on a new generated world in
// a unique temporary directory (the application's saves are never opened). TEST-ONLY customers are bootstrapped straight into
// the server world at the competitor nearest the starting restaurant: both clients receive them in their crowds, a client far
// from a competitor does not, figures first appear off screen, queue outside the door in ticket order and go in when served,
// and at a lowered cap the current site's customers always keep their figures while the cap is never exceeded. An explicit test
// records the frame cost with the cap full and captures for visual review.
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
using FoodFactoryGame.Session.Customers;
using FoodFactoryGame.Session.Player;
using FoodFactoryGame.Session.WorldMap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class CompetitorCustomerTests
    {
        private const string ScenePath = "Assets/Scenes/WorldGen.unity";
        // TEST-ONLY seed of the isolated world; the same one the other WorldGen tests use.
        private const string Seed = "piece-two";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private WorldLayoutPresenter _map;
        private CustomerPresenter _presenter;
        private NetworkManager _remote;
        private DevAuthenticator _remoteAuth;
        private ClientSiteSubscription _remoteSite;
        private ushort _port;

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 20f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input.Setup();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryCompetitorCustomers", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _map = UnityEngine.Object.FindAnyObjectByType<WorldLayoutPresenter>();
            _presenter = UnityEngine.Object.FindAnyObjectByType<CustomerPresenter>();
            Assert.That((_root != null, _map != null, _presenter != null), Is.EqualTo((true, true, true)), "WorldGen has a session, map and customer presenter.");
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"), IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Host", Address = "127.0.0.1", WorldSeed = Seed
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            _root.NetworkManager.TransportManager.Transport.SetPort(_port);
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

        private IEnumerator StartHost()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && _map.Generated, "host baseline and map", 90f);
            yield return Until(() => LocalAvatar() != null && Camera.allCameras.Any(x => x.isActiveAndEnabled), "local avatar and camera");
        }

        private PlayerAvatar LocalAvatar() =>
            UnityEngine.Object.FindObjectsByType<PlayerAvatar>().FirstOrDefault(x => x.IsOwner && x.NetworkManager == _root.NetworkManager);

        private PropertyOffer Start => _root.StartOffer;

        private int FromStart(GoodsCompetitor competitor) =>
            Math.Abs(competitor.MapX - Start.AccessX) + Math.Abs(competitor.MapZ - Start.AccessZ);

        private GoodsCompetitor NearestCompetitor() => _root.ServerWorld.Snapshot().Competitors.Where(x => x.LotId != "")
            .OrderBy(FromStart).ThenBy(x => x.Id, StringComparer.Ordinal).First();

        // TEST-ONLY customers, patient enough never to walk out during a test.
        private void SeedQueued(string restaurantId, IEnumerable<string> ids, string recipeId = "")
        {
            var district = _root.ServerWorld.Snapshot().Districts.OrderBy(x => x.Id, StringComparer.Ordinal).First().Id;
            var ticket = 1_000_000L;
            foreach (var id in ids)
                _root.ServerWorld.Bootstrap(new GoodsCustomer
                {
                    Id = id, DistrictId = district, Appearance = 1, DineIn = true, PatienceSeconds = 3600, State = CustomerState.Queued,
                    RestaurantId = restaurantId, RecipeId = recipeId, Ticket = ticket++
                });
        }

        private static bool Has(GoodsCrowdView crowd, string id) => crowd != null && crowd.Restaurants.Any(r => r.Customers.Any(c => c.Id == id));

        // A figure on screen or walking; one hidden inside a competitor is not shown.
        private bool Shown(string id)
        {
            var figure = Figure(id);
            return figure != null && figure.gameObject.activeSelf;
        }

        private Transform Figure(string id) =>
            _presenter.transform.Cast<Transform>().FirstOrDefault(x => x.name == "Customer " + id);

        [UnityTest]
        public IEnumerator CrowdsReachEachClientOnlyNearItsAvatar()
        {
            yield return StartHost();
            CreateRemote();
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest != null; }, "remote baseline");

            var near = NearestCompetitor();
            var far = _root.ServerWorld.Snapshot().Competitors.Where(x => x.LotId != "" && FromStart(x) > 3 * GoodsWorld.CrowdRadiusMetres)
                .OrderBy(FromStart).ThenBy(x => x.Id, StringComparer.Ordinal).First();
            Assert.That(FromStart(near), Is.LessThan(GoodsWorld.CrowdRadiusMetres - 20), "A competitor stands near the starting restaurant.");
            SeedQueued(near.Id, new[] { "near-1", "near-2" });
            SeedQueued(far.Id, new[] { "far-1" });

            yield return Until(() =>
            {
                _remoteSite.Tick();
                return Has(_root.ClientSubscription.LatestCrowd, "near-1") && Has(_remoteSite.LatestCrowd, "near-1");
            }, "both clients receive the nearby competitor's customers");
            var hostCrowd = _root.ClientSubscription.LatestCrowd;
            var entry = hostCrowd.Restaurants.Single(x => x.CompetitorId == near.Id);
            Assert.That(entry.LotId, Is.EqualTo(near.LotId));
            Assert.That(Has(hostCrowd, "far-1") || Has(_remoteSite.LatestCrowd, "far-1"), Is.False, "Nobody near the far competitor hears of it.");

            // The host walks to the far competitor: its crowd follows it there, the remote's stays at the start.
            LocalAvatar().Teleport(SessionRoot.ApronSpawn(_map.Offers[far.LotId], 0).position);
            yield return Until(() => Has(_root.ClientSubscription.LatestCrowd, "far-1") && !Has(_root.ClientSubscription.LatestCrowd, "near-1"),
                "the host's crowd moves with its avatar");
            yield return new WaitForSeconds(2f);
            _remoteSite.Tick();
            Assert.That(Has(_remoteSite.LatestCrowd, "near-1") && !Has(_remoteSite.LatestCrowd, "far-1"), Is.True,
                "The remote client still hears only of what is near it.");
        }

        [UnityTest]
        public IEnumerator FiguresAppearOffScreenQueueOutsideAndGoInWhenServed()
        {
            yield return StartHost();
            var near = NearestCompetitor();
            var frontage = CompetitorFrontage.For(SitePlacement.Active, near.LotId);
            Assert.That(frontage, Is.Not.Null);
            // More than its servers, so some stay queued (and get figures) while the first are served.
            var ids = Enumerable.Range(1, near.Servers + 4).Select(i => "queue-" + i).ToList();
            SeedQueued(near.Id, ids);

            var camera = Camera.main != null ? Camera.main : Camera.allCameras.First(x => x.isActiveAndEnabled);
            var seen = new HashSet<string>();
            var appearedOnScreen = new List<string>();
            var hidden = new HashSet<string>();
            bool Watch()
            {
                foreach (var id in ids)
                {
                    var figure = Figure(id);
                    if (figure == null) continue;
                    if (seen.Add(id))
                    {
                        var view = camera.WorldToViewportPoint(figure.position + Vector3.up);
                        if (view.z > 0 && view.x > -0.05f && view.x < 1.05f && view.y > -0.05f && view.y < 1.05f) appearedOnScreen.Add(id);
                    }
                    if (!figure.gameObject.activeSelf) hidden.Add(id);
                }
                return true;
            }

            // Every queued customer stands at its place in ticket order, at the same moment.
            yield return Until(() =>
            {
                Watch();
                var crowd = _root.ClientSubscription.LatestCrowd?.Restaurants.FirstOrDefault(x => x.CompetitorId == near.Id);
                var queued = crowd?.Customers.Where(x => x.State == CustomerState.Queued && ids.Contains(x.Id)).OrderBy(x => x.Ticket).ToList();
                if (queued == null || queued.Count == 0) return false;
                var ranks = crowd.Customers.Where(x => x.State == CustomerState.Queued).OrderBy(x => x.Ticket).Select(x => x.Id).ToList();
                return queued.All(x =>
                {
                    var figure = Figure(x.Id);
                    return figure != null && figure.gameObject.activeSelf
                        && Vector3.Distance(figure.position, frontage.QueueSpot(ranks.IndexOf(x.Id))) < 0.35f;
                });
            }, "queued figures standing at their places", 40f);

            // A figure that queued is served (by the competitor's servers, in 10 to 30 s) and goes in through the door.
            yield return Until(() => Watch() && hidden.Overlaps(seen), "a served figure going inside", 70f);
            var inside = Figure(hidden.First(seen.Contains));
            Assert.That(Vector3.Distance(inside.position, frontage.Door), Is.LessThan(0.35f), "It went in through the door.");
            Assert.That(appearedOnScreen, Is.Empty, "Figures first appear out of the camera's view.");
        }

        [UnityTest]
        public IEnumerator AtTheCapTheCurrentSiteKeepsItsFiguresAndTheCapHolds()
        {
            const int Cap = 3;
            typeof(CustomerPresenter).GetField("maxVisible", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_presenter, Cap);
            yield return StartHost();
            var near = NearestCompetitor();
            var rivals = Enumerable.Range(1, 3).Select(i => "rival-" + i).ToList();
            SeedQueued(near.Id, Enumerable.Range(1, near.Servers).Select(i => "served-" + i).Concat(rivals));
            var maxShown = 0;
            yield return Until(() =>
            {
                maxShown = Math.Max(maxShown, _presenter.VisibleCount);
                return rivals.Count(Shown) >= 2;
            }, "competitor figures filling the cap", 30f);

            var recipe = _root.Recipes.Single(x => x != null && x.IsSale && x.StationKind == GoodsWorld.CounterKind);
            var mine = Enumerable.Range(1, Cap).Select(i => "mine-" + i).ToList();
            SeedQueued(Start.SiteId, mine, recipe.Id);
            yield return Until(() =>
            {
                maxShown = Math.Max(maxShown, _presenter.VisibleCount);
                return mine.All(Shown) && !rivals.Any(Shown);
            }, "the current site's figures taking every place", 40f);
            for (var frame = 0; frame < 60; frame++)
            {
                yield return null;
                maxShown = Math.Max(maxShown, _presenter.VisibleCount);
                Assert.That(mine.All(Shown), Is.True, "The current site's figures are never displaced.");
            }
            Assert.That(maxShown, Is.LessThanOrEqualTo(Cap), "The cap was never exceeded, even while figures were displaced.");
        }

        // Decision 0033 evidence, run on request: frame time with no seeded figures, then with the 100-figure cap filled by TEST-ONLY
        // queued customers, about half at the starting restaurant and half queued outside the seven competitors nearest it (each seeded
        // with enough to fill its seats and servers and still queue); captures of a busy competitor's queue, a quiet competitor, and
        // the street from above; then, for comparison, the cap filled by the starting restaurant alone. Writes them to
        // docs/verification/competitor-customers-20260930/. Editor frame times include Editor overhead; compare runs within one file only.
        [UnityTest, Explicit("Presentation cost and captures for decision 0033: fills the figure cap twice, about two minutes.")]
        public IEnumerator PresentationCostAndCapturesWithTheCapFull()
        {
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "verification", "competitor-customers-20260930"));
            Directory.CreateDirectory(output);
            yield return StartHost();
            yield return new WaitForSeconds(2f);
            var before = new List<float>();
            yield return Frames(before, 300);
            var shownBefore = _presenter.VisibleCount;

            var competitors = _root.ServerWorld.Snapshot().Competitors.Where(x => x.LotId != "").OrderBy(FromStart)
                .ThenBy(x => x.Id, StringComparer.Ordinal).Take(8).ToList();
            var busy = competitors.Take(7).ToList();
            var quiet = competitors.Last();
            foreach (var competitor in busy)
                SeedQueued(competitor.Id, Enumerable.Range(1, competitor.Seats + competitor.Servers + CompetitorFrontage.QueueSpots).Select(i => $"{competitor.Id}-test-{i}"));
            var recipe = _root.Recipes.Single(x => x != null && x.IsSale && x.StationKind == GoodsWorld.CounterKind);
            SeedQueued(Start.SiteId, Enumerable.Range(1, 50).Select(i => "mine-test-" + i), recipe.Id);
            // Wait until the figures settle (a competitor whose whole street is on screen gets none: figures only appear out of view).
            var end = Time.realtimeSinceStartup + 60f;
            var last = -1;
            var steady = Time.realtimeSinceStartup;
            while (_presenter.VisibleCount < 100 && Time.realtimeSinceStartup < end && Time.realtimeSinceStartup - steady < 10f)
            {
                if (_presenter.VisibleCount != last)
                {
                    last = _presenter.VisibleCount;
                    steady = Time.realtimeSinceStartup;
                }
                yield return null;
            }
            string PerCompetitor() => string.Join(", ", busy.Select(c => $"{c.Id} ({FromStart(c)} m): " +
                _presenter.transform.Cast<Transform>().Count(x => x.name.StartsWith($"Customer {c.Id}-test-") && x.gameObject.activeSelf)));
            Assert.That(_presenter.VisibleCount, Is.GreaterThanOrEqualTo(90), $"The cap did not nearly fill: {_presenter.VisibleCount} shown; {PerCompetitor()}");
            yield return new WaitForSeconds(10f);
            var full = new List<float>();
            yield return Frames(full, 300);
            var shownFull = _presenter.VisibleCount;
            var mine = _presenter.transform.Cast<Transform>().Count(x => x.name.StartsWith("Customer mine-test-") && x.gameObject.activeSelf);
            var perCompetitor = PerCompetitor();

            // Captures use their own disabled cameras, so the presenter's view (the player's camera) is unchanged.
            var placement = SitePlacement.Active;
            var front = CompetitorFrontage.For(placement, busy[0].LotId);
            var still = CompetitorFrontage.For(placement, quiet.LotId);
            Capture(Look(front.Door + front.Outward * 9f + Vector3.up * 4.5f, front.Door + Vector3.up), Path.Combine(output, "busy-competitor-queue.png"));
            Capture(Look(still.Door + still.Outward * 9f + Vector3.up * 4.5f, still.Door + Vector3.up), Path.Combine(output, "quiet-competitor.png"));
            var middle = busy.Select(x => CompetitorFrontage.For(placement, x.LotId).Door).Aggregate(Vector3.zero, (a, b) => a + b) / busy.Count;
            Capture(Look(middle + new Vector3(0f, 60f, -50f), middle), Path.Combine(output, "street-from-above.png"));

            // For comparison, the same cap filled by the starting restaurant alone (what drawing looked like before decision 0033):
            // its extra customers take every place and displace the competitors' figures.
            SeedQueued(Start.SiteId, Enumerable.Range(51, 60).Select(i => "mine-test-" + i), recipe.Id);
            int Mine() => _presenter.transform.Cast<Transform>().Count(x => x.name.StartsWith("Customer mine-test-") && x.gameObject.activeSelf);
            yield return Until(() => Mine() >= 95 && _presenter.VisibleCount == Mine(), "the starting restaurant taking every place", 90f);
            yield return new WaitForSeconds(10f);
            var owned = new List<float>();
            yield return Frames(owned, 300);
            var shownOwned = _presenter.VisibleCount;

            string Summary(List<float> frames)
            {
                var sorted = frames.OrderBy(x => x).ToList();
                return $"mean {frames.Average() * 1000f:F2} ms, p95 {sorted[(int)(sorted.Count * 0.95f)] * 1000f:F2} ms, max {sorted.Last() * 1000f:F2} ms";
            }
            var report = $"Decision 0033 presentation cost, WorldGen seed {Seed}, Editor PlayMode host, 300 frames each, {DateTime.UtcNow:u}\n"
                + $"{shownBefore} figures: {Summary(before)}\n"
                + $"{shownFull} figures ({mine} at the starting restaurant, the rest at competitors): {Summary(full)}\n"
                + $"Figures per busy competitor: {perCompetitor}; quiet: {quiet.Id}\n"
                + $"{shownOwned} figures, all at the starting restaurant: {Summary(owned)}\n";
            File.WriteAllText(Path.Combine(output, "presentation-cost.txt"), report);
            Debug.Log("[Benchmark] " + report);
        }

        private static Camera Look(Vector3 from, Vector3 at)
        {
            var camera = new GameObject("capture-camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.farClipPlane = 2000f;
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            return camera;
        }

        private static IEnumerator Frames(List<float> frames, int count)
        {
            for (var index = 0; index < count; index++)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime);
            }
        }

        private static void Capture(Camera camera, string path)
        {
            var texture = new RenderTexture(1600, 900, 24);
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(image);
            UnityEngine.Object.Destroy(camera.gameObject);
        }

        // A second, client-only NetworkManager in this process, using the same catalog and the real authenticator.
        private void CreateRemote()
        {
            var go = new GameObject("competitor-test-remote");
            go.SetActive(false);
#if UNITY_EDITOR
            LogAssert.Expect(LogType.Error, new Regex("^SpawnablePrefabs is null on competitor-test-remote\\."));
#endif
            _remote = go.AddComponent<NetworkManager>();
            _remote.SpawnablePrefabs = _root.NetworkManager.SpawnablePrefabs;
            typeof(NetworkManager).GetField("_persistence", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_remote, NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_remote, false);
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
