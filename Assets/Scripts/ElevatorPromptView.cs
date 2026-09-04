// Displays the local floor-selection prompt for an insidefactory elevator.
using UnityEngine;
using UnityEngine.UI;

public sealed class ElevatorPromptView : MonoBehaviour
{
    private Text promptText = null!;
    private bool initialized;

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        var panelObject = new GameObject(
            "Elevator Prompt Panel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        panelObject.transform.SetParent(transform, false);
        var panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(520f, 190f);
        panel.anchoredPosition = new Vector2(0f, -190f);
        panelObject.GetComponent<Image>().color = new Color(0.035f, 0.055f, 0.075f, 0.94f);

        var textObject = new GameObject(
            "Elevator Prompt Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        textObject.transform.SetParent(panelObject.transform, false);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 18f);
        textRect.offsetMax = new Vector2(-24f, -18f);
        promptText = textObject.GetComponent<Text>();
        promptText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        promptText.fontSize = 22;
        promptText.alignment = TextAnchor.MiddleCenter;
        promptText.color = new Color(0.9f, 0.95f, 0.98f, 1f);
        promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
        promptText.verticalOverflow = VerticalWrapMode.Overflow;
        promptText.raycastTarget = false;
    }

    public void SetFloorOptions(int currentFloor, int storyCount)
    {
        Initialize();
        var lines = $"ELEVATOR\nCURRENT: {GetFloorLabel(currentFloor)}";
        if (currentFloor < storyCount - 1)
        {
            lines += $"\nW  Go to {GetFloorLabel(currentFloor + 1)}";
        }

        if (currentFloor > 0)
        {
            lines += $"\nS  Go to {GetFloorLabel(currentFloor - 1)}";
        }

        if (storyCount <= 1)
        {
            lines += "\nNo other floors available";
        }

        lines += "\nE / ESC  Close";
        promptText.text = lines;
    }

    public void Show()
    {
        Initialize();
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Awake()
    {
        Initialize();
        gameObject.SetActive(false);
    }

    private static string GetFloorLabel(int floorIndex)
    {
        return floorIndex == 0 ? "GROUND FLOOR" : $"FLOOR {floorIndex}";
    }
}
