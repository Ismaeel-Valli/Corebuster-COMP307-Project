using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FPS.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = CentreRoomFramework.Section;

/// <summary>Room-owned lighting and surface finishes; never edits shared assets or scene lighting settings.</summary>
public static partial class CentreRoomLighting
{
    const string Folder = "Assets/FPS/Art/CentreRoom/Lighting";
    const string Output = "ReviewCaptures/CentreRoom/026";
    const string Owner = "corebuster_env_026";
    const string UndoName = "Build Centre Room Lighting";

    [MenuItem("Tools/CoreBuster/Build Centre Room Lighting")]
    public static void BuildMenu()
    {
        var layout = CentreRoomFramework.MeasureLayout();
        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        try { Build(layout); Validate(layout); Undo.CollapseUndoOperations(undo); }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = CentreRoomFramework.MeasureLayout();
        var before = Snapshot();
        if (!File.Exists(Output + "/Before/South_Entry.png")) Capture(layout, "Before");
        Build(layout);
        Validate(layout);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Scene save failed.");
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = CentreRoomFramework.MeasureLayout();
        Validate(layout);
        var after = Snapshot();
        Capture(layout, "After");
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out var v) || v != p.Value))
        {
            File.WriteAllLines(Output + "/Isolation_Differences.txt", before.Where(p => !after.TryGetValue(p.Key, out var v) || v != p.Value)
                .Select(p => p.Key + "\nBEFORE: " + p.Value + "\nAFTER: " + (after.TryGetValue(p.Key, out var v) ? v : "missing")));
            throw new InvalidOperationException("Lighting changed protected scene state; see Isolation_Differences.txt.");
        }
        Debug.Log($"CENTRE ROOM LIGHTING BUILD VERIFIED: saved/reloaded; {before.Count} existing transforms and protected components unchanged. Live two-wave visibility review remains required.");
    }

    [MenuItem("Tools/CoreBuster/Validate Centre Room Lighting")]
    public static void ValidateMenu() => Validate(CentreRoomFramework.MeasureLayout());

    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        ValidateMenu();
    }

    [MenuItem("Tools/CoreBuster/Capture Centre Room Lighting")]
    public static void CaptureMenu() => Capture(CentreRoomFramework.MeasureLayout(), "After");

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    internal static void Build(CentreRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var required in new[] { Section.Reactor, Section.Cover, Section.PerimeterMachinery, Section.Overhead })
            if (CentreRoomFramework.FindSection(scene, required).childCount == 0)
                throw new InvalidOperationException("Build " + required + " before lighting.");
        EnsureFolder(Folder + "/Materials");
        EnsureFolder(Folder + "/Shell");
        Transform root = CentreRoomFramework.BeginSectionRebuild(layout, Section.Lighting, Owner);
        BuildSurfaces(root, layout);
        ApplyMaterials(scene);
        Material housing = Finish("M_Fixture_Graphite", new Color(.12f, .15f, .18f), Color.black);
        Material amber = Finish("M_Practical_Amber", new Color(.9f, .55f, .2f), new Color(2.5f, 1.2f, .3f));
        var min = layout.interior.min; var max = layout.interior.max; var c = layout.interior.center;
        var sites = new[] {
            new Vector2(min.x + 5, min.z + 5), new Vector2(max.x - 5, min.z + 5),
            new Vector2(min.x + 5, max.z - 5), new Vector2(max.x - 5, max.z - 5),
            new Vector2(min.x + 5, c.z), new Vector2(max.x - 5, c.z),
            new Vector2(c.x, min.z + 5), new Vector2(c.x, max.z - 5) };
        for (int i = 0; i < sites.Length; i++)
        {
            Transform fixture = Group(root, "Pendant_" + i);
            fixture.position = new Vector3(sites[i].x, layout.Floor + 5.4f, sites[i].y);
            Part(fixture, "Housing", new Vector3(0, .16f, 0), new Vector3(1.4f, .22f, .48f), housing);
            for (int j = -1; j <= 1; j += 2)
            {
                Part(fixture, "Strip_" + j, new Vector3(0, .035f, j * .13f), new Vector3(1.18f, .025f, .065f), amber);
                Vector3 suspension = fixture.position + new Vector3(j * .54f, 0, 0);
                float top = OtherDressing(scene, root).Select(r => r.bounds)
                    .Where(b => b.min.y > fixture.position.y + .3f && suspension.x >= b.min.x - .02f && suspension.x <= b.max.x + .02f &&
                        suspension.z >= b.min.z - .02f && suspension.z <= b.max.z + .02f)
                    .Select(b => b.min.y).DefaultIfEmpty(layout.Ceiling).Min();
                float length = top - fixture.position.y - .3f;
                Part(fixture, "Suspension_" + j, new Vector3(j * .54f, .28f + length * .5f, 0), new Vector3(.025f, length, .025f), housing);
            }
            Spot(fixture, "Amber_Pool", fixture.position, Vector3.down, 6f, 78, 400,
                new Color(1f, .67f, .37f), i < 4);
            // Short outward washes stop within the shell thickness; they give the machinery wall a warm rhythm.
            Vector3 inward; Vector3 mount = fixture.position;
            float[] distances = { mount.x - min.x, max.x - mount.x, mount.z - min.z, max.z - mount.z };
            int side = Array.IndexOf(distances, distances.Min());
            inward = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }[side];
            mount -= inward * (distances[side] - .12f); mount.y = layout.Floor + 3.8f;
            Vector3 along = Vector3.Cross(Vector3.up, inward);
            var offsets = new[] { 0f, .3f, -.3f, .6f, -.6f, .9f, -.9f, 1.2f, -1.2f, 1.5f, -1.5f, 2f, -2f, 2.5f, -2.5f, 3f, -3f };
            var site = new[] { 0f, -.6f, .6f, 1.2f }.SelectMany(height => offsets.Select(offset => mount + along * offset + Vector3.up * height)).Where(p =>
                !OtherDressing(scene, root).Any(r => r.bounds.Intersects(new Bounds(p, new Vector3(.32f, .94f, .32f)))))
                .Select(p => (Vector3?)p).FirstOrDefault();
            if (!site.HasValue) throw new InvalidOperationException("No clear bulkhead mount near pendant " + i + " at " + mount + ": " +
                string.Join("; ", OtherDressing(scene, root).Where(r => r.bounds.Intersects(new Bounds(mount, new Vector3(.32f, .94f, .32f))))
                    .Select(r => r.name + " " + r.bounds)));
            mount = site.Value;
            Transform sconce = Group(root, "Bulkhead_" + i);
            sconce.SetPositionAndRotation(mount, Quaternion.LookRotation(inward));
            Part(sconce, "Backplate", Vector3.zero, new Vector3(.24f, .9f, .08f), housing);
            Part(sconce, "Amber_Lens", new Vector3(0, 0, .06f), new Vector3(.1f, .7f, .045f), amber);
            Spot(sconce, "Amber_Wash", mount + inward * .78f, -inward, 1.15f, 105, 20,
                new Color(1f, .53f, .22f), false);
        }
        // The industrial frame catches a cool blue key; emission remains the brightest feature.
        // corebuster_render_008: the keys and a low core glow follow the field's unstable surge.
        Vector3 rc = layout.RingCenter;
        Material field = CentreRoomReactor.FieldMaterial;
        Flicker(Spot(root, "Reactor_Key_South", rc + new Vector3(0, 5, -5.5f), new Vector3(0, -1.5f, 5.5f),
            10.5f, 65, 780, new Color(.48f, .64f, 1f), true), field, .6f, 1.9f);
        Flicker(Spot(root, "Reactor_Key_North", rc + new Vector3(0, 5, 5.5f), new Vector3(0, -1.5f, -5.5f),
            10.5f, 65, 660, new Color(.42f, .68f, 1f), true), field, .6f, 1.9f);
        // Shadowless, so it spills past the base onto the inner ring; its range stops below the ceiling.
        Light glow = Point(root, ReactorGlowName, rc + Vector3.up * ReactorGlowHeight, ReactorGlowRange, 150,
            new Color(.35f, .6f, 1f));
        Flicker(glow, field, .5f, 2.2f);
        ReactorAudioInstaller.Attach(glow.gameObject, field);
        AssetDatabase.SaveAssets();
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    const string ReactorGlowName = "Reactor_Core_Glow";
    const float ReactorGlowHeight = 3f;
    const float ReactorGlowRange = 7f;
    internal static readonly string[] ReactorLights = { "Reactor_Key_South", "Reactor_Key_North", ReactorGlowName };

    static Light Point(Transform parent, string name, Vector3 p, float range, float intensity, Color color)
    {
        var t = Group(parent, name);
        t.position = p;
        var light = t.gameObject.AddComponent<Light>();
        light.type = LightType.Point; light.range = range; light.intensity = intensity; light.color = color;
        light.lightmapBakeType = LightmapBakeType.Realtime; light.shadows = LightShadows.None;
        t.gameObject.AddComponent<UniversalAdditionalLightData>();
        light.renderMode = LightRenderMode.ForcePixel;
        return light;
    }

    static void Flicker(Light light, Material field, float minimum, float maximum) =>
        light.gameObject.AddComponent<ReactorInstabilityLight>().Configure(field, minimum, maximum);

    static Light Spot(Transform parent, string name, Vector3 p, Vector3 direction, float range, float angle,
        float intensity, Color color, bool shadows)
    {
        var t = Group(parent, name);
        t.SetPositionAndRotation(p, Quaternion.LookRotation(direction));
        var light = t.gameObject.AddComponent<Light>();
        light.type = LightType.Spot; light.range = range; light.spotAngle = angle; light.innerSpotAngle = angle * .65f;
        light.intensity = intensity; light.color = color; light.lightmapBakeType = LightmapBakeType.Realtime;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.shadowBias = .03f; light.shadowNormalBias = .15f;
        var data = t.gameObject.AddComponent<UniversalAdditionalLightData>();
        using (var serialized = new SerializedObject(data))
        {
            // The project's low tier is 256. URP's custom tier reads Light.shadowResolution,
            // not shadowCustomResolution; using the pipeline tier avoids an invalid -1 atlas request.
            serialized.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        light.renderMode = LightRenderMode.ForcePixel;
        return light;
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, UndoName);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void Part(Transform parent, string name, Vector3 p, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; Undo.RegisterCreatedObjectUndo(go, UndoName);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false); go.transform.localPosition = p; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    static Material Finish(string name, Color albedo, Color emission)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", albedo); m.SetFloat("_Metallic", .15f); m.SetFloat("_Smoothness", .35f);
        m.SetColor("_EmissionColor", emission);
        if (emission.maxColorComponent > 0) m.EnableKeyword("_EMISSION");
        return Save(m, Folder + "/Materials/" + name + ".mat");
    }

    static T Save<T>(T value, string path) where T : Object
    {
        value.name = Path.GetFileNameWithoutExtension(path);
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!existing) { AssetDatabase.CreateAsset(value, path); return value; }
        Undo.RecordObject(existing, UndoName);
        EditorUtility.CopySerialized(value, existing); EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(value); return existing;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    static IEnumerable<Renderer> Renderers() => SceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(g => g.GetComponentsInChildren<Renderer>(true));

    static IEnumerable<Renderer> OtherDressing(Scene scene, Transform lighting) => lighting.parent.GetComponentsInChildren<Renderer>()
        .Where(r => r.enabled && !r.transform.IsChildOf(lighting));

    internal static void Validate(CentreRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        if (!CentreRoomFramework.Validate(scene, layout, true)) throw new InvalidOperationException("Framework check failed.");
        var root = CentreRoomFramework.FindSection(scene, Section.Lighting);
        var lights = root.GetComponentsInChildren<Light>();
        var errors = new List<string>();
        if (lights.Length != 19) errors.Add("Expected eight amber pendants, eight bulkhead washes, two reactor keys and one reactor glow.");
        var driven = root.GetComponentsInChildren<ReactorInstabilityLight>(true);
        if (driven.Length != ReactorLights.Length || ReactorLights.Any(n => driven.Count(d => d.name == n) != 1))
            errors.Add("Exactly the reactor keys and glow must carry ReactorInstabilityLight.");
        foreach (var d in driven)
            if (d.Field != CentreRoomReactor.FieldMaterial || d.Minimum < .5f || d.Maximum > 2.5f || d.Minimum >= d.Maximum)
                errors.Add(d.name + " flicker must read M_Reactor_Field and stay within 0.5-2.5x its base intensity.");
        if (root.GetComponentsInChildren<Collider>(true).Length > 0) errors.Add("Lighting must be collider-free.");
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        int budget = pipeline ? pipeline.maxAdditionalLightsCount : 0;
        if (budget < 4) errors.Add("Expected a pipeline budget of at least four additional lights.");
        foreach (var l in lights)
        {
            Vector3 p = l.transform.position;
            foreach (Vector3 axis in new[] { Vector3.left, Vector3.right, Vector3.back, Vector3.forward, Vector3.up })
            {
                float angle = Vector3.Angle(l.transform.forward, axis) - l.spotAngle * .5f;
                float reach = l.type == LightType.Point ? l.range
                    : angle >= 90 ? 0 : l.range * Mathf.Cos(Mathf.Max(0, angle) * Mathf.Deg2Rad);
                Vector3 end = p + axis * reach;
                // The measured wall is its inner face; the kit shell is at least 0.3 m thick.
                if (end.x < layout.interior.min.x - .28f || end.x > layout.interior.max.x + .28f ||
                    end.z < layout.interior.min.z - .28f || end.z > layout.interior.max.z + .28f || end.y > layout.Ceiling)
                    errors.Add(l.name + " influence crosses a room wall/ceiling.");
            }
        }
        int maxCount = 0, checkedCount = 0;
        var room = CentreRoomFramework.FindSection(scene, Section.Lighting).parent;
        var allLights = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>())
            .Where(l => l.enabled && l.type != LightType.Directional).ToArray();
        var shellIds = new HashSet<string>(layout.report.shell.Select(s => s.id));
        foreach (var r in Renderers().Where(r => r.enabled && r.gameObject.activeInHierarchy))
        {
            if (r.transform.IsChildOf(room))
            {
                foreach (var m in r.sharedMaterials)
                    if (!m || !m.shader || UnityEditor.ShaderUtil.ShaderHasError(m.shader) ||
                        (m.shader.name != "Universal Render Pipeline/Lit" && !m.shader.name.StartsWith("CoreBuster/")))
                        errors.Add(r.name + " has an unsupported or broken material.");
                int count = allLights.Count(l => Touches(l, r.bounds));
                maxCount = Mathf.Max(maxCount, count); checkedCount++;
                if (count > budget) errors.Add(r.name + " uses " + count + " additional lights (budget " + budget + ").");
            }
            else if (r.bounds.max.y > layout.Floor + .05f && !layout.interior.Contains(r.bounds.center) &&
                !shellIds.Contains(GlobalObjectId.GetGlobalObjectIdSlow(r).ToString()) &&
                lights.Any(l => Touches(l, r.bounds))) errors.Add("A room light reaches outside renderer " + r.name + " " + r.bounds +
                    " from " + string.Join(",", lights.Where(l => Touches(l, r.bounds)).Select(l => l.transform.parent.name + "/" + l.name + " " + l.transform.position)));
        }
        var actorSites = layout.report.spawns.Select(s => s.position).ToList();
        for (int i = 0; i < 72; i++)
        {
            float angle = i * Mathf.PI * 2 / 72;
            actorSites.Add(layout.RingCenter + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 6);
        }
        int actorMaximum = 0;
        foreach (var p in actorSites)
        {
            int count = allLights.Count(l => Touches(l, new Bounds(p + Vector3.up, new Vector3(1.1f, 2.2f, 1.1f))));
            actorMaximum = Mathf.Max(actorMaximum, count);
            if (count > budget) errors.Add($"Actor at {p:F2} exceeds the additional-light budget: {count}/{budget}.");
        }
        Directory.CreateDirectory(Output);
        string report = $"Room renderers: {checkedCount}; maximum additional lights: {maxCount}/{budget}; room lights: {lights.Length}.\n" +
            $"Actor light budget: {actorSites.Count} spawn/ring samples; maximum {actorMaximum}/{budget}.\n" + string.Join("\n", errors);
        File.WriteAllText(Output + "/Validation.txt", report);
        if (errors.Count > 0) throw new InvalidOperationException("CENTRE ROOM LIGHTING CHECKS FAILED\n" + report);
        Debug.Log("CENTRE ROOM LIGHTING CHECKS PASSED: " + report);
        if (!CentreRoomPerimeter.Validate(scene, layout, true)) throw new InvalidOperationException("Perimeter integration check failed.");
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
        var scene = SceneManager.GetActiveScene(); var root = CentreRoomFramework.FindSection(scene, Section.Lighting).parent;
        var lighting = CentreRoomFramework.FindSection(scene, Section.Lighting);
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => t == lighting || !t.IsChildOf(lighting)).ToDictionary(
                t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t =>
                {
                    // Only dressing renderer material assignments are owned outside Lighting.
                    var r = t.GetComponent<Renderer>();
                    string state = r && !t.IsChildOf(root) ? StableState(r) : "";
                    string components = string.Join("|", t.GetComponents<Component>().Where(c => c && !(c is Transform) && !(c is Renderer))
                        .Select(StableState));
                    return FormattableString.Invariant($"{t.name}|{t.parent?.name}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|{state}|{components}");
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

    static void Capture(CentreRoomFramework.Layout l, string phase)
    {
        string output = Output + "/" + phase; Directory.CreateDirectory(output);
        var go = new GameObject("Lighting review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = 65;
        camera.nearClipPlane = .05f; camera.farClipPlane = 180;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var views = new List<(string, Vector3, Vector3)> {
            ("South_Entry", new Vector3(-191.955f, l.Floor + 1.7f, l.interior.min.z + 1.4f), l.RingCenter + Vector3.up * 3.4f),
            ("West_Approach", l.RingCenter + new Vector3(-12, 1.7f, -5), l.RingCenter + Vector3.up * 3.4f),
            ("North_Approach", l.RingCenter + new Vector3(5, 1.7f, 12), l.RingCenter + Vector3.up * 3.4f),
            ("Terminal", l.report.terminalStanding.position + Vector3.up * 1.6f, l.report.terminalFocus.position) };
        foreach (var gate in l.report.gates)
        {
            views.Add(("Gate_" + gate.name, gate.position - gate.outward * 5 + Vector3.up * 1.6f, gate.position + Vector3.up * 1.5f));
            views.Add(("Neighbour_" + gate.name, gate.position + gate.outward * 3 + Vector3.up * 1.6f,
                gate.position + gate.outward * 9 + Vector3.up * 1.6f));
            views.Add(("Boundary_" + gate.name, gate.position + gate.outward * 3 + Vector3.up * 1.6f,
                gate.position + Vector3.up * 1.6f));
        }
        try
        {
            foreach (var v in views)
            {
                camera.transform.SetPositionAndRotation(v.Item2, Quaternion.LookRotation(v.Item3 - v.Item2));
                var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = rt; camera.Render(); camera.Render(); RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                    File.WriteAllBytes(output + "/" + v.Item1 + ".png", texture.EncodeToPNG());
                }
                finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture); }
            }
        }
        finally { Object.DestroyImmediate(go); }
    }
}
