// Checks the DevSite, player prefab, prefab catalog and input authoring contract without entering Play mode.
using System.Linq;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using FoodFactoryGame.Goods.Network;
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
                AssertAssigned(roots[0], "networkManager", "authenticator", "bridgePrefab", "playerPrefab", "spawnPoints");
                using (var serialized = new SerializedObject(roots[0]))
                {
                    Assert.That(serialized.FindProperty("authenticator").objectReferenceValue, Is.SameAs(server.GetAuthenticator()));
                    Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("playerPrefab").objectReferenceValue), Is.EqualTo(PlayerPath));
                    Assert.That(AssetDatabase.GetAssetPath(serialized.FindProperty("bridgePrefab").objectReferenceValue), Is.EqualTo(BridgePath));
                }
                var panels = objects.SelectMany(x => x.GetComponents<SessionPanel>()).ToArray();
                Assert.That(panels.Length, Is.EqualTo(1));
                AssertAssigned(panels[0], "document", "session");
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
            AssertAssigned(rig, "target", "cameraTransform", "lookAction", "orbitAction", "zoomAction");
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
        public void PlayerMapHasMoveLookOrbitAndZoom()
        {
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            var player = input.FindActionMap("Player", true);
            foreach (var name in new[] { "Move", "Look", "Orbit", "Zoom" })
                Assert.That(player.FindAction(name, true).bindings.Count, Is.GreaterThan(0), name);
            Assert.That(player.FindAction("Orbit").bindings.Any(x => x.path == "<Mouse>/rightButton"), Is.True);
            Assert.That(player.FindAction("Zoom").bindings.Any(x => x.path == "<Mouse>/scroll"), Is.True);
        }
    }
}
