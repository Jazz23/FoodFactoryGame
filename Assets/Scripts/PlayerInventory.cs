// Provides the local player's persistent, Factorio-inspired grid inventory and runtime UI.
using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Object;
using SQLite;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class PlayerInventory : NetworkBehaviour
{
    private const string PlayerSteamId = "76561198000000000";
    private const int SlotCount = 80;
    private const int Columns = 10;

    private static readonly Dictionary<string, ItemDefinition> ItemDefinitions = new()
    {
        { "iron-plate", new("Iron Plate", "Fe", 100, new Color(0.68f, 0.76f, 0.83f)) },
        { "copper-plate", new("Copper Plate", "Cu", 100, new Color(0.88f, 0.48f, 0.25f)) },
        { "stone-brick", new("Stone Brick", "BR", 100, new Color(0.62f, 0.58f, 0.52f)) },
        { "conveyor-belt", new("Conveyor Belt", "CV", 100, new Color(0.79f, 0.68f, 0.28f)) },
        { "wall", new("Wall", "WL", 50, new Color(0.52f, 0.56f, 0.61f)) },
        { "factory-building", new("Factory Building", "FB", 20, new Color(0.36f, 0.72f, 0.59f)) }
    };

    private readonly InventoryStack?[] slots = new InventoryStack?[SlotCount];
    private readonly List<SlotVisual> slotVisuals = new();
    private SQLiteConnection database = null!;
    private InputAction toggle = null!;
    private InputAction close = null!;
    private InputAction clearCursor = null!;
    private InputAction transferStackModifier = null!;
    private InputAction transferAllModifier = null!;
    private InputActionMap buildActions = null!;
    private GameObject inventoryRoot = null!;
    private GameObject? createdEventSystem;
    private Text cursorText = null!;
    private Text tooltipText = null!;
    private Text statusText = null!;
    private InventoryStack? cursorStack;
    private int cursorSourceSlot = -1;
    private int hoveredSlot = -1;
    private bool isOpen;

    public static PlayerInventory LocalOwner = null!;
    public bool IsOpen => isOpen;

    public override void OnStartClient()
    {
        if (!IsOwner)
        {
            return;
        }

        LocalOwner = this;
        database = new SQLiteConnection(Path.Combine(Application.persistentDataPath, "food-factory-inventory.db"));
        database.CreateTable<PlayerInventoryProfile>();
        database.CreateTable<PlayerInventoryRecord>();
        LoadInventory();
        CreateEventSystem();
        CreateInterface();

        toggle = InputSystem.actions.FindAction("Inventory/Toggle", true);
        close = InputSystem.actions.FindAction("Inventory/Close", true);
        clearCursor = InputSystem.actions.FindAction("Inventory/ClearCursor", true);
        transferStackModifier = InputSystem.actions.FindAction("Inventory/TransferStackModifier", true);
        transferAllModifier = InputSystem.actions.FindAction("Inventory/TransferAllModifier", true);
        buildActions = InputSystem.actions.FindActionMap("Build", true);
        toggle.performed += TogglePerformed;
        close.performed += ClosePerformed;
        clearCursor.performed += ClearCursorPerformed;
        toggle.Enable();
        close.Enable();
        clearCursor.Enable();
        transferStackModifier.Enable();
        transferAllModifier.Enable();
    }

    public override void OnStopClient()
    {
        if (!IsOwner)
        {
            return;
        }

        if (LocalOwner == this)
        {
            LocalOwner = null!;
        }

        toggle.performed -= TogglePerformed;
        close.performed -= ClosePerformed;
        clearCursor.performed -= ClearCursorPerformed;
        toggle.Disable();
        close.Disable();
        clearCursor.Disable();
        transferStackModifier.Disable();
        transferAllModifier.Disable();
        buildActions.Enable();
        SaveInventory();
        database.Dispose();
        Destroy(inventoryRoot);
        if (createdEventSystem is not null)
        {
            Destroy(createdEventSystem);
        }
    }

    public void ClickSlot(int index, bool rightClick)
    {
        if (transferStackModifier.IsPressed() || transferAllModifier.IsPressed())
        {
            statusText.text = "Transfer needs an open machine or container.";
            return;
        }

        var slot = slots[index];
        if (cursorStack is null)
        {
            PickUp(index, slot, rightClick);
        }
        else
        {
            Place(index, slot, rightClick);
        }

        SaveInventory();
        RefreshInterface();
    }

    public void HoverSlot(int index)
    {
        hoveredSlot = index;
        var slot = slots[index];
        tooltipText.text = slot is null
            ? $"Slot {index + 1}: Empty"
            : $"{ItemDefinitions[slot.ItemId].Name}  {slot.Count} / {ItemDefinitions[slot.ItemId].MaxStackSize}";
        RefreshSlotVisuals();
    }

    public void ClearHover(int index)
    {
        if (hoveredSlot != index)
        {
            return;
        }

        hoveredSlot = -1;
        tooltipText.text = "Left click: stack   Right click: half / one   Q: return cursor";
        RefreshSlotVisuals();
    }

    private void TogglePerformed(InputAction.CallbackContext _)
    {
        if (isOpen)
        {
            CloseInventory();
            return;
        }

        OpenInventory();
    }

    private void ClosePerformed(InputAction.CallbackContext _)
    {
        if (isOpen)
        {
            CloseInventory();
        }
    }

    private void ClearCursorPerformed(InputAction.CallbackContext _)
    {
        if (!isOpen || cursorStack is null)
        {
            return;
        }

        ReturnCursorToInventory();
        SaveInventory();
        RefreshInterface();
    }

    private void OpenInventory()
    {
        isOpen = true;
        inventoryRoot.SetActive(true);
        buildActions.Disable();
        statusText.text = "Personal inventory";
        RefreshInterface();
    }

    private void CloseInventory()
    {
        isOpen = false;
        inventoryRoot.SetActive(false);
        buildActions.Enable();
        SaveInventory();
    }

    private void PickUp(int index, InventoryStack? slot, bool rightClick)
    {
        if (slot is null)
        {
            statusText.text = "Empty slot.";
            return;
        }

        cursorSourceSlot = index;
        if (!rightClick)
        {
            cursorStack = slot;
            slots[index] = null;
            statusText.text = $"Picked up {slot.Count} {ItemDefinitions[slot.ItemId].Name}.";
            return;
        }

        var count = (slot.Count + 1) / 2;
        cursorStack = new InventoryStack(slot.ItemId, count);
        slot.Count -= count;
        if (slot.Count == 0)
        {
            slots[index] = null;
        }
        statusText.text = $"Picked up {count} {ItemDefinitions[cursorStack.ItemId].Name}.";
    }

    private void Place(int index, InventoryStack? slot, bool rightClick)
    {
        if (slot is null)
        {
            var count = rightClick ? 1 : cursorStack!.Count;
            slots[index] = new InventoryStack(cursorStack!.ItemId, count);
            cursorStack.Count -= count;
            if (cursorStack.Count == 0)
            {
                cursorStack = null;
                cursorSourceSlot = -1;
            }
            statusText.text = $"Placed {count}.";
            return;
        }

        if (slot.ItemId == cursorStack!.ItemId)
        {
            var capacity = ItemDefinitions[slot.ItemId].MaxStackSize - slot.Count;
            var count = Mathf.Min(rightClick ? 1 : cursorStack.Count, capacity);
            if (count == 0)
            {
                statusText.text = "Stack is full.";
                return;
            }

            slot.Count += count;
            cursorStack.Count -= count;
            if (cursorStack.Count == 0)
            {
                cursorStack = null;
                cursorSourceSlot = -1;
            }
            statusText.text = $"Moved {count}.";
            return;
        }

        if (rightClick)
        {
            statusText.text = "Different item. Left click to swap.";
            return;
        }

        slots[index] = cursorStack;
        cursorStack = slot;
        cursorSourceSlot = index;
        statusText.text = "Swapped stacks.";
    }

    private void ReturnCursorToInventory()
    {
        if (cursorSourceSlot >= 0 && TryInsert(cursorSourceSlot, cursorStack!))
        {
            statusText.text = "Returned cursor stack to its source.";
            cursorSourceSlot = -1;
            return;
        }

        for (var index = 0; index < SlotCount && cursorStack is not null; index++)
        {
            TryInsert(index, cursorStack);
        }

        statusText.text = cursorStack is null
            ? "Returned cursor stack to inventory."
            : "Inventory is full; cursor stack is still held.";
    }

    private bool TryInsert(int index, InventoryStack stack)
    {
        var slot = slots[index];
        if (slot is null)
        {
            slots[index] = new InventoryStack(stack.ItemId, stack.Count);
            cursorStack = null;
            return true;
        }

        if (slot.ItemId != stack.ItemId)
        {
            return false;
        }

        var capacity = ItemDefinitions[slot.ItemId].MaxStackSize - slot.Count;
        if (capacity == 0)
        {
            return false;
        }

        var count = Mathf.Min(capacity, stack.Count);
        slot.Count += count;
        stack.Count -= count;
        if (stack.Count == 0)
        {
            cursorStack = null;
            return true;
        }

        return false;
    }

    private void LoadInventory()
    {
        Array.Clear(slots, 0, slots.Length);
        var hasProfile = database.Find<PlayerInventoryProfile>(PlayerSteamId) is not null;
        var records = database.Query<PlayerInventoryRecord>(
            "SELECT * FROM PlayerInventoryRecord WHERE PlayerId = ?",
            PlayerSteamId);
        if (!hasProfile)
        {
            database.Insert(new PlayerInventoryProfile { PlayerId = PlayerSteamId });
        }

        foreach (var record in records)
        {
            if (!ItemDefinitions.ContainsKey(record.ItemId))
            {
                continue;
            }

            if (record.SlotIndex == -1)
            {
                cursorStack = new InventoryStack(record.ItemId, record.Count);
                cursorSourceSlot = record.CursorSourceSlot;
                continue;
            }

            if (record.SlotIndex >= 0 && record.SlotIndex < SlotCount)
            {
                slots[record.SlotIndex] = new InventoryStack(record.ItemId, record.Count);
            }
        }

        if (hasProfile || records.Count > 0)
        {
            return;
        }

        slots[0] = new InventoryStack("iron-plate", 100);
        slots[1] = new InventoryStack("copper-plate", 100);
        slots[2] = new InventoryStack("stone-brick", 60);
        slots[3] = new InventoryStack("conveyor-belt", 100);
        slots[4] = new InventoryStack("wall", 50);
        slots[5] = new InventoryStack("factory-building", 10);
        slots[6] = new InventoryStack("iron-plate", 45);
        SaveInventory();
    }

    private void SaveInventory()
    {
        database.Execute("DELETE FROM PlayerInventoryRecord WHERE PlayerId = ?", PlayerSteamId);
        var records = new List<PlayerInventoryRecord>();
        for (var index = 0; index < SlotCount; index++)
        {
            var slot = slots[index];
            if (slot is null)
            {
                continue;
            }

            records.Add(new PlayerInventoryRecord
            {
                PlayerId = PlayerSteamId,
                SlotIndex = index,
                ItemId = slot.ItemId,
                Count = slot.Count
            });
        }

        if (cursorStack is not null)
        {
            records.Add(new PlayerInventoryRecord
            {
                PlayerId = PlayerSteamId,
                SlotIndex = -1,
                ItemId = cursorStack.ItemId,
                Count = cursorStack.Count,
                CursorSourceSlot = cursorSourceSlot
            });
        }

        if (records.Count > 0)
        {
            database.InsertAll(records);
        }
    }

    private void CreateEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() is not null)
        {
            return;
        }

        createdEventSystem = new GameObject("Inventory EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    private void CreateInterface()
    {
        var canvasObject = new GameObject("Inventory Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        inventoryRoot = CreateImage("Inventory Overlay", canvasObject.transform, new Color(0.015f, 0.025f, 0.035f, 0.8f));
        Stretch(inventoryRoot.GetComponent<RectTransform>()!);
        var window = CreateImage("Inventory Window", inventoryRoot.transform, new Color(0.1f, 0.13f, 0.16f, 0.98f));
        var windowRect = window.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.sizeDelta = new Vector2(740f, 630f);
        windowRect.anchoredPosition = Vector2.zero;

        CreateText("Title", window.transform, "INVENTORY", 28, TextAnchor.MiddleLeft, new Color(0.91f, 0.94f, 0.96f));
        var titleRect = window.transform.Find("Title")!.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.offsetMin = new Vector2(28f, -54f);
        titleRect.offsetMax = new Vector2(-28f, -16f);

        cursorText = CreateText("Cursor", window.transform, string.Empty, 16, TextAnchor.MiddleLeft, new Color(0.97f, 0.8f, 0.32f));
        var cursorRect = cursorText.GetComponent<RectTransform>();
        cursorRect.anchorMin = new Vector2(0f, 1f);
        cursorRect.anchorMax = new Vector2(1f, 1f);
        cursorRect.offsetMin = new Vector2(28f, -84f);
        cursorRect.offsetMax = new Vector2(-28f, -56f);

        var grid = new GameObject("Slot Grid", typeof(RectTransform), typeof(GridLayoutGroup));
        grid.transform.SetParent(window.transform, false);
        var gridRect = grid.GetComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0.5f, 0.5f);
        gridRect.anchorMax = new Vector2(0.5f, 0.5f);
        gridRect.sizeDelta = new Vector2(650f, 430f);
        gridRect.anchoredPosition = new Vector2(0f, -15f);
        var gridLayout = grid.GetComponent<GridLayoutGroup>();
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = Columns;
        gridLayout.cellSize = new Vector2(61f, 47f);
        gridLayout.spacing = new Vector2(4f, 4f);
        gridLayout.padding = new RectOffset(2, 2, 2, 2);

        for (var index = 0; index < SlotCount; index++)
        {
            CreateSlot(grid.transform, index);
        }

        tooltipText = CreateText("Tooltip", window.transform, string.Empty, 15, TextAnchor.MiddleLeft, new Color(0.78f, 0.85f, 0.9f));
        var tooltipRect = tooltipText.GetComponent<RectTransform>();
        tooltipRect.anchorMin = new Vector2(0f, 0f);
        tooltipRect.anchorMax = new Vector2(1f, 0f);
        tooltipRect.offsetMin = new Vector2(28f, 46f);
        tooltipRect.offsetMax = new Vector2(-28f, 74f);

        statusText = CreateText("Status", window.transform, "Personal inventory", 14, TextAnchor.MiddleLeft, new Color(0.58f, 0.78f, 0.67f));
        var statusRect = statusText.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(1f, 0f);
        statusRect.offsetMin = new Vector2(28f, 21f);
        statusRect.offsetMax = new Vector2(-28f, 48f);

        var controls = CreateText("Controls", window.transform, "TAB / ESC Close    Q Return Cursor    Shift / Ctrl Transfer when a container is open", 13, TextAnchor.MiddleCenter, new Color(0.55f, 0.62f, 0.68f));
        var controlsRect = controls.GetComponent<RectTransform>();
        controlsRect.anchorMin = new Vector2(0f, 0f);
        controlsRect.anchorMax = new Vector2(1f, 0f);
        controlsRect.offsetMin = new Vector2(28f, 3f);
        controlsRect.offsetMax = new Vector2(-28f, 26f);

        inventoryRoot.SetActive(false);
        RefreshInterface();
    }

    private void CreateSlot(Transform parent, int index)
    {
        var slot = CreateImage($"Slot {index + 1}", parent, new Color(0.18f, 0.21f, 0.24f));
        var slotView = slot.AddComponent<InventorySlotView>();
        slotView.Initialize(this, index);
        var itemText = CreateText("Item", slot.transform, string.Empty, 18, TextAnchor.MiddleCenter, Color.white);
        Stretch(itemText.GetComponent<RectTransform>()!);
        itemText.raycastTarget = false;
        var countText = CreateText("Count", slot.transform, string.Empty, 14, TextAnchor.LowerRight, Color.white);
        var countRect = countText.GetComponent<RectTransform>();
        Stretch(countRect);
        countRect.offsetMin = new Vector2(3f, 2f);
        countRect.offsetMax = new Vector2(-4f, -1f);
        countText.raycastTarget = false;
        slotVisuals.Add(new SlotVisual(slot.GetComponent<Image>(), itemText, countText));
    }

    private void RefreshInterface()
    {
        RefreshSlotVisuals();
        cursorText.text = cursorStack is null
            ? "CURSOR: empty"
            : $"CURSOR: {ItemDefinitions[cursorStack.ItemId].Name} x{cursorStack.Count}";
        if (hoveredSlot == -1)
        {
            tooltipText.text = "Left click: stack   Right click: half / one   Q: return cursor";
        }
    }

    private void RefreshSlotVisuals()
    {
        for (var index = 0; index < SlotCount; index++)
        {
            var slot = slots[index];
            var visual = slotVisuals[index];
            visual.Background.color = index == hoveredSlot
                ? new Color(0.29f, 0.43f, 0.54f)
                : index == cursorSourceSlot && cursorStack is not null
                    ? new Color(0.49f, 0.36f, 0.17f)
                    : slot is null ? new Color(0.18f, 0.21f, 0.24f) : new Color(0.25f, 0.29f, 0.33f);
            visual.Item.text = slot is null ? string.Empty : ItemDefinitions[slot.ItemId].Abbreviation;
            visual.Item.color = slot is null ? Color.white : ItemDefinitions[slot.ItemId].Color;
            visual.Count.text = slot is null || slot.Count == 1 ? string.Empty : slot.Count.ToString();
        }
    }

    private static GameObject CreateImage(string name, Transform parent, Color color)
    {
        var result = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        result.transform.SetParent(parent, false);
        result.GetComponent<Image>().color = color;
        return result;
    }

    private static Text CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment, Color color)
    {
        var result = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        result.transform.SetParent(parent, false);
        var text = result.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private sealed class InventoryStack
    {
        public InventoryStack(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public string ItemId { get; }
        public int Count { get; set; }
    }

    private readonly struct ItemDefinition
    {
        public ItemDefinition(string name, string abbreviation, int maxStackSize, Color color)
        {
            Name = name;
            Abbreviation = abbreviation;
            MaxStackSize = maxStackSize;
            Color = color;
        }

        public string Name { get; }
        public string Abbreviation { get; }
        public int MaxStackSize { get; }
        public Color Color { get; }
    }

    private readonly struct SlotVisual
    {
        public SlotVisual(Image background, Text item, Text count)
        {
            Background = background;
            Item = item;
            Count = count;
        }

        public Image Background { get; }
        public Text Item { get; }
        public Text Count { get; }
    }

    [Table("PlayerInventoryRecord")]
    private sealed class PlayerInventoryRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public string PlayerId { get; set; } = string.Empty;

        public int SlotIndex { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public int Count { get; set; }
        public int CursorSourceSlot { get; set; } = -1;
    }

    [Table("PlayerInventoryProfile")]
    private sealed class PlayerInventoryProfile
    {
        [PrimaryKey]
        public string PlayerId { get; set; } = string.Empty;
    }
}
