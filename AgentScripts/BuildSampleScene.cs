// Authors the PROTOTYPE employee (clips, animator, prefab) and rebuilds SampleScene as a copy of DevSite plus a baked
// NavMesh and one scene-placed employee. Idempotent; keeps asset GUIDs. DevSite is untouched and reopened at the end.
// Run the body of Run() with the Unity MCP execute_code tool (C# 6 / CodeDom compatible, no helper methods).
// The employee model is exported from ArtSource/Employee/Employee_Asset.blend (Employee_Walk, Employee_Idle actions).
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

        // Clips: loop both, short names.
        var importer = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(modelPath);
        importer.animationType = UnityEditor.ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        var clipSettings = importer.defaultClipAnimations;
        foreach (var clip in clipSettings)
        {
            clip.loopTime = true;
            clip.name = clip.takeName.EndsWith("Walk") ? "Walk" : clip.takeName.EndsWith("Idle") ? "Idle" : clip.name;
        }
        importer.clipAnimations = clipSettings;
        importer.SaveAndReimport();
        var clips = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().ToArray();
        var walk = clips.First(x => x.name == "Walk");
        var idle = clips.First(x => x.name == "Idle");

        if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Prefabs", "Employee");

        // Animator: Idle <-> Walk on observed Speed; Walk plays at WalkRate so the feet track ground speed.
        var controller = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
        if (controller == null) controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        foreach (var parameter in controller.parameters.ToArray()) controller.RemoveParameter(parameter);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("WalkRate", AnimatorControllerParameterType.Float);
        var machine = controller.layers[0].stateMachine;
        foreach (var child in machine.states.ToArray()) machine.RemoveState(child.state);
        var idleState = machine.AddState("Idle");
        idleState.motion = idle;
        var walkState = machine.AddState("Walk");
        walkState.motion = walk;
        walkState.speedParameterActive = true;
        walkState.speedParameter = "WalkRate";
        machine.defaultState = idleState;
        var toWalk = idleState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0.15f;
        toWalk.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 0.2f, "Speed");
        var toIdle = walkState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.2f;
        toIdle.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 0.1f, "Speed");
        UnityEditor.EditorUtility.SetDirty(controller);

        // Prefab: networked root with the agent and follower; the model child carries the Animator (clip paths are relative to it).
        var root = new GameObject("Employee");
        root.AddComponent<FishNet.Object.NetworkObject>();
        root.AddComponent<FishNet.Component.Transforming.NetworkTransform>();
        var agent = root.AddComponent<UnityEngine.AI.NavMeshAgent>();
        agent.speed = 1.5f;
        agent.angularSpeed = 360f;
        agent.acceleration = 6f;
        agent.radius = 0.35f;
        agent.height = 1.85f;
        agent.stoppingDistance = 2f;
        var model = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        var animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var follower = root.AddComponent<FoodFactoryGame.Session.Employees.EmployeeFollower>();
        using (var serialized = new UnityEditor.SerializedObject(follower))
        {
            serialized.FindProperty("agent").objectReferenceValue = agent;
            serialized.FindProperty("animator").objectReferenceValue = animator;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        // Scene: DevSite's session setup, saved over SampleScene (same GUID), then the NavMesh and the employee.
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(devSitePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
        scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

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

        var employee = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, scene);
        employee.transform.SetPositionAndRotation(new Vector3(-5f, 0f, 3f), Quaternion.Euler(0f, 90f, 0f));

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(devSitePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
        return "clips " + clips.Length + ", navmesh " + navMeshPath + ", prefab " + prefabPath;
    }
}
