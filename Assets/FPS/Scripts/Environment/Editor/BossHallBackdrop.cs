using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// corebuster_env_049: the final assembly and power backdrop on the west wall behind the unchanged 4x turret. It owns only
/// <c>Assembly_Backdrop</c> and <c>Assets/FPS/Art/BossHall/Assembly_Backdrop/</c>. Placement is derived from the accepted
/// env_047 contract: a low cradle in the west deep pocket below the inter-tier firing volume, a wall strip (ring, towers,
/// lintel) inside the 1.2 m wall band below the second tier, and an overhead gantry above second-tier headroom.
/// </summary>
public static class BossHallBackdrop
{
    public const string Owner = "corebuster_env_049";
    public const string Folder = BossHallFramework.AssetRoot + "/Assembly_Backdrop";
    public const string Output = "ReviewCaptures/BossHall/049";
    const string Turbine = "Assets/FPS/Art/NorthWestRoom/Turbine_Machinery/Meshes/";
    const string Heavy = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/Meshes/";
    const string Warehouse = "Assets/SciFi Warehouse Kit/Art/Meshes/Structures/Pillars and Beams/";
    public static readonly string[] Roots = { "Assembly_Cradle", "Assembly_Ring", "Service_Towers", "Assembly_Gantry" };
    public static readonly string[] Finishes = { "Gunmetal", "RedOxide", "HazardYellow" };
    static readonly string[] ReusePrefixes = { Turbine, Heavy, Warehouse };
    // Floor-level pieces stay clear of the wall plane so the separately scoped under-wall collision repair stays possible.
    const float WallStandoff = .35f, WallContactTop = 1f, UvScale = .35f;
    public const string HullName = "Consolidated_Collision_Hull";

    /// <summary>Placement frame derived from the contract; nothing here is a scene object.</summary>
    public sealed class Frame
    {
        public float floor, wallX, backX, stripFront, stripMid, lowTop, pocketFront, bedSouth, bedNorth, axisZ, deckUnder,
            upperTop, ceiling, lintelTop, lintelBottom, ringY, ringOuter, ringInner;
        public Bounds cradle, strip, gantry, hull, bayApproach;
        public Bounds[] Envelopes => new[] { cradle, strip, gantry };
    }

