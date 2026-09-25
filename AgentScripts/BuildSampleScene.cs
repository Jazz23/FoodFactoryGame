// Authors the PROTOTYPE scriptable employee (clips, animator, outline material, prefab) and rebuilds SampleScene as a copy
// of DevSite plus a storage shelf marking the dev storage, a baked NavMesh and the employee script panel. Employees are not
// placed in the scene: SessionRoot.employeePrefab makes the server seed the dev employee in the save and spawn one worker
// per saved record, so SampleScene uses its own spawnable-prefab catalog (DevSite's two prefabs plus the employee).
// Idempotent; keeps asset GUIDs. DevSite and its catalog are untouched; DevSite is reopened at the end.
// Run the body of Run() with the Unity MCP execute_code tool (C# 6 / CodeDom compatible, no helper methods).
// The employee model is exported from ArtSource/Employee/Employee_Asset.blend (Employee_Walk, Employee_Idle,
// Employee_CarryWalk, Employee_CarryIdle actions; the Employee_CarryBox and Employee_CarryBoxTape parts ride the spine bone).
public static class BuildSampleScene
{
    public static object Run()
    {
        const string modelPath = "Assets/Art/Models/Employee/Employee.fbx";
        const string folder = "Assets/Prefabs/Employee";
        const string controllerPath = folder + "/Employee.controller";
        const string prefabPath = folder + "/Employee.prefab";
        const string devSitePath = "Assets/Scenes/DevSite.unity";
        const string scenePath = "Assets/Scenes/SampleScene.unity";
        const string navMeshFolder = "Assets/Scenes/SampleScene";
        const string navMeshPath = navMeshFolder + "/NavMesh-Navigation.asset";
        const string materialFolder = "Assets/Materials/Employee";
        const string outlinePath = materialFolder + "/EmployeeOutline.mat";
        const string shelfMaterialPath = materialFolder + "/StorageShelf.mat";
        const string crateMaterialPath = materialFolder + "/StorageCrate.mat";
        const string devCatalogPath = "Assets/Network/GamePrefabs.asset";
        const string catalogPath = "Assets/Network/SampleScenePrefabs.asset";

        // Clips: loop all, named after the Blender action without its prefix (Walk, Idle, CarryWalk, CarryIdle).
        var importer = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(modelPath);
        importer.animationType = UnityEditor.ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        var clipSettings = importer.defaultClipAnimations;
        foreach (var clip in clipSettings)
        {
            clip.loopTime = true;
            clip.name = clip.takeName.Substring(clip.takeName.LastIndexOf('_') + 1);
        }
        importer.clipAnimations = clipSettings;
        importer.SaveAndReimport();
        var clips = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().ToArray();
        var walk = clips.First(x => x.name == "Walk");
        var idle = clips.First(x => x.name == "Idle");
        var carryWalk = clips.First(x => x.name == "CarryWalk");
        var carryIdle = clips.First(x => x.name == "CarryIdle");

