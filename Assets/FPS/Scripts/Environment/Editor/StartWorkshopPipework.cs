using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static StartWorkshopDresser;

/// <summary>
/// Rebuildable corebuster_env_009 pass: one continuous heavy pipe network from the corebuster_art_001 kit. It rises
/// from a floor drop between the west shelving and pallets, runs along the north (exit) wall feeding two P-3 tanks
/// that flank the doorway, turns down the east wall and back along the south wall, and ends in a second floor drop.
/// Tees on the east and south runs add two more valve drops. Pieces are snapped port to port, every end joins another
/// piece, the floor or a tank, and each floor drop carries a valve wheel at hand height.
/// The pass runs after the corebuster_env_008 cladding: runs stand off the walls far enough to pass in front of its
/// columns, and support arms and floor props are only kept where they intersect no other pass's geometry. The
/// cladding pass ignores this group, so rebuilding either keeps the other valid. No name here contains "Wall"
/// (see <see cref="StartWorkshopFrame.Measure"/>).
/// </summary>
public static class StartWorkshopPipework
{
    public const string GroupName = "Pipework_009";
    const string UndoName = "Build Start Workshop Pipework";

    const string Kit = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/";
    const string StraightPrefab = Kit + "Prefabs/P_HeavyPipe_Straight_2m.prefab";
    const string ElbowPrefab = Kit + "Prefabs/P_HeavyPipe_Elbow_90.prefab";
    const string TeePrefab = Kit + "Prefabs/P_HeavyPipe_Tee.prefab";
    const string FlangePrefab = Kit + "Prefabs/P_HeavyPipe_Flange.prefab";
    const string ValvePrefab = Kit + "Prefabs/P_HeavyPipe_ValveMount.prefab";
    const string BracketPrefab = Kit + "Prefabs/P_HeavyPipe_WallBracket.prefab";
    const string TankPrefab = Kit + "Prefabs/P_PressureTank_P3.prefab";
    const string RedPaint = Kit + "Materials/M_HeavyPipe_RedOxide.mat";
    const string YellowPaint = Kit + "Materials/M_HeavyPipe_HazardYellow.mat";
    const string SteelFinish = Kit + "Materials/M_HeavyPipe_BareSteel.mat";

    // Floor drops are threaded through the gaps between these props (lower bound, upper bound along the wall).
    const string WestGapSouth = "Storage_WestWall/Shelf_B", WestGapNorth = "Storage_WestWall/Pallet_Stack_A";
    const string EastGapSouth = "Maintenance_EastWall_Detail/Power_Canister", EastGapNorth = "Maintenance_EastWall_Detail/Pallet_Stack_E";
    const string SouthGapWest = "Services_RearWall_Detail/Barrel_S", SouthGapEast = "Services_RearWall_Detail/Warning_Sign";
    const string CornerGapWest = "Services_RearWall_Detail/Pipe_Riser_West", CornerGapEast = "Services_RearWall/Pipes_Low";

    // Run axis above the floor: over the 5.2 m west shelving's reach and every wall prop the runs pass, and below
    // the corebuster_env_007 grid (5.8-7.2 m).
    const float RunHeight = 5.1f;
    // Run axis off the wall face: pipe (0.24 m radius) clears the 0.40 m cladding columns with a margin.
    const float Standoff = .75f;
    // The north run sits over the tanks' centres so each is fed straight down; the tanks clear the columns behind them.
    const float TankStandoff = 1.15f;
    const float DoorClearance = .4f;
    const float ValveHeight = 1.3f;
    const float SupportSpacing = 3.2f;
    const float ArmDepth = .12f, ArmWidth = .12f;
    const float BraceDrop = .7f, BraceReach = .7f;
    const float JoinTolerance = .015f;
    // Placement is stricter than the validator's 3 cm overlap allowance.
    const float Clearance = .005f;

    public static readonly string[] RequiredParts = { "Run_West", "Run_North", "Run_East", "Run_South", "Tank_P3_West", "Tank_P3_East" };

