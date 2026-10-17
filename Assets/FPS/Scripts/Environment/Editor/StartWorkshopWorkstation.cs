using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static StartWorkshopDresser;

/// <summary>
/// Rebuildable corebuster_env_010 pass: the hero workstation around the steel workbench that
/// <see cref="StartWorkshopDresser"/> builds on the east wall. It adds a tool pegboard with a fluorescent task-lamp
/// fixture on its top frame, a red rolling tool chest at the bench's north end, parts trays on the bench and chest,
/// and the corebuster_art_002 robot arm on a hazard-edged plinth at the south end, turned so its gripper works over
/// the bench top. Everything is first-principles (primitives in project-owned finishes, plus the pegboard texture from
/// Tools/Art/generate_start_workshop_graphics.py). It anchors to the bench and the props either side of it, never
/// moves them, and fails with the anchor's name when one is missing. The lamp is a mesh with an emissive tube; its real
/// light is <see cref="StartWorkshopLighting"/>'s task light (corebuster_render_006). No name here contains "Wall" (see
/// <see cref="StartWorkshopFrame.Measure"/>).
/// </summary>
public static class StartWorkshopWorkstation
{
    public const string GroupName = "Workstation_010";
    const string UndoName = "Build Start Workshop Workstation";

    public const string BenchPath = "Maintenance_EastWall/Workbench";
    public const string BenchTopPath = BenchPath + "/Bench_Top";
    // Present only on the corebuster_env_010 steel bench, not on the corebuster_env_002 crate bench it replaced.
    const string BenchMarkerPath = BenchPath + "/Pedestal_South/Drawer_1";
    const string BenchPartsPath = "Maintenance_EastWall_Detail/Bench_Parts";
    const string CartPath = "Maintenance_EastWall/Maintenance_Cart";
    const string CornerStackPath = "Services_RearWall_Detail/Crate_Stack_SE";

    const string ArmPrefab = "Assets/FPS/Art/StartWorkshop/RoboticArm/Prefabs/P_RoboticArm.prefab";
    const string ToolPointPath = "J1_Turret/J2_Shoulder/J3_Elbow/J4_Wrist/J5_WristRoll/Tool_Point";
    const string ArmPath = "Robot_Arm_Cell/Robot_Arm";
    const string TrayPrefix = "Parts_Tray_";
    const string ChestTray = TrayPrefix + "C";
    public static readonly string[] RequiredParts =
        { "Pegboard", "Task_Lamp", "Tool_Chest", TrayPrefix + "A", TrayPrefix + "B", ChestTray, "Robot_Arm_Cell" };

    // Pegboard: 2.8 x 1.1 m (the texture's aspect), its frame starting just above the bench backsplash.
    const float PegboardLength = 2.8f, PegboardHeight = 1.1f, PegboardAboveTop = .2f, PegboardDepth = .025f;
    const float FrameBar = .04f, FrameDepth = .06f;
    // Task lamp: a steel housing on two brackets from the pegboard's top frame, reaching out over the bench.
    const float LampLength = 1.6f, LampReach = .42f, LampNorthOffset = .25f, HousingDepth = .26f, HousingHeight = .07f;
    const float TubeRadius = .026f;
    // Tool chest: 0.76 m wide and 0.5 m deep on 0.12 m casters, rolled off the wall and turned toward the room.
    const float ChestWidth = .76f, ChestDepth = .5f, DrawerSection = .78f, LidHeight = .12f, CasterHeight = .12f;
    const float ChestBenchGap = .35f, ChestWallGap = .12f, ChestYaw = 14f;
    static readonly float[] ChestDrawers = { .07f, .08f, .1f, .12f, .14f, .15f };
    // Parts trays: 0.32 x 0.45 m yellow steel trays with two bins.
    const float TrayDepth = .32f, TrayLength = .45f, TrayHeight = .065f, TrayWall = .008f;
    // Arm cell: a 1.4 m plinth; the gripper's tool point hovers over the bench centre line this far in from its
    // south end, above the corebuster_env_004 gears.
    const float PlinthSize = 1.4f, PlinthHeight = .06f, HazardEdgeWidth = .08f, ArmTargetInset = .42f;
    // Kept free between the plinth and the corner stack for the corebuster_env_012 railing.
    const float RailingClearance = .6f;
    // Placement is stricter than the validator's 3 cm overlap allowance.
    const float Clearance = .005f;
    const float Rest = .02f;

