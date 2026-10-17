using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Unsaved review captures and before/after boundary proof for corebuster_env_052. Never saves the scene.</summary>
public static class BossHallLowerArenaReview
{
    const int Width = BossHallRenderingReview.Width, Height = BossHallRenderingReview.Height;

    /// <summary>Neighbour/backward views with the housings hidden (Before) or shown (After). Only views into the hall may change.</summary>
    public static string NeighbourProof(BossHallFramework.Layout layout, bool compare)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Boundary proof requires graphics; omit -nographics");
        string root = BossHallLowerArena.Output + "/Neighbours", dir = root + "/" + (compare ? "After" : "Before"); Directory.CreateDirectory(dir);
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview(); var lines = new List<string>();
        var parts = BossHallLowerArena.Renderers(); var enabled = parts.Select(r => r.enabled).ToArray();
        if (!compare) foreach (var r in parts) r.enabled = false;
        try
        {
            foreach (var v in BossHallRenderingReview.Neighbours(layout))
            {
                string path = dir + "/" + v.Item1 + ".png"; File.WriteAllBytes(path, BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
                if (!compare) continue;
                var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
                try
                {
                    a.LoadImage(File.ReadAllBytes(root + "/Before/" + v.Item1 + ".png")); b.LoadImage(File.ReadAllBytes(path));
                    var pa = a.GetPixels32(); var pb = b.GetPixels32(); var mask = new Color32[pa.Length]; int changed = 0, outside = 0, large = 0;
                    for (int i = 0; i < pa.Length; i++)
                    {
                        int delta = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b));
                        bool hit = delta > 4; mask[i] = hit ? new Color32(255, 0, 0, 255) : new Color32((byte)(pa[i].r / 3), (byte)(pa[i].g / 3), (byte)(pa[i].b / 3), 255);
                        if (!hit) continue; changed++;
                        // As in render_010, an allowed pixel's camera ray must enter the measured hall.
                        Ray ray = camera.ViewportPointToRay(new Vector3((i % Width + .5f) / Width, (i / Width + .5f) / Height, 0));
                        if (!layout.interior.IntersectRay(ray, out float d) || d < 0) { outside++; if (delta > 32) large++; }
                    }
                    string diff = root + "/Diff"; Directory.CreateDirectory(diff);
                    var image = new Texture2D(Width, Height, TextureFormat.RGB24, false); image.SetPixels32(mask); image.Apply();
                    File.WriteAllBytes(diff + "/" + v.Item1 + ".png", image.EncodeToPNG()); Object.DestroyImmediate(image);
                    bool intoHall = v.Item1 == "Threshold_Forward" || v.Item1 == "Incline_Reveal" || v.Item1 == "Landing_Forward";
                    lines.Add(v.Item1 + ": " + changed + " changed pixels, " + outside + " outside the geometric hall projection (" + large + " above delta 32); " + (intoHall ? "permitted view into hall" : "strict neighbour/backward view"));
                    if (!intoHall && (outside > 32 || large > 0)) throw new InvalidOperationException("Boundary image regression: " + lines.Last());
                }
                finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
            }
        }
        finally
        {
            for (int i = 0; i < parts.Length; i++) parts[i].enabled = enabled[i];
            BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials);
        }
        string report = string.Join("\n", lines);
        if (compare) { File.WriteAllText(root + "/Report.txt", report + "\n"); Debug.Log(report); }
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Capture dirtied MainScene");
        return report;
    }

    [MenuItem("Tools/CoreBuster/Capture Boss Hall Lower Arena Cover")]
    public static void CaptureMenu() { var layout = BossHallFramework.MeasureLayout(); BossHallLowerArena.Validate(layout); Capture(layout); }
    public static void CaptureBatch() { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); CaptureMenu(); }

    /// <summary>Saved-lighting and neutral (unsaved white lamps) views, plus with/without pairs for the combat views.</summary>
    public static void Capture(BossHallFramework.Layout layout)
    {
        var r = layout.Report; var room = layout.interior; var plan = BossHallLowerArena.Plan(layout);
        var focus = (r.turret.head.min + r.turret.head.max) * .5f; float eye = BossHallRenderingReview.EyeHeight();
        float floorEye = r.floorTopY + eye, galleryEye = r.tier1TopY + eye, crouchEye = r.floorTopY + eye * .5f;
        var south = plan[0].hull; var north = plan[1].hull; float y = r.floorTopY + .55f;
        var views = new[] {
            ("Entry", r.entry.centre + r.entry.inward * .5f + Vector3.up * eye, focus + Vector3.up * 3),
            ("Arena_SouthEast", new Vector3(room.max.x - 8, floorEye, room.min.z + 12), new Vector3(room.min.x + 12, r.floorTopY + 2, room.max.z - 2)),
            ("Arena_NorthEast", new Vector3(room.max.x - 8, floorEye, room.max.z - 12), new Vector3(room.min.x + 12, r.floorTopY + 2, room.min.z + 2)),
            ("Housing_North_Front", new Vector3(north.center.x + 1, floorEye, north.min.z - 7), new Vector3(north.center.x, y, north.center.z)),
            ("Housing_South_Front", new Vector3(south.center.x - 1, floorEye, south.max.z + 7), new Vector3(south.center.x, y, south.center.z)),
            ("Housing_North_Oblique", new Vector3(north.max.x + 6, floorEye, north.min.z - 3.5f), new Vector3(north.center.x, y, north.center.z)),
            ("Housing_South_Oblique", new Vector3(south.min.x - 6, floorEye, south.max.z + 3.5f), new Vector3(south.center.x, y, south.center.z)),
            ("Housing_North_Detail", new Vector3(north.center.x + 1.2f, r.floorTopY + 1.6f, north.min.z - 2.4f), new Vector3(north.center.x, r.floorTopY + .6f, north.min.z)),
            ("Crouch_At_North_End", new Vector3(north.max.x + .7f, crouchEye, north.center.z), new Vector3(room.center.x - 8, crouchEye, room.center.z)),
            ("Gallery_To_North_Housing", new Vector3(north.center.x - 4, galleryEye, room.min.z + 5), new Vector3(north.center.x, y, north.center.z)),
            ("Turret_To_South_Housing", r.turret.firingOrigin + Vector3.up * .3f, new Vector3(south.center.x, y, south.center.z)),
        };
        string[] pairs = { "Entry", "Arena_SouthEast", "Arena_NorthEast", "Housing_North_Front", "Housing_South_Oblique", "Gallery_To_North_Housing" };
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview(); var lamps = new List<GameObject>();
        var parts = BossHallLowerArena.Renderers(); var enabled = parts.Select(x => x.enabled).ToArray();
        try
        {
            Directory.CreateDirectory(BossHallLowerArena.Output + "/Saved"); Directory.CreateDirectory(BossHallLowerArena.Output + "/Neutral");
            foreach (var v in views) File.WriteAllBytes(BossHallLowerArena.Output + "/Saved/" + v.Item1 + ".png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
            var spots = new List<Vector3> { r.centre + new Vector3(-16, 8, -16), r.centre + new Vector3(16, 8, -16), r.centre + new Vector3(-16, 15, 16), r.centre + new Vector3(16, 15, 16) };
            foreach (var h in new[] { south, north }) { spots.Add(new Vector3(h.center.x - 4, r.floorTopY + 3, h.center.z)); spots.Add(new Vector3(h.center.x + 4, r.floorTopY + 3, room.center.z + (h.center.z - room.center.z) * .85f)); }
            foreach (var p in spots)
            {
                var g = new GameObject("Unsaved white review lamp") { hideFlags = HideFlags.HideAndDontSave }; g.transform.position = p;
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 30; l.intensity = 250; l.color = Color.white; l.shadows = LightShadows.None; lamps.Add(g);
            }
            foreach (var v in views) File.WriteAllBytes(BossHallLowerArena.Output + "/Neutral/" + v.Item1 + ".png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
            foreach (var x in parts) x.enabled = false;
            foreach (var v in views.Where(v => pairs.Contains(v.Item1)))
                File.WriteAllBytes(BossHallLowerArena.Output + "/Neutral/" + v.Item1 + "_Without.png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
        }
        finally
        {
            for (int i = 0; i < parts.Length; i++) parts[i].enabled = enabled[i];
            foreach (var g in lamps) Object.DestroyImmediate(g); BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials);
        }
        File.WriteAllText(BossHallLowerArena.Output + "/README.txt",
            "corebuster_env_052 review captures. Saved/: the saved scene's own lighting (no final env_055 lighting yet). Neutral/: unsaved white review lamps; " +
            "*_Without.png hides only the housing renderers for comparison. Random film grain is disabled on the temporary camera only. No scene or material is saved.\n");
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Lower arena review dirtied MainScene");
    }
}