        if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Prefabs", "Employee");
        if (!UnityEditor.AssetDatabase.IsValidFolder(materialFolder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Materials", "Employee");

        // Animator: Idle <-> Walk on observed Speed, and the same pair holding a box while Carrying; walks play at WalkRate
        // so the feet track ground speed.
        var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
        if (controller == null) controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        foreach (var parameter in controller.parameters.ToArray()) controller.RemoveParameter(parameter);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("WalkRate", AnimatorControllerParameterType.Float);
        controller.AddParameter("Carrying", AnimatorControllerParameterType.Bool);
        var machine = controller.layers[0].stateMachine;
        foreach (var child in machine.states.ToArray()) machine.RemoveState(child.state);
        var idleState = machine.AddState("Idle");
        idleState.motion = idle;
        var walkState = machine.AddState("Walk");
        walkState.motion = walk;
        walkState.speedParameterActive = true;
        walkState.speedParameter = "WalkRate";
        var carryIdleState = machine.AddState("CarryIdle");
        carryIdleState.motion = carryIdle;
        var carryWalkState = machine.AddState("CarryWalk");
        carryWalkState.motion = carryWalk;
        carryWalkState.speedParameterActive = true;
        carryWalkState.speedParameter = "WalkRate";
        machine.defaultState = idleState;
        var pairs = new[] { new[] { idleState, walkState }, new[] { carryIdleState, carryWalkState } };
        for (var index = 0; index < 2; index++)
        {
            var still = pairs[index][0];
            var moving = pairs[index][1];
            var start = still.AddTransition(moving);
            start.hasExitTime = false;
            start.duration = 0.15f;
            start.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 0.2f, "Speed");
            var stop = moving.AddTransition(still);
            stop.hasExitTime = false;
            stop.duration = 0.2f;
            stop.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 0.1f, "Speed");
            // Picking up or putting down swaps to the matching state of the other pair.
            for (var state = 0; state < 2; state++)
            {
                var swap = pairs[index][state].AddTransition(pairs[1 - index][state]);
                swap.hasExitTime = false;
                swap.duration = 0.2f;
                swap.AddCondition(index == 1 ? UnityEditor.Animations.AnimatorConditionMode.IfNot : UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Carrying");
            }
        }
        UnityEditor.EditorUtility.SetDirty(controller);

        // Hover outline material (FoodFactory/Outline, a soft green).
        var outlineShader = Shader.Find("FoodFactory/Outline");
        var outline = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(outlinePath);
        if (outline == null)
        {
            outline = new Material(outlineShader);
            UnityEditor.AssetDatabase.CreateAsset(outline, outlinePath);
        }
        outline.shader = outlineShader;
        outline.SetColor("_Color", new Color(0.35f, 1f, 0.45f, 1f));
        outline.SetFloat("_Width", 0.018f);
        UnityEditor.EditorUtility.SetDirty(outline);

        // Prefab: networked root with the agent, worker, outline and a click collider; the model child carries the Animator
        // (clip paths are relative to it).
        var root = new GameObject("Employee");
        root.AddComponent<FishNet.Object.NetworkObject>();
        root.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
        var agent = root.AddComponent<UnityEngine.AI.NavMeshAgent>();
        agent.speed = 1.5f;
        agent.angularSpeed = 360f;
        agent.acceleration = 6f;
        agent.radius = 0.35f;
        agent.height = 1.85f;
        agent.stoppingDistance = 0.05f;
        var model = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        var animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // A trigger capsule so the crosshair can hover and click the employee without it blocking anyone's walk.
        var click = root.AddComponent<CapsuleCollider>();
        click.isTrigger = true;
        click.radius = 0.4f;
        click.height = 1.85f;
        click.center = new Vector3(0f, 0.925f, 0f);
        var box = model.GetComponentsInChildren<Renderer>(true).Where(x => x.name.StartsWith("Employee_CarryBox")).ToArray();
        var worker = root.AddComponent<FoodFactoryGame.Session.Employees.EmployeeWorker>();
        using (var serialized = new UnityEditor.SerializedObject(worker))
        {
            serialized.FindProperty("agent").objectReferenceValue = agent;
            serialized.FindProperty("animator").objectReferenceValue = animator;
            var boxProperty = serialized.FindProperty("carriedBox");
            boxProperty.arraySize = box.Length;
            for (var index = 0; index < box.Length; index++) boxProperty.GetArrayElementAtIndex(index).objectReferenceValue = box[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        var outliner = root.AddComponent<FoodFactoryGame.Session.Employees.EmployeeOutline>();
        using (var serialized = new UnityEditor.SerializedObject(outliner))
        {
            serialized.FindProperty("outlineMaterial").objectReferenceValue = outline;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        // Scene: DevSite's session setup, saved over SampleScene (same GUID), then the shelf, panel, NavMesh and employee.
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(devSitePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
        scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

        // PROTOTYPE storage shelf: where employees walk to reach the dev storage location, placed before the NavMesh bake so
        // paths go around it. A plain rack with a few crates; the root collider is its footprint.
        var shelfMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(shelfMaterialPath);
        if (shelfMaterial == null)
        {
            shelfMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            UnityEditor.AssetDatabase.CreateAsset(shelfMaterial, shelfMaterialPath);
        }
        shelfMaterial.SetColor("_BaseColor", new Color(0.33f, 0.35f, 0.38f));
        UnityEditor.EditorUtility.SetDirty(shelfMaterial);
        var crateMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(crateMaterialPath);
        if (crateMaterial == null)
        {
            crateMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            UnityEditor.AssetDatabase.CreateAsset(crateMaterial, crateMaterialPath);
        }
        crateMaterial.SetColor("_BaseColor", new Color(0.62f, 0.43f, 0.22f));
        UnityEditor.EditorUtility.SetDirty(crateMaterial);
        var shelf = new GameObject("StorageShelf");
        shelf.transform.position = new Vector3(-7f, 0f, 7f);
        var shelfCollider = shelf.AddComponent<BoxCollider>();
        shelfCollider.size = new Vector3(2f, 1.6f, 0.8f);
        shelfCollider.center = new Vector3(0f, 0.8f, 0f);
        var marker = shelf.AddComponent<FoodFactoryGame.Session.Employees.SiteLocationMarker>();
        using (var serialized = new UnityEditor.SerializedObject(marker))
        {
            serialized.FindProperty("size").vector2Value = new Vector2(2f, 0.8f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        // x, y, z, width, height, depth, crate (1) or frame (0)
        var parts = new[]
        {
            new[] { 0f, 0.05f, 0f, 2f, 0.1f, 0.8f, 0f }, new[] { 0f, 0.8f, 0f, 2f, 0.06f, 0.8f, 0f }, new[] { 0f, 1.55f, 0f, 2f, 0.06f, 0.8f, 0f },
            new[] { -0.97f, 0.8f, 0f, 0.06f, 1.6f, 0.8f, 0f }, new[] { 0.97f, 0.8f, 0f, 0.06f, 1.6f, 0.8f, 0f },
            new[] { -0.5f, 0.35f, 0f, 0.7f, 0.5f, 0.6f, 1f }, new[] { 0.45f, 0.33f, 0f, 0.6f, 0.46f, 0.6f, 1f }, new[] { -0.2f, 1.08f, 0f, 0.8f, 0.5f, 0.6f, 1f }
        };
        foreach (var part in parts)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = part[6] > 0f ? "Crate" : "Frame";
            UnityEngine.Object.DestroyImmediate(piece.GetComponent<BoxCollider>());
            piece.transform.SetParent(shelf.transform, false);
            piece.transform.localPosition = new Vector3(part[0], part[1], part[2]);
            piece.transform.localScale = new Vector3(part[3], part[4], part[5]);
            piece.GetComponent<Renderer>().sharedMaterial = part[6] > 0f ? crateMaterial : shelfMaterial;
        }

        // The employee script screen: its own UI document on the HUD's panel settings, drawn above the HUD.
        var hud = UnityEngine.Object.FindAnyObjectByType<FoodFactoryGame.Session.Equipment.PlayerHud>();
        var interaction = UnityEngine.Object.FindAnyObjectByType<FoodFactoryGame.Session.Equipment.EquipmentInteraction>();
        var hudDocument = hud.GetComponent<UnityEngine.UIElements.UIDocument>();
        var panelObject = new GameObject("EmployeeScriptPanel");
        var panelDocument = panelObject.AddComponent<UnityEngine.UIElements.UIDocument>();
        panelDocument.panelSettings = hudDocument.panelSettings;
        panelDocument.sortingOrder = hudDocument.sortingOrder + 1;
        var panel = panelObject.AddComponent<FoodFactoryGame.Session.Employees.EmployeeScriptPanel>();
        using (var serialized = new UnityEditor.SerializedObject(panel))
        {
            serialized.FindProperty("document").objectReferenceValue = panelDocument;
            serialized.FindProperty("interaction").objectReferenceValue = interaction;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        var navigation = new GameObject("Navigation");
        var surface = navigation.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
        surface.collectObjects = Unity.AI.Navigation.CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
        if (!UnityEditor.AssetDatabase.IsValidFolder(navMeshFolder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Scenes", "SampleScene");
        var existingNavMesh = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navMeshPath);
        if (existingNavMesh == null) UnityEditor.AssetDatabase.CreateAsset(surface.navMeshData, navMeshPath);
        else UnityEditor.EditorUtility.CopySerialized(surface.navMeshData, existingNavMesh);
        using (var serialized = new UnityEditor.SerializedObject(surface))
        {
            serialized.FindProperty("m_NavMeshData").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(navMeshPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Spawnable prefabs: DevSite's catalog entries plus the employee, in a catalog only this scene uses.
        var devCatalog = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(devCatalogPath);
        var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(catalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<FishNet.Managing.Object.SinglePrefabObjects>();
            UnityEditor.AssetDatabase.CreateAsset(catalog, catalogPath);
        }
        using (var source = new UnityEditor.SerializedObject(devCatalog))
        using (var serialized = new UnityEditor.SerializedObject(catalog))
        {
            var from = source.FindProperty("_prefabs");
            var prefabs = serialized.FindProperty("_prefabs");
            prefabs.ClearArray();
            prefabs.arraySize = from.arraySize + 1;
            for (var index = 0; index < from.arraySize; index++)
                prefabs.GetArrayElementAtIndex(index).objectReferenceValue = from.GetArrayElementAtIndex(index).objectReferenceValue;
            prefabs.GetArrayElementAtIndex(from.arraySize).objectReferenceValue = prefab.GetComponent<FishNet.Object.NetworkObject>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        UnityEditor.EditorUtility.SetDirty(catalog);
        var manager = UnityEngine.Object.FindAnyObjectByType<FishNet.Managing.NetworkManager>();
        manager.SpawnablePrefabs = catalog;
        UnityEditor.EditorUtility.SetDirty(manager);
        var session = UnityEngine.Object.FindAnyObjectByType<FoodFactoryGame.Session.SessionRoot>();
        using (var serialized = new UnityEditor.SerializedObject(session))
        {
            serialized.FindProperty("employeePrefab").objectReferenceValue = prefab.GetComponent<FoodFactoryGame.Session.Employees.EmployeeWorker>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(devSitePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        return "clips " + clips.Length + ", box parts " + box.Length + ", navmesh " + navMeshPath + ", prefab " + prefabPath;
    }
}
