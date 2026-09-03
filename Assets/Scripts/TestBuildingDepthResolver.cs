// Supplies generated test-building surfaces to the scene-level occlusion coordinator.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TestBuildingDepthResolver : MonoBehaviour, IDepthOcclusionSurfaceProvider
{
    [SerializeField] private TestBuildingCreator creator = null!;

    private void Awake()
    {
        RefreshCreator();
    }

    public void CollectOcclusionSurfaces(List<DepthOcclusionSurface> surfaces)
    {
        RefreshCreator();
        if (creator is null || !creator
            || creator.GeneratedBuildings is null
            || !creator.GeneratedBuildings)
        {
            return;
        }

        foreach (var surface in creator.GeneratedBuildings.GetComponentsInChildren<DepthOcclusionSurface>(true))
        {
            if (surface.gameObject.scene == gameObject.scene)
            {
                surfaces.Add(surface);
            }
        }
    }

    private void RefreshCreator()
    {
        if (creator is null || !creator)
        {
            creator = GetComponent<TestBuildingCreator>();
        }
    }
}
