using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = BossHallFramework.Section;

/// <summary>
/// corebuster_env_052: limited lower-arena edge cover. Two low reinforced blast-service housings stand against the south and
/// north walls in the surveyed heavy-perimeter strips under the galleries, centred in the measured free stretch between the
/// env_050 cooling and power hulls. They stay below the standing eye, so no standing firing line (turret, enemy or player) can
/// cross them; the central floor stays open. Each has one consolidated box collider and matching carving footprint, keeping
/// the env_049/050 0.35 m wall stand-off. Placement derives from the accepted contract, measured wall collision and the saved
/// env_050 hulls; nothing is copied from the concept image. The owner waived the separate gameplay prerequisite for env_052
/// only (2026-10-04); containment and win ordering remain open.
/// </summary>
public static class BossHallLowerArena
{
    public const string Owner = "corebuster_env_052", Output = "ReviewCaptures/BossHall/052";
    public const string Folder = BossHallFramework.AssetRoot + "/Lower_Arena_Dressing";
    public const string BodyName = "Housing", HullName = BossHallServices.HullName;
    const Section Owned = Section.LowerArenaDressing;
    // Low cover: the armour cap stays well below the 1.44 m standing eye (Centre Room barriers are 1.05 m).
    public const float Length = 6.4f, Depth = 1.72f, Height = 1.1f, WallStandoff = .35f;
    // Placement acceptance: open floor in front of each housing and beside each end, beyond any narrow-lane threshold.
    const float MinFrontClearance = 3f, MinEndClearance = 6f, EyeMargin = .2f, Shrink = .03f, Bearing = .035f;
    static readonly string[] Sides = { "South", "North" };
    public static readonly string[] Finishes = { "Gunmetal", "HazardYellow", "RedOxide" };
    public static string MeshPath => Folder + "/Meshes/SM_LowerArena_BlastHousing.asset";

    public sealed class Placement
    {
        public string name, zone;
        public int side;
        public Vector3 position;
        public Quaternion rotation;
        public Bounds hull;
        public float wallFace, frontClearance, endClearance, westGap, eastGap;
    }

    public static MeshRenderer[] Renderers() => BossHallFramework.FindSection(Owned).GetComponentsInChildren<MeshRenderer>(true);
    static float Inward(int side) => side == 0 ? 1 : -1;

    static Bounds ServiceHull(Section section, int side)
    {
        var root = BossHallFramework.FindSection(section).Find(BossHallServices.Roots(section)[side]);
        var box = root ? root.Find(HullName)?.GetComponent<BoxCollider>() : null;
        return box ? box.bounds : throw new InvalidOperationException("Missing saved env_050 hull " + BossHallServices.Roots(section)[side]);
    }

