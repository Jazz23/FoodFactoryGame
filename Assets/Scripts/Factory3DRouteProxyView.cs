// Provides a disposable scene facade for read-only cross-floor route snapshots.
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Factory3DRouteProxyView : MonoBehaviour
{
    private Factory3DRouteProxyAssembler assembler = null!;
    private Material fallbackMaterial = null!;
    private Factory3DRouteSnapshot snapshot = null!;
    private Material snapshotMaterial = null!;
    private int activeFloorIndex = -1;

    public Factory3DRouteSnapshot Snapshot => snapshot;
    public int ActiveFloorIndex => activeFloorIndex;

    public Factory3DRouteProxyBuildResult Rebuild(
        Factory3DRouteSnapshot snapshot,
        int activeFloorIndex = -1,
        Material material = null)
    {
        assembler ??= new Factory3DRouteProxyAssembler();
        this.snapshot = snapshot;
        this.activeFloorIndex = activeFloorIndex;
        snapshotMaterial = material;
        if (snapshot is null)
        {
            assembler.Clear(transform);
            return default;
        }

        var effectiveMaterial = material is not null ? material : GetFallbackMaterial();
        return assembler.Reconcile(transform, snapshot, activeFloorIndex, effectiveMaterial);
    }

    public Factory3DRouteProxyBuildResult SetActiveFloor(int newActiveFloorIndex)
    {
        return Rebuild(snapshot, newActiveFloorIndex, snapshotMaterial);
    }

    public Factory3DRouteProxyBuildResult Rebuild(
        FactoryWorldState state,
        SceneGrid grid,
        float storyHeight = 3f,
        int activeFloorIndex = -1,
        Material material = null)
    {
        var snapshot = Factory3DRouteSnapshotBuilder.Build(state, grid, storyHeight);
        return Rebuild(snapshot, activeFloorIndex, material);
    }

    public Factory3DRouteProxyBuildResult Refresh(
        FactoryWorldState state,
        SceneGrid grid,
        float storyHeight = 3f)
    {
        return Rebuild(state, grid, storyHeight, activeFloorIndex, snapshotMaterial);
    }

    public void ClearAuthority()
    {
        snapshot = null!;
        assembler ??= new Factory3DRouteProxyAssembler();
        assembler.Clear(transform);
    }

    public void Clear()
    {
        assembler ??= new Factory3DRouteProxyAssembler();
        snapshot = null!;
        assembler.Clear(transform);
    }

    private Material GetFallbackMaterial()
    {
        if (fallbackMaterial is not null && fallbackMaterial)
        {
            return fallbackMaterial;
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader is null)
        {
            return null!;
        }

        fallbackMaterial = new Material(shader)
        {
            name = "Factory3DRouteProxy Material",
            color = new Color(0.95f, 0.5f, 0.12f, 1f),
            hideFlags = HideFlags.DontSave
        };
        return fallbackMaterial;
    }

    private void Awake()
    {
        gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
    }

    private void OnDestroy()
    {
        Clear();
        if (fallbackMaterial is not null && fallbackMaterial)
        {
            DestroyGeneratedObject(fallbackMaterial);
        }
    }

    private static void DestroyGeneratedObject(UnityEngine.Object target)
    {
        if (target is null || !target)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
