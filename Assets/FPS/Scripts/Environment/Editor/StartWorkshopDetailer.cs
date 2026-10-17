using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static StartWorkshopDresser;

/// <summary>
/// Second dressing layer for the Start Workshop (corebuster_env_004): denser storage on the west wall, a control
/// cluster, stool, bench greeble and a loaded pallet on the east wall, a pipe drop, riser, valves, signage, a loaded
/// pallet and a tall corner stack on the rear (south) wall, and duct branches overhead. It adds only the "*_Detail"
/// groups under Level/Start_Workshop, layered against the first-pass (corebuster_env_002) props, which it reads but
/// never moves.
/// </summary>
public static class StartWorkshopDetailer
{
    public const string DetailSuffix = "_Detail";
    const string UndoName = "Densify Start Workshop";

    const string Kit = "Assets/SciFi Warehouse Kit/Prefabs/";
    const string CrateLong = Kit + "Props/Crates Barrels Pallets/Crate Long.prefab";
    const string CrateShort = Kit + "Props/Crates Barrels Pallets/Crate Short.prefab";
    const string Barrel = Kit + "Props/Crates Barrels Pallets/Barrel.prefab";
    // Pallet variants without Rigidbodies: 09 is a plain flat-topped pallet stack, 02 and 05 are loaded pallets.
    const string PalletBase = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 09.prefab";
    const string PalletCrates = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 02.prefab";
    const string PalletBarrels = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 05.prefab";
    const string DuctStraight = Kit + "Props/Ducts/Duct Straight.prefab";
    const string DuctElbowUp = Kit + "Props/Ducts/Duct Elbow 01.prefab";
    const string DuctTee = Kit + "Props/Ducts/Duct Tee.prefab";
    const string DuctVent = Kit + "Props/Ducts/Duct Vent.prefab";
    const string PipeRiser = Kit + "Structures/Structure Props/Pipes 01.prefab";
    const string PipeDrop = Kit + "Structures/Structure Props/Pipes 06.prefab";

    // Secondary sources, limited to the prefabs named in corebuster_env_004. These face local +Z rather than the kit's +X.
    const string Modular = "Assets/Sci-Fi Styled Modular Pack/Prefabs/";
    const string Console = Modular + "Decorative elements/console.prefab";
    const string Screen = Modular + "Decorative elements/big_screen.prefab";
    const string ShelfSmall = Modular + "Decorative elements/shelf_small.prefab";
    const string BulletinBoard = Modular + "Decorative elements/bulletin_board_small.prefab";
    const string SignSmall = Modular + "Decorative elements/sing_small.prefab";
    const string Generator = Modular + "Machines/generator.prefab";
    const string StorageContainer = Modular + "Machines/storage_container_small.prefab";
    const string Valve = "Assets/HorrorPuzzleItems/Valve/Prefabs/Valve.prefab";
    const string Gear = "Assets/HorrorPuzzleItems/Gear/Prefabs/Gear.prefab";
    const string Cable = "Assets/HorrorPuzzleItems/Cable/Prefabs/Cable_Black.prefab";
    const float ZFront = 90;
    // The pack ships palette swaps sharing one UV layout; its default pastel palette clashes with the grimy kit, so
    // instances are repainted with the pack's own darker palettes (the yellow-red one echoes the reference canisters).
    const string PackPalette = "base color";
    internal const string DarkPalette = "Assets/Sci-Fi Styled Modular Pack/Materials/Palletes/dark color.mat";
    internal const string AccentPalette = "Assets/Sci-Fi Styled Modular Pack/Materials/Palletes/iron man color.mat";

    // The puzzle items are hand-sized (~10 cm); scale them to read as workshop handwheels and parts.
    const float ValveScale = 3.2f;
    const float GearScale = 2.6f;
    const float CableScale = 3f;
    const float ScreenScale = .6f;
    const float ContainerScale = .5f;
    const float StoolSeatRadius = .19f;

