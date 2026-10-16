// The anti-aliasing chosen in Settings (ClientSettings.AntiAliasing), applied to every on-screen game camera as URP renders it,
// so cameras spawned later (the local avatar's rig) pick it up without any reference. MSAA is a pipeline-asset sample count;
// FXAA and SMAA are URP post passes, which only run on a camera with post-processing on. Game cameras are authored without
// post-processing, so a camera this turns it on for gets a volume stack of default (neutral) values that is never blended:
// the pipeline asset's profile (bloom, vignette, tonemapping, motion blur) stays off and only the anti-aliasing pass is added.
// In the Editor the asset's authored sample count is put back when play stops, so trying the setting never edits the asset.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FoodFactoryGame.Session
{
    // Stored by name in ClientSettings; add new options at the end.
    public enum AntiAliasingOption
    {
        Off,
        Fxaa,
        Smaa,
        Msaa2x,
        Msaa4x,
        Msaa8x
    }

    public static class AntiAliasing
    {
        public static AntiAliasingOption Current { get; private set; } = AntiAliasingOption.Off;

        // Cameras already set up for Current (by entity ID), and those whose post-processing was turned on here.
        private static readonly Dictionary<EntityId, AntiAliasingOption> Configured = new();
        private static readonly HashSet<EntityId> PostEnabledHere = new();
        private static readonly Dictionary<UniversalRenderPipelineAsset, int> AuthoredSamples = new();
        private static bool _hooked;

        public static string Label(AntiAliasingOption option) => option switch
        {
            AntiAliasingOption.Fxaa => "FXAA",
            AntiAliasingOption.Smaa => "SMAA",
            AntiAliasingOption.Msaa2x => "MSAA 2×",
            AntiAliasingOption.Msaa4x => "MSAA 4×",
            AntiAliasingOption.Msaa8x => "MSAA 8×",
            _ => "Off"
        };

        public static int SampleCount(AntiAliasingOption option) => option switch
        {
            AntiAliasingOption.Msaa2x => 2,
            AntiAliasingOption.Msaa4x => 4,
            AntiAliasingOption.Msaa8x => 8,
            _ => 1
        };

        private static AntialiasingMode PostMode(AntiAliasingOption option) => option switch
        {
            AntiAliasingOption.Fxaa => AntialiasingMode.FastApproximateAntialiasing,
            AntiAliasingOption.Smaa => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            _ => AntialiasingMode.None
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = AntiAliasingOption.Off;
            Configured.Clear();
            PostEnabledHere.Clear();
            AuthoredSamples.Clear();
            // Without a domain reload the handlers from the last play would otherwise stay subscribed.
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Application.quitting -= OnQuitting;
            _hooked = false;
        }

        // Applies an option now (the pipeline's MSAA and every camera on its next render). Without URP it does nothing.
        public static void Apply(AntiAliasingOption option)
        {
            Current = option;
            Configured.Clear();
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset asset) return;
            if (!_hooked)
            {
                _hooked = true;
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                Application.quitting += OnQuitting;
            }
            if (!AuthoredSamples.ContainsKey(asset)) AuthoredSamples[asset] = asset.msaaSampleCount;
            // Post anti-aliasing replaces MSAA (1 sample); URP lowers a count the device cannot do.
            asset.msaaSampleCount = SampleCount(option);
        }

        // Before URP builds a camera's frame data: brings a game camera that draws to the screen in line with Current.
        private static void OnBeginCamera(ScriptableRenderContext _, Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game || camera.targetTexture != null) return;
            var id = camera.GetEntityId();
            if (Configured.TryGetValue(id, out var done) && done == Current) return;
            Configured[id] = Current;
            var data = camera.GetUniversalAdditionalCameraData();
            if (data == null || data.renderType != CameraRenderType.Base) return;
            var mode = PostMode(Current);
            data.antialiasing = mode;
            data.antialiasingQuality = AntialiasingQuality.High;
            if (SampleCount(Current) > 1) camera.allowMSAA = true;
            if (mode != AntialiasingMode.None && !data.renderPostProcessing)
            {
                PostEnabledHere.Add(id);
                data.renderPostProcessing = true;
                camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);
                // Switching mode fills a stack from the volumes; it is replaced by a fresh one holding each effect's default (off)
                // values, which is never updated, so no volume ever blends in.
                var blended = data.volumeStack;
                data.volumeStack = VolumeManager.instance.CreateStack();
                if (blended != null) VolumeManager.instance.DestroyStack(blended);
            }
            else if (mode == AntialiasingMode.None && PostEnabledHere.Remove(id))
            {
                data.renderPostProcessing = false;
                var neutral = data.volumeStack;
                if (neutral != null) VolumeManager.instance.DestroyStack(neutral);
                data.volumeStack = null;
                camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.UsePipelineSettings);
            }
        }

        // Editor play mode edits the real pipeline asset; put its authored sample count back when play stops.
        private static void OnQuitting()
        {
#if UNITY_EDITOR
            foreach (var (asset, samples) in AuthoredSamples)
                if (asset != null) asset.msaaSampleCount = samples;
#endif
            AuthoredSamples.Clear();
        }
    }
}
