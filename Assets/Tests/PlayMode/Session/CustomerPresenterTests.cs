// Verifies that a replicated customer becomes a walking local visual in DevSite using an isolated host save.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Customers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class CustomerPresenterTests
    {
        private readonly InputTestFixture _input = new();
        private string _directory;
        private SessionRoot _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input.Setup();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryCustomerPresenter", Guid.NewGuid().ToString("N"));
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/DevSite.unity", LoadSceneMode.Single);
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"), IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Customer Presenter Test", Address = "127.0.0.1"
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
            }
            finally
            {
                _input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator ReplicatedCustomerWalksFromOffscreenTowardTheCounter()
        {
            var presenter = UnityEngine.Object.FindAnyObjectByType<CustomerPresenter>();
            Assert.That(presenter, Is.Not.Null, "DevSite needs its customer presenter.");
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ServerBridge != null && _root.ServerBridge.IsServing
                && _root.ClientSite != null && Camera.allCameras.Any(x => x.isActiveAndEnabled), "host and local camera");
            var recipe = _root.Recipes.Single(x => x != null && x.IsSale && x.StationKind == GoodsWorld.CounterKind);
            _root.ServerWorld.Bootstrap(new GoodsCustomer
            {
                Id = "visual-test-customer", DistrictId = DevWorld.DistrictId, Appearance = 2, DineIn = true,
                PatienceSeconds = 600, State = CustomerState.Queued, RestaurantId = DevWorld.SiteId, RecipeId = recipe.Id,
                Ticket = 1000
            });
            yield return Until(() => _root.ClientSite.Customers.Any(x => x.Id == "visual-test-customer"), "customer baseline");
            yield return Until(() => presenter.VisibleCount == 1, "customer visual");
            var visual = presenter.transform.Cast<Transform>().Single(x => x.name == "Customer visual-test-customer");
            var initial = visual.position;
            for (var frame = 0; frame < 20; frame++) yield return null;
            Assert.That(Vector3.Distance(visual.position, initial), Is.GreaterThan(0.1f), "The customer should walk toward the counter.");
            Assert.That(visual.GetComponentInChildren<Animator>(), Is.Not.Null);
        }

        private static IEnumerator Until(Func<bool> condition, string step)
        {
            var end = Time.realtimeSinceStartup + 10f;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for " + step);
        }
    }
}
