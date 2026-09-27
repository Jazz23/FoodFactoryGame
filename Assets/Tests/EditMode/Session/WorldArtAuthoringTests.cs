// Authoring check for the world art (decision 0026 presentation): WorldGen's presenter references the art catalog, and the
// catalog holds every piece the presenter asks for with a mesh and one Lit material per submesh, plus the ground covers.
using System.Linq;
using FoodFactoryGame.Session.WorldMap;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class WorldArtAuthoringTests
    {
        [Test]
        public void WorldGenPresenterHasEveryArtPiece()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/WorldGen.unity");
            try
            {
                var presenter = scene.GetRootGameObjects().Select(x => x.GetComponentInChildren<WorldLayoutPresenter>(true)).Single(x => x != null);
                var catalog = new SerializedObject(presenter).FindProperty("art").objectReferenceValue as WorldArtCatalog;
                Assert.That(catalog, Is.Not.Null, "WorldGen's presenter needs the world art catalog.");
                foreach (var name in WorldLayoutPresenter.RequiredPieces)
                {
                    var piece = catalog.Get(name);
                    Assert.That(piece.mesh, Is.Not.Null, name);
                    Assert.That(piece.materials.Length, Is.EqualTo(piece.mesh.subMeshCount), name);
                    Assert.That(piece.materials.All(x => x != null && x.shader.name == "Universal Render Pipeline/Lit"), name);
                    Assert.That(piece.mesh.isReadable, name + " must be readable to merge and batch");
                }
                Assert.That(new[] { catalog.grass, catalog.paving, catalog.yard, catalog.wheat, catalog.greens }.All(x => x != null));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
