// Owns F2 visibility for developer tools while leaving the shared launcher and build bar available.
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class TestUIVisibility : MonoBehaviour
{
    public static bool Visible { get; private set; } = true;
    public static event Action<bool> VisibilityChanged = delegate { };

    private InputAction toggle = null!;

    public static Rect ButtonRect => new(Screen.width - 164f, 8f, 148f, 44f);

    public static void SetVisible(bool visible)
    {
        if (Visible == visible)
        {
            return;
        }

        Visible = visible;
        VisibilityChanged(visible);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Visible = true;
        var controller = new GameObject("Test UI Visibility");
        DontDestroyOnLoad(controller);
        controller.AddComponent<TestUIVisibility>();
        controller.AddComponent<TestToolsShell>();
    }

    private void Start()
    {
        toggle = InputSystem.actions.FindAction("FactoryBuild/ToggleTestUI", true).Clone();
        toggle.Enable();
    }

    private void Update()
    {
        if (toggle.WasPressedThisFrame() && !TestToolsShell.IsTextInputFocused)
        {
            SetVisible(!Visible);
        }
    }

    private void OnDestroy()
    {
        if (toggle is not null) toggle.Dispose();
    }
}
