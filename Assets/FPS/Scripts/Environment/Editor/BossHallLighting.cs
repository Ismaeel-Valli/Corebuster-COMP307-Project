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
using Unity.FPS.Game;
using Object = UnityEngine.Object;
using Section = BossHallFramework.Section;

/// <summary>env_055: isolated architectural lighting; no gameplay, material, pipeline or navigation mutation.</summary>
public static class BossHallLighting
{
    public const string Owner = "corebuster_env_055";
    // Evidence revision: env_055 authored the section; corebuster_env_056 refines it through this owner tool and writes
    // its own *_056 evidence instead of overwriting env_055's. SessionState survives the Play-mode domain reload.
    const string RevisionKey = "CoreBuster.BossHallLighting.Revision";
    public static string Revision => SessionState.GetString(RevisionKey, "055");
    public static string Output => "ReviewCaptures/BossHall/" + Revision;
    public const string FieldPath = BossHallEnergy.Folder + "/Materials/M_Energy_Contained_Field.mat";
    public sealed class Recipe
    {
        public string name;
        public Vector3 position, direction;
        public float range, angle, intensity;
        public Color colour;
        public bool shadows;
        public int flicker; // 0 steady, 1 containment signal, 2 Workshop lamp signal.
    }
    static readonly Color Amber = new Color(1, .83f, .64f), White = new Color(.86f, .92f, 1), Red = new Color(1, .12f, .055f);

    // The reconciled pre-refinement scene (finale merge 389eff4) still holds env_055's overhead key. Only while the saved
    // scene has exactly this hash may the installed section match that superseded recipe; any later save requires env_056's.
    const string PreRefinementScene = "61deb08049452ba7e3dd403b7d6e5cf27f8495653ec908401271bac7dfbc2cb9";
    static IEnumerable<List<Recipe>> AcceptedPlans(BossHallFramework.Layout layout)
    {
        yield return Plan(layout);
        if (layout.sceneSha == PreRefinementScene) yield return Plan(layout, true);
    }

