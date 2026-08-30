// Stores the replicated identity, grid anchor, and wall shape for one placed building.
using System;
using FishNet.CodeGenerating;
using UnityEngine;

[Serializable]
[IncludeSerialization]
public struct BuildingInstance : IEquatable<BuildingInstance>
{
    public uint Id;
    public string DefinitionId;
    public Vector3Int AnchorCell;
    public Vector2Int Size;
    public int OwnerClientId;
    public GridEdgeDirection Direction;
    public WallCellShape WallShape;

    public BuildingInstance(
        uint id,
        string definitionId,
        Vector3Int anchorCell,
        Vector2Int size,
        int ownerClientId,
        GridEdgeDirection direction = GridEdgeDirection.South,
        WallCellShape wallShape = WallCellShape.Horizontal)
    {
        Id = id;
        DefinitionId = definitionId;
        AnchorCell = anchorCell;
        Size = size;
        OwnerClientId = ownerClientId;
        Direction = direction;
        WallShape = wallShape;
    }

    public bool Equals(BuildingInstance other)
    {
        return Id == other.Id
            && DefinitionId == other.DefinitionId
            && AnchorCell == other.AnchorCell
            && Size == other.Size
            && OwnerClientId == other.OwnerClientId
            && Direction == other.Direction
            && WallShape == other.WallShape;
    }

    public override bool Equals(object value)
    {
        return value is BuildingInstance other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Id,
            DefinitionId,
            AnchorCell,
            Size,
            OwnerClientId,
            Direction,
            WallShape);
    }
}
