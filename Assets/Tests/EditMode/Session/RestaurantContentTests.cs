// Checks the restaurant building content contract (decision 0034) without entering Play mode: every model of the restaurant
// art kit is buildable, either as a wall finish, door or window style the build mode orders (the style catalog) or as a piece of
// equipment with a supplier offer, in both scenes that host sessions; every style the server accepts has its models; decor
// definitions use known layers and every table supplies seats; and build mode is wired to the Input System's Player actions.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class RestaurantContentTests
    {
        private const string ModelRoot = "Assets/Art/Restaurant/Models";
        private const string CatalogPath = "Assets/Content/Restaurant/RestaurantStyles.asset";
        private static readonly string[] Scenes = { "Assets/Scenes/DevSite.unity", "Assets/Scenes/WorldGen.unity" };

        private static HashSet<string> KitModels() => new(AssetDatabase.FindAssets("t:Model", new[] { ModelRoot }).Select(AssetDatabase.GUIDToAssetPath));

        // The kit models an asset is built from (its prefab and model dependencies).
        private static IEnumerable<string> ModelsUsedBy(Object asset) =>
            asset == null ? Enumerable.Empty<string>() : AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(asset), true).Where(x => x.StartsWith(ModelRoot));

        [Test]
        public void EveryArtKitModelIsBuildableInBothSessionScenes()
        {
            var models = KitModels();
            Assert.That(models.Count, Is.EqualTo(59), "The kit's 59 models (docs/verification/restaurant-art-20261001.md).");
            var catalog = AssetDatabase.LoadAssetAtPath<RestaurantStyleCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null);
            var styleModels = new HashSet<string>(ModelsUsedBy(catalog));
            foreach (var scenePath in Scenes)
            {
                var scene = EditorSceneManager.OpenPreviewScene(scenePath);
                try
                {
                    var objects = scene.GetRootGameObjects();
                    var session = objects.SelectMany(x => x.GetComponentsInChildren<SessionRoot>(true)).Single();
                    var buildings = objects.SelectMany(x => x.GetComponentsInChildren<BuildingPresenter>(true)).Single();
                    Assert.That(buildings.RestaurantStyles, Is.SameAs(catalog), $"{scenePath}: the presenter draws walls, doors and windows from the kit.");
                    var offered = session.Offers.Where(x => x != null && x.Equipment != null).Select(x => x.Equipment).ToList();
                    var equipmentModels = new HashSet<string>(offered.Where(x => session.EquipmentDefinitions.Contains(x)).SelectMany(x => ModelsUsedBy(x.VisualPrefab)));
                    var missing = models.Where(x => !styleModels.Contains(x) && !equipmentModels.Contains(x)).ToList();
                    Assert.That(missing, Is.Empty, $"{scenePath}: kit models nobody can build.");
                    var mode = objects.SelectMany(x => x.GetComponentsInChildren<BuildMode>(true)).Single();
                    using var serialized = new SerializedObject(mode);
                    foreach (var (field, action) in new[] { ("buildAction", "Player/Build"), ("confirmAction", "Player/BuildConfirm"), ("placeAction", "Player/Place"),
                                 ("removeAction", "Player/Remove"), ("rotateAction", "Player/Rotate"), ("pointAction", "Player/Point") })
                    {
                        var reference = serialized.FindProperty(field).objectReferenceValue as InputActionReference;
                        Assert.That(reference != null && reference.action != null, Is.True, $"{scenePath}: BuildMode.{field}");
                        Assert.That($"{reference.action.actionMap.name}/{reference.action.name}", Is.EqualTo(action));
                    }
                    foreach (var field in new[] { "session", "interaction", "buildings", "document", "cellMaterial", "ghostModelMaterial" })
                        Assert.That(serialized.FindProperty(field).objectReferenceValue != null, Is.True, $"{scenePath}: BuildMode.{field}");
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            Assert.That(styleModels.Count, Is.EqualTo(23), "Walls, ends, doorways, frames, leaves, window openings, windows and the hatch.");
        }

        [Test]
        public void EveryServerStyleHasItsModelsAndDecorDefinitionsAreConsistent()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<RestaurantStyleCatalog>(CatalogPath);
            foreach (var style in GoodsWorld.WallStyles)
                Assert.That(catalog.WallFinishes.Any(x => x.style == style && x.wall1m != null && x.wall2m != null && x.end != null), Is.True, style);
            foreach (var style in GoodsWorld.DoorStyles) Assert.That(catalog.DoorLeaves.Any(x => x.style == style && x.leaf != null), Is.True, style);
            foreach (var style in GoodsWorld.WindowStyles) Assert.That(catalog.WindowStyles.Any(x => x.style == style && x.bay != null), Is.True, style);
            Assert.That((catalog.SingleDoorway != null, catalog.DoubleDoorway != null, catalog.SingleFrame != null, catalog.DoubleFrame != null),
                Is.EqualTo((true, true, true, true)));
            var definitions = AssetDatabase.FindAssets("t:EquipmentDefinition", new[] { "Assets/Content" })
                .Select(x => AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(AssetDatabase.GUIDToAssetPath(x))).ToList();
            foreach (var definition in definitions)
            {
                Assert.That(SiteGrid.IsLayer(definition.Layer), Is.True, definition.name);
                Assert.That(definition.VisualPrefab != null && definition.Icon != null, Is.True, $"{definition.name} has a model and an icon.");
                if (definition.Kind == GoodsWorld.TableKind || definition.Kind.Contains("table-")) Assert.That(definition.Seats, Is.GreaterThan(0), definition.name);
                var mount = definition.Layer switch
                {
                    SiteGrid.WallLayer => EquipmentMount.Wall,
                    SiteGrid.CeilingLayer => EquipmentMount.Ceiling,
                    SiteGrid.TabletopLayer => EquipmentMount.Tabletop,
                    _ => definition.Mount
                };
                Assert.That(definition.Mount, Is.EqualTo(mount), $"{definition.name}: its mount matches its layer.");
            }
            Assert.That(definitions.Single(x => x.Kind == GoodsWorld.CounterKind).DisplayName, Is.EqualTo("Register"), "The counter is the register (decision 0034).");
        }
    }
}
