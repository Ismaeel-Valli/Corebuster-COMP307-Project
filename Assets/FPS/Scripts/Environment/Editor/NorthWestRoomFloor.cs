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

/// <summary>
/// Floor definition and lane markings for corebuster_env_042: collider-free flat overlays under
/// Level/NorthWest_Room/Floor_Detail. Footprints come from NorthWestRoomDensityPlan.json (corebuster_env_040) and the
/// measured survey; the two atlases are drawn by Tools/Art/generate_north_west_room_floor.py.
/// </summary>
public static class NorthWestRoomFloor
{
    const string Owner = "corebuster_env_042";
    const string Folder = "Assets/FPS/Art/NorthWestRoom/Floor_Detail";
    const string PlanPath = "Assets/FPS/Art/NorthWestRoom/NorthWestRoomDensityPlan.json";
    const string Output = "ReviewCaptures/NorthWestRoom/042";
    const float Lift = .02f, LineWidth = .14f, PlateExtension = .3f, MinPiece = .6f;
    const int AtlasSize = 1024, CellSize = 512, CellInset = 8;

    // Cell order is the contract with generate_north_west_room_floor.py.
    enum Cell { Line, Chevron, Arena, Plate, Grate, Drain, Trench, Roundel }
    static bool IsHardware(Cell c) => c >= Cell.Grate;
    static (int col, int row) CellAt(Cell c) { int i = (int)c % 4; return (i % 2, i / 2); }

    // Bays built by corebuster_env_031/032/041 get a lane edge. Add 033's owner when its bays are built.
    static readonly HashSet<string> BuiltOwners = new HashSet<string> { "corebuster_env_031", "corebuster_env_032", "corebuster_env_041" };
    // Turbine and transformer assemblies get a zone plate, a grate and a hazard border: bay id -> grate position along the wall (0..1).
    static readonly Dictionary<string, float> PlateBays = new Dictionary<string, float> { ["S2"] = .5f, ["S6"] = .5f, ["N1"] = .2f, ["N8"] = .75f };
    // Open-space roundels (hand-placed inside the density plan's secondary open spaces; verified against the polygons).
    static readonly (string space, float x, float z)[] Roundels =
    {
        ("O2", -245.2f, 121.2f), ("O3", -225.5f, 108.9f), ("O4", -228.0f, 125.6f), ("O5", -241.8f, 108.0f),
    };
    const float RoundelSize = 3.2f, DrainSize = .7f, GrateAlong = 1.0f, GrateDepth = .6f, ArenaSize = 16.4f, ChevronLength = 1.5f, ChevronWidth = 1.2f;
    static readonly string[] Groups = { "Lane_Edges", "Zone_Plates", "Hardware", "Arena_And_Open_Spaces", "Progression_Chevrons" };

    [Serializable] class P2 { public float x, z; }
    [Serializable] class R2 { public P2 min, max; }
    [Serializable] class PlanBay { public string id, owner; public R2 footprint; }
    [Serializable] class PlanSpace { public string id; public P2[] polygon; }
    [Serializable] class Plan { public PlanBay[] bays; public PlanSpace[] openSpaces; }

    struct Rect
    {
        public float x0, z0, x1, z1;
        public Rect(float x0, float z0, float x1, float z1) { this.x0 = x0; this.z0 = z0; this.x1 = x1; this.z1 = z1; }
        public Vector2 Centre => new Vector2((x0 + x1) * .5f, (z0 + z1) * .5f);
        public bool Overlaps(Rect o, float tol = 0) => x0 < o.x1 - tol && x1 > o.x0 + tol && z0 < o.z1 - tol && z1 > o.z0 + tol;
        public static Rect Of(Bounds b) => new Rect(b.min.x, b.min.z, b.max.x, b.max.z);
    }

