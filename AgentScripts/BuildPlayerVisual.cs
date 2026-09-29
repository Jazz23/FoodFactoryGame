// Installs the player character from ArtSource/Player on Assets/Prefabs/Player/Player.prefab: FBX import settings and
// clips, the palette texture import settings, the URP Lit PL_Shirt (tinted per player) and PL_Palette materials with the
// model remap, the locomotion AnimatorController, and the prefab's Model child, PlayerAnimation and PlayerAvatar
// references. Replaces the capsule (Body) that BuildDevSite authors. Copy ArtSource/Player/Export/Player.fbx to
// Assets/Art/Models/Player/ and ArtSource/Player/Textures/*.png to Assets/Art/Models/Player/Textures/, and let scripts
// compile first. Idempotent; keeps the prefab's GUID and network identity.
// Run with the Unity CLI: run_script --file AgentScripts/BuildPlayerVisual.cs
using System;
using System.Linq;
using FoodFactoryGame.Session.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

public static class BuildPlayerVisual
{
    private const string ModelPath = "Assets/Art/Models/Player/Player.fbx";
    private const string MaterialFolder = "Assets/Art/Models/Player/Materials";
    private const string BasePalettePath = "Assets/Art/Models/Player/Textures/Player_Palette.png";
    private const string GlossPalettePath = "Assets/Art/Models/Player/Textures/Player_Palette_MetallicGloss.png";
    private const string ControllerPath = "Assets/Prefabs/Player/Player.controller";
    private const string PrefabPath = "Assets/Prefabs/Player/Player.prefab";
    private const string InputPath = "Assets/InputSystem_Actions.inputactions";
    // Ground speeds the Walk and Run clips are authored for (SPEEDS in build_player_model.py).
    private const float WalkSpeed = 1.6f;
    private const float RunSpeed = 6f;

    public static string Run()
    {
        // Picks up freshly copied FBX and textures.
        AssetDatabase.Refresh();
        var materials = BuildMaterials();
        ConfigureModel(materials);
        var controller = BuildController();
        return BuildPrefab(controller);
    }

