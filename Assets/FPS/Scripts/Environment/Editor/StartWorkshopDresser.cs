using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dresses the MainScene Start Workshop with SciFi Warehouse Kit props (corebuster_env_002): storage along the west
/// wall, the corebuster_env_010 steel workbench and maintenance kit along the east wall, services on the rear (south)
/// wall and ceiling, and exit signage over the doorway. Rebuilding replaces Level/Start_Workshop only, including the
/// corebuster_env_004 detail layer (<see cref="StartWorkshopDetailer"/>), the corebuster_env_010 workstation
/// (<see cref="StartWorkshopWorkstation"/>), the corebuster_env_007 overhead layer
/// (<see cref="StartWorkshopOverhead"/>), the corebuster_env_008 wall cladding layer
/// (<see cref="StartWorkshopCladding"/>), the corebuster_env_009 pipework (<see cref="StartWorkshopPipework"/>), the
/// corebuster_env_019 centre islands (<see cref="StartWorkshopCentre"/>), the corebuster_art_003 signage
/// (<see cref="StartWorkshopSignage"/>), the corebuster_render_002 grime decals (<see cref="StartWorkshopGrime"/>), the corebuster_render_007 boundary liners (<see cref="StartWorkshopLiners"/>), the corebuster_render_006 lighting
/// (<see cref="StartWorkshopLighting"/>) and corebuster_render_003's local grade (<see cref="StartWorkshopRendering"/>);
/// no gameplay object or asset is modified.
/// </summary>
public static class StartWorkshopDresser
{
    public const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    public const string LevelName = "Level";
    public const string ContainerName = "Start_Workshop";
    const string UndoName = "Dress Start Workshop";

    const string Kit = "Assets/SciFi Warehouse Kit/Prefabs/";
    const string Barrel = Kit + "Props/Crates Barrels Pallets/Barrel.prefab";
    const string CrateLong = Kit + "Props/Crates Barrels Pallets/Crate Long.prefab";
    const string CrateShort = Kit + "Props/Crates Barrels Pallets/Crate Short.prefab";
    // Shelf and pallet variants chosen because they carry no Rigidbodies (04/06 and pallet 01 do).
    const string PalletStackA = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 03.prefab";
    const string PalletStackB = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 07.prefab";
    const string ShelfA = Kit + "Props/Shelves/Shelf Variation 02.prefab";
    const string ShelfB = Kit + "Props/Shelves/Shelf Variation 05.prefab";
    const string DuctStraight = Kit + "Props/Ducts/Duct Straight.prefab";
    const string Cart = Kit + "Props/Misc Props/Cart.prefab";
    const string Extinguisher = Kit + "Props/Misc Props/Fire Extinguisher.prefab";
    const string GarbageBin = Kit + "Props/Misc Props/Garbage Bin.prefab";
    const string ExitSign = Kit + "Structures/Structure Props/Exit Sign.prefab";
    const string FuseboxA = Kit + "Structures/Structure Props/Fusebox 01.prefab";
    const string FuseboxB = Kit + "Structures/Structure Props/Fusebox 02.prefab";
    const string PipesLow = Kit + "Structures/Structure Props/Pipes 05.prefab";
    const string PipesHigh = Kit + "Structures/Structure Props/Pipes 03.prefab";

    // Workbench (corebuster_env_010): a 3.6 x 0.8 m steel bench with a 0.92 m working height and a four-drawer
    // pedestal at each end. It starts 4.75 m from the south wall, leaving room for the robot-arm cell
    // (StartWorkshopWorkstation) and a railing between it and the south-east corner stack.
    internal const float BenchSouth = 4.75f;
    const float BenchLength = 3.6f, BenchDepth = .8f, BenchHeight = .92f, BenchTopThickness = .06f;
    const float PedestalWidth = .6f, PedestalInset = .03f, PedestalEndGap = .04f;
    // Drawer front heights, top to bottom; what is left below the lowest one reads as the kick plinth.
    static readonly float[] DrawerHeights = { .13f, .15f, .2f, .24f };
    const float DrawerGap = .012f;
    // The cart keeps the place it had beside the corebuster_env_002 crate bench, so every east-wall prop north of it,
    // and the pipework drop anchored between them, is unchanged by the bench upgrade.
    const float CartSouth = 11.27f;
    // Small gap that keeps props visibly off the wall without reading as floating.
    internal const float WallGap = .03f;

