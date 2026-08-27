// Provides definition lookup for building placement and replicated building views.
using UnityEngine;

[CreateAssetMenu(menuName = "Food Factory/Building Catalog", fileName = "BuildingCatalog")]
public sealed class BuildingCatalog : ScriptableObject
{
    [SerializeField] private BuildingDefinition[] buildings = System.Array.Empty<BuildingDefinition>();

    public int Count => buildings.Length;

    public bool TryGetDefinition(string id, out BuildingDefinition definition)
    {
        foreach (var candidate in buildings)
        {
            if (candidate.Id == id)
            {
                definition = candidate;
                return true;
            }
        }

        definition = null!;
        return false;
    }

    public int GetIndex(string id)
    {
        for (var index = 0; index < buildings.Length; index++)
        {
            if (buildings[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    public BuildingDefinition GetDefinition(int index)
    {
        return buildings[index];
    }
}
