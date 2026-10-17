using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = NorthWestRoomFramework.Section;

// Geometric forward-light culling and protected-state checks, adapted from the verified Centre Room pass.
public static partial class NorthWestRoomLighting
{
    static void CompareSnapshot(Dictionary<string, string> before)
    {
        var after = Snapshot();
        var differences = before.Where(p => !after.TryGetValue(p.Key, out var v) || v != p.Value).ToArray();
        if (before.Count == after.Count && differences.Length == 0) return;
        File.WriteAllLines(Output + "/Isolation_Differences.txt", differences.Select(p => p.Key + "\nBEFORE: " + p.Value +
            "\nAFTER: " + (after.TryGetValue(p.Key, out var v) ? v : "missing")));
        throw new InvalidOperationException("Lighting changed protected object state; see Isolation_Differences.txt.");
    }

    static void VerifyUndo(NorthWestRoomFramework.Layout layout)
    {
        var root = NorthWestRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.Lighting);
        int count = root.GetComponentsInChildren<Transform>(true).Length;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        NorthWestRoomFramework.BeginSectionRebuild(layout, Section.Lighting, Owner);
        Undo.CollapseUndoOperations(group);
        if (root.childCount != 0) throw new InvalidOperationException("Lighting clear failed.");
        Undo.PerformUndo();
        if (root.GetComponentsInChildren<Transform>(true).Length != count) throw new InvalidOperationException("Lighting Undo failed.");
    }

    internal static void Validate(NorthWestRoomFramework.Layout layout)
    {
        Directory.CreateDirectory(Output);
        var scene = SceneManager.GetActiveScene();
        var root = NorthWestRoomFramework.FindSection(scene, Section.Lighting); var room = root.parent;
        var errors = new List<string>(); var lines = new List<string>();
        ValidateSurfaceAreas(room);
        if (!NorthWestRoomFramework.Validate(scene, layout, true)) errors.Add("Framework failed.");
        var lights = root.GetComponentsInChildren<Light>(true);
        if (lights.Length != 24 || lights.Any(l => !l.enabled || !l.gameObject.activeInHierarchy || l.intensity <= 0 || l.lightmapBakeType != LightmapBakeType.Realtime))
            errors.Add("Expected 24 active realtime lights.");
        if (root.GetComponentsInChildren<Collider>(true).Length != 0) errors.Add("Fixtures must be collider-free.");
        if (root.GetComponentsInChildren<MonoBehaviour>(true).Any(c => !(c is UniversalAdditionalLightData))) errors.Add("Unexpected lighting script.");
        foreach (var lens in root.GetComponentsInChildren<Renderer>().Where(r => r.name == "Lens" || r.name.StartsWith("Tube_")))
            if (!lens.sharedMaterial || !lens.sharedMaterial.IsKeywordEnabled("_EMISSION")) errors.Add(lens.name + " lost its emissive shader keyword.");
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (!pipeline || pipeline.maxAdditionalLightsCount < 4) errors.Add("Pipeline does not provide four additional lights.");
        const int budget = 4;
        var allLights = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>())
            .Where(l => l.enabled && l.intensity > 0 && l.type != LightType.Directional && l.lightmapBakeType != LightmapBakeType.Baked).ToArray();
        // Bound the full square culling frustum, which is larger than the visible spot cone.
        foreach (var l in lights)
        {
            if (l.type != LightType.Spot) { errors.Add(l.name + " is not a spot."); continue; }
            var t = l.transform; float s = l.range * Mathf.Tan(l.spotAngle * .5f * Mathf.Deg2Rad);
            Vector3 c = t.position + t.forward * l.range;
            var points = new[] { t.position, c+(t.right+t.up)*s, c+(t.right-t.up)*s, c-(t.right+t.up)*s, c+(t.up-t.right)*s };
            // The floor is the receiver plane; a frustum may end below it, but cannot cross a wall or the ceiling.
            foreach (var p in points)
                if (p.x < layout.interior.min.x || p.x > layout.interior.max.x || p.z < layout.interior.min.z ||
                    p.z > layout.interior.max.z || p.y > layout.interior.max.y) errors.Add(t.parent.name + " frustum crosses the room boundary at " + p);
        }
        var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>())
            .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        var shell = new HashSet<string>(layout.report.shell.Select(s => s.id));
        int peak = 0, checkedCount = 0, outsideHits = 0;
        foreach (var r in renderers)
        {
            if (r.transform.IsChildOf(room))
            {
                foreach (var m in r.sharedMaterials)
                    if (!m || !m.shader || ShaderUtil.ShaderHasError(m.shader)) errors.Add("Broken material on " + r.name);
                int count = allLights.Count(l => (l.cullingMask & (1 << r.gameObject.layer)) != 0 && Touches(l, r.bounds));
                peak = Mathf.Max(peak, count); checkedCount++;
                if (count > budget) errors.Add(r.name + " uses " + count + "/4: " + string.Join(",", allLights.Where(l => Touches(l, r.bounds)).Select(l => l.transform.parent.name)));
            }
            else if (!shell.Contains(GlobalObjectId.GetGlobalObjectIdSlow(r).ToString()) && !layout.interior.Contains(r.bounds.center) && r.bounds.max.y > layout.Floor + .05f)
            {
                var hits = lights.Where(l => (l.cullingMask & (1 << r.gameObject.layer)) != 0 && Touches(l, r.bounds)).ToArray();
                if (hits.Length > 0) { outsideHits++; errors.Add("Outside renderer " + r.name + " reached by " + string.Join(",", hits.Select(l => l.transform.parent.name))); }
            }
        }
        var sites = layout.report.spawns.Select(s => s.position).ToList();
        foreach (float radius in new[] { 4f, 6f, 8f })
            for (int i = 0; i < 72; i++) sites.Add(layout.report.centre + new Vector3(Mathf.Cos(i * Mathf.PI / 36), 0, Mathf.Sin(i * Mathf.PI / 36)) * radius);
        // Include a dense whole-room grid, not only the requested spawn/ring sample set.
        for (float x = layout.interior.min.x + 1; x < layout.interior.max.x; x += 2)
            for (float z = layout.interior.min.z + 1; z < layout.interior.max.z; z += 2) sites.Add(new Vector3(x, layout.Floor, z));
        int actorPeak = 0;
        foreach (var p in sites)
        {
            int count = allLights.Count(l => Touches(l, new Bounds(p + Vector3.up, new Vector3(1.1f, 2.2f, 1.1f))));
            actorPeak = Mathf.Max(actorPeak, count);
            if (count > budget) errors.Add($"Actor at {p:F2} uses {count}/4 lights.");
        }
        var plan = NorthWestRoomDensity.LoadPlan();
        foreach (var bay in plan.bays)
        {
            Vector3 p = new Vector3((bay.footprint.min.x + bay.footprint.max.x) * .5f, layout.Floor + 1.3f,
                (bay.footprint.min.z + bay.footprint.max.z) * .5f);
            // Sample the visible machine front (the bay's inner edge), not the wall behind its cabinet.
            if (bay.id.StartsWith("S")) p.z = bay.footprint.max.z - .12f;
            if (bay.id.StartsWith("N")) p.z = bay.footprint.min.z + .12f;
            if (bay.id.StartsWith("W")) p.x = bay.footprint.max.x - .12f;
            if (bay.id.StartsWith("E")) p.x = bay.footprint.min.x + .12f;
            var hits = lights.Where(l => Illuminates(l, p)).ToArray();
            lines.Add("Bay " + bay.id + ": " + string.Join(",", hits.Select(l => l.transform.parent.name)));
            if (hits.Length == 0) errors.Add("Bay " + bay.id + " has no direct light at machinery height.");
        }
        foreach (var space in plan.openSpaces)
        {
            Vector3 p = space.polygon != null && space.polygon.Length > 0 ?
                new Vector3(space.polygon.Average(v => v.x), layout.Floor + 1.2f, space.polygon.Average(v => v.z)) :
                new Vector3(space.centre.x, layout.Floor + 1.2f, space.centre.z);
            var hits = lights.Where(l => Illuminates(l, p)).ToArray();
            lines.Add("Space " + space.id + ": " + string.Join(",", hits.Select(l => l.transform.parent.name)));
            if (hits.Length == 0) errors.Add("Open space " + space.id + " has no direct light at actor height.");
        }
        string report = $"Renderers {checkedCount}: peak {peak}/4; actors {sites.Count}: peak {actorPeak}/4; lights {lights.Length}/24; outside renderer hits {outsideHits}.\n" +
            string.Join("\n", lines) + "\nErrors: " + errors.Count + "\n" + string.Join("\n", errors);
        File.WriteAllText(Output + "/Validation.txt", report);
        if (errors.Count != 0) throw new InvalidOperationException("NORTH-WEST LIGHTING FAILED\n" + report);
        Debug.Log("NORTH-WEST LIGHTING CHECKS PASSED\n" + report);
    }

    // Physical range/cone test for coverage, separate from conservative per-object culling.
    static bool Illuminates(Light light, Vector3 p)
    {
        Vector3 d = p - light.transform.position;
        return d.magnitude < light.range * .98f && Vector3.Angle(light.transform.forward, d) < light.spotAngle * .48f;
    }

    static void Capture(NorthWestRoomFramework.Layout layout)
    {
        var r = layout.report; float eye = r.playerEyeHeight;
        var east = r.gates.Single(g => g.name == NorthWestRoomSurvey.EastDoorName);
        var boss = r.gates.Single(g => g.name == NorthWestRoomSurvey.BossDoorName);
        var views = new List<(string, Vector3, Vector3)> {
            ("East_Entry", east.position-east.outward*4+Vector3.up*eye, r.centre+Vector3.up*2),
            ("Boss_Route", boss.position-boss.outward*8+Vector3.up*eye, boss.position+Vector3.up*2.5f),
            ("Red_Terminal", new Vector3(r.redTerminal.standing.position.x,r.redTerminal.eyeY,r.redTerminal.standing.position.z),r.redTerminal.focus.position),
            ("Master_Terminal",new Vector3(r.masterTerminal.standing.position.x,r.masterTerminal.eyeY,r.masterTerminal.standing.position.z),r.masterTerminal.lookAt.position) };
        foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            views.Add(("Wall_"+direction, r.centre+Vector3.up*eye-direction*4, r.centre+direction*15+Vector3.up*3));
        foreach (var space in NorthWestRoomDensity.LoadPlan().openSpaces.Where(s => s.polygon != null))
        {
            var p = new Vector3(space.polygon.Average(v=>v.x),layout.Floor+eye,space.polygon.Average(v=>v.z));
            views.Add((space.id+"_Open_Space",p,r.centre+Vector3.up*2.5f));
        }
        foreach (var bay in NorthWestRoomDensity.LoadPlan().bays)
        {
            Vector3 target = new Vector3((bay.footprint.min.x + bay.footprint.max.x)*.5f, layout.Floor+2.5f,
                (bay.footprint.min.z + bay.footprint.max.z)*.5f);
            Vector3 inward = bay.id.StartsWith("S") ? Vector3.forward : bay.id.StartsWith("N") ? Vector3.back : bay.id.StartsWith("E") ? Vector3.left : Vector3.right;
            Vector3 from = target + inward*5; from.y=layout.Floor+eye;
            views.Add(("Bay_"+bay.id,from,target+Vector3.up*1.2f));
        }
        Directory.CreateDirectory(Output+"/After"); var camera=MakeCamera("Temporary Room 3 lighting review camera");
        try { foreach(var v in views) File.WriteAllBytes(Output+"/After/"+v.Item1+".png",Shoot(camera,v.Item2,v.Item3)); }
        finally { Object.DestroyImmediate(camera.gameObject); }
    }
    // Conservative Unity forward-light culling: point sphere, spot square frustum (not the smaller visible cone).
    static bool Touches(Light light, Bounds bounds)
    {
        if ((light.cullingMask & ~0) == 0) return false;
        if (light.type != LightType.Spot) return bounds.SqrDistance(light.transform.position) <= light.range * light.range;
        var t = light.transform;
        float s = light.range * Mathf.Tan(light.spotAngle * .5f * Mathf.Deg2Rad);
        Vector3 p = t.position, c = p + t.forward * light.range;
        var vertices = new[] { p, c + (t.right + t.up) * s, c + (t.right - t.up) * s,
            c - (t.right + t.up) * s, c + (t.up - t.right) * s };
        var axes = new List<Vector3> { Vector3.right, Vector3.up, Vector3.forward, t.forward };
        for (int i = 1; i <= 4; i++)
        {
            Vector3 edge = vertices[i] - p;
            axes.Add(Vector3.Cross(edge, vertices[i == 4 ? 1 : i + 1] - p));
            foreach (Vector3 a in new[] { Vector3.right, Vector3.up, Vector3.forward }) axes.Add(Vector3.Cross(edge, a));
        }
        foreach (Vector3 edge in new[] { t.right, t.up })
            foreach (Vector3 a in new[] { Vector3.right, Vector3.up, Vector3.forward }) axes.Add(Vector3.Cross(edge, a));
        foreach (Vector3 axis in axes)
        {
            if (axis.sqrMagnitude < 1e-8f) continue;
            float low = vertices.Min(v => Vector3.Dot(v, axis)), high = vertices.Max(v => Vector3.Dot(v, axis));
            float centre = Vector3.Dot(bounds.center, axis);
            float extent = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));
            if (high < centre - extent || low > centre + extent) return false;
        }
        return true;
    }

    internal static Dictionary<string, string> Snapshot()
    {
        Canvas.ForceUpdateCanvases();
        var scene = SceneManager.GetActiveScene(); var root = NorthWestRoomFramework.FindSection(scene, Section.Lighting).parent;
        var lighting = NorthWestRoomFramework.FindSection(scene, Section.Lighting);
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => (t == lighting || !t.IsChildOf(lighting)) && !t.name.StartsWith(PatchPrefix, StringComparison.Ordinal)).ToDictionary(
                t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t =>
                {
                    // Preserve renderer/component state; derived surface meshes compare by their original source identity.
                    var r = t.GetComponent<Renderer>();
                    string state = r ? StableState(r) : "";
                    string components = string.Join("|", t.GetComponents<Component>().Where(c => c && !(c is Transform) && !(c is Renderer) && !(c is MeshFilter && IsSplittable(t)))
                        .Select(StableState));
                    return FormattableString.Invariant($"{t.name}|{t.parent?.name}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|{state}|{components}|{OriginalMeshIdentity(t)}");
                });
    }

    static string StableState(Component component)
    {
        var values = new List<string>();
        using (var so = new SerializedObject(component))
        {
            var p = so.GetIterator();
            bool enter = true;
            while (p.Next(enter))
            {
                enter = p.propertyType == SerializedPropertyType.Generic;
                if (p.propertyType == SerializedPropertyType.Generic) continue;
                string value;
                if (p.propertyType == SerializedPropertyType.ObjectReference)
                    value = p.objectReferenceValue ? GlobalObjectId.GetGlobalObjectIdSlow(p.objectReferenceValue).ToString() : "null";
                else if (p.propertyType == SerializedPropertyType.Float)
                    value = p.doubleValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                else value = Convert.ToString(p.boxedValue, System.Globalization.CultureInfo.InvariantCulture);
                values.Add(p.propertyPath + "=" + value);
            }
        }
        return string.Join(";", values);
    }

    const int Width = 960, Height = 540;

    static List<(string name, Vector3 position, Vector3 target)> NeighbourViews(NorthWestRoomFramework.Layout layout)
    {
        var report = layout.report;
        var east = report.gates.Single(g => g.name == NorthWestRoomSurvey.EastDoorName);
        var boss = report.gates.Single(g => g.name == NorthWestRoomSurvey.BossDoorName);
        float eye = report.playerEyeHeight;
        Vector3 Steps(float d)
        {
            var s = report.bossApproach.OrderBy(p => Mathf.Abs(p.distance - d)).First();
            return boss.position + boss.outward * d + Vector3.up * (s.groundY + eye - report.floorTopY);
        }
        return new List<(string, Vector3, Vector3)>
        {
            ("East_Door_Outside", east.position + east.outward * 4f + Vector3.up * eye, east.position + Vector3.up * 1.6f),
            ("East_Door_Wide", east.position + east.outward * 8f + Vector3.up * eye, east.position + Vector3.up * 3f),
            ("East_Corridor_To_Centre", east.position + east.outward * 3f + Vector3.up * eye, new Vector3(-196.5f, 1.8f, 90.17f)),
            ("East_Corridor_Mid", new Vector3(-209f, eye, 96f), new Vector3(-218.8f, 3f, 108.8f)),
            ("Access_Machine_View", report.accessStanding.position + Vector3.up * eye, new Vector3(-218.5f, 3f, 111f)),
            ("Boss_Steps", Steps(6f), boss.position + Vector3.up * 2.2f),
            ("Boss_Landing_Back", Steps(11f), boss.position + Vector3.up * 3f),
        };
    }

    static Camera MakeCamera(string name)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 70; camera.nearClipPlane = .05f; camera.farClipPlane = 300;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        return camera;
    }

    static byte[] Shoot(Camera camera, Vector3 position, Vector3 target)
    {
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt;
            camera.Render(); camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            return texture.EncodeToPNG();
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture);
        }
    }

    static void CaptureNeighbours(string set, NorthWestRoomFramework.Layout layout)
    {
        string dir = Output + "/Neighbours/" + set;
        Directory.CreateDirectory(dir);
        var camera = MakeCamera("Temporary Room 3 neighbour camera");
        try { foreach (var v in NeighbourViews(layout)) File.WriteAllBytes(dir + "/" + v.name + ".png", Shoot(camera, v.position, v.target)); }
        finally { Object.DestroyImmediate(camera.gameObject); }
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Neighbour capture dirtied MainScene.");
    }

    // The Boss Hall Door leaves stop short of the lintel, so a thin strip of the room's own interior shows above them from
    // the incline. That strip is the only part of a neighbour view that may legitimately change (pixel x, y from the top, width, height).
    // Outside those windows only edge aliasing is tolerated: a few pixels, each off by at most NoiseDelta channel levels.
    const int NoiseDelta = 32, NoiseBudget = 32;

    static readonly Dictionary<string, RectInt> InteriorWindows = new Dictionary<string, RectInt>
    {
        ["Boss_Steps"] = new RectInt(410, 205, 130, 35),
        ["Boss_Landing_Back"] = new RectInt(440, 255, 90, 30),
    };

    /// <summary>Captures the "After" set, diffs it with "Before" and fails when a view changed outside a room-interior window.</summary>
    static string CompareNeighbours()
    {
        CaptureNeighbours("After", NorthWestRoomFramework.MeasureLayout());
        string before = Output + "/Neighbours/Before", after = Output + "/Neighbours/After", diff = Output + "/Neighbours/Diff";
        Directory.CreateDirectory(diff);
        var lines = new List<string>(); int failures = 0;
        foreach (string file in Directory.GetFiles(before, "*.png").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
            a.LoadImage(File.ReadAllBytes(file)); b.LoadImage(File.ReadAllBytes(after + "/" + name));
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            var mask = new Color32[pa.Length]; int changed = 0, outside = 0, bright = 0, largest = 0;
            string view = Path.GetFileNameWithoutExtension(name);
            bool windowed = InteriorWindows.TryGetValue(view, out var window);
            for (int i = 0; i < pa.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b));
                bool hit = d > 4; // above sampling noise
                if (hit)
                {
                    changed++; largest = Mathf.Max(largest, d);
                    if (!windowed || !window.Contains(new Vector2Int(i % a.width, a.height - 1 - i / a.width))) { outside++; if (d > NoiseDelta) bright++; }
                }
                mask[i] = hit ? new Color32(255, 0, 0, 255) : new Color32((byte)(pa[i].r / 3), (byte)(pa[i].g / 3), (byte)(pa[i].b / 3), 255);
            }
            var m = new Texture2D(a.width, a.height, TextureFormat.RGB24, false); m.SetPixels32(mask);
            File.WriteAllBytes(diff + "/" + name, m.EncodeToPNG());
            Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(m);
            lines.Add($"{view}: {changed} px changed, {outside} outside the room-interior window ({bright} above delta {NoiseDelta}), largest channel delta {largest}");
            if (bright > 0 || outside > NoiseBudget) failures++;
        }
        File.WriteAllText(Output + "/Neighbours/Report.txt", string.Join("\n", lines) + "\n");
        string summary = $"{lines.Count} neighbour views, {failures} changed outside a room-interior window";
        if (failures > 0) throw new InvalidOperationException("Neighbour views changed: " + summary + "\n" + string.Join("\n", lines));
        return summary;
    }

}
