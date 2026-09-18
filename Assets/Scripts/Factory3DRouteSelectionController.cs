// Provides read-only Input System hover and selection for disposable route proxies.
using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class Factory3DRouteSelectionController : MonoBehaviour
{
    [SerializeField] private Camera selectionCamera = null!;

    private InputAction pointAction = null!;
    private Factory3DRouteSnapshot snapshot = null!;
    private Factory3DRouteProxyIdentity hoveredIdentity = null!;
    private MaterialPropertyBlock propertyBlock = null!;

    public Factory3DRouteEntityId? HoveredEntityId => hoveredIdentity is null
        ? null
        : hoveredIdentity.EntityId;

    public event Action<Factory3DRouteEntityId> EntityHovered;
    public event Action EntityCleared;

    public void Configure(
        Camera newSelectionCamera,
        Factory3DRouteSnapshot newSnapshot)
    {
        selectionCamera = newSelectionCamera;
        snapshot = newSnapshot;
    }

    public void SetSnapshot(Factory3DRouteSnapshot newSnapshot)
    {
        snapshot = newSnapshot;
        propertyBlock ??= new MaterialPropertyBlock();
        ApplyHighlight(null);
    }

    private void Awake()
    {
        pointAction = InputSystem.actions.FindAction("UI/Point", true).Clone();
        pointAction.Enable();
        propertyBlock = new MaterialPropertyBlock();
    }

    private void Update()
    {
        if (selectionCamera is null || !selectionCamera || pointAction is null)
        {
            return;
        }

        var ray = selectionCamera.ScreenPointToRay(pointAction.ReadValue<Vector2>());
        var nextIdentity = Physics.Raycast(ray, out var hit)
            ? hit.collider.GetComponentInParent<Factory3DRouteProxyIdentity>()
            : null;
        if (nextIdentity == hoveredIdentity)
        {
            return;
        }

        hoveredIdentity = nextIdentity;
        ApplyHighlight(hoveredIdentity);
        if (hoveredIdentity is not null && hoveredIdentity.EntityId.HasValue)
        {
            EntityHovered?.Invoke(hoveredIdentity.EntityId.Value);
        }
        else
        {
            EntityCleared?.Invoke();
        }
    }

    private void OnDestroy()
    {
        if (pointAction is not null)
        {
            pointAction.Disable();
            pointAction.Dispose();
        }
    }

    private void ApplyHighlight(Factory3DRouteProxyIdentity selected)
    {
        propertyBlock ??= new MaterialPropertyBlock();
        foreach (var identity in GetComponentsInChildren<Factory3DRouteProxyIdentity>(true))
        {
            var highlighted = IsConnected(identity, selected);
            var renderer = identity.GetComponent<Renderer>();
            if (renderer is null || !renderer)
            {
                continue;
            }

            renderer.GetPropertyBlock(propertyBlock);
            var color = highlighted
                ? new Color(1f, 0.85f, 0.2f, 1f)
                : Color.white;
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    private bool IsConnected(
        Factory3DRouteProxyIdentity identity,
        Factory3DRouteProxyIdentity selected)
    {
        if (selected is null || !selected.EntityId.HasValue)
        {
            return false;
        }

        var selectedId = selected.EntityId.Value;
        if (identity.EntityId == selectedId
            || identity.SourceEntityId == selectedId
            || identity.DestinationEntityId == selectedId)
        {
            return true;
        }

        if (snapshot is null || !identity.EntityId.HasValue)
        {
            return false;
        }

        foreach (var connection in snapshot.Connections)
        {
            if ((connection.Source == selectedId && identity.EntityId == connection.Destination)
                || (connection.Destination == selectedId && identity.EntityId == connection.Source))
            {
                return true;
            }
        }

        return false;
    }
}
