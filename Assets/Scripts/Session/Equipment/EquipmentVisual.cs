// Marks a presentation-only equipment instance with the stable ID it shows. Destroying it never affects the equipment.
// Running mirrors the replicated job state and is forwarded to the model's running displays (glow, fans); the storage fill is
// forwarded to its fill displays (a shelf's boxes).
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [DisallowMultipleComponent]
    public sealed class EquipmentVisual : MonoBehaviour
    {
        private IEquipmentRunningDisplay[] _displays = System.Array.Empty<IEquipmentRunningDisplay>();
        private IEquipmentFillDisplay[] _fills = System.Array.Empty<IEquipmentFillDisplay>();

        public string EquipmentId { get; private set; }
        public string Kind { get; private set; }
        public bool Running { get; private set; }

        public void Bind(string equipmentId, string kind)
        {
            EquipmentId = equipmentId;
            Kind = kind;
            _displays = GetComponentsInChildren<IEquipmentRunningDisplay>(true);
            _fills = GetComponentsInChildren<IEquipmentFillDisplay>(true);
        }

        public void SetFill(int slotsUsed, int capacity)
        {
            foreach (var fill in _fills) fill.SetFill(slotsUsed, capacity);
        }

        public void SetRunning(bool running)
        {
            Running = running;
            foreach (var display in _displays) display.SetRunning(running);
        }
    }
}
