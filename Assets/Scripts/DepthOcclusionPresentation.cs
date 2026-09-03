// Defines the presentation-provider contract consumed by the scene-level depth coordinator.
using System.Collections.Generic;
using UnityEngine;

public interface IDepthOcclusionSurfaceProvider
{
    void CollectOcclusionSurfaces(List<DepthOcclusionSurface> surfaces);
}

public abstract class DepthOcclusionPresentation : MonoBehaviour, IDepthOcclusionSurfaceProvider
{
    public virtual void CollectOcclusionSurfaces(List<DepthOcclusionSurface> surfaces)
    {
        foreach (var surface in GetComponentsInChildren<DepthOcclusionSurface>(true))
        {
            surfaces.Add(surface);
        }
    }

    public abstract void SetOcclusionState(float targetAlpha, bool isInside);

    public virtual bool IsInside(Virtual3DSize player)
    {
        return false;
    }
}
