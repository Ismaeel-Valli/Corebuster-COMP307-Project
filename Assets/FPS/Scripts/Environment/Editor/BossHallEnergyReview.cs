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

/// <summary>Read-only saved/neutral visual and boundary review for corebuster_env_053.</summary>
public static class BossHallEnergyReview
{
    const int Width = BossHallRenderingReview.Width, Height = BossHallRenderingReview.Height;
    /// <summary>Neighbour/backward views with the housings hidden (Before) or shown (After). Only views into the hall may change.</summary>
    public static string NeighbourProof(BossHallFramework.Layout layout, bool compare)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Boundary proof requires graphics; omit -nographics");
        string root = BossHallEnergy.Output + "/Neighbours", dir = root + "/" + (compare ? "After" : "Before"); Directory.CreateDirectory(dir);
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview(); var lines = new List<string>();
        var parts = BossHallEnergy.Renderers(); var enabled = parts.Select(r => r.enabled).ToArray();
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


    [MenuItem("Tools/CoreBuster/Capture Boss Hall Contained Energy")]
    public static void CaptureMenu() => Capture(BossHallFramework.MeasureLayout());
    public static void CaptureBatch()
    { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); CaptureMenu(); }

    // Fresh read-only validation, all four arm interfaces, and an unchanged-scene control for the known incline foreground.
    public static void FinalReviewBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); var layout = BossHallFramework.MeasureLayout();
        string validation = BossHallEnergy.Validate(layout); var before = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null);
        var shared = BossHallSnapshot.CaptureAssets();
        var hashes = Directory.GetFiles(BossHallEnergy.Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, BossHallSurvey.Hash);
        BossHallEnergy.BuildAssets();
        BossHallSnapshot.Compare(hashes, Directory.GetFiles(BossHallEnergy.Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, BossHallSurvey.Hash), "Energy assets after fresh reload/regeneration");
        string dir = BossHallEnergy.Output + "/Control"; Directory.CreateDirectory(dir);
        var materials = BossHallRenderingReview.PreserveMaterials(); var parts = BossHallEnergy.Renderers(); var enabled = parts.Select(r => r.enabled).ToArray();
        var lines = new List<string>();
        byte[] Frame(Vector3 eye, Vector3 target)
        {
            var camera = BossHallRenderingReview.CameraForReview();
            try { return BossHallRenderingReview.Shoot(camera, eye, target); }
            finally { BossHallRenderingReview.DestroyCamera(camera); }
        }
        try
        {
            foreach (var p in BossHallEnergy.Plan(layout).Where(p => p.section == BossHallFramework.Section.MechanicalArms))
                File.WriteAllBytes(dir + "/" + p.root + "_" + p.name + ".png", Frame(p.position + (p.rotation * Vector3.back) * 1.5f, p.position));
            var view = BossHallRenderingReview.Neighbours(layout).Single(v => v.Item1 == "Incline_Reveal");
            foreach (var r in parts) r.enabled = false;
            var a = Frame(view.Item2, view.Item3); File.WriteAllBytes(dir + "/Incline_Hidden_A.png", a);
            var b = Frame(view.Item2, view.Item3); File.WriteAllBytes(dir + "/Incline_Hidden_B.png", b);
            for (int i = 0; i < parts.Length; i++) parts[i].enabled = enabled[i];
            var c = Frame(view.Item2, view.Item3); File.WriteAllBytes(dir + "/Incline_Enabled.png", c);
            var camera = BossHallRenderingReview.CameraForReview(); camera.transform.SetPositionAndRotation(view.Item2, Quaternion.LookRotation(view.Item3 - view.Item2));
            string Difference(byte[] first, byte[] second, string label)
            {
                var x = new Texture2D(2, 2); var y = new Texture2D(2, 2);
                try
                {
                    x.LoadImage(first); y.LoadImage(second); var px = x.GetPixels32(); var py = y.GetPixels32(); int changed = 0, outside = 0, large = 0;
                    var mask = new Color32[px.Length];
                    for (int i = 0; i < px.Length; i++)
                    {
                        int delta = Mathf.Max(Mathf.Abs(px[i].r - py[i].r), Mathf.Abs(px[i].g - py[i].g), Mathf.Abs(px[i].b - py[i].b));
                        mask[i] = delta > 4 ? new Color32(255, 0, 0, 255) : new Color32((byte)(px[i].r / 3), (byte)(px[i].g / 3), (byte)(px[i].b / 3), 255);
                        if (delta <= 4) continue; changed++;
                        var ray = camera.ViewportPointToRay(new Vector3((i % Width + .5f) / Width, (i / Width + .5f) / Height, 0));
                        if (!layout.interior.IntersectRay(ray, out float d) || d < 0) { outside++; if (delta > 32) large++; }
                    }
                    var output = new Texture2D(Width, Height, TextureFormat.RGB24, false); output.SetPixels32(mask); output.Apply();
                    File.WriteAllBytes(dir + "/" + label + "_Diff.png", output.EncodeToPNG()); Object.DestroyImmediate(output);
                    return label + $": {changed} changed pixels, {outside} outside hall projection ({large} above delta 32).";
                }
                finally { Object.DestroyImmediate(x); Object.DestroyImmediate(y); }
            }
            try { lines.Add(Difference(a, b, "Hidden_Control")); lines.Add(Difference(b, c, "Hidden_To_Enabled")); }
            finally { BossHallRenderingReview.DestroyCamera(camera); }
        }
        finally
        {
            for (int i = 0; i < parts.Length; i++) parts[i].enabled = enabled[i]; BossHallRenderingReview.RestoreMaterials(materials);
        }
        BossHallEnergy.Validate(layout);
        BossHallSnapshot.Compare(before, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null), "Final read-only scene");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Final read-only shared assets");
        if (SceneManager.GetActiveScene().isDirty || layout.sceneSha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Final review changed scene");
        string report = validation + "\nFresh reload/regeneration is byte-identical, with unchanged metadata; full scene/shared state and scene bytes unchanged.\n" + string.Join("\n", lines);
        File.WriteAllText("Docs/BossHall/EnergyReload_053.txt", report + "\n"); Debug.Log(report);
    }

    public static void Capture(BossHallFramework.Layout layout)
    {
        BossHallEnergy.Validate(layout);
        var before = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null); var shared = BossHallSnapshot.CaptureAssets();
        string neighbours = NeighbourProof(layout, false); neighbours = NeighbourProof(layout, true);
        var r = layout.Report; var room = layout.interior; var f = BossHallBackdrop.Measure(layout);
        var focus = new Vector3(f.wallX + .54f, f.ringY, f.axisZ); float eye = BossHallRenderingReview.EyeHeight();
        var views = new List<(string, Vector3, Vector3)> {
            ("Entry", r.entryEye, focus),
            ("Arena_SouthEast", new Vector3(room.max.x - 12, r.floorTopY + eye, room.min.z + 14), focus),
            ("Arena_NorthEast", new Vector3(room.max.x - 12, r.floorTopY + eye, room.max.z - 14), focus),
            ("Gallery_Reverse", new Vector3(room.min.x + 18, r.tier1TopY + eye, room.max.z - 5), focus),
            ("Ring_Detail", focus + new Vector3(8, .7f, -1), focus),
            ("Ring_Oblique", focus + new Vector3(6, -2, 4), focus),
        };
        foreach (var p in BossHallEnergy.Plan(layout).Where(p => p.material != 0))
        {
            var outward = p.rotation * Vector3.back;
            views.Add((p.root + "_Detail", p.position + outward * (p.material == 1 ? 2 : 4), p.position));
        }
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview(); var lamps = new List<GameObject>();
        var parts = BossHallEnergy.Renderers(); var enabled = parts.Select(x => x.enabled).ToArray();
        try
        {
            foreach (string mode in new[] { "Saved", "Neutral" })
            {
                if (mode == "Neutral")
                    foreach (var p in new[] { r.centre + new Vector3(-16, 8, -16), r.centre + new Vector3(16, 8, -16),
                        r.centre + new Vector3(-16, 15, 16), r.centre + new Vector3(16, 15, 16), focus + Vector3.right * 4 })
                    {
                        var g = new GameObject("Unsaved white review lamp") { hideFlags = HideFlags.HideAndDontSave }; g.transform.position = p;
                        var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 30; l.intensity = 250; l.color = Color.white; l.shadows = LightShadows.None; lamps.Add(g);
                    }
                string dir = BossHallEnergy.Output + "/" + mode; Directory.CreateDirectory(dir);
                foreach (var v in views) File.WriteAllBytes(dir + "/" + v.Item1 + ".png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
                foreach (var x in parts) x.enabled = false;
                foreach (var v in views.Take(6)) File.WriteAllBytes(dir + "/" + v.Item1 + "_Without.png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
                for (int i = 0; i < parts.Length; i++) parts[i].enabled = enabled[i];
            }
        }
        finally
        {
            for (int i = 0; i < parts.Length; i++) parts[i].enabled = enabled[i];
            foreach (var g in lamps) Object.DestroyImmediate(g); BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials);
        }
        BossHallSnapshot.Compare(before, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null), "Read-only capture scene");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Read-only capture shared assets");
        if (SceneManager.GetActiveScene().isDirty || layout.sceneSha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Energy review changed scene");
        File.WriteAllText("Docs/BossHall/EnergyCapture_053.txt", "Read-only saved/neutral review. Neutral lamps are unsaved. Field animation pinned to review time 0 only during captures.\n" + neighbours + "\nFull scene/shared asset state and scene bytes preserved.\n");
    }
}