    public enum Wall { West, East, South, North }
    internal enum Anchor { Min, Center, Max }

    [MenuItem("Tools/CoreBuster/Dress Start Workshop")]
    public static void Dress()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Open {ScenePath} before dressing the Start Workshop (active scene: {scene.path}).");
            return;
        }
        var level = FindLevel(scene);
        if (level == null)
        {
            Debug.LogError($"No root '{LevelName}' GameObject in {ScenePath}; the workshop container belongs under it.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        var existing = level.Find(ContainerName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        var f = StartWorkshopFrame.Measure(scene, null);
        var root = new GameObject(ContainerName).transform;
        root.SetParent(level, false);
        root.position = f.Center;
        try
        {
            Build(root, f);
            StartWorkshopDetailer.Build(root, f);
            StartWorkshopWorkstation.Build(root, f);
            StartWorkshopOverhead.Build(root, f);
            StartWorkshopCladding.Build(root, f);
            StartWorkshopPipework.Build(root, f);
            StartWorkshopCentre.Build(root, f);
            StartWorkshopGraphics.Build(root, f);
            StartWorkshopLiners.Build(root, f);
            StartWorkshopLighting.Build(root, f);
            StartWorkshopPortal.Build(root, f);
            StartWorkshopSignage.Build(root, f);
            StartWorkshopScreens.Build(root, f);
            StartWorkshopAmbient.Build(root, f);
            StartWorkshopAmbience.Build(root, f);
            StartWorkshopGrime.Build(root, f);
            StartWorkshopRendering.Build(root, f);
            ApplyStaticConvention(root, GameObjectUtility.GetStaticEditorFlags(level.gameObject));
            StartWorkshopAmbient.ClearMovingFlags(root.Find(StartWorkshopAmbient.GroupName));
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            throw;
        }
        Undo.RegisterCreatedObjectUndo(root.gameObject, UndoName);
        StartWorkshopLitMaterials.ApplyTo(scene, root);
        StartWorkshopGrime.ApplyReceivers(scene, root, f);
        StartWorkshopLighting.BakeProbe(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root;
        Debug.Log($"Start Workshop dressed. {f}");
        StartWorkshopChecks.Run();
    }

    static void Build(Transform root, StartWorkshopFrame f)
    {
        var storage = Group(root, "Storage_WestWall");
        var maintenance = Group(root, "Maintenance_EastWall");
        var services = Group(root, "Services_RearWall");
        var ceiling = Group(root, "Ducts_Ceiling");
        var exit = Group(root, "Exit_Signage");

        // West wall: storage, running north from a barrel-and-crate corner towards a crate stack in the far corner.
        var barrel = OnFloor(Prop(storage, Barrel, "Barrel_SW", f, Wall.West), f);
        Corner(barrel, f, Wall.West, Wall.South);
        var crate = OnFloor(Prop(storage, CrateShort, "Crate_SW", f, Wall.West), f);
        Face(crate, f, Wall.West);
        Along(crate, f, Wall.West, B(barrel).max.z + .15f, Anchor.Min);
        var shelfA = OnFloor(Prop(storage, ShelfA, "Shelf_A", f, Wall.West, alongWall: true), f);
        Face(shelfA, f, Wall.West);
        Along(shelfA, f, Wall.West, B(crate).max.z + .9f, Anchor.Min);
        var shelfB = OnFloor(Prop(storage, ShelfB, "Shelf_B", f, Wall.West, alongWall: true), f);
        Face(shelfB, f, Wall.West);
        Along(shelfB, f, Wall.West, B(shelfA).max.z + .8f, Anchor.Min);
        var palletA = OnFloor(Prop(storage, PalletStackA, "Pallet_Stack_A", f, Wall.West), f);
        Face(palletA, f, Wall.West);
        Along(palletA, f, Wall.West, B(shelfB).max.z + 1f, Anchor.Min);
        var palletB = OnFloor(Prop(storage, PalletStackB, "Pallet_Stack_B", f, Wall.West), f);
        Face(palletB, f, Wall.West);
        Along(palletB, f, Wall.West, B(palletA).max.z + .4f, Anchor.Min);
        var crateLong = OnFloor(Prop(storage, CrateLong, "Crate_Long_NW", f, Wall.West, alongWall: true), f);
        Corner(crateLong, f, Wall.West, Wall.North);
        var crateTop = Prop(storage, CrateShort, "Crate_Short_NW", f, Wall.West);
        Bottom(crateTop, B(crateLong).max.y);
        Corner(crateTop, f, Wall.West, Wall.North);

        // East wall: workbench first in the player's opening view, then cart, extinguisher and bin; barrels by the exit side.
        BuildWorkbench(maintenance, f, f.South + BenchSouth);
        var cart = OnFloor(Prop(maintenance, Cart, "Maintenance_Cart", f, Wall.East, alongWall: true), f);
        Face(cart, f, Wall.East, .1f);
        Along(cart, f, Wall.East, f.South + CartSouth, Anchor.Min);
        var extinguisher = Prop(maintenance, Extinguisher, "Fire_Extinguisher", f, Wall.East);
        Bottom(extinguisher, f.Floor + .9f);
        Face(extinguisher, f, Wall.East, .01f);
        Along(extinguisher, f, Wall.East, B(cart).max.z + .9f, Anchor.Min);
        Mount(extinguisher, root, Wall.East);
        var bin = OnFloor(Prop(maintenance, GarbageBin, "Garbage_Bin", f, Wall.East), f);
        Face(bin, f, Wall.East);
        Along(bin, f, Wall.East, B(extinguisher).max.z + .6f, Anchor.Min);
        var barrelA = OnFloor(Prop(maintenance, Barrel, "Barrel_NE_A", f, Wall.East), f);
        Corner(barrelA, f, Wall.East, Wall.North);
        var barrelB = OnFloor(Prop(maintenance, Barrel, "Barrel_NE_B", f, Wall.East), f);
        Face(barrelB, f, Wall.East);
        Along(barrelB, f, Wall.East, B(barrelA).min.z - .1f, Anchor.Max);

        // Rear (south) wall: fusebox control panel beside the workbench corner, low and high pipe runs.
        var fuseA = Prop(services, FuseboxA, "Fusebox_A", f, Wall.South);
        Bottom(fuseA, f.Floor + 1.15f);
        Face(fuseA, f, Wall.South, 0);
        Along(fuseA, f, Wall.South, f.East - 2.2f, Anchor.Max);
        Mount(fuseA, root, Wall.South);
        var fuseB = Prop(services, FuseboxB, "Fusebox_B", f, Wall.South);
        Bottom(fuseB, f.Floor + 1.35f);
        Face(fuseB, f, Wall.South, 0);
        Along(fuseB, f, Wall.South, B(fuseA).min.x - .35f, Anchor.Max);
        Mount(fuseB, root, Wall.South);
        var pipesLow = OnFloor(Prop(services, PipesLow, "Pipes_Low", f, Wall.South), f);
        Face(pipesLow, f, Wall.South, 0);
        Along(pipesLow, f, Wall.South, f.Center.x - 5f, Anchor.Center);
        var pipesHigh = Prop(services, PipesHigh, "Pipes_High", f, Wall.South);
        Top(pipesHigh, f.Ceiling - .3f);
        Face(pipesHigh, f, Wall.South, 0);
        Along(pipesHigh, f, Wall.South, f.Center.x + 3f, Anchor.Center);
        KeepInside(pipesLow, f);
        KeepInside(pipesHigh, f);
        Mount(pipesLow, root, Wall.South);
        Mount(pipesHigh, root, Wall.South);

        // Ceiling: one duct run spanning wall to wall over the rear third of the room.
        BuildDuctRun(ceiling, f, f.South + 7f);

        // Exit sign over the doorway, flush with the lintel face; beside the door if no lintel is found.
        var sign = Prop(exit, ExitSign, "Exit_Sign", f, Wall.North);
        if (f.FindLintel(root, out float lintel, out float lintelFace))
        {
            Bottom(sign, lintel + .25f);
            Shift(sign, 0, 0, lintelFace - WallGap - B(sign).max.z);
            Along(sign, f, Wall.North, f.OpeningCenter, Anchor.Center);
        }
        else
        {
            Debug.LogWarning("No doorway lintel found by raycast; mounting the exit sign beside the door instead.");
            Bottom(sign, f.Floor + 2.6f);
            Face(sign, f, Wall.North, WallGap);
            Along(sign, f, Wall.North, f.OpeningMinX - .8f, Anchor.Max);
        }
    }

    /// <summary>
    /// The hero steel workbench (corebuster_env_010), first-principles from primitives in project-owned finishes: a
    /// steel top and backsplash on a drawer pedestal at each end, with a lower shelf and front apron between them. It
    /// keeps the Workbench/Bench_Top names that the detail layer and <see cref="StartWorkshopWorkstation"/> anchor to.
    /// </summary>
    internal static GameObject BuildWorkbench(Transform parent, StartWorkshopFrame f, float southZ)
    {
        var finish = new StartWorkshopWorkstation.Finishes();
        var bench = new GameObject("Workbench").transform;
        bench.SetParent(parent, false);
        bench.position = new Vector3(f.East - WallGap - BenchDepth * .5f, f.Floor, southZ + BenchLength * .5f);
        float back = f.East - WallGap, front = back - BenchDepth, midX = (front + back) * .5f;
        float north = southZ + BenchLength, midZ = bench.position.z;
        float topY = f.Floor + BenchHeight, underTop = BenchHeight - BenchTopThickness;

        Block(bench, "Bench_Top", new Vector3(midX, topY - BenchTopThickness * .5f, midZ),
            new Vector3(BenchDepth, BenchTopThickness, BenchLength), finish.BareSteel);
        Block(bench, "Backsplash", new Vector3(back - .01f, topY + .06f, midZ), new Vector3(.02f, .12f, BenchLength), finish.BareSteel);
        float pedestalSouth = southZ + PedestalEndGap, pedestalNorth = north - PedestalEndGap - PedestalWidth;
        Pedestal(bench, "Pedestal_South", f, front + PedestalInset, back, pedestalSouth, underTop, finish);
        Pedestal(bench, "Pedestal_North", f, front + PedestalInset, back, pedestalNorth, underTop, finish);

        float gapSouth = pedestalSouth + PedestalWidth, gapZ = (gapSouth + pedestalNorth) * .5f, gap = pedestalNorth - gapSouth;
        Block(bench, "Lower_Shelf", new Vector3(midX + .03f, f.Floor + .155f, gapZ), new Vector3(BenchDepth - .06f, .03f, gap), finish.PaintedSteel);
        Block(bench, "Apron", new Vector3(front + .022f, f.Floor + underTop - .04f, gapZ), new Vector3(.025f, .08f, gap), finish.PaintedSteel);
        return bench.gameObject;
    }

    /// <summary>A painted-steel drawer unit under the bench top: dark carcass face, steel drawer fronts and bar handles.</summary>
    static void Pedestal(Transform bench, string name, StartWorkshopFrame f, float front, float back, float south, float height,
        StartWorkshopWorkstation.Finishes finish)
    {
        var unit = new GameObject(name).transform;
        unit.SetParent(bench, false);
        float midZ = south + PedestalWidth * .5f;
        unit.position = new Vector3((front + back) * .5f, f.Floor, midZ);
        Block(unit, "Carcass", new Vector3((front + back) * .5f, f.Floor + height * .5f, midZ),
            new Vector3(back - front, height, PedestalWidth), finish.PaintedSteel);
        // The dark face behind the fronts shows through the drawer gaps and as the kick plinth below the last drawer.
        Block(unit, "Face", new Vector3(front - .002f, f.Floor + height * .5f, midZ),
            new Vector3(.004f, height - .01f, PedestalWidth - .03f), finish.Rubber, collider: false);
        float y = f.Floor + height - DrawerGap;
        for (int i = 0; i < DrawerHeights.Length; i++)
        {
            float h = DrawerHeights[i];
            Block(unit, $"Drawer_{i + 1}", new Vector3(front - .013f, y - h * .5f, midZ),
                new Vector3(.018f, h, PedestalWidth - .03f), finish.PaintedSteel, collider: false);
            Block(unit, $"Handle_{i + 1}", new Vector3(front - .034f, y - .035f, midZ),
                new Vector3(.024f, .018f, PedestalWidth * .5f), finish.BareSteel, collider: false);
            y -= h + DrawerGap;
        }
    }

    /// <summary>A primitive box of <paramref name="size"/> (local axes) centred on <paramref name="center"/>, for
    /// first-principles props built inside an unrotated, unscaled wrapper.</summary>
    internal static GameObject Block(Transform parent, string name, Vector3 center, Vector3 size, Material material,
        bool collider = true, Quaternion? rotation = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, rotation ?? Quaternion.identity);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    /// <summary>Duct Straight pieces laid end to end between the west and east faces, stretched to fit exactly.</summary>
    static void BuildDuctRun(Transform parent, StartWorkshopFrame f, float centerZ)
    {
        var run = new GameObject("Duct_Run");
        run.transform.SetParent(parent, false);
        var probe = Prop(run.transform, DuctStraight, "Duct_Probe", f, Wall.West);
        Bounds pb = B(probe);
        // Turn the piece so its long axis runs along world X (west to east).
        float yaw = pb.size.x >= pb.size.z ? 0 : 90;
        float length = Mathf.Max(pb.size.x, pb.size.z);
        UnityEngine.Object.DestroyImmediate(probe);

        float span = f.East - f.West;
        int count = Mathf.Max(1, Mathf.CeilToInt(span / length - .05f));
        float stretch = span / (count * length);
        float x = f.West;
        for (int i = 0; i < count; i++)
        {
            var piece = Prop(run.transform, DuctStraight, $"Duct_Straight_{i + 1}", f, Wall.West);
            piece.transform.rotation = Quaternion.Euler(0, yaw, 0) * piece.transform.rotation;
            var scale = piece.transform.localScale;
            // At yaw 90 the prefab's local Z is world X.
            if (yaw == 0) scale.x *= stretch; else scale.z *= stretch;
            piece.transform.localScale = scale;
            Top(piece, f.Ceiling - .02f);
            Bounds b = B(piece);
            Shift(piece, x - b.min.x, 0, centerZ - b.center.z);
            x = B(piece).max.x;
        }
    }

    internal static Transform Group(Transform root, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    /// <summary>Instantiates a linked prefab instance facing into the room from <paramref name="wall"/> (the kit's wall-prop convention).</summary>
    internal static GameObject Prop(Transform parent, string path, string name, StartWorkshopFrame f, Wall wall, float scale = 1f, bool alongWall = false, float yawOffset = 0)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) throw new InvalidOperationException("Missing prefab: " + path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.name = name;
        // Compose with the prefab's own root rotation so authored orientation (e.g. model import axes) is kept.
        go.transform.SetPositionAndRotation(f.Center + Vector3.up * 3, Quaternion.Euler(0, Yaw(wall) + yawOffset, 0) * asset.transform.rotation);
        go.transform.localScale *= scale;
        if (alongWall)
        {
            Bounds b = B(go);
            bool alongZ = wall == Wall.West || wall == Wall.East;
            if ((alongZ ? b.size.x > b.size.z : b.size.z > b.size.x)) go.transform.Rotate(0, 90, 0, Space.World);
        }
        // Decorative props must not simulate; kinematic keeps their colliders solid without physics drift.
        foreach (var body in go.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
        // Some kit props (pipes, signage) ship without colliders; give them mesh-accurate ones on this instance only.
        if (go.GetComponentsInChildren<Collider>(true).Length == 0)
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        return go;
    }

    // Kit wall props face local +X into the room when given their wall's yaw.
    internal static float Yaw(Wall wall) => wall switch { Wall.West => 0, Wall.East => 180, Wall.South => -90, _ => 90 };

    internal static GameObject OnFloor(GameObject go, StartWorkshopFrame f) { Bottom(go, f.Floor); return go; }
    internal static void Bottom(GameObject go, float y) => Shift(go, 0, y - B(go).min.y, 0);
    internal static void Top(GameObject go, float y) => Shift(go, 0, y - B(go).max.y, 0);

    internal static void Face(GameObject go, StartWorkshopFrame f, Wall wall, float gap = WallGap)
    {
        Bounds b = B(go);
        switch (wall)
        {
            case Wall.West: Shift(go, f.West + gap - b.min.x, 0, 0); break;
            case Wall.East: Shift(go, f.East - gap - b.max.x, 0, 0); break;
            case Wall.South: Shift(go, 0, 0, f.South + gap - b.min.z); break;
            case Wall.North: Shift(go, 0, 0, f.North - gap - b.max.z); break;
        }
    }

    internal static void Corner(GameObject go, StartWorkshopFrame f, Wall a, Wall b) { Face(go, f, a); Face(go, f, b); }

    internal static void Along(GameObject go, StartWorkshopFrame f, Wall wall, float value, Anchor anchor)
    {
        Bounds b = B(go);
        bool alongZ = wall == Wall.West || wall == Wall.East;
        float min = alongZ ? b.min.z : b.min.x, max = alongZ ? b.max.z : b.max.x;
        float current = anchor == Anchor.Min ? min : anchor == Anchor.Max ? max : (min + max) * .5f;
        float delta = value - current;
        if (alongZ) Shift(go, 0, 0, delta); else Shift(go, delta, 0, 0);
    }

    /// <summary>
    /// Slides a wall-mounted prop onto the wall's real collider surface. Wall renderer bounds include pilasters that
    /// stand proud of the recessed panels, so a prop placed between them would otherwise hover off the panel.
    /// </summary>
    internal static void Mount(GameObject go, Transform exclude, Wall wall)
    {
        const int samples = 5;
        const float reach = 1f;
        Physics.SyncTransforms();
        Bounds b = B(go);
        Vector3 toWall = wall switch { Wall.West => Vector3.left, Wall.East => Vector3.right, Wall.South => Vector3.back, _ => Vector3.forward };
        Vector3 across = wall == Wall.West || wall == Wall.East ? Vector3.forward : Vector3.right;
        // Sample a grid over the prop's wall-side face (inset from its edges) and cast from inside the room.
        Vector3 back = b.center + Vector3.Scale(toWall, b.extents);
        float nearest = float.MaxValue;
        for (int i = 0; i < samples; i++)
        for (int j = 0; j < samples; j++)
        {
            float u = Mathf.Lerp(-.8f, .8f, i / (samples - 1f)), v = Mathf.Lerp(-.8f, .8f, j / (samples - 1f));
            Vector3 p = back + across * (u * Vector3.Dot(b.extents, across)) + Vector3.up * (v * b.extents.y);
            if (StartWorkshopFrame.Blocked(p - toWall * reach, toWall, reach * 3, exclude, out RaycastHit hit))
                nearest = Mathf.Min(nearest, hit.distance - reach);
        }
        if (nearest < float.MaxValue) Shift(go, toWall.x * nearest, 0, toWall.z * nearest);
        else Debug.LogWarning($"'{go.name}' found no wall collider behind it; left at the measured wall face.");
    }

    /// <summary>Pulls a prop whose true size is unknown back inside the room box.</summary>
    internal static void KeepInside(GameObject go, StartWorkshopFrame f)
    {
        Bounds b = B(go);
        Shift(go, Mathf.Max(0, f.West - b.min.x) + Mathf.Min(0, f.East - b.max.x),
            Mathf.Max(0, f.Floor - b.min.y) + Mathf.Min(0, f.Ceiling - b.max.y),
            Mathf.Max(0, f.South - b.min.z) + Mathf.Min(0, f.North - b.max.z));
    }

    internal static void Shift(GameObject go, float x, float y, float z) => go.transform.position += new Vector3(x, y, z);

    public static Bounds B(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        Bounds bounds = default;
        bool any = false;
        foreach (var r in renderers)
        {
            if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
        }
        if (!any) throw new InvalidOperationException($"'{go.name}' has no mesh renderers to place by.");
        return bounds;
    }

    internal static void ApplyStaticConvention(Transform root, StaticEditorFlags flags)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(body);
    }

    public static Transform FindLevel(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var go in scene.GetRootGameObjects()) if (go.name == LevelName) return go.transform;
        return null;
    }
}
