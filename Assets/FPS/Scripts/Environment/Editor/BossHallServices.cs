using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = BossHallFramework.Section;

/// <summary>
/// corebuster_env_050: power, cooling and overhead feeds that carry Room 3's regulated power and the Reactor's coolant
/// through the east wall to the final assembly backdrop. It owns only <c>Power_Services</c>, <c>Cooling_Services</c>,
/// <c>Overhead</c> and their folders under <c>Assets/FPS/Art/BossHall/</c>. The accepted contract reserves the whole hall
/// from the tier-2 deck to its headroom, and the galleries meet the walls, so each network runs in three measured wall
/// bands (under the galleries, the gallery wall band, the upper wall band) joined through wall chases at matching stations.
/// </summary>
public static class BossHallServices
{
    public const string Owner = "corebuster_env_050";
    public const string Output = "ReviewCaptures/BossHall/050";
    public const string HullName = "Consolidated_Collision_Hull";
    const string Heavy = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/Meshes/";
    const string Turbine = "Assets/FPS/Art/NorthWestRoom/Turbine_Machinery/Meshes/";
    const string Electrical = "Assets/FPS/Art/NorthWestRoom/Electrical_Systems/Meshes/";
    static readonly string[] ReusePrefixes = { Heavy, Turbine, Electrical };
    public static readonly Section[] Owned = { Section.PowerServices, Section.CoolingServices, Section.Overhead };
    static readonly string[] Sides = { "South", "North" };
    // Floor-level pieces keep the backdrop's wall stand-off so the separately scoped under-wall collision repair stays possible.
    const float WallStandoff = .35f, WallContactTop = 1f, UvScale = .35f, HullHeight = 2.55f, Shrink = .03f;
    // Kit wall, floor and ceiling faces deviate up to 3 cm from the measured median planes; a part face lying on one of
    // those planes is a bearing face and may sit up to this far inside the kit surface.
    const float Bearing = .035f;

    public static string[] Roots(Section s) => s == Section.PowerServices ? new[] { "Power_Feed_South", "Power_Feed_North" } :
        s == Section.CoolingServices ? new[] { "Cooling_Loop_South", "Cooling_Loop_North" } : new[] { "Overhead_Feeder_South", "Overhead_Feeder_North" };
    public static string[] Finishes(Section s) => s == Section.PowerServices ? new[] { "Gunmetal", "HazardYellow" } :
        s == Section.CoolingServices ? new[] { "RedOxide", "Brass" } : new[] { "Gunmetal" };
    public static string Folder(Section s) => BossHallFramework.AssetRoot + "/" + BossHallFramework.Spec(s).name;

    /// <summary>Placement frame derived from the contract and the env_049 frame; nothing here is a scene object.</summary>
    public sealed class Frame
    {
        public BossHallBackdrop.Frame backdrop;
        public Bounds room;
        public float floor, ceiling, band, floorTop, firingBottom, galleryTop, upperBottom, upperTop, overheadTop;
        public float mainY, mainR, runY, runR, trayY, powerX, coolingX, lowerWallTop;
        public float beamY0, runwayX, runwayY0, runwayY1, runwayS, runwayN;
        public float Z(int side, float d) => side == 0 ? room.min.z + d : room.max.z - d;
        public float D(int side, float z) => side == 0 ? z - room.min.z : room.max.z - z;
        public Vector3 P(int side, float x, float y, float d) => new Vector3(x, y, Z(side, d));
        public Vector3 West(float d, float y, float z) => new Vector3(room.min.x + d, y, z);
        public float CorbelFace(int side) => side == 0 ? backdrop.bedSouth + 2 - .45f : backdrop.bedNorth - 2 + .45f;
        public float LintelEnd(int side) => side == 0 ? backdrop.bedSouth : backdrop.bedNorth;
        public float RunwayEnd(int side) => side == 0 ? runwayS : runwayN;
        public float FeederZ(int side) => RunwayEnd(side) + (side == 0 ? -.3f : .3f);
        /// <summary>Wall-hugging strip of depth <paramref name="depth"/> along the south or north wall.</summary>
        public Bounds Strip(int side, float depth, float y0, float y1) =>
            MinMax(new Vector3(room.min.x, y0, Mathf.Min(Z(side, 0), Z(side, depth))), new Vector3(room.max.x, y1, Mathf.Max(Z(side, 0), Z(side, depth))));
        public Bounds WestStrip(float y0, float y1) => MinMax(new Vector3(room.min.x, y0, room.min.z), new Vector3(room.min.x + band, y1, room.max.z));
        public Bounds EastStrip(float y0, float y1) => MinMax(new Vector3(room.max.x - band, y0, room.min.z), new Vector3(room.max.x, y1, room.max.z));

        /// <summary>The only volumes each section may occupy: perimeter wall bands and two overhead feeder corridors.</summary>
        public Bounds[] Envelopes(Section section)
        {
            if (section == Section.Overhead)
                return Enumerable.Range(0, 2).Select(s => MinMax(new Vector3(room.min.x, upperTop, FeederZ(s) - .35f), new Vector3(room.max.x, ceiling, FeederZ(s) + .35f))).ToArray();
            var list = new List<Bounds>();
            for (int s = 0; s < 2; s++)
            {
                list.Add(Strip(s, band, floor, floorTop));                 // under the gallery, against the wall
                list.Add(Strip(s, 2.2f, floor, firingBottom - .01f));      // under-gallery floor pocket below the firing volume
                list.Add(Strip(s, band, galleryTop, upperBottom));         // gallery wall band, above walking headroom
                list.Add(Strip(s, band, upperTop, overheadTop));           // upper wall band
            }
            list.Add(WestStrip(galleryTop, upperBottom)); list.Add(WestStrip(upperTop, overheadTop)); list.Add(EastStrip(upperTop, overheadTop));
            return list.ToArray();
        }
    }

