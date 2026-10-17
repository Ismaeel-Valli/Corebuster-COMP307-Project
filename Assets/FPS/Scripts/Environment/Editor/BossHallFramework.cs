using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>corebuster_env_048: empty, independently owned environment containers and fail-closed authoring transactions.</summary>
public static class BossHallFramework
{
    static System.Collections.Generic.Dictionary<string, string> baselineDiagnostic;
    public const string RootPath = "Level/Boss_Hall_Dressing", AssetRoot = "Assets/FPS/Art/BossHall";
    public const string ContractPath = "Assets/FPS/Scripts/Environment/Editor/BossHallLayout.json";
    public const string InitialSceneSha = "4ab15c73f6c69a93ae9ddccdc48e89a18d81edd656ea942816e8c358440dd0af";
    public enum Section { AssemblyBackdrop, MechanicalArms, PowerServices, CoolingServices, CatwalkDressing,
        LowerArenaDressing, Overhead, MeltdownVfx, Lighting, Wayfinding, Audio, Rendering }

    public sealed class SectionSpec
    {
        public readonly Section section;
        public readonly string name, owner;
        public readonly int assemblies, prefabs, lights, emitters, materialSlots;
        public SectionSpec(Section section, string name, string owner, int assemblies, int prefabs, int lights, int emitters, int materials)
        { this.section = section; this.name = name; this.owner = owner; this.assemblies = assemblies;
            this.prefabs = prefabs; this.lights = lights; this.emitters = emitters; materialSlots = materials; }
    }
    public static readonly IReadOnlyList<SectionSpec> Sections = Array.AsReadOnly(new[] {
        new SectionSpec(Section.AssemblyBackdrop, "Assembly_Backdrop", "corebuster_env_049", 4, 10, 0, 0, 3),
        new SectionSpec(Section.MechanicalArms, "Mechanical_Arms", "corebuster_art_006", 2, 8, 0, 0, 2),
        new SectionSpec(Section.PowerServices, "Power_Services", "corebuster_env_050", 2, 10, 0, 0, 2),
        new SectionSpec(Section.CoolingServices, "Cooling_Services", "corebuster_env_050", 2, 10, 0, 0, 2),
        new SectionSpec(Section.CatwalkDressing, "Catwalk_Dressing", "corebuster_env_051", 0, 10, 0, 0, 1),
        new SectionSpec(Section.LowerArenaDressing, "Lower_Arena_Dressing", "corebuster_env_052", 2, 8, 0, 0, 1),
        new SectionSpec(Section.Overhead, "Overhead", "corebuster_env_050", 0, 8, 0, 0, 1),
        new SectionSpec(Section.MeltdownVfx, "Meltdown_VFX", "corebuster_env_054", 0, 0, 0, 6, 2),
        new SectionSpec(Section.Lighting, "Lighting", "corebuster_env_055", 0, 0, 24, 0, 2),
        new SectionSpec(Section.Wayfinding, "Wayfinding", "corebuster_env_057", 0, 8, 0, 0, 2),
        new SectionSpec(Section.Audio, "Audio", "corebuster_audio_003", 0, 0, 0, 0, 0),
        new SectionSpec(Section.Rendering, "Rendering", "corebuster_render_010", 0, 0, 0, 0, 2),
    });

    [Serializable] public sealed class Contract
    {
        public string sourceSceneSha, protectedSceneDigest, protectedAssetDigest;
        public int protectedStateCount, protectedAssetCount;
        public BossHallSurvey.Report survey;
    }
    public sealed class Layout
    {
        public readonly Contract contract;
        public readonly Bounds interior;
        public readonly string sceneSha, contractSha;
        internal readonly string contractMemorySha;
        public BossHallSurvey.Report Report => contract.survey;
        internal Layout(Contract contract)
        { this.contract = contract; interior = contract.survey.interior.ToBounds();
            sceneSha = BossHallSurvey.Hash(BossHallSurvey.ScenePath); contractSha = BossHallSurvey.Hash(ContractPath);
            contractMemorySha = BossHallSnapshot.HashText(JsonUtility.ToJson(contract)); }

