// Marks a presentation-only equipment instance with the stable ID it shows. Destroying it never affects the equipment.
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [DisallowMultipleComponent]
    public sealed class EquipmentVisual : MonoBehaviour
    {
        public string EquipmentId { get; private set; }
        public string Kind { get; private set; }

        public void Bind(string equipmentId, string kind)
        {
            EquipmentId = equipmentId;
            Kind = kind;
        }
    }
}