    private static Material[] BuildMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Art/Models/Player", "Materials");
        // Palette cells are 8 px of flat colour: no filtering, mipmaps or compression, so each face samples its exact cell.
        foreach (var (path, srgb) in new[] { (BasePalettePath, true), (GlossPalettePath, false) })
        {
            var texture = (TextureImporter)AssetImporter.GetAtPath(path);
            if (texture == null) throw new InvalidOperationException($"{path} is missing.");
            texture.textureType = TextureImporterType.Default;
            texture.sRGBTexture = srgb;
            texture.alphaSource = TextureImporterAlphaSource.FromInput;
            texture.alphaIsTransparency = false;
            texture.mipmapEnabled = false;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.textureCompression = TextureImporterCompression.Uncompressed;
            texture.SaveAndReimport();
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var shirt = LoadOrCreate("PL_Shirt", shader);
        shirt.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.92f));
        shirt.SetFloat("_Smoothness", 0.2f);
        shirt.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(shirt);

        var palette = LoadOrCreate("PL_Palette", shader);
        palette.SetColor("_BaseColor", Color.white);
        palette.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BasePalettePath));
        palette.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(GlossPalettePath));
        palette.EnableKeyword("_METALLICSPECGLOSSMAP");
        palette.SetFloat("_SmoothnessTextureChannel", 0f);
        palette.SetFloat("_Smoothness", 1f);
        palette.SetFloat("_Metallic", 1f);
        EditorUtility.SetDirty(palette);

        // Materials from earlier versions of the model (one per part) are no longer used.
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name != "PL_Shirt" && name != "PL_Palette") AssetDatabase.DeleteAsset(path);
        }
        return new[] { shirt, palette };
    }

    private static Material LoadOrCreate(string name, Shader shader)
    {
        var path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        return material;
    }

    private static void ConfigureModel(Material[] materials)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        if (importer == null) throw new InvalidOperationException($"{ModelPath} is missing.");
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = false;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        foreach (var stale in importer.GetExternalObjectMap().Keys.Where(x => x.type == typeof(Material)).ToArray())
            importer.RemoveRemap(stale);
        foreach (var material in materials)
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
        importer.SaveAndReimport();

        // Takes arrive as "Player_Armature|<Action>"; clips keep the action name.
        var looping = new[] { "Idle", "Walk", "Run", "Fall" };
        importer.clipAnimations = importer.defaultClipAnimations.Select(clip =>
        {
            clip.name = clip.takeName.Split('|').Last();
            clip.loopTime = looping.Contains(clip.name);
            clip.loopPose = false;
            clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
            return clip;
        }).ToArray();
        importer.SaveAndReimport();
    }

    private static AnimationClip Clip(string name)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .FirstOrDefault(x => x.name == name && !x.name.StartsWith("__preview__"));
        if (clip == null) throw new InvalidOperationException($"{ModelPath} has no {name} clip.");
        return clip;
    }

    private static AnimatorController BuildController()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter
            { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
        controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);

        var machine = controller.layers[0].stateMachine;
        var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(Clip("Idle"), 0f);
        tree.AddChild(Clip("Walk"), WalkSpeed);
        tree.AddChild(Clip("Run"), RunSpeed);
        machine.defaultState = locomotion;
        var jump = machine.AddState("Jump");
        jump.motion = Clip("Jump");
        var fall = machine.AddState("Fall");
        fall.motion = Clip("Fall");
        var land = machine.AddState("Land");
        land.motion = Clip("Land");

        Transition(locomotion, jump, 0.1f, ("Grounded", AnimatorConditionMode.IfNot, 0f), ("VerticalSpeed", AnimatorConditionMode.Greater, 1f));
        Transition(locomotion, fall, 0.2f, ("Grounded", AnimatorConditionMode.IfNot, 0f), ("VerticalSpeed", AnimatorConditionMode.Less, -1f));
        Transition(jump, land, 0.05f, ("Grounded", AnimatorConditionMode.If, 0f));
        Transition(jump, fall, 0.3f, ("VerticalSpeed", AnimatorConditionMode.Less, 0f));
        Transition(fall, land, 0.05f, ("Grounded", AnimatorConditionMode.If, 0f));
        Transition(land, jump, 0.1f, ("Grounded", AnimatorConditionMode.IfNot, 0f), ("VerticalSpeed", AnimatorConditionMode.Greater, 1f));
        // Landing at a run skips most of the crouch.
        Transition(land, locomotion, 0.2f, ("Speed", AnimatorConditionMode.Greater, 3f));
        var settle = land.AddTransition(locomotion);
        settle.hasExitTime = true;
        settle.exitTime = 0.8f;
        settle.duration = 0.15f;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void Transition(AnimatorState from, AnimatorState to, float duration,
        params (string parameter, AnimatorConditionMode mode, float threshold)[] conditions)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = duration;
        foreach (var condition in conditions) transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
    }

    private static InputActionReference Action(string name)
    {
        var reference = AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>()
            .FirstOrDefault(x => x.action != null && x.action.actionMap.name == "Player" && x.action.name == name);
        if (reference == null) throw new InvalidOperationException($"Missing Player/{name} action reference.");
        return reference;
    }

    private static string BuildPrefab(AnimatorController controller)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (var name in new[] { "Body", "Model" })
            {
                var old = root.transform.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), root.transform);
            model.name = "Model";
            // The FBX faces -Z (see build_player_model.py); the avatar moves along its +Z.
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            model.transform.localScale = Vector3.one;
            model.transform.SetSiblingIndex(0);

            // Facing check: the model must face +Z (toes ahead of ankles) with its left side on -X.
            var toes = model.GetComponentsInChildren<Transform>().First(x => x.name == "Toes.L");
            var foot = model.GetComponentsInChildren<Transform>().First(x => x.name == "Foot.L");
            if (toes.position.z < foot.position.z) throw new InvalidOperationException("Player model imports facing -Z.");
            if (toes.position.x > 0f) throw new InvalidOperationException("Player model imports mirrored (left side on +X).");

            var animator = model.TryGetComponent<Animator>(out var existing) ? existing : model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var body = model.GetComponentInChildren<SkinnedMeshRenderer>();
            var shirt = Array.FindIndex(body.sharedMaterials, x => x != null && x.name == "PL_Shirt");
            if (shirt < 0) throw new InvalidOperationException("Player body has no PL_Shirt material.");

            var animation = root.TryGetComponent<PlayerAnimation>(out var current) ? current : root.AddComponent<PlayerAnimation>();
            using (var serialized = new SerializedObject(animation))
            {
                serialized.FindProperty("animator").objectReferenceValue = animator;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            using (var serialized = new SerializedObject(root.GetComponent<PlayerAvatar>()))
            {
                serialized.FindProperty("body").objectReferenceValue = body;
                serialized.FindProperty("tintMaterialIndex").intValue = shirt;
                serialized.FindProperty("jumpAction").objectReferenceValue = Action("Jump");
                serialized.FindProperty("sprintAction").objectReferenceValue = Action("Sprint");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            var bounds = body.bounds;
            return $"Player prefab: model {bounds.size.y:F2} m tall, {body.sharedMesh.triangles.Length / 3} tris, " +
                   $"{body.bones.Length} bones, shirt material {shirt}, clips " +
                   string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                       .Where(x => !x.name.StartsWith("__preview__")).Select(x => $"{x.name} {x.length:F2}s{(x.isLooping ? " loop" : "")}"));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