    public static Frame Measure(BossHallFramework.Layout layout)
    {
        var r = layout.Report; var b = BossHallBackdrop.Measure(layout);
        BossHallSurvey.Zone Zone(string name) => r.zones.Single(z => z.name == name);
        var firing = Zone("Inter-tier firing volume"); var upper = Zone("Upper deck network walking volume"); var overhead = Zone("Overhead");
        var galleries = new[] { Zone("North gallery walking volume"), Zone("South gallery walking volume") };
        var f = new Frame { backdrop = b, room = layout.interior, floor = r.floorTopY, ceiling = r.ceilingUndersideY };
        // The trimmed contract keeps deck footprints but zeroes the underside scalars (BUGS.md, 2026-10-04).
        float galleryUnder = r.deckFootprints.Where(d => d.name.Contains(" gallery:")).Min(d => d.min.y);
        // The wall band is the measured gap between each wall and the inter-tier firing volume (1.2 m), less a small margin.
        f.band = Mathf.Min(Mathf.Min(firing.min.z - f.room.min.z, f.room.max.z - firing.max.z), Mathf.Min(firing.min.x - f.room.min.x, f.room.max.x - firing.max.x)) - .01f;
        f.floorTop = galleryUnder - .1f; f.firingBottom = firing.min.y; f.galleryTop = galleries.Max(g => g.max.y);
        f.upperBottom = upper.min.y; f.upperTop = upper.max.y; f.overheadTop = overhead.max.y;
        f.mainR = .24f; f.mainY = galleryUnder - .8f;          // reused heavy-pipe bore, high under the gallery deck
        f.runR = .2f; f.runY = b.lintelTop + .27f; f.lowerWallTop = b.deckUnder;                    // gallery-band line seated level with the lintel top
        // Recomputed from the env_049 recipe and checked against the saved backdrop renderers in Validate.
        f.beamY0 = b.ceiling - 2f; f.runwayX = b.wallX + 6.6f; f.runwayY0 = f.beamY0 + .7f; f.runwayY1 = f.runwayY0 + .82f;
        f.runwayS = b.bedSouth - 3.6f; f.runwayN = b.bedNorth + 3.6f; f.trayY = f.beamY0 - 1.6f;
        // Chase stations sit inside the measured gallery wall-service zones, clear of both upper stairs.
        var east = Zone("North gallery wall services 3"); var west = Zone("North gallery wall services 1");
        f.powerX = (east.min.x + east.max.x) * .5f; f.coolingX = west.min.x + 1.7f;
        if (f.band < 1.1f || f.mainY + .6f > f.floorTop || galleryUnder < r.tier1TopY - .6f || f.runY + .26f >= f.upperBottom || f.galleryTop + .4f > f.runY - 2f ||
            f.trayY - .8f < f.upperTop || f.floor + HullHeight >= f.firingBottom || east.max.x - east.min.x < 6 || west.max.x - west.min.x < 6)
            throw new InvalidOperationException("Measured service bands are too small for the approved networks.");
        return f;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Composition. Primitives are authored in world space; each composed mesh is pivoted on its own anchor.

    sealed class Composer
    {
        public readonly string name; public readonly Section section; public readonly string root; public readonly Vector3 pivot;
        readonly List<(string finish, ProceduralMeshBuilder builder, Matrix4x4 matrix)> parts = new List<(string, ProceduralMeshBuilder, Matrix4x4)>();
        /// <summary>World bounds of every primitive, used for exact intersection checks instead of the whole mesh's bounds.</summary>
        public readonly List<Bounds> boxes = new List<Bounds>();
        public Composer(Section section, string root, string name, Vector3 pivot) { this.section = section; this.root = root; this.name = name; this.pivot = pivot; }
        void Add(string finish, ProceduralMeshBuilder b, Matrix4x4 m)
        {
            if (!Finishes(section).Contains(finish)) throw new ArgumentException("Unplanned " + section + " finish " + finish);
            parts.Add((finish, b, m));
        }
        ProceduralMeshBuilder New() => new ProceduralMeshBuilder();
        public void Box(string finish, Vector3 a, Vector3 c)
        { var b = New(); b.AddBlock(Vector3.zero, Abs(c - a)); Add(finish, b, Matrix4x4.Translate((a + c) * .5f - pivot)); boxes.Add(MinMax(a, c)); }
        public void Cylinder(string finish, Vector3 a, Vector3 c, float radius, int segments = 16)
        { var b = New(); b.AddCylinder(a, c, radius, radius, segments, true); Add(finish, b, Matrix4x4.Translate(-pivot)); boxes.Add(SegmentBox(a, c, radius)); }
        public void Tube(string finish, IList<Vector3> path, float radius, int segments = 14)
        {
            var b = New(); b.AddTube(path, radius, segments, true); Add(finish, b, Matrix4x4.Translate(-pivot));
            for (int i = 1; i < path.Count; i++) boxes.Add(SegmentBox(path[i - 1], path[i], radius));
        }
        public (Mesh mesh, string[] finishes) Build()
        {
            var temporary = new List<Mesh>(); var combined = new List<CombineInstance>(); var used = new List<string>();
            foreach (var g in parts.GroupBy(p => p.finish).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var instances = g.Select(p =>
                {
                    var mesh = p.builder.ToMesh("temp"); var uv = mesh.uv; for (int i = 0; i < uv.Length; i++) uv[i] *= UvScale; mesh.uv = uv;
                    temporary.Add(mesh); return new CombineInstance { mesh = mesh, transform = p.matrix };
                }).ToArray();
                var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.CombineMeshes(instances, true, true); temporary.Add(m);
                combined.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity }); used.Add(g.Key);
            }
            var result = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            result.CombineMeshes(combined.ToArray(), false, false); result.RecalculateBounds();
            foreach (var m in temporary) Object.DestroyImmediate(m);
            return (result, used.ToArray());
        }
    }

    sealed class Piece
    {
        public Section section; public string root, path, name;
        public Mesh mesh; public string[] finishes;
        public Vector3 position, scale = Vector3.one; public Quaternion rotation = Quaternion.identity;
        public bool reused;
    }
    /// <summary>A support ray: from just in front of a bearing face into the existing wall, ceiling or floor collider.</summary>
    public sealed class Anchor { public string label; public Vector3 origin, direction; public float reach; }
    /// <summary>A planned face contact with a saved env_049 backdrop renderer.</summary>
    public sealed class Contact { public string label, target; public Bounds box; public int axis; public bool positive; }
    public sealed class Hull { public Section section; public string root; public Bounds box; }

    sealed class Plan
    {
        public readonly List<Piece> pieces = new List<Piece>();
        public readonly List<Composer> composers = new List<Composer>();
        public readonly List<Anchor> anchors = new List<Anchor>();
        public readonly List<Contact> contacts = new List<Contact>();
        public readonly List<Hull> hulls = new List<Hull>();
        public Composer Compose(Section section, string root, string name, Vector3 pivot)
        { var c = new Composer(section, root, "SM_" + name, pivot); composers.Add(c); return c; }
        public void Support(string label, Vector3 origin, Vector3 direction, float reach = .2f) =>
            anchors.Add(new Anchor { label = label, origin = origin, direction = direction.normalized, reach = reach });
    }

    static Mesh Source(string path)
    {
        var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().OrderBy(m => m.name, StringComparer.Ordinal).FirstOrDefault();
        return mesh ? mesh : throw new InvalidOperationException("Missing reused mesh " + path);
    }

    // As in env_049: maps each local mesh axis to the world axis it points along after rotation and scales it to that world size.
    static Piece Reuse(Plan plan, Section section, string root, string name, string path, string finish, Vector3 centre, Quaternion rotation, Vector3 worldSize)
    {
        var mesh = Source(path); var s = mesh.bounds.size; var scale = Vector3.one;
        for (int i = 0; i < 3; i++)
        {
            var axis = rotation * (i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
            int j = Mathf.Abs(axis.x) > .5f ? 0 : Mathf.Abs(axis.y) > .5f ? 1 : 2;
            if (Mathf.Abs(axis[j]) < .99f) throw new InvalidOperationException("Reused pieces must be axis-aligned: " + name);
            scale[i] = worldSize[j] / Mathf.Max(.0001f, s[i]);
        }
        var piece = new Piece { section = section, root = root, name = name, path = path, mesh = mesh, finishes = Enumerable.Repeat(finish, Mathf.Max(1, mesh.subMeshCount)).ToArray(),
            rotation = rotation, scale = scale, position = centre - rotation * Vector3.Scale(mesh.bounds.center, scale), reused = true };
        plan.pieces.Add(piece); return piece;
    }
    static void Place(Plan plan, Section section, string root, string name, string path, string finish, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var mesh = Source(path);
        plan.pieces.Add(new Piece { section = section, root = root, name = name, path = path, mesh = mesh, position = position, rotation = rotation, scale = scale, reused = true,
            finishes = Enumerable.Repeat(finish, Mathf.Max(1, mesh.subMeshCount)).ToArray() });
    }

    /// <summary>Rounds every interior corner of a polyline (quadratic fillet) so tubes bend like formed pipe.</summary>
    static List<Vector3> Fillet(IList<Vector3> points, float radius, int steps = 6, float limit = .45f)
    {
        var path = new List<Vector3> { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector3 a = points[i - 1], b = points[i], c = points[i + 1];
            float r = Mathf.Min(radius, Vector3.Distance(a, b) * limit, Vector3.Distance(b, c) * limit);
            Vector3 p1 = b + (a - b).normalized * r, p2 = b + (c - b).normalized * r;
            for (int k = 0; k <= steps; k++) { float t = k / (float)steps; path.Add((1 - t) * (1 - t) * p1 + 2 * (1 - t) * t * b + t * t * p2); }
        }
        path.Add(points[points.Count - 1]);
        return path;
    }

    static Plan Compose(Frame f)
    {
        var plan = new Plan();
        for (int s = 0; s < 2; s++) { Power(plan, f, s); Cooling(plan, f, s); Feeder(plan, f, s); }
        return plan;
    }

    // Power: under-gallery switchgear and booster generator -> wall chase -> gallery junction -> wall chase -> upper cable
    // tray from the east-wall Room 3 intake -> west wall tray -> junction box bolted to the backdrop gantry corbel face.
    static void Power(Plan plan, Frame f, int s)
    {
        const Section sec = Section.PowerServices;
        string tag = Sides[s], root = Roots(sec)[s];
        var room = f.room; float y0 = f.floor, sx = f.powerX, gx = sx - 5.3f, wallNormal = s == 0 ? -1 : 1;
        var face = Quaternion.LookRotation(new Vector3(0, 0, -wallNormal)); // reused fronts face the hall
        Vector3 toWall = new Vector3(0, 0, wallNormal);

        // Switchgear: Room 3 plinth, three control cabinets with doors and a transformer tank.
        float plinthTop = y0 + .22f;
        Reuse(plan, sec, root, "Switchgear_Plinth_" + tag, Electrical + "SM_Electrical_Plinth.asset", "HazardYellow", f.P(s, sx, y0 + .11f, 1.2f), face, new Vector3(3.3f, .22f, 1.6f));
        for (int i = 0; i < 3; i++)
        {
            float cx = sx + (i - 1) * 1.05f;
            Reuse(plan, sec, root, $"Switchgear_Cabinet_{tag}_{i}", Electrical + "SM_Control_Cabinet.asset", "Gunmetal", f.P(s, cx, plinthTop + .89f, .8f), face, new Vector3(.8f, 1.78f, .6f));
            Reuse(plan, sec, root, $"Switchgear_Door_{tag}_{i}", Electrical + "SM_Cabinet_Door.asset", "Gunmetal", f.P(s, cx, plinthTop + .95f, 1.1175f), face, new Vector3(.68f, 1.53f, .035f));
        }
        float tx = sx + 2.55f;
        Reuse(plan, sec, root, "Transformer_" + tag, Electrical + "SM_Transformer_Tank.asset", "Gunmetal", f.P(s, tx, y0 + .1f + .825f, .95f), face, new Vector3(1.16f, 1.65f, 1.05f));
        // Booster generator: Room 3 turbine drum, end bells and bearing pedestals at a supporting scale.
        float gy = plinthTop + .7f;
        Reuse(plan, sec, root, "Generator_Drum_" + tag, Turbine + "SM_Turbine_Drum.asset", "Gunmetal", f.P(s, gx, gy, 1.1f), Quaternion.identity, new Vector3(3.6f, 1.2f, 1.2f));
        foreach (int e in new[] { -1, 1 })
        {
            string end = e < 0 ? "West" : "East";
            Reuse(plan, sec, root, $"Generator_Bell_{tag}_{end}", Turbine + "SM_End_Bell.asset", "HazardYellow", f.P(s, gx + e * 1.985f, gy, 1.1f), Quaternion.identity, new Vector3(.37f, 1.4f, 1.4f));
            Reuse(plan, sec, root, $"Generator_Bearing_{tag}_{end}", Turbine + "SM_Bearing_Block.asset", "Gunmetal", f.P(s, gx + e * 2.6f, plinthTop + .425f, 1.1f), Quaternion.identity, new Vector3(.35f, .85f, .97f));
        }

        var floor = plan.Compose(sec, root, "Power_Floor_" + tag, f.P(s, (gx + sx) * .5f, y0, 1f));
        foreach (float d in new[] { .55f, 1.45f }) floor.Box("Gunmetal", f.P(s, gx - 3f, y0, d), f.P(s, gx + 3f, plinthTop, d + .2f));
        foreach (float x in new[] { gx - 2.6f, gx - 1f, gx + 1f, gx + 2.6f }) floor.Box("Gunmetal", f.P(s, x - .1f, y0, .55f), f.P(s, x + .1f, plinthTop, 1.65f));
        floor.Cylinder("Gunmetal", f.P(s, gx - 2.85f, gy, 1.1f), f.P(s, gx + 2.85f, gy, 1.1f), .13f);
        floor.Box("Gunmetal", f.P(s, gx - .4f, gy + .55f, .85f), f.P(s, gx + .4f, gy + .85f, 1.35f));
        floor.Box("HazardYellow", f.P(s, gx - .42f, gy + .74f, .83f), f.P(s, gx + .42f, gy + .8f, 1.37f));
        floor.Box("Gunmetal", f.P(s, tx - .68f, y0, .38f), f.P(s, tx + .68f, y0 + .1f, 1.52f));
        float tankTop = y0 + .1f + 1.65f, conduitY = y0 + 2.35f;
        for (int i = -1; i <= 1; i++) floor.Cylinder("HazardYellow", f.P(s, tx + i * .35f, tankTop, .95f), f.P(s, tx + i * .35f, tankTop + .28f, .95f), .07f, 10);
        // Conduits rise from the cabinet tops, generator terminal box and transformer bushings into wall chase plates.
        var feeds = new List<(float x, float y, float d)>();
        for (int i = 0; i < 3; i++) feeds.Add((sx + (i - 1) * 1.05f, plinthTop + 1.78f, .8f));
        feeds.Add((gx, gy + .85f, 1.1f)); feeds.Add((tx, tankTop + .28f, .95f));
        var chase = plan.Compose(sec, root, "Power_Floor_Chase_" + tag, f.P(s, (gx + sx) * .5f, conduitY, .4f));
        foreach (var (x, y, d) in feeds)
            chase.Tube("Gunmetal", Fillet(new[] { f.P(s, x, y - .02f, d), f.P(s, x, conduitY, d), f.P(s, x, conduitY, 0) }, .15f), .05f, 10);
        foreach (var (x, w) in new[] { (sx, 1.4f), (gx, .3f), (tx, .3f) })
        {
            chase.Box("Gunmetal", f.P(s, x - w, conduitY - .2f, 0), f.P(s, x + w, conduitY + .2f, .05f));
            plan.Support($"{root} floor chase plate {x:F1}", f.P(s, x, conduitY, .1f), toWall);
        }
        foreach (float x in new[] { gx - 2.9f, gx + 2.9f, sx - 1.5f, sx + 1.5f, tx })
            plan.Support($"{root} floor bearing {x:F1}", f.P(s, x, y0 + .05f, 1.4f), Vector3.down, .15f);
        plan.hulls.Add(new Hull { section = sec, root = root, box = MinMax(f.P(s, gx - 3.1f, y0, WallStandoff), f.P(s, tx + .78f, y0 + HullHeight, 2.1f)) });

        // Gallery band: chase plates above the walking headroom with a reused wall-hung junction cabinet between them.
        var gallery = plan.Compose(sec, root, "Power_Gallery_" + tag, f.P(s, sx, f.galleryTop, 0));
        float cabBottom = f.galleryTop + .55f, cabTop = cabBottom + 1.78f, low = f.galleryTop + .32f, high = cabTop + .35f;
        Reuse(plan, sec, root, "Junction_Cabinet_" + tag, Electrical + "SM_Control_Cabinet.asset", "Gunmetal", f.P(s, sx, cabBottom + .89f, .45f), face, new Vector3(.8f, 1.78f, .6f));
        Reuse(plan, sec, root, "Junction_Door_" + tag, Electrical + "SM_Cabinet_Door.asset", "Gunmetal", f.P(s, sx, cabBottom + .95f, .7675f), face, new Vector3(.68f, 1.53f, .035f));
        foreach (float y in new[] { low, high })
        {
            gallery.Box("Gunmetal", f.P(s, sx - .45f, y - .17f, 0), f.P(s, sx + .45f, y + .17f, .05f));
            plan.Support($"{root} gallery chase plate {y:F2}", f.P(s, sx, y, .1f), toWall);
        }
        foreach (float y in new[] { cabBottom + .35f, cabTop - .35f })
        {
            gallery.Box("Gunmetal", f.P(s, sx - .45f, y - .05f, 0), f.P(s, sx + .45f, y + .05f, .16f));
            plan.Support($"{root} cabinet strap {y:F2}", f.P(s, sx, y, .1f), toWall);
        }
        gallery.Box("HazardYellow", f.P(s, sx - .41f, cabTop + .02f, .15f), f.P(s, sx + .41f, cabTop + .08f, .75f));
        for (int i = -1; i <= 1; i++)
        {
            float x = sx + i * .25f;
            gallery.Tube("Gunmetal", Fillet(new[] { f.P(s, x, low, .04f), f.P(s, x, low, .45f), f.P(s, x, cabBottom + .02f, .45f) }, .12f), .045f, 10);
            gallery.Tube("Gunmetal", Fillet(new[] { f.P(s, x, cabTop + .07f, .45f), f.P(s, x, high, .45f), f.P(s, x, high, .04f) }, .12f), .045f, 10);
        }

        // Upper band: chase plate, three conduits into the cable tray, tray from the east intake to the north-west corner.
        float tray = f.trayY, eastFace = EastFace(f, tray + .3f, f.Z(s, 1.6f)), x0 = room.min.x + 1.08f, x1 = eastFace - .7f;
        var trunk = plan.Compose(sec, root, "Power_Trunk_" + tag, f.P(s, (x0 + x1) * .5f, tray, .75f));
        float plate = f.upperTop + .45f;
        trunk.Box("Gunmetal", f.P(s, sx - .4f, plate - .2f, 0), f.P(s, sx + .4f, plate + .2f, .05f));
        plan.Support(root + " upper chase plate", f.P(s, sx, plate, .1f), toWall);
        for (int i = -1; i <= 1; i++)
        {
            float x = sx + i * .25f;
            trunk.Tube("Gunmetal", Fillet(new[] { f.P(s, x, plate, .04f), f.P(s, x, plate, .75f), f.P(s, x, tray + .01f, .75f) }, .15f), .045f, 10);
        }
        Tray(trunk, f, s, true, x0, x1, tray, plan, root);
        // Room 3 intake: a busway head on the east upper wall with conduits through the wall toward Room 3 and the Reactor.
        var intake = plan.Compose(sec, root, "Power_Intake_" + tag, new Vector3(eastFace - .35f, tray, f.Z(s, 1.6f)));
        intake.Box("Gunmetal", new Vector3(x1, tray - .8f, f.Z(s, .3f)), new Vector3(eastFace, tray + 1.4f, f.Z(s, 3f)));
        intake.Box("HazardYellow", new Vector3(x1 - .03f, tray + 1.08f, f.Z(s, .35f)), new Vector3(x1, tray + 1.2f, f.Z(s, 2.95f)));
        for (float d = .6f; d < 2.8f; d += .55f) intake.Box("Gunmetal", new Vector3(x1 - .06f, tray - .7f, f.Z(s, d - .04f)), new Vector3(x1, tray + 1.3f, f.Z(s, d + .04f)));
        for (int i = 0; i < 3; i++)
        {
            float z = f.Z(s, 1f + i * .6f);
            intake.Tube("Gunmetal", Fillet(new[] { new Vector3(x1 + .35f, tray + 1.38f, z), new Vector3(x1 + .35f, tray + 1.95f, z), new Vector3(eastFace - .04f, tray + 1.95f, z) }, .15f), .06f, 10);
        }
        intake.Box("Gunmetal", new Vector3(eastFace - .05f, tray + 1.7f, f.Z(s, .7f)), new Vector3(eastFace, tray + 2.2f, f.Z(s, 2.5f)));
        plan.Support(root + " intake wall face", new Vector3(room.max.x - .1f, tray + .3f, f.Z(s, 1.6f)), Vector3.right);
        plan.Support(root + " intake chase plate", new Vector3(room.max.x - .1f, tray + 1.95f, f.Z(s, 1.6f)), Vector3.right);

        // West wall tray to a junction box bolted to the backdrop gantry corbel's outer face.
        float zFace = f.CorbelFace(s), zBox = zFace + (s == 0 ? -.6f : .6f), zStart = f.Z(s, .42f);
        var west = plan.Compose(sec, root, "Power_West_" + tag, f.West(.75f, tray, (zStart + zBox) * .5f));
        Tray(west, f, s, false, zStart, zBox, tray, plan, root);
        var box = MinMax(f.West(.4f, tray - .1f, Mathf.Min(zFace, zBox)), f.West(1.1f, tray + .6f, Mathf.Max(zFace, zBox)));
        west.Box("Gunmetal", box.min, box.max);
        west.Box("HazardYellow", f.West(1.1f, tray + .38f, Mathf.Min(zFace, zBox) + .05f), f.West(1.13f, tray + .48f, Mathf.Max(zFace, zBox) - .05f));
        plan.contacts.Add(new Contact { label = root + " junction box on the gantry corbel", target = "Assembly_Backdrop/Assembly_Gantry/Gantry_Support_" + tag,
            box = box, axis = 2, positive = s == 1 });
    }

    /// <summary>Cable tray (bottom, side rails, four cables, hazard edge) with wall brackets and knee braces every 3 m.</summary>
    static void Tray(Composer c, Frame f, int s, bool alongX, float from, float to, float y, Plan plan, string root)
    {
        float a = Mathf.Min(from, to), b = Mathf.Max(from, to);
        // Position on the run (t) and depth from its wall (d) to world.
        Vector3 W(float t, float yy, float d) => alongX ? f.P(s, t, yy, d) : f.West(d, yy, t);
        Vector3 toWall = alongX ? new Vector3(0, 0, s == 0 ? -1 : 1) : Vector3.left;
        c.Box("Gunmetal", W(a, y, .42f), W(b, y + .03f, 1.08f));
        c.Box("Gunmetal", W(a, y, .42f), W(b, y + .3f, .45f));
        c.Box("Gunmetal", W(a, y, 1.05f), W(b, y + .3f, 1.08f));
        c.Box("HazardYellow", W(a, y + .27f, 1.08f), W(b, y + .3f, 1.1f));
        foreach (float d in new[] { .55f, .68f, .81f, .94f }) c.Cylinder("Gunmetal", W(a, y + .09f, d), W(b, y + .09f, d), .055f, 10);
        for (float t = a + 1f; t < b - .5f; t += 3f)
        {
            c.Box("Gunmetal", W(t - .06f, y - .12f, 0), W(t + .06f, y, 1.12f));
            c.Box("Gunmetal", W(t - .12f, y - .75f, 0), W(t + .12f, y, .04f));
            c.Cylinder("Gunmetal", W(t, y - .68f, .04f), W(t, y - .1f, .9f), .035f, 8);
            plan.Support($"{root} tray bracket {t:F1}", W(t, y - .4f, .1f), toWall);
        }
    }

    // Cooling: Reactor coolant main from the east wall along the wall under the gallery, P-3 accumulator bank and valve,
    // wall chase, gallery riser, run along the side wall and down the west wall to a header seated on the backdrop lintel.
    static void Cooling(Plan plan, Frame f, int s)
    {
        const Section sec = Section.CoolingServices;
        string tag = Sides[s], root = Roots(sec)[s];
        var room = f.room; float y0 = f.floor, cx = f.coolingX, my = f.mainY;
        Vector3 toWall = new Vector3(0, 0, s == 0 ? -1 : 1);
        var tanks = new[] { cx + 3.6f, cx + 6f, cx + 8.4f };
        const float tankScale = .75f, tankD = .95f, skidTop = .14f;
        var body = Source(Heavy + "SM_PressureTank_P3_Body.asset");
        for (int i = 0; i < tanks.Length; i++)
        {
            var origin = f.P(s, tanks[i], y0 + skidTop - body.bounds.min.y * tankScale, tankD);
            Place(plan, sec, root, $"Coolant_Tank_{tag}_{i}", Heavy + "SM_PressureTank_P3_Body.asset", "RedOxide", origin, Quaternion.identity, Vector3.one * tankScale);
            Place(plan, sec, root, $"Coolant_Tank_Bands_{tag}_{i}", Heavy + "SM_PressureTank_P3_Bands.asset", "Brass", origin, Quaternion.identity, Vector3.one * tankScale);
        }
        float tankTop = y0 + skidTop + body.bounds.size.y * tankScale;
        // Reused heavy-kit valve inline on the main, its stem upward; local +Z runs west along the pipe.
        float valveE = cx + 1.9f, valveW = valveE - .7f;
        Place(plan, sec, root, "Main_Valve_" + tag, Heavy + "SM_HeavyPipe_ValveMount.asset", "Brass", f.P(s, valveE, my, .45f), Quaternion.LookRotation(Vector3.left, Vector3.back), Vector3.one);

        var skid = plan.Compose(sec, root, "Cooling_Skid_" + tag, f.P(s, tanks[1], y0, tankD));
        skid.Box("RedOxide", f.P(s, tanks[0] - .7f, y0, .4f), f.P(s, tanks[2] + .7f, y0 + skidTop, 1.5f));
        foreach (float x in tanks) skid.Cylinder("Brass", f.P(s, x, y0 + skidTop, tankD), f.P(s, x, y0 + skidTop + .08f, tankD), .52f, 24);
        foreach (float x in new[] { tanks[0] - .6f, tanks[2] + .6f }) foreach (float d in new[] { .5f, 1.4f })
            plan.Support($"{root} skid foot {x:F1}/{d:F1}", f.P(s, x, y0 + .05f, d), Vector3.down, .15f);
        plan.hulls.Add(new Hull { section = sec, root = root, box = MinMax(f.P(s, tanks[0] - .8f, y0, WallStandoff), f.P(s, tanks[2] + .8f, y0 + HullHeight, 1.6f)) });

        var main = plan.Compose(sec, root, "Cooling_Main_" + tag, f.P(s, (room.max.x + cx) * .5f, my, .45f));
        float r = f.mainR, start = room.max.x;
        main.Cylinder("RedOxide", f.P(s, start, my, .45f), f.P(s, valveE, my, .45f), r, 20);
        main.Cylinder("RedOxide", f.P(s, valveW, my, .45f), f.P(s, cx + .6f, my, .45f), r, 20);
        main.Tube("RedOxide", Fillet(new[] { f.P(s, cx + .62f, my, .45f), f.P(s, cx, my, .45f), f.P(s, cx, my, 0) }, .42f, 8, .95f), r, 20);
        main.Cylinder("Brass", f.P(s, start - .1f, my, .45f), f.P(s, start, my, .45f), .38f, 20);
        main.Cylinder("Brass", f.P(s, cx, my, 0), f.P(s, cx, my, .1f), .36f, 20);
        main.Cylinder("Brass", f.P(s, valveE, my, .45f), f.P(s, valveE + .08f, my, .45f), .33f, 20);
        main.Cylinder("Brass", f.P(s, valveW - .08f, my, .45f), f.P(s, valveW, my, .45f), .33f, 20);
        for (float x = start - 3f; x > valveE + 1f; x -= 6f) main.Cylinder("Brass", f.P(s, x - .05f, my, .45f), f.P(s, x + .05f, my, .45f), .33f, 20);
        plan.Support(root + " east wall collar", f.P(s, start - .15f, my, .45f), Vector3.right, .25f);
        plan.Support(root + " chase collar", f.P(s, cx, my, .15f), toWall, .25f);
        for (float x = start - 1.5f; x > cx + 1f; x -= 3f)
        {
            if (x > valveW - .5f && x < valveE + .5f) continue;
            main.Cylinder("RedOxide", f.P(s, x - .05f, my, .45f), f.P(s, x + .05f, my, .45f), r + .03f, 20);
            main.Box("RedOxide", f.P(s, x - .05f, my - .06f, 0), f.P(s, x + .05f, my + .06f, .2f));
            main.Box("RedOxide", f.P(s, x - .12f, my - .25f, 0), f.P(s, x + .12f, my + .25f, .04f));
            plan.Support($"{root} main bracket {x:F1}", f.P(s, x, my, .1f), toWall);
        }
        // Each accumulator connects to the main through a formed nozzle with a brass isolation collar.
        foreach (float x in tanks)
        {
            main.Tube("RedOxide", Fillet(new[] { f.P(s, x, tankTop - .04f, tankD), f.P(s, x, my - .45f, tankD), f.P(s, x, my - .45f, .45f), f.P(s, x, my - r + .04f, .45f) }, .15f), .08f, 12);
            main.Cylinder("Brass", f.P(s, x, tankTop + .08f, tankD), f.P(s, x, tankTop + .18f, tankD), .12f, 12);
        }

        // Gallery band: the chased line re-emerges, rises, runs to the corner and down the west wall to the lintel header.
        float ry = f.galleryTop + .45f, uy = f.runY, rr = f.runR;
        // The kit walls carry no collider between the lower wall top and the upper wall collider, so brackets bear below it.
        float bearing = f.lowerWallTop - .2f;
        float headerFar = f.LintelEnd(s) + (s == 0 ? -.15f : .15f), headerNear = f.LintelEnd(s) + (s == 0 ? 1.4f : -1.4f);
        var path = Fillet(new[] { f.P(s, cx, ry, 0), f.P(s, cx, ry, .45f), f.P(s, cx, uy, .45f), new Vector3(room.min.x + .45f, uy, f.Z(s, .45f)),
            new Vector3(room.min.x + .45f, uy, headerFar) }, .45f, 8, .95f);
        int split = path.FindIndex(p => p.x < room.min.x + 1f && f.D(s, p.z) >= .8f);
        var riser = plan.Compose(sec, root, "Cooling_Riser_" + tag, f.P(s, cx, ry, .45f));
        var west = plan.Compose(sec, root, "Cooling_West_" + tag, f.West(.45f, uy, (f.Z(s, .45f) + headerFar) * .5f));
        riser.Tube("RedOxide", path.Take(split + 1).ToList(), rr, 16);
        west.Tube("RedOxide", path.Skip(split).ToList(), rr, 16);
        riser.Cylinder("Brass", f.P(s, cx, ry, 0), f.P(s, cx, ry, .1f), .3f, 16);
        plan.Support(root + " gallery chase collar", f.P(s, cx, ry, .15f), toWall, .25f);
        riser.Cylinder("Brass", f.P(s, cx, (ry + uy) * .5f - .05f, .45f), f.P(s, cx, (ry + uy) * .5f + .05f, .45f), .26f, 16);
        foreach (float y in new[] { ry + .75f, uy - .7f })
        {
            riser.Cylinder("RedOxide", f.P(s, cx, y - .05f, .45f), f.P(s, cx, y + .05f, .45f), rr + .03f, 16);
            riser.Box("RedOxide", f.P(s, cx - .05f, y - .05f, 0), f.P(s, cx + .05f, y + .05f, .25f));
            riser.Box("RedOxide", f.P(s, cx - .12f, y - .2f, 0), f.P(s, cx + .12f, y + .2f, .04f));
            plan.Support($"{root} riser bracket {y:F2}", f.P(s, cx, y, .1f), toWall);
        }
        for (float x = cx - 1.5f; x > room.min.x + 1.6f; x -= 2.5f)
        {
            riser.Cylinder(x > cx - 2f ? "Brass" : "RedOxide", f.P(s, x - .05f, uy, .45f), f.P(s, x + .05f, uy, .45f), x > cx - 2f ? .26f : rr + .03f, 16);
            riser.Box("RedOxide", f.P(s, x - .05f, uy - .05f, 0), f.P(s, x + .05f, uy + .05f, .25f));
            riser.Box("RedOxide", f.P(s, x - .12f, bearing - .25f, 0), f.P(s, x + .12f, uy + .05f, .04f));
            riser.Cylinder("RedOxide", f.P(s, x, bearing - .2f, .04f), f.P(s, x, uy - .17f, .4f), .035f, 8);
            plan.Support($"{root} side-wall bracket {x:F1}", f.P(s, x, bearing, .1f), toWall);
        }
        float dir = s == 0 ? 1 : -1;
        for (float z = f.Z(s, 2.5f); dir * (headerFar - z) > 1.5f; z += dir * 3f)
        {
            bool flange = Mathf.Abs(z - f.Z(s, 2.5f)) < .1f;
            west.Cylinder(flange ? "Brass" : "RedOxide", f.West(.45f, uy, z - .05f), f.West(.45f, uy, z + .05f), flange ? .26f : rr + .03f, 16);
            west.Box("RedOxide", f.West(0, uy - .05f, z - .05f), f.West(.25f, uy + .05f, z + .05f));
            west.Box("RedOxide", f.West(0, bearing - .25f, z - .12f), f.West(.04f, uy + .05f, z + .12f));
            west.Cylinder("RedOxide", f.West(.04f, bearing - .2f, z), f.West(.4f, uy - .17f, z), .035f, 8);
            plan.Support($"{root} west-wall bracket {z:F1}", f.West(.1f, bearing, z), Vector3.left);
        }
        var lintelTop = f.backdrop.lintelTop;
        var header = MinMax(f.West(.38f, lintelTop, Mathf.Min(headerFar, headerNear)), f.West(1.12f, lintelTop + .5f, Mathf.Max(headerFar, headerNear)));
        var seat = plan.Compose(sec, root, "Cooling_Header_" + tag, header.center);
        seat.Box("RedOxide", header.min + new Vector3(0, .06f, 0), header.max);
        seat.Box("Brass", f.West(.38f, lintelTop, header.min.z), f.West(1.12f, lintelTop + .06f, header.max.z));
        seat.Cylinder("Brass", f.West(.45f, uy, headerFar - dir * .08f), f.West(.45f, uy, headerFar), .26f, 16);
        foreach (float d in new[] { .6f, .9f }) seat.Cylinder("Brass", f.West(d, lintelTop + .5f, header.center.z), f.West(d, lintelTop + .58f, header.center.z), .09f, 10);
        plan.contacts.Add(new Contact { label = root + " header seated on the lintel", target = "Assembly_Backdrop/Service_Towers/Lintel_Girder",
            box = header, axis = 1, positive = true });
    }

    // Overhead: a three-cable feeder from an east-wall Room 3 intake to the gantry runway end, hung from the ceiling.
    static void Feeder(Plan plan, Frame f, int s)
    {
        const Section sec = Section.Overhead;
        string tag = Sides[s], root = Roots(sec)[s];
        var room = f.room; float zc = f.FeederZ(s), runEnd = f.RunwayEnd(s), runwayMid = (f.runwayY0 + f.runwayY1) * .5f;
        var c = plan.Compose(sec, root, "Overhead_Feeder_" + tag, new Vector3((room.max.x + f.runwayX) * .5f, f.ceiling - 2f, zc));
        float face = EastFace(f, f.ceiling - 1.7f, zc), east = face - .55f, westFace = f.runwayX + .4f, hy = f.ceiling - 1.6f, sag = .65f;
        c.Box("Gunmetal", new Vector3(east, f.ceiling - 2.5f, zc - .3f), new Vector3(face, f.ceiling - .9f, zc + .3f));
        c.Box("Gunmetal", new Vector3(face - .05f, f.ceiling - 2.7f, zc - .3f), new Vector3(face, f.ceiling - .7f, zc + .3f));
        plan.Support(root + " east wall junction", new Vector3(room.max.x - .1f, f.ceiling - 1.7f, zc), Vector3.right);
        var westBox = MinMax(new Vector3(f.runwayX - .4f, f.runwayY0 + .05f, runEnd), new Vector3(westFace, f.runwayY1 - .05f, runEnd + (s == 0 ? -.6f : .6f)));
        c.Box("Gunmetal", westBox.min, westBox.max);
        plan.contacts.Add(new Contact { label = root + " runway conductor box on the gantry runway end", target = "Assembly_Backdrop/Assembly_Gantry/Gantry_Runway",
            box = westBox, axis = 2, positive = s == 1 });
        var supports = new[] { new Vector3(east, f.ceiling - 1.7f, zc), Vector3.zero, Vector3.zero, new Vector3(westFace, runwayMid, zc) };
        for (int i = 1; i <= 2; i++) supports[i] = new Vector3(Mathf.Lerp(east, westFace, i / 3f), hy, zc);
        var strands = new[] { new Vector2(.09f, -.12f), new Vector2(.09f, .12f), new Vector2(-.1f, 0) };
        var centre = new List<Vector3>();
        for (int span = 0; span < 3; span++)
            for (int k = span == 0 ? 0 : 1; k <= 24; k++)
            {
                float t = k / 24f; var p = Vector3.Lerp(supports[span], supports[span + 1], t);
                p.y -= 4 * sag * t * (1 - t) * (span == 0 || span == 2 ? .8f : 1f); centre.Add(p);
            }
        foreach (var o in strands) c.Tube("Gunmetal", centre.Select(p => p + new Vector3(0, o.x, o.y)).ToList(), .055f, 8);
        for (int k = 6; k < centre.Count - 3; k += 9)
            c.Box("Gunmetal", centre[k] + new Vector3(-.03f, -.2f, -.2f), centre[k] + new Vector3(.03f, .17f, .2f));
        for (int i = 1; i <= 2; i++)
        {
            var h = supports[i];
            c.Box("Gunmetal", h + new Vector3(-.12f, -.2f, -.24f), h + new Vector3(.12f, .24f, .24f));
            foreach (float dz in new[] { -.14f, .14f }) c.Cylinder("Gunmetal", h + new Vector3(0, .24f, dz), new Vector3(h.x, f.ceiling - .04f, zc + dz), .03f, 8);
            c.Box("Gunmetal", new Vector3(h.x - .2f, f.ceiling - .04f, zc - .24f), new Vector3(h.x + .2f, f.ceiling, zc + .24f));
            plan.Support($"{root} ceiling hanger {i}", new Vector3(h.x, f.ceiling - .1f, zc), Vector3.up);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    /// <summary>Writes the owned composed meshes. Runs before the scene transaction; scene Undo cannot restore asset writes.</summary>
    static Plan BuildAssets(Frame f)
    {
        foreach (var sec in Owned) EnsureFolder(Folder(sec) + "/Meshes");
        var plan = Compose(f);
        foreach (var c in plan.composers.OrderBy(c => c.name, StringComparer.Ordinal))
        {
            var (generated, used) = c.Build();
            string path = Folder(c.section) + "/Meshes/" + generated.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
            else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
            BossHallFramework.RequireOwnedAsset(existing, c.section);
            plan.pieces.Add(new Piece { section = c.section, root = c.root, name = c.name.Substring(3), path = path, mesh = existing, finishes = used, position = c.pivot });
        }
        AssetDatabase.SaveAssets();
        foreach (var p in plan.pieces.Where(p => !p.reused)) p.mesh = AssetDatabase.LoadAssetAtPath<Mesh>(p.path);
        return plan;
    }

    static Dictionary<string, string> AssetHashes() => Owned.SelectMany(s => Directory.GetFiles(Folder(s), "*", SearchOption.AllDirectories))
        .Concat(Owned.Select(s => Folder(s) + ".meta")).Where(File.Exists).ToDictionary(p => p, BossHallSurvey.Hash, StringComparer.Ordinal);

    static void Populate(Transform section, Section sec, Plan plan, Frame f)
    {
        var roots = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (string name in Roots(sec)) roots[name] = Create(name, section);
        foreach (var p in plan.pieces.Where(p => p.section == sec).OrderBy(p => Array.IndexOf(Roots(sec), p.root)).ThenBy(p => p.reused).ThenBy(p => p.name, StringComparer.Ordinal))
        {
            var t = Create(p.name, roots[p.root]);
            t.SetPositionAndRotation(p.position, p.rotation); t.localScale = p.scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = p.mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterials = p.finishes.Select(BossHallRendering.GetFinish).ToArray();
            r.receiveShadows = true; r.shadowCastingMode = ShadowMode(r.bounds, f);
        }
        // One consolidated collision hull and carving footprint per reachable floor-level group.
        foreach (var h in plan.hulls.Where(h => h.section == sec))
        {
            var hull = Create(HullName, roots[h.root]); hull.position = h.box.center;
            var box = hull.gameObject.AddComponent<BoxCollider>(); box.size = h.box.size;
            var obstacle = hull.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = h.box.size; obstacle.center = Vector3.zero;
            obstacle.carving = true; obstacle.carveOnlyStationary = true;
        }
    }

    // The shell kit's 2D-shader walls and roof write no shadow-map depth, so high casters near the east wall threw sun
    // shadows into Room 3. Everything in the upper wall band and overhead is therefore a non-casting, receiving part.
    static ShadowCastingMode ShadowMode(Bounds b, Frame f) => b.min.y >= f.upperTop - .01f ? ShadowCastingMode.Off : ShadowCastingMode.On;

    static Transform Create(string name, Transform parent)
    {
        var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Build Boss Hall services");
        go.transform.SetParent(parent, false); return go.transform;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Validation beyond the framework keep-outs.

    public static MeshRenderer[] Renderers() => Owned.SelectMany(s => BossHallFramework.FindSection(s).GetComponentsInChildren<MeshRenderer>(true)).ToArray();

    public static string Validate(BossHallFramework.Layout layout)
    {
        BossHallMechanicalArms.Validate(layout);
        var f = Measure(layout); var plan = Compose(f); var lines = new List<string>();
        var dressing = BossHallFramework.FindRoot();
        int reusedCount = 0, ownedCount = 0, triangles = 0;
        foreach (var sec in Owned)
        {
            var section = BossHallFramework.FindSection(sec);
            if (!section.Cast<Transform>().Select(t => t.name).SequenceEqual(Roots(sec)))
                throw new InvalidOperationException(section.name + " must contain exactly " + string.Join(", ", Roots(sec)));
            if (section.GetComponentsInChildren<Transform>(true).Any(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)))
                throw new InvalidOperationException("Services reuse meshes in place; prefab instances are not expected in " + section.name);
            var envelopes = f.Envelopes(sec);
            foreach (var r in section.GetComponentsInChildren<Renderer>(true))
            {
                if (BossHallEnergy.IsEnergyRenderer(r)) continue; // Independently validated env_053 child subdivision.
                string path = BossHallFramework.PathOf(r.transform); var b = r.bounds;
                if (!(r is MeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy) throw new InvalidOperationException("Invalid service renderer " + path);
                if (!envelopes.Any(e => Inside(e, b))) throw new InvalidOperationException(path + " leaves the measured service bands: " + B(b));
                if (b.min.y < f.floor + WallContactTop && (b.min.x < f.room.min.x + WallStandoff - .01f || b.max.x > f.room.max.x - WallStandoff + .01f ||
                    b.min.z < f.room.min.z + WallStandoff - .01f || b.max.z > f.room.max.z - WallStandoff + .01f))
                    throw new InvalidOperationException(path + " contacts a wall at floor level");
                var filter = r.GetComponent<MeshFilter>(); var mesh = filter ? filter.sharedMesh : null; string meshPath = mesh ? AssetDatabase.GetAssetPath(mesh) : "";
                bool owned = meshPath.StartsWith(Folder(sec) + "/Meshes/", StringComparison.Ordinal);
                if (!owned && !ReusePrefixes.Any(p => meshPath.StartsWith(p, StringComparison.Ordinal))) throw new InvalidOperationException("Unreviewed mesh source " + meshPath + " at " + path);
                if (owned && EditorUtility.IsDirty(mesh)) throw new InvalidOperationException("Unsaved service mesh " + meshPath);
                if (r.sharedMaterials.Length != mesh.subMeshCount || r.sharedMaterials.Any(m => !Finishes(sec).Any(n => m == BossHallRendering.GetFinish(n))))
                    throw new InvalidOperationException("Finish outside " + section.name + "'s planned " + string.Join("/", Finishes(sec)) + ": " + path);
                if (owned && (!plan.composers.Any(c => c.name == mesh.name && Vector3.Distance(c.pivot, r.transform.position) < .001f) ||
                    Quaternion.Angle(r.transform.rotation, Quaternion.identity) > .01f || Vector3.Distance(r.transform.lossyScale, Vector3.one) > .0001f))
                    throw new InvalidOperationException("Owned service part moved from its planned pivot: " + path);
                if (r.shadowCastingMode != ShadowMode(b, f) || !r.receiveShadows) throw new InvalidOperationException("Unexpected shadow mode (upper/overhead parts must not cast): " + path);
                if (owned) ownedCount++; else reusedCount++;
                for (int i = 0; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
            }
        }

        // Collision: exactly one consolidated box hull and matching carve per floor group; nothing above the floor band.
        var colliders = Owned.SelectMany(s => BossHallFramework.FindSection(s).GetComponentsInChildren<Collider>(true)).ToArray();
        var obstacles = Owned.SelectMany(s => BossHallFramework.FindSection(s).GetComponentsInChildren<NavMeshObstacle>(true)).ToArray();
        if (colliders.Length != plan.hulls.Count || obstacles.Length != plan.hulls.Count)
            throw new InvalidOperationException($"Expected {plan.hulls.Count} consolidated hulls and carves; got {colliders.Length} colliders, {obstacles.Length} obstacles.");
        foreach (var h in plan.hulls)
        {
            var root = BossHallFramework.FindSection(h.section).Find(h.root); var hull = root.Find(HullName);
            var box = hull ? hull.GetComponent<BoxCollider>() : null; var o = hull ? hull.GetComponent<NavMeshObstacle>() : null;
            if (!box || !o || hull.GetComponents<Collider>().Length != 1 || Vector3.Distance(box.bounds.min, h.box.min) > .01f || Vector3.Distance(box.bounds.max, h.box.max) > .01f ||
                o.shape != NavMeshObstacleShape.Box || Vector3.Distance(o.size, h.box.size) > .01f || o.center != Vector3.zero || !o.carving || !o.carveOnlyStationary || box.isTrigger)
                throw new InvalidOperationException("Hull/carve mismatch at " + h.root);
            if (h.box.max.y >= f.firingBottom || h.box.size.y < HullHeight - .01f) throw new InvalidOperationException("Hull height outside the jump-proof floor band: " + h.root);
            // Everything a player can reach from the floor lies inside its hull footprint, extended back to the wall.
            var reach = h.box; reach.Encapsulate(new Vector3(reach.center.x, reach.center.y, h.box.center.z < f.room.center.z ? f.room.min.z : f.room.max.z)); reach.Expand(new Vector3(.02f, 100, .02f));
            foreach (var r in root.GetComponentsInChildren<Renderer>(true).Where(r => r.bounds.min.y < f.floor + 2.1f))
                if (!Inside(reach, r.bounds)) throw new InvalidOperationException(BossHallFramework.PathOf(r.transform) + " is reachable outside its consolidated hull");
        }
        foreach (var r in Renderers().Where(r => r.bounds.min.y < f.floor + 2.1f))
            if (!plan.hulls.Any(h => r.transform.IsChildOf(BossHallFramework.FindSection(h.section).Find(h.root))))
                throw new InvalidOperationException(BossHallFramework.PathOf(r.transform) + " is reachable without a hull");
        lines.Add($"Collision: {plan.hulls.Count} consolidated box hulls with matching carving footprints, one per under-gallery floor group, all {HullHeight:F2} m high (jump-proof) and below the inter-tier firing volume; no collision or carving above the floor band.");

        lines.Add(Attachments(f, plan, dressing));
        lines.Add(Intersections(f, plan, dressing));
        lines.Add(FiringLines(layout, f, plan));
        var hallMaterials = dressing.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count();
        string summary = $"BOSS HALL SERVICES: Power_Services 2 roots, Cooling_Services 2 roots, Overhead 2 feeder roots; {reusedCount + ownedCount} renderers " +
            $"({reusedCount} reused Room 3/Workshop source meshes, {ownedCount} owned composed meshes), {triangles} triangles, {plan.hulls.Count} colliders + {plan.hulls.Count} carving footprints, " +
            $"0 prefab instances/lights/emitters/audio; finishes Power {string.Join("/", Finishes(Section.PowerServices))}, Cooling {string.Join("/", Finishes(Section.CoolingServices))}, " +
            $"Overhead {string.Join("/", Finishes(Section.Overhead))}; {hallMaterials}/20 whole-hall materials.\n" + string.Join("\n", lines);
        Debug.Log(summary);
        return summary;
    }

    static string Attachments(Frame f, Plan plan, Transform dressing)
    {
        Physics.SyncTransforms();
        int ok = 0; var failures = new List<string>();
        foreach (var a in plan.anchors)
        {
            var hits = Physics.RaycastAll(a.origin, a.direction, a.reach, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => !h.collider.transform.IsChildOf(dressing)).OrderBy(h => h.distance).ToArray();
            if (hits.Length == 0) failures.Add(a.label + " " + V(a.origin)); else ok++;
        }
        if (failures.Count > 0) throw new InvalidOperationException($"{failures.Count} unsupported service anchors: " + string.Join("; ", failures.Take(20)));
        var backdrop = BossHallFramework.FindSection(Section.AssemblyBackdrop);
        foreach (var c in plan.contacts)
        {
            var target = backdrop.Find(c.target.Substring("Assembly_Backdrop/".Length));
            var targetRenderer = target ? target.GetComponent<Renderer>() : null;
            if (!targetRenderer) throw new InvalidOperationException("Missing backdrop attachment target " + c.target);
            var t = targetRenderer.bounds; float face = c.positive ? t.max[c.axis] : t.min[c.axis], mine = c.positive ? c.box.min[c.axis] : c.box.max[c.axis];
            bool overlaps = Enumerable.Range(0, 3).Where(i => i != c.axis).All(i => c.box.min[i] < t.max[i] - .05f && c.box.max[i] > t.min[i] + .05f);
            if (Mathf.Abs(face - mine) > .012f || !overlaps) throw new InvalidOperationException($"Detached {c.label}: face {face:F3} vs {mine:F3}, overlap {overlaps}");
            var owner = Renderers().Where(r => Inside(Expanded(r.bounds, .01f), c.box)).ToArray();
            if (owner.Length == 0) throw new InvalidOperationException("No saved service renderer carries " + c.label);
            ok++;
        }
        return $"Attachments: {plan.anchors.Count} support rays hit original wall/ceiling/floor collision within reach; {plan.contacts.Count} face contacts sit on saved backdrop renderers " +
            "(two corbel junction boxes, two lintel headers, two runway conductor boxes). Every run ends in a wall chase, a backdrop contact or a junction box.";
    }

    // Each primitive of every saved service part (owned primitives from the plan; reused pieces by their own bounds) is
    // tested exactly: against original collision by box overlap, and against original and other-dressing triangles by a
    // separating-axis test. Parts shrink by up to 3 cm per side so intended bearing faces and contacts are not counted.
    static string Intersections(Frame f, Plan plan, Transform dressing)
    {
        var mine = Renderers(); var issues = new List<string>();
        var other = dressing.GetComponentsInChildren<MeshRenderer>(true).Where(r => !BossHallEnergy.IsEnergyRenderer(r) && !Owned.Any(s => r.transform.IsChildOf(BossHallFramework.FindSection(s)))).ToArray();
        var hall = f.room; hall.Expand(2);
        var original = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true))
            .Where(r => !r.transform.IsChildOf(dressing) && r.enabled && hall.Intersects(r.bounds)).ToArray();
        var parts = plan.composers.SelectMany(c => c.boxes.Select(b => (c.name.Substring(3), b)))
            .Concat(mine.Where(r => !AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh).StartsWith(BossHallFramework.AssetRoot + "/", StringComparison.Ordinal))
                .Select(r => (r.name, r.bounds))).ToArray();
        var triangles = new Dictionary<MeshRenderer, Vector3[]>();
        Vector3[] World(MeshRenderer r)
        {
            if (triangles.TryGetValue(r, out var cached)) return cached;
            var mesh = r.GetComponent<MeshFilter>()?.sharedMesh; var m = r.localToWorldMatrix; var list = new List<Vector3>();
            if (mesh)
            {
                var vertices = mesh.vertices;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) foreach (int i in mesh.GetTriangles(sub)) list.Add(m.MultiplyPoint3x4(vertices[i]));
            }
            return triangles[r] = list.ToArray();
        }
        int tested = 0;
        foreach (var (name, b) in parts)
        {
            Vector3 min = b.min, max = b.max;
            for (int i = 0; i < 3; i++)
            {
                float low = i == 1 ? f.floor : f.room.min[i], high = i == 1 ? f.ceiling : f.room.max[i];
                if (Mathf.Abs(min[i] - low) < .012f) min[i] += Bearing;
                if (Mathf.Abs(max[i] - high) < .012f) max[i] -= Bearing;
                float shrink = Mathf.Min(Shrink, (max[i] - min[i]) * .25f); min[i] += shrink; max[i] -= shrink;
            }
            if (max.x <= min.x || max.y <= min.y || max.z <= min.z) continue;
            var inner = MinMax(min, max);
            tested++;
            foreach (var c in Physics.OverlapBox(inner.center, inner.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(dressing)) issues.Add(name + " " + B(inner) + " x collider " + BossHallFramework.PathOf(c.transform));
            foreach (var o in other.Concat(original).Where(o => inner.Intersects(o.bounds)))
            {
                var t = World(o);
                for (int i = 0; i < t.Length; i += 3)
                    if (TriangleBox(inner.center, inner.extents, t[i], t[i + 1], t[i + 2]))
                    { issues.Add(name + " " + B(inner) + " x " + (o.transform.IsChildOf(dressing) ? "dressing " : "renderer ") + BossHallFramework.PathOf(o.transform)); break; }
            }
        }
        if (issues.Count > 0) throw new InvalidOperationException($"{issues.Count} service intersections: " + string.Join(" | ", issues.Distinct().Take(60)));
        return $"Intersections: {tested} primitive parts (shrunk up to {Shrink:F2} m per side; wall/floor/ceiling bearing faces up to {Bearing:F3} m) clear of all original colliders, {original.Length} original hall renderers' triangles " +
            $"and {other.Length} backdrop/arms/skin renderers' triangles.";
    }

    /// <summary>Separating-axis triangle/box overlap (Akenine-Moller).</summary>
    internal static bool TriangleBox(Vector3 centre, Vector3 h, Vector3 a, Vector3 b, Vector3 c)
    {
        a -= centre; b -= centre; c -= centre;
        if (Mathf.Max(a.x, b.x, c.x) < -h.x || Mathf.Min(a.x, b.x, c.x) > h.x || Mathf.Max(a.y, b.y, c.y) < -h.y || Mathf.Min(a.y, b.y, c.y) > h.y ||
            Mathf.Max(a.z, b.z, c.z) < -h.z || Mathf.Min(a.z, b.z, c.z) > h.z) return false;
        Vector3 e0 = b - a, e1 = c - b, e2 = a - c;
        foreach (var e in new[] { e0, e1, e2 })
            foreach (var u in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                var axis = Vector3.Cross(u, e); if (axis.sqrMagnitude < 1e-12f) continue;
                float p0 = Vector3.Dot(a, axis), p1 = Vector3.Dot(b, axis), p2 = Vector3.Dot(c, axis);
                float r = h.x * Mathf.Abs(axis.x) + h.y * Mathf.Abs(axis.y) + h.z * Mathf.Abs(axis.z);
                if (Mathf.Min(p0, p1, p2) > r || Mathf.Max(p0, p1, p2) < -r) return false;
            }
        var n = Vector3.Cross(e0, e1);
        float d = Vector3.Dot(n, a), rn = h.x * Mathf.Abs(n.x) + h.y * Mathf.Abs(n.y) + h.z * Mathf.Abs(n.z);
        return Mathf.Abs(d) <= rn;
    }

    // Lines from the turret muzzle to standing eyes on the floor and both raised tiers; eyes inside a hull footprint are occupied.
    static string FiringLines(BossHallFramework.Layout layout, Frame f, Plan plan)
    {
        var r = layout.Report; var t = r.turret; var mine = Renderers(); float eye = BossHallRenderingReview.EyeHeight();
        // Radius comes from the saved player; the trimmed contract does not carry it.
        float radius = Object.FindAnyObjectByType<Unity.FPS.Gameplay.PlayerCharacterController>().GetComponent<CharacterController>().radius;
        var head = (t.head.min + t.head.max) * .5f;
        bool Blocked(Vector3 a, Vector3 b)
        { float d = Vector3.Distance(a, b); var ray = new Ray(a, (b - a) / d); return mine.Any(x => x.bounds.IntersectRay(ray, out float hit) && hit >= 0 && hit < d); }
        if (Blocked(r.entryEye, head)) throw new InvalidOperationException("Services obscure the entry-to-turret head sight line.");
        int eyes = 0, occupied = 0, blocked = 0;
        var footprints = plan.hulls.Select(h => { var b = h.box; b.Encapsulate(new Vector3(b.center.x, b.center.y, b.center.z < f.room.center.z ? f.room.min.z : f.room.max.z)); b.Expand(new Vector3(radius * 2, 0, radius * 2)); return b; }).ToArray();
        for (float x = f.room.min.x + .5f; x < f.room.max.x; x += 1)
            for (float z = f.room.min.z + .5f; z < f.room.max.z; z += 1)
            {
                var p = new Vector3(x, r.floorTopY + eye, z); if (Vector3.Distance(p, t.firingOrigin) > t.detectionRange) continue;
                eyes++; if (footprints.Any(b => b.min.x <= x && x <= b.max.x && b.min.z <= z && z <= b.max.z)) { occupied++; continue; }
                if (Blocked(t.firingOrigin, p)) blocked++;
            }
        int raised = 0;
        foreach (var deck in r.deckFootprints.Where(d => d.name.Contains(" gallery:") || d.name.StartsWith("Upper deck network:", StringComparison.Ordinal)))
            for (float x = deck.min.x + .5f; x < deck.max.x; x += 1)
                for (float z = deck.min.z + .5f; z < deck.max.z; z += 1)
                {
                    var p = new Vector3(x, deck.max.y + eye, z); if (Vector3.Distance(p, t.firingOrigin) > t.detectionRange) continue;
                    raised++; if (Blocked(t.firingOrigin, p)) blocked++;
                }
        if (blocked != 0) throw new InvalidOperationException("Service renderer bounds cross " + blocked + " turret lines.");
        return $"Turret: entry-to-head silhouette clear; {eyes} floor eyes in detection ({occupied} inside hull footprints, 0 blocked) and {raised} tier-1/tier-2 eyes, 0 blocked by any service renderer bound.";
    }

    static bool Inside(Bounds envelope, Bounds b) =>
        b.min.x >= envelope.min.x - .01f && b.min.y >= envelope.min.y - .01f && b.min.z >= envelope.min.z - .01f &&
        b.max.x <= envelope.max.x + .01f && b.max.y <= envelope.max.y + .01f && b.max.z <= envelope.max.z + .01f;
    static Bounds Expanded(Bounds b, float amount) { b.Expand(amount * 2); return b; }
    static Bounds MinMax(Vector3 a, Vector3 b) { var result = new Bounds(); result.SetMinMax(Vector3.Min(a, b), Vector3.Max(a, b)); return result; }
    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    /// <summary>Exact bounds of a capped cylinder between two points.</summary>
    static Bounds SegmentBox(Vector3 a, Vector3 c, float r)
    {
        var u = (c - a).normalized;
        var e = new Vector3(r * Mathf.Sqrt(Mathf.Max(0, 1 - u.x * u.x)), r * Mathf.Sqrt(Mathf.Max(0, 1 - u.y * u.y)), r * Mathf.Sqrt(Mathf.Max(0, 1 - u.z * u.z)));
        return MinMax(Vector3.Min(a, c) - e, Vector3.Max(a, c) + e);
    }
    /// <summary>Measured face of the east wall collider at a mounting point; the kit face varies by a few centimetres.</summary>
    static float EastFace(Frame f, float y, float z)
    {
        var dressing = BossHallFramework.FindRoot();
        var hit = Physics.RaycastAll(new Vector3(f.room.max.x - 1, y, z), Vector3.right, 1.3f, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => !dressing || !h.collider.transform.IsChildOf(dressing)).OrderBy(h => h.distance).FirstOrDefault();
        return hit.collider && Mathf.Abs(hit.point.x - f.room.max.x) < .1f ? Mathf.Min(hit.point.x, f.room.max.x) : f.room.max.x;
    }
    static string V(Vector3 v) => FormattableString.Invariant($"({v.x:F2}, {v.y:F2}, {v.z:F2})");
    public static string B(Bounds b) => FormattableString.Invariant($"X [{b.min.x:F2}, {b.max.x:F2}], Y [{b.min.y:F2}, {b.max.y:F2}], Z [{b.min.z:F2}, {b.max.z:F2}]");

    public static string NetworkReport(BossHallFramework.Layout layout)
    {
        var f = Measure(layout); var plan = Compose(f); string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        var lines = new List<string> {
            $"Frame: wall band {F(f.band)} m; floor band to Y {F(f.floorTop)} against the wall and Y {F(f.firingBottom)} 2.2 m out; gallery band Y {F(f.galleryTop)}-{F(f.upperBottom)}; " +
            $"upper band Y {F(f.upperTop)}-{F(f.overheadTop)}; chase stations power X {F(f.powerX)}, cooling X {F(f.coolingX)}; main Y {F(f.mainY)}, gallery line Y {F(f.runY)}, tray Y {F(f.trayY)}.",
        };
        foreach (var h in plan.hulls) lines.Add("Hull " + h.root + ": " + B(h.box));
        foreach (var c in plan.contacts) lines.Add("Contact " + c.label + ": " + B(c.box) + " -> " + c.target);
        foreach (var sec in Owned) lines.Add("Envelope " + sec + ": " + string.Join("; ", f.Envelopes(sec).Select(B).Distinct()));
        return string.Join("\n", lines.Distinct());
    }

    // ---------------------------------------------------------------------------------------------------------------

    static void Rebuild(BossHallFramework.Layout layout, Plan plan)
    {
        var f = Measure(layout);
        foreach (var sec in Owned) BossHallFramework.RebuildSection(layout, sec, Owner, t => Populate(t, sec, plan, f));
    }

    [MenuItem("Tools/CoreBuster/Build Boss Hall Services")]
    public static void BuildMenu()
    {
        var layout = BossHallFramework.MeasureLayout(); var plan = BuildAssets(Measure(layout)); Rebuild(layout, plan); Validate(layout);
        Debug.Log("Boss Hall services built with Undo. Review and save through the guarded batch workflow; the gameplay gate was waived for env_050 only.");
    }
    [MenuItem("Tools/CoreBuster/Validate Boss Hall Services")]
    public static void ValidateMenu() => Validate(BossHallFramework.MeasureLayout());

    internal static void RebuildForEnergyVerification(BossHallFramework.Layout layout)
    { var plan = BuildAssets(Measure(layout)); Rebuild(layout, plan); Validate(layout); }

    /// <summary>Protected state: everything outside the dressing root plus every dressing section this task does not own.</summary>
    static Dictionary<string, string> CaptureProtected()
    {
        var scene = SceneManager.GetActiveScene(); var result = new Dictionary<string, string>(StringComparer.Ordinal);
        void Merge(string prefix, Dictionary<string, string> values) { foreach (var p in values) result[prefix + p.Key] = p.Value; }
        Merge("outside|", BossHallSnapshot.Capture(scene, BossHallFramework.FindRoot()));
        foreach (var spec in BossHallFramework.Sections.Where(s => !Owned.Contains(s.section)))
            Merge(spec.name + "|", BossHallSnapshot.Capture(scene, null, BossHallFramework.FindSection(spec.section)));
        return result;
    }

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch Editor access.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); BossHallMechanicalArms.Validate(layout);
        var before = CaptureProtected(); var assets = BossHallSnapshot.CaptureAssets();
        BossHallServicesReview.NeighbourProof(layout, false);
        var plan = BuildAssets(Measure(layout));
        Rebuild(layout, plan); Validate(layout);
        VerifyGuards(layout);
        foreach (var sec in Owned)
        {
            var section = BossHallFramework.FindSection(sec); int count = section.GetComponentsInChildren<Transform>(true).Length;
            BossHallFramework.RebuildSection(layout, sec, Owner, _ => { }); Undo.PerformUndo();
            if (section.GetComponentsInChildren<Transform>(true).Length != count) throw new InvalidOperationException("Isolated clear/Undo failed for " + section.name);
            Debug.Log("PASS: isolated " + section.name + " clear and Undo");
        }
        Physics.SyncTransforms(); Validate(layout);
        layout = BossHallFramework.SaveAndReload(layout); string summary = Validate(layout);
        BossHallSnapshot.Compare(before, CaptureProtected(), "Protected scene state after services reload");
        BossHallSnapshot.Compare(assets, BossHallSnapshot.CaptureAssets(), "Shared assets after services reload");
        var hashes = AssetHashes(); BuildAssets(Measure(layout)); BossHallSnapshot.Compare(hashes, AssetHashes(), "Deterministic service assets rerun");
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Asset rerun dirtied MainScene");
        string neighbours = BossHallServicesReview.NeighbourProof(layout, true);
        BossHallServicesReview.Capture(layout);
        BossHallRenderingReview.ValidateNeighbours();
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Service verification dirtied the saved scene.");
        Directory.CreateDirectory("Docs/BossHall");
        File.WriteAllText("Docs/BossHall/Services_050.txt", "corebuster_env_050 | Unity " + Application.unityVersion + " | 2026-10-04\nSaved scene SHA-256: " + BossHallSurvey.Hash(BossHallSurvey.ScenePath) +
            "\nPASS: contract keep-outs, service-band envelopes, wall stand-off, finishes, mesh provenance, hull/carve consolidation, support rays, backdrop face contacts, intersections, turret lines, " +
            "negative guards, isolated clear/Undo per section, save/reload, deterministic assets, " + before.Count + " protected scene/section states, " + assets.Count +
            " shared asset/meta/settings entries, Centre and Room 3 validators.\n" + NetworkReport(layout) + "\n" + summary + "\n" + neighbours + "\n" + BossHallFramework.BudgetReport() + "\n");
        Debug.Log("BOSS HALL SERVICES BUILD VERIFIED");
    }

    public static void ValidateBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch Editor access.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); string sha = BossHallSurvey.Hash(BossHallSurvey.ScenePath);
        ValidateMenu(); BossHallRenderingReview.ValidateNeighbours();
        if (SceneManager.GetActiveScene().isDirty || sha != BossHallSurvey.Hash(BossHallSurvey.ScenePath)) throw new InvalidOperationException("Read-only services check changed the scene.");
        Debug.Log("BOSS HALL SERVICES RELOAD VERIFIED");
    }

    /// <summary>Asset-free dry run: composes the plan and reports measured frames without touching assets or the scene.</summary>
    public static void PlanBatch()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); Debug.Log("BOSS HALL SERVICES PLAN\n" + NetworkReport(layout));
    }

    /// <summary>Unsaved diagnosis: builds in memory, reports every check category separately and captures; never saves the scene.</summary>
    public static void DiagnoseBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use exclusive batch Editor access.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        var layout = BossHallFramework.MeasureLayout(); var f = Measure(layout); var plan = BuildAssets(f);
        foreach (var sec in Owned)
            try { BossHallFramework.RebuildSection(layout, sec, Owner, t => Populate(t, sec, plan, f)); }
            catch (Exception e) { Debug.LogError("DIAG FAIL rebuild " + sec + ": " + e.Message); Populate(BossHallFramework.FindSection(sec), sec, plan, f); }
        Physics.SyncTransforms();
        void Try(string label, Func<string> action)
        { try { Debug.Log("DIAG " + label + ": " + action()); } catch (Exception e) { Debug.LogError("DIAG FAIL " + label + ": " + e.Message); } }
        var dressing = BossHallFramework.FindRoot();
        Try("attachments", () => Attachments(f, plan, dressing));
        Try("intersections", () => Intersections(f, plan, dressing));
        Try("firing", () => FiringLines(layout, f, plan));
        Try("validate", () => Validate(layout));
        Try("capture", () => { BossHallServicesReview.Capture(layout); return "written"; });
        Debug.Log("BOSS HALL SERVICES DIAGNOSIS DONE (scene not saved)");
    }

    static void VerifyGuards(BossHallFramework.Layout layout)
    {
        void Reject(Action action, string label)
        {
            try { action(); } catch (InvalidOperationException) { Debug.Log("PASS: services guard " + label); return; }
            throw new InvalidOperationException("Accepted invalid services state: " + label);
        }
        var power = BossHallFramework.FindSection(Section.PowerServices); var cooling = BossHallFramework.FindSection(Section.CoolingServices);
        var overhead = BossHallFramework.FindSection(Section.Overhead);
        var loop = cooling.GetChild(1); var p = loop.position;
        try { loop.position = p + Vector3.up * 1.5f; Physics.SyncTransforms(); Reject(() => BossHallFramework.Validate(layout), "cooling loop raised into the inter-tier firing volume"); }
        finally { loop.position = p; }
        var feeder = overhead.GetChild(0); var q = feeder.position;
        try { feeder.position = q + Vector3.down * 5f; Physics.SyncTransforms(); Reject(() => BossHallFramework.Validate(layout), "feeder lowered into tier-2 headroom"); }
        finally { feeder.position = q; }
        var feed = power.GetChild(0); var w = feed.position;
        try { feed.position = w + Vector3.back * .3f; Physics.SyncTransforms(); Reject(() => Validate(layout), "power floor group touching the wall at floor level"); }
        finally { feed.position = w; }
        var renderer = cooling.GetComponentInChildren<MeshRenderer>(); var materials = renderer.sharedMaterials;
        try { renderer.sharedMaterial = BossHallRendering.GetFinish("WarningRed"); Reject(() => Validate(layout), "unplanned finish"); }
        finally { renderer.sharedMaterials = materials; }
        var light = new GameObject("__Services_Light_Probe").AddComponent<Light>();
        try { light.transform.SetParent(overhead.GetChild(1), false); Reject(() => BossHallFramework.Validate(layout), "light outside Lighting"); }
        finally { Object.DestroyImmediate(light.gameObject); }
        var extra = overhead.GetChild(1).gameObject.AddComponent<BoxCollider>();
        try { Physics.SyncTransforms(); Reject(() => Validate(layout), "collider on an unreachable feeder"); }
        finally { Object.DestroyImmediate(extra); }
        var obstacle = power.GetChild(1).Find(HullName).GetComponent<NavMeshObstacle>();
        try { obstacle.carving = false; Reject(() => Validate(layout), "hull without carving"); }
        finally { obstacle.carving = true; }
        Reject(() => BossHallFramework.RebuildSection(layout, Section.PowerServices, "corebuster_env_051", _ => { }), "wrong owner");
        Physics.SyncTransforms(); Validate(layout);
    }
}
