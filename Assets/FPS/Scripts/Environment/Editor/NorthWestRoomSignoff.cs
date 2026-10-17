using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// corebuster_env_043 read-only sign-off. Runs the env_040 density gate with requireTargets, the env_036 rendering and
/// env_037 lighting validators, renders the review views with scene lighting only (walls from the centre and each
/// secondary open space, east door, both terminals, a top-down measurement view and two Start Workshop views), measures
/// their colour, and writes side-by-side sheets against both references. Saves nothing; fails if MainScene changes.
/// </summary>
public static class NorthWestRoomSignoff
{
    const string Output = "ReviewCaptures/NorthWestRoom/043";
    const string DensityReference = "target_design/north_west_room_density_target.png", WorkshopReference = "target_design/start_workshop.png";
    const int Width = 960, Height = 540, MapSize = 1024;
    // Near-black: sRGB luma below 0.08 (about 20/255). Teal cast: blue and green both clearly above red.
    const float NearBlack = .08f;

    [MenuItem("Tools/CoreBuster/Review North-West Room Sign-off")]
    public static void ReviewMenu() => Review();

    /// <summary>Batch entry point (graphics required; omit -nographics).</summary>
    public static void ReviewBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        Review();
    }

    static void Review()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try { Run(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    static void Run()
    {
        string before = Hash(NorthWestRoomSurvey.ScenePath);
        var scene = SceneManager.GetActiveScene();
        var layout = NorthWestRoomFramework.MeasureLayout();
        Directory.CreateDirectory(Output + "/Views");
        Directory.CreateDirectory(Output + "/Sheets");
        var lines = new List<string> { "corebuster_env_043 sign-off, scene " + before, "" };

        // ---- Measured gates.
        var density = NorthWestRoomDensity.Measure(scene, layout);
        File.WriteAllText(Output + "/Density.json", JsonUtility.ToJson(density, true));
        bool densityGate = NorthWestRoomDensity.Check(scene, layout, true, true);
        lines.Add($"DENSITY GATE (requireTargets): {(densityGate ? "PASS" : "FAIL")}");
        lines.Add($"  Occupied wall line {density.occupiedWallLine:P1} (body {density.bodyOccupiedWallLine:P1}); " +
            string.Join(", ", density.walls.Select(w => $"{w.wall} {w.occupiedFraction:P0}/body {w.bodyOccupiedFraction:P0}")));
        lines.Add($"  Clear floor {density.clearFloorFraction:P1} ({density.clearFloorArea:F1} of {density.interiorArea:F1} m2)");
        lines.Add("  Open spaces: " + string.Join(", ", density.openSpaces.Select(s => $"{s.id} {s.clearArea:F0} m2 (plan {s.projectedClearArea:F0})")));
        lines.Add("  Lanes: " + string.Join(", ", density.lanes.Where(l => !l.connector).Select(l => $"{l.id} {l.width:F2} m")) +
            $"; narrowest connector {density.minConnectorWidth:F2} m at " + density.lanes.Where(l => l.connector).OrderBy(l => l.width).Select(l => l.name).FirstOrDefault());
        lines.Add($"  Loops clear {density.clearLoops}/{density.loops.Count}");
        lines.Add("  Budgets: " + string.Join(", ", density.budgets.Select(b => $"{b.name} {b.used}/{b.proposed}")));
        lines.AddRange(density.errors.Select(e => "  ERROR: " + e));
        lines.AddRange(density.targetsNotMet.Select(e => "  TARGET: " + e));
        lines.Add("LIGHTING VALIDATOR (env_037): " + Attempt(NorthWestRoomLighting.ValidateMenu));
        lines.Add("RENDERING VALIDATOR (env_036): " + Attempt(NorthWestRoomRendering.ValidateMenu));
        lines.Add("");

        // ---- Captures with scene lighting only.
        var views = Views(layout);
        var stats = new List<string> { "view,meanLuma,nearBlack,tealCast,warmth(R-B),saturation" };
        var camera = MakeCamera();
        try
        {
            foreach (var v in views)
            {
                var pixels = Shoot(camera, v.position, v.target, Width, Height, false, 0);
                Save(Output + "/Views/" + v.name + ".png", pixels, Width, Height);
                stats.Add(v.name + "," + Stats(pixels));
            }
            var map = Shoot(camera, Vector3.zero, Vector3.zero, MapSize, MapSize, true, layout.interior.size.z * .5f + .3f, layout);
            Save(Output + "/Views/TopDown_Raw.png", map, MapSize, MapSize);
            Annotate(map, layout);
            Save(Output + "/Views/TopDown_Measured.png", map, MapSize, MapSize);
        }
        finally { Object.DestroyImmediate(camera.gameObject); }
        foreach (var reference in new[] { WorkshopReference, DensityReference })
            if (File.Exists(reference))
            {
                var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(reference));
                stats.Add("REF " + Path.GetFileNameWithoutExtension(reference) + "," + Stats(t.GetPixels32()));
                Object.DestroyImmediate(t);
            }
        File.WriteAllLines(Output + "/Colour.csv", stats);
        Sheets(views.Select(v => v.name).Where(n => !n.StartsWith("Workshop_")));

        string after = Hash(NorthWestRoomSurvey.ScenePath);
        if (scene.isDirty || before != after) throw new InvalidOperationException($"Sign-off changed MainScene (before {before}, after {after}, dirty {scene.isDirty}).");
        lines.Add($"{views.Count} views + top-down (raw and measured) in Views/, sheets in Sheets/, colour in Colour.csv. Scene unchanged.");
        File.WriteAllLines(Output + "/Signoff.txt", lines);
        Debug.Log("NORTH-WEST ROOM SIGN-OFF REVIEW WRITTEN\n" + string.Join("\n", lines));
    }

    static string Attempt(Action check)
    {
        try { check(); return "PASS"; }
        catch (Exception e) { return "FAIL: " + e.Message.Split('\n')[0]; }
    }

    static List<(string name, Vector3 position, Vector3 target)> Views(NorthWestRoomFramework.Layout layout)
    {
        var r = layout.report; float eye = r.playerEyeHeight, floor = layout.Floor;
        var interior = layout.interior;
        var walls = new (string name, Func<Vector3, Vector3> aim)[]
        {
            ("North", p => new Vector3(p.x, floor + 3f, interior.max.z)),
            ("South", p => new Vector3(p.x, floor + 3f, interior.min.z)),
            ("East", p => new Vector3(interior.max.x, floor + 3f, p.z)),
            ("West", p => new Vector3(interior.min.x, floor + 3f, p.z)),
        };
        var views = new List<(string, Vector3, Vector3)>();
        Vector3 centre = new Vector3(r.centre.x, floor + eye, r.centre.z);
        foreach (var w in walls) views.Add(("Centre_to_" + w.name, centre, w.aim(centre)));
        foreach (var space in NorthWestRoomDensity.LoadPlan().openSpaces.Where(s => s.polygon != null && s.polygon.Length > 0))
        {
            var p = new Vector3(space.polygon.Average(v => v.x), floor + eye, space.polygon.Average(v => v.z));
            foreach (var w in walls)
            {
                var target = w.aim(p);
                // From a pocket the near walls are close; look along them so the view still shows a wall run.
                if (new Vector2(target.x - p.x, target.z - p.z).magnitude < 6f) target = Vector3.Lerp(target, new Vector3(r.centre.x, target.y, r.centre.z), .5f);
                views.Add((space.id + "_to_" + w.name, p, target));
            }
        }
        var east = r.gates.Single(g => g.name == NorthWestRoomSurvey.EastDoorName);
        views.Add(("East_Door", east.position - east.outward * 1.2f + Vector3.up * eye, new Vector3(r.centre.x, floor + 2f, r.centre.z)));
        views.Add(("Red_Terminal_Into_Room", new Vector3(r.redTerminal.standing.position.x, r.redTerminal.eyeY, r.redTerminal.standing.position.z),
            new Vector3(r.centre.x, floor + 2f, r.centre.z)));
        views.Add(("Master_Terminal_Into_Room", new Vector3(r.masterTerminal.standing.position.x, r.masterTerminal.eyeY, r.masterTerminal.standing.position.z),
            new Vector3(r.centre.x, floor + 2f, r.centre.z)));
        // In-game Start Workshop, for the colour comparison (measured interior X [-186.43, -154.51], Z [2.43, 34.20]; SYSTEM_BOUNDS.md).
        views.Add(("Workshop_SW_to_Exit", new Vector3(-182f, floor + eye, 6.5f), new Vector3(-164f, floor + 2.5f, 34f)));
        views.Add(("Workshop_Spawn_to_West", new Vector3(-167.98f, floor + eye, 17.96f), new Vector3(-186f, floor + 2.5f, 20f)));
        return views;
    }

    static Camera MakeCamera()
    {
        var go = new GameObject("Temporary Room 3 sign-off camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 70; camera.nearClipPlane = .05f; camera.farClipPlane = 300;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        return camera;
    }

    /// <summary>Perspective eye view, or (ortho) a top-down view from just under the ceiling that clips everything above floor + 6 m.</summary>
    static Color32[] Shoot(Camera camera, Vector3 position, Vector3 target, int width, int height, bool ortho, float size, NorthWestRoomFramework.Layout layout = null)
    {
        camera.orthographic = ortho;
        if (ortho)
        {
            var c = layout.interior.center;
            float y = layout.report.ceilingUndersideY - .05f;
            camera.transform.SetPositionAndRotation(new Vector3(c.x, y, c.z), Quaternion.LookRotation(Vector3.down, Vector3.forward));
            camera.orthographicSize = size; camera.nearClipPlane = y - (layout.Floor + 6f); camera.farClipPlane = y - layout.Floor + 1f;
        }
        else
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.nearClipPlane = .05f; camera.farClipPlane = 300;
        }
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            camera.targetTexture = rt;
            camera.Render(); camera.Render(); // the first frame after opening a scene in batch mode is unlit
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            return texture.GetPixels32();
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); }
    }

    static string Stats(Color32[] pixels)
    {
        double luma = 0, warmth = 0, saturation = 0; int black = 0, teal = 0;
        foreach (var p in pixels)
        {
            float r = p.r / 255f, g = p.g / 255f, b = p.b / 255f;
            float l = .2126f * r + .7152f * g + .0722f * b, max = Mathf.Max(r, g, b), min = Mathf.Min(r, g, b);
            luma += l; warmth += r - b; saturation += max > 0 ? (max - min) / max : 0;
            if (l < NearBlack) black++;
            if (b > r + .05f && g > r + .03f) teal++;
        }
        int n = pixels.Length;
        return $"{luma / n:F3},{black / (float)n:P1},{teal / (float)n:P1},{warmth / n:F3},{saturation / n:F3}";
    }

    /// <summary>Overlays the approved plan on the top-down render: bays (orange), anchors (red), open spaces (green), lanes (cyan), loops (yellow), arena ring (white).</summary>
    static void Annotate(Color32[] map, NorthWestRoomFramework.Layout layout)
    {
        var plan = NorthWestRoomDensity.LoadPlan();
        var c = layout.interior.center; float half = layout.interior.size.z * .5f + .3f;
        // The camera looks down with +Z up, so its right is +X; texture row 0 is the bottom (-Z) edge.
        Vector2Int Px(float x, float z) => new Vector2Int(Mathf.RoundToInt((x - c.x + half) / (2 * half) * MapSize), Mathf.RoundToInt((z - c.z + half) / (2 * half) * MapSize));
        void Line(Vector2 a, Vector2 b, Color32 col)
        {
            var pa = Px(a.x, a.y); var pb = Px(b.x, b.y);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2Int.Distance(pa, pb)));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(pa, pb, i / (float)steps);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = Mathf.RoundToInt(p.x) + dx, y = Mathf.RoundToInt(p.y) + dy;
                        if (x >= 0 && y >= 0 && x < MapSize && y < MapSize) map[y * MapSize + x] = col;
                    }
            }
        }
        void Poly(IList<Vector2> points, Color32 col, bool closed)
        {
            for (int i = 1; i < points.Count; i++) Line(points[i - 1], points[i], col);
            if (closed && points.Count > 2) Line(points[points.Count - 1], points[0], col);
        }
        Vector2[] Box(NorthWestRoomDensity.Area a) => new[] { new Vector2(a.min.x, a.min.z), new Vector2(a.max.x, a.min.z), new Vector2(a.max.x, a.max.z), new Vector2(a.min.x, a.max.z) };
        foreach (var bay in plan.bays) Poly(Box(bay.footprint), new Color32(255, 140, 0, 255), true);
        foreach (var anchor in plan.anchors ?? new NorthWestRoomDensity.Anchor[0]) Poly(Box(anchor.footprint), new Color32(255, 40, 40, 255), true);
        foreach (var space in plan.openSpaces.Where(s => s.polygon != null)) Poly(space.polygon.Select(v => new Vector2(v.x, v.z)).ToList(), new Color32(40, 255, 80, 255), true);
        foreach (var lane in plan.lanes ?? new NorthWestRoomDensity.Lane[0]) Poly(lane.polyline.Select(v => new Vector2(v.x, v.z)).ToList(), new Color32(0, 230, 255, 255), false);
        foreach (var loop in plan.loops ?? new NorthWestRoomDensity.Loop[0]) Poly(loop.polyline.Select(v => new Vector2(v.x, v.z)).ToList(), new Color32(255, 230, 0, 255), true);
        var centre = layout.report.centre;
        foreach (float radius in new[] { layout.report.ringInnerRadius, layout.report.ringOuterRadius })
            Poly(Enumerable.Range(0, 96).Select(i => new Vector2(centre.x + Mathf.Cos(i * Mathf.PI / 48) * radius, centre.z + Mathf.Sin(i * Mathf.PI / 48) * radius)).ToList(), new Color32(255, 255, 255, 255), true);
    }

    /// <summary>One strip per view: capture | Start Workshop reference | density reference, each 640 x 360.</summary>
    static void Sheets(IEnumerable<string> names)
    {
        if (!File.Exists(WorkshopReference) || !File.Exists(DensityReference)) { Debug.LogWarning("References missing; sheets skipped."); return; }
        const int w = 640, h = 360;
        var workshop = Scaled(File.ReadAllBytes(WorkshopReference), w, h);
        var reference = Scaled(File.ReadAllBytes(DensityReference), w, h);
        foreach (string name in names)
        {
            var capture = Scaled(File.ReadAllBytes(Output + "/Views/" + name + ".png"), w, h);
            var sheet = new Color32[w * 3 * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    sheet[y * w * 3 + x] = capture[y * w + x];
                    sheet[y * w * 3 + w + x] = workshop[y * w + x];
                    sheet[y * w * 3 + 2 * w + x] = reference[y * w + x];
                }
            Save(Output + "/Sheets/" + name + ".png", sheet, w * 3, h);
        }
    }

    static Color32[] Scaled(byte[] png, int width, int height)
    {
        var source = new Texture2D(2, 2); source.LoadImage(png);
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            return result.GetPixels32();
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(source); Object.DestroyImmediate(result); }
    }

    static void Save(string path, Color32[] pixels, int width, int height)
    {
        var t = new Texture2D(width, height, TextureFormat.RGB24, false);
        t.SetPixels32(pixels); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
}
