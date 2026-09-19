// Owns the disposable scene object that presents derived 3D OutsideTest proxy geometry.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class Factory3DOutsideTestProxyView : MonoBehaviour
{
    public const string RouteProxyRootName = Factory3DRouteProxyAssembler.RootName;
    private const string WallRootPrefix = "Wall ";

    private Factory3DOutsideTestProxyAssembler assembler = null!;
    private Material fallbackMaterial = null!;
    private int activeFloor = -1;
    private bool exteriorOcclusionSurfacesVisible = true;

    public int ActiveFloor => activeFloor;
    public bool ExteriorOcclusionSurfacesVisible => exteriorOcclusionSurfacesVisible;

    public Factory3DOutsideTestProxyBuildResult Rebuild(
        SceneGrid grid,
        FactoryWorldState state,
        float wallHeight,
        float doorCornerExclusionDistance,
        Material material = null)
    {
        var result = state is null
            ? Rebuild(
                grid,
                Array.Empty<BuildingRecord>(),
                Array.Empty<OutsideTestFloorRecord>(),
                wallHeight,
                doorCornerExclusionDistance,
                material)
            : RebuildStateAware(
                grid,
                state,
                wallHeight,
                doorCornerExclusionDistance,
                material);
        ApplyActiveFloor();
        return result;
    }

    private Factory3DOutsideTestProxyBuildResult RebuildStateAware(
        SceneGrid grid,
        FactoryWorldState state,
        float wallHeight,
        float doorCornerExclusionDistance,
        Material material)
    {
        if (assembler is null)
        {
            assembler = new Factory3DOutsideTestProxyAssembler();
        }

        if (grid is null || !grid)
        {
            assembler.Clear(transform);
            return default;
        }

        var effectiveMaterial = material is not null
            ? material
            : GetFallbackMaterial();
        var settings = Factory3DOutsideTestProxySettings.ForGrid(
            wallHeight,
            grid.CellSize,
            doorCornerExclusionDistance,
            effectiveMaterial);
        var result = assembler.Reconcile(
            transform,
            grid,
            state,
            settings);
        return result;
    }

    public Factory3DOutsideTestProxyBuildResult Rebuild(
        SceneGrid grid,
        IEnumerable<BuildingRecord> records,
        IEnumerable<OutsideTestFloorRecord> floors,
        float wallHeight,
        float doorCornerExclusionDistance,
        Material material = null)
    {
        if (assembler is null)
        {
            assembler = new Factory3DOutsideTestProxyAssembler();
        }

        if (grid is null || !grid)
        {
            assembler.Clear(transform);
            return default;
        }

        var effectiveMaterial = material is not null
            ? material
            : GetFallbackMaterial();
        var settings = Factory3DOutsideTestProxySettings.ForGrid(
            wallHeight,
            grid.CellSize,
            doorCornerExclusionDistance,
            effectiveMaterial);
        var result = assembler.Reconcile(
            transform,
            grid,
            records,
            floors,
            settings);
        ApplyActiveFloor();
        return result;
    }

    public Factory3DOutsideTestProxyBuildResult Rebuild(
        SceneGrid grid,
        IEnumerable<Factory3DOutsideTestProxyBuildingSource> buildings,
        IEnumerable<OutsideTestFloorRecord> floors,
        float wallHeight,
        float doorCornerExclusionDistance,
        Material material = null)
    {
        if (assembler is null)
        {
            assembler = new Factory3DOutsideTestProxyAssembler();
        }

        if (grid is null || !grid)
        {
            assembler.Clear(transform);
            return default;
        }

        var effectiveMaterial = material is not null
            ? material
            : GetFallbackMaterial();
        var settings = Factory3DOutsideTestProxySettings.ForGrid(
            wallHeight,
            grid.CellSize,
            doorCornerExclusionDistance,
            effectiveMaterial);
        var result = assembler.Reconcile(
            transform,
            grid,
            buildings,
            floors,
            settings);
        ApplyActiveFloor();
        return result;
    }

    public void SetActiveFloor(int floorIndex)
    {
        activeFloor = Mathf.Max(-1, floorIndex);
        ApplyActiveFloor();
    }

    public void ClearActiveFloor()
    {
        activeFloor = -1;
        ApplyActiveFloor();
    }

    public void SetExteriorOcclusionSurfacesVisible(bool visible)
    {
        exteriorOcclusionSurfacesVisible = visible;
        ApplyActiveFloor();
    }

    public void Clear()
    {
        if (assembler is null)
        {
            assembler = new Factory3DOutsideTestProxyAssembler();
        }

        assembler.Clear(transform);
        activeFloor = -1;
        exteriorOcclusionSurfacesVisible = true;
    }

    public Factory3DRouteProxyView GetOrCreateRouteProxyView()
    {
        var routeView = (Factory3DRouteProxyView)null!;
        var duplicateRoots = new List<GameObject>();
        for (var index = 0; index < transform.childCount; index++)
        {
            var child = transform.GetChild(index);
            if (child.name != RouteProxyRootName)
            {
                continue;
            }

            var candidate = child.GetComponent<Factory3DRouteProxyView>();
            if (routeView is null || !routeView)
            {
                routeView = candidate;
                if (routeView is null || !routeView)
                {
                    routeView = child.gameObject.AddComponent<Factory3DRouteProxyView>();
                }

                continue;
            }

            duplicateRoots.Add(child.gameObject);
        }

        foreach (var duplicateRoot in duplicateRoots)
        {
            DestroyGeneratedObject(duplicateRoot);
        }

        if (routeView is null || !routeView)
        {
            var routeRoot = new GameObject(RouteProxyRootName);
            routeRoot.transform.SetParent(transform, false);
            ConfigureDisposableFlags(routeRoot);
            routeView = routeRoot.AddComponent<Factory3DRouteProxyView>();
        }

        ConfigureDisposableFlags(routeView.gameObject);
        return routeView;
    }

    public Factory3DRouteProxyView FindRouteProxyView()
    {
        for (var index = 0; index < transform.childCount; index++)
        {
            var child = transform.GetChild(index);
            if (child.name != RouteProxyRootName)
            {
                continue;
            }

            var routeView = child.GetComponent<Factory3DRouteProxyView>();
            if (routeView is not null && routeView)
            {
                return routeView;
            }
        }

        return null!;
    }

    public static Factory3DOutsideTestProxyView FindExisting(Scene scene)
    {
        if (!scene.IsValid())
        {
            return null!;
        }

        foreach (var candidate in Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>())
        {
            if (candidate is not null
                && candidate
                && candidate.gameObject.scene == scene)
            {
                return candidate;
            }
        }

        return null!;
    }

    public static Factory3DOutsideTestProxyView FindOrCreate(Scene scene)
    {
        if (!scene.IsValid())
        {
            scene = SceneManager.GetActiveScene();
        }

        var view = (Factory3DOutsideTestProxyView)null!;
        // Resources lookup includes the DontSave proxy root, which normal scene searches omit.
        var candidates = Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>();
        foreach (var candidate in candidates)
        {
            if (candidate is null
                || !candidate
                || candidate.gameObject.scene != scene)
            {
                continue;
            }

            if (view is null || !view)
            {
                view = candidate;
                continue;
            }

            candidate.Clear();
            DestroyGeneratedObject(candidate.gameObject);
        }

        if (view is not null && view)
        {
            ConfigureDisposableFlags(view.gameObject);
            return view;
        }

        var root = new GameObject(Factory3DOutsideTestProxyAssembler.RootName);
        ConfigureDisposableFlags(root);
        if (scene.IsValid() && root.scene != scene)
        {
            SceneManager.MoveGameObjectToScene(root, scene);
        }

        return root.AddComponent<Factory3DOutsideTestProxyView>();
    }

    private Material GetFallbackMaterial()
    {
        if (fallbackMaterial is not null && fallbackMaterial)
        {
            return fallbackMaterial;
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader is null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader is null)
        {
            return null!;
        }

        fallbackMaterial = new Material(shader)
        {
            name = "Factory3DOutsideTestProxy Material",
            color = new Color(0.18f, 0.55f, 0.86f, 1f),
            hideFlags = HideFlags.DontSave
        };
        return fallbackMaterial;
    }

    private void ApplyActiveFloor()
    {
        for (var buildingIndex = 0; buildingIndex < transform.childCount; buildingIndex++)
        {
            var building = transform.GetChild(buildingIndex);
            if (building.name == RouteProxyRootName)
            {
                continue;
            }

            for (var floorIndex = 0; floorIndex < building.childCount; floorIndex++)
            {
                var floor = building.GetChild(floorIndex);
                if (!TryParseFloorIndex(floor.name, out var parsedFloorIndex))
                {
                    continue;
                }

                var isVisible = activeFloor < 0 || parsedFloorIndex == activeFloor;
                floor.gameObject.SetActive(isVisible);
                for (var childIndex = 0; childIndex < floor.childCount; childIndex++)
                {
                    var child = floor.GetChild(childIndex);
                    if (child.name.StartsWith(WallRootPrefix, StringComparison.Ordinal))
                    {
                        child.gameObject.SetActive(isVisible);
                    }
                }

                var slab = floor.Find(Factory3DOutsideTestProxyAssembler.FloorSlabName);
                if (slab is not null && slab)
                {
                    slab.gameObject.SetActive(activeFloor >= 0 && parsedFloorIndex == activeFloor);
                }

                var roof = floor.Find(Factory3DOutsideTestProxyAssembler.RoofName);
                if (roof is not null && roof)
                {
                    roof.gameObject.SetActive(
                        isVisible
                        && activeFloor < 0
                        && exteriorOcclusionSurfacesVisible);
                }
            }
        }
    }

    private static bool TryParseFloorIndex(string floorName, out int floorIndex)
    {
        const string prefix = "Floor ";
        if (!floorName.StartsWith(prefix, StringComparison.Ordinal))
        {
            floorIndex = -1;
            return false;
        }

        return int.TryParse(floorName.Substring(prefix.Length), out floorIndex);
    }

    private void Awake()
    {
        ConfigureDisposableFlags(gameObject);
    }

    private void OnDestroy()
    {
        if (assembler is not null)
        {
            assembler.Clear(transform);
        }

        DestroyGeneratedObject(fallbackMaterial);
        fallbackMaterial = null!;
    }

    private static void ConfigureDisposableFlags(GameObject target)
    {
        target.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
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
