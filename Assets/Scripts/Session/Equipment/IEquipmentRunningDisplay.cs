// Implemented by a visual prefab's components that show whether its machine is running a batch. EquipmentPresenter sets it
// from the replicated site baseline only; a display never decides or reports production state.
namespace FoodFactoryGame.Session.Equipment
{
    public interface IEquipmentRunningDisplay
    {
        void SetRunning(bool running);
    }
}