    [MenuItem("Tools/CoreBuster/Densify Start Workshop")]
    public static void Densify()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; the detail layer builds on {LevelName}/{ContainerName}.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        for (int i = root.childCount - 1; i >= 0; i--)
            if (root.GetChild(i).name.EndsWith(DetailSuffix) || root.GetChild(i).name == StartWorkshopGraphics.GroupName)
                Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);

        var f = StartWorkshopFrame.Measure(scene, root);
        int first = root.childCount;
        try
        {
            Build(root, f);
            StartWorkshopGraphics.Build(root, f);
        }
        catch
        {
            for (int i = root.childCount - 1; i >= first; i--) UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
            throw;
        }
        var flags = GameObjectUtility.GetStaticEditorFlags(level.gameObject);
        for (int i = first; i < root.childCount; i++)
        {
            ApplyStaticConvention(root.GetChild(i), flags);
            Undo.RegisterCreatedObjectUndo(root.GetChild(i).gameObject, UndoName);
        }
        StartWorkshopLitMaterials.ApplyTo(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root;
        Debug.Log($"Start Workshop detail layer rebuilt. {f}");
        StartWorkshopChecks.Run();
    }

    /// <summary>Adds the detail groups to a container that already holds the corebuster_env_002 groups.</summary>
    public static void Build(Transform root, StartWorkshopFrame f)
    {
        var storage = Group(root, "Storage_WestWall" + DetailSuffix);
        var maintenance = Group(root, "Maintenance_EastWall" + DetailSuffix);
        var services = Group(root, "Services_RearWall" + DetailSuffix);
        var ceiling = Group(root, "Ducts_Ceiling" + DetailSuffix);

        // West wall: a 2x2 wall of small shelves and a low container with crates, filling the gap between the
        // pallet stacks and the north-west crates so storage reads as one continuous run.
        Bounds palletB = B(Base(root, "Storage_WestWall/Pallet_Stack_B"));
        float z = palletB.max.z + 1f;
        for (int i = 0; i < 2; i++)
        {
            string id = i == 0 ? "A" : "B";
            var lower = OnFloor(Pack(storage, ShelfSmall, $"Shelf_Small_{id}_Lower", f, Wall.West), f);
            Face(lower, f, Wall.West);
            Along(lower, f, Wall.West, z, Anchor.Min);
            var upper = Pack(storage, ShelfSmall, $"Shelf_Small_{id}_Upper", f, Wall.West);
            Bottom(upper, B(lower).max.y);
            Face(upper, f, Wall.West);
            Along(upper, f, Wall.West, z, Anchor.Min);
            z = B(lower).max.z + .08f;
        }
        var container = OnFloor(Pack(storage, StorageContainer, "Storage_Container", f, Wall.West, ContainerScale), f);
        Face(container, f, Wall.West);
        Along(container, f, Wall.West, z + .5f, Anchor.Min);
        Bounds cb = B(container);
        var containerCrate = Prop(storage, CrateLong, "Container_Crate_Long", f, Wall.West, alongWall: true);
        Bottom(containerCrate, cb.max.y);
        Face(containerCrate, f, Wall.West, .15f);
        Along(containerCrate, f, Wall.West, cb.center.z - .15f, Anchor.Center);
        var containerTop = Prop(storage, CrateShort, "Container_Crate_Short", f, Wall.West);
        Bottom(containerTop, B(containerCrate).max.y);
        Face(containerTop, f, Wall.West, .15f);
        Along(containerTop, f, Wall.West, B(containerCrate).min.z, Anchor.Min);

        // East wall: stool and parts at the bench, a cable coil beside the cart, then (north of the bin) a blueprint
        // board, a console-and-screen control cluster and an upright generator as the tall landmark canister.
        Bounds bench = B(Base(root, "Maintenance_EastWall/Workbench"));
        Bounds benchTop = B(Base(root, "Maintenance_EastWall/Workbench/Bench_Top"));
        BuildStool(maintenance, new Vector3(bench.min.x - .06f - StoolSeatRadius, f.Floor, benchTop.center.z - .45f));
        BuildBenchParts(maintenance, f, benchTop);

        Bounds cart = B(Base(root, "Maintenance_EastWall/Maintenance_Cart"));
        var cable = OnFloor(Prop(maintenance, Cable, "Cart_Cable_Coil", f, Wall.West, CableScale), f);
        Face(cable, f, Wall.East, .12f);
        Along(cable, f, Wall.East, cart.max.z + .2f, Anchor.Min);

        Bounds bin = B(Base(root, "Maintenance_EastWall/Garbage_Bin"));
        var board = OnFloor(Pack(maintenance, BulletinBoard, "Blueprint_Board", f, Wall.East), f);
        Face(board, f, Wall.East);
        Along(board, f, Wall.East, bin.max.z + .7f, Anchor.Min);

        float clusterZ = B(board).max.z + 2.2f;
        for (int i = 0; i < 2; i++)
        {
            var console = OnFloor(Pack(maintenance, Console, i == 0 ? "Control_Console_A" : "Control_Console_B", f, Wall.East), f);
            Face(console, f, Wall.East);
            Along(console, f, Wall.East, clusterZ + (i == 0 ? -.07f : .07f), i == 0 ? Anchor.Max : Anchor.Min);
        }
        var screen = Pack(maintenance, Screen, "Control_Screen", f, Wall.East, ScreenScale);
        Bottom(screen, f.Floor + 2.05f);
        Face(screen, f, Wall.East, 0);
        Along(screen, f, Wall.East, clusterZ, Anchor.Center);
        Mount(screen, root, Wall.East);

        // Stood on end: the hex cap up and the base plate against the wall.
        var canister = Pack(maintenance, Generator, "Power_Canister", f, Wall.East, palette: AccentPalette);
        canister.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.left);
        OnFloor(canister, f);
        Face(canister, f, Wall.East);
        Along(canister, f, Wall.East, clusterZ + 2.6f, Anchor.Min);
        var eastStack = OnFloor(Prop(maintenance, PalletBarrels, "Pallet_Stack_E", f, Wall.East), f);
        Face(eastStack, f, Wall.East);
        Along(eastStack, f, Wall.East, B(canister).max.z + 1.2f, Anchor.Min);

        // Rear (south) wall: a pipe drop from the ceiling into the fuseboxes with a handwheel beside them, a warning
        // sign, a floor-to-ceiling riser with a valve in the west bay, and a tall crate stack in the free south-east corner.
        Bounds fuseA = B(Base(root, "Services_RearWall/Fusebox_A"));
        Bounds fuseB = B(Base(root, "Services_RearWall/Fusebox_B"));
        var drop = Prop(services, PipeDrop, "Pipe_Drop_Fusebox", f, Wall.South);
        // Both fuseboxes stand under the drop's footprint; stop on the taller one so the run cannot cut into either.
        StretchY(drop, f.Ceiling - Mathf.Max(fuseA.max.y, fuseB.max.y));
        Top(drop, f.Ceiling);
        Face(drop, f, Wall.South, 0);
        // On this wall the drop's lower (fusebox) end is its west end; its upper run heads east over Fusebox_A.
        Along(drop, f, Wall.South, fuseB.center.x - .15f, Anchor.Min);
        Mount(drop, root, Wall.South);

        var fuseValve = WallValve(services, "Fusebox_Valve", f);
        Bottom(fuseValve, f.Floor + 1.55f);
        Along(fuseValve, f, Wall.South, fuseB.min.x - .45f, Anchor.Max);
        Mount(fuseValve, root, Wall.South);

        var sign = Pack(services, SignSmall, "Warning_Sign", f, Wall.South, palette: AccentPalette);
        Bottom(sign, f.Floor + 1.9f);
        Face(sign, f, Wall.South, 0);
        Along(sign, f, Wall.South, B(fuseValve).min.x - .6f, Anchor.Max);
        Mount(sign, root, Wall.South);

        var riser = Prop(services, PipeRiser, "Pipe_Riser_West", f, Wall.South);
        StretchY(riser, f.Ceiling - f.Floor);
        OnFloor(riser, f);
        Face(riser, f, Wall.South, 0);
        Along(riser, f, Wall.South, f.West + 3.6f, Anchor.Center);
        Mount(riser, root, Wall.South);
        Bounds rb = B(riser);
        var riserValve = WallValve(services, "Riser_Valve", f);
        Bottom(riserValve, f.Floor + 1.3f);
        Shift(riserValve, rb.center.x - B(riserValve).center.x, 0, rb.max.z - B(riserValve).min.z);

        // Loaded pallet and barrel on the bare stretch east of the low pipes, facing the player's opening view back.
        Bounds pipesLow = B(Base(root, "Services_RearWall/Pipes_Low"));
        var southStack = OnFloor(Prop(services, PalletCrates, "Pallet_Stack_S", f, Wall.South), f);
        Face(southStack, f, Wall.South);
        Along(southStack, f, Wall.South, pipesLow.max.x + .6f, Anchor.Min);
        var southBarrel = OnFloor(Prop(services, Barrel, "Barrel_S", f, Wall.South), f);
        Face(southBarrel, f, Wall.South);
        Along(southBarrel, f, Wall.South, B(southStack).max.x + .25f, Anchor.Min);

        BuildCornerStack(services, f, fuseA);

        // Ceiling: two branches off the corebuster_env_002 duct run heading north, one rising into the ceiling
        // through an elbow and one ending in a tee with vents to either side.
        Bounds run = B(Base(root, "Ducts_Ceiling/Duct_Run"));
        BuildDuctBranch(ceiling, "Duct_Branch_East", f, run, f.East - 4.5f, tee: false);
        BuildDuctBranch(ceiling, "Duct_Branch_West", f, run, f.West + 6f, tee: true);
    }

    /// <summary>
    /// A first-principles shop stool at the bench: a round rubber seat on a steel pan, four legs and a foot ring
    /// (corebuster_env_010). It replaced the 1.55 m Modular Pack chair, whose back hid the hero workbench from the room.
    /// </summary>
    static void BuildStool(Transform parent, Vector3 floorCentre)
    {
        var finish = new StartWorkshopWorkstation.Finishes();
        var stool = new GameObject("Workbench_Stool").transform;
        stool.SetParent(parent, false);
        stool.position = floorCentre;
        const float seat = .66f, leg = .13f, legTop = seat - .07f, ring = .24f;
        StartWorkshopWorkstation.Rod(stool, "Seat", floorCentre + Vector3.up * (seat - .025f), Vector3.up, StoolSeatRadius, .05f, finish.Rubber);
        StartWorkshopWorkstation.Rod(stool, "Seat_Pan", floorCentre + Vector3.up * (legTop + .01f), Vector3.up, .15f, .02f, finish.PaintedSteel);
        int n = 0;
        foreach (float x in new[] { -leg, leg })
        foreach (float z in new[] { -leg, leg })
            Block(stool, $"Leg_{++n}", floorCentre + new Vector3(x, legTop * .5f, z), new Vector3(.028f, legTop, .028f), finish.PaintedSteel);
        foreach (float side in new[] { -leg, leg })
        {
            Block(stool, side < 0 ? "Foot_Ring_South" : "Foot_Ring_North", floorCentre + new Vector3(0, ring, side),
                new Vector3(2 * leg, .02f, .02f), finish.BareSteel, collider: false);
            Block(stool, side < 0 ? "Foot_Ring_West" : "Foot_Ring_East", floorCentre + new Vector3(side, ring, 0),
                new Vector3(.02f, .02f, 2 * leg), finish.BareSteel, collider: false);
        }
    }

    static void BuildBenchParts(Transform parent, StartWorkshopFrame f, Bounds top)
    {
        var parts = new GameObject("Bench_Parts");
        parts.transform.SetParent(parent, false);
        var gearA = Prop(parts.transform, Gear, "Gear_A", f, Wall.West, GearScale);
        Bottom(gearA, top.max.y);
        Shift(gearA, top.center.x - .12f - B(gearA).center.x, 0, top.min.z + .3f - B(gearA).min.z);
        var gearB = Prop(parts.transform, Gear, "Gear_B", f, Wall.West, GearScale * .8f);
        Bottom(gearB, top.max.y);
        Shift(gearB, top.center.x + .15f - B(gearB).center.x, 0, B(gearA).max.z + .08f - B(gearB).min.z);
        // A spare handwheel laid flat, stem up so it rests on its rim.
        var wheel = Prop(parts.transform, Valve, "Spare_Handwheel", f, Wall.West, ValveScale * .8f);
        wheel.transform.rotation = Quaternion.Euler(180, 0, 0) * wheel.transform.rotation;
        Bottom(wheel, top.max.y);
        Shift(wheel, top.center.x - B(wheel).center.x, 0, B(gearB).max.z + .1f - B(wheel).min.z);
    }

    /// <summary>A pallet stack with crates piled on it, taller than the first-pass stacks, tucked into the SE corner.</summary>
    static void BuildCornerStack(Transform parent, StartWorkshopFrame f, Bounds fusebox)
    {
        var stack = new GameObject("Crate_Stack_SE");
        stack.transform.SetParent(parent, false);
        var pallets = OnFloor(Prop(stack.transform, PalletBase, "Pallet_Base", f, Wall.East), f);
        Corner(pallets, f, Wall.East, Wall.South);
        if (B(pallets).min.x < fusebox.max.x + .3f)
            throw new InvalidOperationException("The south-east corner stack would crowd Fusebox_A; move it before densifying.");
        float deck = B(pallets).max.y;
        var crateLong = Prop(stack.transform, CrateLong, "Crate_Long", f, Wall.East, alongWall: true);
        Bottom(crateLong, deck);
        Corner(crateLong, f, Wall.East, Wall.South);
        var crateSide = Prop(stack.transform, CrateShort, "Crate_Short_Side", f, Wall.East);
        Bottom(crateSide, deck);
        Face(crateSide, f, Wall.South);
        Along(crateSide, f, Wall.South, B(crateLong).min.x - .05f, Anchor.Max);
        var crateMid = Prop(stack.transform, CrateShort, "Crate_Short_Mid", f, Wall.East);
        Bottom(crateMid, B(crateLong).max.y);
        Corner(crateMid, f, Wall.East, Wall.South);
        var crateTop = Prop(stack.transform, CrateShort, "Crate_Short_Top", f, Wall.East, .85f);
        Bottom(crateTop, B(crateMid).max.y);
        Corner(crateTop, f, Wall.East, Wall.South);
    }

    /// <summary>
    /// A straight duct butted against the side of the main run, capped by an elbow rising into the ceiling or by a tee
    /// with a vent on each arm. The kit's duct pieces share one cross-section and put their pivot on a port, so later
    /// pieces are placed pivot-to-port rather than by bounds.
    /// </summary>
    static void BuildDuctBranch(Transform parent, string name, StartWorkshopFrame f, Bounds run, float x, bool tee)
    {
        var branch = new GameObject(name);
        branch.transform.SetParent(parent, false);
        var straight = Prop(branch.transform, DuctStraight, "Duct_Straight", f, Wall.West);
        Top(straight, run.max.y);
        Bounds sb = B(straight);
        Shift(straight, x - sb.center.x, 0, run.max.z - sb.min.z);
        sb = B(straight);
        Vector3 port = new Vector3(straight.transform.position.x, straight.transform.position.y, sb.max.z);
        // Elbow and tee ports face local +Z; turn them to meet the straight coming from the south.
        Quaternion back = Quaternion.Euler(0, 180, 0);
        if (!tee)
        {
            var elbow = Prop(branch.transform, DuctElbowUp, "Duct_Elbow_Up", f, Wall.West);
            elbow.transform.SetPositionAndRotation(port, back);
            return;
        }
        var t = Prop(branch.transform, DuctTee, "Duct_Tee", f, Wall.West);
        t.transform.SetPositionAndRotation(port, back);
        Bounds tb = B(t);
        float armZ = tb.max.z - sb.size.x * .5f;
        var east = Prop(branch.transform, DuctVent, "Duct_Vent_East", f, Wall.West);
        east.transform.SetPositionAndRotation(new Vector3(tb.max.x, port.y, armZ), Quaternion.Euler(0, 90, 0));
        var west = Prop(branch.transform, DuctVent, "Duct_Vent_West", f, Wall.West);
        west.transform.SetPositionAndRotation(new Vector3(tb.min.x, port.y, armZ), Quaternion.Euler(0, -90, 0));
    }

    /// <summary>A Sci-Fi Styled Modular Pack prop facing into the room from <paramref name="wall"/>, repainted in a kit-friendly palette.</summary>
    internal static GameObject Pack(Transform parent, string path, string name, StartWorkshopFrame f, Wall wall, float scale = 1f, string palette = DarkPalette)
    {
        var go = Prop(parent, path, name, f, wall, scale, yawOffset: ZFront);
        var material = AssetDatabase.LoadAssetAtPath<Material>(palette);
        if (material == null) throw new InvalidOperationException("Missing palette material: " + palette);
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null && materials[i].name == PackPalette) materials[i] = material;
            r.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        }
        return go;
    }

    /// <summary>A handwheel standing off the south wall, its stem towards the wall.</summary>
    static GameObject WallValve(Transform parent, string name, StartWorkshopFrame f)
    {
        var valve = Prop(parent, Valve, name, f, Wall.West, ValveScale);
        valve.transform.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward) * valve.transform.rotation;
        Face(valve, f, Wall.South, 0);
        return valve;
    }

    /// <summary>Scales a prop along world Y so its rendered height becomes <paramref name="height"/>.</summary>
    static void StretchY(GameObject go, float height)
    {
        var s = go.transform.localScale;
        s.y *= height / B(go).size.y;
        go.transform.localScale = s;
    }

    static GameObject Base(Transform root, string path)
    {
        var t = root.Find(path);
        if (t == null)
            throw new InvalidOperationException($"First-pass prop '{path}' is missing; run Tools/CoreBuster/Dress Start Workshop before densifying.");
        return t.gameObject;
    }
}
