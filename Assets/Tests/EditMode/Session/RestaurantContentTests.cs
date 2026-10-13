// Checks the restaurant building content contract (decision 0034) without entering Play mode: every model of the restaurant
// art kit but those the owner withdrew is buildable, either as a wall finish, door or window style the build mode orders (the style catalog) or as a piece of
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

        // Kit models the owner took out of the build catalog (the ceiling panel, 2026-10-02).
        private static readonly string[] Withdrawn = { ModelRoot + "/Surfaces/RT_Ceiling_Panel_1m.fbx" };

        private static HashSet<string> KitModels() => new(AssetDatabase.FindAssets("t:Model", new[] { ModelRoot }).Select(AssetDatabase.GUIDToAssetPath));

        // The kit models an asset is built from (its prefab and model dependencies).
        private static IEnumerable<string> ModelsUsedBy(Object asset) =>
            asset == null ? Enumerable.Empty<string>() : AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(asset), true).Where(x => x.StartsWith(ModelRoot));

        // Owner request (2026-10-12): a storage shelf shows more boxes as its slots fill, one stack per slot, bottom deck first.
        [Test]
        public void AStorageShelfShowsAStackOfBoxesPerUsedSlot()
        {
            var definition = AssetDatabase.FindAssets("t:EquipmentDefinition", new[] { "Assets/Content" }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<EquipmentDefinition>).Single(x => x.Kind == "rt-shelf");
            var shelf = (GameObject)PrefabUtility.InstantiatePrefab(definition.VisualPrefab);
            try
            {
                var display = shelf.GetComponent<StorageFillDisplay>();
                Assert.That(display, Is.Not.Null, "The shelf prefab has a fill display.");
                var stages = Enumerable.Range(1, 8).Select(i => shelf.transform.Find($"Boxes/Boxes_{i}").gameObject).ToList();
                Assert.That(stages.All(x => !x.activeSelf), Is.True, "An empty shelf (and the placement ghost copied from the prefab) shows no boxes.");
                var steel = shelf.transform.Find("RT_StorageShelf_1_2m").GetComponent<Renderer>();
                Assert.That(stages.SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).All(x => x.sharedMaterials.All(m => m != null && m.name.StartsWith("RT_"))),
                    Is.True, "Every box material is a kit material.");
                foreach (var (used, shown) in new[] { (0, 0), (1, 1), (3, 3), (7, 7), (8, 8), (9, 8) })
                {
                    display.SetFill(used, definition.InputCapacity);
                    Assert.That(stages.Count(x => x.activeSelf), Is.EqualTo(shown), $"{used} of {definition.InputCapacity} slots used.");
                    Assert.That(stages.Take(shown).All(x => x.activeSelf), Is.True, "Stacks fill in order.");
                }
                // The stacks stand inside the shelf's frame, so the shelf's footprint and pivot do not depend on what it holds.
                var frame = steel.bounds;
                foreach (var renderer in stages.SelectMany(x => x.GetComponentsInChildren<Renderer>()))
                    Assert.That(renderer.bounds.min.x >= frame.min.x && renderer.bounds.max.x <= frame.max.x && renderer.bounds.min.z >= frame.min.z
                        && renderer.bounds.max.z <= frame.max.z && renderer.bounds.min.y >= frame.min.y, Is.True, $"{renderer.name} {renderer.bounds} inside {frame}");
                Assert.That(StorageFillDisplay.StagesFor(1, 40, 8), Is.EqualTo(1), "A larger store still shows its first goods.");
            }
            finally
            {
                Object.DestroyImmediate(shelf);
            }
        }

        [Test]
        public void EveryArtKitModelIsBuildableInBothSessionScenes()
        {
            var models = KitModels();
            Assert.That(models.Count, Is.EqualTo(60),
                "The kit's 59 models (docs/verification/restaurant-art-20261001.md) and the storage shelf's box stacks (built with the shelf).");
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
                    var missing = models.Where(x => !styleModels.Contains(x) && !equipmentModels.Contains(x) && !Withdrawn.Contains(x)).ToList();
                    Assert.That(Withdrawn.Where(equipmentModels.Contains), Is.Empty, $"{scenePath}: withdrawn kit models on offer.");
                    Assert.That(missing, Is.Empty, $"{scenePath}: kit models nobody can build.");
                    var mode = objects.SelectMany(x => x.GetComponentsInChildren<BuildMode>(true)).Single();
                    using var serialized = new SerializedObject(mode);
                    foreach (var (field, action) in new[] { ("buildAction", "Player/Build"), ("placeAction", "Player/Place"),
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
