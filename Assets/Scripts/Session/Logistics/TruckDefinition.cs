// Authored truck model (decision 0023): the name bought trucks are numbered under and the stats each one copies when it is
// created, so a saved truck never depends on this asset afterwards.
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    [CreateAssetMenu(menuName = "Food Factory/Truck Definition")]
    public sealed class TruckDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Truck";
        [SerializeField, Min(1)] private int cargoSlots = 1;
        [SerializeField, Min(1)] private int speedMetresPerSecond = 1;
        [SerializeField, Min(1)] private int loadUnitsPerSecond = 1;

        public string DisplayName => displayName;
        public int CargoSlots => cargoSlots;
        public int SpeedMetresPerSecond => speedMetresPerSecond;
        public int LoadUnitsPerSecond => loadUnitsPerSecond;
    }
}
