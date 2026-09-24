// Runs the dev restaurant shell (decision 0019) through the real DevSite host: the presenter builds solid walls from the
// replicated baseline, and walking the owned avatar in and out switches the camera to top-down and hides the roof, while the
// camera toggle still overrides the view until the next crossing. Every save and identity path is a unique temporary directory.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class BuildingPresenterTests
    {
        private const string ScenePath = "Assets/Scenes/DevSite.unity";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private BuildingPresenter _presenter;
        private PlayerAvatar _avatar;

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 10f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Isolated input state: no real device (a wheel, the developer's own mouse) reaches the session under test.
            _input.Setup();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryBuildingPlay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _presenter = UnityEngine.Object.FindAnyObjectByType<BuildingPresenter>();
            Assert.That(_presenter, Is.Not.Null, "DevSite must contain a BuildingPresenter.");
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"),
                IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Host",
                Address = "127.0.0.1"
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _root.NetworkManager.TransportManager.Transport.SetPort((ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
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

        private IEnumerator StandIn(int cellX, int cellZ)
        {
            var controller = _avatar.GetComponent<CharacterController>();
            controller.enabled = false;
            _avatar.transform.position = SiteGridSpace.FootprintCenter(_root.ClientSite.SiteLayouts.Single(), cellX, cellZ, 1, 1) + Vector3.up * 0.05f;
            controller.enabled = true;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator WalkingInSwitchesToTopDownAndHidesTheRoofUntilWalkingOut()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null, "host dev-site baseline");
            yield return Until(() => _presenter.ShellOf(DevWorld.RestaurantId) != null, "restaurant shell visual");
            // Removed colliders go at the end of the frame the shell was built in.
            yield return null;
            var shell = _presenter.ShellOf(DevWorld.RestaurantId);
            // Collider bounds follow transforms only after a physics sync.
            Physics.SyncTransforms();
            var walls = shell.GetComponentsInChildren<BoxCollider>().Where(x => x.name.StartsWith("Wall")).ToList();
            Assert.That(walls.Count, Is.EqualTo(5), "South, west and east walls, plus the north wall split by the doorway.");
            Assert.That(shell.GetComponentsInChildren<Collider>().Count(), Is.EqualTo(walls.Count), "Only walls are solid; floor, lintels and roof never catch aim rays.");
            var layout = _root.ClientSite.SiteLayouts.Single();
            var building = _root.ClientSite.Buildings.Single();
            var gap = SiteGridSpace.FootprintCenter(layout, DevWorld.RestaurantDoorX, building.CellZ + building.Depth - 1, 2, 1) + Vector3.up;
            Assert.That(walls.Any(x => x.bounds.Contains(gap)), Is.False, "The doorway is open.");
            Assert.That(walls.Any(x => x.bounds.Contains(SiteGridSpace.FootprintCenter(layout, building.CellX, 4, 1, 1) + Vector3.up)), Is.True, "The west wall is solid.");

            yield return Until(() => (_avatar = UnityEngine.Object.FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None).FirstOrDefault(x => x.IsOwner)) != null, "owned avatar");
            var rig = _avatar.CameraRig;
            yield return null;
            Assert.That((_presenter.LocalBuildingId, rig.TopDown, _presenter.RoofVisible(DevWorld.RestaurantId)), Is.EqualTo(((string)null, false, true)),
                "The avatar spawns outdoors in the orbit view.");

            yield return StandIn(14, 4);
            Assert.That((_presenter.LocalBuildingId, rig.TopDown, _presenter.RoofVisible(DevWorld.RestaurantId)), Is.EqualTo((DevWorld.RestaurantId, true, false)),
                "Inside: top-down and no roof.");

            // The camera toggle overrides the view while the avatar stays inside.
            typeof(OrbitCameraRig).GetMethod("SetTopDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rig, new object[] { false });
            yield return StandIn(15, 5);
            Assert.That(rig.TopDown, Is.False, "Moving within the building keeps the player's chosen view.");

            yield return StandIn(DevWorld.RestaurantDoorX, building.CellZ + building.Depth - 1);
            Assert.That(_presenter.LocalBuildingId, Is.Null, "The doorway is a threshold, not indoors.");
            yield return StandIn(14, 4);
            Assert.That(rig.TopDown, Is.True, "Re-entering switches to top-down again.");
            yield return StandIn(5, 5);
            Assert.That((_presenter.LocalBuildingId, rig.TopDown, _presenter.RoofVisible(DevWorld.RestaurantId)), Is.EqualTo(((string)null, false, true)),
                "Outside: orbit view and the roof is back.");
        }
    }
}
