// Toggles test overlays together while leaving gameplay and the factory build toolbar available.
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class TestUIVisibility : MonoBehaviour
{
    public static bool Visible { get; private set; } = true;
    private InputAction toggle = null!;

    public static Rect ButtonRect => new(Screen.width - 164f, 8f, 152f, 26f);

    public static void SetVisible(bool visible) => Visible = visible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Visible = true;
        var controller = new GameObject("Test UI Visibility");
        DontDestroyOnLoad(controller);
        controller.AddComponent<TestUIVisibility>();
    }

    private void Start()
    {
        toggle = InputSystem.actions.FindAction("FactoryBuild/ToggleTestUI", true).Clone();
        toggle.Enable();
    }

    private void Update()
    {
        if (toggle.WasPressedThisFrame()) Visible = !Visible;
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (canvas.name == "NetworkHudCanvas")
            {
                canvas.enabled = Visible;
                foreach (var behaviour in canvas.GetComponents<MonoBehaviour>())
                    if (behaviour.GetType().Name == "NetworkHudCanvases") behaviour.enabled = Visible;
            }
    }

    private void OnGUI()
    {
        if (GUI.Button(ButtonRect, Visible ? "F2: Hide test UIs" : "F2: Show test UIs"))
            Visible = !Visible;
    }

    private void OnDestroy()
    {
        if (toggle is not null) toggle.Dispose();
    }
}
