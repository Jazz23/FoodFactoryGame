// Tags a purchasable building's map model (WorldLayoutPresenter) with its lot, so aiming at it and pressing Interact opens the
// buy panel for that lot (EquipmentInteraction, PropertyPanel). Presentation only: it holds no ownership or price.
using UnityEngine;

namespace FoodFactoryGame.Session.WorldMap
{
    [DisallowMultipleComponent]
    public sealed class PropertyMarker : MonoBehaviour
    {
        public string LotId { get; private set; }
        public string BuildingId { get; private set; }

        public void Configure(string lotId, string buildingId)
        {
            LotId = lotId;
            BuildingId = buildingId;
        }
    }
}
