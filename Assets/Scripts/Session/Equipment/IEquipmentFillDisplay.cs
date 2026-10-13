// Implemented by a visual prefab's components that show how full a piece's storage is (StorageFillDisplay). EquipmentPresenter
// sets it from the replicated site baseline only: the slots its input buffer's stacks take (GoodsSlots) out of that buffer's
// capacity. A display never decides or reports what is stored.
namespace FoodFactoryGame.Session.Equipment
{
    public interface IEquipmentFillDisplay
    {
        void SetFill(int slotsUsed, int capacity);
    }
}
