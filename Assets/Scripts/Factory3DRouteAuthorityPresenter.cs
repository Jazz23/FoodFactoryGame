// Presents initialized runtime OutsideTest authority state through the disposable 3D route proxy.
using UnityEngine;
using UnityEngine.SceneManagement;
using NotAI;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class Factory3DRouteAuthorityPresenter : MonoBehaviour
{
    private const string OutsideTestSceneName = "OutsideTest";

    private NAIStateManager authority = null!;
    private SceneGrid targetGrid = null!;
    private Factory3DOutsideTestProxyView shellView = null!;
    private Factory3DRouteProxyView routeView = null!;
    private Factory3DRouteSnapshot snapshot = null!;
    private OutsideTestFloorDebugPanel floorPanel = null!;
    private int activeFloorIndex = -1;

    public NAIStateManager Authority => authority;
    public SceneGrid TargetGrid => targetGrid;
    public Factory3DOutsideTestProxyView ShellView => shellView;
    public Factory3DRouteProxyView RouteView => routeView;
    public Factory3DRouteSnapshot Snapshot => snapshot;
    public int ActiveFloorIndex => activeFloorIndex;

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        BindFloorSelection();

        var currentAuthority = NAIStateManager.Instance;
        if (currentAuthority is null || !currentAuthority)
        {
            ClearPresentation();
            return;
        }

        if (authority is not null && !ReferenceEquals(authority, currentAuthority))
        {
            ClearPresentation();
        }

        if (!currentAuthority.IsInitialized)
        {
            ClearPresentation();
            return;
        }

        if (!TryGetOutsideTestGrid(out var grid))
        {
            ClearPresentation();
            return;
        }

        authority = currentAuthority;
        targetGrid = grid;
        shellView = Factory3DOutsideTestProxyView.FindOrCreate(grid.gameObject.scene);
        routeView = shellView.GetOrCreateRouteProxyView();
        activeFloorIndex = ResolveActiveFloorIndex();
        snapshot = authority.BuildRoutePresentationSnapshot(grid);
        routeView.Rebuild(snapshot, activeFloorIndex);
    }

    private void OnDestroy()
    {
        ClearPresentation();
    }

    private void OnDisable()
    {
        UnbindFloorSelection();
        ClearPresentation();
    }

    private void ClearPresentation()
    {
        if (routeView is not null && routeView)
        {
            routeView.ClearAuthority();
        }
        else
        {
            var outsideTestScene = SceneManager.GetSceneByName(OutsideTestSceneName);
            var existingShell = Factory3DOutsideTestProxyView.FindExisting(outsideTestScene);
            var existingRouteView = existingShell is not null && existingShell
                ? existingShell.FindRouteProxyView()
                : null;
            if (existingRouteView is not null && existingRouteView)
            {
                existingRouteView.ClearAuthority();
            }
        }

        authority = null!;
        targetGrid = null!;
        shellView = null!;
        routeView = null!;
        snapshot = null!;
        activeFloorIndex = -1;
    }

    private void BindFloorSelection()
    {
        var nextPanel = FindFirstObjectByType<OutsideTestFloorDebugPanel>(FindObjectsInactive.Include);
        if (nextPanel == floorPanel)
        {
            return;
        }

        UnbindFloorSelection();
        floorPanel = nextPanel;
        if (floorPanel is not null && floorPanel)
        {
            floorPanel.FloorSelected += FloorSelected;
        }
    }

    private void UnbindFloorSelection()
    {
        if (floorPanel is not null && floorPanel)
        {
            floorPanel.FloorSelected -= FloorSelected;
        }

        floorPanel = null!;
    }

    private void FloorSelected(uint _, int floorIndex)
    {
        activeFloorIndex = floorIndex;
        if (routeView is not null && routeView)
        {
            routeView.SetActiveFloor(activeFloorIndex);
        }
    }

    private int ResolveActiveFloorIndex()
    {
        var player = PlayerSceneTransition.LocalOwner;
        if (player is not null
            && player
            && player.TryGetCurrentOutsideTestFloor(out _, out var currentFloorIndex))
        {
            return currentFloorIndex;
        }

        var panel = FindFirstObjectByType<OutsideTestFloorDebugPanel>(FindObjectsInactive.Include);
        return panel is not null
            && panel
            && panel.HasExplicitFloorSelection
            && panel.TryGetSelectedFloor(out _, out var selectedFloorIndex)
            ? selectedFloorIndex
            : -1;
    }

    private static bool TryGetOutsideTestGrid(out SceneGrid grid)
    {
        var outsideTestScene = SceneManager.GetSceneByName(OutsideTestSceneName);
        if (outsideTestScene.IsValid()
            && outsideTestScene.isLoaded
            && SceneGrid.TryGetForScene(outsideTestScene, out grid)
            && grid is not null
            && grid)
        {
            return true;
        }

        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var candidateScene = SceneManager.GetSceneAt(sceneIndex);
            if (!candidateScene.IsValid()
                || !candidateScene.isLoaded
                || !HasFactoryBuildingCreator(candidateScene)
                || !SceneGrid.TryGetForScene(candidateScene, out grid)
                || grid is null
                || !grid)
            {
                continue;
            }

            return true;
        }

        grid = null!;
        return false;
    }

    private static bool HasFactoryBuildingCreator(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<TestBuildingCreator>(true) is not null)
            {
                return true;
            }
        }

        return false;
    }
}
