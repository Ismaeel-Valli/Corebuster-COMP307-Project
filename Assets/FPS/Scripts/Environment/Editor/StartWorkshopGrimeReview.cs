using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>
/// corebuster_render_002 evidence tools. All three run in the Editor and toggle the decal feature and the grime group only
/// for the time they take, restoring both afterwards; neither saves the scene.
/// <list type="bullet">
/// <item><b>Capture</b>: the standard review views in three states (feature off; feature on, grime off; grime on), before/after
/// comparison reports for each step, and close-ups of one decal of each kind and grazing floor views.</item>
/// <item><b>Neighbour proof</b>: the corridor and outside views with the feature off, then with the feature and grime on. The
/// global renderer change must leave every neighbouring view unchanged; only the room seen through the doorway may differ.</item>
/// <item><b>Cost</b>: the milliseconds a real URP camera needs per frame in the same three states, from three views.</item>
/// </list>
/// Toggling the feature's active flag edits the renderer asset in memory only; the flag is put back and nothing is saved.
/// </summary>
public static class StartWorkshopGrimeReview
{
    const string Folder = StartWorkshopReviewCapture.OutputFolder + "Grime_render002/";

    [MenuItem("Tools/CoreBuster/Capture Start Workshop Grime")]
    public static void CaptureFromMenu() => Capture(EditorSceneManager.GetActiveScene());

