// Owns the reversible runtime gate between authoritative factory state and disposable 3D presentation.
using System.Collections.Generic;
using NotAI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

[DefaultExecutionOrder(1100)]
[DisallowMultipleComponent]
public sealed class Factory3DPresentationBridge : MonoBehaviour
{
    private const string OutsideTestSceneName = "OutsideTest";
    private const string PlayerVisualName = "Factory 3D Player Visual";
    private const float DefaultPlayerHeight = 1.8f;
    private const float DefaultPlayerRadius = 0.3f;
    public const float DimetricCameraYawDegrees = 45f;
    public const float DimetricCameraElevationDegrees = 30f;
    public const float DimetricCameraGroundScale = 0.70710677f;

    private readonly Dictionary<Component, bool> capturedLegacyComponents = new();
    private readonly Dictionary<Factory3DTraversalController, GameObject> playerVisuals = new();
    private Factory3DOutsideTestProxyView outsideShell = null!;
    private NAIStateManager authority = null!;
    private SceneGrid targetGrid = null!;
    private Scene targetScene;
    private Factory3DOutsideTestProxyBuildResult lastShellResult;
    private Camera configuredCamera = null!;
    private CameraFollow configuredCameraFollow = null!;
    private bool cameraFollowWasEnabled;
    private bool cameraCaptured;
    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    private bool cameraOrthographic;
    private float cameraOrthographicSize;
    private float cameraNearClipPlane;
    private float cameraFarClipPlane;
    private Material playerMaterial = null!;
    private Factory3DTraversalController screenAlignedTraversal = null!;

    public bool Active { get; private set; }
    public string TargetSceneName => targetScene.IsValid() ? targetScene.name : string.Empty;
    public SceneGrid TargetGrid => targetGrid;
    public Factory3DOutsideTestProxyBuildResult LastShellResult => lastShellResult;
    public int LegacyComponentCount
    {
        get
        {
            PruneDestroyedState();
            return capturedLegacyComponents.Count;
        }
    }

    public int PlayerVisualCount
    {
        get
        {
            PruneDestroyedState();
            return playerVisuals.Count;
        }
    }
    public bool CameraConfigured => configuredCamera is not null && configuredCamera;

    public static Quaternion GetDimetricCameraRotation()
    {
        var yaw = DimetricCameraYawDegrees * Mathf.Deg2Rad;
        var elevation = DimetricCameraElevationDegrees * Mathf.Deg2Rad;
        var horizontal = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        var forward = horizontal * Mathf.Cos(elevation);
        forward.y = -Mathf.Sin(elevation);
        return Quaternion.LookRotation(forward, Vector3.up);
    }

    public static float GetDimetricOrthographicSize(
        float gridOrthographicSize,
        float cellSize)
    {
        return Mathf.Max(
            0.1f,
            gridOrthographicSize * Mathf.Max(0.01f, cellSize) * DimetricCameraGroundScale);
    }

    public bool ReconcileOutsideTest(
        NAIStateManager runtimeAuthority,
        SceneGrid grid,
        float wallHeight,
        float doorCornerExclusionDistance)
    {
        if (runtimeAuthority is null
            || !runtimeAuthority
            || !runtimeAuthority.IsInitialized
            || grid is null
            || !grid
            || !grid.gameObject.scene.IsValid()
            || !grid.gameObject.scene.isLoaded
            || grid.gameObject.scene.name != OutsideTestSceneName)
        {
            ClearPresentation();
            return false;
        }

        authority = runtimeAuthority;
        outsideShell = Factory3DOutsideTestProxyView.FindOrCreate(grid.gameObject.scene);
        lastShellResult = outsideShell.Rebuild(
            grid,
            runtimeAuthority.BuildingRecords,
            runtimeAuthority.FloorStates,
            wallHeight,
            doorCornerExclusionDistance,
            GetOutsideMaterial());
        outsideShell.SetExteriorOcclusionSurfacesVisible(false);
        return outsideShell is not null && outsideShell;
    }

