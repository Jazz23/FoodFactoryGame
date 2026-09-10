// Keeps the shared NAI definition identifiers separate from prefab instance identities and behavior.
using System;
using System.Collections.Generic;

namespace NotAI
{
    public readonly struct NAIBuildableDefinition
    {
        public NAIBuildableDefinition(string newId, int newLegacyIndex)
        {
            Id = newId;
            LegacyIndex = newLegacyIndex;
        }

        public string Id { get; }
        public int LegacyIndex { get; }
        public bool IsPassive => true;
    }

    public static class NAIBuildableDefinitionCatalog
    {
        public const string PizzaOvenDefinitionId = "nai-pizza-oven";
        public const string BeltDefinitionId = "nai-belt";

        private static readonly NAIBuildableDefinition[] definitions =
        {
            new(PizzaOvenDefinitionId, 0),
            new(BeltDefinitionId, 1)
        };

        public static IReadOnlyList<NAIBuildableDefinition> Definitions => definitions;

        public static string GetDefinitionId(int legacyIndex)
        {
            foreach (var definition in definitions)
            {
                if (definition.LegacyIndex == legacyIndex)
                {
                    return definition.Id;
                }
            }

            throw new InvalidOperationException($"No NAI definition exists for legacy index {legacyIndex}.");
        }

        public static bool Contains(string definitionId)
        {
            foreach (var definition in definitions)
            {
                if (definition.Id == definitionId)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetLegacyIndex(string definitionId, out int legacyIndex)
        {
            foreach (var definition in definitions)
            {
                if (definition.Id == definitionId)
                {
                    legacyIndex = definition.LegacyIndex;
                    return true;
                }
            }

            legacyIndex = -1;
            return false;
        }
    }
}
