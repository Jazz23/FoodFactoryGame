// Checks the DevSite, player prefab, prefab catalog, equipment content and input authoring contract without entering Play mode.
using System.Linq;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FoodFactoryGame.Goods.Network;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class SessionAuthoringTests
    {
        private const string ScenePath = "Assets/Scenes/DevSite.unity";
        private const string PlayerPath = "Assets/Prefabs/Player/Player.prefab";
        private const string BridgePath = "Assets/Prefabs/Network/GoodsNetworkBridge.prefab";
        private const string CatalogPath = "Assets/Network/GamePrefabs.asset";
        private const string FixturePath = "Assets/Tests/PlayMode/Goods/GoodsBridgeFixture.prefab";
        private const string OvenPrefabPath = "Assets/Prefabs/Equipment/Oven.prefab";
        private const string OvenDefinitionPath = "Assets/Content/Equipment/Oven.asset";
        private const string BreadRecipePath = "Assets/Content/Recipes/Bread.asset";

        private static void AssertAssigned(Object component, params string[] fields)
        {
            using var serialized = new SerializedObject(component);
            foreach (var field in fields)
            {
                var property = serialized.FindProperty(field);
                Assert.That(property, Is.Not.Null, $"{component.GetType().Name}.{field} does not exist.");
                if (property.isArray)
                {
                    Assert.That(property.arraySize, Is.GreaterThan(0), $"{component.GetType().Name}.{field} is empty.");
                    for (var index = 0; index < property.arraySize; index++)
                        Assert.That(property.GetArrayElementAtIndex(index).objectReferenceValue != null, Is.True, $"{field}[{index}]");
                }
                else Assert.That(property.objectReferenceValue != null, Is.True, $"{component.GetType().Name}.{field} is unassigned.");
            }
        }

        [Test]
        public void DevSiteIsTheOnlyBuildSceneAndIsWiredForSessions()
        {
            var enabled = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
            Assert.That(enabled, Is.EqualTo(new[] { ScenePath }));
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var objects = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).Select(x => x.gameObject).ToArray();
                foreach (var item in objects)
                    Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item), Is.Zero, item.name);
                var managers = objects.SelectMany(x => x.GetComponents<NetworkManager>()).ToArray();
                Assert.That(managers.Length, Is.EqualTo(1));
                Assert.That(AssetDatabase.GetAssetPath(managers[0].SpawnablePrefabs), Is.EqualTo(CatalogPath));
                using (var serialized = new SerializedObject(managers[0]))
                    Assert.That(serialized.FindProperty("_dontDestroyOnLoad").boolValue, Is.False);
                var server = managers[0].GetComponent<FishNet.Managing.Server.ServerManager>();
                Assert.That(server.GetAuthenticator(), Is.InstanceOf<DevAuthenticator>());
                var roots = objects.SelectMany(x => x.GetComponents<SessionRoot>()).ToArray();
                Assert.That(roots.Length, Is.EqualTo(1));
                AssertAssigned(roots[0], "networkManager", "authenticator", "bridgePrefab", "playerPrefab", "spawnPoints", "equipmentDefinitions", "recipes");
                using (var serialized = new SerializedObject(roots[0]))
                {
                    Assert.That(serialized.FindProperty("authenticator").objectReferenceValue, Is.SameAs(server.GetAuthenticator()));
                    Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("playerPrefab").objectReferenceValue), Is.EqualTo(PlayerPath));
                    Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("bridgePrefab").objectReferenceValue), Is.EqualTo(BridgePath));
                    Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("equipmentDefinitions").GetArrayElementAtIndex(0).objectReferenceValue),
                        Is.EqualTo(OvenDefinitionPath));
                    Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("recipes").GetArrayElementAtIndex(0).objectReferenceValue),
                        Is.EqualTo(BreadRecipePath));
                }
                var panels = objects.SelectMany(x => x.GetComponents<SessionPanel>()).ToArray();
                Assert.That(panels.Length, Is.EqualTo(1));
                AssertAssigned(panels[0], "document", "session", "equipment");

                // Equipment is shown from replicated state; the scene itself holds no equipment instance.
                var presenters = objects.SelectMany(x => x.GetComponents<EquipmentPresenter>()).ToArray();
                Assert.That(presenters.Length, Is.EqualTo(1));
                AssertAssigned(presenters[0], "session");
                var interactions = objects.SelectMany(x => x.GetComponents<EquipmentInteraction>()).ToArray();
                Assert.That(interactions.Length, Is.EqualTo(1));
                AssertAssigned(interactions[0], "session", "ghost", "placeAction", "removeAction", "rotateAction", "pointAction",
                    "inventoryAction", "clearCursorAction", "closeScreenAction", "hotbarAction");
                var huds = objects.SelectMany(x => x.GetComponents<PlayerHud>()).ToArray();
                Assert.That(huds.Length, Is.EqualTo(1));
                AssertAssigned(huds[0], "document", "interaction");
                Assert.That(huds[0].GetComponent<UnityEngine.UIElements.UIDocument>().panelSettings, Is.Not.Null);
                var ghost = interactions[0].GetComponentInChildren<Renderer>(true);
                Assert.That(ghost.gameObject.activeSelf, Is.False);
                Assert.That(ghost.GetComponent<Collider>(), Is.Null, "The ghost must not intercept pickup raycasts.");
                var oven = AssetDatabase.LoadAssetAtPath<GameObject>(OvenPrefabPath);
                Assert.That(objects.Any(x => PrefabUtility.GetCorrespondingObjectFromSource(x) == oven), Is.False);
                Assert.That(objects.Any(x => x.GetComponent<EquipmentVisual>() != null), Is.False);
                var document = panels[0].GetComponent<UnityEngine.UIElements.UIDocument>();
                Assert.That(document, Is.Not.Null);
                Assert.That(document.panelSettings, Is.Not.Null, "Without PanelSettings the menu and readout never render.");
                Assert.That(document.panelSettings.themeStyleSheet, Is.Not.Null);
                // The local player's rig is the only camera; the scene itself has none.
                Assert.That(objects.Any(x => x.GetComponent<Camera>() != null), Is.False);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void OvenDefinitionHasFootprintCapacitiesAndServerFreeVisual()
        {
            var definition = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(OvenDefinitionPath);
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Kind, Is.EqualTo("oven"));
            Assert.That((definition.Width, definition.Depth), Is.EqualTo((3, 3)));
            Assert.That(definition.InputCapacity, Is.GreaterThan(0));
            Assert.That(definition.OutputCapacity, Is.GreaterThan(0));
            Assert.That(AssetDatabase.GetAssetPath(definition.VisualPrefab), Is.EqualTo(OvenPrefabPath));
            var components = definition.VisualPrefab.GetComponentsInChildren<Component>(true);
            Assert.That(components.Any(x => x is NetworkObject), Is.False, "Equipment visuals are local, not network objects.");
            Assert.That(components.Any(x => x != null && x.GetType().Name == "OvenClickInput"), Is.False, "A local toggle contradicts server authority.");
            Assert.That(definition.VisualPrefab.GetComponentsInChildren<Collider>(true), Is.Not.Empty, "Pickup raycasts need a collider.");
            // The measured model must fit its footprint.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(definition.VisualPrefab);
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var item in renderers.Skip(1)) bounds.Encapsulate(item.bounds);
                Assert.That(bounds.size.x, Is.LessThanOrEqualTo(definition.Width * FoodFactoryGame.Goods.SiteGrid.CellSize));
                Assert.That(bounds.size.z, Is.LessThanOrEqualTo(definition.Depth * FoodFactoryGame.Goods.SiteGrid.CellSize));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void BreadRecipeTurnsDoughIntoBreadInTheOven()
        {
            var recipe = AssetDatabase.LoadAssetAtPath<RecipeAsset>(BreadRecipePath);
            Assert.That(recipe, Is.Not.Null);
            var definition = recipe.ToDefinition();
            Assert.That((definition.StationKind, definition.DurationSeconds, definition.OutputItemId, definition.OutputQuantity),
                Is.EqualTo(("oven", 10L, "bread", 1)));
            Assert.That(definition.Inputs.Select(x => (x.ItemId, x.Quantity)), Is.EqualTo(new[] { (DevWorld.DoughItemId, 1) }));
            Assert.That(definition.OutputSpoilAfterSeconds, Is.GreaterThan(0));
            // The server must accept it as content; this throws on an invalid recipe.
            Assert.DoesNotThrow(() => new FoodFactoryGame.Goods.GoodsWorld("authoring-check").RegisterRecipe(definition));
        }

        [Test]
        public void PlayerPrefabHasNetworkedBodyAndDisabledOwnerRig()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<NetworkObject>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CharacterController>(), Is.Not.Null);
            var transform = prefab.GetComponent<NetworkTransform>();
            Assert.That(transform, Is.Not.Null);
            using (var serialized = new SerializedObject(transform))
                Assert.That(serialized.FindProperty("_clientAuthoritative").boolValue, Is.True, "Prototype movement authority is the owner.");
            var avatar = prefab.GetComponent<PlayerAvatar>();
            AssertAssigned(avatar, "controller", "cameraRig", "body", "moveAction");
            var rig = prefab.GetComponentInChildren<OrbitCameraRig>(true);
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.gameObject.activeSelf, Is.False, "Only the owning client enables the rig.");
            AssertAssigned(rig, "target", "cameraTransform", "lookAction", "zoomAction");
            Assert.That(rig.GetComponentsInChildren<Camera>(true).Length, Is.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<Camera>(true).Single().transform.IsChildOf(rig.transform), Is.True);
            Assert.That(prefab.GetComponentsInChildren<AudioListener>(true).All(x => x.transform.IsChildOf(rig.transform)), Is.True);
        }

        [Test]
        public void GamePrefabCatalogListsOnlyProjectSpawnables()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(CatalogPath);
            Assert.That(catalog, Is.Not.Null);
            var paths = catalog.Prefabs.Select(AssetDatabase.GetAssetPath).ToArray();
            Assert.That(paths, Is.EquivalentTo(new[] { PlayerPath, BridgePath }));
            Assert.That(catalog.Prefabs.All(x => x.GetIsSpawnable()), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(BridgePath).GetComponent<GoodsNetworkBridge>(), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<NetworkObject>(FixturePath).GetIsSpawnable(), Is.False,
                "The PlayMode fixture must stay out of generated gameplay catalogs.");
        }

        [Test]
        public void PlayerMapHasMovementCameraAndEquipmentActions()
        {
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            var player = input.FindActionMap("Player", true);
            foreach (var name in new[] { "Move", "Look", "Zoom", "Place", "Remove", "Rotate", "Point", "Inventory", "ClearCursor", "CloseScreen", "Hotbar" })
                Assert.That(player.FindAction(name, true).bindings.Count, Is.GreaterThan(0), name);
            Assert.That(player.FindAction("Orbit"), Is.Null, "Orbiting is always on; no button holds it.");
            Assert.That(player.FindAction("Zoom").bindings.Any(x => x.path == "<Mouse>/scroll"), Is.True);
            Assert.That(player.FindAction("Place").bindings.Any(x => x.path == "<Mouse>/leftButton"), Is.True);
            Assert.That(player.FindAction("Remove").bindings.Any(x => x.path == "<Mouse>/rightButton"), Is.True);
            Assert.That(player.FindAction("Rotate").bindings.Any(x => x.path == "<Keyboard>/r"), Is.True);
            Assert.That(player.FindAction("Inventory").bindings.Any(x => x.path == "<Keyboard>/e"), Is.True);
            Assert.That(player.FindAction("ClearCursor").bindings.Any(x => x.path == "<Keyboard>/q"), Is.True);
            Assert.That(player.FindAction("CloseScreen").bindings.Any(x => x.path == "<Keyboard>/escape"), Is.True);
            foreach (var name in new[] { "Place", "Remove", "Inventory", "ClearCursor", "CloseScreen" })
                Assert.That(player.FindAction(name).interactions, Is.Empty, $"{name} is a press, not a hold.");
            // Each hotbar key reads as its slot number through a scale processor.
            var hotbar = player.FindAction("Hotbar");
            for (var slot = 1; slot <= 9; slot++)
                Assert.That(hotbar.bindings.Any(x => x.path == $"<Keyboard>/{slot}" && x.processors == $"Scale(factor={slot})"), Is.True, $"slot {slot}");
        }
    }
}
