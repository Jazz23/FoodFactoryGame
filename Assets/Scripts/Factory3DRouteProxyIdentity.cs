// Identifies one disposable route proxy by its canonical authoritative route identity.
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Factory3DRouteProxyIdentity : MonoBehaviour
{
    public string CanonicalId { get; private set; } = string.Empty;
    public string DefinitionId { get; private set; } = string.Empty;
    public Factory3DRouteProxyKind Kind { get; private set; }
    public Factory3DRouteEntityId? EntityId { get; private set; }
    public Factory3DRouteEntityId? SourceEntityId { get; private set; }
    public Factory3DRouteEntityId? DestinationEntityId { get; private set; }

    public void Configure(
        string canonicalId,
        Factory3DRouteProxyKind kind,
        string definitionId = "",
        Factory3DRouteEntityId? entityId = null,
        Factory3DRouteEntityId? sourceEntityId = null,
        Factory3DRouteEntityId? destinationEntityId = null)
    {
        CanonicalId = canonicalId ?? string.Empty;
        Kind = kind;
        DefinitionId = definitionId ?? string.Empty;
        EntityId = entityId;
        SourceEntityId = sourceEntityId;
        DestinationEntityId = destinationEntityId;
    }
}

public enum Factory3DRouteProxyKind
{
    Entity,
    Connection,
    ElevatorTransfer
}
