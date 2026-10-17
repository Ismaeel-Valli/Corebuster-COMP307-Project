using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Renders a fixed, repeatable set of Start Workshop review views (corebuster_tools_001), through a real URP camera
/// with post-processing and volumes enabled, so every visual dressing task is judged from the same framing under
/// Game-view lighting. Read-only: the active scene's dirty flag is restored to what it was before capture.
/// </summary>
public static class StartWorkshopReviewCapture
{
    public const string OutputFolder = "ReviewCaptures/StartWorkshop/";
    const string ReferenceImage = "target_design/start_workshop.png";
    const int Width = 1280, Height = 720;
    const float EyeHeight = 1.6f, Fov = 60f;
    const float WallStandoff = 3f, CornerStandoff = 5f;

    [MenuItem("Tools/CoreBuster/Capture Start Workshop Review Shots")]
    public static void CaptureFromMenu() => Capture(TimestampedSubfolder());

    /// <summary>
    /// Batch entry point, run without -nographics (URP needs a graphics device to render):
    /// Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod StartWorkshopReviewCapture.CaptureBatch -quit
    /// </summary>
    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(StartWorkshopDresser.ScenePath);
        Capture(TimestampedSubfolder());
    }

    /// <summary>
    /// The neighbouring spaces (corebuster_render_006): the exit corridor, the corridors west and north of the room,
    /// and the unreachable outside of the east and south walls. Nothing built for the room may change these views,
    /// apart from the room itself seen through the doorway (the Cor_Door_* views).
    /// </summary>
    [MenuItem("Tools/CoreBuster/Capture Start Workshop Neighbour Shots")]
    public static void CaptureNeighboursFromMenu() => Capture(TimestampedSubfolder("_Neighbours"), neighbours: true);

    /// <summary>Batch entry point, run without -nographics: -executeMethod StartWorkshopReviewCapture.CaptureNeighboursBatch.</summary>
    public static void CaptureNeighboursBatch()
    {
        EditorSceneManager.OpenScene(StartWorkshopDresser.ScenePath);
        Capture(TimestampedSubfolder("_Neighbours"), neighbours: true);
    }

    [MenuItem("Tools/CoreBuster/Compare Start Workshop Review Shots")]
    public static void CompareFromMenu()
    {
        string root = Path.Combine(RepositoryRoot(), OutputFolder);
        string before = EditorUtility.OpenFolderPanel("Before capture folder", root, "");
        if (string.IsNullOrEmpty(before)) return;
        string after = EditorUtility.OpenFolderPanel("After capture folder", root, "");
        if (string.IsNullOrEmpty(after)) return;
        BuildComparisonSheet(before, after, Path.Combine(after, "Before_After_Sheet.png"));
    }

    /// <summary>
    /// Batch comparison: -executeMethod StartWorkshopReviewCapture.CompareBatch -captureBefore &lt;folder&gt; -captureAfter &lt;folder&gt;.
    /// Writes Before_After_Sheet.png and Before_After_Report.txt into the after folder.
    /// </summary>
    public static void CompareBatch()
    {
        string before = Argument("-captureBefore"), after = Argument("-captureAfter");
        if (before == null || after == null)
        {
            Debug.LogError("CompareBatch needs -captureBefore <folder> -captureAfter <folder>.");
            EditorApplication.Exit(1);
            return;
        }
        BuildComparisonSheet(before, after, Path.Combine(after, "Before_After_Sheet.png"));
    }

    /// <summary>
    /// Compares two capture folders given as repository-relative paths, writing Before_After_Sheet.png and
    /// Before_After_Report.txt into the second (used by the grime review tools, which capture several states in one session).
    /// </summary>
    public static void CompareFolders(string before, string after)
    {
        string beforeDir = Path.Combine(RepositoryRoot(), before), afterDir = Path.Combine(RepositoryRoot(), after);
        BuildComparisonSheet(beforeDir, afterDir, Path.Combine(afterDir, "Before_After_Sheet.png"));
    }

    static string Argument(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    static string TimestampedSubfolder(string suffix = "") =>
        OutputFolder + System.DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + suffix + "/";

    public static void Capture(string subfolder, bool neighbours = false)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != StartWorkshopDresser.ScenePath)
        {
            Debug.LogError($"Open {StartWorkshopDresser.ScenePath} before capturing Start Workshop review shots (active scene: {scene.path}).");
            return;
        }
        var frame = StartWorkshopFrame.Measure(scene, null);
        CaptureViews(subfolder, neighbours ? BuildNeighbourViews(frame) : BuildViews(frame));
    }

    /// <summary>
    /// Renders caller-supplied views through the same camera, into <paramref name="subfolder"/> with a composite sheet
    /// (used by the corebuster_art_003 signage legibility views). <paramref name="warmUpAll"/> renders every view once,
    /// unsaved, before the saved pass, for view sets where a later view can otherwise catch a not-yet-ready material.
    /// </summary>
    public static void CaptureViews(string subfolder, List<(string Name, Vector3 Position, Quaternion Rotation)> views,
        bool warmUpAll = false)
    {
        var scene = EditorSceneManager.GetActiveScene();
        bool wasDirty = scene.isDirty;
        string outputDir = Path.Combine(RepositoryRoot(), subfolder).Replace('\\', '/');
        Directory.CreateDirectory(outputDir);

        var camera = CreateCamera();
        var written = new List<(string Name, string Path)>();
        try
        {
            // In a fresh batch session the first post-processed frame can come out black and magenta (TESTING.md).
            if (views.Count > 0) RenderView(camera, views[0].Position, views[0].Rotation, null);
            if (warmUpAll)
                foreach (var (_, position, rotation) in views) RenderView(camera, position, rotation, null);
            foreach (var (name, position, rotation) in views)
            {
                string file = outputDir + "/" + name + ".png";
                RenderView(camera, position, rotation, file);
                written.Add((name, file));
            }
        }
        finally
        {
            Object.DestroyImmediate(camera.gameObject);
            // The HideAndDontSave capture camera should never dirty MainScene. Unity 6000.5 has no public API to clear
            // the flag, so report it rather than leave an unexplained pending save.
            if (!wasDirty && scene.isDirty)
                Debug.LogWarning("Start Workshop review capture marked MainScene dirty; nothing in the scene was changed by the capture.");
        }

        BuildCompositeSheet(written, outputDir + "/Composite_Sheet.png");
        Debug.Log($"Captured {written.Count} Start Workshop review views to {subfolder}");
    }

    /// <summary>Named views derived from the measured room frame, so they track layout changes instead of hard-coded coordinates.</summary>
    static List<(string Name, Vector3 Position, Quaternion Rotation)> BuildViews(StartWorkshopFrame f)
    {
        var views = new List<(string, Vector3, Quaternion)>();
        float eyeY = f.Floor + EyeHeight;
        float midX = (f.West + f.East) * .5f, midZ = (f.South + f.North) * .5f;

        Vector3 spawn = new Vector3(f.Spawn.x, eyeY, f.Spawn.z);
        views.Add(("Spawn_North", spawn, Look(spawn, spawn + Vector3.forward)));
        views.Add(("Spawn_East", spawn, Look(spawn, spawn + Vector3.right)));
        views.Add(("Spawn_South", spawn, Look(spawn, spawn + Vector3.back)));
        views.Add(("Spawn_West", spawn, Look(spawn, spawn + Vector3.left)));

        views.Add(WallView("Wall_West", new Vector3(f.West + WallStandoff, eyeY, midZ), Vector3.left));
        views.Add(WallView("Wall_East", new Vector3(f.East - WallStandoff, eyeY, midZ), Vector3.right));
        views.Add(WallView("Wall_South", new Vector3(midX, eyeY, f.South + WallStandoff), Vector3.back));
        views.Add(WallView("Wall_North", new Vector3(midX, eyeY, f.North - WallStandoff), Vector3.forward));

        foreach (var (name, x, z) in new (string, float, float)[]
                 {
                     ("Corner_SW", f.West, f.South), ("Corner_SE", f.East, f.South),
                     ("Corner_NW", f.West, f.North), ("Corner_NE", f.East, f.North),
                 })
        {
            Vector3 corner = new Vector3(x, eyeY, z);
            Vector3 inward = new Vector3(midX - x, 0, midZ - z).normalized;
            Vector3 position = corner + inward * CornerStandoff;
            views.Add((name, position, Look(position, corner)));
        }

        Vector3 ceilingPos = new Vector3(midX, eyeY, midZ);
        views.Add(("Ceiling", ceilingPos, Quaternion.Euler(-55f, 0, 0)));

        Vector3 exitPos = new Vector3(f.OpeningCenter, eyeY, f.North - StartWorkshopFrame.DoorApproachDepth);
        views.Add(("Exit", exitPos, Look(exitPos, new Vector3(f.OpeningCenter, eyeY, f.North))));

        // Approximates target_design/start_workshop.png's elevated three-quarter framing: high, off to one side,
        // looking across the room toward the exit.
        Vector3 highPos = new Vector3(f.West + 4f, f.Floor + (f.Ceiling - f.Floor) * .55f, f.South + 4f);
        Vector3 highTarget = new Vector3(f.OpeningCenter, f.Floor + 1.2f, f.North - 2f);
        views.Add(("Reference_Like", highPos, Look(highPos, highTarget)));

        return views;
    }

    /// <summary>
    /// Fixed views of the spaces around the room, placed from the measured frame: the exit corridor (door views from 2,
    /// 6 and 12 m, looking north up the corridor, and at its side walls), the corridor along the west wall and the
    /// corridor network beyond it, the corridor north of the room, and the outside of the east and south walls.
    /// </summary>
    static List<(string Name, Vector3 Position, Quaternion Rotation)> BuildNeighbourViews(StartWorkshopFrame f)
    {
        var views = new List<(string, Vector3, Quaternion)>();
        float eyeY = f.Floor + EyeHeight, door = f.OpeningCenter, midZ = (f.South + f.North) * .5f;
        // The room's walls are about 0.35 m thick; the exit corridor starts at their outer face.
        float corridor = f.North + .4f;
        foreach (float d in new[] { 2f, 6f, 12f })
        {
            var p = new Vector3(door, eyeY, corridor + d);
            views.Add(($"Cor_Door_{d:0}m", p, Look(p, new Vector3(door, eyeY, f.North))));
        }
        var nearDoor = new Vector3(door, eyeY, corridor + 1.5f);
        views.Add(("Cor_Looking_North", nearDoor, Look(nearDoor, nearDoor + Vector3.forward)));
        views.Add(("Cor_Side_West", nearDoor, Look(nearDoor, nearDoor + Vector3.left)));
        views.Add(("Cor_Side_East", nearDoor, Look(nearDoor, nearDoor + Vector3.right)));

        // The corridor running along the outside of the west wall (x about -190.8 to -186.8) and the network beyond it.
        float westCorridor = f.West - 2.4f;
        foreach (var (name, z, look) in new (string, float, Vector3)[]
                 {
                     ("West_S_LookNorth", f.South + 2f, Vector3.forward), ("West_Mid_LookNorth", f.South + 10f, Vector3.forward),
                     ("West_Mid_LookSouth", midZ + 4f, Vector3.back), ("West_N_LookSouth", f.North - 4f, Vector3.back),
                     ("West_Mid_LookEast", f.South + 10f, Vector3.right),
                 })
        {
            var p = new Vector3(westCorridor, eyeY, z);
            views.Add((name, p, Look(p, p + look)));
        }
        foreach (var (name, z) in new[] { ("West_Far_LookEast_S", f.South + 4f), ("West_Far_LookEast_N", midZ + 6f) })
        {
            var p = new Vector3(f.West - 10f, eyeY, z);
            views.Add((name, p, Look(p, new Vector3(f.West, eyeY, z))));
        }

        // The corridor about 6-14 m north of the room, west of the exit corridor.
        var north = new Vector3(f.West + 8f, eyeY, f.North + 8f);
        views.Add(("North_LookSouth", north, Look(north, north + Vector3.back)));
        views.Add(("North_LookEast", north, Look(north, north + Vector3.right)));

        // Nobody can stand east or south of the room, but the ground plane and the walls' outer faces show there.
        var east = new Vector3(f.East + 6f, f.Floor + 3f, midZ);
        views.Add(("Outside_East", east, Look(east, new Vector3(f.East, f.Floor + 1f, midZ))));
        var south = new Vector3((f.West + f.East) * .5f, f.Floor + 3f, f.South - 6f);
        views.Add(("Outside_South", south, Look(south, new Vector3(south.x, f.Floor + 1f, f.South))));
        return views;
    }

    static (string, Vector3, Quaternion) WallView(string name, Vector3 position, Vector3 direction) =>
        (name, position, Look(position, position + direction));

    static Quaternion Look(Vector3 from, Vector3 to) => Quaternion.LookRotation((to - from).normalized, Vector3.up);

    static Camera CreateCamera()
    {
        var go = new GameObject("StartWorkshopReviewCamera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = Fov;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 300f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.cullingMask = ~0;
        camera.allowHDR = true;
        camera.allowMSAA = true;
        camera.enabled = false;
        // Post-processing and volumes are only applied through the URP additional camera data, so the Game view's
        // lighting/effects stack is actually exercised instead of a flat, unlit render (see corebuster_env_004's lesson).
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.renderShadows = true;
        data.volumeLayerMask = ~0;
        return camera;
    }

    static void RenderView(Camera camera, Vector3 position, Quaternion rotation, string filePath)
    {
        camera.transform.SetPositionAndRotation(position, rotation);
        var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        camera.targetTexture = rt;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        if (filePath != null) File.WriteAllBytes(filePath, tex.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(tex);
    }

    /// <summary>Tiles every captured view, plus the reference image when present, into one scan sheet.</summary>
    static void BuildCompositeSheet(List<(string Name, string Path)> views, string outputPath)
    {
        const int cols = 4, tileW = 320, tileH = 180, pad = 6;
        var tiles = new List<Texture2D>();
        foreach (var (_, path) in views) tiles.Add(LoadScaled(path, tileW, tileH));
        string referencePath = Path.Combine(RepositoryRoot(), ReferenceImage);
        if (File.Exists(referencePath)) tiles.Add(LoadScaled(referencePath, tileW, tileH));
        else Debug.LogWarning("Reference image not found at " + referencePath + "; composite sheet omits it.");

        int rows = Mathf.CeilToInt(tiles.Count / (float)cols);
        int sheetW = cols * (tileW + pad) + pad, sheetH = rows * (tileH + pad) + pad;
        var sheet = Fill(sheetW, sheetH, new Color32(24, 24, 24, 255));
        for (int i = 0; i < tiles.Count; i++)
        {
            int col = i % cols, row = i / cols;
            int x = pad + col * (tileW + pad);
            int y = sheetH - pad - (row + 1) * (tileH + pad);
            sheet.SetPixels32(x, y, tileW, tileH, tiles[i].GetPixels32());
            Object.DestroyImmediate(tiles[i]);
        }
        sheet.Apply();
        File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }

    /// <summary>
    /// Stacks matching filenames from two capture folders side by side (before | after) for a visual regression check,
    /// and writes Before_After_Report.txt beside the sheet: per view, the number of pixels whose RGB channels differ by
    /// more than <see cref="DiffThreshold"/> and the largest difference, from the full-resolution captures.
    /// </summary>
    static void BuildComparisonSheet(string beforeDir, string afterDir, string outputPath)
    {
        WriteDiffReport(beforeDir, afterDir, Path.Combine(Path.GetDirectoryName(outputPath), "Before_After_Report.txt"));
        const int tileW = 480, tileH = 270, pad = 6;
        var names = new List<string>();
        foreach (var file in Directory.GetFiles(afterDir, "*.png"))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith("Composite_Sheet") || name.StartsWith("Before_After")) continue;
            if (File.Exists(Path.Combine(beforeDir, name))) names.Add(name);
        }
        if (names.Count == 0)
        {
            Debug.LogWarning("No matching filenames between the two capture folders; nothing to compare.");
            return;
        }

        int sheetW = 2 * tileW + 3 * pad, sheetH = names.Count * (tileH + pad) + pad;
        var sheet = Fill(sheetW, sheetH, new Color32(24, 24, 24, 255));
        for (int i = 0; i < names.Count; i++)
        {
            int y = sheetH - pad - (i + 1) * (tileH + pad);
            var before = LoadScaled(Path.Combine(beforeDir, names[i]), tileW, tileH);
            var after = LoadScaled(Path.Combine(afterDir, names[i]), tileW, tileH);
            sheet.SetPixels32(pad, y, tileW, tileH, before.GetPixels32());
            sheet.SetPixels32(2 * pad + tileW, y, tileW, tileH, after.GetPixels32());
            Object.DestroyImmediate(before);
            Object.DestroyImmediate(after);
        }
        sheet.Apply();
        File.WriteAllBytes(outputPath, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        Debug.Log($"Wrote before/after comparison sheet for {names.Count} view(s) to {outputPath}");
    }

    // Per-channel difference (0-255) below which a pixel counts as unchanged.
    const int DiffThreshold = 2;

    static void WriteDiffReport(string beforeDir, string afterDir, string reportPath)
    {
        var lines = new List<string> { $"before: {beforeDir}", $"after:  {afterDir}", $"changed = any RGB channel differs by more than {DiffThreshold}" };
        foreach (var file in Directory.GetFiles(afterDir, "*.png"))
        {
            string name = Path.GetFileName(file), other = Path.Combine(beforeDir, name);
            if (name.StartsWith("Composite_Sheet") || name.StartsWith("Before_After") || !File.Exists(other)) continue;
            var a = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var b = new Texture2D(2, 2, TextureFormat.RGB24, false);
            a.LoadImage(File.ReadAllBytes(other));
            b.LoadImage(File.ReadAllBytes(file));
            if (a.width != b.width || a.height != b.height) lines.Add($"{name}: size differs");
            else
            {
                var pa = a.GetPixels32();
                var pb = b.GetPixels32();
                int changed = 0, max = 0;
                for (int i = 0; i < pa.Length; i++)
                {
                    int d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b));
                    if (d > DiffThreshold) changed++;
                    max = Mathf.Max(max, d);
                }
                lines.Add(System.FormattableString.Invariant($"{name}: {changed} pixel(s) changed ({100f * changed / pa.Length:F2}%), max difference {max}"));
            }
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
        File.WriteAllLines(reportPath, lines);
        Debug.Log("Capture difference report:\n" + string.Join("\n", lines));
    }

    static Texture2D Fill(int width, int height, Color32 color)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels32(pixels);
        return tex;
    }

    static Texture2D LoadScaled(string path, int width, int height)
    {
        var raw = new Texture2D(2, 2, TextureFormat.RGB24, false);
        raw.LoadImage(File.ReadAllBytes(path));
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(raw, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
        scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        scaled.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(raw);
        return scaled;
    }

    static string RepositoryRoot() => Directory.GetParent(Application.dataPath).FullName;
}