    public static List<Recipe> Plan(BossHallFramework.Layout layout) => Plan(layout, false);
    static List<Recipe> Plan(BossHallFramework.Layout layout, bool superseded055)
    {
        var result = new List<Recipe>(); var r = layout.Report; var b = layout.interior;
        void Add(string name, Vector3 p, Vector3 target, float range, float angle, float intensity, Color colour, bool shadow = false, int flicker = 0)
            => result.Add(new Recipe { name = name, position = p, direction = (target - p).normalized,
                range = range, angle = angle, intensity = intensity, colour = colour, shadows = shadow, flicker = flicker });
        // Broad, separated pools leave room in the forward lists for combat lights.
        foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
        {
            var p = new Vector3(b.center.x + x * 16, 24.8f, b.center.z + z * 16);
            Add("Arena_" + (x < 0 ? "West_" : "East_") + (z < 0 ? "South" : "North"), p, p + Vector3.down, 20, 72, 4500, Amber);
        }
        var head = (r.turret.head.min + r.turret.head.max) * .5f;
        // env_056: the merged giant core (FinaleBuilder) hangs between the original overhead key and the turret. A low
        // east key on the entry axis lights the turret's entry-facing side and the core's visible underside together.
        var key = new Vector3(head.x + 12, 12.5f, head.z);
        if (superseded055) Add("Turret_Key", new Vector3(head.x + 2, 24.8f, head.z), head, 20, 48, 5000, White, true);
        else Add("Turret_Key", key, new Vector3(head.x, 14.5f, head.z), 18, 56, 1500, White, true);
        var assembly = new Vector3(b.min.x + 10.5f, 11.3f, head.z);
        Add("Assembly_Key", assembly, assembly + Vector3.left, 10.4f, 100, 5000, new Color(.72f, .8f, 1));
        // Long throws follow each measured stair axis. Lights have no physical volume or collision.
        foreach (var stair in r.stairs)
        {
            var foot = stair.foot.position; var top = stair.top.position;
            var p = top + stair.axis * 3 + Vector3.up * 3;
            Add("Stair_" + stair.label.Replace(' ', '_'), p, foot + Vector3.up * 1.2f,
                16, 17, stair.fromTier == 0 ? 850 : 1200, Amber);
        }
        // Gallery washes run lengthwise, inside the north/south boundaries.
        foreach (int side in new[] { -1, 1 }) foreach (int end in new[] { -1, 1 })
        {
            float z = side < 0 ? b.min.z + 4.1f : b.max.z - 4.1f;
            var p = new Vector3(b.center.x + end * 22, 15.5f, z);
            Add("Gallery_" + (side < 0 ? "South_" : "North_") + (end < 0 ? "West" : "East"), p,
                p + new Vector3(-end * 15, -4.3f, 0), 23, 17, 1000, Amber);
        }
        Add("Entry_Key", new Vector3(b.max.x - 3.2f, 15.5f, r.entryEye.z),
            new Vector3(b.max.x - 5, r.floorTopY, r.entryEye.z), 10.5f, 26, 700, Amber);
        foreach (int side in new[] { -1, 1 })
        {
            var p = new Vector3(b.center.x + side * 20, 25.2f, b.center.z);
            Add("Upper_Warning_" + (side < 0 ? "West" : "East"), p, p + Vector3.down, 10, 90, 900, Red);
        }
        // Contract-derived positions remain valid during an isolated parent clear/Undo. When the
        // machinery exists, also require equality with env_054's measured public reservations.
        var frame = BossHallServices.Measure(layout);
        var reserved = new[] {
            (name: "Warning_Flicker_Ring", position: new Vector3(frame.backdrop.wallX + 2.08f, frame.backdrop.ringY, frame.backdrop.axisZ), colour: new Color(.55f, .45f, 1)),
            (name: "Warning_Flicker_South", position: new Vector3(frame.runwayX, frame.runwayY0 - .97f, frame.FeederZ(0)), colour: new Color(1, .6f, .22f)),
            (name: "Warning_Flicker_North", position: new Vector3(frame.runwayX, frame.runwayY0 - .97f, frame.FeederZ(1)), colour: new Color(1, .6f, .22f)) };
        if (BossHallMeltdown.AnchorsPresent())
            foreach (var actual in BossHallMeltdown.FlickerReservations(layout))
                if (Vector3.Distance(actual.position, reserved.Single(f => f.name == actual.name).position) > .002f)
                    throw new InvalidOperationException("Meltdown flicker reservation changed; review the lighting binding: " + actual.name);
        foreach (var f in reserved)
        {
            bool ring = f.name == "Warning_Flicker_Ring";
            // The reserved ring lamp washes back onto the stator; feeder lamps illuminate their machinery below.
            Add(f.name, f.position, f.position + (ring ? Vector3.left : Vector3.down), ring ? 2 : 4,
                ring ? 100 : 48, ring ? 90 : 180, f.colour, false, ring ? 1 : 2);
        }
        if (result.Count > 24) throw new InvalidOperationException("Architectural light budget exceeded");
        return result;
    }

