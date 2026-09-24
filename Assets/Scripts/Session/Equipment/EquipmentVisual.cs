// Marks a presentation-only equipment instance with the stable ID it shows. Destroying it never affects the equipment.
// Running mirrors the replicated job state and is forwarded to the model's running displays (glow, fans).
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [DisallowMultipleComponent]
    public sealed class EquipmentVisual : MonoBehaviour
    {
        private IEquipmentRunningDisplay[] _displays = System.Array.Empty<IEquipmentRunningDisplay>();

        public string EquipmentId { get; private set; }
        public string Kind { get; private set; }
        public bool Running { get; private set; }

        public void Bind(string equipmentId, string kind)
        {
            EquipmentId = equipmentId;
            Kind = kind;
            _displays = GetComponentsInChildren<IEquipmentRunningDisplay>(true);
        }

        public void SetRunning(bool running)
        {
            Running = running;
            foreach (var display in _displays) display.SetRunning(running);
        }
    }
}
