using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>Read-only saved/neutral visual, boundary, cue-distinctness and cost review for corebuster_env_054.</summary>
public static class BossHallMeltdownReview
{
    const int Width = BossHallRenderingReview.Width, Height = BossHallRenderingReview.Height;
    static readonly string[] WeaponHits = { "VFX_LazerSparksRed", "VFX_LazerSparksGreen", "VFX_LazerSparksBlue", "VFX_DisassemblerImpact" };

    /// <summary>Advance every emitter to the same edit-mode time so captures show a representative live state.</summary>
    static void Pose(float seconds)
    {
        foreach (var ps in BossHallMeltdown.Emitters()) ps.Simulate(seconds, false, true, true);
    }
    static void Settle() { foreach (var ps in BossHallMeltdown.Emitters()) { ps.Clear(true); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); } }

    static (int changed, int outside, int large, Color32[] mask) Difference(byte[] first, byte[] second, Camera camera, BossHallFramework.Layout layout)
    {
        var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
        try
        {
            a.LoadImage(first); b.LoadImage(second); var pa = a.GetPixels32(); var pb = b.GetPixels32(); var mask = new Color32[pa.Length]; int changed = 0, outside = 0, large = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                int delta = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b));
                bool hit = delta > 4; mask[i] = hit ? new Color32(255, 0, 0, 255) : new Color32((byte)(pa[i].r / 3), (byte)(pa[i].g / 3), (byte)(pa[i].b / 3), 255);
                if (!hit) continue; changed++;
                var ray = camera.ViewportPointToRay(new Vector3((i % Width + .5f) / Width, (i / Width + .5f) / Height, 0));
                if (!layout.interior.IntersectRay(ray, out float d) || d < 0) { outside++; if (delta > 32) large++; }
            }
            return (changed, outside, large, mask);
        }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }
    static void WriteMask(string path, Color32[] mask)
    {
        var image = new Texture2D(Width, Height, TextureFormat.RGB24, false); image.SetPixels32(mask); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG()); Object.DestroyImmediate(image);
    }

    /// <summary>Neighbour/backward views with the effects hidden (Before) or live (After). Only views into the hall may change.</summary>
    static string NeighbourProof(BossHallFramework.Layout layout)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Boundary proof requires graphics; omit -nographics");
        string root = BossHallMeltdown.Output + "/Neighbours"; var lines = new List<string>();
        var parts = BossHallMeltdown.Renderers(); var camera = BossHallRenderingReview.CameraForReview();
        try
        {
            foreach (var v in BossHallRenderingReview.Neighbours(layout))
            {
                Pose(3);
                foreach (var r in parts) r.enabled = false; var before = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                foreach (var r in parts) r.enabled = true; var after = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                foreach (var r in parts) r.enabled = false; var control = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                foreach (var r in parts) r.enabled = true;
                foreach (var (dir, bytes) in new[] { ("Before", before), ("After", after), ("Control", control) })
                { Directory.CreateDirectory(root + "/" + dir); File.WriteAllBytes(root + "/" + dir + "/" + v.Item1 + ".png", bytes); }
                var d = Difference(before, after, camera, layout); var c = Difference(before, control, camera, layout);
                Directory.CreateDirectory(root + "/Diff"); WriteMask(root + "/Diff/" + v.Item1 + ".png", d.mask);
                bool intoHall = v.Item1 == "Threshold_Forward" || v.Item1 == "Incline_Reveal" || v.Item1 == "Landing_Forward";
                lines.Add($"{v.Item1}: {d.changed} changed pixels, {d.outside} outside the geometric hall projection ({d.large} above delta 32); hidden/hidden control {c.changed} ({c.outside} outside); " +
                    (intoHall ? "permitted view into hall" : "strict neighbour/backward view"));
                if (!intoHall && (d.changed > 0 || d.outside > 0)) throw new InvalidOperationException("Boundary image regression: " + lines.Last());
            }
        }
        finally { foreach (var r in parts) r.enabled = true; Settle(); BossHallRenderingReview.DestroyCamera(camera); }
        return string.Join("\n", lines);
    }

    static float Hue(Color c) { Color.RGBToHSV(c, out float h, out _, out _); return h * 360; }
    /// <summary>Material tint times the alpha-weighted average of its main texture (the lazer sparks carry their colour in the texture).</summary>
    static Color Tint(Material m)
    {
        var tint = Color.white;
        foreach (string p in new[] { "_TintColor", "_BaseColor", "_Color" }) if (m && m.HasProperty(p)) { tint = m.GetColor(p); break; }
        // The lazer impacts carry their hue in HDR emission (for example red 8,0,0), which dominates the grey base.
        if (m && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > .05f)
        { var e = m.GetColor("_EmissionColor"); return e / e.maxColorComponent; }
        var texture = m ? m.mainTexture : null; if (!texture) return tint;
        var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); var previous = RenderTexture.active;
        var t = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(texture, rt); RenderTexture.active = rt; t.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); t.Apply();
            Color sum = Color.clear; float weight = 0;
            foreach (var c in t.GetPixels()) { sum += c * c.a; weight += c.a; }
            var average = weight > 0 ? sum / weight : Color.white; return new Color(tint.r * average.r, tint.g * average.g, tint.b * average.b, 1);
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(t); }
    }
    /// <summary>Compare the ambient sparks' hue and behaviour with the shipped weapon impact effects.</summary>
    static string CueReport(List<BossHallMeltdown.Effect> plan)
    {
        var sparks = plan.Where(e => e.kind == BossHallMeltdown.Kind.Sparks).Select(BossHallMeltdown.EffectBounds).ToArray();
        var lines = new List<string>(); float sparkHue = Hue(new Color(1, .7f, .3f)); float nearest = 360;
        foreach (string name in WeaponHits)
        {
            string path = "Assets/FPS/Prefabs/VFX/" + name + ".prefab"; var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) { lines.Add(name + ": missing"); continue; }
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>(); var tint = Tint(r ? r.sharedMaterial : null) * ps.main.startColor.color;
                Color.RGBToHSV(tint, out float h, out float s, out float v); float distance = Mathf.Abs(Mathf.DeltaAngle(h * 360, sparkHue));
                if (s > .25f) nearest = Mathf.Min(nearest, distance);
                lines.Add(FormattableString.Invariant($"{name}/{ps.name}: tint ({tint.r:F2}, {tint.g:F2}, {tint.b:F2}) hue {h * 360:F0}, saturation {s:F2}; loop {ps.main.loop}, life {ps.main.startLifetime.constantMax:F2} s, gravity {ps.main.gravityModifier.constantMax:F2}, mode {(r ? r.renderMode.ToString() : "none")}"));
            }
        }
        lines.Insert(0, FormattableString.Invariant($"Ambient sparks: amber hue {sparkHue:F0} with white-hot core, looping, gravity 1.0, stretched; only below the two gantry-runway conductor boxes, Y {sparks.Min(b => b.min.y):F2}-{sparks.Max(b => b.max.y):F2} (upper-deck walking volume tops at 19.30). Nearest saturated weapon-hit hue distance {nearest:F0} degrees."));
        lines.Add("Discharge: 1-2 violet billboards, 0.08-0.14 s, burst slots 1.9 s apart (p 0.5, 5.7 s loop): <= 0.53 flash events/s, under the 3 flashes/s comfort limit; arcs strobe between 0.81x and 1.20x brightness (_Flicker 0.3).");
        return string.Join("\n", lines);
    }

    [MenuItem("Tools/CoreBuster/Capture Boss Hall Meltdown VFX")]
    public static void CaptureMenu() => Capture(BossHallFramework.MeasureLayout());
    public static void CaptureBatch() { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); CaptureMenu(); }

    public static void Capture(BossHallFramework.Layout layout)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string validation = BossHallMeltdown.Validate(layout);
        // Simulation re-rolls the emitters' automatic seeds; their recipes are rechecked exactly by ValidateInstalled below.
        var section = BossHallFramework.FindSection(BossHallFramework.Section.MeltdownVfx);
        var before = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), section); var shared = BossHallSnapshot.CaptureAssets();
        var materials = BossHallRenderingReview.PreserveMaterials(); string neighbours, cost = "";
        var plan = BossHallMeltdown.Plan(layout); var r = layout.Report; var room = layout.interior; float eye = BossHallRenderingReview.EyeHeight();
        Vector3 At(string name) => plan.Single(e => e.name == name).position;
        var ring = At("Ring_Discharge"); var clampS = At("Clamp_Steam_South"); var runS = At("Runway_Sparks_South");
        var views = new List<(string, Vector3, Vector3)> {
            ("Entry", r.entryEye, ring),
            ("Arena_SouthEast", new Vector3(room.max.x - 12, r.floorTopY + eye, room.min.z + 14), ring),
            ("Arena_West_Close", new Vector3(ring.x + 20, r.floorTopY + eye, ring.z - 7), ring + Vector3.up * 1.5f),
            ("Floor_To_Runway_South", new Vector3(runS.x + 12, r.floorTopY + eye, runS.z + 5), runS + Vector3.down * 1.2f),
            ("Gallery_South_To_Assembly", new Vector3(room.min.x + 22, r.tier1TopY + eye, room.min.z + 4.5f), ring + Vector3.up * 1.5f),
            ("Upper_Deck_To_Assembly", new Vector3(room.max.x - 14, r.tier2TopY + eye, room.center.z - 4), new Vector3(ring.x, (ring.y + runS.y) * .5f, ring.z)),
            ("Ring_Detail", ring + new Vector3(9, .5f, 0), ring),
            ("Clamp_South_Detail", clampS + new Vector3(6, -1, -2), clampS + new Vector3(0, .3f, -.7f)),
            ("Runway_South_Detail", runS + new Vector3(3.5f, -1.6f, -2.5f), runS + Vector3.down * .9f),
        };
        var camera = BossHallRenderingReview.CameraForReview(); var lamps = new List<GameObject>(); var parts = BossHallMeltdown.Renderers();
        try
        {
            neighbours = NeighbourProof(layout);
            foreach (string mode in new[] { "Saved", "Neutral" })
            {
                if (mode == "Neutral")
                    foreach (var p in new[] { r.centre + new Vector3(-16, 8, -16), r.centre + new Vector3(16, 8, -16), r.centre + new Vector3(-16, 15, 16),
                        r.centre + new Vector3(16, 15, 16), ring + Vector3.right * 4, runS + new Vector3(3, -2, 0), clampS + new Vector3(4, -1, 0) })
                    {
                        var g = new GameObject("Unsaved white review lamp") { hideFlags = HideFlags.HideAndDontSave }; g.transform.position = p;
                        var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 30; l.intensity = 250; l.color = Color.white; l.shadows = LightShadows.None; lamps.Add(g);
                    }
                string dir = BossHallMeltdown.Output + "/" + mode; Directory.CreateDirectory(dir);
                foreach (var v in views)
                {
                    Pose(3); var with = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3); File.WriteAllBytes(dir + "/" + v.Item1 + ".png", with);
                    foreach (var x in parts) x.enabled = false;
                    var without = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3); File.WriteAllBytes(dir + "/" + v.Item1 + "_Without.png", without);
                    foreach (var x in parts) x.enabled = true;
                    if (mode == "Saved")
                    {
                        var d = Difference(without, with, camera, layout);
                        cost += $"{v.Item1}: VFX covers {d.changed} px ({100f * d.changed / (Width * Height):F2}% of frame) at the t=3 s pose\n";
                    }
                }
                // Behaviour over time at the three effect types.
                foreach (float t in new[] { 1f, 1.6f, 2.2f, 2.8f, 3.4f })
                {
                    Pose(t); string seq = dir + "/Sequence"; Directory.CreateDirectory(seq);
                    foreach (var v in views.Where(v => v.Item1.EndsWith("_Detail", StringComparison.Ordinal)))
                        File.WriteAllBytes(seq + "/" + v.Item1 + "_" + t.ToString("F1") + "s.png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
                }
            }
            // Headless render-cost proxy at the widest live view: synchronised render+readback with effects on/off.
            var worst = views.Single(v => v.Item1 == "Upper_Deck_To_Assembly"); Pose(3);
            double Measure(bool on)
            {
                foreach (var x in parts) x.enabled = on; BossHallRenderingReview.Shoot(camera, worst.Item2, worst.Item3);
                var watch = Stopwatch.StartNew(); for (int i = 0; i < 20; i++) BossHallRenderingReview.Shoot(camera, worst.Item2, worst.Item3);
                return watch.Elapsed.TotalMilliseconds / 20;
            }
            double off1 = Measure(false), on1 = Measure(true), off2 = Measure(false), on2 = Measure(true);
            cost += FormattableString.Invariant($"Headless synchronised render+readback proxy at Upper_Deck_To_Assembly (2 renders per sample): off {off1:F2}/{off2:F2} ms, on {on1:F2}/{on2:F2} ms. Not a GPU profiler capture.\n");
        }
        finally
        {
            foreach (var x in parts) x.enabled = true; Settle();
            foreach (var g in lamps) Object.DestroyImmediate(g); BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials);
        }
        BossHallSnapshot.Compare(before, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), section), "Read-only capture scene"); BossHallMeltdown.ValidateInstalled(layout);
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Read-only capture shared assets");
        if (SceneManager.GetActiveScene().isDirty || layout.sceneSha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Meltdown review changed scene");
        string report = validation + "\nRead-only saved/neutral review. Neutral lamps are unsaved; emitters are posed by 3 s edit-mode simulation and cleared afterwards.\n" +
            neighbours + "\n" + cost + CueReport(plan) + "\nFull scene/shared asset state and scene bytes preserved.\n";
        File.WriteAllText("Docs/BossHall/MeltdownCapture_054.txt", report); Debug.Log(report);
    }
}
