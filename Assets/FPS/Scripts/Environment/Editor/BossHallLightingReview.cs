using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.FPS.Game;
using Object = UnityEngine.Object;

/// <summary>Full square-frustum lists, physical cone coverage, combat-light stress and read-only captures.</summary>
public static class BossHallLightingReview
{
    sealed class Volume
    {
        public Light light;
        public Vector3[] vertices, axes;
        public float[] low, high;
        public Volume(Light source)
        {
            light = source; if (source.type != LightType.Spot) return;
            var t = source.transform; var p = t.position; var c = p + t.forward * source.range;
            float s = source.range * Mathf.Tan(source.spotAngle * Mathf.Deg2Rad * .5f);
            vertices = new[] { p, c + (t.right + t.up) * s, c + (t.right - t.up) * s,
                c - (t.right + t.up) * s, c + (t.up - t.right) * s };
            var a = new List<Vector3> { Vector3.right, Vector3.up, Vector3.forward, t.forward };
            for (int i = 1; i <= 4; i++)
            {
                var edge = vertices[i] - p; a.Add(Vector3.Cross(edge, vertices[i == 4 ? 1 : i + 1] - p));
                foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward }) a.Add(Vector3.Cross(edge, axis));
            }
            foreach (var edge in new[] { t.right, t.up }) foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward }) a.Add(Vector3.Cross(edge, axis));
            axes = a.Where(v => v.sqrMagnitude > 1e-8f).ToArray();
            low = axes.Select(axis => vertices.Min(v => Vector3.Dot(v, axis))).ToArray();
            high = axes.Select(axis => vertices.Max(v => Vector3.Dot(v, axis))).ToArray();
        }
        public bool Touches(Bounds b, int layer = -1)
        {
            if (layer >= 0 && (light.cullingMask & (1 << layer)) == 0) return false;
            if (vertices == null) return b.SqrDistance(light.transform.position) <= light.range * light.range;
            for (int i = 0; i < axes.Length; i++)
            {
                var axis = axes[i]; float centre = Vector3.Dot(b.center, axis);
                float extent = Vector3.Dot(b.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));
                if (high[i] < centre - extent || low[i] > centre + extent) return false;
            }
            return true;
        }
    }
    internal static bool Illuminates(Light l, Vector3 p)
    {
        var d = p - l.transform.position;
        return d.magnitude < l.range * .98f && (l.type != LightType.Spot || Vector3.Angle(l.transform.forward, d) < l.spotAngle * .48f);
    }
    static IEnumerable<T> All<T>(bool inactive = true) where T : Component
        => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(inactive));

    public static string Analyse(BossHallFramework.Layout layout, out int errorCount)
    {
        BossHallLighting.ValidateInstalled(layout);
        var lines = new List<string>(); var errors = new List<string>(); var limitations = new List<string>(); var room = layout.interior;
        var lamps = BossHallLighting.Lights(); var own = new HashSet<Light>(lamps);
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (!pipeline || pipeline.maxAdditionalLightsCount != 4) errors.Add("Expected the unchanged four-additional-light forward pipeline");
        // Include positive-intensity status/door lights; dormant core lamps are evaluated separately at surge range.
        var all = All<Light>(false).Where(l => l.enabled && l.intensity > 0 && l.type != LightType.Directional && l.lightmapBakeType != LightmapBakeType.Baked)
            .Select(l => new Volume(l)).ToArray();
        var architectural = lamps.Select(l => new Volume(l)).ToArray();
        foreach (var v in architectural)
        {
            foreach (var p in v.vertices)
                if (p.x < room.min.x || p.x > room.max.x || p.z < room.min.z || p.z > room.max.z || p.y > room.max.y)
                { errors.Add(v.light.name + " square culling frustum crosses hall boundary: " + p.ToString("F2")); break; }
            if (!room.Contains(v.light.transform.position)) errors.Add(v.light.name + " origin outside hall");
        }
        int checkedCount = 0, rendererPeak = 0, baselineRendererPeak = 0, outside = 0;
        var renderers = All<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        foreach (var r in renderers)
        {
            var hits = all.Where(v => v.Touches(r.bounds, r.gameObject.layer)).ToArray();
            bool inHall = r.transform.IsChildOf(BossHallFramework.FindRoot()) || room.Contains(r.bounds.center);
            if (inHall)
            {
                checkedCount++; rendererPeak = Mathf.Max(rendererPeak, hits.Length);
                int baseline = hits.Count(v => !own.Contains(v.light)); baselineRendererPeak = Mathf.Max(baselineRendererPeak, baseline);
                if (hits.Length > 4) limitations.Add("Renderer " + BossHallFramework.PathOf(r.transform) + " bounds " + r.bounds + " has " + hits.Length + " candidates for 4 slots (baseline " + baseline + "): " + string.Join(", ", hits.Select(v => v.light.name)));
            }
            else if (r.bounds.max.y > layout.Report.floorTopY + .05f && !r.bounds.Intersects(room))
            {
                var leaks = hits.Where(v => own.Contains(v.light)).ToArray();
                if (leaks.Length > 0) { outside++; errors.Add("Outside renderer " + BossHallFramework.PathOf(r.transform) + ": " + string.Join(", ", leaks.Select(v => v.light.name))); }
            }
        }
        // All reachable elevations plus a dense floor grid; actor bounds include body, core and held weapon extent.
        var sites = new List<(string name, Vector3 position)>();
        for (float x = room.min.x + 1.5f; x < room.max.x; x += 2)
            for (float z = room.min.z + 1.5f; z < room.max.z; z += 2) sites.Add(("Floor", new Vector3(x, layout.Report.floorTopY, z)));
        foreach (var d in layout.Report.deckFootprints)
            sites.Add((d.name, new Vector3((d.min.x + d.max.x) * .5f, d.max.y, (d.min.z + d.max.z) * .5f)));
        foreach (var s in layout.Report.stairs)
            for (int i = 0; i <= 8; i++) sites.Add((s.label + " " + i, Vector3.Lerp(s.foot.position, s.top.position, i / 8f)));
        int actorPeak = 0, reservePeak = 0;
        foreach (var site in sites)
        {
            var b = new Bounds(site.position + Vector3.up * 1.1f, new Vector3(1.4f, 2.2f, 1.4f));
            int count = all.Count(v => v.Touches(b)); int arch = architectural.Count(v => v.Touches(b));
            actorPeak = Mathf.Max(actorPeak, count); reservePeak = Mathf.Max(reservePeak, arch + 2);
            if (count > 4) limitations.Add(site.name + " actor/weapon bounds at " + site.position.ToString("F2") + " have " + count + " candidates for 4 slots");
        }
        lines.Add($"Architecture: {lamps.Length}/24; hall renderers {checkedCount}, combined peak {rendererPeak}/4 (baseline {baselineRendererPeak}/4); {sites.Count} actor/core/weapon bounds, current peak {actorPeak}/4; architectural peak + two combat lights {reservePeak}/4. Outside renderer hits {outside}.");
        if (reservePeak > 4) limitations.Add("Architecture plus two hypothetical combat lights: " + reservePeak + " candidates for 4 slots");

        // Saved core start positions, including all disabled lights at maximum surge, are a separate reproducible stress case.
        var cores = All<UnstableCore>().Where(c => c.CoreLight && room.Contains(c.transform.position)).ToArray();
        int corePeak = 0, baselineCorePeak = 0;
        foreach (var site in sites)
        {
            var b = new Bounds(site.position + Vector3.up * 1.1f, new Vector3(1.4f, 2.2f, 1.4f));
            int activeCores = cores.Count(c => b.SqrDistance(c.CoreLight.transform.position) <= Mathf.Pow(c.LightRange * 1.35f, 2));
            int existing = all.Count(v => !own.Contains(v.light) && !cores.Any(c => c.CoreLight == v.light) && v.Touches(b));
            baselineCorePeak = Mathf.Max(baselineCorePeak, activeCores + existing);
            corePeak = Mathf.Max(corePeak, activeCores + existing + architectural.Count(v => v.Touches(b)));
        }
        lines.Add($"All {cores.Length} saved hall cores exposed at 1.35x surge range: baseline peak {baselineCorePeak}/4, with architecture {corePeak}/4. Dynamic clustering/clouds/projectiles require live validation; no gameplay lights suppressed.");
        if (corePeak > 4) limitations.Add("Simultaneously exposed saved cores: baseline " + baselineCorePeak + ", with lighting " + corePeak + " candidates for 4 slots; forward selection remains capped at 4");
        // Coverage is direct-cone evidence, not a claim of unoccluded visibility. Steady lights alone must carry navigation.
        var required = sites.Where(s => s.name != "Floor").ToList();
        required.Add(("Entry", layout.Report.entryEye - Vector3.up * BossHallRenderingReview.EyeHeight() + Vector3.left * 3));
        required.AddRange(layout.Report.actors.Select(a => (a.name, a.position)));
        int covered = 0;
        var steady = lamps.Where(l => !l.GetComponent<ReactorInstabilityLight>() && !l.GetComponent<WorkshopAmbientMotion>()).ToArray();
        foreach (var s in required)
        {
            var p = s.position + Vector3.up * 1.2f;
            if (steady.Any(l => Illuminates(l, p))) covered++;
            else limitations.Add("Ambient/emissive readability review required (no steady direct cone): " + s.name + " " + p.ToString("F2"));
        }
        lines.Add($"Dimmest-state steady cone coverage: {covered}/{required.Count} stair/deck/entry/actor samples. Shadow/occlusion and silhouette review remain separate.");
        var shadow = all.Where(v => v.light.shadows != LightShadows.None).ToArray();
        lines.Add($"Shadow cost: {lamps.Count(l => l.shadows != LightShadows.None)} new low-tier spot slice(s); scene total {shadow.Length} shadow lights, {shadow.Sum(v => v.light.type == LightType.Point ? 6 : 1)} potential slices before camera culling. No new alpha geometry/overdraw, probe or material.");
        lines.AddRange(BossHallLighting.Plan(layout).Select(p => FormattableString.Invariant($"{p.name}: {p.position:F2}, direction {p.direction:F3}, range {p.range:F2}, angle {p.angle:F1}, intensity {p.intensity:F1}, shadows {p.shadows}, flicker {p.flicker}")));
        errorCount = errors.Count;
        return "BOSS HALL LIGHTING env_055 (evidence revision " + BossHallLighting.Revision + ")\n" + string.Join("\n", lines) + "\nErrors: " + errors.Count + "\n" + string.Join("\n", errors) +
            "\nComposition decision (owner-delegated 2026-10-05): preserve gameplay/global pipeline; accept capped forward selection in crowded combat. " +
            "Coverage cones are diagnostic; saved worst-state images and runtime probes establish readability.\nLimitations: " + limitations.Count + "\n" + string.Join("\n", limitations) + "\n";
    }

    static int[] Changed(byte[] a, byte[] b)
    {
        var x = new Texture2D(2, 2); var y = new Texture2D(2, 2);
        try
        {
            x.LoadImage(a); y.LoadImage(b); var p = x.GetPixels32(); var q = y.GetPixels32();
            return Enumerable.Range(0, p.Length).Where(i => Mathf.Max(Mathf.Abs(p[i].r - q[i].r), Mathf.Abs(p[i].g - q[i].g), Mathf.Abs(p[i].b - q[i].b)) > 4).ToArray();
        }
        finally { Object.DestroyImmediate(x); Object.DestroyImmediate(y); }
    }
    public static string Capture(BossHallFramework.Layout layout, string label)
    {
        var lines = new List<string>(); var lights = BossHallLighting.Lights(); var intensity = lights.Select(l => l.intensity).ToArray();
        var materials = BossHallRenderingReview.PreserveMaterials(); var camera = BossHallRenderingReview.CameraForReview();
        string folder = BossHallLighting.Output + "/" + label; Directory.CreateDirectory(folder);
        var r = layout.Report; var b = layout.interior; var head = (r.turret.head.min + r.turret.head.max) * .5f;
        float eye = BossHallRenderingReview.EyeHeight(); // Optional contract playerEyeHeight is deliberately omitted.
        var views = new List<(string, Vector3, Vector3)> {
            ("Entry", r.entryEye, head),
            ("Arena", new Vector3(b.max.x - 12, r.floorTopY + eye, b.min.z + 14), head),
            ("Gallery", new Vector3(b.min.x + 22, r.tier1TopY + eye, b.min.z + 4.5f), head),
            ("Upper_Reverse", new Vector3(b.min.x + 15, r.tier2TopY + eye, r.entryEye.z), r.entryEye + Vector3.up * 8),
            ("Upper_Assembly", new Vector3(b.max.x - 17, r.tier2TopY + eye, r.entryEye.z), new Vector3(b.min.x + 1, 12, b.center.z)),
            ("Gallery_North", new Vector3(b.min.x + 22, r.tier1TopY + eye, b.max.z - 4.5f), head),
            ("Assembly", new Vector3(b.min.x + 21, r.floorTopY + eye, b.center.z - 8), new Vector3(b.min.x + 1, 12, b.center.z)) };
        views.AddRange(r.stairs.Select(s => ("Stair_" + s.label.Replace(' ', '_'), s.foot.position + Vector3.up * eye, s.top.position + Vector3.up)));
        try
        {
            foreach (var v in BossHallRenderingReview.Neighbours(layout))
            {
                foreach (var l in lights) l.enabled = false; var before = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                foreach (var l in lights) l.enabled = true; var after = BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                var changedPixels = Changed(before, after); int changed = changedPixels.Length;
                // env_056: a strict view may show hall content through an opening (the lit giant core through the gap above
                // the Boss Hall Door leaves). As in render_010's boundary proof, a changed pixel is allowed only when its
                // camera ray enters the measured hall; any other change still fails, with zero tolerance.
                int outside = changedPixels.Count(i =>
                {
                    var ray = camera.ViewportPointToRay(new Vector3((i % BossHallRenderingReview.Width + .5f) / BossHallRenderingReview.Width,
                        (i / BossHallRenderingReview.Width + .5f) / BossHallRenderingReview.Height, 0));
                    return !layout.interior.IntersectRay(ray, out float d) || d < 0;
                });
                bool forward = v.Item1 == "Threshold_Forward" || v.Item1 == "Incline_Reveal" || v.Item1 == "Landing_Forward";
                lines.Add(v.Item1 + ": " + changed + " changed pixels, " + outside + " outside the measured hall projection; " + (forward ? "forward hall view" : "strict neighbour"));
                Directory.CreateDirectory(folder + "/Neighbours");
                File.WriteAllBytes(folder + "/Neighbours/" + v.Item1 + "_Before.png", before);
                File.WriteAllBytes(folder + "/Neighbours/" + v.Item1 + "_After.png", after);
                if (!forward && outside > 0) lines.Add("FAIL: strict neighbour changed outside the hall projection");
            }
            foreach (string state in new[] { "Steady", "Minimum", "Maximum" })
            {
                Directory.CreateDirectory(folder + "/" + state);
                for (int i = 0; i < lights.Length; i++)
                {
                    bool ring = lights[i].GetComponent<ReactorInstabilityLight>(); bool feeder = lights[i].GetComponent<WorkshopAmbientMotion>();
                    lights[i].intensity = intensity[i] * (state == "Steady" ? 1 : ring ? state == "Minimum" ? .65f : 1.3f : feeder && state == "Minimum" ? .45f : 1);
                }
                foreach (var v in views) File.WriteAllBytes(folder + "/" + state + "/" + v.Item1 + ".png", BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3));
            }
            // Captured separately from real frame/GPU profiling; PNG encoding/readback is deliberately named in the result.
            foreach (var v in views.Take(4))
            {
                double Time(bool on)
                {
                    foreach (var l in lights) l.enabled = on;
                    BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < 5; i++) BossHallRenderingReview.Shoot(camera, v.Item2, v.Item3);
                    return watch.Elapsed.TotalMilliseconds / 5;
                }
                lines.Add(FormattableString.Invariant($"{v.Item1}: render/readback/PNG proxy, off {Time(false):F2} ms, on {Time(true):F2} ms per two renders. Not GPU/frame-time profiling."));
            }
        }
        finally
        {
            for (int i = 0; i < lights.Length; i++) { lights[i].enabled = true; lights[i].intensity = intensity[i]; }
            BossHallRenderingReview.DestroyCamera(camera); BossHallRenderingReview.RestoreMaterials(materials);
        }
        return string.Join("\n", lines);
    }
    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); var before = BossHallLighting.Protected();
        var shared = BossHallSnapshot.CaptureAssets();
        if (BossHallLighting.Lights().Length == 0) throw new InvalidOperationException("Lighting not installed");
        string report = Capture(layout, "Saved");
        if (report.Contains("FAIL:")) throw new InvalidOperationException("Saved lighting changed a strict neighbour view");
        BossHallLighting.ValidateInstalled(layout); BossHallSnapshot.Compare(before, BossHallLighting.Protected(), "Capture outside Lighting");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Capture protected assets");
        if (layout.sceneSha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Capture changed scene bytes");
        BossHallLighting.Write("LightingCapture_055.txt", report);
    }
}
