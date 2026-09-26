// Measures Game view frame intervals while the existing animated character model is visible in DevSite.
// This probe creates only temporary Play Mode objects and writes its measurements outside application saves.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ProbeAnimatedCustomers
{
    private static readonly int[] Counts = { 0, 50, 100, 200, 400 };
    private static readonly List<float> Frames = new();
    private static GameObject _root;
    private static GameObject _model;
    private static Camera _camera;
    private static int _stage;
    private static int _lastFrame;
    private static double _stageStart;
    private static int _oldTargetFrameRate;
    private static int _oldVsync;
    private static string _path;
    private static readonly List<string> Results = new();

    public static string Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        if (_root != null) throw new InvalidOperationException("Probe already running.");
        var employee = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Employee/Employee.prefab");
        if (employee == null) throw new InvalidOperationException("Employee prefab is missing.");
        _model = employee.GetComponentInChildren<Animator>(true)?.gameObject;
        if (_model == null) throw new InvalidOperationException("Animated employee model is missing.");
        _root = new GameObject("Animated customer frame probe (temporary)");
        _camera = new GameObject("Animated customer probe camera").AddComponent<Camera>();
        _camera.transform.SetPositionAndRotation(new Vector3(12, 35, -22), Quaternion.LookRotation(new Vector3(0, 0, 12) - new Vector3(12, 35, -22)));
        _camera.orthographic = true;
        _camera.orthographicSize = 36;
        _camera.depth = 100;
        _oldTargetFrameRate = Application.targetFrameRate;
        _oldVsync = QualitySettings.vSyncCount;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        _path = Path.Combine("docs", "verification", "artifacts", "animated-customer-probe-20260925.csv");
        Results.Clear();
        Results.Add("count,frames,median_ms,p95_ms,under_16_67_percent");
        _stage = 0;
        SetCount(Counts[0]);
        _stageStart = EditorApplication.timeSinceStartup;
        _lastFrame = Time.frameCount;
        EditorApplication.update += Tick;
        return "Started temporary animated-character Game view probe: 0, 50, 100, 200, 400; 1 s warmup and 3 s sampling each.";
    }

    private static void SetCount(int count)
    {
        for (var index = _root.transform.childCount; index < count; index++)
        {
            var instance = UnityEngine.Object.Instantiate(_model, _root.transform);
            instance.name = "Probe customer " + index;
            var x = (index % 25) * 0.9f;
            var z = (index / 25) * 0.9f;
            instance.transform.position = new Vector3(x, 0, z);
            instance.transform.rotation = Quaternion.Euler(0, (index * 73) % 360, 0);
            var animator = instance.GetComponent<Animator>();
            animator.SetFloat("Speed", 2);
            animator.SetFloat("WalkRate", 1.33f);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            Finish();
            return;
        }
        var elapsed = EditorApplication.timeSinceStartup - _stageStart;
        if (Time.frameCount != _lastFrame)
        {
            _lastFrame = Time.frameCount;
            if (elapsed >= 1 && elapsed < 4) Frames.Add(Time.unscaledDeltaTime * 1000);
        }
        if (elapsed < 4) return;
        var sorted = Frames.OrderBy(x => x).ToArray();
        var median = sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
        var p95 = sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)];
        var within = sorted.Length == 0 ? 0 : 100.0 * sorted.Count(x => x <= 1000f / 60f) / sorted.Length;
        Results.Add($"{Counts[_stage]},{sorted.Length},{median:F2},{p95:F2},{within:F1}");
        Debug.Log("[CustomerVisualProbe] " + Results[^1]);
        Frames.Clear();
        _stage++;
        if (_stage >= Counts.Length)
        {
            Finish();
            return;
        }
        SetCount(Counts[_stage]);
        _stageStart = EditorApplication.timeSinceStartup;
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        Directory.CreateDirectory(Path.GetDirectoryName(_path));
        File.WriteAllLines(_path, Results);
        QualitySettings.vSyncCount = _oldVsync;
        Application.targetFrameRate = _oldTargetFrameRate;
        if (_camera != null) UnityEngine.Object.Destroy(_camera.gameObject);
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _camera = null;
        _root = null;
        Debug.Log("[CustomerVisualProbe] Complete: " + _path);
    }
}