    [MenuItem("Tools/CoreBuster/Build Start Workshop Pipework")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; the pipework needs {LevelName}/{ContainerName}.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        var old = root.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var frame = StartWorkshopFrame.Measure(scene, root);
        int firstChild = root.childCount;
        try
        {
            Build(root, frame);
            ApplyStaticConvention(root.GetChild(firstChild), GameObjectUtility.GetStaticEditorFlags(level.gameObject));
        }
        catch
        {
            if (root.childCount > firstChild) UnityEngine.Object.DestroyImmediate(root.GetChild(firstChild).gameObject);
            throw;
        }
        Undo.RegisterCreatedObjectUndo(root.GetChild(firstChild).gameObject, UndoName);
        StartWorkshopLitMaterials.ApplyTo(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        Debug.Log($"Start Workshop pipework rebuilt. {frame}");
        StartWorkshopChecks.Run();
    }

    /// <summary>Called by the full workshop rebuild after the cladding pass, and by the isolated menu action.</summary>
    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null)
            throw new InvalidOperationException($"{GroupName} already exists. Remove it before rebuilding the pipework pass.");

        var kit = new KitParts();
        var group = Group(root, GroupName);
        float y = f.Floor + RunHeight;
        float xW = f.West + Standoff, xE = f.East - Standoff, zS = f.South + Standoff, zN = f.North - TankStandoff;
        float teeDropTop = y - kit.TeeBranch.x, elbowDropTop = y - kit.Bend;

        // Floor-standing pieces first, so the runs' tees and supports can be placed around them.
        var (lo, hi) = Gap(root, WestGapSouth, WestGapNorth, alongZ: true);
        float zWestDrop = Place("Drop_West", lo, hi, (lo + hi) * .5f, root, f,
            z => Drop(group, "Drop_West", new Vector3(xW, elbowDropTop, z), Vector3.right, f, kit));
        (lo, hi) = Gap(root, EastGapSouth, EastGapNorth, alongZ: true);
        float zEastDrop = Place("Drop_East", lo, hi, (lo + hi) * .5f, root, f,
            z => Drop(group, "Drop_East", new Vector3(xE, teeDropTop, z), Vector3.left, f, kit));
        (lo, hi) = Gap(root, SouthGapWest, SouthGapEast, alongZ: false);
        float xSouthDrop = Place("Drop_South", lo, hi, (lo + hi) * .5f, root, f,
            x => Drop(group, "Drop_South", new Vector3(x, teeDropTop, zS), Vector3.forward, f, kit));
        (lo, hi) = Gap(root, CornerGapWest, CornerGapEast, alongZ: false);
        float xCornerDrop = Place("Drop_SouthWest", lo, hi, (lo + hi) * .5f, root, f,
            x => Drop(group, "Drop_SouthWest", new Vector3(x, elbowDropTop, zS), Vector3.forward, f, kit));

        // The tanks flank the exit, just outside the doorway approach keep-out, and are fed from the north run.
        float westTank = f.OpeningMinX - 1f - kit.TankRadius - DoorClearance;
        float xWestTank = Place("Tank_P3_West", westTank - 3f, westTank, westTank, root, f,
            x => TankUnit(group, "Tank_P3_West", new Vector3(x, f.Floor, zN), Vector3.back, teeDropTop, kit));
        float eastTank = f.OpeningMaxX + 1f + kit.TankRadius + DoorClearance;
        float xEastTank = Place("Tank_P3_East", eastTank, eastTank + 3f, eastTank, root, f,
            x => TankUnit(group, "Tank_P3_East", new Vector3(x, f.Floor, zN), Vector3.back, teeDropTop, kit));

        // One continuous network, clockwise from the west drop: west, north, east and south runs.
        var run = Run(group, "Run_West", new Vector3(xW, elbowDropTop, zWestDrop), Vector3.up, kit.Red, kit);
        run.Elbow(Vector3.forward);
        Vector3 from = run.Position;
        run.StraightTo(zN - kit.Bend);
        Supports(run, root, from, run.Position, Vector3.right, brace: false, kit, null);
        run.Elbow(Vector3.right);