    public static Frame Measure(BossHallFramework.Layout layout)
    {
        var r = layout.Report; var room = layout.interior;
        BossHallSurvey.Zone Zone(string name) => r.zones.Single(z => z.name == name);
        var pocket = Zone("West deep-pocket 2"); var firing = Zone("Inter-tier firing volume");
        var upper = Zone("Upper deck network walking volume"); var bay = Zone("Wall BayDoor (1) approach");
        float axis = r.turret.pivot.z;
        // The two actors flanking the pocket (the Elite Riflemen) bound the cradle; it stays symmetric about the focal axis.
        var flank = r.actors.Where(a => a.kind != "turret" && a.envelope.min.x < pocket.max.x && a.envelope.max.x > pocket.min.x).ToArray();
        if (flank.Count(a => a.position.z < axis) != 1 || flank.Count(a => a.position.z > axis) != 1)
            throw new InvalidOperationException("Expected one measured actor envelope either side of the west deep pocket.");
        float south = Mathf.Max(pocket.min.z, flank.Single(a => a.position.z < axis).envelope.max.z) + .45f;
        float north = Mathf.Min(pocket.max.z, flank.Single(a => a.position.z > axis).envelope.min.z) - .45f;
        float half = Mathf.Min(axis - south, north - axis);
        var f = new Frame
        {
            floor = r.floorTopY, wallX = room.min.x, axisZ = axis, upperTop = upper.max.y,
            // The trimmed contract keeps deck footprints but not the tier-2 underside scalar (BUGS.md, 2026-10-04).
            deckUnder = r.deckFootprints.Where(d => d.name.StartsWith("Upper deck network:", StringComparison.Ordinal)).Min(d => d.min.y),
            ceiling = r.ceilingUndersideY, lowTop = firing.min.y - .1f, bedSouth = axis - half, bedNorth = axis + half,
        };
        f.backX = f.wallX + WallStandoff; f.stripFront = firing.min.x - .05f; f.stripMid = (f.backX + f.stripFront) * .5f;
        f.pocketFront = pocket.max.x - .9f;
        f.lintelTop = f.deckUnder - .12f; f.lintelBottom = f.lintelTop - 1f;
        f.ringOuter = 4.2f; f.ringInner = 3.3f; f.ringY = f.lintelBottom + .12f - f.ringOuter;
        f.cradle = MinMax(new Vector3(f.backX, f.floor, f.bedSouth), new Vector3(f.pocketFront, f.lowTop, f.bedNorth));
        f.hull = f.cradle;
        // The wall strip may sit just behind the stand-off above the wall-contact band (the stator backplate).
        f.strip = MinMax(new Vector3(f.wallX + .12f, f.floor, f.bedSouth), new Vector3(f.stripFront, f.deckUnder - .1f, f.bedNorth));
        f.gantry = MinMax(new Vector3(f.backX, f.upperTop + .3f, f.bedSouth - 4.5f), new Vector3(f.wallX + 8f, f.ceiling - .3f, f.bedNorth + 4.5f));
        f.bayApproach = MinMax(bay.min, bay.max);
        if (f.deckUnder < r.tier1TopY + 3f || f.deckUnder > r.tier2TopY ||
            f.ringY - f.ringOuter < f.floor || f.lowTop - f.floor < 2.3f || f.stripFront - f.backX < .7f || half < 9f)
            throw new InvalidOperationException("Measured backdrop envelopes are too small for the approved composition.");
        return f;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Assets: first-principles composed meshes under the section folder; reused meshes are referenced in place.

    sealed class Composer
    {
        readonly string name; readonly Vector3 pivot;
        readonly SortedDictionary<string, List<CombineInstance>> groups = new SortedDictionary<string, List<CombineInstance>>(StringComparer.Ordinal);
        readonly List<Mesh> temporary = new List<Mesh>();
        public Composer(string name, Vector3 pivot) { this.name = name; this.pivot = pivot; }
        public Vector3 Pivot => pivot;
        void Add(string finish, ProceduralMeshBuilder b, Vector3 position, Quaternion rotation)
        {
            if (!Finishes.Contains(finish)) throw new ArgumentException("Unplanned finish " + finish);
            var mesh = b.ToMesh("temp"); var uv = mesh.uv; for (int i = 0; i < uv.Length; i++) uv[i] *= UvScale; mesh.uv = uv;
            temporary.Add(mesh);
            if (!groups.TryGetValue(finish, out var list)) groups[finish] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(position - pivot, rotation, Vector3.one) });
        }
        public void Block(string finish, Vector3 centre, Vector3 size) => Block(finish, centre, size, Quaternion.identity);
        public void Block(string finish, Vector3 centre, Vector3 size, Quaternion rotation)
        { var b = new ProceduralMeshBuilder(); b.AddBlock(Vector3.zero, size); Add(finish, b, centre, rotation); }
        public void Box(string finish, Vector3 min, Vector3 max) => Block(finish, (min + max) * .5f, max - min);
        public void Cylinder(string finish, Vector3 a, Vector3 c, float radius, int segments = 24)
        { var b = new ProceduralMeshBuilder(); b.AddCylinder(a, c, radius, radius, segments, true); Add(finish, b, pivot, Quaternion.identity); }
        public void Torus(string finish, Vector3 centre, Vector3 normal, float major, float minor)
        { var b = new ProceduralMeshBuilder(); b.AddTorus(centre, normal, major, minor, 48, 10); Add(finish, b, pivot, Quaternion.identity); }
        /// <summary>Annulus starting at <paramref name="start"/> and extending along +X by <paramref name="depth"/>.</summary>
        public void AnnulusX(string finish, Vector3 start, float depth, float outer, float inner)
        { var b = new ProceduralMeshBuilder(); b.AddAnnularCylinderZ(depth, outer, inner, 64); Add(finish, b, start, Quaternion.Euler(0, 90, 0)); }
        public (Mesh mesh, string[] finishes) Build()
        {
            // Cylinder/torus primitives are authored around the pivot already; translate them back by the same pivot offset.
            var parts = new List<CombineInstance>(); var used = new List<string>();
            foreach (var g in groups)
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.CombineMeshes(g.Value.ToArray(), true, true);
                parts.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity }); used.Add(g.Key); temporary.Add(m);
            }
            var result = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            result.CombineMeshes(parts.ToArray(), false, false); result.RecalculateBounds();
            foreach (var m in temporary) Object.DestroyImmediate(m);
            return (result, used.ToArray());
        }
    }

    /// <summary>A generated asset part (owned composed mesh) or a reused source mesh with an explicit world transform.</summary>
    sealed class Piece
    {
        public string root, path, name;
        public Mesh mesh;
        public string[] finishes;
        public Vector3 position, scale = Vector3.one;
        public Quaternion rotation = Quaternion.identity;
        public bool reused;
    }

    sealed class Plan
    {
        public readonly List<Piece> pieces = new List<Piece>();
        public readonly Dictionary<string, Composer> composers = new Dictionary<string, Composer>(StringComparer.Ordinal);
        public Composer Compose(string root, string name, Vector3 pivot)
        {
            var c = new Composer("SM_Backdrop_" + name, pivot); composers.Add(root + "/" + name, c); return c;
        }
    }

    static Mesh Source(string path)
    {
        var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderBy(m => m.name, StringComparer.Ordinal).FirstOrDefault();
        return mesh ? mesh : throw new InvalidOperationException("Missing reused mesh " + path);
    }

    // Maps each local mesh axis to the world axis it points along after rotation, and scales it to that world size.
    static void Reuse(Plan plan, string root, string name, string path, string finish, Vector3 centre, Quaternion rotation, Vector3 worldSize)
    {
        var mesh = Source(path); var s = mesh.bounds.size; var scale = Vector3.one;
        for (int i = 0; i < 3; i++)
        {
            var axis = rotation * (i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
            int j = Mathf.Abs(axis.x) > .5f ? 0 : Mathf.Abs(axis.y) > .5f ? 1 : 2;
            if (Mathf.Abs(axis[j]) < .99f) throw new InvalidOperationException("Reused pieces must be axis-aligned: " + name);
            scale[i] = worldSize[j] / Mathf.Max(.0001f, s[i]);
        }
        plan.pieces.Add(new Piece { root = root, name = name, path = path, mesh = mesh, finishes = Enumerable.Repeat(finish, Mathf.Max(1, mesh.subMeshCount)).ToArray(),
            rotation = rotation, scale = scale, position = centre - rotation * Vector3.Scale(mesh.bounds.center, scale), reused = true });
    }
    static Quaternion Align(string path, Vector3 worldDirection, bool shortest)
    {
        var s = Source(path).bounds.size;
        int a = shortest ? (s.x <= s.y && s.x <= s.z ? 0 : s.y <= s.z ? 1 : 2) : (s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2);
        return Quaternion.FromToRotation(a == 0 ? Vector3.right : a == 1 ? Vector3.up : Vector3.forward, worldDirection);
    }
    static Vector3 Sized(Vector3 direction, float along, float across)
    {
        var d = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
        return d * along + (Vector3.one - d) * across;
    }

    static Plan Compose(Frame f)
    {
        var plan = new Plan();
        Cradle(plan, f); Ring(plan, f); Towers(plan, f); Gantry(plan, f);
        return plan;
    }

    // Root 1: the low assembly cradle in the west deep pocket, wholly below the inter-tier firing volume.
    static void Cradle(Plan plan, Frame f)
    {
        const string root = "Assembly_Cradle";
        float x0 = f.backX, x1 = f.pocketFront, y0 = f.floor, z0 = f.bedSouth, z1 = f.bedNorth, bodyTop = y0 + 1.55f, bodyFront = x1 - .35f;
        var bed = plan.Compose(root, "Cradle_Bed", new Vector3((x0 + x1) * .5f, y0, f.axisZ));
        bed.Box("Gunmetal", new Vector3(x0, y0, z0), new Vector3(x1, y0 + .42f, z1));
        bed.Box("HazardYellow", new Vector3(x1 - .05f, y0 + .06f, z0 + .05f), new Vector3(x1, y0 + .36f, z1 - .05f));
        bed.Box("RedOxide", new Vector3(x0, y0 + .42f, z0 + .3f), new Vector3(bodyFront, bodyTop, z1 - .3f));
        for (float z = z0 + 1.05f; z < z1 - .8f; z += 1.6f)
            bed.Box("Gunmetal", new Vector3(bodyFront - .02f, y0 + .42f, z - .09f), new Vector3(bodyFront + .1f, bodyTop - .05f, z + .09f));
        bed.Box("Gunmetal", new Vector3(x0, bodyTop - .12f, z0 + .3f), new Vector3(bodyFront + .08f, bodyTop, z1 - .3f));
        // Ring saddle: the assembly ring seats into it at the focal axis.
        float seatS = f.axisZ - 3.7f, seatN = f.axisZ + 3.7f;
        bed.Box("Gunmetal", new Vector3(x0, bodyTop, seatS), new Vector3(x0 + 1.25f, f.lowTop, seatN));
        bed.Box("HazardYellow", new Vector3(x0 + 1.25f, bodyTop, seatS), new Vector3(x0 + 1.32f, f.lowTop - .05f, seatS + .5f));
        bed.Box("HazardYellow", new Vector3(x0 + 1.25f, bodyTop, seatN - .5f), new Vector3(x0 + 1.32f, f.lowTop - .05f, seatN));
        // Tower plinths carry the wall towers above the cradle.
        foreach (float tz in new[] { z0, z1 - 4f })
            bed.Box("Gunmetal", new Vector3(x0, bodyTop, tz + .15f), new Vector3(x0 + 1.05f, f.lowTop, tz + 3.85f));

        // Room 3 turbine drums reused as the twin power generators feeding the ring.
        float drumY = f.lowTop - .84f, drumX = bodyFront - 1.07f;
        for (int side = 0; side < 2; side++)
        {
            float inner = side == 0 ? seatS - .35f : seatN + .35f, dir = side == 0 ? -1 : 1, outer = inner + dir * 6f;
            float mid = (inner + outer) * .5f; string tag = side == 0 ? "South" : "North";
            var along = Quaternion.Euler(0, 90, 0);
            Reuse(plan, root, "Generator_Drum_" + tag, Turbine + "SM_Turbine_Drum.asset", "RedOxide", new Vector3(drumX, drumY, mid), along, new Vector3(1.44f, 1.44f, 6f));
            Reuse(plan, root, "Generator_Bell_Outer_" + tag, Turbine + "SM_End_Bell.asset", "Gunmetal", new Vector3(drumX, drumY, outer - dir * .1f), along, new Vector3(1.64f, 1.64f, .55f));
            Reuse(plan, root, "Generator_Flange_Inner_" + tag, Turbine + "SM_Drive_Flange.asset", "HazardYellow", new Vector3(drumX, drumY, inner + dir * .1f), along, new Vector3(1.6f, 1.6f, .2f));
            for (int i = 0; i < 4; i++)
                Reuse(plan, root, $"Generator_Fin_{tag}_{i}", Turbine + "SM_Cooling_Fin.asset", "Gunmetal",
                    new Vector3(drumX, drumY, inner + dir * (1.4f + i * 1.15f)), along, new Vector3(2.05f, 1.6f, .09f));
            for (int i = 0; i < 3; i++)
                Reuse(plan, root, $"Anchor_Foot_{tag}_{i}", Turbine + "SM_Anchor_Foot.asset", "Gunmetal",
                    new Vector3(x1 - .45f, y0 + .42f + .16f, inner + dir * (1f + i * 2.2f)), Quaternion.Euler(0, 90, 0), new Vector3(.9f, .32f, .72f));
            // Coolant header behind the drum: reused heavy pipe and flanges along the cradle.
            float hx = x0 + .55f, hy = f.lowTop - .32f, pipeD = .5f;
            var pipeRot = Align(Heavy + "SM_HeavyPipe_Straight_2m.asset", Vector3.forward, false);
            var flangeRot = Align(Heavy + "SM_HeavyPipe_Flange.asset", Vector3.forward, true);
            for (int i = 0; i < 3; i++)
            {
                float zc = inner + dir * (1f + i * 2f);
                Reuse(plan, root, $"Coolant_Header_{tag}_{i}", Heavy + "SM_HeavyPipe_Straight_2m.asset", "Gunmetal", new Vector3(hx, hy, zc), pipeRot, Sized(Vector3.forward, 2f, pipeD));
            }
            for (int i = 0; i < 4; i++)
                Reuse(plan, root, $"Coolant_Flange_{tag}_{i}", Heavy + "SM_HeavyPipe_Flange.asset", "HazardYellow", new Vector3(hx, hy, inner + dir * i * 2f), flangeRot, Sized(Vector3.forward, .12f, .62f));
        }
    }

    // Root 2: the assembly ring framing the turret from the entry, its stator, clamps and the two heavy pistons.
    static void Ring(Plan plan, Frame f)
    {
        const string root = "Assembly_Ring";
        var centre = new Vector3(f.stripMid, f.ringY, f.axisZ);
        float depth = f.stripFront - f.backX;
        var body = plan.Compose(root, "Ring_Body", centre);
        body.AnnulusX("RedOxide", new Vector3(f.backX, f.ringY, f.axisZ), depth, f.ringOuter, f.ringInner);
        body.AnnulusX("Gunmetal", new Vector3(f.stripFront - .14f, f.ringY, f.axisZ), .12f, f.ringOuter + .12f, f.ringOuter - .18f);
        body.AnnulusX("Gunmetal", new Vector3(f.stripFront - .14f, f.ringY, f.axisZ), .12f, f.ringInner + .18f, f.ringInner - .1f);
        foreach (float angle in new[] { 50f, -50f, 130f, -130f })
        {
            var rot = Quaternion.AngleAxis(angle, Vector3.right); var radial = rot * Vector3.up;
            var at = new Vector3(f.stripMid, f.ringY, f.axisZ) + radial * f.ringOuter;
            body.Block("Gunmetal", at, new Vector3(depth, 1.35f, 1.05f), rot);
            body.Block("HazardYellow", at + Vector3.right * (depth * .5f - .03f), new Vector3(.06f, 1.1f, .8f), rot);
        }
        // Top hanger: the ring hangs from the lintel at the focal axis.
        body.Box("Gunmetal", new Vector3(f.backX, f.ringY + f.ringOuter - .5f, f.axisZ - .7f), new Vector3(f.stripFront, f.lintelBottom + .05f, f.axisZ + .7f));

        // Stator: recessed backplate, hub and spokes. The open sectors are env_053's reserved contained-energy envelope.
        var stator = plan.Compose(root, "Ring_Stator", centre);
        float back = f.wallX + .14f;
        stator.Cylinder("Gunmetal", new Vector3(back, f.ringY, f.axisZ) - centre, new Vector3(back + .1f, f.ringY, f.axisZ) - centre, f.ringInner + .05f, 48);
        stator.Cylinder("Gunmetal", new Vector3(back + .1f, f.ringY, f.axisZ) - centre, new Vector3(f.backX + .55f, f.ringY, f.axisZ) - centre, .95f, 32);
        stator.Cylinder("HazardYellow", new Vector3(f.backX + .55f, f.ringY, f.axisZ) - centre, new Vector3(f.backX + .63f, f.ringY, f.axisZ) - centre, .55f, 32);
        stator.Torus("Gunmetal", new Vector3(f.backX + .1f, f.ringY, f.axisZ) - centre, Vector3.right, 2.35f, .11f);
        for (int i = 0; i < 6; i++)
        {
            var rot = Quaternion.AngleAxis(30f + i * 60f, Vector3.right); var radial = rot * Vector3.up;
            float mid = (.95f + f.ringInner) * .5f, len = f.ringInner - .95f + .1f;
            stator.Block("Gunmetal", new Vector3(f.backX + .1f, f.ringY, f.axisZ) + radial * mid, new Vector3(.22f, len, .26f), rot);
        }

        // Two hydraulic rams from the tower plinths up to the upper clamps.
        var rams = plan.Compose(root, "Ring_Pistons", centre);
        for (int side = 0; side < 2; side++)
        {
            float dir = side == 0 ? -1 : 1;
            var clamp = new Vector3(f.stripMid, f.ringY, f.axisZ) + Quaternion.AngleAxis(dir * 50f, Vector3.right) * Vector3.up * (f.ringOuter + .55f);
            var foot = new Vector3(f.stripMid, f.lowTop + .3f, (side == 0 ? f.bedSouth + 4.6f : f.bedNorth - 4.6f));
            var a = foot - centre; var b = clamp - centre; var housingEnd = Vector3.Lerp(a, b, .58f);
            rams.Cylinder("RedOxide", a, housingEnd, .3f);
            rams.Cylinder("HazardYellow", housingEnd - (b - a).normalized * .2f, housingEnd + (b - a).normalized * .05f, .34f);
            rams.Cylinder("Gunmetal", Vector3.Lerp(a, b, .5f), b, .15f);
            rams.Block("Gunmetal", foot, new Vector3(depth - .1f, .55f, .7f));
            rams.Block("Gunmetal", clamp, new Vector3(depth - .1f, .45f, .45f));
        }
    }

    // Root 3: two service towers (power risers, coolant accumulators, breaker cabinets) and the lintel that carries the ring.
    static void Towers(Plan plan, Frame f)
    {
        const string root = "Service_Towers";
        float depth = f.stripFront - f.backX, y0 = f.lowTop, y1 = f.lintelBottom;
        for (int side = 0; side < 2; side++)
        {
            string tag = side == 0 ? "South" : "North";
            float outer = side == 0 ? f.bedSouth : f.bedNorth, dir = side == 0 ? 1 : -1; // dir points inward from the outer edge
            float Z(float d) => outer + dir * d;
            var frame = plan.Compose(root, "Tower_Frame_" + tag, new Vector3(f.stripMid, (y0 + y1) * .5f, Z(2f)));
            // Lower: electrical cabinet beside the accumulator. Upper: breaker cabinet with hazard banding.
            frame.Box("Gunmetal", new Vector3(f.backX, y0 + .05f, Mathf.Min(Z(2.35f), Z(3.4f))), new Vector3(f.stripFront - .05f, y0 + 2.75f, Mathf.Max(Z(2.35f), Z(3.4f))));
            frame.Box("HazardYellow", new Vector3(f.stripFront - .08f, y0 + 2.35f, Mathf.Min(Z(2.4f), Z(3.35f))), new Vector3(f.stripFront, y0 + 2.5f, Mathf.Max(Z(2.4f), Z(3.35f))));
            frame.Box("RedOxide", new Vector3(f.backX, y0 + 3.05f, Mathf.Min(Z(1.35f), Z(3.4f))), new Vector3(f.stripFront - .05f, y1 - .35f, Mathf.Max(Z(1.35f), Z(3.4f))));
            for (int i = 0; i < 3; i++)
            {
                float y = y0 + 3.4f + i * 1.0f;
                frame.Box("Gunmetal", new Vector3(f.stripFront - .1f, y, Mathf.Min(Z(1.5f), Z(3.2f))), new Vector3(f.stripFront, y + .55f, Mathf.Max(Z(1.5f), Z(3.2f))));
            }
            frame.Box("HazardYellow", new Vector3(f.stripFront - .08f, y1 - .65f, Mathf.Min(Z(1.35f), Z(3.4f))), new Vector3(f.stripFront, y1 - .45f, Mathf.Max(Z(1.35f), Z(3.4f))));
            // Cross bracing in the upper bay, flat bars in the YZ plane.
            float braceH = y1 - (y0 + 3.05f), braceW = 2.4f, braceL = Mathf.Sqrt(braceH * braceH + braceW * braceW) * .5f;
            float braceAngle = Mathf.Atan2(braceW, braceH) * Mathf.Rad2Deg;
            var braceCentre = new Vector3(f.backX + .06f, (y0 + 3.05f + y1) * .5f, Z(2.2f));
            frame.Block("Gunmetal", braceCentre, new Vector3(.08f, braceL * 2f, .14f), Quaternion.AngleAxis(braceAngle, Vector3.right));
            frame.Block("Gunmetal", braceCentre, new Vector3(.08f, braceL * 2f, .14f), Quaternion.AngleAxis(-braceAngle, Vector3.right));
            // Pipe brackets for the riser.
            for (int i = 0; i < 3; i++)
                frame.Box("Gunmetal", new Vector3(f.backX, y0 + 1.2f + i * 2.2f, Mathf.Min(Z(.75f), Z(1.15f))), new Vector3(f.stripMid + .05f, y0 + 1.35f + i * 2.2f, Mathf.Max(Z(.75f), Z(1.15f))));

            // Reused Warehouse Kit pillars as the tower posts.
            foreach (float d in new[] { .3f, 3.7f })
                Reuse(plan, root, $"Tower_Post_{tag}_{(d < 1 ? "Outer" : "Inner")}", Warehouse + "Pillar.fbx", "Gunmetal",
                    new Vector3(f.stripMid, (y0 + y1) * .5f, Z(d)), Align(Warehouse + "Pillar.fbx", Vector3.up, false), new Vector3(.55f, y1 - y0, .55f));
            // Reused P-3 tank as a coolant accumulator in the lower bay.
            var tankRot = Align(Heavy + "SM_PressureTank_P3_Body.asset", Vector3.up, false);
            var tankSize = new Vector3(.72f, 2.5f, .72f); var tankCentre = new Vector3(f.stripMid, y0 + .1f + 1.25f, Z(1.75f));
            Reuse(plan, root, "Coolant_Accumulator_" + tag, Heavy + "SM_PressureTank_P3_Body.asset", "RedOxide", tankCentre, tankRot, tankSize);
            var body = plan.pieces.Last();
            var bands = Source(Heavy + "SM_PressureTank_P3_Bands.asset");
            plan.pieces.Add(new Piece { root = root, name = "Coolant_Accumulator_Bands_" + tag, path = Heavy + "SM_PressureTank_P3_Bands.asset", mesh = bands,
                finishes = Enumerable.Repeat("HazardYellow", Mathf.Max(1, bands.subMeshCount)).ToArray(), position = body.position, rotation = body.rotation, scale = body.scale, reused = true });
            // Reused heavy pipe power riser from the cradle header to the lintel.
            var pipeRot = Align(Heavy + "SM_HeavyPipe_Straight_2m.asset", Vector3.up, false);
            var flangeRot = Align(Heavy + "SM_HeavyPipe_Flange.asset", Vector3.up, true);
            float run = (y1 - y0) / 3f;
            for (int i = 0; i < 3; i++)
                Reuse(plan, root, $"Power_Riser_{tag}_{i}", Heavy + "SM_HeavyPipe_Straight_2m.asset", "Gunmetal",
                    new Vector3(f.stripMid, y0 + run * (i + .5f), Z(.95f)), pipeRot, Sized(Vector3.up, run, .5f));
            for (int i = 0; i < 4; i++)
                Reuse(plan, root, $"Power_Riser_Flange_{tag}_{i}", Heavy + "SM_HeavyPipe_Flange.asset", "HazardYellow",
                    new Vector3(f.stripMid, y0 + run * i + (i == 0 ? .06f : i == 3 ? -.06f : 0), Z(.95f)), flangeRot, Sized(Vector3.up, .12f, .68f));
        }
        // Lintel: an I-girder under the bridge deck spanning both towers, with stiffeners and a hazard edge.
        var lintel = plan.Compose(root, "Lintel_Girder", new Vector3(f.stripMid, (f.lintelBottom + f.lintelTop) * .5f, f.axisZ));
        float zs = f.bedSouth, zn = f.bedNorth, lb = f.lintelBottom, lt = f.lintelTop;
        lintel.Box("RedOxide", new Vector3(f.backX, lt - .16f, zs), new Vector3(f.stripFront, lt, zn));
        lintel.Box("RedOxide", new Vector3(f.backX, lb, zs), new Vector3(f.stripFront, lb + .16f, zn));
        lintel.Box("Gunmetal", new Vector3(f.backX + .25f, lb + .16f, zs), new Vector3(f.backX + .42f, lt - .16f, zn));
        for (float z = zs + .2f; z <= zn - .1f; z += 1.55f)
            lintel.Box("Gunmetal", new Vector3(f.backX + .02f, lb + .16f, z - .06f), new Vector3(f.stripFront - .04f, lt - .16f, z + .06f));
        lintel.Box("HazardYellow", new Vector3(f.stripFront - .05f, lb + .02f, zs + .1f), new Vector3(f.stripFront, lb + .14f, zn - .1f));
    }

    // Root 4: overhead gantry crane runway on wall corbels, with a parked hoist over the focal axis.
    static void Gantry(Plan plan, Frame f)
    {
        const string root = "Assembly_Gantry";
        float beamY0 = f.ceiling - 2f, beamY1 = beamY0 + .7f, runwayX = f.wallX + 6.6f, runwayY0 = beamY1, runwayY1 = runwayY0 + .82f;
        float zs = f.bedSouth - 3.6f, zn = f.bedNorth + 3.6f;
        foreach (float z in new[] { f.bedSouth + 2f, f.bedNorth - 2f })
        {
            // One mesh per side: a combined bound would span the west bay-door approach between them.
            var supports = plan.Compose(root, "Gantry_Support_" + (z < f.axisZ ? "South" : "North"), new Vector3(f.stripMid, beamY0, z));
            supports.Box("Gunmetal", new Vector3(f.backX, beamY0 - 1.7f, z - .45f), new Vector3(f.stripFront, beamY0, z + .45f));
            supports.Box("HazardYellow", new Vector3(f.stripFront - .05f, beamY0 - 1.6f, z - .35f), new Vector3(f.stripFront, beamY0 - 1.45f, z + .35f));
            supports.Cylinder("RedOxide", new Vector3(f.stripFront - .1f, beamY0 - 1.45f, z) - supports.Pivot, new Vector3(runwayX - .9f, beamY0 - .05f, z) - supports.Pivot, .13f, 16);
            // Reused Warehouse Kit ceiling beam as each cantilever.
            Reuse(plan, root, "Gantry_Cantilever_" + (z < f.axisZ ? "South" : "North"), Warehouse + "Ceiling Beam.fbx", "RedOxide",
                new Vector3((f.backX + runwayX + .6f) * .5f, (beamY0 + beamY1) * .5f, z), Align(Warehouse + "Ceiling Beam.fbx", Vector3.right, false),
                new Vector3(runwayX + .6f - f.backX, beamY1 - beamY0, .55f));
        }
        var runway = plan.Compose(root, "Gantry_Runway", new Vector3(runwayX, (runwayY0 + runwayY1) * .5f, f.axisZ));
        runway.Box("Gunmetal", new Vector3(runwayX - .5f, runwayY0, zs), new Vector3(runwayX + .5f, runwayY0 + .14f, zn));
        runway.Box("Gunmetal", new Vector3(runwayX - .5f, runwayY1 - .14f, zs), new Vector3(runwayX + .5f, runwayY1, zn));
        runway.Box("RedOxide", new Vector3(runwayX - .09f, runwayY0 + .14f, zs), new Vector3(runwayX + .09f, runwayY1 - .14f, zn));
        for (float z = zs + .3f; z <= zn; z += 2.4f)
            runway.Box("Gunmetal", new Vector3(runwayX - .45f, runwayY0 + .14f, z - .05f), new Vector3(runwayX + .45f, runwayY1 - .14f, z + .05f));
        foreach (float z in new[] { zs, zn })
            runway.Box("HazardYellow", new Vector3(runwayX - .55f, runwayY0 - .05f, z - (z < f.axisZ ? 0 : .25f)), new Vector3(runwayX + .55f, runwayY1 + .05f, z + (z < f.axisZ ? .25f : 0)));
        // Parked hoist: trolley under the runway, twin cables and a hook block above second-tier headroom.
        var hoist = plan.Compose(root, "Gantry_Hoist", new Vector3(runwayX, runwayY0 - 2f, f.axisZ));
        float hookTop = f.upperTop + 2f;
        hoist.Box("HazardYellow", new Vector3(runwayX - .8f, runwayY0 - .75f, f.axisZ - 1f), new Vector3(runwayX + .8f, runwayY0 - .05f, f.axisZ + 1f));
        hoist.Box("Gunmetal", new Vector3(runwayX - .55f, runwayY0 - .95f, f.axisZ - .6f), new Vector3(runwayX + .55f, runwayY0 - .75f, f.axisZ + .6f));
        foreach (float dz in new[] { -.3f, .3f })
            hoist.Cylinder("Gunmetal", new Vector3(runwayX, runwayY0 - .95f, f.axisZ + dz) - hoist.Pivot, new Vector3(runwayX, hookTop, f.axisZ + dz) - hoist.Pivot, .035f, 8);
        hoist.Box("HazardYellow", new Vector3(runwayX - .38f, hookTop - .8f, f.axisZ - .45f), new Vector3(runwayX + .38f, hookTop, f.axisZ + .45f));
        hoist.Torus("Gunmetal", new Vector3(runwayX, hookTop - 1.15f, f.axisZ) - hoist.Pivot, Vector3.right, .3f, .08f);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    /// <summary>Writes the owned composed meshes. Runs before the scene transaction; scene Undo cannot restore asset writes.</summary>
    static Plan BuildAssets(Frame f)
    {
        EnsureFolder(Folder + "/Meshes");
        var plan = Compose(f);
        foreach (var entry in plan.composers.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var (generated, used) = entry.Value.Build();
            string path = Folder + "/Meshes/" + generated.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
            else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
            BossHallFramework.RequireOwnedAsset(existing, BossHallFramework.Section.AssemblyBackdrop);
            string[] parts = entry.Key.Split('/');
            plan.pieces.Add(new Piece { root = parts[0], name = parts[1], path = path, mesh = existing, finishes = used, position = entry.Value.Pivot });
        }
        AssetDatabase.SaveAssets();
        foreach (var p in plan.pieces.Where(p => !p.reused)) p.mesh = AssetDatabase.LoadAssetAtPath<Mesh>(p.path);
        return plan;
    }

    static void Populate(Transform section, Frame f, Plan plan)
    {
        var roots = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (string name in Roots) roots[name] = Create(name, section);
        foreach (var p in plan.pieces.OrderBy(p => Array.IndexOf(Roots, p.root)).ThenBy(p => p.reused).ThenBy(p => p.name, StringComparer.Ordinal))
        {
            var t = Create(p.name, roots[p.root]);
            t.SetPositionAndRotation(p.position, p.rotation); t.localScale = p.scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterials = p.finishes.Select(BossHallRendering.GetFinish).ToArray();
            r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
        }
        // One consolidated collision hull and carving footprint for the only reachable (floor-level) assembly.
        var hull = Create(HullName, roots["Assembly_Cradle"]);
        hull.position = f.hull.center;
        var box = hull.gameObject.AddComponent<BoxCollider>(); box.size = f.hull.size;
        var obstacle = hull.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = f.hull.size; obstacle.center = Vector3.zero;
        obstacle.carving = true; obstacle.carveOnlyStationary = true;
    }

    static Transform Create(string name, Transform parent)
    {
        var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Build Boss Hall backdrop");
        go.transform.SetParent(parent, false); return go.transform;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Validation beyond the framework: envelopes, wall stand-off, finishes, mesh provenance, collision and firing lines.

    public static string Validate(BossHallFramework.Layout layout)
    {
        BossHallRendering.Validate(layout);
        var f = Measure(layout);
        var section = BossHallFramework.FindSection(BossHallFramework.Section.AssemblyBackdrop);
        var names = section.Cast<Transform>().Select(t => t.name).ToArray();
        if (!names.SequenceEqual(Roots)) throw new InvalidOperationException("Assembly_Backdrop must contain exactly " + string.Join(", ", Roots));
        var renderers = section.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length < 40) throw new InvalidOperationException("Backdrop is missing pieces: " + renderers.Length);
        foreach (var r in renderers)
        {
            if (BossHallEnergy.IsEnergyRenderer(r)) continue; // Independently validated env_053 child subdivision.
            string path = BossHallFramework.PathOf(r.transform); var b = r.bounds;
            if (!f.Envelopes.Any(e => Inside(e, b))) throw new InvalidOperationException(path + " leaves the backdrop placement envelopes");
            if (b.min.y < f.floor + WallContactTop && b.min.x < f.backX - .01f) throw new InvalidOperationException(path + " contacts the wall at floor level");
            if (!(r is MeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy) throw new InvalidOperationException("Invalid backdrop renderer " + path);
            var filter = r.GetComponent<MeshFilter>(); string meshPath = filter && filter.sharedMesh ? AssetDatabase.GetAssetPath(filter.sharedMesh) : "";
            bool owned = meshPath.StartsWith(Folder + "/Meshes/", StringComparison.Ordinal);
            if (!owned && !ReusePrefixes.Any(p => meshPath.StartsWith(p, StringComparison.Ordinal))) throw new InvalidOperationException("Unreviewed mesh source " + meshPath + " at " + path);
            if (owned && EditorUtility.IsDirty(filter.sharedMesh)) throw new InvalidOperationException("Unsaved backdrop mesh " + meshPath);
            if (r.sharedMaterials.Length != filter.sharedMesh.subMeshCount || r.sharedMaterials.Any(m => !Finishes.Any(n => m == BossHallRendering.GetFinish(n))))
                throw new InvalidOperationException("Backdrop finish outside its three planned finishes: " + path);
        }
        var colliders = section.GetComponentsInChildren<Collider>(true);
        var obstacles = section.GetComponentsInChildren<NavMeshObstacle>(true);
        var hull = colliders.Length == 1 ? colliders[0] as BoxCollider : null;
        if (!hull || hull.name != HullName || obstacles.Length != 1 || obstacles[0].gameObject != hull.gameObject ||
            Vector3.Distance(hull.bounds.min, f.hull.min) > .01f || Vector3.Distance(hull.bounds.max, f.hull.max) > .01f ||
            obstacles[0].shape != NavMeshObstacleShape.Box || Vector3.Distance(obstacles[0].size, f.hull.size) > .01f || obstacles[0].center != Vector3.zero)
            throw new InvalidOperationException("Expected exactly one consolidated cradle collision hull and matching carving footprint.");
        if (hull.bounds.max.y > f.lowTop + .01f) throw new InvalidOperationException("Hull reaches the inter-tier firing volume.");
        if (section.GetComponentsInChildren<Transform>(true).Any(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)))
            throw new InvalidOperationException("Backdrop reuses meshes in place; prefab instances are not expected.");
        string firing = FiringLines(layout, f, hull);
        var root = BossHallFramework.FindRoot();
        int materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count();
        int reused = renderers.Count(r => !AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh).StartsWith(Folder + "/", StringComparison.Ordinal));
        string summary = $"BOSS HALL BACKDROP: {Roots.Length} assembly roots, {renderers.Length} renderers ({reused} reused source meshes, {renderers.Length - reused} owned composed meshes), " +
            $"1 consolidated collider + 1 carving footprint, 0 prefab instances/lights/emitters, {Finishes.Length} section finishes, {materials}/20 whole-hall materials.\n" + firing;
        Debug.Log(summary);
        return summary;
    }

    // Floor (2 m grid) and gallery (1 m grid) standing eyes within turret detection: no line that is clear in the
    // original hall may be blocked by the hull. Positions now inside the hull are reported as occupied.
    static string FiringLines(BossHallFramework.Layout layout, Frame f, BoxCollider hull)
    {
        var r = layout.Report; var t = r.turret; var room = layout.interior;
        // Eye height and radius come from the saved player; the trimmed contract does not carry them.
        var player = Object.FindAnyObjectByType<Unity.FPS.Gameplay.PlayerCharacterController>() ?? throw new InvalidOperationException("Missing saved player");
        float eye = player.CapsuleHeightStanding * player.CameraHeightRatio, radius = player.GetComponent<CharacterController>().radius;
        int inRange = 0, occupied = 0, blocked = 0, galleryInRange = 0, galleryBlocked = 0;
        var occupiedBox = hull.bounds; occupiedBox.Expand(new Vector3(radius * 2, 0, radius * 2));
        for (float x = room.min.x + 1; x < room.max.x; x += 2)
            for (float z = room.min.z + 1; z < room.max.z; z += 2)
            {
                var feet = new Vector3(x, r.floorTopY, z); var target = feet + Vector3.up * eye;
                float d = Vector3.Distance(t.firingOrigin, target); if (d > t.detectionRange) continue;
                inRange++;
                if (occupiedBox.Contains(feet + Vector3.up * .5f)) { occupied++; continue; }
                if (hull.Raycast(new Ray(t.firingOrigin, (target - t.firingOrigin) / d), out _, d)) blocked++;
            }
        foreach (var deck in r.deckFootprints.Where(k => k.name.Contains(" gallery:")))
            for (float x = deck.min.x + .5f; x < deck.max.x; x += 1)
                for (float z = deck.min.z + .5f; z < deck.max.z; z += 1)
                {
                    var target = new Vector3(x, deck.max.y + eye, z); float d = Vector3.Distance(t.firingOrigin, target);
                    if (d > t.detectionRange) continue; galleryInRange++;
                    if (hull.Raycast(new Ray(t.firingOrigin, (target - t.firingOrigin) / d), out _, d)) galleryBlocked++;
                }
        var rest = new Ray(t.firingOrigin, t.restForward);
        bool restBlocked = hull.Raycast(rest, out _, t.attackRange);
        var sight = new Ray(r.entryEye, ((t.head.min + t.head.max) * .5f - r.entryEye).normalized);
        float sightLength = Vector3.Distance(r.entryEye, (t.head.min + t.head.max) * .5f);
        var section = BossHallFramework.FindSection(BossHallFramework.Section.AssemblyBackdrop);
        bool sightBlocked = section.GetComponentsInChildren<Renderer>(true).Any(x => x.bounds.IntersectRay(sight, out float hit) && hit < sightLength);
        if (blocked > 0 || galleryBlocked > 0 || restBlocked || sightBlocked)
            throw new InvalidOperationException($"Backdrop blocks turret lines: floor {blocked}, gallery {galleryBlocked}, rest axis {restBlocked}, entry sight {sightBlocked}");
        return $"Turret lines: floor {inRange} eyes in detection, {occupied} now inside the cradle footprint, 0 newly blocked; tier-1 galleries {galleryInRange} eyes, 0 blocked; " +
            "rest firing axis clear of the hull; entry eye to turret head clear of every backdrop renderer.";
    }

    static bool Inside(Bounds envelope, Bounds b) =>
        b.min.x >= envelope.min.x - .01f && b.min.y >= envelope.min.y - .01f && b.min.z >= envelope.min.z - .01f &&
        b.max.x <= envelope.max.x + .01f && b.max.y <= envelope.max.y + .01f && b.max.z <= envelope.max.z + .01f;
    static Bounds MinMax(Vector3 min, Vector3 max) { var b = new Bounds(); b.SetMinMax(min, max); return b; }

    // ---------------------------------------------------------------------------------------------------------------

    [MenuItem("Tools/CoreBuster/Build Boss Hall Assembly Backdrop")]
    public static void BuildMenu()
    {
        var layout = BossHallFramework.MeasureLayout(); var f = Measure(layout); var plan = BuildAssets(f);
        BossHallFramework.RebuildSection(layout, BossHallFramework.Section.AssemblyBackdrop, Owner, t => Populate(t, f, plan));
        Validate(layout);
        Debug.Log("Boss Hall assembly backdrop built with Undo; review, then save using the guarded batch workflow or in the Editor.");
    }
    [MenuItem("Tools/CoreBuster/Validate Boss Hall Assembly Backdrop")]
    public static void ValidateMenu() => Validate(BossHallFramework.MeasureLayout());

    internal static void RebuildForEnergyVerification(BossHallFramework.Layout layout)
    {
        var f = Measure(layout); var plan = BuildAssets(f);
        BossHallFramework.RebuildSection(layout, BossHallFramework.Section.AssemblyBackdrop, Owner, t => Populate(t, f, plan));
        Validate(layout);
    }

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Batch requires exclusive Editor access");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); BossHallRendering.Validate(layout);
        var f = Measure(layout);
        var before = BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot()); var assets = BossHallSnapshot.CaptureAssets();
        BossHallBackdropReview.NeighbourProof(layout, false);
        var plan = BuildAssets(f);
        BossHallFramework.RebuildSection(layout, BossHallFramework.Section.AssemblyBackdrop, Owner, t => Populate(t, f, plan));
        Validate(layout);
        VerifyGuards(layout);
        var section = BossHallFramework.FindSection(BossHallFramework.Section.AssemblyBackdrop);
        int count = section.GetComponentsInChildren<Transform>(true).Length;
        BossHallFramework.RebuildSection(layout, BossHallFramework.Section.AssemblyBackdrop, Owner, _ => { }); Undo.PerformUndo();
        if (section.GetComponentsInChildren<Transform>(true).Length != count) throw new InvalidOperationException("Backdrop clear/Undo failed");
        Debug.Log("PASS: isolated Assembly_Backdrop clear and Undo");
        string summary = Validate(layout);
        layout = BossHallFramework.SaveAndReload(layout); summary = Validate(layout);
        BossHallSnapshot.Compare(before, BossHallSnapshot.Capture(SceneManager.GetActiveScene(), BossHallFramework.FindRoot()), "Original scene after backdrop reload");
        BossHallSnapshot.Compare(assets, BossHallSnapshot.CaptureAssets(), "Shared assets after backdrop reload");
        var generated = Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => BossHallSurvey.Hash(p));
        BuildAssets(Measure(layout));
        BossHallSnapshot.Compare(generated, Directory.GetFiles(Folder, "*", SearchOption.AllDirectories).ToDictionary(p => p, p => BossHallSurvey.Hash(p)), "Deterministic backdrop assets rerun");
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Asset rerun dirtied MainScene");
        string neighbours = BossHallBackdropReview.NeighbourProof(layout, true);
        BossHallBackdropReview.Capture(layout);
        BossHallRenderingReview.ValidateNeighbours();
        Directory.CreateDirectory("Docs/BossHall");
        File.WriteAllText("Docs/BossHall/Backdrop_049.txt", "corebuster_env_049 | Unity " + Application.unityVersion + " | 2026-10-04\nSaved scene: " + BossHallSurvey.Hash(BossHallSurvey.ScenePath) +
            "\nPASS: envelopes, wall stand-off, finishes, mesh provenance, one hull/carve, turret lines, negative guards, isolated Undo, saved reload, deterministic assets, " +
            before.Count + " original scene states, " + assets.Count + " protected assets/meta/settings, existing-room validators.\n" + EnvelopeReport(f) + "\n" + summary + "\n" + neighbours + "\n" + BossHallFramework.BudgetReport() + "\n");
        Debug.Log("BOSS HALL BACKDROP BUILD VERIFIED");
    }

    public static void ValidateBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch Editor");
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        var layout = BossHallFramework.MeasureLayout(); Validate(layout); BossHallRenderingReview.ValidateNeighbours();
        if (SceneManager.GetActiveScene().isDirty || sha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Read-only validation changed scene");
        Debug.Log("BOSS HALL BACKDROP RELOAD VERIFIED");
    }

    public static string EnvelopeReport(Frame f)
    {
        string B(Bounds b) => FormattableString.Invariant($"X [{b.min.x:F2}, {b.max.x:F2}], Y [{b.min.y:F2}, {b.max.y:F2}], Z [{b.min.z:F2}, {b.max.z:F2}]");
        float energyR = f.ringInner - .1f;
        var energy = MinMax(new Vector3(f.wallX + .3f, f.ringY - energyR, f.axisZ - energyR), new Vector3(f.stripFront, f.ringY + energyR, f.axisZ + energyR));
        var armsSouth = MinMax(new Vector3(f.wallX + 4.3f, f.upperTop + .3f, f.bedSouth - 3.5f), new Vector3(f.wallX + 8f, f.ceiling - 2.2f, f.axisZ - 3.2f));
        var armsNorth = MinMax(new Vector3(f.wallX + 4.3f, f.upperTop + .3f, f.axisZ + 3.2f), new Vector3(f.wallX + 8f, f.ceiling - 2.2f, f.bedNorth + 3.5f));
        return "Placement envelopes: cradle " + B(f.cradle) + "; wall strip " + B(f.strip) + "; gantry " + B(f.gantry) + " (bay-door approach " + B(f.bayApproach) + " excluded).\n" +
            "Collision hull/carve: " + B(f.hull) + ".\n" +
            "Reserved attachment envelopes (later owners; empty here): contained energy (env_053) ring aperture " + B(energy) +
            "; mechanical arms (art_006) under the gantry runway " + B(armsSouth) + " and " + B(armsNorth) +
            "; power/cooling feeds (env_050) at the lintel top Y " + f.lintelTop.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
            " and the corbel faces, continuing in the measured west upper wall-service zones.";
    }

    static void VerifyGuards(BossHallFramework.Layout layout)
    {
        void Rejected(Action action, string label)
        {
            try { action(); } catch (InvalidOperationException) { Debug.Log("PASS: backdrop guard " + label); return; }
            throw new InvalidOperationException("Accepted invalid backdrop state: " + label);
        }
        var section = BossHallFramework.FindSection(BossHallFramework.Section.AssemblyBackdrop);
        var ring = section.Find("Assembly_Ring"); var cradle = section.Find("Assembly_Cradle");
        var p = ring.position;
        try { ring.position = p + Vector3.right * 6; Physics.SyncTransforms(); Rejected(() => BossHallFramework.Validate(layout), "ring moved into the inter-tier firing volume"); }
        finally { ring.position = p; }
        var c = cradle.position;
        try { cradle.position = c + Vector3.back * 3; Physics.SyncTransforms(); Rejected(() => BossHallFramework.Validate(layout), "cradle moved into a Rifleman envelope"); }
        finally { cradle.position = c; }
        try { cradle.position = c + Vector3.left * .3f; Physics.SyncTransforms(); Rejected(() => Validate(layout), "cradle touching the wall at floor level"); }
        finally { cradle.position = c; }
        var renderer = section.GetComponentsInChildren<MeshRenderer>().First(); var material = renderer.sharedMaterial;
        try { renderer.sharedMaterial = BossHallRendering.GetFinish("Brass"); Rejected(() => Validate(layout), "unplanned finish"); }
        finally { renderer.sharedMaterial = material; }
        var light = new GameObject("__Backdrop_Light_Probe").AddComponent<Light>();
        try { light.transform.SetParent(ring, false); light.transform.position = p; Rejected(() => BossHallFramework.Validate(layout), "light outside Lighting"); }
        finally { Object.DestroyImmediate(light.gameObject); }
        var extra = cradle.Find(HullName).gameObject.AddComponent<SphereCollider>();
        try { Rejected(() => Validate(layout), "second collider"); }
        finally { Object.DestroyImmediate(extra); }
        Rejected(() => BossHallFramework.RebuildSection(layout, BossHallFramework.Section.AssemblyBackdrop, "corebuster_env_050", _ => { }), "wrong owner");
        Physics.SyncTransforms(); Validate(layout);
    }
}
