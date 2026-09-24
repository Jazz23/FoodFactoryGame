// Checks imported dependency and starter-authoring integrity without changing open scenes or application saves.
using System.Linq;
using FishNet.Managing;
using FoodFactoryGame.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Baseline.Tests
{
    public class ProjectBaselineTests
    {
        [Test]
        public void FishNetRuntimeIsAvailable()
        {
            Assert.That(typeof(NetworkManager).Assembly.GetName().Name, Is.EqualTo("FishNet.Runtime"));
            Assert.That(typeof(NetworkManager).IsSubclassOf(typeof(MonoBehaviour)), Is.True);
        }

        [Test]
        public void DefaultNetworkPrefabReferencesResolve()
        {
            var asset = AssetDatabase.LoadMainAssetAtPath("Assets/DefaultPrefabObjects.asset");
            Assert.That(asset, Is.Not.Null, "The imported FishNet prefab collection must resolve.");
            using var serialized = new SerializedObject(asset);
            var prefabs = serialized.FindProperty("_prefabs");
            Assert.That(prefabs, Is.Not.Null);
            Assert.That(prefabs.arraySize, Is.GreaterThan(0), "The baseline collection includes FishNet demo prefabs.");
            for (var index = 0; index < prefabs.arraySize; index++)
            {
                var prefab = prefabs.GetArrayElementAtIndex(index).objectReferenceValue;
                Assert.That(prefab != null, Is.True, $"Network prefab reference {index} did not resolve.");
            }
        }

        [Test]
        public void StarterInputActionsImportWithPlayerAndUiMaps()
        {
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            Assert.That(input, Is.Not.Null);
            var player = input.FindActionMap("Player", true);
            Assert.That(player.FindAction("Move", true).bindings.Count, Is.GreaterThan(0));
            Assert.That(player.FindAction("Interact", true).bindings.Count, Is.GreaterThan(0));
            Assert.That(input.FindActionMap("UI", true).actions.Count, Is.GreaterThan(0));
        }

        [Test]
        public void EnabledBuildScenesLoadWithoutMissingScriptsAndHaveCameraSource()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            Assert.That(scenes, Is.Not.Empty, "At least one baseline scene must be enabled for the player build.");
            foreach (var entry in scenes)
            {
                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(entry.path), Is.Not.Null, entry.path);
                var scene = EditorSceneManager.OpenPreviewScene(entry.path);
                try
                {
                    var transforms = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
                    foreach (var transform in transforms)
                    {
                        Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject),
                            Is.Zero, $"Missing script on {entry.path}: {transform.name}");
                    }
                    // A session scene is intentionally camera-free: the local player's prefab supplies the view.
                    var sessionCamera = transforms.Select(transform => transform.GetComponent<SessionRoot>())
                        .Where(root => root != null)
                        .Select(root => new SerializedObject(root).FindProperty("playerPrefab").objectReferenceValue as Component)
                        .Any(player => player != null && player.GetComponentInChildren<Camera>(true) != null);
                    Assert.That(sessionCamera || transforms.Any(transform => transform.GetComponent<Camera>() != null),
                        Is.True, $"The build scene {entry.path} must have a camera or a session player prefab with one.");
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }
    }
}