    public bool ActivatePresentation(Scene scene, SceneGrid grid)
    {
        if (!scene.IsValid()
            || !scene.isLoaded
            || grid is null
            || !grid
            || grid.gameObject.scene != scene)
        {
            return false;
        }

        if (targetScene != scene || targetGrid != grid)
        {
            RestoreCamera();
        }

        targetScene = scene;
        targetGrid = grid;
        Active = true;
        CaptureLegacyForScene(scene);
        EnsurePlayerVisualsForScene(scene);
        EnableScreenAlignedMovementForLocalOwner();
        ConfigureCamera();
        return true;
    }

    public void CaptureLegacyForScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        foreach (var renderer in FindObjectsByType<SpriteRenderer>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            CaptureAndDisable(renderer, scene);
        }

        foreach (var renderer in FindObjectsByType<TilemapRenderer>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            CaptureAndDisable(renderer, scene);
        }

        foreach (var collider in FindObjectsByType<Collider2D>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            CaptureAndDisable(collider, scene);
        }

        foreach (var body in FindObjectsByType<Rigidbody2D>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            CaptureAndDisable(body, scene);
        }
    }

    public void ClearPresentation()
    {
        ClearScreenAlignedMovement();
        ClearOutsideShell();
        RestoreLegacyPresentation();
        ClearPlayerVisuals();
        RestoreCamera();
        authority = null!;
        targetGrid = null!;
        targetScene = default;
        Active = false;
    }

    private void EnableScreenAlignedMovementForLocalOwner()
    {
        var localOwner = PlayerSceneTransition.LocalOwner;
        if (localOwner is null
            || !localOwner
            || !localOwner.TryGetComponent<Factory3DTraversalController>(out var traversal))
        {
            ClearScreenAlignedMovement();
            return;
        }

        if (screenAlignedTraversal is not null
            && screenAlignedTraversal
            && screenAlignedTraversal != traversal)
        {
            screenAlignedTraversal.SetScreenAlignedMovement(false);
        }

        traversal.SetScreenAlignedMovement(true);
        screenAlignedTraversal = traversal;
    }

    private void ClearScreenAlignedMovement()
    {
        if (screenAlignedTraversal is not null && screenAlignedTraversal)
        {
            screenAlignedTraversal.SetScreenAlignedMovement(false);
        }

        screenAlignedTraversal = null!;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        var runtimeAuthority = NAIStateManager.Instance;
        var outsideGrid = TryGetOutsideGrid(out var resolvedOutsideGrid)
            ? resolvedOutsideGrid
            : null!;
        var outsideReady = ReconcileOutsideTest(
            runtimeAuthority,
            outsideGrid,
            GetOutsideWallHeight(),
            GetOutsideDoorDistance());
        if (!outsideReady)
        {
            ClearPresentation();
            return;
        }

        ReassertLocal3DOwnership();
        CaptureLegacyForScene(outsideGrid.gameObject.scene);
        EnsurePlayerVisualsForScene(outsideGrid.gameObject.scene);
        var scene = ResolveTargetScene(outsideGrid, out var grid);
        if (!ActivatePresentation(scene, grid))
        {
            ClearPresentation();
        }
    }

    private static void ReassertLocal3DOwnership()
    {
        var localOwner = PlayerSceneTransition.LocalOwner;
        if (localOwner is null
            || !localOwner
            || !localOwner.IsOwner
            || !localOwner.TryGetComponent<Factory3DTraversalController>(out var traversal))
        {
            return;
        }

        if (!traversal.OwnsPlayer && localOwner.EnableProduction3DTraversal())
        {
            return;
        }

        traversal.Set3DOwnership(true);
    }

    private Scene ResolveTargetScene(SceneGrid outsideGrid, out SceneGrid grid)
    {
        var localOwner = PlayerSceneTransition.LocalOwner;
        if (localOwner is not null
            && localOwner
            && localOwner.gameObject.scene.IsValid()
            && localOwner.gameObject.scene != outsideGrid.gameObject.scene
            && SceneGrid.TryGetForScene(localOwner.gameObject.scene, out var interiorGrid)
            && HasReadyInteriorPresentation(localOwner.gameObject.scene))
        {
            grid = interiorGrid;
            return localOwner.gameObject.scene;
        }

        grid = outsideGrid;
        return outsideGrid.gameObject.scene;
    }

    private bool HasReadyInteriorPresentation(Scene scene)
    {
        foreach (var presenter in FindObjectsByType<Factory3DInteriorPresenter>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (presenter is not null
                && presenter
                && presenter.gameObject.scene == scene
                && presenter.Authority == authority
                && presenter.TargetGrid is not null
                && presenter.TargetGrid
                && presenter.ProxyCount > 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetOutsideGrid(out SceneGrid grid)
    {
        var scene = SceneManager.GetSceneByName(OutsideTestSceneName);
        if (scene.IsValid()
            && scene.isLoaded
            && SceneGrid.TryGetForScene(scene, out grid)
            && grid is not null
            && grid)
        {
            return true;
        }

        grid = null!;
        return false;
    }

    private void EnsurePlayerVisualsForScene(Scene scene)
    {
        foreach (var traversal in FindObjectsByType<Factory3DTraversalController>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (traversal is null
                || !traversal
                || traversal.gameObject.scene != scene)
            {
                continue;
            }

            if (playerVisuals.TryGetValue(traversal, out var existing)
                && existing is not null
                && existing)
            {
                continue;
            }

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = PlayerVisualName;
            visual.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            SceneManager.MoveGameObjectToScene(visual, scene);
            visual.transform.SetParent(traversal.transform, false);
            var collider = visual.GetComponent<Collider>();
            if (collider is not null && collider)
            {
                collider.enabled = false;
            }

            var capsule = traversal.GetComponentInChildren<CapsuleCollider>(true);
            var height = capsule is not null && capsule
                ? Mathf.Max(DefaultPlayerHeight, capsule.height)
                : DefaultPlayerHeight;
            var radius = capsule is not null && capsule
                ? Mathf.Max(DefaultPlayerRadius, capsule.radius)
                : DefaultPlayerRadius;
            visual.transform.localPosition = Vector3.up * (height * 0.5f);
            visual.transform.localScale = new Vector3(
                radius * 2f,
                height * 0.5f,
                radius * 2f);
            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer is not null && renderer)
            {
                renderer.sharedMaterial = GetPlayerMaterial();
            }

            playerVisuals[traversal] = visual;
        }
    }

    private Material GetPlayerMaterial()
    {
        if (playerMaterial is not null && playerMaterial)
        {
            return playerMaterial;
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader is null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader is null)
        {
            return null!;
        }

        playerMaterial = new Material(shader)
        {
            name = "Factory 3D Player Material",
            color = new Color(0.2f, 0.9f, 0.45f, 1f),
            hideFlags = HideFlags.DontSave
        };
        return playerMaterial;
    }

    private void ConfigureCamera()
    {
        var camera = FindGameplayCamera(targetScene);
        if (camera is null || !camera)
        {
            return;
        }

        if (configuredCamera != camera)
        {
            RestoreCamera();
            configuredCamera = camera;
            cameraPosition = camera.transform.position;
            cameraRotation = camera.transform.rotation;
            cameraOrthographic = camera.orthographic;
            cameraOrthographicSize = camera.orthographicSize;
            cameraNearClipPlane = camera.nearClipPlane;
            cameraFarClipPlane = camera.farClipPlane;
            configuredCameraFollow = camera.GetComponent<CameraFollow>();
            cameraFollowWasEnabled = configuredCameraFollow is not null
                && configuredCameraFollow.enabled;
            if (configuredCameraFollow is not null && configuredCameraFollow)
            {
                configuredCameraFollow.enabled = false;
            }

            camera.orthographic = true;
            camera.orthographicSize = GetDimetricOrthographicSize(
                targetGrid.OrthographicSize,
                targetGrid.CellSize);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = Mathf.Max(100f, camera.orthographicSize * 20f);
            cameraCaptured = true;
        }

        var focus = ResolveCameraFocus();
        camera.transform.rotation = GetDimetricCameraRotation();
        var cameraDistance = Mathf.Max(10f, camera.orthographicSize * 2f);
        camera.transform.position = focus - camera.transform.forward * cameraDistance;
    }

    private static Camera FindGameplayCamera(Scene scene)
    {
        Camera sceneMainCamera = null!;
        Camera loadedMainCamera = null!;
        Camera sceneCamera = null!;
        Camera loadedCamera = null!;
        foreach (var camera in FindObjectsByType<Camera>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (camera is null
                || !camera
                || !camera.gameObject.activeInHierarchy
                || !camera.enabled)
            {
                continue;
            }

            var isSceneCamera = camera.gameObject.scene == scene;
            if (camera.CompareTag("MainCamera"))
            {
                if (isSceneCamera && (sceneMainCamera is null || !sceneMainCamera))
                {
                    sceneMainCamera = camera;
                }
                else if (loadedMainCamera is null || !loadedMainCamera)
                {
                    loadedMainCamera = camera;
                }
            }
            else if (isSceneCamera && (sceneCamera is null || !sceneCamera))
            {
                sceneCamera = camera;
            }
            else if (loadedCamera is null || !loadedCamera)
            {
                loadedCamera = camera;
            }
        }

        return sceneMainCamera is not null && sceneMainCamera
            ? sceneMainCamera
            : loadedMainCamera is not null && loadedMainCamera
                ? loadedMainCamera
                : sceneCamera is not null && sceneCamera
                    ? sceneCamera
                    : loadedCamera;
    }

    private Vector3 ResolveCameraFocus()
    {
        var localOwner = PlayerSceneTransition.LocalOwner;
        if (localOwner is not null
            && localOwner
            && localOwner.gameObject.scene == targetScene
            && localOwner.TryGetComponent<Factory3DTraversalController>(out var traversal))
        {
            return traversal.FootAnchor + Vector3.up * (targetGrid.CellSize * 0.5f);
        }

        var logical = targetGrid.InitialPlayerLogicalPosition;
        var world = targetGrid.CreateSpatialAdapter().LogicalToWorld3D(
            new FactoryLogicalLocation(0u, 0, logical),
            0f);
        return world + Vector3.up * (targetGrid.CellSize * 0.5f);
    }

    private void RestoreCamera()
    {
        if (configuredCamera is not null && configuredCamera && cameraCaptured)
        {
            configuredCamera.transform.SetPositionAndRotation(
                cameraPosition,
                cameraRotation);
            configuredCamera.orthographic = cameraOrthographic;
            configuredCamera.orthographicSize = cameraOrthographicSize;
            configuredCamera.nearClipPlane = cameraNearClipPlane;
            configuredCamera.farClipPlane = cameraFarClipPlane;
        }

        if (configuredCameraFollow is not null && configuredCameraFollow)
        {
            configuredCameraFollow.enabled = cameraFollowWasEnabled;
        }

        configuredCamera = null!;
        configuredCameraFollow = null!;
        cameraCaptured = false;
    }

    private void CaptureAndDisable(Component component, Scene scene)
    {
        if (component is null
            || !component
            || component.gameObject.scene != scene
            || IsScreenSpaceUi(component))
        {
            return;
        }

        if (IsTraversalOwnedPhysics(component))
        {
            capturedLegacyComponents.Remove(component);
            return;
        }

        if (!capturedLegacyComponents.ContainsKey(component))
        {
            var wasEnabled = component is Rigidbody2D body
                ? body.simulated
                : component is Renderer renderer
                    ? renderer.enabled
                : component is Behaviour behaviour
                    ? behaviour.enabled
                    : true;
            capturedLegacyComponents.Add(component, wasEnabled);
        }

        if (component is Rigidbody2D rigidbody)
        {
            rigidbody.simulated = false;
        }
        else if (component is Renderer renderer)
        {
            renderer.enabled = false;
        }
        else if (component is Behaviour behaviour)
        {
            behaviour.enabled = false;
        }
    }

    private static bool IsTraversalOwnedPhysics(Component component)
    {
        if (component is not Rigidbody2D && component is not Collider2D)
        {
            return false;
        }

        var traversal = component.GetComponentInParent<Factory3DTraversalController>(true);
        return traversal is not null && traversal;
    }

    private void RestoreLegacyPresentation()
    {
        foreach (var pair in capturedLegacyComponents)
        {
            if (pair.Key is null || !pair.Key)
            {
                continue;
            }

            if (pair.Key is Rigidbody2D rigidbody)
            {
                rigidbody.simulated = pair.Value;
            }
            else if (pair.Key is Renderer renderer)
            {
                renderer.enabled = pair.Value;
            }
            else if (pair.Key is Behaviour behaviour)
            {
                behaviour.enabled = pair.Value;
            }
        }

        capturedLegacyComponents.Clear();
    }

    private void ClearPlayerVisuals()
    {
        foreach (var visual in playerVisuals.Values)
        {
            DestroyGeneratedObject(visual);
        }

        playerVisuals.Clear();
        DestroyGeneratedObject(playerMaterial);
        playerMaterial = null!;
    }

    private void PruneDestroyedState()
    {
        var staleLegacy = new List<Component>();
        foreach (var component in capturedLegacyComponents.Keys)
        {
            if (component is null || !component)
            {
                staleLegacy.Add(component);
            }
        }

        foreach (var component in staleLegacy)
        {
            capturedLegacyComponents.Remove(component);
        }

        var stalePlayers = new List<Factory3DTraversalController>();
        foreach (var pair in playerVisuals)
        {
            if (pair.Key is null
                || !pair.Key
                || pair.Value is null
                || !pair.Value)
            {
                stalePlayers.Add(pair.Key);
            }
        }

        foreach (var traversal in stalePlayers)
        {
            playerVisuals.Remove(traversal);
        }
    }

    private void ClearOutsideShell()
    {
        if (outsideShell is not null && outsideShell)
        {
            outsideShell.SetExteriorOcclusionSurfacesVisible(true);
            outsideShell.Clear();
        }
        else
        {
            outsideShell = null!;
        }

        lastShellResult = default;
    }

    private float GetOutsideWallHeight()
    {
        var creator = FindOutsideCreator();
        return creator is null || !creator ? 3f : creator.WallHeight;
    }

    private float GetOutsideDoorDistance()
    {
        var creator = FindOutsideCreator();
        return creator is null || !creator
            ? TestBuildingCreator.DefaultDoorCornerExclusionDistance
            : creator.DoorCornerExclusionDistance;
    }

    private static Material GetOutsideMaterial()
    {
        var creator = FindOutsideCreator();
        return creator is null || !creator ? null! : creator.Material;
    }

    private static TestBuildingCreator FindOutsideCreator()
    {
        foreach (var creator in FindObjectsByType<TestBuildingCreator>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (creator is not null
                && creator
                && creator.gameObject.scene.name == OutsideTestSceneName)
            {
                return creator;
            }
        }

        return null!;
    }

    private static bool IsScreenSpaceUi(Component component)
    {
        var canvas = component.GetComponentInParent<Canvas>(true);
        return canvas is not null
            && canvas
            && canvas.renderMode != RenderMode.WorldSpace;
    }

    private static void DestroyGeneratedObject(Object target)
    {
        if (target is null || !target)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void OnDisable()
    {
        ClearPresentation();
    }

    private void OnDestroy()
    {
        ClearPresentation();
    }
}
