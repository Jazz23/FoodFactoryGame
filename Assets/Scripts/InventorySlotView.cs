// Routes a Factorio-style inventory slot's pointer interactions to its owning player inventory.
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class InventorySlotView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
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
        inventory.ClickSlot(slotIndex, eventData.button == PointerEventData.InputButton.Right);
    }

    public void OnPointerEnter(PointerEventData _)
    {
        inventory.HoverSlot(slotIndex);
    }

    public void OnPointerExit(PointerEventData _)
    {
        inventory.ClearHover(slotIndex);
    }
}