    public static Light[] Lights() => BossHallFramework.FindSection(Section.Lighting).GetComponentsInChildren<Light>(true);
    internal static void Populate(Transform root, BossHallFramework.Layout layout)
    {
        var field = AssetDatabase.LoadAssetAtPath<Material>(FieldPath);
        if (!field) throw new InvalidOperationException("Missing saved containment field");
        foreach (var p in Plan(layout))
        {
            var go = new GameObject(p.name); Undo.RegisterCreatedObjectUndo(go, "Build Boss Hall lighting");
            go.transform.SetParent(root, false); go.transform.SetPositionAndRotation(p.position, Quaternion.LookRotation(p.direction));
            var light = go.AddComponent<Light>(); light.type = LightType.Spot;
            light.range = p.range; light.spotAngle = p.angle; light.innerSpotAngle = p.angle * .78f;
            light.intensity = p.intensity; light.color = p.colour; light.lightmapBakeType = LightmapBakeType.Realtime;
            light.renderMode = LightRenderMode.ForcePixel; light.shadows = p.shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowBias = .025f; light.shadowNormalBias = .12f;
            var data = go.AddComponent<UniversalAdditionalLightData>();
            using (var so = new SerializedObject(data))
            { so.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
            if (p.flicker == 1) go.AddComponent<ReactorInstabilityLight>().Configure(field, .65f, 1.3f);
            if (p.flicker == 2) go.AddComponent<WorkshopAmbientMotion>().ConfigureLamp(light, p.name.EndsWith("South", StringComparison.Ordinal) ? 0 : 2.1f);
        }
    }

    // Exact light-only placements may enter a firing reservation: they contain no physical/rendered obstruction.
    // This does not exempt fixtures, descendants, colliders, or a light moved away from its recipe.
    internal static bool IsApprovedLightObject(Transform t, BossHallFramework.Layout layout)
    {
        if (t.parent != BossHallFramework.FindSection(Section.Lighting) || t.childCount != 0 || !t.GetComponent<Light>()) return false;
        return AcceptedPlans(layout).Any(plan => Matches(t, plan.SingleOrDefault(x => x.name == t.name)));
    }
    static bool Matches(Transform t, Recipe p)
    {
        return p != null && Vector3.Distance(t.position, p.position) < .001f &&
            Quaternion.Angle(t.rotation, Quaternion.LookRotation(p.direction)) < .01f && t.localScale == Vector3.one &&
            t.GetComponents<Component>().All(c => c is Transform || c is Light || c is UniversalAdditionalLightData ||
                (p.flicker == 1 && c is ReactorInstabilityLight) || (p.flicker == 2 && c is WorkshopAmbientMotion));
    }
    internal static bool IsApprovedBinding(Component c, BossHallFramework.Layout layout)
        => (c is ReactorInstabilityLight || c is WorkshopAmbientMotion) && IsApprovedLightObject(c.transform, layout);

    public static void ValidateInstalled(BossHallFramework.Layout layout)
    {
        InvalidOperationException first = null;
        foreach (var plan in AcceptedPlans(layout))
            try { ValidateInstalled(layout, plan); return; }
            catch (InvalidOperationException ex) { first = first ?? ex; }
        throw first;
    }
    static void ValidateInstalled(BossHallFramework.Layout layout, List<Recipe> plan)
    {
        var root = BossHallFramework.FindSection(Section.Lighting); if (root.childCount == 0) return;
        var lights = Lights();
        if (lights.Length != plan.Count || root.childCount != plan.Count) throw new InvalidOperationException("Lighting recipe count changed");
        foreach (var p in plan)
        {
            var t = root.Find(p.name); var l = t ? t.GetComponent<Light>() : null;
            if (!t || !l || !IsApprovedLightObject(t, layout) || !t.gameObject.activeSelf || t.gameObject.isStatic || t.gameObject.layer != 0 || t.tag != "Untagged" ||
                !l.enabled || l.type != LightType.Spot || l.lightmapBakeType != LightmapBakeType.Realtime || l.cullingMask != -1 ||
                Mathf.Abs(l.range - p.range) > .001f || Mathf.Abs(l.spotAngle - p.angle) > .001f ||
                Mathf.Abs(l.innerSpotAngle - p.angle * .78f) > .001f || (!Application.isPlaying && Mathf.Abs(l.intensity - p.intensity) > .001f) || l.color != p.colour ||
                l.shadows != (p.shadows ? LightShadows.Soft : LightShadows.None) || l.renderMode != LightRenderMode.ForcePixel)
                throw new InvalidOperationException("Unapproved architectural light: " + p.name);
            var scripts = t.GetComponents<MonoBehaviour>().Where(c => !(c is UniversalAdditionalLightData)).ToArray();
            if (scripts.Length != (p.flicker == 0 ? 0 : 1)) throw new InvalidOperationException("Flicker binding count changed: " + p.name);
            if (p.flicker == 1)
            {
                var c = t.GetComponent<ReactorInstabilityLight>();
                if (!c || !c.enabled || AssetDatabase.GetAssetPath(c.Field) != FieldPath || c.Minimum != .65f || c.Maximum != 1.3f)
                    throw new InvalidOperationException("Invalid ring flicker binding");
            }
            if (p.flicker == 2)
            {
                var c = t.GetComponent<WorkshopAmbientMotion>();
                if (!c || !c.enabled) throw new InvalidOperationException("Missing feeder flicker binding");
                using var so = new SerializedObject(c);
                if (so.FindProperty("kind").enumValueIndex != (int)WorkshopAmbientMotion.MotionKind.Lamp || so.FindProperty("lamp").objectReferenceValue != l ||
                    so.FindProperty("movingPart").objectReferenceValue || so.FindProperty("display").objectReferenceValue ||
                    so.FindProperty("minimum").floatValue != .45f || so.FindProperty("maximum").floatValue != 1 ||
                    so.FindProperty("phase").floatValue != (p.name.EndsWith("South", StringComparison.Ordinal) ? 0 : 2.1f))
                    throw new InvalidOperationException("Feeder flicker must affect only its own lamp");
            }
            var data = t.GetComponent<UniversalAdditionalLightData>();
            if (!data) throw new InvalidOperationException("Missing URP light settings");
            using var lightData = new SerializedObject(data);
            if (lightData.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue != 0)
                throw new InvalidOperationException("Unapproved shadow resolution");
        }
    }

    internal static Dictionary<string, string> Protected() => BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindSection(Section.Lighting));