        run = Run(group, "Run_North", run.Position, run.Direction, kit.Red, kit);
        from = run.Position;
        run.StraightTo(xWestTank - kit.TeeBranch.z);
        run.Tee(Vector3.down);
        run.StraightTo(xEastTank - kit.TeeBranch.z);
        run.Tee(Vector3.down);
        run.StraightTo(xE - kit.Bend);
        Supports(run, root, from, run.Position, Vector3.back, brace: true, kit, new[] { xWestTank, xEastTank });
        run.Elbow(Vector3.back);

        run = Run(group, "Run_East", run.Position, run.Direction, kit.Red, kit);
        from = run.Position;
        run.StraightTo(zEastDrop + kit.TeeBranch.z);
        run.Tee(Vector3.down);
        run.StraightTo(zS + kit.Bend);
        Supports(run, root, from, run.Position, Vector3.left, brace: false, kit, new[] { zEastDrop });
        run.Elbow(Vector3.left);

        run = Run(group, "Run_South", run.Position, run.Direction, kit.Red, kit);
        from = run.Position;
        run.StraightTo(xSouthDrop + kit.TeeBranch.z);
        run.Tee(Vector3.down);
        run.StraightTo(xCornerDrop + kit.Bend);
        Supports(run, root, from, run.Position, Vector3.forward, brace: false, kit, new[] { xSouthDrop });
        run.Elbow(Vector3.down);

