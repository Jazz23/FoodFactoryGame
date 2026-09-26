// Records frame intervals in the isolated running host after customer visuals have settled.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodFactoryGame.Session.Customers;
using UnityEditor;
using UnityEngine;

public static class MeasureCustomerHost
{
    private static readonly List<float> Frames = new();
    private static double _started;
    private static int _lastFrame;
    private static int _visible;

    public static string Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var presenter = UnityEngine.Object.FindAnyObjectByType<CustomerPresenter>();
        _visible = presenter.VisibleCount;
        Frames.Clear();
        _started = EditorApplication.timeSinceStartup;
        _lastFrame = Time.frameCount;
        EditorApplication.update += Tick;
        return $"Sampling running host with {_visible} customer visuals: 1 s warmup, 4 s frames.";
    }

    private static void Tick()
    {
        var elapsed = EditorApplication.timeSinceStartup - _started;
        if (Time.frameCount != _lastFrame)
        {
            _lastFrame = Time.frameCount;
            if (elapsed >= 1 && elapsed < 5) Frames.Add(Time.unscaledDeltaTime * 1000);
        }
        if (elapsed < 5) return;
        EditorApplication.update -= Tick;
        var sorted = Frames.OrderBy(x => x).ToArray();
        var median = sorted[sorted.Length / 2];
        var p95 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)];
        var within = 100.0 * sorted.Count(x => x <= 1000f / 60f) / sorted.Length;
        var path = Path.Combine("docs", "verification", "artifacts", "customer-host-frame-probe-20260925.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (!File.Exists(path)) File.WriteAllText(path, "visible,frames,median_ms,p95_ms,under_16_67_percent\n");
        File.AppendAllText(path, $"{_visible},{sorted.Length},{median:F2},{p95:F2},{within:F1}\n");
        Debug.Log($"[CustomerHostProbe] visible={_visible} frames={sorted.Length} median={median:F2}ms p95={p95:F2}ms underBudget={within:F1}%");
    }
}