    /// <summary>Measured placement. Rejects any candidate that leaves its surveyed strip, enters a keep-out, overlaps collision,
    /// is unsupported, or leaves less than the required open floor in front of it or beside either end.</summary>
    public static List<Placement> Plan(BossHallFramework.Layout layout)
    {
        Physics.SyncTransforms();
        var r = layout.Report; var room = layout.interior; var dressing = BossHallFramework.FindRoot(); var own = BossHallFramework.FindSection(Owned);
        var result = new List<Placement>();
        for (int s = 0; s < 2; s++)
        {
            var zone = r.zones.Single(z => z.name == Sides[s] + " heavy-perimeter 1"); var envelope = MinMax(zone.min, zone.max);
            var cooling = ServiceHull(Section.CoolingServices, s); var power = ServiceHull(Section.PowerServices, s);
            float west = cooling.max.x, east = power.min.x, cx = (west + east) * .5f, inward = Inward(s), plane = s == 0 ? room.min.z : room.max.z;
            if (east - west < Length + 2 * MinEndClearance) throw new InvalidOperationException("Free stretch between env_050 hulls too short on the " + Sides[s] + " side.");
            // The lower kit wall collider starts at floor + 0.99 m; measure its face at floor + 1.5 m over the housing length.
            var faces = new List<float>();
            for (float x = cx - Length * .5f + .2f; x <= cx + Length * .5f - .19f; x += .5f)
            {
                var hit = Physics.RaycastAll(new Vector3(x, r.floorTopY + 1.5f, plane + inward * 3), new Vector3(0, 0, -inward), 4, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => !h.collider.transform.IsChildOf(dressing)).OrderBy(h => h.distance).FirstOrDefault();
                if (!hit.collider || Mathf.Abs(hit.point.z - plane) > .1f) throw new InvalidOperationException($"No measured {Sides[s]} wall face at X {x:F2}");
                faces.Add(hit.point.z);
            }
            float face = s == 0 ? faces.Max() : faces.Min(), back = face + inward * WallStandoff;
            var hull = MinMax(new Vector3(cx - Length * .5f, r.floorTopY, back), new Vector3(cx + Length * .5f, r.floorTopY + Height, back + inward * Depth));
            string name = "Blast_Housing_" + Sides[s];
            if (!Inside(envelope, hull)) throw new InvalidOperationException(name + " leaves " + zone.name);
            string violation = layout.KeepOutViolation(hull); if (violation != null) throw new InvalidOperationException(name + " enters " + violation);
            var test = hull; test.Expand(-2 * Shrink); test.min += Vector3.up * Bearing;
            var overlaps = Physics.OverlapBox(test.center, test.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(own)).ToArray();
            if (overlaps.Length > 0) throw new InvalidOperationException(name + " overlaps " + string.Join(", ", overlaps.Select(c => BossHallFramework.PathOf(c.transform))));
            for (float x = hull.min.x + .2f; x < hull.max.x; x += (Length - .4f) / 4)
                foreach (float z in new[] { hull.min.z + .2f, hull.center.z, hull.max.z - .2f })
                    if (!Physics.RaycastAll(new Vector3(x, r.floorTopY + .05f, z), Vector3.down, .25f, ~0, QueryTriggerInteraction.Ignore).Any(h => !h.collider.transform.IsChildOf(dressing)))
                        throw new InvalidOperationException($"{name} unsupported by the original floor at ({x:F2}, {z:F2})");
            float Clear(Vector3 origin, Vector3 direction, float reach) => Physics.RaycastAll(origin, direction, reach, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => !h.collider.transform.IsChildOf(own)).Select(h => h.distance).DefaultIfEmpty(reach).Min();
            float front = float.MaxValue, ends = float.MaxValue, frontZ = s == 0 ? hull.max.z : hull.min.z;
            foreach (float y in new[] { .3f, 1f, 1.7f })
            {
                for (float x = hull.min.x; x <= hull.max.x + .01f; x += .4f)
                    front = Mathf.Min(front, Clear(new Vector3(x, r.floorTopY + y, frontZ + inward * .01f), new Vector3(0, 0, inward), 10));
                for (float z = hull.min.z; z <= hull.max.z + .01f; z += .43f)
                {
                    ends = Mathf.Min(ends, Clear(new Vector3(hull.min.x - .01f, r.floorTopY + y, z), Vector3.left, 12));
                    ends = Mathf.Min(ends, Clear(new Vector3(hull.max.x + .01f, r.floorTopY + y, z), Vector3.right, 12));
                }
            }
            if (front < MinFrontClearance || ends < MinEndClearance)
                throw new InvalidOperationException($"{name} leaves {front:F2} m in front / {ends:F2} m beside its ends; defer rather than narrowing a route.");
            result.Add(new Placement { name = name, zone = zone.name, side = s, position = new Vector3(cx, r.floorTopY, back),
                rotation = s == 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0), hull = hull, wallFace = face,
                frontClearance = front, endClearance = ends, westGap = hull.min.x - west, eastGap = east - hull.max.x });
        }
        return result;
    }

    /// <summary>One first-principles housing recipe in local space: X along the wall, Y up, Z from the stand-off line into the hall.
    /// Submeshes: 0 Gunmetal, 1 HazardYellow, 2 RedOxide. Every primitive stays inside the consolidated hull box.</summary>
    internal static Mesh Compose()
    {
        var b = new ProceduralMeshBuilder();
        const int Steel = 0, Hazard = 1, Oxide = 2;
        void Block(int finish, float x0, float y0, float z0, float x1, float y1, float z1)
        { b.UseSubmesh(finish); b.AddBlock(new Vector3((x0 + x1) * .5f, (y0 + y1) * .5f, (z0 + z1) * .5f), new Vector3(x1 - x0, y1 - y0, z1 - z0)); }
        void Cylinder(int finish, Vector3 a, Vector3 c, float radius, int segments) { b.UseSubmesh(finish); b.AddCylinder(a, c, radius, radius, segments, true); }
        float hx = Length * .5f;
        // Bolted skid with a hazard kick strip along the hall-facing edge.
        Block(Steel, -hx, 0, 0, hx, .12f, Depth - .04f);
        Block(Hazard, -hx, .015f, Depth - .04f, hx, .105f, Depth);
        // Two reinforced housings either side of a recessed coolant block.
        foreach (int side in new[] { -1, 1 })
        {
            float x0 = side < 0 ? -hx : .6f, x1 = side < 0 ? -.6f : hx, xc = (x0 + x1) * .5f;
            Block(Steel, x0 + .02f, .12f, .06f, x1 - .02f, .98f, 1.58f);
            Block(Steel, x0, .98f, .02f, x1, 1.06f, 1.64f);                       // armour cap with a front blast lip
            foreach (float px in new[] { xc - .64f, xc + .64f })
            {
                Block(Steel, px - .56f, .22f, 1.58f, px + .56f, .9f, 1.61f);        // riveted blast plate
                foreach (float bx in new[] { px - .46f, px + .46f })
                    foreach (float by in new[] { .3f, .82f })
                        Cylinder(Steel, new Vector3(bx, by, 1.61f), new Vector3(bx, by, 1.64f), .03f, 8);
            }
            Block(Hazard, x0, .12f, 1.5f, x0 + .1f, .98f, 1.62f);                   // corner guards on the hall-facing edges
            Block(Hazard, x1 - .1f, .12f, 1.5f, x1, .98f, 1.62f);
            foreach (float lx in new[] { x0 + .35f, x1 - .35f }) Block(Steel, lx - .09f, 1.06f, .7f, lx + .09f, Height, .94f); // lifting lugs
            for (float vx = xc - .9f; vx < xc + .91f; vx += .3f) Block(Steel, vx - .1f, 1.06f, .18f, vx + .1f, 1.085f, .5f);   // cap vent ribs
        }
        // Coolant block: louvred front, cross header with a handwheel valve, flanged riser stub.
        Block(Oxide, -.58f, .12f, .3f, .58f, .84f, 1.34f);
        Block(Oxide, -.6f, .84f, .26f, .6f, .9f, 1.38f);
        for (float y = .18f; y < .45f; y += .07f) Block(Steel, -.5f, y, 1.34f, .5f, y + .03f, 1.37f);
        Cylinder(Oxide, new Vector3(-.6f, .62f, 1.42f), new Vector3(.6f, .62f, 1.42f), .07f, 14);
        foreach (float fx in new[] { -.56f, .56f }) Cylinder(Steel, new Vector3(fx - .03f, .62f, 1.42f), new Vector3(fx + .03f, .62f, 1.42f), .11f, 14);
        Block(Oxide, -.12f, .5f, 1.32f, .12f, .74f, 1.52f);
        Cylinder(Steel, new Vector3(0, .62f, 1.52f), new Vector3(0, .62f, 1.57f), .025f, 8);
        b.UseSubmesh(Hazard); b.AddTorus(new Vector3(0, .62f, 1.58f), Vector3.forward, .14f, .022f, 20, 8);
        Cylinder(Oxide, new Vector3(0, .9f, .8f), new Vector3(0, 1.04f, .8f), .09f, 14);
        Cylinder(Steel, new Vector3(0, 1.04f, .8f), new Vector3(0, 1.08f, .8f), .14f, 14);
        return b.ToMesh("SM_LowerArena_BlastHousing");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    /// <summary>Writes the owned mesh. Runs before the scene transaction; scene Undo cannot restore asset writes.</summary>
    static void BuildAssets()
    {
        EnsureFolder(Folder + "/Meshes");
        var mesh = Compose(); var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(mesh, MeshPath); existing = mesh; }
        BossHallFramework.RequireOwnedAsset(existing, Owned);
        AssetDatabase.SaveAssets();
    }
    static Dictionary<string, string> AssetHashes() => Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).Append(Folder + ".meta")
        .Where(File.Exists).ToDictionary(p => p.Replace('\\', '/'), BossHallSurvey.Hash, StringComparer.Ordinal);

    static void Rebuild(BossHallFramework.Layout layout, List<Placement> plan) => BossHallFramework.RebuildSection(layout, Owned, Owner, section =>
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) ?? throw new InvalidOperationException("Prepare the housing mesh first.");
        foreach (var p in plan)
        {
            var root = new GameObject(p.name); Undo.RegisterCreatedObjectUndo(root, "Build Boss Hall lower arena housing");
            root.transform.SetParent(section, false); root.transform.SetPositionAndRotation(p.position, p.rotation);
            var body = new GameObject(BodyName); body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = body.AddComponent<MeshRenderer>(); renderer.sharedMaterials = Finishes.Select(BossHallRendering.GetFinish).ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            var hull = new GameObject(HullName); hull.transform.SetParent(root.transform, false);
            hull.transform.localPosition = new Vector3(0, Height * .5f, Depth * .5f);
            var box = hull.AddComponent<BoxCollider>(); box.size = new Vector3(Length, Height, Depth);
            var carve = hull.AddComponent<NavMeshObstacle>(); carve.shape = NavMeshObstacleShape.Box; carve.size = box.size; carve.center = Vector3.zero;
            carve.carving = true; carve.carveOnlyStationary = true;
        }
    });

    public static string Validate(BossHallFramework.Layout layout)
    {
        BossHallServices.Validate(layout); BossHallCatwalk.Validate(layout);
        Physics.SyncTransforms();
        var plan = Plan(layout); var section = BossHallFramework.FindSection(Owned); var r = layout.Report;
        float eye = BossHallRenderingReview.EyeHeight();
        if (!section.Cast<Transform>().Select(t => t.name).SequenceEqual(plan.Select(p => p.name)))
            throw new InvalidOperationException("Lower_Arena_Dressing must contain exactly " + string.Join(", ", plan.Select(p => p.name)));
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (!mesh || EditorUtility.IsDirty(mesh)) throw new InvalidOperationException("Missing or unsaved housing mesh " + MeshPath);
        var expected = Compose();
        try
        {
            if (mesh.subMeshCount != 3 || !mesh.vertices.SequenceEqual(expected.vertices) || !Enumerable.Range(0, 3).All(i => mesh.GetTriangles(i).SequenceEqual(expected.GetTriangles(i))) ||
                !mesh.uv.SequenceEqual(expected.uv) || !mesh.normals.SequenceEqual(expected.normals)) throw new InvalidOperationException("Altered housing mesh " + MeshPath);
        }
        finally { Object.DestroyImmediate(expected); }
        var saved = new List<Bounds>();
        foreach (var p in plan)
        {
            var root = section.Find(p.name); var body = root.Find(BodyName); var hull = root.Find(HullName);
            var renderer = body ? body.GetComponent<MeshRenderer>() : null; var filter = body ? body.GetComponent<MeshFilter>() : null;
            var box = hull ? hull.GetComponent<BoxCollider>() : null; var carve = hull ? hull.GetComponent<NavMeshObstacle>() : null;
            if (!renderer || !filter || !box || !carve || root.childCount != 2 || root.GetComponents<Component>().Length != 1 || body.GetComponents<Component>().Length != 3 ||
                hull.GetComponents<Component>().Length != 3 || body.childCount != 0 || hull.childCount != 0)
                throw new InvalidOperationException("Unexpected housing structure " + p.name);
            var rb = renderer.bounds; var hb = box.bounds;
            // Rules independent of the exact recipe (the negative guards exercise these).
            if (!Inside(MinMax(r.zones.Single(z => z.name == p.zone).min, r.zones.Single(z => z.name == p.zone).max), hb) ||
                !Inside(MinMax(r.zones.Single(z => z.name == p.zone).min, r.zones.Single(z => z.name == p.zone).max), rb))
                throw new InvalidOperationException(p.name + " leaves the surveyed " + p.zone);
            if (rb.max.y > r.floorTopY + eye - EyeMargin || hb.max.y > r.floorTopY + eye - EyeMargin)
                throw new InvalidOperationException(p.name + " rises toward standing eye height; limited cover must not cross standing firing lines.");
            float back = p.side == 0 ? Mathf.Min(rb.min.z, hb.min.z) - p.wallFace : p.wallFace - Mathf.Max(rb.max.z, hb.max.z);
            if (back < WallStandoff - .01f) throw new InvalidOperationException(p.name + " contacts the wall at floor level (" + back.ToString("F2", CultureInfo.InvariantCulture) + " m)");
            if (!Inside(Expanded(hb, .005f), rb) || Vector3.Distance(hb.size, rb.size) > .05f) throw new InvalidOperationException(p.name + " collision hull does not match its housing");
            if (!box.enabled || box.isTrigger || box.center != Vector3.zero || carve.shape != NavMeshObstacleShape.Box || Vector3.Distance(carve.size, box.size) > .001f ||
                carve.center != Vector3.zero || !carve.enabled || !carve.carving || !carve.carveOnlyStationary)
                throw new InvalidOperationException("Hull/carve mismatch at " + p.name);
            if (renderer.sharedMaterials.Length != Finishes.Length || Enumerable.Range(0, Finishes.Length).Any(i => renderer.sharedMaterials[i] != BossHallRendering.GetFinish(Finishes[i])))
                throw new InvalidOperationException("Finish outside the planned " + string.Join("/", Finishes) + ": " + p.name);
            if (renderer.shadowCastingMode != ShadowCastingMode.On || !renderer.receiveShadows || !renderer.enabled || renderer.renderingLayerMask != 1)
                throw new InvalidOperationException("Unexpected renderer state " + p.name);
            // Exact saved recipe.
            if (Vector3.Distance(root.position, p.position) > .001f || Quaternion.Angle(root.rotation, p.rotation) > .01f || root.localScale != Vector3.one ||
                body.localPosition != Vector3.zero || body.localRotation != Quaternion.identity || body.localScale != Vector3.one ||
                Vector3.Distance(hull.localPosition, new Vector3(0, Height * .5f, Depth * .5f)) > .0001f || hull.localRotation != Quaternion.identity || hull.localScale != Vector3.one ||
                filter.sharedMesh != mesh || Vector3.Distance(hb.min, p.hull.min) > .01f || Vector3.Distance(hb.max, p.hull.max) > .01f ||
                new[] { root, body, hull }.Any(t => !t.gameObject.activeSelf || t.gameObject.layer != 0 || t.gameObject.tag != "Untagged"))
                throw new InvalidOperationException("Altered housing " + p.name);
            saved.Add(rb);
        }
        if (section.GetComponentsInChildren<Collider>(true).Length != plan.Count || section.GetComponentsInChildren<NavMeshObstacle>(true).Length != plan.Count ||
            section.GetComponentsInChildren<Light>(true).Length + section.GetComponentsInChildren<AudioSource>(true).Length + section.GetComponentsInChildren<ParticleSystem>(true).Length != 0 ||
            section.GetComponentsInChildren<Transform>(true).Any(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)))
            throw new InvalidOperationException("Lower_Arena_Dressing holds only the planned housings, hulls and carves.");
        Intersections(plan);
        return Evidence(layout, plan, saved.ToArray()) + "\nPASS: exact saved recipe, transforms, finishes, hull/carve consolidation, surveyed strip, wall stand-off, below-eye cap and intersections.";
    }

    // Each hull (shrunk 3 cm; floor bearing 3.5 cm) against original and other-dressing renderer triangles.
    static void Intersections(List<Placement> plan)
    {
        var dressing = BossHallFramework.FindRoot(); var own = BossHallFramework.FindSection(Owned); var issues = new List<string>();
        var others = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true))
            .Where(x => x.enabled && x.gameObject.activeInHierarchy && !x.transform.IsChildOf(own)).ToArray();
        int tested = 0;
        foreach (var p in plan)
        {
            var inner = p.hull; inner.Expand(-2 * Shrink); inner.min += Vector3.up * Bearing;
            foreach (var o in others.Where(o => inner.Intersects(o.bounds)))
            {
                var mesh = o.GetComponent<MeshFilter>()?.sharedMesh; if (!mesh) continue;
                var m = o.localToWorldMatrix; var v = mesh.vertices; tested++;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var t = mesh.GetTriangles(sub); bool hit = false;
                    for (int i = 0; i < t.Length && !hit; i += 3)
                        hit = BossHallServices.TriangleBox(inner.center, inner.extents, m.MultiplyPoint3x4(v[t[i]]), m.MultiplyPoint3x4(v[t[i + 1]]), m.MultiplyPoint3x4(v[t[i + 2]]));
                    if (hit) { issues.Add(p.name + " x " + (o.transform.IsChildOf(dressing) ? "dressing " : "renderer ") + BossHallFramework.PathOf(o.transform)); break; }
                }
            }
        }
        if (issues.Count > 0) throw new InvalidOperationException(issues.Count + " housing intersections: " + string.Join(" | ", issues.Take(30)));
    }

    /// <summary>Footprints, clearances, firing lines, open floor and budgets for planned hulls (preflight) or saved renderer bounds.</summary>
    public static string Evidence(BossHallFramework.Layout layout, List<Placement> plan, Bounds[] boxes)
    {
        var r = layout.Report; var t = r.turret; var room = layout.interior; float eye = BossHallRenderingReview.EyeHeight();
        var player = Object.FindAnyObjectByType<PlayerCharacterController>(); float radius = player.GetComponent<CharacterController>().radius;
        float crouchEye = player.CapsuleHeightCrouching * player.CameraHeightRatio;
        string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        var lines = new List<string> { $"corebuster_env_052 lower-arena edge cover: {plan.Count} reinforced blast-service housings, {F(Length)} x {F(Depth)} m, {F(Height)} m high " +
            $"(standing eye {F(eye)} m, crouched eye {F(crouchEye)} m); one consolidated box collider and carving footprint each; 0 prefabs/lights/emitters/audio." };
        foreach (var p in plan)
            lines.Add($"{p.name}: {p.zone}; footprint {BossHallServices.B(p.hull)}; wall face Z {F(p.wallFace)}, stand-off {F(WallStandoff)} m; " +
                $"open floor {F(p.frontClearance)} m in front (>= {F(MinFrontClearance)}), {F(p.endClearance)} m beside the ends (>= {F(MinEndClearance)}); " +
                $"{F(p.westGap)} m to the env_050 cooling hull and {F(p.eastGap)} m to the power hull.");
        bool Blocked(Vector3 a, Vector3 b)
        { float d = Vector3.Distance(a, b); var ray = new Ray(a, (b - a) / d); return boxes.Any(x => x.IntersectRay(ray, out float h) && h >= 0 && h < d); }
        var head = (t.head.min + t.head.max) * .5f;
        if (Blocked(r.entryEye, head)) throw new InvalidOperationException("Housings obscure the entry-to-turret head sight line.");
        // Eyes between a housing and its wall, or within a player radius of it, are occupied rather than playable.
        var footprints = plan.Select(p => { var b = p.hull; b.Encapsulate(new Vector3(b.center.x, b.center.y, p.wallFace)); b.Expand(new Vector3(radius * 2, 0, radius * 2)); return b; }).ToArray();
        bool Occupied(float x, float z) => footprints.Any(b => b.min.x <= x && x <= b.max.x && b.min.z <= z && z <= b.max.z);
        int floorEyes = 0, occupied = 0, blocked = 0, raised = 0;
        for (float x = room.min.x + .5f; x < room.max.x; x += 1)
            for (float z = room.min.z + .5f; z < room.max.z; z += 1)
            {
                var p = new Vector3(x, r.floorTopY + eye, z); if (Vector3.Distance(p, t.firingOrigin) > t.detectionRange) continue;
                floorEyes++; if (Occupied(x, z)) { occupied++; continue; }
                if (Blocked(t.firingOrigin, p)) blocked++;
            }
        foreach (var deck in r.deckFootprints.Where(d => d.name.Contains(" gallery:") || d.name.StartsWith("Upper deck network:", StringComparison.Ordinal)))
            for (float x = deck.min.x + .5f; x < deck.max.x; x += 1)
                for (float z = deck.min.z + .5f; z < deck.max.z; z += 1)
                {
                    var p = new Vector3(x, deck.max.y + eye, z); if (Vector3.Distance(p, t.firingOrigin) > t.detectionRange) continue;
                    raised++; if (Blocked(t.firingOrigin, p)) blocked++;
                }
        if (blocked != 0) throw new InvalidOperationException("Housings cross " + blocked + " turret firing lines.");
        lines.Add($"Turret: entry-to-head silhouette clear; {floorEyes} floor standing eyes in detection ({occupied} inside occupied footprints, 0 blocked) and {raised} tier-1/tier-2 eyes, 0 blocked.");
        // Ranged humanoids from their start eyes: standing targets never blocked; crouched targets in a housing's lee are its cover value.
        int standing = 0, standingBlocked = 0, crouched = 0, shielded = 0;
        foreach (var a in r.actors.Where(a => a.kind != "turret" && a.attackRange > 6))
        {
            var from = a.position + Vector3.up * a.agentHeight * .85f;
            for (float x = room.min.x + .5f; x < room.max.x; x += 1)
                for (float z = room.min.z + .5f; z < room.max.z; z += 1)
                {
                    if (Occupied(x, z)) continue;
                    var p = new Vector3(x, r.floorTopY + eye, z); if (Vector3.Distance(p, from) > a.attackRange) continue;
                    standing++; if (Blocked(from, p)) standingBlocked++;
                    crouched++; if (Blocked(from, new Vector3(x, r.floorTopY + crouchEye, z))) shielded++;
                }
        }
        if (standingBlocked != 0) throw new InvalidOperationException("Housings block " + standingBlocked + " ranged-enemy lines to standing eyes.");
        lines.Add($"Ranged enemies (six RivetCannon/Rifleman starts, within attack range): 0 of {standing} lines to standing eyes blocked; {shielded} of {crouched} crouched positions shielded. " +
            "Wall-hugging edge cover deliberately gives no shelter from the arena centre or the turret.");
        // Flank cover: a crouched player beside one end, fired on along the under-gallery lane from beyond the other end.
        int flank = 0, flankShielded = 0, flankStanding = 0;
        foreach (var p in plan)
        {
            float inward = Inward(p.side);
            foreach (int end in new[] { -1, 1 })
            {
                float near = end < 0 ? p.hull.min.x : p.hull.max.x, far = end < 0 ? p.hull.max.x : p.hull.min.x;
                for (float tx = radius + .15f; tx <= 1.2f; tx += .35f)
                    for (float td = .7f; td <= 2f; td += .3f)
                        for (float sx = 2; sx <= 12; sx += 2)
                            for (float sd = 1; sd <= 3; sd += 1)
                            {
                                var from = new Vector3(far - end * sx, r.floorTopY + 1.8f, p.wallFace + inward * sd);
                                var to = new Vector3(near + end * tx, r.floorTopY, p.wallFace + inward * td);
                                flank++; if (Blocked(from, to + Vector3.up * crouchEye)) flankShielded++; if (Blocked(from, to + Vector3.up * eye)) flankStanding++;
                            }
            }
        }
        if (flankStanding != 0) throw new InvalidOperationException("Housings block " + flankStanding + " flank lines to standing eyes.");
        lines.Add($"Flank cover along the under-gallery lanes (shooter eye 1.8 m, 2-12 m beyond the far end, wall distance 1-3 m; target 0.5-1.2 m beside the near end, wall distance 0.7-2.0 m): " +
            $"{flankShielded} of {flank} crouched lines shielded ({F(100f * flankShielded / flank)}%), 0 standing lines blocked.");
        // Open floor: every dressing floor hull's footprint, including these housings, on a 1 m grid.
        var hulls = BossHallFramework.FindRoot().GetComponentsInChildren<Collider>(true).Where(c => !c.transform.IsChildOf(BossHallFramework.FindSection(Owned)))
            .Select(c => c.bounds).Where(b => b.min.y < r.floorTopY + 2).Concat(plan.Select(p => p.hull)).ToArray();
        var southEdge = r.zones.Single(z => z.name == "South gallery walking volume").max.z; var northEdge = r.zones.Single(z => z.name == "North gallery walking volume").min.z;
        var westEdge = r.zones.Single(z => z.name == "West deep-pocket 2").max.x; var eastEdge = r.zones.Single(z => z.name == "East deep-pocket 1").min.x;
        int cells = 0, taken = 0, central = 0, centralTaken = 0, mine = 0;
        for (float x = room.min.x + .5f; x < room.max.x; x += 1)
            for (float z = room.min.z + .5f; z < room.max.z; z += 1)
            {
                bool inCentre = x > westEdge && x < eastEdge && z > southEdge && z < northEdge; cells++; if (inCentre) central++;
                bool any = hulls.Any(b => b.min.x <= x && x <= b.max.x && b.min.z <= z && z <= b.max.z); if (!any) continue;
                taken++; if (inCentre) centralTaken++;
                if (plan.Any(p => p.hull.min.x <= x && x <= p.hull.max.x && p.hull.min.z <= z && z <= p.hull.max.z)) mine++;
            }
        if (centralTaken != 0) throw new InvalidOperationException("Dressing collision enters the central arena floor.");
        lines.Add($"Open floor (1 m grid): central arena X [{F(westEdge)}, {F(eastEdge)}] Z [{F(southEdge)}, {F(northEdge)}] {central - centralTaken}/{central} cells open (100%); " +
            $"whole hall {cells - taken}/{cells} open ({F(100f * (cells - taken) / cells)}%); these housings take {mine} perimeter cells.");
        return string.Join("\n", lines);
    }

    static void NegativeGuards(BossHallFramework.Layout layout)
    {
        void Reject(Action action, string label)
        {
            try { action(); } catch (InvalidOperationException) { Debug.Log("PASS: lower arena guard " + label); return; }
            throw new InvalidOperationException("Accepted invalid lower arena state: " + label);
        }
        var section = BossHallFramework.FindSection(Owned); var root = section.GetChild(0); var position = root.position;
        try { root.position = position + Vector3.forward * 2f; Physics.SyncTransforms(); Reject(() => Validate(layout), "housing pushed out of its surveyed strip toward the arena"); }
        finally { root.position = position; Physics.SyncTransforms(); }
        try { root.position = position + Vector3.back * .3f; Physics.SyncTransforms(); Reject(() => Validate(layout), "housing against the wall at floor level"); }
        finally { root.position = position; Physics.SyncTransforms(); }
        try { root.localScale = new Vector3(1, 1.6f, 1); Physics.SyncTransforms(); Reject(() => Validate(layout), "cover raised to standing eye height"); }
        finally { root.localScale = Vector3.one; Physics.SyncTransforms(); }
        var renderer = root.GetComponentInChildren<MeshRenderer>(); var materials = renderer.sharedMaterials;
        try { renderer.sharedMaterials = materials.Select((m, i) => i == 0 ? BossHallRendering.GetFinish("WarningRed") : m).ToArray(); Reject(() => Validate(layout), "unplanned finish"); }
        finally { renderer.sharedMaterials = materials; }
        var carve = root.Find(HullName).GetComponent<NavMeshObstacle>();
        try { carve.carving = false; Reject(() => Validate(layout), "hull without carving"); }
        finally { carve.carving = true; }
        var extra = root.Find(BodyName).gameObject.AddComponent<BoxCollider>();
        try { Physics.SyncTransforms(); Reject(() => Validate(layout), "second collider"); }
        finally { Object.DestroyImmediate(extra); Physics.SyncTransforms(); }
        var light = new GameObject("__LowerArena_Light_Probe").AddComponent<Light>();
        try { light.transform.SetParent(root, false); Reject(() => BossHallFramework.Validate(layout), "light outside Lighting"); }
        finally { Object.DestroyImmediate(light.gameObject); }
        Reject(() => BossHallFramework.RebuildSection(layout, Owned, "corebuster_env_050", _ => { }), "wrong owner");
        Physics.SyncTransforms(); Validate(layout);
    }

    /// <summary>Protected state: everything outside the dressing root plus every dressing section this task does not own.</summary>
    static Dictionary<string, string> CaptureProtected()
    {
        var scene = SceneManager.GetActiveScene(); var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in BossHallSnapshot.Capture(scene, BossHallFramework.FindRoot())) result["outside|" + p.Key] = p.Value;
        foreach (var spec in BossHallFramework.Sections.Where(s => s.section != Owned))
            foreach (var p in BossHallSnapshot.Capture(scene, null, BossHallFramework.FindSection(spec.section))) result[spec.name + "|" + p.Key] = p.Value;
        return result;
    }

    [MenuItem("Tools/CoreBuster/Build Boss Hall Lower Arena Cover")]
    public static void BuildMenu()
    {
        var layout = BossHallFramework.MeasureLayout(); var plan = Plan(layout); Evidence(layout, plan, plan.Select(p => p.hull).ToArray());
        BuildAssets(); Rebuild(layout, plan); Debug.Log(Validate(layout));
        Debug.Log("Boss Hall lower arena cover built with Undo. Save through the guarded batch workflow; the gameplay gate was waived for env_052 only.");
    }
    [MenuItem("Tools/CoreBuster/Validate Boss Hall Lower Arena Cover")]
    public static void ValidateMenu() => Debug.Log(Validate(BossHallFramework.MeasureLayout()));

    /// <summary>Read-only: measured plan and evidence on the saved scene before any asset or scene content exists.</summary>
    public static void PreflightBatch()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        BossHallServices.Validate(layout); BossHallCatwalk.Validate(layout);
        if (BossHallFramework.FindSection(Owned).childCount != 0) throw new InvalidOperationException("Preflight expects the empty env_048 Lower_Arena_Dressing section.");
        var plan = Plan(layout);
        string report = "READ-ONLY ENV052 PREFLIGHT; no asset or scene content created.\nScene: " + sha + "\n" + Evidence(layout, plan, plan.Select(p => p.hull).ToArray());
        if (SceneManager.GetActiveScene().isDirty || BossHallSurvey.Hash(BossHallSurvey.ScenePath) != sha) throw new InvalidOperationException("Preflight changed MainScene.");
        Directory.CreateDirectory("Docs/BossHall"); File.WriteAllText("Docs/BossHall/LowerArenaPreflight_052.txt", report + "\n"); Debug.Log(report);
    }

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch Editor access.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); BossHallServices.Validate(layout); BossHallCatwalk.Validate(layout);
        var before = CaptureProtected(); var assets = BossHallSnapshot.CaptureAssets();
        BossHallLowerArenaReview.NeighbourProof(layout, false);
        var plan = Plan(layout); Evidence(layout, plan, plan.Select(p => p.hull).ToArray());
        BuildAssets(); Rebuild(layout, plan); Validate(layout); NegativeGuards(layout);
        var section = BossHallFramework.FindSection(Owned); int count = section.GetComponentsInChildren<Transform>(true).Length;
        BossHallFramework.RebuildSection(layout, Owned, Owner, _ => { }); Undo.PerformUndo();
        if (section.GetComponentsInChildren<Transform>(true).Length != count) throw new InvalidOperationException("Isolated clear/Undo failed for " + section.name);
        Physics.SyncTransforms(); Validate(layout);
        layout = BossHallFramework.SaveAndReload(layout); string summary = Validate(layout);
        BossHallSnapshot.Compare(before, CaptureProtected(), "Protected scene state after lower arena reload");
        BossHallSnapshot.Compare(assets, BossHallSnapshot.CaptureAssets(), "Shared assets after lower arena reload");
        var hashes = AssetHashes(); BuildAssets(); BossHallSnapshot.Compare(hashes, AssetHashes(), "Deterministic housing mesh rerun");
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Asset rerun dirtied MainScene");
        string neighbours = BossHallLowerArenaReview.NeighbourProof(layout, true);
        BossHallLowerArenaReview.Capture(layout);
        BossHallRenderingReview.ValidateNeighbours();
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Lower arena verification dirtied the saved scene.");
        var housing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath); int triangles = Enumerable.Range(0, housing.subMeshCount).Sum(i => (int)housing.GetIndexCount(i) / 3);
        File.WriteAllText("Docs/BossHall/LowerArena_052.txt", "corebuster_env_052 | Unity " + Application.unityVersion + " | " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            "\nSaved scene SHA-256: " + BossHallSurvey.Hash(BossHallSurvey.ScenePath) +
            "\nPASS: contract keep-outs, surveyed heavy-perimeter strips, wall stand-off, below-eye cap, finishes, hull/carve consolidation, floor support, front/end clearances, " +
            "intersections, turret and ranged-enemy lines, open central floor, eight negative guards, isolated clear/Undo, save/reload, deterministic mesh, " + before.Count +
            " protected scene/section states, " + assets.Count + " shared asset/meta/settings entries, Centre and Room 3 validators.\n" +
            $"Mesh: one owned first-principles mesh, {triangles} triangles x {plan.Count} instances = {triangles * plan.Count}; finishes reused: {string.Join("/", Finishes)}.\n" +
            summary + "\n" + neighbours + "\n" + BossHallFramework.BudgetReport() + "\n");
        Debug.Log("BOSS HALL LOWER ARENA BUILD VERIFIED");
    }

    public static void ValidateBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch Editor access.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        ValidateMenu(); BossHallRenderingReview.ValidateNeighbours();
        if (SceneManager.GetActiveScene().isDirty || sha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Read-only lower arena check changed the scene.");
        Debug.Log("BOSS HALL LOWER ARENA RELOAD VERIFIED");
    }

    static bool Inside(Bounds envelope, Bounds b) =>
        b.min.x >= envelope.min.x - .01f && b.min.y >= envelope.min.y - .01f && b.min.z >= envelope.min.z - .01f &&
        b.max.x <= envelope.max.x + .01f && b.max.y <= envelope.max.y + .01f && b.max.z <= envelope.max.z + .01f;
    static Bounds Expanded(Bounds b, float amount) { b.Expand(amount * 2); return b; }
    static Bounds MinMax(Vector3 a, Vector3 b) { var result = new Bounds(); result.SetMinMax(Vector3.Min(a, b), Vector3.Max(a, b)); return result; }
}