        if ((run.Position - new Vector3(xCornerDrop, elbowDropTop, zS)).sqrMagnitude > JoinTolerance * JoinTolerance)
            throw new InvalidOperationException($"The south run ended at {run.Position:F3}, not on Drop_SouthWest; the kit's port layout changed.");
        Debug.Log(FormattableString.Invariant(
            $"{GroupName}: runs at {y:F2} m; drops W z={zWestDrop:F2}, E z={zEastDrop:F2}, S x={xSouthDrop:F2}, SW x={xCornerDrop:F2}; ") +
            FormattableString.Invariant($"tanks x={xWestTank:F2} and {xEastTank:F2} at z={zN:F2}."));
    }

    /// <summary>A vertical floor drop hanging from <paramref name="top"/>: pipe, a valve at hand height facing into the
    /// room, pipe to the floor and the kit bracket laid flat as a floor collar.</summary>
    static GameObject Drop(Transform parent, string name, Vector3 top, Vector3 inward, StartWorkshopFrame f, KitParts kit)
    {
        var drop = new GameObject(name).transform;
        drop.SetParent(parent, false);
        var chain = new Chain(drop, top, Vector3.down, kit.Yellow, kit);
        chain.StraightTo(f.Floor + ValveHeight + kit.ValveLength * .5f, inward);
        chain.Valve(inward);
        chain.StraightTo(f.Floor, inward);
        var collar = Instance(drop, BracketPrefab, "Floor_Collar");
        collar.transform.SetPositionAndRotation(new Vector3(top.x, f.Floor, top.z), Orient(Vector3.up, inward));
        return drop.gameObject;
    }

    /// <summary>A P-3 tank with its label facing into the room, fed from above through a flanged inlet.</summary>
    static GameObject TankUnit(Transform parent, string name, Vector3 floorPoint, Vector3 inward, float feedTop, KitParts kit)
    {
        var unit = new GameObject(name).transform;
        unit.SetParent(parent, false);
        var tank = Instance(unit, TankPrefab, "Tank");
        tank.transform.SetPositionAndRotation(floorPoint, Quaternion.LookRotation(inward));
        var body = tank.transform.Find("Tank_Body");
        if (body == null || tank.transform.Find("P3_ID") == null)
            throw new InvalidOperationException($"{TankPrefab} has no Tank_Body or P3_ID; rebuild the Heavy Industrial Kit.");
        float tankTop = B(body.gameObject).max.y;
        var chain = new Chain(unit, new Vector3(floorPoint.x, feedTop, floorPoint.z), Vector3.down, kit.Yellow, kit);
        chain.StraightTo(tankTop + kit.FlangeLength, inward);
        chain.Flange(inward);
        return unit.gameObject;
    }

    static Chain Run(Transform parent, string name, Vector3 start, Vector3 direction, Material paint, KitParts kit)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return new Chain(t, start, direction, paint, kit);
    }

    /// <summary>
    /// Cantilever arms from the real wall surface to under a straight run, about every <see cref="SupportSpacing"/>
    /// metres. Each arm slides along the run until it clears every other pass's geometry; tees stay unsupported so an
    /// arm never cuts through a branch.
    /// </summary>
    static void Supports(Chain run, Transform root, Vector3 from, Vector3 to, Vector3 inward, bool brace, KitParts kit, float[] tees)
    {
        Vector3 axis = to - from;
        float length = axis.magnitude;
        if (length < 1.5f) return;
        axis /= length;
        var obstacles = Obstacles(root, run.Root);
        int count = Mathf.Max(2, Mathf.RoundToInt(length / SupportSpacing) + 1);
        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            float target = Mathf.Lerp(.6f, length - .6f, i / (count - 1f));
            for (int step = 0; step < 13; step++)
            {
                float along = target + Offset(step, .3f);
                if (along < .4f || along > length - .4f) continue;
                Vector3 p = from + axis * along;
                float coordinate = Vector3.Dot(p, Abs(axis));
                if (tees != null && Array.Exists(tees, t => Mathf.Abs(coordinate - t) < .9f)) continue;
                var support = Support(run.Root, $"Support_{placed + 1}", p, inward, axis, brace, root, kit);
                if (support == null) continue;
                if (Collision(support, obstacles) == null) { placed++; break; }
                UnityEngine.Object.DestroyImmediate(support);
            }
        }
        if (placed < 2) throw new InvalidOperationException($"{run.Root.name} found fewer than two clear support positions; it would hang unsupported.");
    }

    // Runs are axis-aligned; tee and target positions are world coordinates on that axis, whichever way the run heads.
    static float Sign(Vector3 axis) => axis.x + axis.y + axis.z;
    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static Vector3 Snap(Vector3 v)
    {
        Vector3 a = Abs(v);
        if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0, 0);
        return a.y >= a.z ? new Vector3(0, Mathf.Sign(v.y), 0) : new Vector3(0, 0, Mathf.Sign(v.z));
    }

    /// <summary>An arm under the pipe from the wall collider, a base plate, clamp cheeks and optionally a diagonal brace.</summary>
    static GameObject Support(Transform parent, string name, Vector3 pipe, Vector3 inward, Vector3 along, bool brace, Transform exclude, KitParts kit)
    {
        Physics.SyncTransforms();
        float armY = pipe.y - kit.PipeRadius - ArmDepth * .5f;
        Vector3 origin = new Vector3(pipe.x, armY, pipe.z);
        if (!StartWorkshopFrame.Blocked(origin, -inward, TankStandoff + 1.5f, exclude, out RaycastHit hit)) return null;
        var support = new GameObject(name);
        support.transform.SetParent(parent, false);
        Vector3 wall = new Vector3(hit.point.x, armY, hit.point.z);
        float reach = hit.distance + kit.PipeRadius * .6f;
        Quaternion facing = Orient(inward, along);
        Box(support.transform, "Arm", wall + inward * (reach * .5f), facing, new Vector3(ArmWidth, ArmDepth, reach), kit.Steel);
        Box(support.transform, "Base_Plate", wall + inward * .015f, facing, new Vector3(.34f, .34f, .03f), kit.Steel);
        float cheekHeight = kit.PipeRadius + .06f;
        float cheekY = pipe.y - kit.PipeRadius + cheekHeight * .5f - .02f;
        foreach (float side in new[] { -1f, 1f })
            Box(support.transform, side < 0 ? "Clamp_Near" : "Clamp_Far",
                new Vector3(pipe.x, cheekY, pipe.z) + inward * (side * (kit.PipeRadius + .025f)), facing,
                new Vector3(.1f, cheekHeight, .05f), kit.Steel);
        if (brace)
        {
            Vector3 low = wall + Vector3.down * (ArmDepth * .5f + BraceDrop);
            Vector3 high = wall + Vector3.down * (ArmDepth * .5f) + inward * BraceReach;
            Vector3 d = high - low;
            Box(support.transform, "Brace", (low + high) * .5f, Orient(d.normalized, along), new Vector3(.08f, .08f, d.magnitude), kit.Steel);
        }
        return support;
    }

    static void Box(Transform parent, string name, Vector3 center, Quaternion rotation, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, rotation);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>Tries <paramref name="build"/> at the preferred coordinate, then alternately either side of it in 10 cm
    /// steps within [lo, hi], keeping the first result clear of the keep-outs and every other prop.</summary>
    static float Place(string what, float lo, float hi, float preferred, Transform root, StartWorkshopFrame f, Func<float, GameObject> build)
    {
        string last = "no candidate fitted the gap";
        for (int step = 0; step < 80; step++)
        {
            float value = preferred + Offset(step, .1f);
            if (value < lo - .001f || value > hi + .001f) continue;
            var go = build(value);
            Physics.SyncTransforms();
            Bounds b = B(go);
            string problem = f.KeepOutViolation(b);
            if (problem == null && (b.min.x < f.West || b.max.x > f.East || b.min.z < f.South || b.max.z > f.North))
                problem = "outside the room";
            if (problem == null) problem = Collision(go, Obstacles(root, go.transform));
            if (problem == null) return value;
            last = problem;
            UnityEngine.Object.DestroyImmediate(go);
        }
        throw new InvalidOperationException(FormattableString.Invariant(
            $"No clear position for {GroupName}/{what} between {lo:F2} and {hi:F2} (last: {last}). Re-run the earlier passes or move their props."));
    }

    // 0, -s, +s, -2s, +2s, ...
    static float Offset(int step, float size) => (step + 1) / 2 * size * (step % 2 == 1 ? -1 : 1);

    static (float lo, float hi) Gap(Transform root, string lowerPath, string upperPath, bool alongZ)
    {
        Bounds a = B(Anchor(root, lowerPath)), b = B(Anchor(root, upperPath));
        float lo = alongZ ? a.max.z : a.max.x, hi = alongZ ? b.min.z : b.min.x;
        if (hi - lo < 1f)
            throw new InvalidOperationException(FormattableString.Invariant(
                $"The gap between '{lowerPath}' and '{upperPath}' is {hi - lo:F2} m; a floor drop needs at least 1 m."));
        return (lo, hi);
    }

    static GameObject Anchor(Transform root, string path)
    {
        var t = root.Find(path);
        if (t == null)
            throw new InvalidOperationException($"Anchor prop '{path}' is missing; run Tools/CoreBuster/Dress Start Workshop before the pipework pass.");
        return t.gameObject;
    }

    static List<(Transform t, Bounds b)> Obstacles(Transform root, Transform self)
    {
        var list = new List<(Transform, Bounds)>();
        // The corebuster_render_007 liners are wall surface, 1 cm in front of the kept panels the brackets mount on.
        // The corebuster_render_006 lamps are the room's last layer and fit themselves around this pass.
        var liners = root.Find(StartWorkshopLiners.GroupName);
        var lighting = root.Find(StartWorkshopLighting.GroupName);
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && !r.transform.IsChildOf(self) &&
                (liners == null || !r.transform.IsChildOf(liners)) && (lighting == null || !r.transform.IsChildOf(lighting)))
                list.Add((r.transform, r.bounds));
        return list;
    }

    static string Collision(GameObject go, List<(Transform t, Bounds b)> obstacles)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            foreach (var (t, b) in obstacles)
                if (Overlaps(r.bounds, b)) return $"{r.name} intersects {t.name}";
        return null;
    }

    static bool Overlaps(Bounds a, Bounds b) =>
        Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > Clearance &&
        Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > Clearance &&
        Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z) > Clearance;

    /// <summary>Rotation whose local +Z is <paramref name="forward"/> and local +X is <paramref name="side"/> (the
    /// kit's bend, branch and valve-wheel direction).</summary>
    static Quaternion Orient(Vector3 forward, Vector3 side) => Quaternion.LookRotation(forward, Vector3.Cross(forward, side));

    static Vector3 AnySide(Vector3 forward) => Mathf.Abs(forward.y) > .9f ? Vector3.right : Vector3.up;

    static GameObject Instance(Transform parent, string path, string name)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) throw new InvalidOperationException($"Missing prefab {path}; run Tools/CoreBuster/Build Heavy Industrial Kit.");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.name = name;
        return go;
    }

    /// <summary>
    /// Explicit corebuster_env_009 acceptance rule: the network parts exist, each floor drop has a valve at hand
    /// height, each tank keeps its P-3 label, and every pipe port joins another port or ends at the floor or in a tank.
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add($"missing {GroupName}; run Tools/CoreBuster/Build Start Workshop Pipework.");
            return;
        }
        foreach (string part in RequiredParts)
            if (group.Find(part) == null) errors.Add($"{GroupName} is missing {part}.");

        int drops = 0;
        var tanks = new List<Bounds>();
        foreach (Transform child in group)
        {
            if (child.name.StartsWith("Tank_P3_"))
            {
                var tank = child.Find("Tank");
                if (tank == null || tank.Find("P3_ID") == null) errors.Add($"{GroupName}/{child.name} has no P-3 label.");
                else tanks.Add(B(tank.gameObject));
            }
            if (!child.name.StartsWith("Drop_")) continue;
            drops++;
            Transform valve = null;
            foreach (Transform piece in child) if (piece.name.StartsWith("Valve")) valve = piece;
            float height = valve != null ? B(valve.gameObject).center.y - f.Floor : -1f;
            if (height < 1f || height > 1.7f)
                errors.Add($"{GroupName}/{child.name} needs a valve wheel at hand height (1.0-1.7 m); found {height:F2} m.");
        }
        if (drops < 3 || drops > 5) errors.Add($"{GroupName} has {drops} floor drops; corebuster_env_009 asks for 3-5.");

        var ports = new List<Transform>();
        foreach (var t in group.GetComponentsInChildren<Transform>())
            if ((t.name == "Port_A" || t.name == "Port_B" || t.name == "Port_C") && !t.parent.name.StartsWith("Collar"))
                ports.Add(t);
        foreach (var port in ports)
        {
            Vector3 p = port.position;
            bool joined = ports.Exists(o => o != port && (o.position - p).sqrMagnitude < JoinTolerance * JoinTolerance);
            bool floor = Mathf.Abs(p.y - f.Floor) < .02f;
            bool tank = tanks.Exists(b => { b.Expand(.1f); return b.Contains(p); });
            if (!joined && !floor && !tank)
                errors.Add($"{GroupName}/{port.parent.parent.name}/{port.parent.name}/{port.name} is an open pipe end at {p:F2}.");
        }
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    /// <summary>The kit's port geometry, read from the prefabs so this pass follows any kit rebuild.</summary>
    sealed class KitParts
    {
        public readonly Material Red = LoadMaterial(RedPaint), Yellow = LoadMaterial(YellowPaint), Steel = LoadMaterial(SteelFinish);
        public readonly float StraightLength = Port(StraightPrefab, "Port_B").z;
        public readonly float Bend = Port(ElbowPrefab, "Port_B").z;
        public readonly Vector3 TeeBranch = Port(TeePrefab, "Port_C");
        public readonly float ValveLength = Port(ValvePrefab, "Port_B").z;
        public readonly float FlangeLength = Port(FlangePrefab, "Port_B").z;
        public readonly float PipeRadius;
        public readonly float TankRadius;

        public KitParts()
        {
            var straight = AssetDatabase.LoadAssetAtPath<GameObject>(StraightPrefab);
            PipeRadius = straight.GetComponent<MeshFilter>().sharedMesh.bounds.extents.x;
            var tank = AssetDatabase.LoadAssetAtPath<GameObject>(TankPrefab);
            if (tank == null) throw new InvalidOperationException($"Missing prefab {TankPrefab}; run Tools/CoreBuster/Build Heavy Industrial Kit.");
            float radius = 0;
            foreach (var filter in tank.GetComponentsInChildren<MeshFilter>())
                radius = Mathf.Max(radius, filter.sharedMesh.bounds.extents.x, filter.sharedMesh.bounds.extents.z);
            TankRadius = radius;
        }

        static Vector3 Port(string path, string port)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var t = asset != null ? asset.transform.Find(port) : null;
            if (t == null) throw new InvalidOperationException($"Missing {port} on {path}; run Tools/CoreBuster/Build Heavy Industrial Kit.");
            return t.localPosition;
        }

        static Material LoadMaterial(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) throw new InvalidOperationException($"Missing material {path}; run Tools/CoreBuster/Build Heavy Industrial Kit.");
            return m;
        }
    }

    /// <summary>Places kit pieces port to port: each piece's Port_A goes on the current end, and its Port_B becomes the
    /// new end. Flange collars (excluded from the open-end check) dress each joint inside the chain.</summary>
    sealed class Chain
    {
        public readonly Transform Root;
        public Vector3 Position { get; private set; }
        public Vector3 Direction { get; private set; }
        readonly Material m_Paint;
        readonly KitParts m_Kit;
        int m_Pieces, m_Collars;

        public Chain(Transform root, Vector3 start, Vector3 direction, Material paint, KitParts kit)
        {
            Root = root;
            Position = start;
            Direction = direction;
            m_Paint = paint;
            m_Kit = kit;
        }

        /// <summary>Straight pipe until the end reaches <paramref name="target"/> along the current direction's axis.</summary>
        public void StraightTo(float target, Vector3? side = null)
        {
            float length = target * Sign(Direction) - Vector3.Dot(Position, Direction);
            if (length < -.001f)
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"{Root.name} cannot reach {target:F2}: its end at {Position:F2} is already {-length:F2} m past it."));
            if (length < .01f) return;
            int pieces = Mathf.Max(1, Mathf.CeilToInt(length / m_Kit.StraightLength - .01f));
            for (int i = 0; i < pieces; i++)
            {
                var piece = Add(StraightPrefab, "Straight", side ?? AnySide(Direction), m_Paint);
                var scale = piece.transform.localScale;
                scale.z *= length / pieces / m_Kit.StraightLength;
                piece.transform.localScale = scale;
                Follow(piece, "Port_B");
            }
        }

        public void Elbow(Vector3 turn) { Follow(Add(ElbowPrefab, "Elbow", turn, null), "Port_B"); }
        public void Tee(Vector3 branch) { Follow(Add(TeePrefab, "Tee", branch, null), "Port_B"); }
        public void Valve(Vector3 wheel) { Follow(Add(ValvePrefab, "Valve", wheel, null), "Port_B"); }
        public void Flange(Vector3 side) { Follow(Add(FlangePrefab, "Inlet_Flange", side, null), "Port_B"); }

        GameObject Add(string path, string name, Vector3 side, Material paint)
        {
            if (m_Pieces > 0) Collar();
            var go = Instance(Root, path, $"{name}_{++m_Pieces}");
            go.transform.SetPositionAndRotation(Position, Orient(Direction, side));
            if (paint != null)
            {
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = paint;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            return go;
        }

        /// <summary>A kit flange centred on the joint between two pieces of this chain.</summary>
        void Collar()
        {
            var collar = Instance(Root, FlangePrefab, $"Collar_{++m_Collars}");
            collar.transform.SetPositionAndRotation(Position - Direction * (m_Kit.FlangeLength * .5f), Orient(Direction, AnySide(Direction)));
        }

        void Follow(GameObject piece, string port)
        {
            var end = piece.transform.Find(port);
            Position = end.position;
            Direction = Snap(end.forward);
        }
    }
}
