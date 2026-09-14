// Captures deterministic SceneView visual-regression artifacts and writes reviewable evidence.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VisualRegressionEvidence
{
    public const uint TargetBuildingInstanceId = 13;
    public const string TargetScenePath = "Assets/Scenes/OutsideTest.unity";
    public const string DefaultRelativeDirectory = "TestResults/VisualRegression/Building13";
    public const string DefaultAcceptanceText = "In this exact Building 13 view, the dark triangle is absent.";
    public const string DefaultAttemptedChangeText = "[Describe the attempted change.]";

    [Serializable]
    public sealed class ComparisonReport
    {
        public string baselinePath = string.Empty;
        public string afterPath = string.Empty;
        public string comparisonPath = string.Empty;
        public int baselineWidth;
        public int baselineHeight;
        public int afterWidth;
        public int afterHeight;
        public bool dimensionsMatch;
        public int tolerance;
        public RectInt requestedRoi;
        public RectInt clampedRoi;
        public bool roiWasClamped;
        public bool roiValid;
        public int totalPixels;
        public int differingPixels;
        public int maxChannelDelta;
        public bool passed;
        public string failureReason = string.Empty;
    }

    [Serializable]
    public sealed class ViewManifest
    {
        public uint targetBuildingInstanceId = TargetBuildingInstanceId;
        public string scenePath = string.Empty;
        public string selectedHierarchyPath = string.Empty;
        public string acceptanceText = DefaultAcceptanceText;
        public string consoleState = string.Empty;
        public string baselinePath = string.Empty;
        public string afterPath = string.Empty;
        public string comparisonPath = string.Empty;
        public string evidencePath = string.Empty;
        public CameraState camera = new();
        public SceneViewState sceneView = new();
        public ResolutionData resolution = new();
        public RectInt roi;
    }

    [Serializable]
    public sealed class CameraState
    {
        public Vector3 position;
        public Vector3 eulerAngles;
        public string projection = string.Empty;
        public bool orthographic;
        public float fieldOfView;
        public float orthographicSize;
        public float nearClipPlane;
        public float farClipPlane;
        public float aspect;
        public int cullingMask;
    }

    [Serializable]
    public sealed class SceneViewState
    {
        public bool drawGizmos;
        public bool sceneLighting;
        public bool showGrid;
        public bool in2DMode;
    }

    [Serializable]
    public sealed class ResolutionData
    {
        public int width;
        public int height;
    }

    [Serializable]
    public sealed class EvidencePacket
    {
        public uint targetBuildingInstanceId = TargetBuildingInstanceId;
        public string scenePath = string.Empty;
        public string viewManifestPath = string.Empty;
        public string baselinePath = string.Empty;
        public string afterPath = string.Empty;
        public string comparisonPath = string.Empty;
        public string acceptanceText = DefaultAcceptanceText;
        public string attemptedChangeText = DefaultAttemptedChangeText;
        public string consoleState = string.Empty;
        public string selectedObjectHierarchyPath = string.Empty;
        public RendererEvidence[] rendererEvidence = Array.Empty<RendererEvidence>();
        public MeshFilterEvidence[] meshFilterEvidence = Array.Empty<MeshFilterEvidence>();
        public ReviewGate reviewGate = new();
        public bool comparisonPassed;
        public bool claimedPass;
        public string overallStatus = "pending-review";
    }

    [Serializable]
    public sealed class RendererEvidence
    {
        public string objectPath = string.Empty;
        public string rendererType = string.Empty;
        public string materialName = string.Empty;
        public string shaderName = string.Empty;
        public BoundsData worldBounds;
    }

    [Serializable]
    public sealed class MeshFilterEvidence
    {
        public string objectPath = string.Empty;
        public string meshName = string.Empty;
        public BoundsData localBounds;
    }

    [Serializable]
    public sealed class BoundsData
    {
        public Vector3 center;
        public Vector3 size;
        public Vector3 min;
        public Vector3 max;

        public static BoundsData From(Bounds bounds)
        {
            return new BoundsData
            {
                center = bounds.center,
                size = bounds.size,
                min = bounds.min,
                max = bounds.max
            };
        }
    }

    [Serializable]
    public sealed class ReviewGate
    {
        public bool required = true;
        public string reviewerName = string.Empty;
        public string verdict = "pending";
        public string notes = string.Empty;
    }

    [MenuItem("Food Factory/Visual Regression/Capture Building 13 Baseline")]
    private static void CaptureBaselineMenu()
    {
        var baselinePath = GetArtifactPath("baseline.png");
        var overwrite = File.Exists(baselinePath)
            && EditorUtility.DisplayDialog(
                "Overwrite visual baseline?",
                "A baseline already exists. Overwrite it only if this is an intentional new baseline.",
                "Overwrite",
                "Cancel");
        var result = CaptureBaseline(overwrite);
        if (result is not null)
        {
            Debug.Log($"Visual regression baseline captured: {result}");
        }
    }

    [MenuItem("Food Factory/Visual Regression/Capture Building 13 After + Compare")]
    private static void CaptureAfterAndCompareMenu()
    {
        var result = CaptureAfterAndCompare(0);
        if (result is not null)
        {
            Debug.Log($"Visual regression comparison written: {result.comparisonPath}; passed={result.passed}");
        }
    }

    [MenuItem("Food Factory/Visual Regression/Write Building 13 Evidence Packet")]
    private static void WriteEvidencePacketMenu()
    {
        var result = WriteEvidencePacket(DefaultAttemptedChangeText, DefaultAcceptanceText);
        if (result is not null)
        {
            Debug.Log($"Visual regression evidence packet written: {result}");
        }
    }

    public static string CaptureBaseline(bool overwriteExisting)
    {
        var paths = GetArtifactPaths();
        if (File.Exists(paths.baseline) && !overwriteExisting)
        {
            Debug.LogError($"Visual regression baseline already exists and was not overwritten: {paths.baseline}");
            return null;
        }

        var sceneView = GetActiveSceneView();
        if (sceneView is null)
        {
            return null;
        }

        if (!TrySelectTargetBuilding())
        {
            return null;
        }

        var resolution = GetCaptureResolution(sceneView);
        var roi = new RectInt(0, 0, resolution.width, resolution.height);
        var manifest = CreateManifest(sceneView, resolution, roi, paths);
        WritePng(CaptureSceneView(sceneView, resolution.width, resolution.height), paths.baseline);
        manifest.baselinePath = ToProjectRelative(paths.baseline);
        WriteJson(paths.manifest, manifest);
        return ToProjectRelative(paths.baseline);
    }

    public static ComparisonReport CaptureAfterAndCompare(int tolerance)
    {
        var paths = GetArtifactPaths();
        if (!File.Exists(paths.baseline) || !File.Exists(paths.manifest))
        {
            Debug.LogError("Capture After + Compare requires an existing baseline and view manifest. Capture a baseline first.");
            return null;
        }

        var sceneView = GetActiveSceneView();
        if (sceneView is null)
        {
            return null;
        }

        if (!TrySelectTargetBuilding())
        {
            return null;
        }

        var manifest = ReadJson<ViewManifest>(paths.manifest);
        var camera = sceneView.camera;
        var original = CaptureCameraState(camera);
        var originalSceneView = CaptureSceneViewState(sceneView);
        try
        {
            ApplyCameraState(camera, manifest.camera);
            ApplySceneViewState(sceneView, manifest.sceneView);
            var resolution = manifest.resolution;
            WritePng(CaptureSceneView(sceneView, resolution.width, resolution.height), paths.after);
        }
        finally
        {
            ApplyCameraState(camera, original);
            ApplySceneViewState(sceneView, originalSceneView);
        }

        manifest.afterPath = ToProjectRelative(paths.after);
        WriteJson(paths.manifest, manifest);
        return ComparePngs(paths.baseline, paths.after, manifest.roi, tolerance, paths.comparison);
    }

    public static string WriteEvidencePacket(
        string attemptedChangeText = DefaultAttemptedChangeText,
        string acceptanceText = DefaultAcceptanceText)
    {
        var paths = GetArtifactPaths();
        if (!File.Exists(paths.baseline) || !File.Exists(paths.after) || !File.Exists(paths.comparison))
        {
            Debug.LogError("Write Evidence Packet requires baseline, after, and comparison artifacts.");
            return null;
        }

        var manifest = File.Exists(paths.manifest) ? ReadJson<ViewManifest>(paths.manifest) : new ViewManifest();
        var comparison = ReadJson<ComparisonReport>(paths.comparison);
        if (!TrySelectTargetBuilding())
        {
            return null;
        }

        var selected = Selection.activeGameObject;
        var reviewGate = ReadExistingReviewGate(paths.evidence);
        var packet = new EvidencePacket
        {
            targetBuildingInstanceId = TargetBuildingInstanceId,
            scenePath = manifest.scenePath,
            viewManifestPath = ToProjectRelative(paths.manifest),
            baselinePath = ToProjectRelative(paths.baseline),
            afterPath = ToProjectRelative(paths.after),
            comparisonPath = ToProjectRelative(paths.comparison),
            acceptanceText = string.IsNullOrWhiteSpace(acceptanceText) ? DefaultAcceptanceText : acceptanceText,
            attemptedChangeText = string.IsNullOrWhiteSpace(attemptedChangeText) ? DefaultAttemptedChangeText : attemptedChangeText,
            consoleState = CaptureConsoleState(),
            selectedObjectHierarchyPath = selected is null ? string.Empty : GetHierarchyPath(selected.transform),
            rendererEvidence = GetRendererEvidence(selected),
            meshFilterEvidence = GetMeshFilterEvidence(selected),
            reviewGate = reviewGate,
            comparisonPassed = comparison.passed,
            claimedPass = comparison.passed && IsIndependentReviewApproved(reviewGate),
            overallStatus = comparison.passed
                ? (IsIndependentReviewApproved(reviewGate) ? "passed" : "pending-review")
                : "failed"
        };

        manifest.selectedHierarchyPath = packet.selectedObjectHierarchyPath;
        manifest.acceptanceText = packet.acceptanceText;
        manifest.consoleState = packet.consoleState;
        WriteJson(paths.manifest, manifest);
        WriteJson(paths.evidence, packet);
        return ToProjectRelative(paths.evidence);
    }

    public static ComparisonReport ComparePngs(
        string baselinePath,
        string afterPath,
        RectInt roi,
        int tolerance,
        string comparisonJsonPath)
    {
        if (tolerance < 0 || tolerance > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be between 0 and 255.");
        }

        var baseline = LoadPng(baselinePath);
        var after = LoadPng(afterPath);
        try
        {
            var report = new ComparisonReport
            {
                baselinePath = ToProjectRelative(baselinePath),
                afterPath = ToProjectRelative(afterPath),
                comparisonPath = ToProjectRelative(comparisonJsonPath),
                baselineWidth = baseline.width,
                baselineHeight = baseline.height,
                afterWidth = after.width,
                afterHeight = after.height,
                dimensionsMatch = baseline.width == after.width && baseline.height == after.height,
                tolerance = tolerance,
                requestedRoi = roi
            };

            if (!report.dimensionsMatch)
            {
                report.failureReason = "PNG dimensions do not match.";
                WriteJson(comparisonJsonPath, report);
                return report;
            }

            report.clampedRoi = ClampRoi(roi, baseline.width, baseline.height);
            report.roiWasClamped = report.clampedRoi != roi;
            report.roiValid = report.clampedRoi.width > 0 && report.clampedRoi.height > 0;
            if (!report.roiValid)
            {
                report.failureReason = "ROI is empty after deterministic clamping to the PNG dimensions.";
                WriteJson(comparisonJsonPath, report);
                return report;
            }

            var baselinePixels = baseline.GetPixels32();
            var afterPixels = after.GetPixels32();
            for (var y = report.clampedRoi.yMin; y < report.clampedRoi.yMax; y++)
            {
                for (var x = report.clampedRoi.xMin; x < report.clampedRoi.xMax; x++)
                {
                    var index = y * baseline.width + x;
                    var delta = GetChannelDelta(baselinePixels[index], afterPixels[index]);
                    report.totalPixels++;
                    report.maxChannelDelta = Mathf.Max(report.maxChannelDelta, delta);
                    if (delta > tolerance)
                    {
                        report.differingPixels++;
                    }
                }
            }

            report.passed = report.differingPixels == 0;
            report.failureReason = report.passed ? string.Empty : "One or more pixels differ inside the ROI beyond tolerance.";
            WriteJson(comparisonJsonPath, report);
            return report;
        }
        finally
        {
            DestroyTexture(baseline);
            DestroyTexture(after);
        }
    }

    private static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;

    private sealed class ArtifactPaths
    {
        public string directory = string.Empty;
        public string baseline = string.Empty;
        public string after = string.Empty;
        public string manifest = string.Empty;
        public string comparison = string.Empty;
        public string evidence = string.Empty;
    }

    private static ArtifactPaths GetArtifactPaths()
    {
        var directory = Path.Combine(ProjectRoot, DefaultRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);
        return new ArtifactPaths
        {
            directory = directory,
            baseline = Path.Combine(directory, "baseline.png"),
            after = Path.Combine(directory, "after.png"),
            manifest = Path.Combine(directory, "view-manifest.json"),
            comparison = Path.Combine(directory, "comparison.json"),
            evidence = Path.Combine(directory, "evidence.json")
        };
    }

    private static SceneView GetActiveSceneView()
    {
        var sceneView = SceneView.lastActiveSceneView;
        if (sceneView is null || sceneView.camera is null)
        {
            Debug.LogError("Visual regression capture failed: no active SceneView camera exists.");
            return null;
        }

        return sceneView;
    }

    private static bool TrySelectTargetBuilding()
    {
        var activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.path, TargetScenePath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError(
                $"Visual regression capture requires {TargetScenePath} as the active scene; "
                + $"active scene is '{activeScene.path}'.");
            return false;
        }

        var layouts = UnityEngine.Object.FindObjectsByType<TestBuildingLayout>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var layout in layouts)
        {
            if (layout.gameObject.scene != activeScene
                || layout.BuildingInstanceId != TargetBuildingInstanceId)
            {
                continue;
            }

            Selection.activeGameObject = layout.gameObject;
            return true;
        }

        Debug.LogError(
            $"Visual regression capture could not find building {TargetBuildingInstanceId} "
            + $"in {TargetScenePath}.");
        return false;
    }

    private static string CaptureConsoleState()
    {
        try
        {
            var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
            var getCounts = logEntriesType?.GetMethod(
                "GetCountsByType",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getCounts is null)
            {
                return "unavailable; UnityEditor.LogEntries.GetCountsByType was not found";
            }

            var arguments = new object[] { 0, 0, 0 };
            getCounts.Invoke(null, arguments);
            return $"errors={arguments[0]};warnings={arguments[1]};logs={arguments[2]};"
                + $"compiling={EditorApplication.isCompiling};playing={EditorApplication.isPlaying}";
        }
        catch (Exception exception)
        {
            return $"unavailable; {exception.GetType().Name}: {exception.Message}";
        }
    }

    private static ResolutionData GetCaptureResolution(SceneView sceneView)
    {
        return new ResolutionData
        {
            width = Mathf.Max(1, sceneView.camera.pixelWidth),
            height = Mathf.Max(1, sceneView.camera.pixelHeight)
        };
    }

    private static ViewManifest CreateManifest(SceneView sceneView, ResolutionData resolution, RectInt roi, ArtifactPaths paths)
    {
        var scene = SceneManager.GetActiveScene();
        return new ViewManifest
        {
            targetBuildingInstanceId = TargetBuildingInstanceId,
            scenePath = scene.path,
            selectedHierarchyPath = Selection.activeGameObject is null
                ? string.Empty
                : GetHierarchyPath(Selection.activeGameObject.transform),
            acceptanceText = DefaultAcceptanceText,
            consoleState = CaptureConsoleState(),
            baselinePath = ToProjectRelative(paths.baseline),
            afterPath = ToProjectRelative(paths.after),
            comparisonPath = ToProjectRelative(paths.comparison),
            evidencePath = ToProjectRelative(paths.evidence),
            camera = CaptureCameraState(sceneView.camera),
            sceneView = new SceneViewState
            {
                drawGizmos = sceneView.drawGizmos,
                sceneLighting = sceneView.sceneLighting,
                showGrid = sceneView.showGrid,
                in2DMode = sceneView.in2DMode
            },
            resolution = resolution,
            roi = roi
        };
    }

    private static CameraState CaptureCameraState(Camera camera)
    {
        return new CameraState
        {
            position = camera.transform.position,
            eulerAngles = camera.transform.eulerAngles,
            projection = camera.orthographic ? "Orthographic" : "Perspective",
            orthographic = camera.orthographic,
            fieldOfView = camera.fieldOfView,
            orthographicSize = camera.orthographicSize,
            nearClipPlane = camera.nearClipPlane,
            farClipPlane = camera.farClipPlane,
            aspect = camera.aspect,
            cullingMask = camera.cullingMask
        };
    }

    private static SceneViewState CaptureSceneViewState(SceneView sceneView)
    {
        return new SceneViewState
        {
            drawGizmos = sceneView.drawGizmos,
            sceneLighting = sceneView.sceneLighting,
            showGrid = sceneView.showGrid,
            in2DMode = sceneView.in2DMode
        };
    }

    private static void ApplySceneViewState(SceneView sceneView, SceneViewState state)
    {
        sceneView.drawGizmos = state.drawGizmos;
        sceneView.sceneLighting = state.sceneLighting;
        sceneView.showGrid = state.showGrid;
        sceneView.in2DMode = state.in2DMode;
    }

    private static void ApplyCameraState(Camera camera, CameraState state)
    {
        camera.transform.position = state.position;
        camera.transform.eulerAngles = state.eulerAngles;
        camera.orthographic = state.orthographic;
        camera.fieldOfView = state.fieldOfView;
        camera.orthographicSize = state.orthographicSize;
        camera.nearClipPlane = state.nearClipPlane;
        camera.farClipPlane = state.farClipPlane;
        camera.aspect = state.aspect;
        camera.cullingMask = state.cullingMask;
    }

    private static Texture2D CaptureSceneView(SceneView sceneView, int width, int height)
    {
        var camera = sceneView.camera;
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        try
        {
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            texture.Apply(false, false);
            return texture;
        }
        catch
        {
            DestroyTexture(texture);
            throw;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    private static void WritePng(Texture2D texture, string path)
    {
        try
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            DestroyTexture(texture);
        }
    }

    private static Texture2D LoadPng(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("PNG artifact was not found.", path);
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!texture.LoadImage(File.ReadAllBytes(path), false))
        {
            DestroyTexture(texture);
            throw new InvalidDataException($"Could not decode PNG artifact: {path}");
        }

        return texture;
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (texture is not null)
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static int GetChannelDelta(Color32 left, Color32 right)
    {
        return Mathf.Max(
            Mathf.Abs(left.r - right.r),
            Mathf.Abs(left.g - right.g),
            Mathf.Abs(left.b - right.b),
            Mathf.Abs(left.a - right.a));
    }

    private static RectInt ClampRoi(RectInt roi, int width, int height)
    {
        var xMin = Mathf.Clamp(roi.x, 0, width);
        var yMin = Mathf.Clamp(roi.y, 0, height);
        var xMax = Mathf.Clamp((int)Math.Min(int.MaxValue, (long)roi.x + roi.width), 0, width);
        var yMax = Mathf.Clamp((int)Math.Min(int.MaxValue, (long)roi.y + roi.height), 0, height);
        return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
    }

    private static RendererEvidence[] GetRendererEvidence(GameObject selected)
    {
        if (selected is null)
        {
            return Array.Empty<RendererEvidence>();
        }

        var renderers = selected.GetComponentsInChildren<Renderer>(true);
        Array.Sort(renderers, (left, right) => string.CompareOrdinal(
            GetHierarchyPath(left.transform),
            GetHierarchyPath(right.transform)));
        var evidence = new List<RendererEvidence>(renderers.Length);
        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            var materialNames = new List<string>();
            var shaderNames = new List<string>();
            foreach (var material in materials)
            {
                if (material is null)
                {
                    continue;
                }

                materialNames.Add(material.name);
                shaderNames.Add(material.shader is null ? string.Empty : material.shader.name);
            }

            evidence.Add(new RendererEvidence
            {
                objectPath = GetHierarchyPath(renderer.transform),
                rendererType = renderer.GetType().FullName,
                materialName = string.Join(" | ", materialNames),
                shaderName = string.Join(" | ", shaderNames),
                worldBounds = BoundsData.From(renderer.bounds)
            });
        }

        return evidence.ToArray();
    }

    private static MeshFilterEvidence[] GetMeshFilterEvidence(GameObject selected)
    {
        if (selected is null)
        {
            return Array.Empty<MeshFilterEvidence>();
        }

        var filters = selected.GetComponentsInChildren<MeshFilter>(true);
        Array.Sort(filters, (left, right) => string.CompareOrdinal(
            GetHierarchyPath(left.transform),
            GetHierarchyPath(right.transform)));
        var evidence = new List<MeshFilterEvidence>(filters.Length);
        foreach (var filter in filters)
        {
            if (filter.sharedMesh is null)
            {
                continue;
            }

            evidence.Add(new MeshFilterEvidence
            {
                objectPath = GetHierarchyPath(filter.transform),
                meshName = filter.sharedMesh.name,
                localBounds = BoundsData.From(filter.sharedMesh.bounds)
            });
        }

        return evidence.ToArray();
    }

    private static ReviewGate ReadExistingReviewGate(string path)
    {
        if (File.Exists(path))
        {
            var existing = ReadJson<EvidencePacket>(path);
            if (existing is not null && existing.reviewGate is not null)
            {
                return existing.reviewGate;
            }
        }

        return new ReviewGate();
    }

    private static bool IsIndependentReviewApproved(ReviewGate reviewGate)
    {
        return reviewGate is not null
            && reviewGate.required
            && !string.IsNullOrWhiteSpace(reviewGate.reviewerName)
            && string.Equals(reviewGate.verdict, "approved", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(reviewGate.notes);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var names = new List<string>();
        for (var current = transform; current is not null; current = current.parent)
        {
            names.Add(current.name);
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static T ReadJson<T>(string path)
    {
        return JsonUtility.FromJson<T>(File.ReadAllText(path));
    }

    private static void WriteJson<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && directory.Length > 0)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonUtility.ToJson(value, true));
    }

    private static string GetArtifactPath(string fileName)
    {
        return Path.Combine(ProjectRoot, DefaultRelativeDirectory.Replace('/', Path.DirectorySeparatorChar), fileName);
    }

    private static string ToProjectRelative(string path)
    {
        var absolute = Path.GetFullPath(path);
        var root = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return ToDisplayPath(Path.GetRelativePath(ProjectRoot, absolute));
        }

        return ToDisplayPath(path);
    }

    private static string ToDisplayPath(string path)
    {
        return path.Replace('\\', '/');
    }
}
