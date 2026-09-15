// Routes quick-access slot clicks to the owning player's inventory hotbar.
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class HotbarSlotView : MonoBehaviour, IPointerClickHandler
{
    private PlayerInventory inventory = null!;
    private int slotIndex;

    public void Initialize(PlayerInventory owner, int index)
    {
        inventory = owner;
        slotIndex = index;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        inventory.ClickHotbarSlot(slotIndex, eventData.button == PointerEventData.InputButton.Right);
    }
}
