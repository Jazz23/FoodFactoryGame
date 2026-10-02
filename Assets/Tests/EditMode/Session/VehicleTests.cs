// Checks the road vehicles (decision 0032): the Blender vehicles are installed in every session scene (the box truck on the
// truck presenter, city cars and paints on the city traffic presenter, no colliders, a paintable body), and the city cars are a
// deterministic function of the clock that is busier at rush hour, queues at red lights and behind trucks, and keeps the cap.
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Logistics;
using FoodFactoryGame.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class VehicleTests
    {
        private const string TruckPrefab = "Assets/Prefabs/Vehicles/Vehicle_BoxTruck.prefab";

        // TEST-ONLY: one long arterial east-west street, 400 m, with a traffic light at its east end where a local street
        // crosses, inside a downtown-like district.
        private static RoadNetwork Street()
        {
            var layout = new WorldLayout();
            layout.Nodes.Add(new RoadNode { Id = "west", X = 0, Z = 0 });
            layout.Nodes.Add(new RoadNode { Id = "east", X = 400, Z = 0, Control = JunctionControl.TrafficLight });
            layout.Nodes.Add(new RoadNode { Id = "north", X = 400, Z = 100 });
            layout.Nodes.Add(new RoadNode { Id = "south", X = 400, Z = -100 });
            layout.Roads.Add(new RoadSegment { Id = "main", FromId = "west", ToId = "east", Kind = RoadKind.Arterial, Width = 14, CapacityPerHour = 1800 });
            layout.Roads.Add(new RoadSegment { Id = "cross-n", FromId = "east", ToId = "north", Kind = RoadKind.Local, Width = 10, CapacityPerHour = 600 });
            layout.Roads.Add(new RoadSegment { Id = "cross-s", FromId = "south", ToId = "east", Kind = RoadKind.Local, Width = 10, CapacityPerHour = 600 });
            layout.Districts.Add(new WorldDistrict { Id = "downtown", TrafficPercent = 90, Areas = { new WorldRect(-50, -150, 500, 300) } });
            return new RoadNetwork(layout);
        }

        private static readonly GoodsTruck[] NoTrucks = new GoodsTruck[0];
        private static double At(int hour, double seconds = 0) => hour * RoadTraffic.HourSeconds + seconds;

        [TestCase("Assets/Scenes/DevSite.unity")]
        [TestCase("Assets/Scenes/SampleScene.unity")]
        [TestCase("Assets/Scenes/WorldGen.unity")]
        public void EverySessionSceneDrawsTheBlenderVehicles(string scenePath)
        {
            var scene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                var objects = scene.GetRootGameObjects();
                var trucks = objects.SelectMany(x => x.GetComponentsInChildren<TruckPresenter>(true)).Single();
                using (var serialized = new SerializedObject(trucks))
                    Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("truckPrefab").objectReferenceValue), Is.EqualTo(TruckPrefab));
                var traffic = objects.SelectMany(x => x.GetComponentsInChildren<CityTrafficPresenter>(true)).Single();
                using var cars = new SerializedObject(traffic);
                Assert.That(cars.FindProperty("session").objectReferenceValue, Is.Not.Null);
                var prefabs = cars.FindProperty("carPrefabs");
                Assert.That(prefabs.arraySize, Is.GreaterThanOrEqualTo(6));
                for (var i = 0; i < prefabs.arraySize; i++) Assert.That(prefabs.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"car {i}");
                Assert.That(cars.FindProperty("tints").arraySize, Is.GreaterThan(1));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void VehiclePrefabsHaveNoCollidersAndAPaintableBody()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Vehicles" });
            Assert.That(guids.Length, Is.EqualTo(7));
            foreach (var path in guids.Select(AssetDatabase.GUIDToAssetPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, path);
                var materials = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(x => x.sharedMaterials).ToList();
                Assert.That(materials.All(x => x != null), Is.True, path);
                if (!path.Contains("Taxi") && !path.Contains("Bus"))
                    Assert.That(materials.Any(x => x.name.StartsWith(CityTrafficPresenter.BodyMaterialPrefix)), Is.True, path);
            }
            var truck = AssetDatabase.LoadAssetAtPath<GameObject>(TruckPrefab);
            var bounds = truck.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
            Assert.That(bounds.size.z, Is.GreaterThan(bounds.size.x * 2), "The truck's length runs along its local Z.");
        }

        [Test]
        public void CityCarsAreTheSameForTheSameClockAndNearestFirst()
        {
            var network = Street();
            var view = new Vector2(200, 0);
            var a = CityCars.Compute(network, At(8, 12.5), view, 500, NoTrucks, 0, 500);
            var b = CityCars.Compute(network, At(8, 12.5), view, 500, NoTrucks, 0, 500);
            Assert.That(a.Count, Is.GreaterThan(0));
            Assert.That(a.Select(x => (x.Key, x.Segment, x.Forward, x.Lane, x.Along)), Is.EqualTo(b.Select(x => (x.Key, x.Segment, x.Forward, x.Lane, x.Along))));
            Assert.That(a.Select(x => x.Key).Distinct().Count(), Is.EqualTo(a.Count), "Every car has its own key.");
            var capped = CityCars.Compute(network, At(8, 12.5), view, 500, NoTrucks, 0, 5);
            Assert.That(capped.Select(x => x.Key), Is.EqualTo(a.Take(5).Select(x => x.Key)), "The cap keeps the nearest.");
        }

        [Test]
        public void RushHourIsBusierThanNight()
        {
            var network = Street();
            var view = new Vector2(200, 0);
            var rush = Enumerable.Range(0, 30).Sum(s => CityCars.Compute(network, At(8, s * 2), view, 500, NoTrucks, 0, 1000).Count);
            var night = Enumerable.Range(0, 30).Sum(s => CityCars.Compute(network, At(3, s * 2), view, 500, NoTrucks, 0, 1000).Count);
            Assert.That(rush, Is.GreaterThan(night * 4));
        }

        [Test]
        public void CarsQueueAtARedLightAndNeverOverlap()
        {
            var network = Street();
            var main = network.Segments.Single(x => x.Id == "main");
            var node = network.Nodes.Single(x => x.Id == "east");
            // A moment deep into the red for traffic along X at rush hour.
            var red = Enumerable.Range(0, RoadTraffic.LightCycleSeconds).Select(x => At(8, 600 + x))
                .First(x => !RoadTraffic.IsGreen(node, true, x) && !RoadTraffic.IsGreen(node, true, x - 10));
            var cars = CityCars.Compute(network, red, new Vector2(350, 0), 500, NoTrucks, 0, 1000)
                .Where(x => x.Segment == main.Index && x.Forward).ToList();
            var stopLine = main.Length - (10 / 2f + 2f);
            foreach (var lane in cars.GroupBy(x => x.Lane))
            {
                var positions = lane.Select(x => x.Along).OrderByDescending(x => x).ToList();
                Assert.That(positions.First(), Is.LessThanOrEqualTo(stopLine + 0.01f), "Nobody passes the stop line on red.");
                for (var i = 1; i < positions.Count; i++)
                    Assert.That(positions[i - 1] - positions[i], Is.GreaterThanOrEqualTo(CityCars.Gap - 0.01f), "A gap between cars.");
            }
            Assert.That(cars.Count(x => x.Along > stopLine - 3 * CityCars.Gap), Is.GreaterThanOrEqualTo(2), "A queue forms at the light.");
        }

        [Test]
        public void KerbLaneCarsWaitBehindATruck()
        {
            var network = Street();
            var main = network.Segments.Single(x => x.Id == "main");
            var truck = new GoodsTruck
            {
                Id = "truck", State = TruckState.ToDropoff, LegSegmentId = "main", LegFrom = 0, LegTo = 150, LegDriveSeconds = 15,
                LegSeconds = 15, RemainingSeconds = 0
            };
            for (var s = 0; s < 40; s++)
            {
                var cars = CityCars.Compute(network, At(8, s), new Vector2(150, 0), 500, new[] { truck }, 0, 1000)
                    .Where(x => x.Segment == main.Index && x.Forward && x.Lane == 0).ToList();
                Assert.That(cars.Where(x => x.Along > 150 - CityCars.Gap && x.Along <= 150 + CityCars.Gap), Is.Empty,
                    $"t={s}: no kerb-lane car overlaps the waiting truck.");
            }
        }
    }
}
