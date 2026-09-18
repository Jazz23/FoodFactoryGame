// Owns the disposable scene object that presents derived 3D OutsideTest proxy geometry.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class Factory3DOutsideTestProxyView : MonoBehaviour
{
    private Factory3DOutsideTestProxyAssembler assembler = null!;
    private Material fallbackMaterial = null!;

    public Factory3DOutsideTestProxyBuildResult Rebuild(
        SceneGrid grid,
        FactoryWorldState state,
        float wallHeight,
        float doorCornerExclusionDistance)
    {
        return state is null
            ? Rebuild(
                grid,
                Array.Empty<BuildingRecord>(),
                Array.Empty<OutsideTestFloorRecord>(),
                wallHeight,
                doorCornerExclusionDistance)
            : RebuildStateAware(
                grid,
                state,
                wallHeight,
                doorCornerExclusionDistance);
    }

    private Factory3DOutsideTestProxyBuildResult RebuildStateAware(
        SceneGrid grid,
        FactoryWorldState state,
        float wallHeight,
        float doorCornerExclusionDistance)
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

        var effectiveMaterial = GetFallbackMaterial();
        var settings = Factory3DOutsideTestProxySettings.ForGrid(
            wallHeight,
            grid.CellSize,
            doorCornerExclusionDistance,
            effectiveMaterial);
        return assembler.Reconcile(
            transform,
            grid,
            state,
            settings);
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
        return assembler.Reconcile(
            transform,
            grid,
            records,
            floors,
            settings);
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
        return assembler.Reconcile(
            transform,
            grid,
            buildings,
            floors,
            settings);
    }

    public void Clear()
    {
        if (assembler is null)
        {
            assembler = new Factory3DOutsideTestProxyAssembler();
        }

        assembler.Clear(transform);
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
