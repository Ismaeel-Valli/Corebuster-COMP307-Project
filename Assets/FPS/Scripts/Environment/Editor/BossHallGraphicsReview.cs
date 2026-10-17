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

/// <summary>Saved lighting, actual player FOV/eye and original floor/deck collision. No authoring.</summary>
public static class BossHallGraphicsReview
{
    const int Width = BossHallRenderingReview.Width, Height = BossHallRenderingReview.Height;
    public static string Neighbours(BossHallFramework.Layout layout, bool compare)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Boundary proof requires graphics; omit -nographics");
        string root = BossHallGraphics.Output + "/Neighbours", dir = root + "/" + (compare ? "After" : "Before"); Directory.CreateDirectory(dir);
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview(); var lines = new List<string>();
        var parts = BossHallFramework.FindSection(BossHallFramework.Section.Wayfinding).GetComponentsInChildren<Renderer>(true); var enabled = parts.Select(r => r.enabled).ToArray();
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


    public sealed class Approach { public string name, signName = "Sign_Final_Assembly"; public Vector3 eye, target; }
    public static List<Approach> Approaches(BossHallFramework.Layout layout)
    {
        var signs = BossHallGraphics.Plan(layout); var sign = signs[0]; var r = layout.Report;
        var list = BossHallReveal.Views(layout).Select(v => new Approach { name = v.name, eye = v.eye, target = v.eye + v.Rotation * Vector3.forward * 10 }).ToList();
        foreach (float distance in new[] { 20f, 12f, 8f })
            list.Add(new Approach { name = "Floor_" + distance + "m", eye = FloorEye(layout, new Vector3(sign.centre.x + distance, r.floorTopY, sign.centre.z)), target = sign.centre });
        // West end of each original first-tier gallery; never invent a camera in its walking lane.
        foreach (string side in new[] { "North", "South" })
        {
            var gallerySign = signs.Single(s => s.name == "Sign_Gallery_" + side);
            var deck = r.deckFootprints.Where(d => d.name.StartsWith(side + " gallery: Catwalk Long", StringComparison.Ordinal) &&
                !d.name.Contains("Rails") && Mathf.Abs(d.max.y - r.tier1TopY) < .15f)
                .OrderBy(d => Mathf.Abs((d.min.x + d.max.x) * .5f - gallerySign.centre.x))
                .ThenBy(d => Mathf.Abs((d.min.z + d.max.z) * .5f - gallerySign.centre.z - gallerySign.normal.z * 4)).First();
            var p = (deck.min + deck.max) * .5f; p.y = r.tier1TopY;
            list.Add(new Approach { name = side + "_Gallery", signName = gallerySign.name, eye = FloorEye(layout, p), target = gallerySign.centre });
            list.Add(new Approach { name = side + "_Gallery_Along", signName = gallerySign.name,
                eye = FloorEye(layout, p + Vector3.right * 8), target = gallerySign.centre });
        }
        return list;
    }
    public static Vector3 FloorEye(BossHallFramework.Layout layout, Vector3 p)
    {
        var root = BossHallFramework.FindRoot();
        var hits = Physics.RaycastAll(p + Vector3.up * 2, Vector3.down, 4, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => !h.collider.transform.IsChildOf(root) && h.normal.y > .8f && Mathf.Abs(h.point.y - p.y) < .3f)
            .OrderBy(h => h.distance).ToArray();
        if (hits.Length == 0) throw new InvalidOperationException("No original floor/deck below approach " + p);
        return hits[0].point + Vector3.up * BossHallRenderingReview.EyeHeight();
    }
    public static void Capture(BossHallFramework.Layout layout, string subfolder = "Saved")
    {
        // Camera rendering can advance/cull animated actor poses in Play mode. Check the static
        // owned graphics there; the runtime harness separately checks saved scene/shared hashes
        // after leaving Play. Edit-mode review still protects every scene component.
        var checkedRoot = Application.isPlaying ? BossHallFramework.FindSection(BossHallFramework.Section.Wayfinding) : null;
        var before = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null, checkedRoot);
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview();
        camera.fieldOfView = Object.FindAnyObjectByType<Unity.FPS.Gameplay.PlayerWeaponsManager>().DefaultFov;
        string dir = BossHallGraphics.Output + "/" + subfolder; Directory.CreateDirectory(dir);
        var lines = new List<string> { "Actual standing eye and saved player vertical FOV; saved lighting; no extra lamps. Floor/gallery captures look toward the plate, reveal views retain their original gaze." };
        var signs = BossHallGraphics.Plan(layout);
        try
        {
            foreach (var v in Approaches(layout))
            {
                var sign = signs.Single(s => s.name == v.signName);
                File.WriteAllBytes(dir + "/" + v.name + ".png", BossHallRenderingReview.Shoot(camera, v.eye, v.target));
                int clear = 0; var tangent = Vector3.Cross(sign.normal, Vector3.up);
                foreach (float u in new[] { -.45f, -.225f, 0, .225f, .45f }) foreach (float y in new[] { -.35f, 0, .35f })
                    if (!Physics.Linecast(v.eye, sign.centre + tangent * sign.size.x * u + Vector3.up * sign.size.y * y, ~0, QueryTriggerInteraction.Ignore)) clear++;
                // Camera pixel dimensions have reverted after Shoot; use viewport coordinates for the 960px review frame.
                var a = camera.WorldToViewportPoint(sign.centre - tangent * sign.size.x * .5f);
                var b = camera.WorldToViewportPoint(sign.centre + tangent * sign.size.x * .5f);
                lines.Add(FormattableString.Invariant($"{v.name}: {v.signName}, eye {v.eye:F2}, distance {Vector3.Distance(v.eye, sign.centre):F2} m; width {Mathf.Abs(a.x - b.x) * BossHallRenderingReview.Width:F1} px; collider-clear samples {clear}/15; front face {Vector3.Dot(sign.normal, v.eye - sign.centre) > 0}."));
            }
        }
        finally { BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials); }
        File.WriteAllText(dir + "/Approaches.txt", string.Join("\n", lines) + "\n");
        BossHallSnapshot.Compare(before, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null, checkedRoot),
            Application.isPlaying ? "Graphics captures preserve static Wayfinding" : "Graphics captures preserve all edit-mode scene state");
    }
    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); BossHallGraphics.Validate(layout); Capture(layout);
    }
}