    [MenuItem("Tools/CoreBuster/Build Start Workshop Workstation")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; the workstation needs {LevelName}/{ContainerName}.");
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
        Debug.Log($"Start Workshop workstation rebuilt. {frame}");
        StartWorkshopChecks.Run();
    }

    /// <summary>Called by the full workshop rebuild after the detail pass, and by the isolated menu action.</summary>
    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null)
            throw new InvalidOperationException($"{GroupName} already exists. Remove it before rebuilding the workstation pass.");
        Anchor(root, BenchTopPath);
        if (root.Find(BenchMarkerPath) == null)
            throw new InvalidOperationException($"'{BenchPath}' is still the corebuster_env_002 crate bench. Run Tools/CoreBuster/Dress Start Workshop to rebuild the first pass with the steel workbench.");
        Bounds bench = B(Anchor(root, BenchPath)), top = B(Anchor(root, BenchTopPath));
        Bounds cart = B(Anchor(root, CartPath)), corner = B(Anchor(root, CornerStackPath));

        var finish = new Finishes();
        var group = Group(root, GroupName);
        var pegboard = BuildPegboard(group, root, f, top, finish);
        BuildLamp(group, B(pegboard), top, finish);
        var chest = BuildToolChest(group, f, bench, cart, finish);

        float trayX = top.max.x - .06f - TrayDepth * .5f;
        float trayB = top.max.z - .1f - TrayLength * .5f;
        BuildTray(group, TrayPrefix + "A", new Vector3(trayX, top.max.y, trayB - TrayLength - .06f), 0, finish);
        BuildTray(group, TrayPrefix + "B", new Vector3(trayX, top.max.y, trayB), 0, finish);
        Bounds lid = B(chest.transform.Find("Lid_Mat").gameObject);
        BuildTray(group, ChestTray, new Vector3(lid.center.x, lid.max.y, lid.center.z), ChestYaw, finish);

        BuildArmCell(group, f, top, corner, finish);
        CheckClear(root, group);
    }

    /// <summary>A painted-steel backing and steel frame holding the tool-board face, mounted on the real wall.</summary>
    static GameObject BuildPegboard(Transform group, Transform root, StartWorkshopFrame f, Bounds top, Finishes m)
    {
        var board = new GameObject("Pegboard").transform;
        board.SetParent(group, false);
        float y0 = top.max.y + PegboardAboveTop + FrameBar, yc = y0 + PegboardHeight * .5f, z = top.center.z;
        float wall = f.East, frameX = wall - FrameDepth * .5f;
        board.position = new Vector3(wall, y0, z);
        Block(board, "Backing", new Vector3(wall - PegboardDepth * .5f, yc, z), new Vector3(PegboardDepth, PegboardHeight, PegboardLength), m.PaintedSteel);
        // A Quad is visible from its local -Z side: facing +X puts that side toward the room, texture unmirrored.
        var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
        face.name = "Tool_Board";
        face.transform.SetParent(board, false);
        face.transform.SetPositionAndRotation(new Vector3(wall - PegboardDepth - .002f, yc, z), Quaternion.LookRotation(Vector3.right));
        face.transform.localScale = new Vector3(PegboardLength, PegboardHeight, 1);
        UnityEngine.Object.DestroyImmediate(face.GetComponent<Collider>());
        face.GetComponent<MeshRenderer>().sharedMaterial = m.Pegboard;

        float span = PegboardLength + 2 * FrameBar;
        Block(board, "Frame_Top", new Vector3(frameX, y0 + PegboardHeight + FrameBar * .5f, z), new Vector3(FrameDepth, FrameBar, span), m.BareSteel);
        Block(board, "Frame_Bottom", new Vector3(frameX, y0 - FrameBar * .5f, z), new Vector3(FrameDepth, FrameBar, span), m.BareSteel);
        foreach (float side in new[] { -1f, 1f })
            Block(board, side < 0 ? "Frame_South" : "Frame_North",
                new Vector3(frameX, yc, z + side * (PegboardLength + FrameBar) * .5f), new Vector3(FrameDepth, PegboardHeight, FrameBar), m.BareSteel);
        Mount(board.gameObject, root, Wall.East);
        return board.gameObject;
    }

    /// <summary>A fluorescent fixture resting on the pegboard's top frame: two brackets, a housing, end caps and one
    /// emissive tube hanging under the housing.</summary>
    static void BuildLamp(Transform group, Bounds pegboard, Bounds top, Finishes m)
    {
        var lamp = new GameObject("Task_Lamp").transform;
        lamp.SetParent(group, false);
        float wall = pegboard.max.x, y = pegboard.max.y, z = top.center.z + LampNorthOffset;
        lamp.position = new Vector3(wall, y, z);
        foreach (float side in new[] { -1f, 1f })
            Block(lamp, side < 0 ? "Bracket_South" : "Bracket_North", new Vector3(wall - LampReach * .5f, y + .02f, z + side * (LampLength * .5f - .15f)),
                new Vector3(LampReach, .04f, .05f), m.BareSteel, collider: false);
        float housingX = wall - LampReach + HousingDepth * .5f, housingBottom = y + .04f;
        Block(lamp, "Housing", new Vector3(housingX, housingBottom + HousingHeight * .5f, z), new Vector3(HousingDepth, HousingHeight, LampLength), m.PaintedSteel);
        float capHeight = HousingHeight + 2 * TubeRadius + .02f;
        foreach (float side in new[] { -1f, 1f })
            Block(lamp, side < 0 ? "End_Cap_South" : "End_Cap_North",
                new Vector3(housingX, housingBottom + HousingHeight - capHeight * .5f, z + side * (LampLength * .5f + .012f)),
                new Vector3(HousingDepth + .02f, capHeight, .024f), m.Rubber, collider: false);
        Rod(lamp, "Tube", new Vector3(housingX, housingBottom - TubeRadius - .006f, z), Vector3.forward, TubeRadius, LampLength - .02f, m.Tube);
    }

    /// <summary>A red rolling tool chest: drawer body with a dark face, bar handles, lid with a rubber mat, a side push
    /// handle and four casters, turned from the wall toward the room.</summary>
    static GameObject BuildToolChest(Transform group, StartWorkshopFrame f, Bounds bench, Bounds cart, Finishes m)
    {
        var chest = new GameObject("Tool_Chest").transform;
        chest.SetParent(group, false);
        float x = f.East - WallGap - ChestWallGap - ChestDepth * .5f, z = bench.max.z + ChestBenchGap + ChestWidth * .5f;
        chest.position = new Vector3(x, f.Floor, z);
        float y0 = f.Floor + CasterHeight, lidTop = y0 + DrawerSection + LidHeight, front = x - ChestDepth * .5f + .02f;

        Block(chest, "Body", new Vector3(x + .01f, y0 + DrawerSection * .5f, z), new Vector3(ChestDepth - .02f, DrawerSection, ChestWidth), m.Red);
        Block(chest, "Face", new Vector3(front - .002f, y0 + DrawerSection * .5f, z), new Vector3(.004f, DrawerSection - .01f, ChestWidth - .04f), m.Rubber, collider: false);
        float y = y0 + DrawerSection - .012f;
        for (int i = 0; i < ChestDrawers.Length; i++)
        {
            float h = ChestDrawers[i];
            Block(chest, $"Drawer_{i + 1}", new Vector3(front - .012f, y - h * .5f, z), new Vector3(.016f, h, ChestWidth - .05f), m.Red, collider: false);
            Block(chest, $"Handle_{i + 1}", new Vector3(front - .03f, y - .025f, z), new Vector3(.02f, .014f, ChestWidth * .72f), m.BareSteel, collider: false);
            y -= h + .012f;
        }
        Block(chest, "Lid", new Vector3(x, y0 + DrawerSection + LidHeight * .5f, z), new Vector3(ChestDepth + .02f, LidHeight, ChestWidth + .02f), m.Red);
        Block(chest, "Lid_Handle", new Vector3(x - ChestDepth * .5f - .02f, y0 + DrawerSection + LidHeight * .5f, z), new Vector3(.02f, .016f, ChestWidth * .5f), m.BareSteel, collider: false);
        Block(chest, "Lid_Mat", new Vector3(x, lidTop + .003f, z), new Vector3(ChestDepth - .02f, .006f, ChestWidth - .04f), m.Rubber, collider: false);

        float handleY = y0 + DrawerSection - .06f, handleZ = z + ChestWidth * .5f;
        foreach (float side in new[] { -1f, 1f })
            Block(chest, side < 0 ? "Push_Standoff_Front" : "Push_Standoff_Back", new Vector3(x + side * ChestDepth * .3f, handleY, handleZ + .04f),
                new Vector3(.025f, .025f, .08f), m.BareSteel, collider: false);
        Block(chest, "Push_Bar", new Vector3(x, handleY, handleZ + .0925f), new Vector3(ChestDepth * .6f + .05f, .025f, .025f), m.BareSteel, collider: false);

        int caster = 0;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
        {
            var p = new Vector3(x + sx * (ChestDepth * .5f - .06f), 0, z + sz * (ChestWidth * .5f - .06f));
            caster++;
            Block(chest, $"Caster_Plate_{caster}", new Vector3(p.x, y0 - .006f, p.z), new Vector3(.07f, .012f, .07f), m.BareSteel, collider: false);
            Block(chest, $"Caster_Fork_{caster}", new Vector3(p.x, f.Floor + .099f, p.z), new Vector3(.05f, .018f, .05f), m.BareSteel, collider: false);
            Rod(chest, $"Caster_Wheel_{caster}", new Vector3(p.x, f.Floor + .045f, p.z), Vector3.right, .045f, .03f, m.Rubber);
        }

        chest.rotation = Quaternion.Euler(0, ChestYaw, 0);
        Bounds b = B(chest.gameObject);
        if (b.max.x > f.East - .02f || b.min.z < bench.max.z + .1f || b.max.z > cart.min.z - .3f)
            throw new InvalidOperationException(FormattableString.Invariant(
                $"No room for {GroupName}/Tool_Chest between '{BenchPath}' (ends z={bench.max.z:F2}) and '{CartPath}' (starts z={cart.min.z:F2})."));
        return chest.gameObject;
    }

    /// <summary>A yellow steel tray with a divider, nuts in one bin and bolts in the other, resting on
    /// <paramref name="bottomCentre"/> and turned <paramref name="yaw"/> degrees.</summary>
    static void BuildTray(Transform group, string name, Vector3 bottomCentre, float yaw, Finishes m)
    {
        var tray = new GameObject(name).transform;
        tray.SetParent(group, false);
        tray.position = bottomCentre;
        Vector3 o = bottomCentre;
        float inner = TrayDepth - 2 * TrayWall;
        Block(tray, "Base", o + new Vector3(0, .004f, 0), new Vector3(TrayDepth, .008f, TrayLength), m.Yellow);
        foreach (float side in new[] { -1f, 1f })
        {
            Block(tray, side < 0 ? "Side_Front" : "Side_Back", o + new Vector3(side * (TrayDepth - TrayWall) * .5f, TrayHeight * .5f, 0),
                new Vector3(TrayWall, TrayHeight, TrayLength), m.Yellow, collider: false);
            Block(tray, side < 0 ? "End_South" : "End_North", o + new Vector3(0, TrayHeight * .5f, side * (TrayLength - TrayWall) * .5f),
                new Vector3(inner, TrayHeight, TrayWall), m.Yellow, collider: false);
        }
        Block(tray, "Divider", o + new Vector3(0, TrayHeight * .4f, 0), new Vector3(inner, TrayHeight * .8f, TrayWall), m.Yellow, collider: false);

        int n = 0;
        foreach (float px in new[] { -.08f, 0f, .08f })
        foreach (float pz in new[] { -.16f, -.09f })
            Rod(tray, $"Nut_{++n}", o + new Vector3(px + pz * .1f, .013f, pz), Vector3.up, .014f, .01f, m.BareSteel);
        for (int i = 0; i < 4; i++)
            Rod(tray, $"Bolt_{i + 1}", o + new Vector3(.03f, .014f, .05f + i * .035f), Vector3.right, .006f, .07f, m.BareSteel);
        Block(tray, "Bracket_Part", o + new Vector3(-.09f, .023f, .12f), new Vector3(.05f, .03f, .04f), m.Red, collider: false);
        tray.rotation = Quaternion.Euler(0, yaw, 0);
    }

    /// <summary>
    /// The corebuster_art_002 arm in its shipped display pose on a hazard-edged steel plinth against the east wall.
    /// The root is placed and yawed so the tool point (about 1.48 m ahead of the root) lands over the bench centre line
    /// just in from the bench's south end.
    /// </summary>
    static void BuildArmCell(Transform group, StartWorkshopFrame f, Bounds top, Bounds corner, Finishes m)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ArmPrefab);
        if (asset == null) throw new InvalidOperationException($"Missing prefab {ArmPrefab}; run Tools/CoreBuster/Build Robotic Arm.");
        var cell = new GameObject("Robot_Arm_Cell").transform;
        cell.SetParent(group, false);
        var arm = (GameObject)PrefabUtility.InstantiatePrefab(asset, cell);
        arm.name = "Robot_Arm";
        arm.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var tool = arm.transform.Find(ToolPointPath);
        if (tool == null) throw new InvalidOperationException($"{ArmPrefab} has no {ToolPointPath}; rebuild the robotic arm.");
        Vector3 tip = tool.position;

        float half = PlinthSize * .5f;
        float xRoot = f.East - WallGap - half;
        var target = new Vector3(top.center.x, 0, top.min.z + ArmTargetInset);
        float reach = new Vector2(tip.x, tip.z).magnitude, dx = target.x - xRoot;
        if (Mathf.Abs(dx) > reach - .05f)
            throw new InvalidOperationException($"The robot arm cannot reach over '{BenchTopPath}' from the plinth against the wall.");
        float dz = Mathf.Sqrt(reach * reach - dx * dx);
        float yaw = (Mathf.Atan2(dx, dz) - Mathf.Atan2(tip.x, tip.z)) * Mathf.Rad2Deg;
        var rootPos = new Vector3(xRoot, f.Floor + PlinthHeight, target.z - dz);
        if (rootPos.z - half < corner.max.z + RailingClearance || rootPos.z + half > top.min.z - .2f)
            throw new InvalidOperationException(FormattableString.Invariant(
                $"No room for the robot-arm cell between '{CornerStackPath}' (ends z={corner.max.z:F2}) and '{BenchPath}' (starts z={top.min.z:F2})."));
        cell.position = new Vector3(xRoot, f.Floor, rootPos.z);
        arm.transform.SetPositionAndRotation(rootPos, Quaternion.Euler(0, yaw, 0));

        Block(cell, "Plinth", new Vector3(xRoot, f.Floor + PlinthHeight * .5f, rootPos.z), new Vector3(PlinthSize, PlinthHeight, PlinthSize), m.PaintedSteel);
        // Painted edge strips on the plinth top; the east and west ones are turned so the stripe runs along them.
        float edgeY = f.Floor + PlinthHeight + .002f, edge = half - HazardEdgeWidth * .5f;
        foreach (float side in new[] { -1f, 1f })
        {
            Block(cell, side < 0 ? "Hazard_Edge_South" : "Hazard_Edge_North", new Vector3(xRoot, edgeY, rootPos.z + side * edge),
                new Vector3(PlinthSize, .004f, HazardEdgeWidth), m.HazardEdge, collider: false);
            Block(cell, side < 0 ? "Hazard_Edge_West" : "Hazard_Edge_East", new Vector3(xRoot + side * edge, edgeY, rootPos.z),
                new Vector3(PlinthSize - 2 * HazardEdgeWidth, .004f, HazardEdgeWidth), m.HazardEdge, collider: false, rotation: Quaternion.Euler(0, 90, 0));
        }
    }

    /// <summary>
    /// Fails when a workstation prop cuts into another workstation prop or an earlier pass's geometry. The overhead,
    /// cladding and pipework passes are only warned about: they lay themselves out around this group when rebuilt
    /// after it, as the full Dress Start Workshop order does.
    /// </summary>
    static void CheckClear(Transform root, Transform group)
    {
        Physics.SyncTransforms();
        var others = new List<(Transform g, Renderer r)>();
        // The liners are wall surface; the corebuster_render_006 lamps are rebuilt after this pass and fit around it.
        foreach (Transform g in root)
            if (!StartWorkshopLiners.IsGroup(g) && !StartWorkshopLighting.IsGroup(g))
                foreach (var r in g.GetComponentsInChildren<Renderer>())
                    if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) others.Add((g, r));
        var errors = new SortedSet<string>();
        var warnings = new SortedSet<string>();
        foreach (Transform prop in group)
            foreach (var mine in prop.GetComponentsInChildren<Renderer>())
                foreach (var (g, other) in others)
                {
                    if (other.transform.IsChildOf(prop) || !Overlaps(mine.bounds, other.bounds)) continue;
                    string message = $"{GroupName}/{prop.name}/{mine.name} intersects {g.name}/.../{other.name}";
                    bool later = StartWorkshopOverhead.IsGroup(g) || StartWorkshopCladding.IsGroup(g) || StartWorkshopPipework.IsGroup(g);
                    (later ? warnings : errors).Add(message);
                }
        foreach (var w in warnings) Debug.LogWarning(w + "; rebuild that pass after the workstation.");
        if (errors.Count > 0)
            throw new InvalidOperationException("The workstation does not fit beside the existing props:\n  " + string.Join("\n  ", errors));
    }

    static bool Overlaps(Bounds a, Bounds b) =>
        Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x) > Clearance &&
        Mathf.Min(a.max.y, b.max.y) - Mathf.Max(a.min.y, b.min.y) > Clearance &&
        Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z) > Clearance;

    /// <summary>
    /// Explicit corebuster_env_010 acceptance rule: the workstation parts exist on the steel bench, the bench trays and
    /// the corebuster_env_004 gears and handwheel rest on the bench top, the arm's gripper works over it, and the lamp is
    /// an emissive mesh with no Light component.
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add($"missing {GroupName}; run Tools/CoreBuster/Build Start Workshop Workstation.");
            return;
        }
        var topTransform = root.Find(BenchTopPath);
        if (topTransform == null || root.Find(BenchMarkerPath) == null)
        {
            errors.Add($"{BenchPath} is not the corebuster_env_010 steel workbench; run Tools/CoreBuster/Dress Start Workshop.");
            return;
        }
        foreach (string part in RequiredParts)
            if (group.Find(part) == null) errors.Add($"{GroupName} is missing {part}.");

        Bounds top = B(topTransform.gameObject);
        var onBench = new List<(string name, Transform t)>();
        var benchParts = root.Find(BenchPartsPath);
        if (benchParts == null) errors.Add($"missing {BenchPartsPath}; run Tools/CoreBuster/Densify Start Workshop.");
        else foreach (Transform t in benchParts) onBench.Add(($"{BenchPartsPath}/{t.name}", t));
        foreach (Transform t in group)
            if (t.name.StartsWith(TrayPrefix) && t.name != ChestTray) onBench.Add(($"{GroupName}/{t.name}", t));
        foreach (var (name, t) in onBench)
        {
            Bounds b = B(t.gameObject);
            if (Mathf.Abs(b.min.y - top.max.y) > Rest || b.center.x < top.min.x || b.center.x > top.max.x ||
                b.center.z < top.min.z || b.center.z > top.max.z)
                errors.Add($"{name} does not rest on the workbench top ({b.min:F2} to {b.max:F2}).");
        }

        var tool = group.Find(ArmPath + "/" + ToolPointPath);
        if (tool == null) errors.Add($"{GroupName}/{ArmPath} is missing or has no {ToolPointPath}.");
        else
        {
            Vector3 p = tool.position;
            float above = p.y - top.max.y;
            if (p.x < top.min.x || p.x > top.max.x || p.z < top.min.z || p.z > top.max.z || above < .2f || above > .8f)
                errors.Add($"{GroupName}/{ArmPath} does not face the workbench: its tool point {p:F2} is not 0.2-0.8 m over the top.");
        }

        var tube = group.Find("Task_Lamp/Tube");
        var tubeRenderer = tube != null ? tube.GetComponent<Renderer>() : null;
        if (tubeRenderer == null || tubeRenderer.sharedMaterial == null || !tubeRenderer.sharedMaterial.IsKeywordEnabled("_EMISSION"))
            errors.Add($"{GroupName}/Task_Lamp needs an emissive fluorescent tube.");
        foreach (var light in group.GetComponentsInChildren<Light>(true))
            errors.Add($"{GroupName} has a Light on {light.name}; the task lamp's light belongs to {StartWorkshopLighting.GroupName}/{StartWorkshopLighting.TaskName}.");
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    static GameObject Anchor(Transform root, string path)
    {
        var t = root.Find(path);
        if (t == null)
            throw new InvalidOperationException($"Anchor prop '{path}' is missing; run Tools/CoreBuster/Dress Start Workshop before the workstation pass.");
        return t.gameObject;
    }

    /// <summary>A collider-free primitive cylinder along <paramref name="axis"/>.</summary>
    internal static void Rod(Transform parent, string name, Vector3 center, Vector3 axis, float radius, float length, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, Quaternion.FromToRotation(Vector3.up, axis));
        go.transform.localScale = new Vector3(2 * radius, length * .5f, 2 * radius);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>
    /// The workstation's finishes, shared with the dresser's steel workbench. The corebuster_art_001/002 materials are
    /// reused as they are; the four project-owned materials below are (re)written deterministically on every build.
    /// </summary>
    internal sealed class Finishes
    {
        const string Kit = "Assets/FPS/Art/StartWorkshop/HeavyIndustrialKit/Materials/";
        const string Arm = "Assets/FPS/Art/StartWorkshop/RoboticArm/Materials/";
        const string Props = "Assets/FPS/Art/Materials/Props/";
        const string PegboardTexture = "Assets/FPS/Art/Textures/StartWorkshop/Pegboard_Tools.png";

        public readonly Material BareSteel = Load(Kit + "M_HeavyPipe_BareSteel.mat", "Build Heavy Industrial Kit");
        public readonly Material Red = Load(Kit + "M_HeavyPipe_RedOxide.mat", "Build Heavy Industrial Kit");
        public readonly Material Yellow = Load(Kit + "M_HeavyPipe_HazardYellow.mat", "Build Heavy Industrial Kit");
        public readonly Material Rubber = Load(Arm + "M_RoboticArm_HoseRubber.mat", "Build Robotic Arm");
        public readonly Material PaintedSteel, HazardEdge, Pegboard, Tube;

        public Finishes()
        {
            // Grey enamel over the kit's steel grain and normals, for the bench pedestals, pegboard backing and lamp.
            PaintedSteel = Owned("M_Workstation_PaintedSteel", BareSteel.shader, m =>
            {
                m.CopyPropertiesFromMaterial(BareSteel);
                m.shaderKeywords = BareSteel.shaderKeywords;
                var tint = new Color(.5f, .53f, .56f);
                m.SetColor("_BaseColor", tint);
                m.SetColor("_Color", tint);
                m.SetFloat("_Metallic", .35f);
                m.SetFloat("_Smoothness", .3f);
            });
            // The corebuster_env_008 hazard stripe, tiled on the material (property blocks are not saved with the
            // scene) so each plinth edge strip keeps the texture's 2:1 stripe aspect.
            var hazard = Load(Props + "M_Hazard_Trim.mat", "Build Start Workshop Cladding");
            HazardEdge = Owned("M_Workstation_HazardEdge", hazard.shader, m =>
            {
                m.CopyPropertiesFromMaterial(hazard);
                m.shaderKeywords = hazard.shaderKeywords;
                var repeat = new Vector2(PlinthSize / (2 * HazardEdgeWidth), 1);
                m.SetTextureScale("_BaseMap", repeat);
                m.SetTextureScale("_MainTex", repeat);
            });
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            var board = AssetDatabase.LoadAssetAtPath<Texture2D>(PegboardTexture);
            if (board == null)
                throw new InvalidOperationException($"Missing project-owned texture {PegboardTexture}; run python Tools/Art/generate_start_workshop_graphics.py.");
            Pegboard = Owned("M_Pegboard_Tools", lit, m =>
            {
                m.SetTexture("_BaseMap", board);
                m.SetTexture("_MainTex", board);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Metallic", .3f);
                m.SetFloat("_Smoothness", .28f);
            });
            Tube = Owned("M_Fluorescent_Tube", lit, m =>
            {
                m.SetColor("_BaseColor", new Color(.93f, .95f, 1f));
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", .8f);
                m.SetColor("_EmissionColor", new Color(2.4f, 2.33f, 2.16f, 1f));
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            });
        }

        static Material Load(string path, string builder)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) throw new InvalidOperationException($"Missing material {path}; run Tools/CoreBuster/{builder}.");
            return m;
        }

        static Material Owned(string name, Shader shader, Action<Material> setup)
        {
            string path = Props + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            setup(m);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
