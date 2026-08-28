// Owns a generated mesh and releases it when its visual module is destroyed.
using UnityEngine;

public sealed class GeneratedMeshOwner : MonoBehaviour
{
    private Mesh mesh = null!;

    public void SetMesh(Mesh ownedMesh)
    {
        ReleaseMesh();
        mesh = ownedMesh;
    }

    private void OnDestroy()
    {
        ReleaseMesh();
    }

    private void ReleaseMesh()
    {
        if (mesh is null || !mesh)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(mesh);
        }
        else
        {
            DestroyImmediate(mesh);
        }

        mesh = null!;
    }
}
