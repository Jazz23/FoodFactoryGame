// Presentation only: builds the figures on foot that customers and ambient pedestrians are drawn with. Variant 0 is the
// customer prefab as authored; the others swap its model for a street character from Resources/Characters (built by
// ArtSource/Characters/build_pedestrian_models.py on the Employee armature, so the prefab's Animator controller and its
// path-bound clips drive them unchanged). A variant is a pure function of a key, so every client draws an entity alike.
using UnityEngine;

namespace FoodFactoryGame.Session.Customers
{
    public static class CharacterFigures
    {
        // Resources paths of the extra models, in variant order (variant 1 is the first). Keep in step with the art script.
        private static readonly string[] ModelPaths = { "Characters/Pedestrian_A", "Characters/Pedestrian_B" };
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        // Renderer names that take the clothing tint (the Employee-derived prefab's outfit, the street characters' tops).
        private static readonly string[] TintedParts = { "Torso", "UpperArm", "Thigh", "Shin", "Pelvis" };

        private static GameObject[] _models;
        private static MaterialPropertyBlock _block;

        // The prefab's own model plus every street character that loaded.
        public static int Count => 1 + Models().Length;

        // A well-mixed variant for any stable key (a customer ID's hash, an ambient pedestrian's serial).
        public static int Pick(uint key)
        {
            unchecked
            {
                key ^= key >> 16;
                key *= 0x7feb352dU;
                key ^= key >> 15;
                key *= 0x846ca68bU;
                key ^= key >> 16;
            }
            return (int)(key % (uint)Count);
        }

        // An instance of `prefab` (whose model carries an Animator) as the given variant, with the Animator that drives it.
        public static GameObject Create(GameObject prefab, int variant, Transform parent, Vector3 position, Quaternion rotation,
            out Animator animator)
        {
            var root = Object.Instantiate(prefab, position, rotation, parent);
            animator = root.GetComponentInChildren<Animator>();
            var models = Models();
            if (variant <= 0 || variant > models.Length || animator == null) return root;
            var old = animator.gameObject;
            var model = Object.Instantiate(models[variant - 1], old.transform.parent, false);
            model.name = old.name;
            // The clips animate "root/spine/...", so the Animator sits on the bone root's parent: the model root when the
            // importer collapsed the single armature node into it, else the armature node below it.
            var bones = FindChild(model.transform, "root");
            var host = bones != null && bones.parent != null ? bones.parent.gameObject : model;
            if (!host.TryGetComponent<Animator>(out var driver)) driver = host.AddComponent<Animator>();
            driver.avatar = animator.avatar;
            driver.applyRootMotion = animator.applyRootMotion;
            driver.updateMode = animator.updateMode;
            driver.cullingMode = animator.cullingMode;
            driver.runtimeAnimatorController = animator.runtimeAnimatorController;
            // Hidden first so the old model never draws for the frame before Destroy takes effect.
            old.SetActive(false);
            old.transform.SetParent(null, false);
            Object.Destroy(old);
            animator = driver;
            return root;
        }

        // Every renderer of a figure that takes the clothing tint.
        public static Renderer[] Tinted(GameObject root)
        {
            var all = root.GetComponentsInChildren<Renderer>(true);
            var count = 0;
            foreach (var renderer in all)
                if (TakesTint(renderer.name)) all[count++] = renderer;
            System.Array.Resize(ref all, count);
            return all;
        }

        public static void Tint(Renderer[] renderers, Color color)
        {
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            _block.SetColor(BaseColor, color);
            foreach (var renderer in renderers) renderer.SetPropertyBlock(_block);
        }

        private static bool TakesTint(string name)
        {
            foreach (var part in TintedParts)
                if (name.Contains(part)) return true;
            return false;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindChild(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject[] Models()
        {
            if (_models != null) return _models;
            var loaded = new System.Collections.Generic.List<GameObject>();
            foreach (var path in ModelPaths)
            {
                var model = Resources.Load<GameObject>(path);
                if (model != null) loaded.Add(model);
            }
            return _models = loaded.ToArray();
        }
    }
}