        public string KeepOutViolation(Bounds bounds)
        {
            if (!Finite(bounds.min) || !Finite(bounds.max) || bounds.size.x < 0 || bounds.size.y < 0 || bounds.size.z < 0)
                return "invalid bounds";
            if (bounds.size.x == 0 || bounds.size.y == 0 || bounds.size.z == 0) bounds.Expand(.002f);
            var room = interior; room.Expand(.02f);
            if (!room.Contains(bounds.min) || !room.Contains(bounds.max)) return "outside measured hall";
            foreach (var zone in Report.zones.Where(z => z.kind == "reserved-volume" || z.kind == "column" || z.kind == "reserved"))
                if (Overlap(bounds, Box(zone.min, zone.max))) return zone.name;
            foreach (var actor in Report.actors.Where(a => a.kind != "turret"))
                if (Overlap(bounds, actor.envelope.ToBounds())) return actor.name + " start envelope";
            var approach = Box(new Vector3(interior.max.x - 8, Report.floorTopY, Report.entry.clearMin - 1),
                new Vector3(interior.max.x, Report.floorTopY + 2.6f, Report.entry.clearMax + 1));
            if (Overlap(bounds, approach)) return "entry approach";
            foreach (var route in Report.routes.Where(r => r.connector && r.corners.Length > 1 && r.corners.All(p => Mathf.Abs(p.y - Report.floorTopY) < 1)))
                for (int i = 1; i < route.corners.Length; i++)
                {
                    var a = route.corners[i - 1]; var b = route.corners[i];
                    if (bounds.min.y >= Report.floorTopY + 2.6f) continue;
                    // Sampling conservatively expands the measured half-width by half a sample interval.
                    int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .1f));
                    for (int j = 0; j <= count; j++)
                    {
                        var p = Vector3.Lerp(a, b, j / (float)count);
                        float dx = Mathf.Max(bounds.min.x - p.x, 0, p.x - bounds.max.x);
                        float dz = Mathf.Max(bounds.min.z - p.z, 0, p.z - bounds.max.z);
                        if (dx * dx + dz * dz < 1.55f * 1.55f) return "connector " + route.name;
                    }
                }
            return null;
        }
    }

    [MenuItem("Tools/CoreBuster/Build Boss Hall Framework")]
    public static void BuildMenu() { var layout = MeasureLayout(); Build(layout); Selection.activeTransform = FindRoot(); }
    [MenuItem("Tools/CoreBuster/Validate Boss Hall Framework")]
    public static void ValidateMenu() { Validate(MeasureLayout()); Debug.Log("BOSS HALL FRAMEWORK CHECKS PASSED"); }
    [MenuItem("Tools/CoreBuster/Report Boss Hall Budgets")]
    public static void ReportMenu() { var layout = MeasureLayout(); Validate(layout); Debug.Log(BudgetReport()); }

    public static Layout MeasureLayout()
    {
        RequireScene(true);
        var contract = JsonUtility.FromJson<Contract>(File.ReadAllText(ContractPath));
        if (contract == null || contract.sourceSceneSha != InitialSceneSha || contract.survey == null ||
            contract.survey.sceneSha256 != InitialSceneSha || contract.survey.actors.Count != 11 || contract.survey.stairs.Count != 8 ||
            contract.survey.bayDoors.Count != 2 || contract.survey.deckFootprints.Count == 0 || contract.survey.zones.Count == 0)
            throw new InvalidOperationException("Missing or invalid accepted env_047 contract; re-survey and review before authoring.");
        var layout = new Layout(contract);
        RequireAnchors(layout);
        VerifyBaseline(layout);
        return layout;
    }

    // Strict one-time initialization is only possible on the accepted, byte-identical survey scene.
    // Subsequent scene changes require explicit contract reconciliation, never automatic baseline adoption.
    public static void InitializeBaseline()
    {
        RequireScene(true);
        var contract = JsonUtility.FromJson<Contract>(File.ReadAllText(ContractPath));
        if (!string.IsNullOrEmpty(contract.protectedSceneDigest)) return;
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != InitialSceneSha || FindRoot())
            throw new InvalidOperationException("Cannot initialize baseline on a changed scene.");
        var layout = new Layout(contract); RequireAnchors(layout);
        Debug.Log("BOSS HALL: capturing accepted protected baseline");
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), null);
        baselineDiagnostic = states;
        var assets = BossHallSnapshot.CaptureAssets();
        contract.protectedSceneDigest = BossHallSnapshot.Digest(states); contract.protectedStateCount = states.Count;
        contract.protectedAssetDigest = BossHallSnapshot.Digest(assets); contract.protectedAssetCount = assets.Count;
        File.WriteAllText(ContractPath, JsonUtility.ToJson(contract, true) + "\n");
        AssetDatabase.ImportAsset(ContractPath);
        Debug.Log("BOSS HALL: baseline initialized: " + states.Count + " states, " + assets.Count + " assets/meta/settings");
    }

    public static void Build(Layout layout)
    {
        RequireCurrent(layout); RequireAnchors(layout); VerifyBaseline(layout); PreflightContainers();
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Build Boss Hall framework");
        try
        {
            var level = Unique(SceneManager.GetActiveScene().GetRootGameObjects().Select(g => g.transform), "Level", true);
            var root = FindRoot(); bool changed = false;
            if (!root)
            {
                root = Create("Boss_Hall_Dressing", level);
                root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.localScale = Vector3.one; // Level's unit scale is checked before creating anything.
                changed = true;
            }
            foreach (var spec in Sections) if (!Unique(root.Cast<Transform>(), spec.name, false)) { Create(spec.name, root); changed = true; }
            ValidateContent(layout);
            if (changed) VerifyBaseline(layout);
            Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    /// <summary>Only the owner's children are replaced. Failed builds restore the scene through the same Undo group.
    /// The callback must register created objects and record changed objects with Undo. Asset writes need separate ownership.</summary>
    public static void RebuildSection(Layout layout, Section section, string ownerTaskId, Action<Transform> build)
    {
        var spec = Spec(section);
        if (spec.owner != ownerTaskId) throw new InvalidOperationException(spec.name + " is owned by " + spec.owner);
        if (build == null) throw new ArgumentNullException(nameof(build));
        Validate(layout);
        var target = FindSection(section);
        bool restoreEnergy = BossHallEnergy.HasEnergy(target);
        // Empty framework is allowed while gameplay verification is pending. Any real content needs that separate gate.
        // The gate is intentionally not inferred or granted by this framework.
        var outside = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), target, FindRoot());
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Rebuild Boss Hall " + spec.name);
        try
        {
            foreach (Transform child in target.Cast<Transform>().ToArray()) Undo.DestroyObjectImmediate(child.gameObject);
            build(target); BossHallRendering.ApplyOwnedMaterials(target);
            if (restoreEnergy && target.childCount != 0) BossHallEnergy.RestoreSection(layout, section);
            Physics.SyncTransforms();
            BossHallSnapshot.Compare(outside, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), target, FindRoot()), "Unowned sections");
            VerifyBaseline(layout);
            ValidateContent(layout); Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static Layout SaveAndReload(Layout layout)
    {
        RequireCurrent(layout); Validate(layout);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("MainScene save failed.");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var reloaded = MeasureLayout(); Validate(reloaded); return reloaded;
    }

    public static void Validate(Layout layout)
    {
        RequireCurrent(layout); RequireAnchors(layout); VerifyBaseline(layout); ValidateContent(layout);
    }
    static void ValidateContent(Layout layout)
    {
        PreflightContainers();
        var root = FindRoot() ?? throw new InvalidOperationException("Missing " + RootPath);
        Container(root, false);
        var expected = new HashSet<string>(Sections.Select(s => s.name));
        foreach (Transform child in root) if (!expected.Contains(child.name)) throw new InvalidOperationException("Unowned section " + child.name);
        foreach (var spec in Sections)
        {
            var section = FindSection(spec.section); Container(section, true);
            foreach (var t in section.GetComponentsInChildren<Transform>(true))
            {
                if (t.Cast<Transform>().GroupBy(c => c.name, StringComparer.Ordinal).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("Ambiguous generated child name at " + PathOf(t));
                if (!Finite(t.position) || !Finite(t.lossyScale)) throw new InvalidOperationException("Invalid transform " + PathOf(t));
                if (t != section && !t.GetComponentsInChildren<Renderer>(true).Any() && !t.GetComponentsInChildren<Collider>(true).Any() &&
                    !(spec.section == Section.Lighting && BossHallLighting.IsApprovedLightObject(t, layout)))
                    RejectBounds(layout, new Bounds(t.position, Vector3.zero), PathOf(t));
                foreach (var c in t.GetComponents<Component>())
                {
                    if (!c) throw new InvalidOperationException("Missing script " + PathOf(t));
                    if (c is Transform || c is Renderer || c is MeshFilter || c is Collider || c is NavMeshObstacle ||
                        c is Light || c is AudioSource || c is ParticleSystem || c is ParticleSystemForceField ||
                        c is UnityEngine.Rendering.Universal.UniversalAdditionalLightData) continue;
                    if (spec.section == Section.Lighting && BossHallLighting.IsApprovedBinding(c, layout)) continue;
                    // Later visual-only components get a scoped explicit allowlist change and their own verification.
                    throw new InvalidOperationException("Unapproved decorative component " + c.GetType().FullName + " at " + PathOf(t));
                }
            }
            foreach (var r in section.GetComponentsInChildren<Renderer>(true))
                if (spec.section != Section.Rendering || !BossHallRendering.IsApprovedSkin(r, layout))
                    // Particle renderer bounds depend on transient simulation; env_054 supplies its recipe envelope.
                    RejectBounds(layout, spec.section == Section.MeltdownVfx ? BossHallMeltdown.RendererEnvelope(r, layout) : r.bounds, PathOf(r.transform));
            foreach (var c in section.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled || !c.gameObject.activeInHierarchy) throw new InvalidOperationException("Disabled decorative collider must be removed: " + PathOf(c.transform));
                if (c.isTrigger || spec.section == Section.Rendering || spec.section == Section.MeltdownVfx)
                    throw new InvalidOperationException("Unapproved decorative collider " + PathOf(c.transform));
                RejectBounds(layout, c.bounds, PathOf(c.transform));
            }
            foreach (var o in section.GetComponentsInChildren<NavMeshObstacle>(true))
            {
                if (!o.enabled || !o.carving) throw new InvalidOperationException("Obstacle must be enabled and carving: " + PathOf(o.transform));
                var size = o.shape == NavMeshObstacleShape.Box ? o.size : new Vector3(o.radius * 2, o.height, o.radius * 2);
                var b = new Bounds(o.transform.TransformPoint(o.center), Vector3.zero);
                for (int i = 0; i < 8; i++) b.Encapsulate(o.transform.TransformPoint(o.center + Vector3.Scale(size * .5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                RejectBounds(layout, b, PathOf(o.transform));
            }
            foreach (var l in section.GetComponentsInChildren<Light>(true))
            {
                if (spec.section != Section.Lighting || l.type == LightType.Directional) throw new InvalidOperationException("Light outside architectural owner or directional light");
                if (!BossHallLighting.IsApprovedLightObject(l.transform, layout))
                    RejectBounds(layout, new Bounds(l.transform.position, Vector3.zero), PathOf(l.transform));
            }
            if (spec.section != Section.Audio && section.GetComponentsInChildren<AudioSource>(true).Length != 0)
                throw new InvalidOperationException("AudioSource outside Audio section");
            if (spec.section != Section.MeltdownVfx && section.GetComponentsInChildren<ParticleSystem>(true).Length != 0)
                throw new InvalidOperationException("Emitter outside Meltdown_VFX");
            CheckBudget(section, spec);
        }
        ValidateMaterials();
        BossHallEnergy.ValidateInstalled(layout);
        BossHallMeltdown.ValidateInstalled(layout);
        BossHallLighting.ValidateInstalled(layout);
        BossHallGraphics.ValidateInstalled(layout);
        int materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count();
        if (materials > 20) throw new InvalidOperationException("Boss Hall exceeds 20 unique materials");
    }

    public static void ValidateMaterials()
    {
        var root = FindRoot();
        foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
            foreach (var m in r.sharedMaterials)
            {
                bool inside = root && r.transform.IsChildOf(root);
                if (!m) { if (inside) throw new InvalidOperationException("Missing material " + PathOf(r.transform)); continue; }
                string path = AssetDatabase.GetAssetPath(m);
                bool owned = path.StartsWith(AssetRoot + "/", StringComparison.Ordinal);
                if (inside && (!owned || m.parent)) throw new InvalidOperationException("Dressing needs flattened Boss Hall material: " + path);
                if (inside && (!m.shader || m.shader.name.Contains("/2D/") || m.shader.name.StartsWith("Hidden/", StringComparison.Ordinal) ||
                    m.shader.name == "Standard" || m.shader.name.StartsWith("Legacy Shaders/", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Unsuitable Boss Hall mesh shader: " + path);
                if (!inside && owned) throw new InvalidOperationException("Boss Hall material assigned outside dressing: " + PathOf(r.transform));
                if (!owned && EditorUtility.IsDirty(m)) throw new InvalidOperationException("Protected material has unsaved edits: " + path);
            }
    }

    public static void RequireOwnedAsset(UnityEngine.Object asset, Section section)
    {
        string path = asset ? AssetDatabase.GetAssetPath(asset) : "";
        if (!path.StartsWith(AssetRoot + "/" + Spec(section).name + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("Generated asset must belong to " + Spec(section).name + ": " + path);
    }
    public static SectionSpec Spec(Section section) => Sections.Single(s => s.section == section);
    public static Transform FindRoot()
    {
        var level = Unique(SceneManager.GetActiveScene().GetRootGameObjects().Select(g => g.transform), "Level", true);
        return Unique(level.Cast<Transform>(), "Boss_Hall_Dressing", false);
    }
    public static Transform FindSection(Section section) => Unique((FindRoot() ?? throw new InvalidOperationException("Missing framework")).Cast<Transform>(), Spec(section).name, true);

    public static string BudgetReport()
    {
        var lines = new List<string> { "BOSS HALL BUDGETS (provisional ceilings; material shares are planning reservations)" };
        foreach (var spec in Sections)
        {
            var t = FindSection(spec.section);
            lines.Add(spec.name + " [" + spec.owner + "]: assemblies " + (spec.assemblies > 0 ? t.childCount : 0) + "/" + spec.assemblies +
                ", prefabs including nested " + Prefabs(t) + "/" + spec.prefabs + ", renderers " + t.GetComponentsInChildren<Renderer>(true).Length +
                ", colliders " + t.GetComponentsInChildren<Collider>(true).Length + ", carving " + t.GetComponentsInChildren<NavMeshObstacle>(true).Count(o => o.carving) +
                ", lights " + t.GetComponentsInChildren<Light>(true).Length + "/" + spec.lights + ", emitters " + t.GetComponentsInChildren<ParticleSystem>(true).Length + "/" + spec.emitters +
                ", live particles " + t.GetComponentsInChildren<ParticleSystem>(true).Sum(p => p.particleCount) +
                ", assigned materials " + t.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count() +
                ", initial material planning share " + spec.materialSlots);
        }
        lines.Add("Whole pass: <=12 assembly roots, <=80 nested prefab instances, <=20 unique materials, <=24 architectural lights, <=6 emitters. Actor/core/weapon lights are separate; combined per-object limit remains 4.");
        return string.Join("\n", lines);
    }

    static void CheckBudget(Transform t, SectionSpec spec)
    {
        if (spec.assemblies > 0 && t.childCount > spec.assemblies || Prefabs(t) > spec.prefabs ||
            t.GetComponentsInChildren<Light>(true).Length > spec.lights || t.GetComponentsInChildren<ParticleSystem>(true).Length > spec.emitters)
            throw new InvalidOperationException("Section exceeds provisional budget: " + spec.name);
    }
    static int Prefabs(Transform t) => t.GetComponentsInChildren<Transform>(true).Count(c => PrefabUtility.IsAnyPrefabInstanceRoot(c.gameObject));
    static void VerifyBaseline(Layout layout)
    {
        var states = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), FindRoot());
        if (string.IsNullOrEmpty(layout.contract.protectedSceneDigest) || BossHallSnapshot.Digest(states) != layout.contract.protectedSceneDigest)
        {
            if (baselineDiagnostic != null) BossHallSnapshot.Compare(baselineDiagnostic, states, "Baseline diagnostic");
            throw new InvalidOperationException("Protected scene baseline changed; reconcile the survey/contract explicitly before authoring.");
        }
        if (BossHallSnapshot.Digest(BossHallSnapshot.CaptureAssets()) != layout.contract.protectedAssetDigest)
            throw new InvalidOperationException("Protected material/prefab/navigation/render/settings baseline changed; reconcile explicitly.");
    }
    static void RequireAnchors(Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        foreach (string name in new[] { BossHallSurvey.BossDoorName, BossHallSurvey.InclineName, "Master Terminal" }) Unique(all, name, true);
        foreach (string id in layout.Report.actors.Select(a => a.id).Concat(layout.Report.stairs.Select(s => s.id)).Concat(layout.Report.bayDoors.Select(d => d.id)))
        {
            if (!GlobalObjectId.TryParse(id, out var parsed)) throw new InvalidOperationException("Invalid survey anchor " + id);
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed);
            var t = obj is GameObject go ? go.transform : (obj as Component)?.transform;
            if (!t || t.gameObject.scene != scene || FindRoot() && t.IsChildOf(FindRoot())) throw new InvalidOperationException("Missing or adopted survey anchor " + id);
        }
        foreach (var actor in layout.Report.actors)
        {
            GlobalObjectId.TryParse(actor.id, out var id);
            var t = ((GameObject)GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id)).transform;
            if (Vector3.Distance(t.position, actor.position) > .001f || Vector3.Distance(t.lossyScale, actor.scale) > .001f || Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, actor.eulerY)) > .01f)
                throw new InvalidOperationException("Actor moved from accepted survey: " + actor.name);
        }
    }
    static void PreflightContainers()
    {
        var level = Unique(SceneManager.GetActiveScene().GetRootGameObjects().Select(g => g.transform), "Level", true);
        if (Vector3.Distance(level.lossyScale, Vector3.one) > .0001f || Quaternion.Angle(level.rotation, Quaternion.identity) > .001f)
            throw new InvalidOperationException("Level must retain unit scale/rotation; world offset is compensated only on the new root.");
        var root = FindRoot(); if (!root) return;
        Container(root, false);
        foreach (var spec in Sections) { var section = Unique(root.Cast<Transform>(), spec.name, false); if (section) Container(section, true); }
    }
    static void Container(Transform t, bool local)
    {
        if (t.GetComponents<Component>().Length != 1 || !t.gameObject.activeSelf || t.gameObject.layer != 0 || t.gameObject.tag != "Untagged")
            throw new InvalidOperationException("Container must be active, Default, Untagged and Transform-only: " + PathOf(t));
        var position = local ? t.localPosition : t.position; var rotation = local ? t.localRotation : t.rotation;
        if (position.sqrMagnitude > .00000001f || Quaternion.Angle(rotation, Quaternion.identity) > .001f || Vector3.Distance(t.localScale, Vector3.one) > .0001f)
            throw new InvalidOperationException("Container identity changed: " + PathOf(t));
    }
    static Transform Create(string name, Transform parent)
    {
        var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Create Boss Hall " + name);
        Undo.SetTransformParent(go.transform, parent, "Parent Boss Hall " + name);
        Undo.RecordObject(go.transform, "Place Boss Hall " + name);
        go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
        return go.transform;
    }
    static void RequireScene(bool clean)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != BossHallSurvey.ScenePath || SceneManager.sceneCount != 1 || EditorApplication.isPlayingOrWillChangePlaymode || clean && scene.isDirty)
            throw new InvalidOperationException("Open a clean saved MainScene alone in Edit mode before measuring; do not discard edits.");
    }
    static void RequireCurrent(Layout layout)
    {
        RequireScene(false);
        if (layout == null || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != layout.sceneSha || BossHallSurvey.Hash(ContractPath) != layout.contractSha ||
            BossHallSnapshot.HashText(JsonUtility.ToJson(layout.contract)) != layout.contractMemorySha)
            throw new InvalidOperationException("Stale scene or layout contract; measure again before authoring.");
    }
    public static Transform Unique(IEnumerable<Transform> items, string name, bool required)
    {
        var found = items.Where(t => t.name == name).ToArray();
        if (found.Length > 1 || required && found.Length != 1) throw new InvalidOperationException("Missing or ambiguous anchor/container " + name + ": " + found.Length);
        return found.SingleOrDefault();
    }
    static void RejectBounds(Layout layout, Bounds bounds, string label)
    { string violation = layout.KeepOutViolation(bounds); if (violation != null) throw new InvalidOperationException(label + " enters " + violation); }
    static bool Finite(Vector3 v) => !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z);
    static bool Overlap(Bounds a, Bounds b) => a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y && a.min.z < b.max.z && a.max.z > b.min.z;
    static Bounds Box(Vector3 min, Vector3 max) { var b = new Bounds(); b.SetMinMax(min, max); return b; }
    public static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
}
