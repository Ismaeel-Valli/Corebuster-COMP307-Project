using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>
/// Rebuildable corebuster_env_019 pass: three work islands on the open centre floor of the Start Workshop, each its
/// own "*_Centre" group framed by a dark zone plate with a hazard edge and corner bollards.
/// <list type="bullet">
/// <item><c>Maintenance_Bay_Centre</c> (north-west): a low grated service platform carrying a partly stripped machine
/// (repainted Modular Pack rotor, capacitor and battery) under a yellow hoist gantry, the room's tall centre silhouette,
/// with tool trolleys, a removed cover panel and parts crates beside it.</item>
/// <item><c>Staging_Centre</c> (east of the route): loaded pallets, a tall crate stack and hard cases waiting for
/// pickup, screened from the route by two low Modular Pack half-wall partitions.</item>
/// <item><c>Parts_Sorting_Centre</c> (south-west): back-to-back sorting benches with parts bins, a bin rack, a scrap
/// cage and totes.</item>
/// </list>
/// Each island is laid out around a preferred spot and then slid to the nearest place, within
/// <see cref="SearchRadius"/>, that keeps every keep-out of <see cref="StartWorkshopFrame"/> and a
/// <see cref="LaneWidth"/> combat lane to every other floor prop, other islands included. Each island carries a carving
/// <see cref="NavMeshObstacle"/> over its footprint, so enemies path around it on the scene's existing NavMesh; the
/// scene-wide NavMeshSurface is not rebaked here, because a rebake would change every other area's NavMesh too
/// (corebuster_env_003 owns the final NavMesh review). The pass never moves another pass's props. Machines, pallets,
/// crates and partitions are pack prefabs; everything else is first-principles primitives in
/// <see cref="StartWorkshopWorkstation.Finishes"/>. No name here contains "Wall" (see <see cref="StartWorkshopFrame.Measure"/>).
/// </summary>
public static class StartWorkshopCentre
{
    public const string GroupSuffix = "_Centre";
    const string UndoName = "Build Start Workshop Centre";

    const string MaintenanceBay = "Maintenance_Bay" + GroupSuffix;
    const string Staging = "Staging" + GroupSuffix;
    const string PartsSorting = "Parts_Sorting" + GroupSuffix;
    public const string PlateName = "Zone_Plate";

    const string Kit = "Assets/SciFi Warehouse Kit/Prefabs/";
    const string Barrel = Kit + "Props/Crates Barrels Pallets/Barrel.prefab";
    const string CrateLong = Kit + "Props/Crates Barrels Pallets/Crate Long.prefab";
    const string CrateShort = Kit + "Props/Crates Barrels Pallets/Crate Short.prefab";
    // Pallet variants without Rigidbodies (see StartWorkshopDetailer): 09 plain stack, 02 loaded with crates, 05 with barrels.
    const string PalletBase = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 09.prefab";
    const string PalletCrates = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 02.prefab";
    const string PalletBarrels = Kit + "Props/Crates Barrels Pallets/Pallet Variations/Pallet Variation 05.prefab";
    const string Modular = "Assets/Sci-Fi Styled Modular Pack/Prefabs/";
    const string Rotor = Modular + "Machines/rotor.prefab";
    const string Capacitor = Modular + "Machines/Capacitor.prefab";
    const string BatteryBig = Modular + "Machines/Battery_big.prefab";
    const string HalfPartition = Modular + "Walls/Half walls/decorative_half_wall_5.prefab";

    // Preferred island centres, from the measured room corners; each island slides from here to the nearest fit.
    static readonly Vector2 BayFromSouthWest = new Vector2(10.6f, 23.8f);
    static readonly Vector2 StagingFromSouthEast = new Vector2(-7.5f, 16.5f);
    static readonly Vector2 SortingFromSouthWest = new Vector2(7.9f, 7.2f);

    // Combat lane kept between an island's props and every other floor prop, other islands included.
    public const float LaneWidth = 3f;
    // Placement keeps this much more than the validator requires, so float noise cannot fail a fresh build.
    const float LaneSlack = .05f;
    const float SearchRadius = 4f, SearchStep = .25f;
    const float WallMargin = .5f;
    // Heights: islands stay low, with one or two taller silhouettes.
    public const float LowHeight = 1.5f;
    const int MinTall = 1, MaxTall = 2;
    const int MinIslands = 2, MaxIslands = 4, MinIslandProps = 4;
    // The carving obstacle covers each island's footprint from the floor to this height.
    const float CarveHeight = 2f;
    const float EyeHeight = 1.6f;

    // Zone plate: a 2 cm dark plate edged in 1.4 m hazard tiles (StartWorkshopWorkstation.Finishes.HazardEdge is
    // tiled for a 1.4 x 0.08 m strip), so plates are 1.4 m multiples long plus the two edge widths across.
    const float PlateThickness = .02f, EdgeTile = 1.4f, EdgeWidth = .08f, EdgeThickness = .004f;
    const float BollardInset = .25f, BollardRadius = .08f, BollardHeight = 1f;
    // Service platform: 4.2 x 3.0 m, its grating 0.22 m above the plate.
    const float PlatformLength = 4.2f, PlatformDepth = 3f, PlatformFrame = .2f, GratingDepth = .02f, NosingWidth = .08f;
    const float GantryHalfSpan = 1.35f, GantryHeight = 3f, HookAboveDeck = 1.95f;
    const float PartitionHeight = 1.2f, PartitionLength = 1.8f, PartitionMaxThickness = .5f;

    [MenuItem("Tools/CoreBuster/Build Start Workshop Centre")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; the centre islands need {LevelName}/{ContainerName}.");
            return;
        }
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        RebuildIn(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(MaintenanceBay);
        StartWorkshopChecks.Run();
    }

