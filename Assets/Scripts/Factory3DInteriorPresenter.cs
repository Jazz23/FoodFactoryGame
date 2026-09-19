// Bridges initialized runtime factory snapshots to the disposable 3D interior proxy root.
using System.Collections.Generic;
using NotAI;
using UnityEngine;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class Factory3DInteriorPresenter : MonoBehaviour
{
    public const float DefaultStoryHeight = 3f;
    private const string ProxyRootName = "Factory 3D Interior Proxies";

    [SerializeField] private InsideFactoryController controller = null!;
    [SerializeField, Min(0.01f)] private float storyHeight = DefaultStoryHeight;
    [SerializeField] private Material material = null!;

    private Factory3DInteriorProxyAssembler assembler = new();
    private Transform proxyRoot = null!;
    private Material fallbackMaterial = null!;
    private NAIStateManager authority = null!;
    private SceneGrid targetGrid = null!;
    private InsideFactoryController activeController = null!;
    private int lastFingerprint;
    private bool hasFingerprint;
    private Factory3DInteriorProxyBuildResult lastBuild;
    private Factory3DTraversalCollisionPresenter collisionPresenter = null!;

    public float StoryHeight => storyHeight;
    public Transform ProxyRoot => proxyRoot;
    public NAIStateManager Authority => authority;
    public SceneGrid TargetGrid => targetGrid;
    public InsideFactoryController ActiveController => activeController;
    public Factory3DInteriorProxyBuildResult LastBuild => lastBuild;
    public Factory3DTraversalCollisionPresenter CollisionPresenter => collisionPresenter;
    public int ProxyCount => proxyRoot is not null && proxyRoot ? proxyRoot.childCount : 0;

    public IReadOnlyList<string> ProxyKeys
    {
        get
        {
            var keys = new List<string>();
            if (proxyRoot is null || !proxyRoot)
            {
                return keys;
            }

            for (var index = 0; index < proxyRoot.childCount; index++)
            {
                var identity = proxyRoot.GetChild(index).GetComponent<Factory3DInteriorProxyView>();
                if (identity is not null)
                {
                    keys.Add(identity.CanonicalId);
                }
            }

            return keys;
        }
    }

    public Factory3DInteriorProxyBuildResult Reconcile(
        NAIStateManager runtimeAuthority,
        InsideFactoryController runtimeController,
        SceneGrid grid,
        IndoorGrid indoorGrid)
    {
        EnsureProxyRoot();
        if (runtimeAuthority is null
            || !runtimeAuthority
            || !runtimeAuthority.IsInitialized
            || runtimeController is null
            || !runtimeController
            || grid is null
            || !grid
            || indoorGrid is null
            || !indoorGrid
            || runtimeController.gameObject.scene != gameObject.scene
            || grid.gameObject.scene != gameObject.scene
            || indoorGrid.gameObject.scene != gameObject.scene
            || runtimeController.BuildingInstanceId == 0
            || runtimeController.CurrentFloor < 0)
        {
            ClearPresentation();
            return lastBuild;
        }

        var snapshot = runtimeAuthority.BuildRoutePresentationSnapshot(
            grid,
            GetStoryHeight());
        return ReconcileSnapshot(
            snapshot,
            runtimeController,
            grid,
            indoorGrid.Size,
            runtimeAuthority);
    }

    public Factory3DInteriorProxyBuildResult ReconcileSnapshot(
        Factory3DRouteSnapshot snapshot,
        InsideFactoryController runtimeController,
        SceneGrid grid,
        Vector2Int interiorSize,
        NAIStateManager runtimeAuthority = null)
    {
        EnsureProxyRoot();
        if (snapshot is null
            || runtimeController is null
            || !runtimeController
            || grid is null
            || !grid
            || runtimeController.BuildingInstanceId == 0
            || runtimeController.CurrentFloor < 0
            || runtimeController.gameObject.scene != gameObject.scene
            || grid.gameObject.scene != gameObject.scene)
        {
            ClearPresentation();
            return lastBuild;
        }

        var selectedFloor = FindFloor(
            snapshot,
            runtimeController.BuildingInstanceId,
            runtimeController.CurrentFloor);
        if (!selectedFloor.HasValue)
        {
            ClearPresentation();
            return lastBuild;
        }

        var fingerprint = ComputeFingerprint(
            selectedFloor.Value,
            runtimeController.BuildingInstanceId,
            runtimeController.CurrentFloor,
            interiorSize,
            grid,
            GetStoryHeight());
        authority = runtimeAuthority;
        activeController = runtimeController;
        targetGrid = grid;
        if (hasFingerprint && fingerprint == lastFingerprint)
        {
            return lastBuild;
        }

        var settings = Factory3DInteriorProxySettings.ForGrid(
            GetStoryHeight(),
            grid.CellSize,
            GetMaterial());
        lastBuild = assembler.Reconcile(
            proxyRoot,
            grid,
            snapshot,
            runtimeController.BuildingInstanceId,
            runtimeController.CurrentFloor,
            interiorSize,
            settings);
        EnsureCollisionPresenter();
        collisionPresenter.Reconcile(
            snapshot,
            grid,
            runtimeController.BuildingInstanceId,
            runtimeController.CurrentFloor,
            interiorSize,
            GetStoryHeight(),
            true);
        lastFingerprint = fingerprint;
        hasFingerprint = true;
        return lastBuild;
    }

    public void Clear()
    {
        EnsureProxyRoot();
        ClearPresentation();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        var runtimeAuthority = NAIStateManager.Instance;
        var runtimeController = ResolveController();
        if (runtimeAuthority is null
            || !runtimeAuthority
            || runtimeController is null
            || !runtimeController
            || !SceneGrid.TryGetForScene(gameObject.scene, out var grid)
            || !IndoorGrid.TryGetForScene(gameObject.scene, out var indoorGrid))
        {
            ClearPresentation();
            return;
        }

        Reconcile(runtimeAuthority, runtimeController, grid, indoorGrid);
    }

    private InsideFactoryController ResolveController()
    {
        if (controller is not null
            && controller
            && controller.gameObject.scene == gameObject.scene)
        {
            return controller;
        }

        return InsideFactoryController.TryGetForScene(
            gameObject.scene,
            out var resolvedController)
            ? resolvedController
            : null!;
    }

    private void EnsureProxyRoot()
    {
        if (proxyRoot is not null && proxyRoot)
        {
            return;
        }

        var rootObject = new GameObject(ProxyRootName)
        {
            hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
        };
        rootObject.transform.SetParent(transform, false);
        proxyRoot = rootObject.transform;
    }

    private void ClearPresentation()
    {
        assembler.Clear(proxyRoot);
        if (collisionPresenter is not null && collisionPresenter)
        {
            collisionPresenter.Clear();
        }
        authority = null!;
        targetGrid = null!;
        activeController = null!;
        lastBuild = default;
        lastFingerprint = 0;
        hasFingerprint = false;
    }

    private void EnsureCollisionPresenter()
    {
        if (collisionPresenter is not null && collisionPresenter)
        {
            return;
        }

        collisionPresenter = GetComponent<Factory3DTraversalCollisionPresenter>();
        if (collisionPresenter is null || !collisionPresenter)
        {
            collisionPresenter = gameObject.AddComponent<Factory3DTraversalCollisionPresenter>();
        }
    }

    private Material GetMaterial()
    {
        if (material is not null && material)
        {
            return material;
        }

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
            name = "Factory3DInteriorProxy Material",
            color = new Color(0.22f, 0.72f, 0.62f, 1f),
            hideFlags = HideFlags.DontSave
        };
        return fallbackMaterial;
    }

    private float GetStoryHeight()
    {
        return float.IsNaN(storyHeight) || float.IsInfinity(storyHeight)
            ? DefaultStoryHeight
            : Mathf.Max(0.01f, storyHeight);
    }

    private static Factory3DRouteFloorSnapshot? FindFloor(
        Factory3DRouteSnapshot snapshot,
        uint buildingInstanceId,
        int floorIndex)
    {
        foreach (var floor in snapshot.Floors)
        {
            if (floor.BuildingInstanceId == buildingInstanceId
                && floor.FloorIndex == floorIndex)
            {
                return floor;
            }
        }

        return null;
    }

    private static int ComputeFingerprint(
        Factory3DRouteFloorSnapshot floor,
        uint buildingInstanceId,
        int floorIndex,
        Vector2Int interiorSize,
        SceneGrid grid,
        float storyHeight)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + buildingInstanceId.GetHashCode();
            hash = hash * 31 + floorIndex;
            hash = hash * 31 + interiorSize.GetHashCode();
            hash = hash * 31 + grid.GetHashCode();
            hash = hash * 31 + grid.CellSize.GetHashCode();
            hash = hash * 31 + storyHeight.GetHashCode();
            hash = hash * 31 + floor.WorldElevation.GetHashCode();
            hash = hash * 31 + floor.Entities.Count;
            foreach (var entity in floor.Entities)
            {
                hash = hash * 31 + entity.EntityId.GetHashCode();
                hash = hash * 31 + entity.DefinitionId.GetHashCode();
                hash = hash * 31 + entity.EntityType.GetHashCode();
                hash = hash * 31 + entity.RouteRole.GetHashCode();
                hash = hash * 31 + entity.LogicalPosition.GetHashCode();
                hash = hash * 31 + entity.WorldOrientation.GetHashCode();
                hash = hash * 31 + entity.InputCount;
                hash = hash * 31 + entity.OutputCount;
                hash = hash * 31 + entity.InventoryCount;
                hash = hash * 31 + entity.CycleProgress.GetHashCode();
            }

            return hash;
        }
    }

    private void OnDestroy()
    {
        if (proxyRoot is not null && proxyRoot)
        {
            assembler.Clear(proxyRoot);
            DestroyGeneratedObject(proxyRoot.gameObject);
        }

        DestroyGeneratedObject(fallbackMaterial);
        proxyRoot = null!;
        fallbackMaterial = null!;
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