    sealed class Context
    {
        public NorthWestRoomFramework.Layout layout;
        public Plan plan;
        public Mesh[] meshes;
        public Material markings, hardware;
        public float y;
        public List<Rect> blockers = new List<Rect>();   // terminal and door-approach keep-outs, for lines and plates
        public List<Rect> terminals = new List<Rect>();  // terminal keep-outs only, for the chevrons
        public List<Rect> footprints = new List<Rect>();
        public List<string> skipped = new List<string>();
    }

    [MenuItem("Tools/CoreBuster/Build North-West Room Floor Detail")]
    public static void BuildMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        NorthWestRoomFramework.Build(layout);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build North-West Room Floor Detail");
        try { Build(layout); RequireValid(layout); Undo.CollapseUndoOperations(group); }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        NorthWestRoomFramework.Build(layout); // creates only the missing Floor_Detail container
        var before = SnapshotOutside();
        var protectedMaterials = NorthWestRoomFramework.CaptureProtectedMaterialHashes();
        Build(layout);
        VerifyIsolatedUndo(layout);
        RequireValid(layout);
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not save Room 3 floor detail.");
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var after = SnapshotOutside();
        if (before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key, out string value) || value != p.Value))
            throw new InvalidOperationException("A scene object outside Floor_Detail changed: " + string.Join("; ",
                before.Where(p => !after.TryGetValue(p.Key, out string value) || value != p.Value).Take(5).Select(p => p.Value + " => " + (after.TryGetValue(p.Key, out string v) ? v : "missing"))) +
                (before.Count != after.Count ? $" (count {before.Count} -> {after.Count})" : ""));
        NorthWestRoomFramework.VerifyProtectedMaterialHashes(protectedMaterials);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Build.txt", "corebuster_env_042: floor overlays; isolation, flat/collider-free rules, keep-outs, misleading-marking rules, material hashes, and save/reload passed.\n");
        Debug.Log($"NORTH-WEST FLOOR DETAIL VERIFIED: {before.Count} outside transforms unchanged.");
    }

    [MenuItem("Tools/CoreBuster/Validate North-West Room Floor Detail")]
    public static void ValidateMenu() => RequireValid(NorthWestRoomFramework.MeasureLayout());

    public static void ValidateBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        ValidateMenu();
    }

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    [MenuItem("Tools/CoreBuster/Capture North-West Room Floor Detail")]
    public static void CaptureMenu()
    {
        var layout = NorthWestRoomFramework.MeasureLayout();
        RequireValid(layout);
        var room = layout.interior;
        var c = layout.report.centre;
        float eye = layout.Floor + layout.report.playerEyeHeight, fy = layout.Floor;
        var door = layout.report.gates.First(g => g.name == NorthWestRoomSurvey.EastDoorName);
        var views = new[]
        {
            ("From_East_Door", new Vector3(room.max.x - 1.5f, eye, door.position.z), new Vector3(c.x - 6f, fy, c.z + 2f)),
            ("From_Centre_South", new Vector3(c.x, eye, c.z + 1f), new Vector3(c.x - 2f, fy, room.min.z + 3f)),
            ("From_Centre_North", new Vector3(c.x, eye, c.z - 1f), new Vector3(c.x + 1f, fy, room.max.z - 3f)),
            ("Progression_Lane", new Vector3(-248.5f, eye, 106.6f), new Vector3(-248.3f, fy, 114.5f)),
            ("Arena_Overview", new Vector3(c.x - 10f, layout.Floor + 6f, c.z - 10f), new Vector3(c.x, fy, c.z)),
        };
        Directory.CreateDirectory(Output);
        var go = new GameObject("Temporary Room 3 floor review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 70;
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        // Unsaved review lamps: the room has no lighting until corebuster_env_037.
        var lamps = new List<GameObject>();
        foreach (var p in new[] { new Vector3(c.x, layout.Floor + 6f, c.z), new Vector3(c.x - 10f, layout.Floor + 6f, c.z - 8f), new Vector3(c.x + 10f, layout.Floor + 6f, c.z + 8f), new Vector3(c.x, layout.Floor + 6f, room.max.z - 4f) })
        {
            var lamp = new GameObject("Temporary floor review lamp") { hideFlags = HideFlags.HideAndDontSave };
            lamp.transform.position = p;
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point; light.range = 24; light.intensity = 8; light.color = new Color(1f, .78f, .5f); light.shadows = LightShadows.None;
            lamps.Add(lamp);
        }
        try
        {
            foreach (var view in views)
            {
                camera.transform.SetPositionAndRotation(view.Item2, Quaternion.LookRotation(view.Item3 - view.Item2));
                var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = target;
                    camera.Render(); camera.Render();
                    RenderTexture.active = target;
                    texture.ReadPixels(Rect2(0, 0, 1280, 720), 0, 0);
                    texture.Apply();
                    File.WriteAllBytes(Output + "/" + view.Item1 + ".png", texture.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    Object.DestroyImmediate(texture);
                }
            }
        }
        finally { foreach (var l in lamps) Object.DestroyImmediate(l); Object.DestroyImmediate(go); }
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Floor capture dirtied MainScene.");
    }

    // UnityEngine.Rect is shadowed by the private overlay Rect above.
    static UnityEngine.Rect Rect2(float x, float y, float w, float h) => new UnityEngine.Rect(x, y, w, h);

    // --- Build ----------------------------------------------------------------------------------------------------

    static void Build(NorthWestRoomFramework.Layout layout)
    {
        var ctx = NewContext(layout, true);
        Transform section = NorthWestRoomFramework.BeginSectionRebuild(layout, Section.FloorDetail, Owner);
        var groups = Groups.ToDictionary(n => n, n => Group(section, n));
        var report = layout.report;

        // Lane edges, zone plates, grates.
        foreach (var bay in ctx.plan.bays.Where(b => BuiltOwners.Contains(b.owner)))
        {
            var fp = new Rect(bay.footprint.min.x, bay.footprint.min.z, bay.footprint.max.x, bay.footprint.max.z);
            var wall = WallOf(fp, layout.interior, out Vector2 inward);
            bool plate = PlateBays.TryGetValue(bay.id, out float grateAlong);
            if (plate)
            {
                Rect pr = Extend(fp, inward, PlateExtension);
                Quad(ctx, groups["Zone_Plates"], "Plate_" + bay.id, Cell.Plate, pr.Centre, Vector2.right, pr.x1 - pr.x0, pr.z1 - pr.z0, 0);
                foreach (var side in SideLines(pr, inward)) Line(ctx, groups["Zone_Plates"], "Border_" + bay.id, side);
                Line(ctx, groups["Zone_Plates"], "Border_" + bay.id, FrontLine(pr, wall, inward, -LineWidth * .5f));
                Vector2 along = AlongAxis(inward);
                Vector2 front = FrontPoint(pr, inward) + inward * (GrateDepth * .5f + .25f);
                Vector2 pos = AlongPoint(fp, along, grateAlong, front);
                Quad(ctx, groups["Hardware"], "Grate_" + bay.id, Cell.Grate, pos, along.x > 0 ? Vector2.right : Vector2.up, GrateAlong, GrateDepth, 3);
            }
            else Line(ctx, groups["Lane_Edges"], "Edge_" + bay.id, FrontLine(fp, wall, inward, .10f + LineWidth * .5f));
        }

        // Cable trenches joining the transformer and turbine plates, stopping short of terminals and door approaches.
        foreach (var piece in Clip(new Rect(-239.2f, 102.42f, -229.4f, 102.82f), ctx.blockers)) Quad(ctx, groups["Hardware"], "Trench_South", Cell.Trench, piece.Centre, Vector2.right, piece.x1 - piece.x0, piece.z1 - piece.z0, 1);
        foreach (var piece in Clip(new Rect(-246.1f, 128.1f, -223.3f, 128.5f), ctx.blockers)) Quad(ctx, groups["Hardware"], "Trench_North", Cell.Trench, piece.Centre, Vector2.right, piece.x1 - piece.x0, piece.z1 - piece.z0, 1);

        // Central arena rings and drain, and a roundel with a drain in each secondary open space.
        Vector2 centre = new Vector2(report.centre.x, report.centre.z);
        Quad(ctx, groups["Arena_And_Open_Spaces"], "Arena_Rings", Cell.Arena, centre, Vector2.right, ArenaSize, ArenaSize, 1);
        Quad(ctx, groups["Arena_And_Open_Spaces"], "Drain_Arena", Cell.Drain, centre, Vector2.right, DrainSize, DrainSize, 3);
        foreach (var (space, x, z) in Roundels)
        {
            Quad(ctx, groups["Arena_And_Open_Spaces"], "Roundel_" + space, Cell.Roundel, new Vector2(x, z), Vector2.right, RoundelSize, RoundelSize, 2);
            Quad(ctx, groups["Arena_And_Open_Spaces"], "Drain_" + space, Cell.Drain, new Vector2(x, z), Vector2.right, DrainSize, DrainSize, 3);
        }

        // Worn chevrons along the progression lane, Master Terminal to Boss Hall Door.
        var route = report.routes.Single(r => r.name == "Master Terminal to boss door inside");
        Vector2 a = new Vector2(route.corners[0].x, route.corners[0].z), b = new Vector2(route.corners[route.corners.Length - 1].x, route.corners[route.corners.Length - 1].z);
        Vector2 dir = (b - a).normalized;
        float length = Vector2.Distance(a, b);
        for (float s = 3.6f; s <= length - .8f; s += 1.8f)
        {
            Vector2 p = a + dir * s;
            var box = Box(p, dir, ChevronLength, ChevronWidth);
            if (ctx.terminals.Any(t => t.Overlaps(box))) { ctx.skipped.Add($"chevron at s={s:F1} overlaps a terminal keep-out"); continue; }
            Quad(ctx, groups["Progression_Chevrons"], "Chevron_" + (groups["Progression_Chevrons"].childCount + 1), Cell.Chevron, p, dir, ChevronLength, ChevronWidth, 2);
        }
        if (ctx.skipped.Count > 0) Debug.Log("NORTH-WEST FLOOR DETAIL notes: " + string.Join("; ", ctx.skipped));
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static Context NewContext(NorthWestRoomFramework.Layout layout, bool create)
    {
        var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath));
        var ctx = new Context { layout = layout, plan = plan, y = layout.Floor + Lift };
        foreach (var b in plan.bays) ctx.footprints.Add(new Rect(b.footprint.min.x, b.footprint.min.z, b.footprint.max.x, b.footprint.max.z));
        var report = layout.report;
        ctx.terminals.Add(Rect.Of(ToBounds(report.redTerminal.keepOut)));
        ctx.terminals.Add(Rect.Of(ToBounds(report.masterTerminal.keepOut)));
        ctx.blockers.AddRange(ctx.terminals);
        foreach (var gate in report.gates) ctx.blockers.Add(Rect.Of(ToBounds(gate.approach)));
        if (create) { EnsureFolders(); EnsureTextureImport(); ctx.meshes = LoadMeshes(); LoadMaterials(ctx); }
        return ctx;
    }

    // --- Geometry helpers -----------------------------------------------------------------------------------------

    enum Wall { East, West, North, South }

    static Wall WallOf(Rect fp, Bounds room, out Vector2 inward)
    {
        float e = room.max.x - fp.x1, w = fp.x0 - room.min.x, n = room.max.z - fp.z1, s = fp.z0 - room.min.z;
        float best = Mathf.Min(Mathf.Min(e, w), Mathf.Min(n, s));
        if (best == e) { inward = Vector2.left; return Wall.East; }
        if (best == w) { inward = Vector2.right; return Wall.West; }
        if (best == n) { inward = Vector2.down; return Wall.North; }
        inward = Vector2.up; return Wall.South;
    }

    static Vector2 AlongAxis(Vector2 inward) => Mathf.Abs(inward.x) > .5f ? Vector2.up : Vector2.right;

    static Rect Extend(Rect fp, Vector2 inward, float by)
    {
        if (inward.x < -.5f) fp.x0 -= by; else if (inward.x > .5f) fp.x1 += by;
        else if (inward.y < -.5f) fp.z0 -= by; else fp.z1 += by;
        return fp;
    }

    static Vector2 FrontPoint(Rect r, Vector2 inward)
    {
        Vector2 c = r.Centre;
        if (inward.x < -.5f) return new Vector2(r.x0, c.y);
        if (inward.x > .5f) return new Vector2(r.x1, c.y);
        return inward.y < -.5f ? new Vector2(c.x, r.z0) : new Vector2(c.x, r.z1);
    }

    /// <summary>Point at a fraction along the wall of the bay, and the given depth coordinate.</summary>
    static Vector2 AlongPoint(Rect fp, Vector2 along, float t, Vector2 front)
    {
        return along.x > .5f ? new Vector2(Mathf.Lerp(fp.x0, fp.x1, t), front.y) : new Vector2(front.x, Mathf.Lerp(fp.z0, fp.z1, t));
    }

    /// <summary>A strip along a bay's front edge, its centre offset from the edge by offset (positive = into the room).</summary>
    static Rect FrontLine(Rect fp, Wall wall, Vector2 inward, float offset)
    {
        float h = LineWidth * .5f;
        switch (wall)
        {
            case Wall.East: { float c = fp.x0 - offset; return new Rect(c - h, fp.z0, c + h, fp.z1); }
            case Wall.West: { float c = fp.x1 + offset; return new Rect(c - h, fp.z0, c + h, fp.z1); }
            case Wall.North: { float c = fp.z0 - offset; return new Rect(fp.x0, c - h, fp.x1, c + h); }
            default: { float c = fp.z1 + offset; return new Rect(fp.x0, c - h, fp.x1, c + h); }
        }
    }

    static IEnumerable<Rect> SideLines(Rect plate, Vector2 inward)
    {
        float h = LineWidth * .5f;
        if (Mathf.Abs(inward.x) > .5f)
        {
            yield return new Rect(plate.x0, plate.z0, plate.x1, plate.z0 + LineWidth);
            yield return new Rect(plate.x0, plate.z1 - LineWidth, plate.x1, plate.z1);
        }
        else
        {
            yield return new Rect(plate.x0, plate.z0, plate.x0 + LineWidth, plate.z1);
            yield return new Rect(plate.x1 - LineWidth, plate.z0, plate.x1, plate.z1);
        }
    }

    static Rect Box(Vector2 centre, Vector2 dir, float length, float width)
    {
        float hx = Mathf.Abs(dir.x) * length * .5f + Mathf.Abs(dir.y) * width * .5f, hz = Mathf.Abs(dir.y) * length * .5f + Mathf.Abs(dir.x) * width * .5f;
        return new Rect(centre.x - hx, centre.y - hz, centre.x + hx, centre.y + hz);
    }

    /// <summary>Subtracts blockers from an axis-aligned strip along its long axis, dropping pieces shorter than MinPiece.</summary>
    static List<Rect> Clip(Rect strip, List<Rect> blockers)
    {
        bool alongX = strip.x1 - strip.x0 >= strip.z1 - strip.z0;
        var pieces = new List<(float a, float b)> { alongX ? (strip.x0, strip.x1) : (strip.z0, strip.z1) };
        foreach (var blocker in blockers)
        {
            if (!strip.Overlaps(blocker)) continue;
            float b0 = alongX ? blocker.x0 : blocker.z0, b1 = alongX ? blocker.x1 : blocker.z1;
            var next = new List<(float a, float b)>();
            foreach (var p in pieces)
            {
                if (b1 <= p.a || b0 >= p.b) { next.Add(p); continue; }
                if (b0 > p.a) next.Add((p.a, b0));
                if (b1 < p.b) next.Add((b1, p.b));
            }
            pieces = next;
        }
        return pieces.Where(p => p.b - p.a >= MinPiece)
            .Select(p => alongX ? new Rect(p.a, strip.z0, p.b, strip.z1) : new Rect(strip.x0, p.a, strip.x1, p.b)).ToList();
    }

    static void Line(Context ctx, Transform parent, string name, Rect strip)
    {
        int index = 0;
        foreach (var piece in Clip(strip, ctx.blockers))
        {
            bool alongX = piece.x1 - piece.x0 >= piece.z1 - piece.z0;
            Quad(ctx, parent, name + "_" + index++, Cell.Line, piece.Centre, alongX ? Vector2.right : Vector2.up,
                alongX ? piece.x1 - piece.x0 : piece.z1 - piece.z0, alongX ? piece.z1 - piece.z0 : piece.x1 - piece.x0, 2);
        }
    }

    /// <summary>One flat overlay quad. u runs along dir, v across it.</summary>
    static void Quad(Context ctx, Transform parent, string name, Cell cell, Vector2 centre, Vector2 dir, float sizeU, float sizeV, int order)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room floor detail");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(centre.x, ctx.y, centre.y);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.right, new Vector3(dir.x, 0, dir.y));
        go.transform.localScale = new Vector3(sizeU, 1f, sizeV);
        go.AddComponent<MeshFilter>().sharedMesh = ctx.meshes[(int)cell];
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = IsHardware(cell) ? ctx.hardware : ctx.markings;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.sortingOrder = order;
    }

    // --- Assets ---------------------------------------------------------------------------------------------------

    static void EnsureFolders()
    {
        EnsureFolder(Folder + "/Meshes");
        EnsureFolder(Folder + "/Materials");
    }

    static void EnsureTextureImport()
    {
        foreach (var name in new[] { "Floor_Markings", "Floor_Hardware" })
        {
            string path = Folder + "/Textures/" + name + ".png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter ?? throw new InvalidOperationException("Missing floor atlas (run Tools/Art/generate_north_west_room_floor.py): " + path);
            bool dirty = importer.textureType != TextureImporterType.Default || !importer.sRGBTexture || !importer.alphaIsTransparency ||
                importer.wrapMode != TextureWrapMode.Clamp || importer.anisoLevel != 8 || !importer.mipmapEnabled || importer.maxTextureSize != AtlasSize;
            if (!dirty) continue;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = AtlasSize;
            importer.SaveAndReimport();
        }
    }

    static Mesh[] LoadMeshes()
    {
        var meshes = new Mesh[Enum.GetValues(typeof(Cell)).Length];
        foreach (Cell cell in Enum.GetValues(typeof(Cell)))
        {
            var (col, row) = CellAt(cell);
            float u0 = (col * CellSize + CellInset) / (float)AtlasSize, u1 = ((col + 1) * CellSize - CellInset) / (float)AtlasSize;
            float v1 = 1f - (row * CellSize + CellInset) / (float)AtlasSize, v0 = 1f - ((row + 1) * CellSize - CellInset) / (float)AtlasSize;
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) },
                uv = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            mesh.RecalculateBounds();
            meshes[(int)cell] = Save(mesh, "Meshes/SM_FloorOverlay_" + cell + ".asset");
        }
        return meshes;
    }

    static void LoadMaterials(Context ctx)
    {
        ctx.markings = TransparentMaterial("M_Floor_Markings", "Floor_Markings", 3000);
        ctx.hardware = TransparentMaterial("M_Floor_Hardware", "Floor_Hardware", 3001);
    }

    static Material TransparentMaterial(string name, string texture, int queue)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/" + texture + ".png") ?? throw new InvalidOperationException("Missing texture " + texture));
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", .12f);
        m.SetFloat("_Surface", 1);
        m.SetFloat("_Blend", 0);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.renderQueue = queue;
        return Save(m, "Materials/" + name + ".mat");
    }

    static T Save<T>(T generated, string relative) where T : Object
    {
        string path = Folder + "/" + relative;
        generated.name = Path.GetFileNameWithoutExtension(relative);
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
        else { AssetDatabase.CreateAsset(generated, path); existing = generated; }
        NorthWestRoomFramework.RequireOwnedAsset(existing, Section.FloorDetail);
        return existing;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Build North-West Room floor detail");
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Bounds ToBounds(NorthWestRoomSurvey.Box box) { var b = new Bounds(); b.SetMinMax(box.min, box.max); return b; }

    // --- Validation -----------------------------------------------------------------------------------------------

    static void RequireValid(NorthWestRoomFramework.Layout layout)
    {
        var scene = SceneManager.GetActiveScene();
        if (!NorthWestRoomFramework.Validate(scene, layout, true) ||
            !NorthWestRoomFramework.ValidateSection(scene, layout, Section.FloorDetail, true))
            throw new InvalidOperationException("North-West Room framework or floor section validation failed.");
        var section = NorthWestRoomFramework.FindSection(scene, Section.FloorDetail);
        var ctx = NewContext(layout, false);
        var errors = new List<string>();
        var names = section.Cast<Transform>().Select(t => t.name).ToArray();
        if (names.Length != Groups.Length || Groups.Any(g => !names.Contains(g))) errors.Add("Floor_Detail must contain exactly the groups: " + string.Join(", ", Groups));

        var report = layout.report;
        var room = layout.interior;
        var route = report.routes.Single(r => r.name == "Master Terminal to boss door inside");
        Vector2 routeDir = new Vector2(route.corners[route.corners.Length - 1].x - route.corners[0].x, route.corners[route.corners.Length - 1].z - route.corners[0].z).normalized;
        var plan = ctx.plan;
        int chevrons = 0, roundels = 0, drains = 0, grates = 0, trenches = 0, plates = 0, edges = 0;
        foreach (var renderer in section.GetComponentsInChildren<Renderer>(true))
        {
            string label = renderer.name;
            var b = renderer.bounds;
            var rect = Rect.Of(b);
            if (renderer.GetComponent<Collider>()) errors.Add(label + " has a collider.");
            if (renderer.shadowCastingMode != ShadowCastingMode.Off) errors.Add(label + " casts shadows.");
            if (b.min.y < layout.Floor + .005f || b.max.y > layout.Floor + .03f) errors.Add($"{label} is not 5-30 mm above the floor (y {b.min.y - layout.Floor:F3} to {b.max.y - layout.Floor:F3}).");
            bool chevron = label.StartsWith("Chevron_"), hardware = label.StartsWith("Grate_") || label.StartsWith("Drain_") || label.StartsWith("Roundel_");
            if (chevron) chevrons++; else if (label.StartsWith("Roundel_")) roundels++; else if (label.StartsWith("Drain_")) drains++; else if (label.StartsWith("Grate_")) grates++;
            else if (label.StartsWith("Trench_")) trenches++; else if (label.StartsWith("Plate_")) plates++; else if (label.StartsWith("Edge_") || label.StartsWith("Border_")) edges++;
            var terminalHit = ctx.terminals.Any(t => t.Overlaps(rect, .01f));
            if (terminalHit) errors.Add(label + " overlaps a terminal keep-out.");
            if (!chevron && ctx.blockers.Any(t => t.Overlaps(rect, .01f))) errors.Add(label + " overlaps a terminal or door-approach keep-out.");
            // Misleading markings: hardware and chevrons that sit inside machinery footprints (hidden or pointing at one).
            if (chevron || hardware)
                foreach (var fp in ctx.footprints)
                    if (fp.Overlaps(rect, .02f)) errors.Add(label + " lies inside a machinery bay footprint.");
            if (chevron)
            {
                Vector2 forward = new Vector2(renderer.transform.right.x, renderer.transform.right.z).normalized;
                if (Vector2.Dot(forward, routeDir) < .95f) errors.Add(label + " does not point along the progression lane toward Boss Hall Door.");
                Vector2 ahead = rect.Centre + forward * (ChevronLength * .5f + .6f);
                if (ahead.x < room.min.x || ahead.x > room.max.x || ahead.y < room.min.z || ahead.y > room.max.z || ctx.footprints.Any(f => f.x0 < ahead.x && ahead.x < f.x1 && f.z0 < ahead.y && ahead.y < f.z1))
                    errors.Add(label + " points into a wall or machinery bay.");
            }
        }
        foreach (var (space, x, z) in Roundels)
        {
            var poly = plan.openSpaces.Single(s => s.id == space).polygon.Select(p => new Vector2(p.x, p.z)).ToArray();
            var c = new Vector2(x, z);
            if (!InsidePolygon(c, poly) || EdgeDistance(c, poly) < RoundelSize * .5f - .15f)
                errors.Add($"Roundel {space} at ({x:F1}, {z:F1}) is not inside the plan's open space {space}.");
        }
        if (chevrons < 3) errors.Add($"Only {chevrons} chevrons; the progression lane needs at least 3.");
        if (roundels != Roundels.Length || drains != Roundels.Length + 1 || grates != PlateBays.Count || plates != PlateBays.Count || trenches < 2 || edges < 12)
            errors.Add($"Unexpected overlay counts: {roundels} roundels, {drains} drains, {grates} grates, {plates} plates, {trenches} trench pieces, {edges} edge/border pieces.");
        if (section.GetComponentsInChildren<Light>(true).Length != 0) errors.Add("Floor_Detail must carry no lights (corebuster_env_037).");
        if (section.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) errors.Add("Floor_Detail must be script-free.");
        var materials = section.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
        if (materials.Length != 2 || materials.Any(m => !AssetDatabase.GetAssetPath(m).StartsWith(Folder + "/"))) errors.Add($"Floor_Detail must use exactly its 2 own materials; it uses {materials.Length}.");
        if (errors.Count > 0) throw new InvalidOperationException("North-West floor detail checks failed:\n  " + string.Join("\n  ", errors));
        Debug.Log($"NORTH-WEST FLOOR DETAIL CHECKS PASSED: {section.GetComponentsInChildren<Renderer>(true).Length} overlays ({edges} edge/border, {plates} plates, {trenches} trench, {grates} grates, {drains} drains, {roundels} roundels, {chevrons} chevrons), no colliders, 2 materials.");
    }

    static bool InsidePolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
        return inside;
    }

    static float EdgeDistance(Vector2 p, Vector2[] poly)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], b = poly[i], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    static void VerifyIsolatedUndo(NorthWestRoomFramework.Layout layout)
    {
        var section = NorthWestRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.FloorDetail);
        int count = section.childCount;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        NorthWestRoomFramework.BeginSectionRebuild(layout, Section.FloorDetail, Owner);
        Undo.CollapseUndoOperations(group);
        if (section.childCount != 0) throw new InvalidOperationException("Floor section clear left content.");
        Undo.PerformUndo();
        if (section.childCount != count) throw new InvalidOperationException("Floor section Undo did not restore its groups.");
    }

    static Dictionary<string, string> SnapshotOutside()
    {
        var scene = SceneManager.GetActiveScene();
        var section = NorthWestRoomFramework.FindSection(scene, Section.FloorDetail);
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Where(t => !t.IsChildOf(section))
            .ToDictionary(t => GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), t =>
            {
                var renderer = t.GetComponent<Renderer>();
                string mats = renderer ? string.Join(",", renderer.sharedMaterials.Select(m => m ? AssetDatabase.GetAssetPath(m) : "null")) : "";
                return FormattableString.Invariant($"{t.name}|{(t.parent ? GlobalObjectId.GetGlobalObjectIdSlow(t.parent).ToString() : "root")}|{t.localPosition:R}|{t.localRotation:R}|{t.localScale:R}|{t.gameObject.activeSelf}|{mats}");
            });
    }
}