    /// <summary>Batch, without -nographics: -executeMethod StartWorkshopGrimeReview.CaptureBatch.</summary>
    public static void CaptureBatch() => Capture(EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single));

    [MenuItem("Tools/CoreBuster/Prove Grime Feature Leaves Neighbours Unchanged")]
    public static void NeighbourProofFromMenu() => NeighbourProof(EditorSceneManager.GetActiveScene());

    /// <summary>Batch, without -nographics: -executeMethod StartWorkshopGrimeReview.NeighbourProofBatch.</summary>
    public static void NeighbourProofBatch() => NeighbourProof(EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single));

    [MenuItem("Tools/CoreBuster/Measure Start Workshop Grime Cost")]
    public static void MeasureFromMenu() => Measure(EditorSceneManager.GetActiveScene());

    /// <summary>Batch, without -nographics: -executeMethod StartWorkshopGrimeReview.MeasureBatch.</summary>
    public static void MeasureBatch() => Measure(EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single));

    // --- State ---------------------------------------------------------------------------------------------------------

    /// <summary>Switches the decal feature and the grime group, and puts both back exactly as found when disposed.</summary>
    sealed class Toggle : IDisposable
    {
        readonly DecalRendererFeature _feature;
        readonly GameObject _group;
        readonly bool _featureWas, _groupWas;

        public Toggle(DecalRendererFeature feature, GameObject group)
        {
            _feature = feature;
            _group = group;
            _featureWas = feature.isActive;
            _groupWas = group.activeSelf;
        }

        public void Set(bool featureOn, bool grimeOn)
        {
            _feature.SetActive(featureOn);
            _group.SetActive(grimeOn);
        }

        public void Dispose()
        {
            _feature.SetActive(_featureWas);
            _group.SetActive(_groupWas);
        }
    }

    static bool Prepare(Scene scene, out Transform root, out Transform group, out DecalRendererFeature feature, out StartWorkshopFrame f)
    {
        root = group = null;
        feature = null;
        f = null;
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Open {ScenePath} first (active scene: {scene.path}).");
            return false;
        }
        var level = FindLevel(scene);
        root = level != null ? level.Find(ContainerName) : null;
        group = root != null ? root.Find(StartWorkshopGrime.GroupName) : null;
        feature = UrpDecalFeatureSetup.Find(UrpDecalFeatureSetup.LoadRenderer());
        if (group == null || feature == null)
        {
            Debug.LogError("Needs the grime group and the URP decal feature: run Tools/CoreBuster/Enable URP Decal Renderer Feature and Build Start Workshop Grime first.");
            return false;
        }
        f = StartWorkshopFrame.Measure(scene, root);
        return true;
    }

    // --- Capture -------------------------------------------------------------------------------------------------------

    static void Capture(Scene scene)
    {
        if (!Prepare(scene, out var root, out var group, out var feature, out var f)) return;
        using var toggle = new Toggle(feature, group.gameObject);

        string featureOff = Folder + "Room_1_FeatureOff/", grimeOff = Folder + "Room_2_GrimeOff/", grimeOn = Folder + "Room_3_GrimeOn/";
        toggle.Set(false, false);
        StartWorkshopReviewCapture.Capture(featureOff);
        toggle.Set(true, false);
        StartWorkshopReviewCapture.Capture(grimeOff);
        toggle.Set(true, true);
        StartWorkshopReviewCapture.Capture(grimeOn);
        StartWorkshopReviewCapture.CaptureViews(Folder + "Closeups/", CloseupViews(group, f));
        StartWorkshopReviewCapture.CompareFolders(featureOff, grimeOff);
        StartWorkshopReviewCapture.CompareFolders(grimeOff, grimeOn);
        Debug.Log($"Grime capture done in {Folder}. Room_2_GrimeOff vs Room_1_FeatureOff should differ by nothing but noise (the feature alone paints nothing); Room_3_GrimeOn vs Room_2_GrimeOff shows the grime.");
    }

    /// <summary>One close-up of the first decal of each kind, and low, grazing floor views (z-fighting and bleed show there).</summary>
    static List<(string Name, Vector3 Position, Quaternion Rotation)> CloseupViews(Transform group, StartWorkshopFrame f)
    {
        var views = new List<(string, Vector3, Quaternion)>();
        var seen = new HashSet<string>();
        Vector3 centre = f.Center;
        foreach (Transform child in group)
        {
            if (!seen.Add(StartWorkshopGrime.KindOf(child.name))) continue;
            Vector3 target = child.position, forward = child.forward;
            bool onFloor = forward.y < -.5f;
            Vector3 eye;
            if (onFloor)
            {
                Vector3 away = new Vector3(centre.x - target.x, 0, centre.z - target.z).normalized;
                eye = target + away * 2.6f;
            }
            else eye = target - forward * 3.2f;
            eye.y = Mathf.Clamp(onFloor ? f.Floor + 1.6f : target.y, f.Floor + 1.4f, f.Floor + 2f);
            views.Add(($"Close_{child.name}", eye, Quaternion.LookRotation((target - eye).normalized, Vector3.up)));
        }
        Vector3 spawn = f.Spawn;
        foreach (var (name, dir) in new[] { ("Low_Route_North", Vector3.forward), ("Low_West", Vector3.left) })
        {
            var eye = new Vector3(spawn.x, f.Floor + .15f, spawn.z);
            views.Add((name, eye, Quaternion.LookRotation(dir, Vector3.up)));
        }
        return views;
    }

    // --- Neighbour proof -----------------------------------------------------------------------------------------------

    static void NeighbourProof(Scene scene)
    {
        if (!Prepare(scene, out _, out var group, out var feature, out _)) return;
        using var toggle = new Toggle(feature, group.gameObject);
        string off = Folder + "Neighbours_1_FeatureOff/", on = Folder + "Neighbours_2_GrimeOn/";
        toggle.Set(false, false);
        StartWorkshopReviewCapture.Capture(off, neighbours: true);
        toggle.Set(true, true);
        StartWorkshopReviewCapture.Capture(on, neighbours: true);
        StartWorkshopReviewCapture.CompareFolders(off, on);
        Debug.Log($"Neighbour proof done in {Folder}. Read Neighbours_2_GrimeOn/Before_After_Report.txt: every view except the Cor_Door_* views (which see into the room) must show 0 changed pixels, apart from the animated sky.");
    }

    // --- Other build scenes ---------------------------------------------------------------------------------------------

    static readonly string[] OtherScenes =
    {
        "Assets/FPS/Scenes/IntroMenu.unity", "Assets/FPS/Scenes/WinScene.unity", "Assets/FPS/Scenes/LoseScene.unity",
    };

    /// <summary>
    /// Batch, without -nographics: -executeMethod StartWorkshopGrimeReview.OtherScenesBatch. The renderer is shared by every
    /// scene, so this opens each of the other build scenes, renders its first camera through the real pipeline with the decal
    /// feature off and then on, and reports the pixels that differ (there are no decals there, so none should). Writes
    /// Other_Scenes.txt beside the other grime evidence and exits nonzero if any scene differs or logs an error.
    /// </summary>
    public static void OtherScenesBatch()
    {
        var feature = UrpDecalFeatureSetup.Find(UrpDecalFeatureSetup.LoadRenderer());
        if (feature == null) { Debug.LogError("The URP decal feature is not installed."); EditorApplication.Exit(1); return; }
        bool was = feature.isActive, failed = false;
        var report = new StringBuilder().AppendLine("Other build scenes with the URP decal feature off vs on (corebuster_render_002)");
        try
        {
            foreach (string path in OtherScenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var camera = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.enabled && c.gameObject.activeInHierarchy);
                if (camera == null) { report.AppendLine($"{path}: no enabled camera; nothing to render."); continue; }
                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                var previousTarget = camera.targetTexture;
                camera.targetTexture = rt;
                try
                {
                    Color32[] Render(bool featureOn)
                    {
                        feature.SetActive(featureOn);
                        for (int i = 0; i < 3; i++) camera.Render();
                        var previous = RenderTexture.active;
                        RenderTexture.active = rt;
                        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                        RenderTexture.active = previous;
                        var pixels = tex.GetPixels32();
                        UnityEngine.Object.DestroyImmediate(tex);
                        return pixels;
                    }
                    var off = Render(false);
                    var on = Render(true);
                    int changed = 0, max = 0;
                    for (int i = 0; i < off.Length; i++)
                    {
                        int d = Mathf.Max(Mathf.Abs(off[i].r - on[i].r), Mathf.Abs(off[i].g - on[i].g), Mathf.Abs(off[i].b - on[i].b));
                        if (d > 2) changed++;
                        max = Mathf.Max(max, d);
                    }
                    report.AppendLine(System.FormattableString.Invariant($"{path}: camera '{camera.name}', {changed} pixel(s) changed, max difference {max}."));
                    if (changed > 0) failed = true;
                }
                finally
                {
                    camera.targetTexture = previousTarget;
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }
        finally { feature.SetActive(was); }
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Other_Scenes.txt"), report.ToString());
        Debug.Log(report.ToString());
        if (failed) EditorApplication.Exit(1);
    }

    // --- Cost ----------------------------------------------------------------------------------------------------------

    const int Rounds = 3, WarmUp = 6, Frames = 30, Width = 1280, Height = 720;

    static void Measure(Scene scene)
    {
        if (!Prepare(scene, out var root, out var group, out var feature, out var f)) return;
        var views = new List<(string Name, Vector3 Position, Quaternion Rotation)>();
        float eyeY = f.Floor + 1.6f;
        Vector3 spawn = new Vector3(f.Spawn.x, eyeY, f.Spawn.z);
        views.Add(("Spawn_North", spawn, Quaternion.LookRotation(Vector3.forward, Vector3.up)));
        Vector3 east = new Vector3(f.East - 3f, eyeY, (f.South + f.North) * .5f);
        views.Add(("Wall_East", east, Quaternion.LookRotation(Vector3.right, Vector3.up)));
        Vector3 high = new Vector3(f.West + 4f, f.Floor + (f.Ceiling - f.Floor) * .55f, f.South + 4f);
        views.Add(("Reference_Like", high, Quaternion.LookRotation((new Vector3(f.OpeningCenter, f.Floor + 1.2f, f.North - 2f) - high).normalized, Vector3.up)));

        var configs = new (string Name, bool Feature, bool Grime)[]
        {
            ("A feature off", false, false), ("B feature on, grime off", true, false), ("C feature on, grime on", true, true),
        };
        var samples = new Dictionary<(int config, int view), List<double>>();
        using var toggle = new Toggle(feature, group.gameObject);
        var camera = CreateCamera();
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt;
            for (int round = 0; round < Rounds; round++)
                for (int c = 0; c < configs.Length; c++)
                {
                    toggle.Set(configs[c].Feature, configs[c].Grime);
                    for (int v = 0; v < views.Count; v++)
                    {
                        camera.transform.SetPositionAndRotation(views[v].Position, views[v].Rotation);
                        for (int i = 0; i < WarmUp; i++) RenderSynced(camera, rt, pixel);
                        if (!samples.TryGetValue((c, v), out var list)) samples[(c, v)] = list = new List<double>();
                        for (int i = 0; i < Frames; i++)
                        {
                            var watch = System.Diagnostics.Stopwatch.StartNew();
                            RenderSynced(camera, rt, pixel);
                            list.Add(watch.Elapsed.TotalMilliseconds);
                        }
                    }
                }
        }
        finally
        {
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(pixel);
        }

        var report = new StringBuilder();
        report.AppendLine("Start Workshop grime frame cost (corebuster_render_002)");
        report.AppendLine($"Editor camera renders at {Width}x{Height}, each synchronised with a 1-pixel GPU readback; {Rounds} interleaved rounds x {Frames} frames per view after {WarmUp} warm-up frames.");
        report.AppendLine($"Graphics device: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}). Decal count: {group.childCount}. Editor measurements include editor overhead; only the differences between rows are meaningful.");
        report.AppendLine();
        report.AppendLine("view | config | mean ms | median ms | p95 ms");
        var means = new double[configs.Length];
        for (int c = 0; c < configs.Length; c++)
        {
            for (int v = 0; v < views.Count; v++)
            {
                var s = samples[(c, v)].OrderBy(x => x).ToList();
                double mean = s.Average();
                means[c] += mean / views.Count;
                report.AppendLine(FormattableString.Invariant($"{views[v].Name} | {configs[c].Name} | {mean:F2} | {s[s.Count / 2]:F2} | {s[(int)(s.Count * .95)]:F2}"));
            }
        }
        report.AppendLine();
        report.AppendLine(FormattableString.Invariant($"Mean over views: A {means[0]:F2} ms, B {means[1]:F2} ms, C {means[2]:F2} ms."));
        report.AppendLine(FormattableString.Invariant($"Cost of the renderer feature alone (B - A): {means[1] - means[0]:+0.00;-0.00} ms per frame."));
        report.AppendLine(FormattableString.Invariant($"Cost of the grime decals (C - B): {means[2] - means[1]:+0.00;-0.00} ms per frame."));
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Frame_Cost.txt"), report.ToString());
        Debug.Log(report.ToString());
    }

    static void RenderSynced(Camera camera, RenderTexture rt, Texture2D pixel)
    {
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        RenderTexture.active = previous;
    }

    static Camera CreateCamera()
    {
        var go = new GameObject("StartWorkshopGrimeCostCamera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 60f;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 300f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.cullingMask = ~0;
        camera.allowHDR = true;
        camera.enabled = false;
        // The Game view's stack: post-processing, shadows and volumes (the room's local grade) all run.
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.renderShadows = true;
        data.volumeLayerMask = ~0;
        return camera;
    }
}