    [MenuItem("Tools/CoreBuster/Build Boss Hall Lighting")]
    public static void BuildMenu()
    {
        var layout = BossHallFramework.MeasureLayout();
        Build(layout);
    }
    static void Build(BossHallFramework.Layout layout)
    {
        BossHallRendering.Validate(layout); BossHallFramework.Validate(layout); BossHallMeltdown.ValidateInstalled(layout);
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        try
        {
            BossHallFramework.RebuildSection(layout, Section.Lighting, Owner, root => Populate(root, layout));
            string report = BossHallLightingReview.Analyse(layout, out int errors);
            Write("Lighting_055.txt", report);
            if (errors != 0) throw new InvalidOperationException("Lighting validation failed; rolled back. See Docs/BossHall/Lighting_055.txt");
            Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }
    public static void BuildBatch()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Lighting authoring requires graphics for boundary captures; omit -nographics");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var before = Protected(); var assets = BossHallSnapshot.CaptureAssets();
        var layout = BossHallFramework.MeasureLayout(); Build(layout); VerifyGuards(layout);
        string capture = BossHallLightingReview.Capture(layout, "BeforeSave");
        Write("LightingCapture_055.txt", capture);
        if (capture.Contains("FAIL:")) throw new InvalidOperationException("Lighting boundary capture failed; scene not saved");
        BossHallSnapshot.Compare(before, Protected(), "Outside Lighting");
        BossHallSnapshot.Compare(assets, BossHallSnapshot.CaptureAssets(), "Protected assets");
        layout = BossHallFramework.SaveAndReload(layout); ValidateInstalled(layout);
        string report = BossHallLightingReview.Analyse(layout, out int errors);
        BossHallSnapshot.Compare(before, Protected(), "Saved outside Lighting");
        BossHallSnapshot.Compare(assets, BossHallSnapshot.CaptureAssets(), "Saved protected assets");
        Write("Lighting_055.txt", report + $"\nSaved/reloaded; {before.Count} outside states and {assets.Count} protected assets unchanged. Negative guards and clear/Undo passed.\n");
        if (errors != 0) throw new InvalidOperationException("Saved lighting validation failed");
    }
    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); BossHallFramework.Validate(layout);
        if (Lights().Length == 0) throw new InvalidOperationException("Lighting is not installed");
        string report = BossHallLightingReview.Analyse(layout, out int errors); Write("LightingReload_055.txt", report);
        if (errors != 0) throw new InvalidOperationException("Lighting validation failed");
    }
    // Safe while the gameplay prerequisite is pending: create only transient objects, analyse, discard, compare bytes/state.
    public static void PreflightBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); var before = Protected(); var shared = BossHallSnapshot.CaptureAssets();
        string report = "";
        try
        {
            Debug.Log("ENV055: validating baseline and populating transient Lighting");
            BossHallRendering.Validate(layout); BossHallFramework.Validate(layout); BossHallMeltdown.ValidateInstalled(layout);
            BossHallFramework.RebuildSection(layout, Section.Lighting, Owner, root => Populate(root, layout));
            report = BossHallLightingReview.Analyse(layout, out int errors);
            Write("LightingPreflight_055.txt", report + "\nIntermediate proposal analysis; guards/captures pending.\n");
            Debug.Log("ENV055: testing guards and clear/Undo");
            VerifyGuards(layout);
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) report += "\n" + BossHallLightingReview.Capture(layout, "Proposed");
            report += "\nPROPOSAL ONLY. No scene/assets saved. Analysis errors: " + errors + ".\n";
        }
        finally { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); }
        BossHallSnapshot.Compare(before, Protected(), "Preflight outside Lighting");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Preflight protected assets");
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != layout.sceneSha) throw new InvalidOperationException("Preflight changed scene bytes");
        Write("LightingPreflight_055.txt", report);
    }
    static void VerifyGuards(BossHallFramework.Layout layout)
    {
        void Reject(Action mutation, Action restore)
        {
            bool rejected = false;
            try { mutation(); try { ValidateInstalled(layout); } catch (InvalidOperationException) { rejected = true; } }
            finally { restore(); }
            if (!rejected) throw new InvalidOperationException("Lighting negative guard failed");
        }
        var light = Lights().First(); var p = light.transform.position; float range = light.range;
        Reject(() => light.transform.position += Vector3.right, () => light.transform.position = p);
        Reject(() => light.range += 10, () => light.range = range);
        Reject(() => light.gameObject.SetActive(false), () => light.gameObject.SetActive(true));
        BoxCollider collider = null;
        Reject(() => collider = light.gameObject.AddComponent<BoxCollider>(), () => Object.DestroyImmediate(collider));
        var c = Lights().Single(l => l.name == "Warning_Flicker_Ring").GetComponent<ReactorInstabilityLight>(); var field = c.Field;
        Reject(() => c.Configure(null, .65f, 1.3f), () => c.Configure(field, .65f, 1.3f));
        var root = BossHallFramework.FindSection(Section.Lighting); int count = root.childCount;
        BossHallFramework.RebuildSection(layout, Section.Lighting, Owner, _ => { });
        if (root.childCount != 0) throw new InvalidOperationException("Lighting clear failed");
        Undo.PerformUndo();
        if (root.childCount != count) throw new InvalidOperationException("Lighting Undo failed");
        BossHallFramework.Validate(layout);
    }
    internal static void Write(string file, string text)
    {
        file = file.Replace("_055", "_" + Revision);
        Directory.CreateDirectory("Docs/BossHall"); File.WriteAllText("Docs/BossHall/" + file, text); Debug.Log(text);
    }
    static void Revise() => SessionState.SetString(RevisionKey, "056");
    /// <summary>env_056 refinement entry points: the same owner build, capture, validation and runtime probe, with *_056 evidence.</summary>
    public static void BuildRevealBatch() { Revise(); BuildBatch(); }
    public static void CaptureRevealBatch() { Revise(); BossHallLightingReview.CaptureBatch(); }
    public static void ValidateRevealBatch() { Revise(); ValidateBatch(); }
    public static void RuntimeRevealBatch() { Revise(); BossHallLightingRuntime.RunBatch(); }
}
