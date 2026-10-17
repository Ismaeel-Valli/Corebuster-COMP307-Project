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

/// <summary>Unsaved review captures and before/after boundary proof for corebuster_art_006. Never saves the scene.</summary>
public static class BossHallMechanicalArmsReview
{
    const int Width = BossHallRenderingReview.Width, Height = BossHallRenderingReview.Height;

    static Renderer[] ArmParts() => BossHallFramework.FindSection(BossHallFramework.Section.MechanicalArms).GetComponentsInChildren<Renderer>(true);

    /// <summary>Neighbour/backward views with the mechanical arms hidden (Before) or shown (After). Only views into the hall may change.</summary>
    public static string NeighbourProof(BossHallFramework.Layout layout, bool compare)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Boundary proof requires graphics; omit -nographics");
        string dir = BossHallMechanicalArms.Output + "/Neighbours/" + (compare ? "After" : "Before"); Directory.CreateDirectory(dir);
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview(); var lines = new List<string>();
        var parts = ArmParts(); var enabled = parts.Select(r => r.enabled).ToArray();
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
                    a.LoadImage(File.ReadAllBytes(BossHallMechanicalArms.Output + "/Neighbours/Before/" + v.Item1 + ".png")); b.LoadImage(File.ReadAllBytes(path));
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
                    string diff = BossHallMechanicalArms.Output + "/Neighbours/Diff"; Directory.CreateDirectory(diff);
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
        if (compare) { File.WriteAllText(BossHallMechanicalArms.Output + "/Neighbours/Report.txt", report + "\n"); Debug.Log(report); }
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Capture dirtied MainScene");
        return report;
    }

    [MenuItem("Tools/CoreBuster/Capture Boss Hall Mechanical Arms")]
    public static void CaptureMenu() { var layout = BossHallFramework.MeasureLayout(); BossHallMechanicalArms.Validate(layout); Capture(layout); }
    public static void CaptureBatch() { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); CaptureMenu(); }

    /// <summary>Saved-lighting and neutral (unsaved white lamps) views, plus with/without pairs for the key compositions.</summary>
    public static void Capture(BossHallFramework.Layout layout)
    {
        var r = layout.Report; var room = layout.interior; var f = BossHallBackdrop.Measure(layout);
        var focus = (r.turret.head.min + r.turret.head.max) * .5f; float eye = BossHallRenderingReview.EyeHeight();
        
        var armFrame = BossHallMechanicalArms.Measure(layout);
        var armFocus = new Vector3(armFrame.runwayX, armFrame.baseY - 1.6f, f.axisZ);
        var views = new[] {
            ("Entry", r.entry.centre + r.entry.inward * .5f + Vector3.up * eye, focus + Vector3.up * 3),
            ("Landing_Forward", BossHallRenderingReview.InclineEye(11, layout), focus + Vector3.up * 3),
            ("Incline_Reveal", BossHallRenderingReview.InclineEye(8.5f, layout), focus + Vector3.up * 3),
            ("Arena_SouthEast", new Vector3(room.max.x - 8, r.floorTopY + eye, room.min.z + 12), armFocus),
            ("Arena_NorthEast", new Vector3(room.max.x - 10, r.floorTopY + eye, room.max.z - 14), armFocus),
            ("North_Gallery", new Vector3(room.center.x, r.tier1TopY + eye, room.max.z - 5), armFocus),
            ("South_Gallery", new Vector3(room.center.x, r.tier1TopY + eye, room.min.z + 5), armFocus),
            ("Upper_Arms", new Vector3(room.min.x + 19, r.tier2TopY + eye, f.axisZ - 7), armFocus),
            ("South_Arm_Detail", new Vector3(armFrame.runwayX + 6, r.tier2TopY + eye, armFrame.bays[0].center.z + 3), armFrame.Pivot(0) + Vector3.down * 1.5f),
            ("North_Arm_Detail", new Vector3(armFrame.runwayX + 6, r.tier2TopY + eye, armFrame.bays[1].center.z - 3), armFrame.Pivot(1) + Vector3.down * 1.5f),
        };
        string[] pairs = { "Entry", "Landing_Forward", "Arena_SouthEast" };
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview(); var lamps = new List<GameObject>();
        var parts = ArmParts(); var enabled = parts.Select(x => x.enabled).ToArray();
        try
        {
            Directory.CreateDirectory(BossHallMechanicalArms.Output + "/Saved"); Directory.CreateDirectory(BossHallMechanicalArms.Output + "/Neutral");
            foreach (var v in views) File.WriteAllBytes(BossHallMechanicalArms.Output + "/Saved/" + v.Item1 + ".png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
            foreach (var p in new[] { new Vector3(-16, 8, -16), new Vector3(16, 8, -16), new Vector3(-16, 15, 16), new Vector3(16, 15, 16), new Vector3(-26, 6, 0) })
            {
                var g = new GameObject("Unsaved white review lamp") { hideFlags = HideFlags.HideAndDontSave }; g.transform.position = r.centre + p;
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 40; l.intensity = 400; l.color = Color.white; l.shadows = LightShadows.None; lamps.Add(g);
            }
            foreach (var v in views) File.WriteAllBytes(BossHallMechanicalArms.Output + "/Neutral/" + v.Item1 + ".png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
            foreach (var x in parts) x.enabled = false;
            foreach (var v in views.Where(v => pairs.Contains(v.Item1)))
                File.WriteAllBytes(BossHallMechanicalArms.Output + "/Neutral/" + v.Item1 + "_Without.png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
        }
        finally
        {
            for (int i = 0; i < parts.Length; i++) parts[i].enabled = enabled[i];
            foreach (var g in lamps) Object.DestroyImmediate(g); BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials);
        }
        File.WriteAllText(BossHallMechanicalArms.Output + "/README.txt",
            "corebuster_art_006 review captures. Saved/: the saved scene's own lighting (no final env_055 lighting yet). Neutral/: five unsaved white review lamps; " +
            "*_Without.png hides only the mechanical arm renderers for comparison. Random film grain is disabled on the temporary camera only. No scene or material is saved.\n");
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Mechanical-arm review dirtied MainScene");
    }
}
