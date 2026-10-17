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

/// <summary>Unsaved review captures and before/after boundary proof for corebuster_env_050. Never saves the scene.</summary>
public static class BossHallServicesReview
{
    const int Width = BossHallRenderingReview.Width, Height = BossHallRenderingReview.Height;

    /// <summary>Neighbour/backward views with the services hidden (Before) or shown (After). Only views into the hall may change.</summary>
    public static string NeighbourProof(BossHallFramework.Layout layout, bool compare)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Boundary proof requires graphics; omit -nographics");
        string root = BossHallServices.Output + "/Neighbours", dir = root + "/" + (compare ? "After" : "Before"); Directory.CreateDirectory(dir);
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview(); var lines = new List<string>();
        var parts = BossHallServices.Renderers(); var enabled = parts.Select(r => r.enabled).ToArray();
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
                        // As in render_010, an allowed pixel's camera ray must enter the measured hall (e.g. the sliver above the door leaves).
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

    /// <summary>Read-only diagnosis: per service root, how many strict neighbour-view pixels change when only it is shown.</summary>
    public static void NeighbourDiagnoseBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); var camera = BossHallRenderingReview.CameraForReview(); var materials = BossHallRenderingReview.PreserveMaterials();
        var parts = BossHallServices.Renderers(); var views = BossHallRenderingReview.Neighbours(layout).Where(v => v.Item1 != "Threshold_Forward" && v.Item1 != "Incline_Reveal" && v.Item1 != "Landing_Forward").ToArray();
        int Diff(byte[] x, byte[] y)
        {
            var a = new Texture2D(2, 2); var b = new Texture2D(2, 2); a.LoadImage(x); b.LoadImage(y);
            var pa = a.GetPixels32(); var pb = b.GetPixels32(); int n = 0;
            for (int i = 0; i < pa.Length; i++) if (Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b)) > 4) n++;
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); return n;
        }
        try
        {
            foreach (var v in views)
            {
                foreach (var r in parts) r.enabled = false;
                var none = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3); var again = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                foreach (var r in parts) r.enabled = true;
                var all = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                var modes = parts.Select(r => r.shadowCastingMode).ToArray(); foreach (var r in parts) r.shadowCastingMode = ShadowCastingMode.Off;
                var noShadow = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                for (int i = 0; i < parts.Length; i++) parts[i].shadowCastingMode = modes[i];
                Debug.Log($"NEIGHBOUR DIAG {v.Item1}: repeat {Diff(none, again)}, all shown {Diff(none, all)}, shown without shadow casting {Diff(none, noShadow)}");
                foreach (var root in parts.Select(r => r.transform.parent).Distinct())
                {
                    foreach (var r in parts) r.enabled = r.transform.parent == root;
                    Debug.Log($"NEIGHBOUR DIAG {v.Item1} only {root.name}: {Diff(none, BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3))}");
                }
                foreach (var r in parts) r.enabled = true;
            }
        }
        finally { BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials); }
    }

    [MenuItem("Tools/CoreBuster/Capture Boss Hall Services")]
    public static void CaptureMenu() { var layout = BossHallFramework.MeasureLayout(); BossHallServices.Validate(layout); Capture(layout); }
    public static void CaptureBatch() { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); CaptureMenu(); }

    /// <summary>Saved-lighting and neutral (unsaved white lamps) views, plus with/without pairs for the key compositions.</summary>
    public static void Capture(BossHallFramework.Layout layout)
    {
        var r = layout.Report; var room = layout.interior; var f = BossHallServices.Measure(layout); var b = f.backdrop;
        var focus = (r.turret.head.min + r.turret.head.max) * .5f; float eye = BossHallRenderingReview.EyeHeight();
        float floorEye = r.floorTopY + eye, galleryEye = r.tier1TopY + eye, upperEye = r.tier2TopY + eye, n = room.max.z, s = room.min.z;
        var views = new[] {
            ("Entry", r.entry.centre + r.entry.inward * .5f + Vector3.up * eye, focus + Vector3.up * 3),
            ("Landing_Forward", BossHallRenderingReview.InclineEye(11, layout), focus + Vector3.up * 3),
            ("Arena_SouthEast", new Vector3(room.max.x - 8, floorEye, s + 12), new Vector3(room.min.x + 12, 14, n - 2)),
            ("Arena_NorthEast", new Vector3(room.max.x - 8, floorEye, n - 12), new Vector3(room.min.x + 12, 14, s + 2)),
            ("Under_North_Gallery", new Vector3(room.max.x - 5, floorEye, n - 4.5f), new Vector3(room.min.x + 12, r.floorTopY + 1.6f, n - 1.2f)),
            ("Under_South_Gallery", new Vector3(room.min.x + 5, floorEye, s + 4.5f), new Vector3(room.max.x - 12, r.floorTopY + 1.6f, s + 1.2f)),
            ("Switchgear_North", new Vector3(f.powerX + 2, floorEye, n - 5.5f), new Vector3(f.powerX - 3, r.floorTopY + 1.2f, n - 1)),
            ("Coolant_Bank_South", new Vector3(f.coolingX + 12, floorEye, s + 5.5f), new Vector3(f.coolingX + 4, r.floorTopY + 1.8f, s + 1)),
            ("Gallery_Power_Junction", new Vector3(f.powerX - 7, galleryEye, n - 4), new Vector3(f.powerX, f.galleryTop + 1.5f, n - .5f)),
            ("Gallery_Cooling_Riser", new Vector3(f.coolingX + 9, galleryEye, s + 4), new Vector3(f.coolingX - 2, f.runY - .6f, s + .5f)),
            ("West_Lintel_And_Corbel", new Vector3(room.min.x + 7, upperEye, b.axisZ + 2.5f), new Vector3(room.min.x + .8f, f.runY + 2.5f, b.bedNorth + 1)),
            ("Upper_North_Trunk", new Vector3(-295.9f, upperEye, n - 2.5f), new Vector3(room.min.x + 3, f.trayY, n - 1)),
            ("Upper_East_Intakes", new Vector3(room.min.x + 20, upperEye, b.axisZ), new Vector3(room.max.x, f.trayY + .5f, b.axisZ)),
            ("Feeder_To_Runway", new Vector3(-285, upperEye, b.axisZ - 4), new Vector3(f.runwayX, f.runwayY0, f.FeederZ(1))),
        };
        string[] pairs = { "Entry", "Arena_SouthEast", "Under_North_Gallery", "Gallery_Cooling_Riser", "Upper_North_Trunk" };
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview(); var lamps = new List<GameObject>();
        var parts = BossHallServices.Renderers(); var enabled = parts.Select(x => x.enabled).ToArray();
        try
        {
            Directory.CreateDirectory(BossHallServices.Output + "/Saved"); Directory.CreateDirectory(BossHallServices.Output + "/Neutral");
            foreach (var v in views) File.WriteAllBytes(BossHallServices.Output + "/Saved/" + v.Item1 + ".png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
            var c = room.center;
            var spots = new List<Vector3> { r.centre + new Vector3(-16, 8, -16), r.centre + new Vector3(16, 8, -16), r.centre + new Vector3(-16, 15, 16), r.centre + new Vector3(16, 15, 16), r.centre + new Vector3(-26, 6, 0) };
            foreach (float x in new[] { room.min.x + 12, c.x, room.max.x - 12 })
                foreach (float z in new[] { s + 5, n - 5 }) { spots.Add(new Vector3(x, r.floorTopY + 2.5f, z)); spots.Add(new Vector3(x, f.trayY - 1, z)); }
            foreach (var p in spots)
            {
                var g = new GameObject("Unsaved white review lamp") { hideFlags = HideFlags.HideAndDontSave }; g.transform.position = p;
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 30; l.intensity = 250; l.color = Color.white; l.shadows = LightShadows.None; lamps.Add(g);
            }
            foreach (var v in views) File.WriteAllBytes(BossHallServices.Output + "/Neutral/" + v.Item1 + ".png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
            foreach (var x in parts) x.enabled = false;
            foreach (var v in views.Where(v => pairs.Contains(v.Item1)))
                File.WriteAllBytes(BossHallServices.Output + "/Neutral/" + v.Item1 + "_Without.png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
        }
        finally
        {
            for (int i = 0; i < parts.Length; i++) parts[i].enabled = enabled[i];
            foreach (var g in lamps) Object.DestroyImmediate(g); BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials);
        }
        File.WriteAllText(BossHallServices.Output + "/README.txt",
            "corebuster_env_050 review captures. Saved/: the saved scene's own lighting (no final env_055 lighting yet). Neutral/: unsaved white review lamps; " +
            "*_Without.png hides only the service renderers for comparison. Random film grain is disabled on the temporary camera only. No scene or material is saved.\n");
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Services review dirtied MainScene");
    }
}
