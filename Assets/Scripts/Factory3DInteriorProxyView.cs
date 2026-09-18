// Stores stable identity and presentation metadata for one disposable interior proxy.
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Factory3DInteriorProxyView : MonoBehaviour
{
    public string CanonicalId { get; private set; } = string.Empty;
    public uint BuildingInstanceId { get; private set; }
    public int FloorIndex { get; private set; } = -1;
    public uint EntityId { get; private set; }
    public string DefinitionId { get; private set; } = string.Empty;
    public Factory3DInteriorProxyKind Kind { get; private set; }

    public void Configure(
        string canonicalId,
        uint buildingInstanceId,
        int floorIndex,
        Factory3DInteriorProxyKind kind,
        uint entityId = 0,
        string definitionId = "")
    {
        CanonicalId = canonicalId ?? string.Empty;
        BuildingInstanceId = buildingInstanceId;
        FloorIndex = floorIndex;
        Kind = kind;
        EntityId = entityId;
        DefinitionId = definitionId ?? string.Empty;
    }
}

public enum Factory3DInteriorProxyKind
{
    FloorSlab,
    Wall,
    Entity
}
