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
using Section = BossHallFramework.Section;

/// <summary>corebuster_env_053: contained mesh energy, never an executable core. No runtime components.</summary>
public static class BossHallEnergy
{
    public const string Owner = "corebuster_env_053", GroupName = "Energy_053";
    public const string Folder = BossHallFramework.AssetRoot + "/Energy", Output = "ReviewCaptures/BossHall/053";
    public static readonly Section[] Sections = { Section.AssemblyBackdrop, Section.MechanicalArms, Section.PowerServices, Section.Overhead };
    static readonly string[] Materials = { "Contained_Field", "Interface_Blue", "Feed_Teal" };
    static readonly string[] Shaders = { "CoreBuster/Reactor Containment", "CoreBuster/Core Energy Additive", "CoreBuster/Core Energy Additive" };

    public sealed class Piece
    {
        public Section section;
        public string root, name, mesh;
        public int material;
        public Vector3 position, scale = Vector3.one;
        public Quaternion rotation = Quaternion.identity;
    }
    static string MaterialPath(int i) => Folder + "/Materials/M_Energy_" + Materials[i] + ".mat";
    static string MeshPath(string name) => Folder + "/Meshes/SM_Energy_" + name + ".asset";
    public static IEnumerable<Transform> Groups() => Sections.SelectMany(s => BossHallFramework.FindSection(s).GetComponentsInChildren<Transform>(true))
        .Where(t => t.name == GroupName);
    public static bool HasEnergy(Transform section) => section.GetComponentsInChildren<Transform>(true).Any(t => t.name == GroupName);
    public static bool IsEnergyRenderer(Renderer renderer)
    {
        for (var t = renderer.transform; t; t = t.parent)
            if (t.name == GroupName && t.parent && Sections.Any(s => t.parent.parent == BossHallFramework.FindSection(s)) &&
                AllowedRoots().Any(p => p.Item1 == t.parent.parent && p.Item2 == t.parent.name)) return true;
        return false;
    }
    static IEnumerable<(Transform, string)> AllowedRoots()
    {
        yield return (BossHallFramework.FindSection(Section.AssemblyBackdrop), "Assembly_Ring");
        foreach (string name in BossHallMechanicalArms.Roots) yield return (BossHallFramework.FindSection(Section.MechanicalArms), name);
        foreach (var s in new[] { Section.PowerServices, Section.Overhead })
            foreach (string name in BossHallServices.Roots(s)) yield return (BossHallFramework.FindSection(s), name);
    }
    public static MeshRenderer[] Renderers() => Groups().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).ToArray();

    public static List<Piece> Plan(BossHallFramework.Layout layout)
    {
        var result = new List<Piece>(); var f = BossHallBackdrop.Measure(layout);
        if (BossHallFramework.FindSection(Section.AssemblyBackdrop).childCount != 0) result.Add(new Piece { section = Section.AssemblyBackdrop, root = "Assembly_Ring", name = "Contained_Stator_Field", mesh = "Stator_Sectors",
            position = new Vector3(f.wallX + .54f, f.ringY, f.axisZ) });
        for (int s = 0; s < 2; s++)
        {
            string tag = s == 0 ? "South" : "North";
            var arms = BossHallFramework.FindSection(Section.MechanicalArms).Find(BossHallMechanicalArms.Roots[s]);
            if (arms)
            {
                var frame = BossHallMechanicalArms.Measure(layout); float drop = frame.runwayBottom - frame.baseY, direction = s == 0 ? 1 : -1;
                foreach (float mountZ in new[] { -2.5f, 2.5f })
                    result.Add(new Piece { section = Section.MechanicalArms, root = arms.name, name = mountZ < 0 ? "Arm_A_Supply_Interface" : "Arm_B_Supply_Interface",
                        mesh = "Quad", material = 2, position = arms.TransformPoint(new Vector3(-.332f, drop - .12f, mountZ - .5f * direction)),
                        rotation = Quaternion.Euler(0, -90, 0), scale = new Vector3(.24f, .14f, 1) });
            }
            else if (BossHallFramework.FindSection(Section.MechanicalArms).childCount != 0) throw new InvalidOperationException("Missing arm energy parent " + tag);
            var power = BossHallFramework.FindSection(Section.PowerServices).Find(BossHallServices.Roots(Section.PowerServices)[s]);
            var door = power ? power.Find("Junction_Door_" + tag)?.GetComponent<MeshRenderer>() : null;
            if (!door && BossHallFramework.FindSection(Section.PowerServices).childCount != 0) throw new InvalidOperationException("Missing saved junction door " + tag);
            if (door)
            {
                var b = door.bounds; float inward = s == 0 ? 1 : -1;
                result.Add(new Piece { section = Section.PowerServices, root = power.name, name = "Junction_Interface", mesh = "Quad", material = 1,
                    position = new Vector3(b.center.x, b.center.y + .26f, (s == 0 ? b.max.z : b.min.z) + inward * .008f),
                    rotation = Quaternion.Euler(0, s == 0 ? 180 : 0, 0), scale = new Vector3(.4f, .4f, 1) });
            }
            var feeder = BossHallFramework.FindSection(Section.Overhead).Find(BossHallServices.Roots(Section.Overhead)[s]);
            var body = feeder ? feeder.Find("Overhead_Feeder_" + tag)?.GetComponent<MeshRenderer>() : null;
            if (!body && BossHallFramework.FindSection(Section.Overhead).childCount != 0) throw new InvalidOperationException("Missing saved feeder " + tag);
            if (!body) continue;
            // The east intake is the first box in the feeder recipe. Derive its room-facing surface from the saved mesh vertices.
            var verts = body.GetComponent<MeshFilter>().sharedMesh.vertices.Take(24).Select(v => body.transform.TransformPoint(v)).ToArray();
            var intake = new Bounds(verts[0], Vector3.zero); foreach (var v in verts) intake.Encapsulate(v);
            result.Add(new Piece { section = Section.Overhead, root = feeder.name, name = "Intake_Conductor_Strips", mesh = "Feed_Strips", material = 2,
                position = new Vector3(intake.min.x - .008f, intake.center.y, intake.center.z), rotation = Quaternion.Euler(0, 90, 0) });
        }
        foreach (var p in result)
        {
            var mesh = Compose(p.mesh); var bounds = WorldBounds(mesh.bounds, p); Object.DestroyImmediate(mesh);
            string issue = layout.KeepOutViolation(bounds); if (issue != null) throw new InvalidOperationException(p.name + " enters " + issue);
        }
        return result;
    }

    static Bounds WorldBounds(Bounds local, Piece p)
    {
        var matrix = Matrix4x4.TRS(p.position, p.rotation, p.scale); var b = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++) b.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return b;
    }
    static Mesh Compose(string name)
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int n = vertices.Count; vertices.AddRange(new[] { a, b, c, d }); uv.AddRange(new[] { ua, ub, uc, ud });
            triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }
        if (name == "Stator_Sectors")
        {
            // Six steel spokes are at 30+60i degrees. Leave angular gaps and a radial gap for the existing steel torus.
            for (int sector = 0; sector < 6; sector++)
                foreach (var radii in new[] { new Vector2(1.12f, 2.19f), new Vector2(2.50f, 3.08f) })
                    for (int i = 0; i < 20; i++)
                    {
                        float a = (-23 + sector * 60 + i * 46f / 20) * Mathf.Deg2Rad, b = (-23 + sector * 60 + (i + 1) * 46f / 20) * Mathf.Deg2Rad;
                        Vector3 At(float angle, float r) => new Vector3(0, Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
                        // Facing +X into the room; UV height keeps the containment shader's procedural coils visible.
                        Quad(At(a, radii.x), At(a, radii.y), At(b, radii.y), At(b, radii.x),
                            new Vector2(a / (2 * Mathf.PI), radii.x), new Vector2(a / (2 * Mathf.PI), radii.y),
                            new Vector2(b / (2 * Mathf.PI), radii.y), new Vector2(b / (2 * Mathf.PI), radii.x));
                    }
        }
        else if (name == "Quad" || name == "Feed_Strips")
        {
            int count = name == "Quad" ? 1 : 3;
            for (int i = 0; i < count; i++)
            {
                float x = count == 1 ? 0 : (i - 1) * .15f, w = count == 1 ? .5f : .032f, h = count == 1 ? .5f : .5f;
                Quad(new Vector3(x - w, -h, 0), new Vector3(x - w, h, 0), new Vector3(x + w, h, 0), new Vector3(x + w, -h, 0),
                    Vector2.zero, Vector2.up, Vector2.one, Vector2.right); // Visible -Z, matching Unity's quad.
            }
        }
        else throw new InvalidOperationException("Unknown energy mesh " + name);
        var mesh = new Mesh { name = "SM_Energy_" + name }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv);
        mesh.SetColors(Enumerable.Repeat(Color.white, vertices.Count).ToList()); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
    static Material ComposeMaterial(int i)
    {
        var shader = Shader.Find(Shaders[i]); if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Unavailable custom shader " + Shaders[i]);
        var m = new Material(shader) { name = "M_Energy_" + Materials[i] };
        if (i == 0)
        {
            m.SetColor("_Color", new Color(.21f, .06f, .8f)); m.SetColor("_Accent", new Color(.06f, .45f, 1)); m.SetColor("_Core", new Color(.42f, .72f, 1));
            m.SetFloat("_Intensity", 1.35f); m.SetFloat("_Instability", .3f); m.SetFloat("_ReviewTime", -1);
        }
        else
        {
            m.SetColor("_TintColor", i == 1 ? new Color(.07f, .3f, 1) : new Color(.03f, .8f, .65f));
            m.SetFloat("_Intensity", i == 1 ? 1.4f : 1.15f); m.SetFloat("_Shape", i == 1 ? 1 : 0);
            m.SetFloat("_RingWidth", .11f); m.SetFloat("_HotCore", .05f);
        }
        return m;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return; int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
    public static void BuildAssets()
    {
        EnsureFolder(Folder + "/Meshes"); EnsureFolder(Folder + "/Materials");
        foreach (string name in new[] { "Stator_Sectors", "Quad", "Feed_Strips" }) Store(Compose(name), MeshPath(name));
        for (int i = 0; i < Materials.Length; i++) Store(ComposeMaterial(i), MaterialPath(i));
        AssetDatabase.SaveAssets();
    }
    static void Store(Object generated, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else AssetDatabase.CreateAsset(generated, path);
    }
    static Transform Create(string name, Transform parent)
    {
        var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Build contained Boss Hall energy"); go.transform.SetParent(parent, false); return go.transform;
    }
    static void Populate(BossHallFramework.Layout layout, Section section)
    {
        foreach (var group in Plan(layout).Where(p => p.section == section).GroupBy(p => p.root))
        {
            var parent = BossHallFramework.FindSection(section).Find(group.Key) ?? throw new InvalidOperationException("Missing energy parent " + group.Key);
            if (parent.Find(GroupName)) throw new InvalidOperationException("Energy group already exists " + parent.name);
            var container = Create(GroupName, parent);
            foreach (var p in group)
            {
                var t = Create(p.name, container); t.SetPositionAndRotation(p.position, p.rotation); t.localScale = p.scale;
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(p.mesh)) ?? throw new InvalidOperationException("Prepare energy assets first");
                var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(p.material));
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }
    }
    public static void RestoreSection(BossHallFramework.Layout layout, Section section)
    { if (Sections.Contains(section)) Populate(layout, section); }
    static Dictionary<string, string> CaptureProtected() => BossHallSnapshot.CaptureExcluding(SceneManager.GetActiveScene(), Groups());
    public static void Rebuild(BossHallFramework.Layout layout, string owner = Owner)
    {
        if (owner != Owner) throw new InvalidOperationException("Energy child groups belong to " + Owner);
        BossHallFramework.Validate(layout); var before = CaptureProtected();
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Build Boss Hall contained energy");
        try
        {
            foreach (var t in Groups().ToArray()) Undo.DestroyObjectImmediate(t.gameObject);
            foreach (var section in Sections) Populate(layout, section);
            Physics.SyncTransforms(); BossHallFramework.Validate(layout);
            BossHallSnapshot.Compare(before, CaptureProtected(), "Protected machinery and scene"); Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    // Called by the framework; no recursive parent validation. Empty installation is allowed, partial installation is rejected.
    public static void ValidateInstalled(BossHallFramework.Layout layout)
    {
        var groups = BossHallFramework.FindRoot().GetComponentsInChildren<Transform>(true).Where(t => t.name == GroupName).ToArray();
        if (groups.Length == 0) return;
        var plan = Plan(layout); var expected = plan.Select(p => BossHallFramework.FindSection(p.section).Find(p.root)).ToArray();
        // Parent clear/Undo legitimately removes its group temporarily; require exact bindings for every populated owned parent.
        var populated = expected.Where(t => t).Distinct().ToArray();
        if (groups.Length != populated.Length || groups.Any(g => !populated.Contains(g.parent))) throw new InvalidOperationException("Missing, duplicate or unowned energy child group");
        foreach (var g in groups)
        {
            if (g.GetComponents<Component>().Length != 1 || g.localPosition != Vector3.zero || g.localRotation != Quaternion.identity || g.localScale != Vector3.one ||
                !g.gameObject.activeSelf || g.gameObject.layer != 0 || g.gameObject.tag != "Untagged") throw new InvalidOperationException("Energy container must retain identity");
            var pieces = plan.Where(p => p.root == g.parent.name && BossHallFramework.FindSection(p.section) == g.parent.parent).ToArray();
            if (g.childCount != pieces.Length) throw new InvalidOperationException("Unexpected energy children");
            foreach (var p in pieces)
            {
                var t = g.Find(p.name); if (!t || t.childCount != 0 || !t.gameObject.activeSelf || t.gameObject.layer != 0 || t.gameObject.tag != "Untagged" ||
                    t.GetComponents<Component>().Length != 3 || !t.GetComponent<MeshFilter>() || !t.GetComponent<MeshRenderer>() ||
                    Vector3.Distance(t.position, p.position) > .0001f || Quaternion.Angle(t.rotation, p.rotation) > .01f || Vector3.Distance(t.lossyScale, p.scale) > .0001f)
                    throw new InvalidOperationException("Energy may contain only exact transform/filter/renderer bindings: " + p.name);
                var r = t.GetComponent<MeshRenderer>(); var mesh = t.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh || AssetDatabase.GetAssetPath(mesh) != MeshPath(p.mesh) || EditorUtility.IsDirty(mesh) || !r.enabled ||
                    r.sharedMaterials.Length != 1 || AssetDatabase.GetAssetPath(r.sharedMaterial) != MaterialPath(p.material) || r.sharedMaterial.parent ||
                    r.shadowCastingMode != ShadowCastingMode.Off || r.receiveShadows || r.lightProbeUsage != LightProbeUsage.Off || r.reflectionProbeUsage != ReflectionProbeUsage.Off)
                    throw new InvalidOperationException("Invalid energy mesh/material/rendering binding: " + p.name);
                var recipe = Compose(p.mesh);
                try { if (!mesh.vertices.SequenceEqual(recipe.vertices) || !mesh.triangles.SequenceEqual(recipe.triangles) || !mesh.uv.SequenceEqual(recipe.uv) ||
                    !mesh.colors.SequenceEqual(recipe.colors) || !mesh.normals.SequenceEqual(recipe.normals)) throw new InvalidOperationException("Energy mesh differs from recipe"); }
                finally { Object.DestroyImmediate(recipe); }
                var expectedMaterial = ComposeMaterial(p.material); var m = r.sharedMaterial;
                try
                {
                    if (m.shader != expectedMaterial.shader || m.renderQueue != expectedMaterial.renderQueue || m.shaderKeywords.Length != 0) throw new InvalidOperationException("Unexpected energy shader settings");
                    foreach (string prop in p.material == 0 ? new[] { "_Intensity", "_Instability", "_ReviewTime" } : new[] { "_Intensity", "_Shape", "_RingWidth", "_HotCore" })
                        if (m.GetFloat(prop) != expectedMaterial.GetFloat(prop)) throw new InvalidOperationException("Energy material differs: " + prop);
                    foreach (string prop in p.material == 0 ? new[] { "_Color", "_Accent", "_Core" } : new[] { "_TintColor" })
                        if (m.GetColor(prop) != expectedMaterial.GetColor(prop)) throw new InvalidOperationException("Energy palette differs: " + prop);
                }
                finally { Object.DestroyImmediate(expectedMaterial); }
            }
        }
    }

    public static string Validate(BossHallFramework.Layout layout)
    {
        BossHallLowerArena.Validate(layout); // Includes services, arms, backdrop, rendering, catwalks and the framework.
        ValidateInstalled(layout); if (Groups().Count() != 7) throw new InvalidOperationException("Expected seven installed energy groups");
        var parts = Renderers(); var head = (layout.Report.turret.head.min + layout.Report.turret.head.max) * .5f;
        float d = Vector3.Distance(layout.Report.entryEye, head); var ray = new Ray(layout.Report.entryEye, (head - layout.Report.entryEye) / d);
        if (parts.Any(r => r.bounds.IntersectRay(ray, out float hit) && hit >= 0 && hit < d)) throw new InvalidOperationException("Energy overlaps the entry-to-turret sight line");
        int triangles = parts.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
        int mats = BossHallFramework.FindRoot().GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().Count();
        return $"BOSS HALL ENERGY: 7 owned nested groups, {parts.Length} renderers, {triangles} triangles, 3 owned materials, {mats}/20 hall materials. " +
            "Only Transform/MeshFilter/MeshRenderer; no runtime script/core/health/damage, collider/carve, light, emitter or audio. Entry-to-turret sight line clear.";
    }
    [MenuItem("Tools/CoreBuster/Build Boss Hall Contained Energy")]
    public static void BuildMenu()
    { var layout = BossHallFramework.MeasureLayout(); BossHallLowerArena.Validate(layout); BuildAssets(); Rebuild(layout); Debug.Log(Validate(layout)); }
    [MenuItem("Tools/CoreBuster/Validate Boss Hall Contained Energy")]
    public static void ValidateMenu() => Debug.Log(Validate(BossHallFramework.MeasureLayout()));

    public static void PreflightBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); var layout = BossHallFramework.MeasureLayout();
        BossHallServices.Validate(layout); BossHallCatwalk.Validate(layout); BossHallLowerArena.Validate(layout);
        var plan = Plan(layout); string report = "READ-ONLY ENV053 PREFLIGHT\nScene: " + layout.sceneSha + "\n" +
            string.Join("\n", plan.Select(p => p.section + "/" + p.root + "/" + GroupName + "/" + p.name + " at " + p.position.ToString("F3")));
        if (SceneManager.GetActiveScene().isDirty || layout.sceneSha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Preflight changed scene");
        File.WriteAllText("Docs/BossHall/EnergyPreflight_053.txt", report + "\n"); Debug.Log(report);
    }
    static Dictionary<string, string> AssetHashes() => Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, BossHallSurvey.Hash);
    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch access");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); var layout = BossHallFramework.MeasureLayout();
        BossHallLowerArena.Validate(layout); // Populate the existing read-only survey cache while the baseline is clean.
        var before = CaptureProtected(); var shared = BossHallSnapshot.CaptureAssets();
        var parentAssets = Directory.GetFiles(BossHallFramework.AssetRoot, "*", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').StartsWith(Folder + "/", StringComparison.Ordinal)).ToDictionary(p => p, BossHallSurvey.Hash);
        BuildAssets(); var hashes = AssetHashes(); BuildAssets(); BossHallSnapshot.Compare(hashes, AssetHashes(), "Regenerated energy assets/meta");
        Rebuild(layout); Validate(layout); NegativeGuards(layout);
        // Each parent builder must recreate installed children without changes to any other section.
        foreach (var s in Sections.Where(s => s != Section.Overhead)) // The service builder verifies power and overhead together.
        {
            var old = Renderers().Select(r => r.transform.position).ToArray();
            Undo.IncrementCurrentGroup(); int parentGroup = Undo.GetCurrentGroup();
            if (s == Section.AssemblyBackdrop) BossHallBackdrop.RebuildForEnergyVerification(layout);
            else if (s == Section.MechanicalArms) BossHallMechanicalArms.RebuildForEnergyVerification(layout);
            else BossHallServices.RebuildForEnergyVerification(layout);
            Validate(layout);
            var current = Renderers().Select(r => r.transform.position).ToArray();
            if (!old.SequenceEqual(current)) throw new InvalidOperationException("Parent rebuild lost energy");
            Undo.RevertAllDownToGroup(parentGroup); Validate(layout);
        }
        BossHallSnapshot.Compare(before, CaptureProtected(), "Protected scene state after rebuild/Undo");
        BossHallSnapshot.Compare(shared, BossHallSnapshot.CaptureAssets(), "Protected shared entries");
        BossHallSnapshot.Compare(parentAssets, Directory.GetFiles(BossHallFramework.AssetRoot, "*", SearchOption.AllDirectories)
            .Where(p => !p.Replace('\\', '/').StartsWith(Folder + "/", StringComparison.Ordinal)).ToDictionary(p => p, BossHallSurvey.Hash), "Parent machinery assets/meta");
        layout = BossHallFramework.SaveAndReload(layout); string report = Validate(layout);
        File.WriteAllText("Docs/BossHall/Energy_053.txt", report + "\nSaved scene: " + layout.sceneSha + "\nProtected scene states: " + before.Count +
            "; shared entries: " + shared.Count + ". Byte-identical regenerated assets/meta; parent rebuild/Undo; negative guards; save/reload pass.\n" + BossHallFramework.BudgetReport() + "\n");
        Debug.Log(report);
    }
    static void NegativeGuards(BossHallFramework.Layout layout)
    {
        void Reject(Action action, string label)
        { try { action(); } catch (InvalidOperationException) { Debug.Log("PASS: energy guard " + label); return; } throw new InvalidOperationException("Accepted invalid energy: " + label); }
        Reject(() => Rebuild(layout, "corebuster_env_049"), "wrong energy owner");
        var t = Renderers()[0].transform; var position = t.position;
        try { t.position += Vector3.right; Reject(() => BossHallFramework.Validate(layout), "moved energy"); } finally { t.position = position; }
        var collider = t.gameObject.AddComponent<BoxCollider>();
        try { Reject(() => BossHallFramework.Validate(layout), "energy collision"); } finally { Object.DestroyImmediate(collider); }
        var health = t.gameObject.AddComponent<Unity.FPS.Game.Health>();
        try { Reject(() => BossHallFramework.Validate(layout), "gameplay health"); } finally { Object.DestroyImmediate(health); }
        var r = t.GetComponent<MeshRenderer>(); var material = r.sharedMaterial;
        try { r.sharedMaterial = BossHallRendering.GetFinish("Gunmetal"); Reject(() => BossHallFramework.Validate(layout), "wrong material"); } finally { r.sharedMaterial = material; }
        var group = t.parent; var parent = group.parent;
        try { group.SetParent(BossHallFramework.FindSection(Section.CoolingServices), true); Reject(() => BossHallFramework.Validate(layout), "unowned energy group"); }
        finally { group.SetParent(parent, true); }
        Physics.SyncTransforms(); Validate(layout);
    }
    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); var layout = BossHallFramework.MeasureLayout();
        string report = Validate(layout); if (SceneManager.GetActiveScene().isDirty || layout.sceneSha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Validation changed scene");
        Debug.Log(report);
    }
}