    /// <summary>
    /// Batch entry point, run without -nographics (the probe bake renders):
    /// Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod StartWorkshopCentre.BuildBatch -quit.
    /// Opens MainScene, rebuilds the islands, applies the lit materials, rebakes the probe, saves the scene and validates.
    /// </summary>
    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var level = FindLevel(scene);
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"No {LevelName}/{ContainerName} in {ScenePath}; run Tools/CoreBuster/Dress Start Workshop first.");
            EditorApplication.Exit(1);
            return;
        }
        RebuildIn(scene, root);
        EditorSceneManager.SaveScene(scene);
        StartWorkshopChecks.Run();
    }

    static void RebuildIn(Scene scene, Transform root)
    {
        foreach (var old in Islands(root)) Undo.DestroyObjectImmediate(old.gameObject);
        var f = StartWorkshopFrame.Measure(scene, root);
        int first = root.childCount;
        try
        {
            Build(root, f);
            var flags = GameObjectUtility.GetStaticEditorFlags(root.parent.gameObject);
            for (int i = first; i < root.childCount; i++) ApplyStaticConvention(root.GetChild(i), flags);
        }
        catch
        {
            for (int i = root.childCount - 1; i >= first; i--) UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
            throw;
        }
        for (int i = first; i < root.childCount; i++) Undo.RegisterCreatedObjectUndo(root.GetChild(i).gameObject, UndoName);
        // The islands are new geometry in the room's reflections.
        StartWorkshopLitMaterials.ApplyTo(scene, root);
        StartWorkshopLighting.BakeProbe(scene, root);
    }

    /// <summary>Called by the full workshop rebuild after the pipework, and by the isolated menu action.</summary>
    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (Islands(root).Count > 0)
            throw new InvalidOperationException($"Centre islands (groups ending {GroupSuffix}) already exist. Remove them before rebuilding the centre pass.");
        var m = new StartWorkshopWorkstation.Finishes();
        var report = new StringBuilder("Start Workshop centre islands:");
        var bay = BuildMaintenanceBay(root, f, At(f, f.West + BayFromSouthWest.x, f.South + BayFromSouthWest.y), m);
        Settle(bay, root, f, report);
        var staging = BuildStaging(root, f, At(f, f.East + StagingFromSouthEast.x, f.South + StagingFromSouthEast.y), m);
        Settle(staging, root, f, report);
        var sorting = BuildPartsSorting(root, f, At(f, f.West + SortingFromSouthWest.x, f.South + SortingFromSouthWest.y), m);
        Settle(sorting, root, f, report);
        Debug.Log(report.ToString());
    }

    static Vector3 At(StartWorkshopFrame f, float x, float z) => new Vector3(x, f.Floor, z);

    // ---------------------------------------------------------------------------------------------------------------
    // Islands. Offsets are from the island centre o (on the floor); +X is east and +Z is north.

    static Transform BuildMaintenanceBay(Transform root, StartWorkshopFrame f, Vector3 o, StartWorkshopWorkstation.Finishes m)
    {
        const int tilesX = 5, tilesZ = 4;
        var island = Island(root, MaintenanceBay, o);
        float top = ZonePlate(island, o, tilesX, tilesZ, m);
        float deck = ServicePlatform(island, new Vector3(o.x - .8f, top, o.z), m);
        Machine(island, Rotor, "Machine_Rotor", f, new Vector3(o.x - 1.8f, deck, o.z), new Vector3(1.5f, 1.2f, 1.5f), StartWorkshopDetailer.DarkPalette);
        Machine(island, Capacitor, "Machine_Capacitor", f, new Vector3(o.x + .3f, deck, o.z - .75f), new Vector3(1.1f, 1.2f, 1.1f), StartWorkshopDetailer.DarkPalette);
        Machine(island, BatteryBig, "Machine_Battery", f, new Vector3(o.x + .3f, deck, o.z + .75f), new Vector3(1.1f, 1.2f, 1.1f), StartWorkshopDetailer.AccentPalette);
        ServiceGantry(island, new Vector3(o.x - 1.8f, deck, o.z), m);
        RemovedPanel(island, new Vector3(o.x - 1.8f, top, o.z - 2.15f), m);
        KitProp(island, CrateShort, "Parts_Crate", f, new Vector3(o.x - .2f, top, o.z - 2.15f));
        KitProp(island, Barrel, "Coolant_Barrel", f, new Vector3(o.x - 2.4f, top, o.z + 2.15f));
        KitProp(island, CrateLong, "Parts_Crate_Long", f, new Vector3(o.x - .6f, top, o.z + 2.2f), longAlongX: true);
        ToolTrolley(island, "Tool_Trolley_A", new Vector3(o.x + 2.5f, top, o.z - 1.7f), m);
        ToolTrolley(island, "Tool_Trolley_B", new Vector3(o.x + 2.5f, top, o.z + 1.2f), m);
        Bollards(island, o, tilesX, tilesZ, top, m);
        return island;
    }

    static Transform BuildStaging(Transform root, StartWorkshopFrame f, Vector3 o, StartWorkshopWorkstation.Finishes m)
    {
        const int tilesX = 4, tilesZ = 4;
        var island = Island(root, Staging, o);
        float top = ZonePlate(island, o, tilesX, tilesZ, m);
        KitProp(island, PalletCrates, "Pallet_Crates", f, new Vector3(o.x + 1.4f, top, o.z - 1.85f));
        KitProp(island, PalletBarrels, "Pallet_Barrels", f, new Vector3(o.x + 1.4f, top, o.z));
        TallStack(island, "Pallet_Tall_Stack", f, new Vector3(o.x + 1.4f, top, o.z + 1.9f));
        // The partitions face the route: cover and a sightline break that runs beside it, never across it.
        Partition(island, "Partition_South", f, new Vector3(o.x - 2.1f, top, o.z - 1.35f), m);
        Partition(island, "Partition_North", f, new Vector3(o.x - 2.1f, top, o.z + 1.35f), m);
        var caseA = HardCase(island, "Hard_Case_A", new Vector3(o.x - .5f, top, o.z - 1.7f), new Vector3(.6f, .5f, 1f), m);
        HardCase(island, "Hard_Case_B", new Vector3(o.x - .5f, B(caseA).max.y, o.z - 1.7f), new Vector3(.5f, .4f, .8f), m);
        HardCase(island, "Hard_Case_C", new Vector3(o.x - .5f, top, o.z + .6f), new Vector3(.55f, .6f, .9f), m);
        HardCase(island, "Hard_Case_D", new Vector3(o.x - .5f, top, o.z + 1.85f), new Vector3(.45f, .35f, .6f), m);
        Bollards(island, o, tilesX, tilesZ, top, m);
        return island;
    }

    static Transform BuildPartsSorting(Transform root, StartWorkshopFrame f, Vector3 o, StartWorkshopWorkstation.Finishes m)
    {
        const int tilesX = 4, tilesZ = 3;
        var island = Island(root, PartsSorting, o);
        float top = ZonePlate(island, o, tilesX, tilesZ, m);
        SortingBench(island, "Sorting_Bench_A", new Vector3(o.x - .35f, top, o.z - .46f), faceSouth: true, m);
        SortingBench(island, "Sorting_Bench_B", new Vector3(o.x - .35f, top, o.z + .46f), faceSouth: false, m);
        BinRack(island, "Bin_Rack", new Vector3(o.x + 1.75f, top, o.z), m);
        ScrapCage(island, "Scrap_Cage", new Vector3(o.x + 1.8f, top, o.z + 1.4f), m);
        KitProp(island, Barrel, "Sorting_Barrel", f, new Vector3(o.x + 1.8f, top, o.z - 1.45f));
        var tote = KitProp(island, CrateLong, "Tote_Crate_Long", f, new Vector3(o.x - 2.2f, top, o.z + 1.05f), longAlongZ: true);
        KitProp(island, CrateShort, "Tote_Crate_Short", f, new Vector3(o.x - 2.2f, B(tote).max.y, o.z + 1.05f));
        KitProp(island, CrateShort, "Tote_Crate_Loose", f, new Vector3(o.x - 2.2f, top, o.z - 1.3f));
        Bollards(island, o, tilesX, tilesZ, top, m);
        return island;
    }

    static Transform Island(Transform root, string name, Vector3 o)
    {
        var island = Group(root, name);
        island.position = o;
        return island;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Placement

    /// <summary>
    /// Slides a built island to the nearest offset (0.25 m grid, nearest first) at which every prop is inside the
    /// room, clear of the keep-outs and at least a lane away from every other floor prop; then fails if its own props
    /// intersect and gives it its carving NavMesh obstacle.
    /// </summary>
    static void Settle(Transform island, Transform root, StartWorkshopFrame f, StringBuilder report)
    {
        var mine = new List<Bounds>();
        foreach (Transform prop in island) mine.Add(B(prop.gameObject));
        var others = FloorProps(root, f, island).Select(p => p.b).ToList();
        Vector3? found = null;
        foreach (var d in Offsets())
            if (Fits(mine, others, f, d)) { found = d; break; }
        if (found == null)
            throw new InvalidOperationException(FormattableString.Invariant(
                $"No place for {island.name} within {SearchRadius} m of ({island.position.x:F2}, {island.position.z:F2}) that keeps the keep-outs and a {LaneWidth} m lane to every other floor prop."));
        island.position += found.Value;
        CheckOwnProps(island);
        Bounds fp = Footprint(island, f);
        Carve(island, f, fp);
        report.AppendLine().Append(FormattableString.Invariant(
            $"  {island.name}: {island.childCount} props, footprint X [{fp.min.x:F2}, {fp.max.x:F2}] Z [{fp.min.z:F2}, {fp.max.z:F2}], slid {found.Value.x:F2}, {found.Value.z:F2} m"));
    }

    static IEnumerable<Vector3> Offsets()
    {
        int n = Mathf.RoundToInt(SearchRadius / SearchStep);
        var offsets = new List<Vector3>();
        for (int i = -n; i <= n; i++)
        for (int j = -n; j <= n; j++)
        {
            var d = new Vector3(i * SearchStep, 0, j * SearchStep);
            if (d.magnitude <= SearchRadius + .001f) offsets.Add(d);
        }
        return offsets.OrderBy(d => d.sqrMagnitude).ThenBy(d => d.x).ThenBy(d => d.z);
    }

    static bool Fits(List<Bounds> mine, List<Bounds> others, StartWorkshopFrame f, Vector3 d)
    {
        foreach (var original in mine)
        {
            var b = new Bounds(original.center + d, original.size);
            if (b.min.x < f.West + WallMargin || b.max.x > f.East - WallMargin || b.min.z < f.South + WallMargin || b.max.z > f.North - WallMargin)
                return false;
            if (f.KeepOutViolation(b) != null) return false;
            if (!f.IsReachable(b)) continue;
            foreach (var other in others)
                if (Gap(b, other) < LaneWidth + LaneSlack) return false;
        }
        return true;
    }

    /// <summary>The builder's own layout must not make its props intersect (the validator's rule, checked early).</summary>
    static void CheckOwnProps(Transform island)
    {
        var props = island.Cast<Transform>().ToList();
        var clashes = new List<string>();
        for (int i = 0; i < props.Count; i++)
            for (int j = i + 1; j < props.Count; j++)
                if (StartWorkshopChecks.Overlap(B(props[i].gameObject), B(props[j].gameObject)) && StartWorkshopChecks.PartsOverlap(props[i], props[j]))
                    clashes.Add($"{island.name}/{props[i].name} intersects {props[j].name}");
        if (clashes.Count > 0)
            throw new InvalidOperationException("The centre island layout intersects itself (a pack prefab may be larger than expected):\n  " + string.Join("\n  ", clashes));
    }

    /// <summary>A box NavMeshObstacle over the island's footprint that carves the existing NavMesh while it stands still.</summary>
    static void Carve(Transform island, StartWorkshopFrame f, Bounds footprint)
    {
        var obstacle = island.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        obstacle.center = new Vector3(footprint.center.x - island.position.x, f.Floor + CarveHeight * .5f - island.position.y,
            footprint.center.z - island.position.z);
        obstacle.size = new Vector3(footprint.size.x, CarveHeight, footprint.size.z);
    }

    /// <summary>The union of the island's body-height prop bounds.</summary>
    static Bounds Footprint(Transform island, StartWorkshopFrame f)
    {
        Bounds fp = default;
        bool any = false;
        foreach (Transform prop in island)
            if (TryBounds(prop, out Bounds b) && f.IsReachable(b))
            {
                if (any) fp.Encapsulate(b); else { fp = b; any = true; }
            }
        return fp;
    }

    /// <summary>Body-height prop bounds of every group except <paramref name="except"/> and the non-prop groups.</summary>
    internal static List<(string name, Bounds b)> FloorProps(Transform root, StartWorkshopFrame f, Transform except)
    {
        var props = new List<(string, Bounds)>();
        foreach (Transform group in root)
        {
            // As in StartWorkshopChecks: overlays, the collider-free exit portal, liners and the rendering controls are not
            // props; the lighting pass's lamps hang above body height.
            if (group == except || group.name == StartWorkshopGraphics.GroupName || group.name == StartWorkshopPortal.GroupName ||
                StartWorkshopLiners.IsGroup(group) || StartWorkshopRendering.IsGroup(group) || StartWorkshopLighting.IsGroup(group) ||
                StartWorkshopSignage.IsGroup(group) || StartWorkshopFloor.IsGroup(group)) continue;
            foreach (Transform prop in group)
                if (TryBounds(prop, out Bounds b) && f.IsReachable(b)) props.Add(($"{group.name}/{prop.name}", b));
        }
        return props;
    }

    /// <summary>The horizontal gap between two boxes (0 when their footprints overlap).</summary>
    public static float Gap(Bounds a, Bounds b)
    {
        float dx = Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x, 0);
        float dz = Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z, 0);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static bool TryBounds(Transform t, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
        }
        return any;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Pieces. Each builder makes one direct child of its island (one validator prop) from world-space primitives.

    /// <summary>The island's floor zone: a dark plate edged with hazard tiles. Returns the plate's top.</summary>
    static float ZonePlate(Transform island, Vector3 o, int tilesX, int tilesZ, StartWorkshopWorkstation.Finishes m)
    {
        var plate = Wrapper(island, PlateName, o);
        float sizeX = tilesX * EdgeTile, innerZ = tilesZ * EdgeTile, sizeZ = innerZ + 2 * EdgeWidth;
        float top = o.y + PlateThickness, edgeY = top + EdgeThickness * .5f;
        Block(plate, "Plate", o + Vector3.up * (PlateThickness * .5f), new Vector3(sizeX, PlateThickness, sizeZ), m.Rubber);
        // The north and south edges run the full length; the east and west ones fit between them, turned so the
        // stripe runs along them.
        for (int i = 0; i < tilesX; i++)
        {
            float x = o.x - sizeX * .5f + EdgeTile * (i + .5f);
            foreach (float side in new[] { -1f, 1f })
                Block(plate, $"Edge_{(side < 0 ? "South" : "North")}_{i + 1}", new Vector3(x, edgeY, o.z + side * (sizeZ - EdgeWidth) * .5f),
                    new Vector3(EdgeTile, EdgeThickness, EdgeWidth), m.HazardEdge, collider: false);
        }
        for (int j = 0; j < tilesZ; j++)
        {
            float z = o.z - innerZ * .5f + EdgeTile * (j + .5f);
            foreach (float side in new[] { -1f, 1f })
                Block(plate, $"Edge_{(side < 0 ? "West" : "East")}_{j + 1}", new Vector3(o.x + side * (sizeX - EdgeWidth) * .5f, edgeY, z),
                    new Vector3(EdgeTile, EdgeThickness, EdgeWidth), m.HazardEdge, collider: false, rotation: Quaternion.Euler(0, 90, 0));
        }
        return top;
    }

    /// <summary>A yellow bollard just inside each corner of the island's zone plate.</summary>
    static void Bollards(Transform island, Vector3 o, int tilesX, int tilesZ, float top, StartWorkshopWorkstation.Finishes m)
    {
        float hx = tilesX * EdgeTile * .5f - BollardInset, hz = (tilesZ * EdgeTile + 2 * EdgeWidth) * .5f - BollardInset;
        foreach (float sz in new[] { -1f, 1f })
        foreach (float sx in new[] { -1f, 1f })
        {
            string name = $"Bollard_{(sz < 0 ? "S" : "N")}{(sx < 0 ? "W" : "E")}";
            var p = new Vector3(o.x + sx * hx, top, o.z + sz * hz);
            var bollard = Wrapper(island, name, p);
            Block(bollard, "Base_Plate", p + Vector3.up * .01f, new Vector3(.26f, .02f, .26f), m.BareSteel, collider: false);
            Post(bollard, "Post", p + Vector3.up * .02f, BollardRadius, BollardHeight - .02f, m.Yellow);
            StartWorkshopWorkstation.Rod(bollard, "Band", p + Vector3.up * (BollardHeight - .2f), Vector3.up, BollardRadius + .003f, .08f, m.Rubber);
        }
    }

    /// <summary>
    /// A low grated platform: a painted-steel frame (its collider), yellow nosing and skirts, steel grating bars over
    /// a dark void and a step on its east side. Returns the grating's top.
    /// </summary>
    static float ServicePlatform(Transform island, Vector3 c, StartWorkshopWorkstation.Finishes m)
    {
        var platform = Wrapper(island, "Service_Platform", c);
        float deck = c.y + PlatformFrame + GratingDepth, hx = PlatformLength * .5f, hz = PlatformDepth * .5f;
        Block(platform, "Frame", c + Vector3.up * (PlatformFrame * .5f), new Vector3(PlatformLength, PlatformFrame, PlatformDepth), m.PaintedSteel);
        foreach (float side in new[] { -1f, 1f })
        {
            Block(platform, side < 0 ? "Skirt_South" : "Skirt_North", new Vector3(c.x, c.y + PlatformFrame * .5f, c.z + side * (hz + .005f)),
                new Vector3(PlatformLength, PlatformFrame, .01f), m.Yellow, collider: false);
            Block(platform, side < 0 ? "Nosing_South" : "Nosing_North", new Vector3(c.x, deck - GratingDepth * .5f, c.z + side * (hz - NosingWidth * .5f)),
                new Vector3(PlatformLength, GratingDepth, NosingWidth), m.Yellow, collider: false);
            Block(platform, side < 0 ? "Nosing_West" : "Nosing_East", new Vector3(c.x + side * (hx - NosingWidth * .5f), deck - GratingDepth * .5f, c.z),
                new Vector3(NosingWidth, GratingDepth, PlatformDepth - 2 * NosingWidth), m.Yellow, collider: false);
        }
        float innerX = PlatformLength - 2 * NosingWidth, innerZ = PlatformDepth - 2 * NosingWidth;
        Block(platform, "Grating_Void", new Vector3(c.x, c.y + PlatformFrame + .001f, c.z), new Vector3(innerX, .002f, innerZ), m.Rubber, collider: false);
        const float pitch = .1f;
        int bars = Mathf.FloorToInt(innerZ / pitch);
        for (int i = 0; i < bars; i++)
            Block(platform, $"Grating_Bar_{i + 1}", new Vector3(c.x, deck - GratingDepth * .5f, c.z - (bars - 1) * pitch * .5f + i * pitch),
                new Vector3(innerX, GratingDepth, .025f), m.BareSteel, collider: false);
        const float stepDepth = .32f, stepHeight = .11f, stepWidth = 1.2f;
        float stepX = c.x + hx + stepDepth * .5f;
        Block(platform, "Step", new Vector3(stepX, c.y + stepHeight * .5f, c.z), new Vector3(stepDepth, stepHeight, stepWidth), m.PaintedSteel);
        Block(platform, "Step_Nosing", new Vector3(stepX + stepDepth * .5f - .025f, c.y + stepHeight + .0025f, c.z),
            new Vector3(.05f, .005f, stepWidth), m.Yellow, collider: false);
        return deck;
    }

    /// <summary>A yellow portal gantry straddling the machine along Z, with a trolley, chain hoist and hook over it.</summary>
    static void ServiceGantry(Transform island, Vector3 c, StartWorkshopWorkstation.Finishes m)
    {
        var gantry = Wrapper(island, "Service_Gantry", c);
        float foot = c.y + .02f, beamBottom = foot + GantryHeight;
        foreach (float side in new[] { -1f, 1f })
        {
            string end = side < 0 ? "South" : "North";
            float z = c.z + side * GantryHalfSpan;
            Block(gantry, $"Foot_{end}", new Vector3(c.x, c.y + .01f, z), new Vector3(.3f, .02f, .3f), m.BareSteel, collider: false);
            Block(gantry, $"Upright_{end}", new Vector3(c.x, foot + GantryHeight * .5f, z), new Vector3(.14f, GantryHeight, .14f), m.Yellow);
            // Knee brace from the upright up to the beam, its top leaning in toward the span.
            Block(gantry, $"Brace_{end}", new Vector3(c.x, beamBottom - .2f, z - side * .2f), new Vector3(.07f, .5f, .07f), m.Yellow,
                collider: false, rotation: Quaternion.Euler(-side * 45, 0, 0));
        }
        Block(gantry, "Beam", new Vector3(c.x, beamBottom + .1f, c.z), new Vector3(.16f, .2f, 2 * GantryHalfSpan + .3f), m.Yellow, collider: false);
        Block(gantry, "Trolley", new Vector3(c.x, beamBottom - .06f, c.z), new Vector3(.26f, .12f, .3f), m.BareSteel, collider: false);
        float hoistTop = beamBottom - .12f, hoistBottom = hoistTop - .32f, hookTop = c.y + HookAboveDeck;
        Block(gantry, "Hoist", new Vector3(c.x, (hoistTop + hoistBottom) * .5f, c.z), new Vector3(.24f, .32f, .2f), m.Red, collider: false);
        StartWorkshopWorkstation.Rod(gantry, "Chain", new Vector3(c.x, (hoistBottom + hookTop) * .5f, c.z), Vector3.up, .012f, hoistBottom - hookTop, m.BareSteel);
        Block(gantry, "Hook_Block", new Vector3(c.x, hookTop - .08f, c.z), new Vector3(.1f, .16f, .06f), m.Yellow, collider: false);
    }

    /// <summary>A repainted Modular Pack machine scaled uniformly to fit <paramref name="max"/>, standing on <paramref name="bottomCentre"/>.</summary>
    static void Machine(Transform island, string path, string name, StartWorkshopFrame f, Vector3 bottomCentre, Vector3 max, string palette)
    {
        var go = StartWorkshopDetailer.Pack(island, path, name, f, Wall.South, palette: palette);
        Bounds b = B(go);
        go.transform.localScale *= Mathf.Min(max.x / b.size.x, Mathf.Min(max.y / b.size.y, max.z / b.size.z));
        Place(go, bottomCentre);
    }

    /// <summary>The machine's removed cover plate, bolt heads up, lying on the zone plate.</summary>
    static void RemovedPanel(Transform island, Vector3 bottomCentre, StartWorkshopWorkstation.Finishes m)
    {
        var panel = Wrapper(island, "Removed_Panel", bottomCentre);
        const float sx = 1.1f, sy = .03f, sz = .7f;
        Block(panel, "Plate", bottomCentre + Vector3.up * (sy * .5f), new Vector3(sx, sy, sz), m.PaintedSteel);
        int n = 0;
        foreach (float x in new[] { -.49f, 0f, .49f })
        foreach (float z in new[] { -.3f, .3f })
            StartWorkshopWorkstation.Rod(panel, $"Bolt_{++n}", bottomCentre + new Vector3(x, sy + .005f, z), Vector3.up, .015f, .01f, m.BareSteel);
    }

    /// <summary>A red two-shelf tool trolley on casters, with a push bar at its east end and tools on the top tray.</summary>
    static void ToolTrolley(Transform island, string name, Vector3 c, StartWorkshopWorkstation.Finishes m)
    {
        var trolley = Wrapper(island, name, c);
        const float length = .9f, width = .5f, topY = .86f, lowY = .26f, tray = .03f, postBottom = .11f;
        Block(trolley, "Top_Tray", c + Vector3.up * topY, new Vector3(length, tray, width), m.Red);
        Block(trolley, "Lower_Shelf", c + Vector3.up * lowY, new Vector3(length, tray, width), m.Red);
        float lipY = c.y + topY + tray * .5f + .02f;
        foreach (float side in new[] { -1f, 1f })
        {
            Block(trolley, side < 0 ? "Lip_South" : "Lip_North", new Vector3(c.x, lipY, c.z + side * (width * .5f - .01f)), new Vector3(length, .04f, .02f), m.Red, collider: false);
            Block(trolley, side < 0 ? "Lip_West" : "Lip_East", new Vector3(c.x + side * (length * .5f - .01f), lipY, c.z), new Vector3(.02f, .04f, width - .04f), m.Red, collider: false);
        }
        int n = 0;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
        {
            var p = new Vector3(c.x + sx * (length * .5f - .04f), 0, c.z + sz * (width * .5f - .04f));
            n++;
            float postHeight = topY - tray * .5f - postBottom;
            Block(trolley, $"Post_{n}", new Vector3(p.x, c.y + postBottom + postHeight * .5f, p.z), new Vector3(.03f, postHeight, .03f), m.BareSteel, collider: false);
            Block(trolley, $"Caster_Plate_{n}", new Vector3(p.x, c.y + postBottom - .005f, p.z), new Vector3(.06f, .01f, .06f), m.BareSteel, collider: false);
            StartWorkshopWorkstation.Rod(trolley, $"Caster_Wheel_{n}", new Vector3(p.x, c.y + .05f, p.z), Vector3.right, .05f, .03f, m.Rubber);
        }
        float barX = c.x + length * .5f + .06f, barY = c.y + topY + .12f;
        foreach (float side in new[] { -1f, 1f })
            Block(trolley, side < 0 ? "Bar_Standoff_South" : "Bar_Standoff_North", new Vector3(c.x + length * .5f + .03f, barY - .06f, c.z + side * .18f),
                new Vector3(.06f, .15f, .025f), m.BareSteel, collider: false);
        Block(trolley, "Push_Bar", new Vector3(barX, barY, c.z), new Vector3(.025f, .025f, .44f), m.BareSteel, collider: false);

        float trayTop = c.y + topY + tray * .5f;
        Block(trolley, "Drill_Case", new Vector3(c.x - .2f, trayTop + .05f, c.z - .02f), new Vector3(.34f, .1f, .24f), m.Rubber, collider: false);
        Block(trolley, "Socket_Tray", new Vector3(c.x + .2f, trayTop + .015f, c.z - .13f), new Vector3(.25f, .03f, .12f), m.Yellow, collider: false);
        Block(trolley, "Wrench_1", new Vector3(c.x + .18f, trayTop + .006f, c.z + .08f), new Vector3(.22f, .012f, .03f), m.BareSteel,
            collider: false, rotation: Quaternion.Euler(0, 8, 0));
        Block(trolley, "Wrench_2", new Vector3(c.x + .2f, trayTop + .006f, c.z + .15f), new Vector3(.18f, .012f, .026f), m.BareSteel,
            collider: false, rotation: Quaternion.Euler(0, -5, 0));
        Block(trolley, "Parts_Bin", new Vector3(c.x, c.y + lowY + tray * .5f + .07f, c.z), new Vector3(.35f, .14f, .3f), m.Yellow, collider: false);
    }

    /// <summary>
    /// A 2.8 x 0.8 m steel sorting bench facing south or north: a back upstand, a row of filled parts bins along the
    /// back, loose parts at the front and three large bins on the lower shelf.
    /// </summary>
    static void SortingBench(Transform island, string name, Vector3 c, bool faceSouth, StartWorkshopWorkstation.Finishes m)
    {
        var bench = Wrapper(island, name, c);
        const float length = 2.8f, depth = .8f, height = .9f, thickness = .04f;
        // s points from the front edge toward the back edge.
        float s = faceSouth ? 1 : -1, front = c.z - s * depth * .5f, back = c.z + s * depth * .5f;
        float top = c.y + height;
        Block(bench, "Top", new Vector3(c.x, top - thickness * .5f, c.z), new Vector3(length, thickness, depth), m.BareSteel);
        Block(bench, "Apron", new Vector3(c.x, top - thickness - .04f, front + s * .015f), new Vector3(length - .1f, .08f, .02f), m.PaintedSteel, collider: false);
        int n = 0;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Block(bench, $"Leg_{++n}", new Vector3(c.x + sx * (length * .5f - .06f), c.y + (height - thickness) * .5f, c.z + sz * (depth * .5f - .06f)),
                new Vector3(.05f, height - thickness, .05f), m.PaintedSteel, collider: false);
        Block(bench, "Lower_Shelf", new Vector3(c.x, c.y + .2f, c.z), new Vector3(length - .16f, .025f, depth - .12f), m.PaintedSteel);
        Block(bench, "Upstand", new Vector3(c.x, top + .125f, back - s * .01f), new Vector3(length, .25f, .02f), m.PaintedSteel, collider: false);

        var colours = new[] { m.Yellow, m.Red, m.Yellow, m.PaintedSteel, m.Yellow, m.Red, m.Yellow };
        const float binW = .3f, binH = .13f, binD = .22f, binGap = .08f;
        float rowZ = back - s * (.03f + binD * .5f), startX = c.x - (colours.Length * binW + (colours.Length - 1) * binGap) * .5f + binW * .5f;
        for (int i = 0; i < colours.Length; i++)
            Bin(bench, $"Bin_{i + 1}", new Vector3(startX + i * (binW + binGap), top, rowZ), new Vector3(binW, binH, binD), colours[i], m);
        float partsZ = front + s * .2f;
        for (int i = 0; i < 5; i++)
            StartWorkshopWorkstation.Rod(bench, $"Nut_{i + 1}", new Vector3(c.x - .5f + i * .09f, top + .005f, partsZ + (i % 2) * .05f), Vector3.up, .014f, .01f, m.BareSteel);
        Block(bench, "Parts_Tray", new Vector3(c.x + .6f, top + .015f, partsZ), new Vector3(.4f, .03f, .25f), m.Yellow, collider: false);
        float shelfTop = c.y + .2f + .0125f;
        for (int i = 0; i < 3; i++)
            Bin(bench, $"Shelf_Bin_{i + 1}", new Vector3(c.x + (i - 1) * .8f, shelfTop, c.z), new Vector3(.6f, .2f, .45f), i == 1 ? m.Red : m.Yellow, m);
    }

    /// <summary>A louvred bin rack facing west: four rows of three filled bins on a painted-steel panel.</summary>
    static void BinRack(Transform island, string name, Vector3 c, StartWorkshopWorkstation.Finishes m)
    {
        var rack = Wrapper(island, name, c);
        const float width = 1.3f, height = 1.4f, feet = .45f;
        float backX = c.x + .2f;
        Block(rack, "Panel", new Vector3(backX - .015f, c.y + .05f + (height - .05f) * .5f, c.z), new Vector3(.03f, height - .05f, width - .1f), m.PaintedSteel);
        foreach (float side in new[] { -1f, 1f })
        {
            string end = side < 0 ? "South" : "North";
            Block(rack, $"Post_{end}", new Vector3(backX - .025f, c.y + height * .5f, c.z + side * (width * .5f - .025f)), new Vector3(.05f, height, .05f), m.PaintedSteel, collider: false);
            Block(rack, $"Foot_{end}", new Vector3(c.x, c.y + .02f, c.z + side * (width * .5f - .025f)), new Vector3(feet, .04f, .06f), m.BareSteel, collider: false);
        }
        var rows = new[] { m.Yellow, m.Red, m.Yellow, m.PaintedSteel };
        for (int row = 0; row < rows.Length; row++)
        {
            float y = c.y + .15f + row * .32f;
            // The louvre rail each row of bins hangs on, just under it.
            Block(rack, $"Louvre_{row + 1}", new Vector3(backX - .04f, y - .015f, c.z), new Vector3(.02f, .03f, width - .1f), m.BareSteel, collider: false);
            for (int col = 0; col < 3; col++)
                Bin(rack, $"Bin_{row + 1}_{col + 1}", new Vector3(backX - .03f - .16f, y, c.z + (col - 1) * .4f), new Vector3(.32f, .16f, .34f), rows[row], m);
        }
    }

    /// <summary>A wire-sided scrap cage on a steel pan, part-filled with offcuts. One box collider covers it.</summary>
    static void ScrapCage(Transform island, string name, Vector3 c, StartWorkshopWorkstation.Finishes m)
    {
        var cage = Wrapper(island, name, c);
        const float size = .9f, height = 1f, pan = .06f;
        var collider = cage.gameObject.AddComponent<BoxCollider>();
        collider.center = new Vector3(0, height * .5f, 0);
        collider.size = new Vector3(size, height, size);
        Block(cage, "Pan", c + Vector3.up * (pan * .5f), new Vector3(size, pan, size), m.PaintedSteel, collider: false);
        float h = size * .5f - .02f, postHeight = height - pan;
        int n = 0;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Block(cage, $"Post_{++n}", c + new Vector3(sx * h, pan + postHeight * .5f, sz * h), new Vector3(.04f, postHeight, .04f), m.BareSteel, collider: false);
        foreach (float side in new[] { -1f, 1f })
        {
            Block(cage, side < 0 ? "Rim_South" : "Rim_North", c + new Vector3(0, height - .015f, side * h), new Vector3(size, .03f, .03f), m.BareSteel, collider: false);
            Block(cage, side < 0 ? "Rim_West" : "Rim_East", c + new Vector3(side * h, height - .015f, 0), new Vector3(.03f, .03f, size), m.BareSteel, collider: false);
        }
        const int barsPerSide = 7;
        float barY = pan + (postHeight - .03f) * .5f, barLength = postHeight - .03f;
        n = 0;
        for (int i = 1; i <= barsPerSide; i++)
        {
            float t = -h + 2 * h * i / (barsPerSide + 1);
            foreach (float side in new[] { -1f, 1f })
            {
                StartWorkshopWorkstation.Rod(cage, $"Bar_{++n}", c + new Vector3(t, barY, side * h), Vector3.up, .007f, barLength, m.BareSteel);
                StartWorkshopWorkstation.Rod(cage, $"Bar_{++n}", c + new Vector3(side * h, barY, t), Vector3.up, .007f, barLength, m.BareSteel);
            }
        }
        Block(cage, "Offcut_1", c + new Vector3(-.1f, pan + .05f, .05f), new Vector3(.55f, .08f, .3f), m.Red, collider: false, rotation: Quaternion.Euler(0, 20, 6));
        Block(cage, "Offcut_2", c + new Vector3(.12f, pan + .16f, -.08f), new Vector3(.4f, .12f, .25f), m.PaintedSteel, collider: false, rotation: Quaternion.Euler(8, -30, 0));
        Block(cage, "Offcut_3", c + new Vector3(-.05f, pan + .3f, -.12f), new Vector3(.3f, .05f, .5f), m.BareSteel, collider: false, rotation: Quaternion.Euler(-12, 45, 10));
        Block(cage, "Offcut_4", c + new Vector3(.15f, pan + .38f, .15f), new Vector3(.2f, .2f, .2f), m.Yellow, collider: false, rotation: Quaternion.Euler(15, 10, 20));
    }

    /// <summary>A rubber flight case with steel corner and rim strips, two latches on its west face and an end handle.</summary>
    static GameObject HardCase(Transform island, string name, Vector3 bottomCentre, Vector3 size, StartWorkshopWorkstation.Finishes m)
    {
        var box = Wrapper(island, name, bottomCentre);
        Vector3 c = bottomCentre;
        float hx = size.x * .5f, hz = size.z * .5f, strip = .03f;
        Block(box, "Shell", c + Vector3.up * (size.y * .5f), size, m.Rubber);
        int n = 0;
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sz in new[] { -1f, 1f })
            Block(box, $"Corner_{++n}", c + new Vector3(sx * (hx - .01f), size.y * .5f, sz * (hz - .01f)), new Vector3(strip, size.y, strip), m.BareSteel, collider: false);
        foreach (float y in new[] { strip * .5f, size.y - strip * .5f })
        {
            string level = y < size.y * .5f ? "Bottom" : "Top";
            foreach (float side in new[] { -1f, 1f })
            {
                Block(box, $"Rim_{level}_{(side < 0 ? "South" : "North")}", c + new Vector3(0, y, side * (hz - .01f)), new Vector3(size.x, strip, strip), m.BareSteel, collider: false);
                Block(box, $"Rim_{level}_{(side < 0 ? "West" : "East")}", c + new Vector3(side * (hx - .01f), y, 0), new Vector3(strip, strip, size.z), m.BareSteel, collider: false);
            }
        }
        foreach (float side in new[] { -1f, 1f })
            Block(box, side < 0 ? "Latch_South" : "Latch_North", c + new Vector3(-hx - .006f, size.y - .09f, side * size.z * .28f), new Vector3(.012f, .06f, .08f), m.Yellow, collider: false);
        Block(box, "End_Handle", c + new Vector3(0, size.y * .65f, hz + .02f), new Vector3(size.x * .4f, .025f, .03f), m.BareSteel, collider: false);
        return box.gameObject;
    }

    /// <summary>
    /// A low Modular Pack half-wall partition, its long side along Z, scaled to <see cref="PartitionHeight"/> (at most
    /// <see cref="PartitionLength"/> long). Falls back to a first-principles steel screen if the prefab is too deep for
    /// the island layout.
    /// </summary>
    static void Partition(Transform island, string name, StartWorkshopFrame f, Vector3 bottomCentre, StartWorkshopWorkstation.Finishes m)
    {
        var go = StartWorkshopDetailer.Pack(island, HalfPartition, name, f, Wall.West);
        Bounds b = B(go);
        if (b.size.x > b.size.z) go.transform.Rotate(0, 90, 0, Space.World);
        b = B(go);
        go.transform.localScale *= Mathf.Min(PartitionHeight / b.size.y, PartitionLength / b.size.z);
        Place(go, bottomCentre);
        if (B(go).size.x <= PartitionMaxThickness) return;
        Debug.LogWarning(FormattableString.Invariant(
            $"{HalfPartition} is {B(go).size.x:F2} m deep at partition size; {island.name}/{name} is a first-principles steel screen instead."));
        UnityEngine.Object.DestroyImmediate(go);
        var screen = Wrapper(island, name, bottomCentre);
        Vector3 c = bottomCentre;
        Block(screen, "Panel", c + Vector3.up * (PartitionHeight * .5f), new Vector3(.08f, PartitionHeight, PartitionLength - .1f), m.PaintedSteel);
        Block(screen, "Hazard_Band", c + new Vector3(0, PartitionHeight - .1f, 0), new Vector3(.09f, .08f, PartitionLength - .1f), m.Yellow, collider: false);
        foreach (float side in new[] { -1f, 1f })
        {
            Block(screen, side < 0 ? "Post_South" : "Post_North", c + new Vector3(0, PartitionHeight * .5f, side * (PartitionLength * .5f - .05f)),
                new Vector3(.1f, PartitionHeight, .1f), m.BareSteel, collider: false);
            Block(screen, side < 0 ? "Foot_South" : "Foot_North", c + new Vector3(0, .015f, side * (PartitionLength * .5f - .05f)),
                new Vector3(.4f, .03f, .12f), m.BareSteel, collider: false);
        }
    }

    /// <summary>A plain pallet stack with crates piled on it: the staging area's tall silhouette (about 2.5 m).</summary>
    static void TallStack(Transform island, string name, StartWorkshopFrame f, Vector3 bottomCentre)
    {
        var stack = Wrapper(island, name, bottomCentre);
        var pallets = KitProp(stack, PalletBase, "Pallet_Base", f, bottomCentre);
        var crateLong = KitProp(stack, CrateLong, "Crate_Long", f, new Vector3(bottomCentre.x, B(pallets).max.y, bottomCentre.z), longAlongZ: true);
        var mid = KitProp(stack, CrateShort, "Crate_Short_Mid", f, new Vector3(bottomCentre.x, B(crateLong).max.y, bottomCentre.z - .25f));
        KitProp(stack, CrateShort, "Crate_Short_Top", f, new Vector3(bottomCentre.x, B(mid).max.y, bottomCentre.z - .25f), scale: .85f);
    }

    static void Bin(Transform parent, string name, Vector3 bottomCentre, Vector3 size, Material colour, StartWorkshopWorkstation.Finishes m)
    {
        // A solid bin with a dark fill slab just above its top, inset so the coloured rim reads around it.
        Block(parent, name, bottomCentre + Vector3.up * (size.y * .5f), size, colour, collider: false);
        Block(parent, name + "_Fill", bottomCentre + Vector3.up * (size.y + .001f), new Vector3(size.x - .03f, .002f, size.z - .03f), m.Rubber, collider: false);
    }

    /// <summary>A kit prefab resting on <paramref name="bottomCentre"/>, optionally turned so its long side runs along X or Z.</summary>
    static GameObject KitProp(Transform parent, string path, string name, StartWorkshopFrame f, Vector3 bottomCentre,
        bool longAlongX = false, bool longAlongZ = false, float scale = 1f)
    {
        var go = Prop(parent, path, name, f, Wall.West, scale);
        Bounds b = B(go);
        if ((longAlongX && b.size.z > b.size.x) || (longAlongZ && b.size.x > b.size.z)) go.transform.Rotate(0, 90, 0, Space.World);
        Place(go, bottomCentre);
        return go;
    }

    static void Place(GameObject go, Vector3 bottomCentre)
    {
        Bottom(go, bottomCentre.y);
        Bounds b = B(go);
        Shift(go, bottomCentre.x - b.center.x, 0, bottomCentre.z - b.center.z);
    }

    static Transform Wrapper(Transform parent, string name, Vector3 position)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.position = position;
        return t;
    }

    /// <summary>An upright primitive cylinder standing on <paramref name="bottom"/>, keeping its collider.</summary>
    static void Post(Transform parent, string name, Vector3 bottom, float radius, float height, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(bottom + Vector3.up * (height * .5f), Quaternion.identity);
        go.transform.localScale = new Vector3(2 * radius, height * .5f, 2 * radius);
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Validation

    public static bool IsGroup(Transform group) => group != null && group.name.EndsWith(GroupSuffix, StringComparison.Ordinal);

    static List<Transform> Islands(Transform root) => root.Cast<Transform>().Where(IsGroup).ToList();

    /// <summary>
    /// Explicit corebuster_env_019 acceptance rule: 2-4 islands, each with a zone plate and a carving NavMesh obstacle
    /// over its footprint; every island prop keeps a <see cref="LaneWidth"/> lane to every floor prop outside its
    /// island; one or two centre props stand taller than <see cref="LowHeight"/>; no island blocks the spawn's view of
    /// the exit; and at least two islands are in clear view from spawn. The keep-outs themselves are checked for every
    /// prop by <see cref="StartWorkshopChecks"/>.
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var islands = Islands(root);
        if (islands.Count < MinIslands || islands.Count > MaxIslands)
        {
            errors.Add($"{islands.Count} centre island group(s) ending {GroupSuffix}; corebuster_env_019 needs {MinIslands}-{MaxIslands}. Run Tools/CoreBuster/Build Start Workshop Centre.");
            if (islands.Count == 0) return;
        }
        var tall = new List<string>();
        var lanes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var island in islands)
        {
            if (island.childCount < MinIslandProps) errors.Add($"{island.name} has {island.childCount} props; an island needs at least {MinIslandProps}.");
            if (island.Find(PlateName) == null) errors.Add($"{island.name} has no {PlateName} framing its zone.");
            CheckCarve(island, f, errors);
            var others = FloorProps(root, f, island);
            foreach (Transform prop in island)
            {
                if (!TryBounds(prop, out Bounds b)) continue;
                if (b.max.y - f.Floor > LowHeight) tall.Add(FormattableString.Invariant($"{island.name}/{prop.name} ({b.max.y - f.Floor:F2} m)"));
                if (!f.IsReachable(b)) continue;
                foreach (var (name, other) in others)
                {
                    float gap = Gap(b, other);
                    if (gap < LaneWidth - .005f)
                        lanes.Add(FormattableString.Invariant($"{island.name}/{prop.name} leaves only a {gap:F2} m lane to {name}; centre islands keep {LaneWidth} m."));
                }
            }
        }
        errors.AddRange(lanes);
        if (tall.Count < MinTall || tall.Count > MaxTall)
            errors.Add($"{tall.Count} centre prop(s) stand over {LowHeight} m; islands keep {MinTall}-{MaxTall} taller silhouettes" +
                       (tall.Count > 0 ? ": " + string.Join(", ", tall) : "") + ".");
        CheckSightlines(root, f, islands, errors);
    }

    static void CheckCarve(Transform island, StartWorkshopFrame f, List<string> errors)
    {
        var obstacle = island.GetComponent<NavMeshObstacle>();
        if (obstacle == null || !obstacle.enabled || !obstacle.carving || obstacle.shape != NavMeshObstacleShape.Box)
        {
            errors.Add($"{island.name} needs an enabled, carving box NavMeshObstacle so enemies path around it.");
            return;
        }
        if (island.rotation != Quaternion.identity || island.lossyScale != Vector3.one)
        {
            errors.Add($"{island.name} must stay unrotated and unscaled; its NavMeshObstacle box is in world axes.");
            return;
        }
        var box = new Bounds(island.TransformPoint(obstacle.center), obstacle.size);
        Bounds fp = Footprint(island, f);
        const float tolerance = .05f;
        if (box.min.x > fp.min.x + tolerance || box.max.x < fp.max.x - tolerance || box.min.z > fp.min.z + tolerance ||
            box.max.z < fp.max.z - tolerance || box.min.y > f.Floor + tolerance || box.max.y < f.Floor + CarveHeight - tolerance)
            errors.Add($"{island.name}'s NavMeshObstacle no longer covers its footprint; rebuild the centre islands.");
    }

    static void CheckSightlines(Transform root, StartWorkshopFrame f, List<Transform> islands, List<string> errors)
    {
        Physics.SyncTransforms();
        Vector3 eye = new Vector3(f.Spawn.x, f.Floor + EyeHeight, f.Spawn.z);
        Vector3 exit = f.RouteEnd + Vector3.up * 1.2f;
        foreach (var hit in Physics.RaycastAll(eye, exit - eye, Vector3.Distance(eye, exit), ~0, QueryTriggerInteraction.Ignore))
        {
            var island = islands.FirstOrDefault(i => hit.collider.transform.IsChildOf(i));
            if (island != null) errors.Add($"{island.name}/{hit.collider.name} blocks the view of the exit from spawn.");
        }
        var visible = islands.Where(i => InView(root, i, eye)).Select(i => i.name).ToList();
        if (visible.Count < MinIslands)
            errors.Add($"Only {visible.Count} centre island(s) in clear view from spawn ({string.Join(", ", visible)}); corebuster_env_019 needs at least {MinIslands}.");
    }

    /// <summary>True if the line from <paramref name="eye"/> to the top of any island prop crosses no other workshop collider.</summary>
    static bool InView(Transform root, Transform island, Vector3 eye)
    {
        foreach (Transform prop in island)
        {
            if (prop.name == PlateName || !TryBounds(prop, out Bounds b)) continue;
            Vector3 target = new Vector3(b.center.x, b.max.y - .05f, b.center.z), ray = target - eye;
            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(eye, ray, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                var t = hit.collider.transform;
                if (t.IsChildOf(root) && !t.IsChildOf(island)) { blocked = true; break; }
            }
            if (!blocked) return true;
        }
        return false;
    }
}
