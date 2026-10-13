// Presentation only: shows a storage piece filling up. Stages are drawn in order (a storage shelf has one box stack per slot,
// bottom deck first): as many as the share of used slots, rounded up, so any stored goods show at least one stage and a full
// buffer shows them all. Stages are authored inactive, so the placement ghost and an empty piece show none.
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [DisallowMultipleComponent]
    public sealed class StorageFillDisplay : MonoBehaviour, IEquipmentFillDisplay
    {
        [SerializeField] private GameObject[] stages = System.Array.Empty<GameObject>();

        public int Shown { get; private set; }

        public void SetFill(int slotsUsed, int capacity)
        {
            Shown = StagesFor(slotsUsed, capacity, stages.Length);
            for (var index = 0; index < stages.Length; index++)
                if (stages[index] != null && stages[index].activeSelf != index < Shown) stages[index].SetActive(index < Shown);
        }

        public static int StagesFor(int slotsUsed, int capacity, int stageCount)
        {
            if (slotsUsed <= 0 || capacity <= 0 || stageCount <= 0) return 0;
            return Mathf.Clamp((int)Mathf.Ceil((float)slotsUsed * stageCount / capacity), 1, stageCount);
        }
    }
}
