using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>
/// corebuster_render_006 (absorbs corebuster_env_015). Lights the Start Workshop with realtime lights on first-principles
/// fixtures, in the rebuildable <see cref="GroupName"/> group:
/// <list type="bullet">
/// <item>caged amber wall lamps at floor-tile quarter points along every wall, each with a tilted spot for its floor
/// pool (<see cref="PoolName"/>) and a short spot back onto its own wall (<see cref="WashName"/>);</item>
/// <item>a lamp either side of the exit, between the doorway and the P-3 tanks, and a warm strip over the exit sign;</item>
/// <item>a cool spot on the corebuster_env_010 task lamp over the workbench, and one warm downlight filling the centre;</item>
/// <item>a box-projected reflection probe bounded to the room, baked on its own to a project-owned cubemap.</item>
/// </list>
/// Nothing lit by these lights can reach another space: every light's lit volume (its range sphere, or for a spot the
/// cone within its range) stays inside the outer faces of the room's walls. Every light also respects URP's forward
/// per-object limit (<see cref="MaxLightsPerObject"/> additional lights): no floor tile, ceiling tile, wall panel or
/// boundary liner is touched by more room lights than that, counted with the test Unity itself uses (<see cref="Touches"/>).
/// Shadows go to the exit pair (beside the P-3 tanks) and the lights nearest the pipe drops and the robot arm, two to
/// four in all (<see cref="CastOccluderShadows"/>). Lamps anchor to the walls and slide clear of
/// every other pass's props; required lights fail with a message when their anchor or budget is missing. The lighting
/// pass is the room's last layer: rebuild it after any pass that moves wall props. No name contains "Wall" (see
/// <see cref="StartWorkshopFrame.Measure"/>).
/// </summary>
public static class StartWorkshopLighting
{
    public const string GroupName = "Lighting_006";
    const string UndoName = "Build Start Workshop Lighting";
    const string ProbeParent = "Assets/FPS/Art/StartWorkshop", ProbeFolder = ProbeParent + "/Lighting";
    public const string ProbeTexturePath = ProbeFolder + "/StartWorkshop_Reflection.exr";
    const string Props = "Assets/FPS/Art/Materials/Props/";

    const string WestTankPath = "Pipework_009/Tank_P3_West", EastTankPath = "Pipework_009/Tank_P3_East";
    const string ExitSignPath = "Exit_Signage/Exit_Sign";
    const string TubePath = "Workstation_010/Task_Lamp/Tube", PegboardPath = "Workstation_010/Pegboard";
    const string ArmCellPath = "Workstation_010/Robot_Arm_Cell";
    // Solid shapes a lamp's light would otherwise pass straight through: the arm, the P-3 tanks and the pipe drops.
    static readonly string[] Occluders =
        { ArmCellPath, WestTankPath, EastTankPath, "Pipework_009/Drop_West", "Pipework_009/Drop_East", "Pipework_009/Drop_South", "Pipework_009/Drop_SouthWest" };

    public const string ExitWest = "Lamp_Exit_West", ExitEast = "Lamp_Exit_East", StripName = "Exit_Strip";
    public const string TaskName = "Task_Light", FillName = "Fill_Center", ProbeName = "Reflection_Probe";
    public const string PoolName = "Pool_Light", WashName = "Wash_Light", StripLightName = "Strip_Light";
    const string LampPrefix = "Lamp_";
    static readonly string[] Required = { ExitWest, ExitEast, StripName, TaskName, FillName, ProbeName };

    // URP's forward renderer lights each object with the main light plus at most this many additional lights
    // (Default_PipelineAsset, Additional Lights > Per Object Limit).
    public const int MaxLightsPerObject = 4;
    // Shadow-casting lights: the exit pair (beside the P-3 tanks), then the lamps nearest the other occluders.
    const int MinShadowed = 2, MaxShadowed = 4;
    const int MinLampsPerWall = 3;
    const float MaxRange = 20f;
    // Light volumes stop this far short of the walls' measured outer faces.
    const float OuterMargin = .02f;
    // Fallback wall thickness when a wall's outer face cannot be measured (the walls are 0.33-0.38 m thick).
    const float ShellAllowance = .3f;

    // Caged wall lamp: globe centre above the floor and out from the wall surface it is mounted on.
    const float LampHeight = 2.85f, GlobeOut = .125f, GlobeRadius = .045f, GlobeLength = .2f;
    const float PlateWidth = .15f, PlateHeight = .34f, PlateDepth = .025f;
    // The fixture is modelled at a bulkhead lamp's size and scaled up so it still reads on the room's 11 m walls from
    // across a 32 m floor. The dimensions above are before this scale.
    const float FixtureScale = 1.7f;
    // Slots sit at the quarter points of each floor tile's span along the wall: two lamps per 8 m tile, about 4 m
    // apart, each pool mostly on its own tile. A blocked slot slides this far either way before it is skipped.
    const float SlideStep = .1f, MaxSlide = 1f, CornerClear = 1f;
    // Kept clear of the regular rhythm: the exit pair and strip on the north wall, the task light on the east wall.
    const float ReservedMargin = .8f;
    // A lamp keeps this much open space in front of it, so it is not hidden behind a pipe drop or a tank.
    const float FrontClear = .5f;
    const float Clearance = .005f;
    // How far in front of the wall the lamps' surfaces are searched for.
    const float MountReach = .6f;

    // Warm amber practicals, the warm exit strip, the cool fluorescent task lamp and a warm fill.
    static readonly Color Amber = new Color(1f, .62f, .3f), StripColor = new Color(1f, .74f, .45f);
    static readonly Color Cool = new Color(.8f, .9f, 1f), FillColor = new Color(1f, .92f, .7f);

    // Spots: tilt below the horizontal away from the wall, full cone angle, inner angle, range and intensity. A tilt
    // plus half-angle of 90 degrees or less keeps the cone entirely in front of the wall the light is mounted on.
    struct Spot
    {
        public float Tilt, Angle, Inner, Range, Intensity;
        public Spot(float tilt, float angle, float inner, float range, float intensity)
        {
            Tilt = tilt; Angle = angle; Inner = inner; Range = range; Intensity = intensity;
        }
    }

    static readonly Spot PoolSpot = new Spot(58, 50, 20, 5f, 60);
    // Near a corner the pool's range is cut to what the perpendicular wall allows, down to this.
    const float PoolMinRange = 4f;
    static readonly Spot StripSpot = new Spot(55, 60, 25, 4.5f, 30);
    // The task lamp's tube hangs 0.3 m out from the pegboard: its cone may reach just behind it, onto the pegboard,
    // within what the east wall's thickness allows.
    static readonly Spot TaskSpot = new Spot(70, 62, 30, 3f, 14);
    // The wash stands this far in front of its lamp and looks back at the wall; its range is the most the wall's
    // thickness allows, and a wash that cannot reach this far past the wall surface is not placed.
    const float WashStandoff = 1.1f, WashAngle = 80, WashInner = 30, WashIntensity = 10, WashMaxRange = 1.8f, WashMinPast = .15f;
    // The centre fill hangs this high over the corner shared by the four central floor tiles and covers exactly those.
    // Its range is well past the floor so the pool stays even to its edge instead of peaking under the light.
    // It is yellower than the lamps: an orange fill over the scene's cyan ambient turned the floor pink.
    const float FillHeight = 10f, FillRange = 20f, FillIntensity = 80, FillInner = .6f;
    // Reflection probe: captured at eye height, its box the room interior from just above the map's ground plane
    // (y = 0, under the floor tiles) to the ceiling.
    const float ProbeEye = 1.7f, ProbeFloorDepth = .1f;
    const int ProbeResolution = 256;

    [MenuItem("Tools/CoreBuster/Build Start Workshop Lighting")]
    public static void Rebuild()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var root = FindRoom(scene);
        if (root == null) return;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        RebuildIn(scene, root);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        StartWorkshopChecks.Run();
    }

    /// <summary>
    /// Batch entry point, run without -nographics (the probe bake renders):
    /// Unity.exe -batchmode -projectPath &lt;path&gt; -executeMethod StartWorkshopLighting.BuildBatch -quit.
    /// Opens MainScene, rebuilds the lighting, applies the lit materials, bakes the probe, saves the scene and validates.
    /// </summary>
    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = FindRoom(scene);
        if (root == null) { EditorApplication.Exit(1); return; }
        RebuildIn(scene, root);
        EditorSceneManager.SaveScene(scene);
        StartWorkshopChecks.Run();
    }

    static Transform FindRoom(Scene scene)
    {
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; the lighting belongs under {LevelName}/{ContainerName}.");
        return root;
    }

    static void RebuildIn(Scene scene, Transform root)
    {
        var old = root.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var f = StartWorkshopFrame.Measure(scene, root);
        int firstChild = root.childCount;
        try
        {
            Build(root, f);
            ApplyStaticConvention(root.GetChild(firstChild), GameObjectUtility.GetStaticEditorFlags(root.parent.gameObject));
        }
        catch
        {
            if (root.childCount > firstChild) UnityEngine.Object.DestroyImmediate(root.GetChild(firstChild).gameObject);
            throw;
        }
        Undo.RegisterCreatedObjectUndo(root.GetChild(firstChild).gameObject, UndoName);
        // The probe captures the room as it renders, on its lit copies.
        StartWorkshopLitMaterials.ApplyTo(scene, root);
        BakeProbe(scene, root);
        if (root.Find(StartWorkshopAmbient.GroupName) != null) StartWorkshopAmbient.BindLamp(root);
    }

    /// <summary>Called by the full workshop rebuild after the boundary liners, and by the isolated menu action.</summary>
    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null)
            throw new InvalidOperationException($"{GroupName} already exists. Remove it before rebuilding the lighting.");
        var c = new Context(root, f);
        var m = new Materials();
        var group = Group(root, GroupName);
        c.Group = group;

        var westTank = B(Anchor(root, WestTankPath));
        var eastTank = B(Anchor(root, EastTankPath));
        var sign = B(Anchor(root, ExitSignPath));
        var tube = B(Anchor(root, TubePath));
        var pegboard = B(Anchor(root, PegboardPath));
        // The regular rhythm stays clear of the exit (lamps, strip and tanks) and of the workstation's task lamp.
        c.Reserved[Wall.North] = (westTank.min.x - ReservedMargin, eastTank.max.x + ReservedMargin);
        c.Reserved[Wall.East] = (pegboard.min.z - ReservedMargin, pegboard.max.z + ReservedMargin);

        // Required lights first, so the regular rhythm fits around their budget.
        RequireExitLamp(c, m, ExitWest, (westTank.max.x + f.OpeningMinX) * .5f);
        RequireExitLamp(c, m, ExitEast, (f.OpeningMaxX + eastTank.min.x) * .5f);
        BuildStrip(c, m, sign, f);
        BuildTaskLight(c, tube);
        BuildFill(c, f);

        var slots = new List<(Wall wall, float at, float fromCorner)>();
        foreach (Wall wall in new[] { Wall.West, Wall.North, Wall.East, Wall.South })
        {
            var (lo, hi) = Span(f, wall);
            foreach (float at in c.Slots(wall))
                if (at >= lo + CornerClear && at <= hi - CornerClear) slots.Add((wall, at, Mathf.Min(at - lo, hi - at)));
        }
        // Mid-wall slots first: where two walls' rhythms meet in a corner tile, the per-object budget decides.
        foreach (var (wall, at, _) in slots.OrderByDescending(s => s.fromCorner).ThenBy(s => s.wall).ThenBy(s => s.at))
            TryLamp(c, m, wall, at);

        NameLamps(group);
        CastOccluderShadows(c);
        BuildProbe(c, f);
        AssetDatabase.SaveAssets();
        Debug.Log(Report(c));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Fixtures and lights

    /// <summary>A lamp between the doorway and a P-3 tank; it casts shadows, since the tank stands beside it.</summary>
    static void RequireExitLamp(Context c, Materials m, string name, float at)
    {
        string why = PlaceLamp(c, m, name, Wall.North, at, required: true, out var lamp);
        if (lamp == null)
            throw new InvalidOperationException(FormattableString.Invariant($"{GroupName}/{name} cannot stand at x={at:F2} beside the exit: {why}."));
        CastShadows(lamp.Find(PoolName).GetComponent<Light>());
    }

    static void TryLamp(Context c, Materials m, Wall wall, float slot)
    {
        string first = null;
        for (float offset = 0; offset <= MaxSlide + .001f; offset += SlideStep)
            foreach (float sign in offset == 0 ? new[] { 1f } : new[] { 1f, -1f })
            {
                float at = slot + sign * offset;
                string why = PlaceLamp(c, m, $"{LampPrefix}{wall}_slot", wall, at, required: false, out var lamp);
                if (lamp != null)
                {
                    if (offset > 0) c.Log.AppendLine(FormattableString.Invariant($"  {wall} slot {slot:F2} slid to {at:F2}: {first}"));
                    return;
                }
                first ??= why;
            }
        c.Log.AppendLine(FormattableString.Invariant($"  skipped {wall} slot {slot:F2}: {first}; nothing clear within {MaxSlide} m"));
    }

    /// <summary>
    /// Builds a caged lamp on <paramref name="wall"/> at <paramref name="at"/> along it, with its pool and wash lights.
    /// Returns why it cannot stand there (and destroys it), or null with the lamp in <paramref name="lamp"/>. Only a
    /// <paramref name="required"/> lamp may stand in a reserved stretch.
    /// </summary>
    static string PlaceLamp(Context c, Materials m, string name, Wall wall, float at, bool required, out Transform lamp)
    {
        lamp = null;
        var f = c.F;
        Vector3 n = Inward(wall);
        if (!required && c.Reserved.TryGetValue(wall, out var zone) && at + PlateWidth * FixtureScale > zone.lo && at - PlateWidth * FixtureScale < zone.hi)
            return "reserved for the exit or the workstation";
        float y = f.Floor + LampHeight;
        Vector3 face = FacePoint(f, wall, at, y);
        if (!Surface(c, face, n, out float surface)) return "no wall surface behind it";

        var t = Fixture(c.Group, name, m);
        t.SetPositionAndRotation(face - n * surface, Quaternion.LookRotation(n, Vector3.up));
        string blocked = Blocking(c, t, n);
        if (blocked != null) return Discard(t, "is blocked by " + blocked);

        Vector3 globe = t.TransformPoint(0, 0, GlobeOut);
        var pool = AddSpot(t, PoolName, globe, Aim(n, PoolSpot.Tilt), PoolSpot, Amber);
        pool.range = Mathf.Min(PoolSpot.Range, ContainedRange(pool, c.Faces) - OuterMargin);
        if (pool.range < PoolMinRange) return Discard(t, "too close to a corner for its pool");
        var wash = AddSpot(t, WashName, globe + n * WashStandoff, Quaternion.LookRotation(-n, Vector3.up),
            new Spot(0, WashAngle, WashInner, WashMaxRange, WashIntensity), Amber);
        wash.range = Mathf.Min(WashMaxRange, ContainedRange(wash, c.Faces) - OuterMargin);
        if (wash.range < WashStandoff + GlobeOut * FixtureScale + WashMinPast) return Discard(t, "too little wall thickness behind it for its wash");
        string refused = c.Commit(pool, wash);
        if (refused != null) return Discard(t, refused);
        c.Occupy(t);
        lamp = t;
        return null;
    }

    static string Discard(Transform t, string why)
    {
        UnityEngine.Object.DestroyImmediate(t.gameObject);
        return why;
    }

    /// <summary>
    /// The first-principles caged lamp, facing +Z with its back plate on the wall plane z = 0: a painted plate and arm,
    /// an emissive amber globe between steel caps, four cage bars and a cage band, scaled by <see cref="FixtureScale"/>.
    /// </summary>
    static Transform Fixture(Transform group, string name, Materials m)
    {
        var lamp = new GameObject(name).transform;
        lamp.SetParent(group, false);
        lamp.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Block(lamp, "Back_Plate", new Vector3(0, 0, PlateDepth * .5f), new Vector3(PlateWidth, PlateHeight, PlateDepth), m.Painted, collider: false);
        Block(lamp, "Arm", new Vector3(0, 0, PlateDepth + .03f), new Vector3(.05f, .07f, .06f), m.Steel, collider: false);
        StartWorkshopWorkstation.Rod(lamp, "Globe", new Vector3(0, 0, GlobeOut), Vector3.up, GlobeRadius, GlobeLength, m.Amber);
        StartWorkshopWorkstation.Rod(lamp, "Cap_Top", new Vector3(0, GlobeLength * .5f + .015f, GlobeOut), Vector3.up, .062f, .03f, m.Steel);
        StartWorkshopWorkstation.Rod(lamp, "Cap_Bottom", new Vector3(0, -GlobeLength * .5f - .015f, GlobeOut), Vector3.up, .062f, .03f, m.Steel);
        StartWorkshopWorkstation.Rod(lamp, "Cage_Band", new Vector3(0, 0, GlobeOut), Vector3.up, .064f, .01f, m.Steel);
        for (int i = 0; i < 4; i++)
        {
            float a = (45 + 90 * i) * Mathf.Deg2Rad;
            Block(lamp, $"Cage_{i + 1}", new Vector3(.062f * Mathf.Cos(a), 0, GlobeOut + .062f * Mathf.Sin(a)),
                new Vector3(.012f, GlobeLength, .012f), m.Steel, collider: false);
        }
        lamp.localScale = Vector3.one * FixtureScale;
        return lamp;
    }

    /// <summary>
    /// Saves the caged lamp with its amber pool and wall-wash spots as a prefab, for walls outside the Workshop (the
    /// corridors' lamps, Tools/CoreBuster/Build Corridor Lockdown). With no room to measure, the pool keeps its full
    /// range and the wash reaches <paramref name="washRange"/> from its standoff; placers recolour the lights.
    /// </summary>
    internal static GameObject SaveFixturePrefab(string path, string name, float washRange)
    {
        string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
        var t = Fixture(null, name, new Materials());
        MergeFixture(t, folder + "/SM_" + name + "_Fixture.asset");
        Vector3 n = Vector3.forward;
        Vector3 globe = t.TransformPoint(0, 0, GlobeOut);
        AddSpot(t, PoolName, globe, Aim(n, PoolSpot.Tilt), PoolSpot, Amber);
        AddSpot(t, WashName, globe + n * WashStandoff, Quaternion.LookRotation(-n, Vector3.up),
            new Spot(0, WashAngle, WashInner, washRange, WashIntensity), Amber);
        var prefab = PrefabUtility.SaveAsPrefabAsset(t.gameObject, path);
        UnityEngine.Object.DestroyImmediate(t.gameObject);
        return prefab;
    }

    // The fixture's plate, arm, caps and cage as one renderer (a submesh per finish), the same geometry in two draws
    // rather than ten, for walls that carry hundreds of lamps. The globe stays its own renderer: the corridors'
    // beacons colour it through a property block.
    static void MergeFixture(Transform lamp, string meshPath)
    {
        var parts = lamp.GetComponentsInChildren<MeshRenderer>().Where(r => r.name != "Globe").ToList();
        var finishes = parts.Select(r => r.sharedMaterial).Distinct().ToList();
        var toLamp = lamp.worldToLocalMatrix;
        var submeshes = finishes.Select(finish =>
        {
            var part = new Mesh();
            part.CombineMeshes(parts.Where(r => r.sharedMaterial == finish).Select(r => new CombineInstance
            {
                mesh = r.GetComponent<MeshFilter>().sharedMesh,
                transform = toLamp * r.transform.localToWorldMatrix,
            }).ToArray(), true, true);
            return part;
        }).ToList();
        var merged = new Mesh();
        merged.CombineMeshes(submeshes.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(), false, false);
        merged.RecalculateBounds();
        foreach (var m in submeshes) UnityEngine.Object.DestroyImmediate(m);

        // Rewritten in place on a rebuild, so the prefab keeps its reference.
        var asset = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (asset)
        {
            EditorUtility.CopySerialized(merged, asset);
            UnityEngine.Object.DestroyImmediate(merged);
        }
        else
        {
            asset = merged;
            AssetDatabase.CreateAsset(asset, meshPath);
        }
        asset.name = System.IO.Path.GetFileNameWithoutExtension(meshPath);

        foreach (var r in parts) UnityEngine.Object.DestroyImmediate(r.gameObject);
        var body = new GameObject("Fixture");
        body.transform.SetParent(lamp, false);
        body.AddComponent<MeshFilter>().sharedMesh = asset;
        body.AddComponent<MeshRenderer>().sharedMaterials = finishes.ToArray();
    }

    /// <summary>A warm strip light over the exit sign: a steel housing with an emissive bar along its front edge.</summary>
    static void BuildStrip(Context c, Materials m, Bounds sign, StartWorkshopFrame f)
    {
        const float length = 1.6f, height = .09f, depth = .08f;
        var strip = new GameObject(StripName).transform;
        strip.SetParent(c.Group, false);
        strip.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Block(strip, "Housing", new Vector3(0, 0, depth * .5f), new Vector3(length, height, depth), m.Painted, collider: false);
        Block(strip, "Diffuser", new Vector3(0, -height * .5f + .012f, depth + .006f), new Vector3(length - .1f, .024f, .012f), m.Strip, collider: false);
        foreach (float side in new[] { -1f, 1f })
            Block(strip, side < 0 ? "End_West" : "End_East", new Vector3(side * (length * .5f + .01f), 0, depth * .5f),
                new Vector3(.02f, height + .01f, depth + .01f), m.Steel, collider: false);

        Vector3 n = Inward(Wall.North);
        float y = sign.max.y + .12f + height * .5f;
        Vector3 face = FacePoint(f, Wall.North, f.OpeningCenter, y);
        if (!Surface(c, face, n, out float surface))
            throw new InvalidOperationException($"No doorway lintel surface found above '{ExitSignPath}' for {GroupName}/{StripName}.");
        strip.SetPositionAndRotation(face - n * surface, Quaternion.LookRotation(n, Vector3.up));
        string blocked = Blocking(c, strip, n);
        if (blocked != null) throw new InvalidOperationException($"{GroupName}/{StripName} intersects {blocked} above the exit sign.");
        var light = AddSpot(strip, StripLightName, strip.TransformPoint(0, -height * .5f, depth + .02f), Aim(n, StripSpot.Tilt), StripSpot, StripColor);
        string refused = c.Commit(light);
        if (refused != null) throw new InvalidOperationException($"{GroupName}/{StripName}: {refused}.");
        c.Occupy(strip);
    }

    /// <summary>A cool spot just under the corebuster_env_010 task lamp's tube, over the bench and pegboard.</summary>
    static void BuildTaskLight(Context c, Bounds tube)
    {
        var go = new GameObject(TaskName).transform;
        go.SetParent(c.Group, false);
        var light = AddSpot(go, null, new Vector3(tube.center.x, tube.min.y - .01f, tube.center.z), Aim(Inward(Wall.East), TaskSpot.Tilt), TaskSpot, Cool);
        light.range = Mathf.Min(TaskSpot.Range, ContainedRange(light, c.Faces) - OuterMargin);
        string refused = c.Commit(light);
        if (refused != null) throw new InvalidOperationException($"{GroupName}/{TaskName}: {refused}.");
    }

    /// <summary>
    /// One warm downlight over the corner shared by the four central floor tiles, its cone and Unity's per-object
    /// frustum both inside those tiles, so it adds one light to each of them and none to the tiles along the walls.
    /// </summary>
    static void BuildFill(Context c, StartWorkshopFrame f)
    {
        var (corner, half, bottom) = c.CentralTiles();
        var go = new GameObject(FillName).transform;
        go.SetParent(c.Group, false);
        float apex = f.Floor + FillHeight;
        float angle = 2 * Mathf.Atan((half - .05f) / (apex - bottom)) * Mathf.Rad2Deg;
        var light = AddSpot(go, null, new Vector3(corner.x, apex, corner.z), Quaternion.LookRotation(Vector3.down, Vector3.forward),
            new Spot(90, angle, angle * FillInner, FillRange, FillIntensity), FillColor);
        string refused = c.Commit(light);
        if (refused != null) throw new InvalidOperationException($"{GroupName}/{FillName}: {refused}.");
    }

    static void BuildProbe(Context c, StartWorkshopFrame f)
    {
        var go = new GameObject(ProbeName).transform;
        go.SetParent(c.Group, false);
        var box = ProbeBox(f);
        go.position = new Vector3(box.center.x, f.Floor + ProbeEye, box.center.z);
        var probe = go.gameObject.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.renderDynamicObjects = true;
        probe.boxProjection = true;
        probe.center = box.center - go.position;
        probe.size = box.size;
        probe.blendDistance = 0;
        probe.importance = 1;
        probe.intensity = 1;
        probe.resolution = ProbeResolution;
        probe.hdr = true;
        // The room is enclosed; clearing to the scene's animated nebula skybox made every bake differ.
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = Color.black;
        probe.cullingMask = ~0;
        probe.nearClipPlane = .3f;
        probe.farClipPlane = 60;
        probe.shadowDistance = 60;
        probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(ProbeTexturePath);
    }

    static Bounds ProbeBox(StartWorkshopFrame f)
    {
        var box = new Bounds();
        box.SetMinMax(new Vector3(f.West, f.Floor - ProbeFloorDepth, f.South), new Vector3(f.East, f.Ceiling, f.North));
        return box;
    }

    /// <summary>
    /// Bakes the reflection probe to <see cref="ProbeTexturePath"/> and assigns it. Actors (enemies, the player, the
    /// decorative characters) are hidden during the capture so they are not left behind in the room's reflections.
    /// Needs a graphics device. The shell pieces are not static, so the probe captures dynamic objects.
    /// </summary>
    public static void BakeProbe(Scene scene, Transform root)
    {
        var probe = root.Find(GroupName + "/" + ProbeName)?.GetComponent<ReflectionProbe>();
        if (probe == null) throw new InvalidOperationException($"No {GroupName}/{ProbeName}; run Tools/CoreBuster/Build Start Workshop Lighting.");
        if (!AssetDatabase.IsValidFolder(ProbeFolder)) AssetDatabase.CreateFolder(ProbeParent, ProbeFolder.Substring(ProbeParent.Length + 1));
        var hidden = new List<Renderer>();
        foreach (var go in scene.GetRootGameObjects())
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                if (!r.forceRenderingOff && IsActor(r.transform))
                {
                    r.forceRenderingOff = true;
                    hidden.Add(r);
                }
        try
        {
            if (!Lightmapping.BakeReflectionProbe(probe, ProbeTexturePath))
                throw new InvalidOperationException($"Baking {GroupName}/{ProbeName} to {ProbeTexturePath} failed; it needs a graphics device.");
        }
        finally
        {
            foreach (var r in hidden) r.forceRenderingOff = false;
        }
        AssetDatabase.ImportAsset(ProbeTexturePath, ImportAssetOptions.ForceUpdate);
        Undo.RecordObject(probe, UndoName);
        probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(ProbeTexturePath);
        Debug.Log($"Start Workshop reflection probe baked to {ProbeTexturePath} ({hidden.Count} actor renderer(s) hidden from the capture).");
    }

    static Light AddSpot(Transform parent, string name, Vector3 position, Quaternion rotation, Spot s, Color color)
    {
        var go = name == null ? parent.gameObject : new GameObject(name);
        if (name != null) go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        var light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        light.spotAngle = s.Angle;
        light.innerSpotAngle = s.Inner;
        light.range = s.Range;
        light.intensity = s.Intensity;
        light.color = color;
        light.shadows = LightShadows.None;
        light.bounceIntensity = 0;
        go.AddComponent<UniversalAdditionalLightData>();
        return light;
    }

    /// <summary>Soft shadows at the pipeline's medium additional-light tier (512 px), so three fit the 1024 px atlas.</summary>
    static void CastShadows(Light light)
    {
        light.shadows = LightShadows.Soft;
        var data = new SerializedObject(light.GetComponent<UniversalAdditionalLightData>());
        data.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Unshadowed, a lamp lights the floor and wall behind a tank, a pipe drop or the robot arm as if it were not there.
    /// The exit pair already cast shadows (the tanks stand beside them); the remaining budget goes to the lights nearest
    /// the occluders their light reaches, and any left over are logged.
    /// </summary>
    static void CastOccluderShadows(Context c)
    {
        var occluders = Occluders.Select(path => (path, b: B(Anchor(c.Root, path)))).ToList();
        var leaks = c.Group.GetComponentsInChildren<Light>()
            .Where(l => l.name == PoolName || l.name == TaskName || l.name == StripLightName)
            .SelectMany(l => occluders.Where(o => Touches(l, o.b)).Select(o => (l, o.path, d: Vector3.Distance(l.transform.position, o.b.center))))
            .OrderBy(x => x.d).ToList();
        foreach (var (l, path, d) in leaks)
        {
            if (l.shadows != LightShadows.None) continue;
            if (c.Group.GetComponentsInChildren<Light>().Count(x => x.shadows != LightShadows.None) >= MaxShadowed)
            {
                c.Log.AppendLine(FormattableString.Invariant($"  no shadow budget left: {PathOf(l.transform, c.Group)} passes {path} unshadowed ({d:F2} m)"));
                continue;
            }
            CastShadows(l);
            c.Log.AppendLine(FormattableString.Invariant($"  {PathOf(l.transform, c.Group)} casts shadows for {path} ({d:F2} m)"));
        }
    }

    /// <summary>Numbers the regular lamps along each wall (south to north, west to east) and orders the group.</summary>
    static void NameLamps(Transform group)
    {
        var lamps = group.Cast<Transform>().Where(t => t.name.EndsWith("_slot")).ToList();
        int index = group.childCount - lamps.Count;
        foreach (Wall wall in new[] { Wall.West, Wall.North, Wall.East, Wall.South })
        {
            int k = 0;
            foreach (var t in lamps.Where(t => t.name == $"{LampPrefix}{wall}_slot")
                         .OrderBy(t => wall == Wall.West || wall == Wall.East ? t.position.z : t.position.x))
            {
                t.name = $"{LampPrefix}{wall}_{++k}";
                t.SetSiblingIndex(index++);
            }
        }
    }

    static Quaternion Aim(Vector3 inward, float tilt) =>
        Quaternion.LookRotation(inward * Mathf.Cos(tilt * Mathf.Deg2Rad) - Vector3.up * Mathf.Sin(tilt * Mathf.Deg2Rad), Vector3.up);

    static Vector3 Inward(Wall wall) => wall switch
    {
        Wall.West => Vector3.right, Wall.East => Vector3.left, Wall.South => Vector3.forward, _ => Vector3.back,
    };

    static (float lo, float hi) Span(StartWorkshopFrame f, Wall wall) =>
        wall == Wall.West || wall == Wall.East ? (f.South, f.North) : (f.West, f.East);

    static Vector3 FacePoint(StartWorkshopFrame f, Wall wall, float at, float y) => wall switch
    {
        Wall.West => new Vector3(f.West, y, at), Wall.East => new Vector3(f.East, y, at),
        Wall.South => new Vector3(at, y, f.South), _ => new Vector3(at, y, f.North),
    };

    /// <summary>
    /// How far behind the measured wall face (negative: in front of it) the nearest wall collider is, over the lamp's
    /// back plate. The room container is ignored, so a lamp that would sit inside another pass's prop is caught by
    /// <see cref="Blocking"/>, not mounted on it.
    /// </summary>
    static bool Surface(Context c, Vector3 face, Vector3 n, out float surface)
    {
        surface = float.MaxValue;
        Vector3 across = Vector3.Cross(Vector3.up, n);
        foreach (float u in new[] { -.4f, 0, .4f })
        foreach (float v in new[] { -.4f, 0, .4f })
        {
            Vector3 p = face + across * (u * PlateWidth * FixtureScale) + Vector3.up * (v * PlateHeight * FixtureScale) + n * MountReach;
            if (StartWorkshopFrame.Blocked(p, -n, MountReach * 2, c.Root, out RaycastHit hit))
                surface = Mathf.Min(surface, hit.distance - MountReach);
        }
        return surface < MountReach;
    }

    /// <summary>The first prop the fixture intersects, or that stands in the <see cref="FrontClear"/> space in front of it.</summary>
    static string Blocking(Context c, Transform t, Vector3 n)
    {
        Physics.SyncTransforms();
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            Bounds front = r.bounds;
            front.Encapsulate(r.bounds.center + n * (Vector3.Dot(r.bounds.extents, new Vector3(Mathf.Abs(n.x), 0, Mathf.Abs(n.z))) + FrontClear));
            foreach (var (other, b) in c.Obstacles)
                if (Mathf.Min(front.max.x, b.max.x) - Mathf.Max(front.min.x, b.min.x) > Clearance &&
                    Mathf.Min(front.max.y, b.max.y) - Mathf.Max(front.min.y, b.min.y) > Clearance &&
                    Mathf.Min(front.max.z, b.max.z) - Mathf.Max(front.min.z, b.min.z) > Clearance)
                    return PathOf(other, c.Root);
        }
        return null;
    }

    static GameObject Anchor(Transform root, string path)
    {
        var t = root.Find(path);
        if (t == null)
            throw new InvalidOperationException($"Anchor '{path}' is missing; run Tools/CoreBuster/Dress Start Workshop before the lighting pass.");
        return t.gameObject;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Light geometry

    /// <summary>
    /// Whether Unity puts <paramref name="light"/> on the per-object light list of a renderer with these bounds. A point
    /// light is tested with its range sphere. A spot light is tested with its frustum: a square pyramid from the light
    /// to its range along its axis, with side planes at half the spot angle and not clipped by the range sphere
    /// (measured with a per-object light-count shader on 2026-09-29, corebuster_render_006).
    /// </summary>
    public static bool Touches(Light light, Bounds b)
    {
        Vector3 p = light.transform.position;
        if (light.type != LightType.Spot) return b.SqrDistance(p) <= light.range * light.range;
        var v = Frustum(light);
        float reach = light.range * Mathf.Sqrt(1 + 2 * Mathf.Pow(Mathf.Tan(light.spotAngle * .5f * Mathf.Deg2Rad), 2));
        if (b.SqrDistance(p) > reach * reach) return false;
        Vector3 r = light.transform.right, u = light.transform.up, fwd = light.transform.forward;
        var edges = new[] { v[1] - v[0], v[2] - v[0], v[3] - v[0], v[4] - v[0], r, u };
        var axes = new List<Vector3> { Vector3.right, Vector3.up, Vector3.forward, fwd };
        for (int i = 0; i < 4; i++) axes.Add(Vector3.Cross(edges[i], edges[(i + 1) % 4]));
        foreach (var e in edges)
        {
            axes.Add(Vector3.Cross(e, Vector3.right));
            axes.Add(Vector3.Cross(e, Vector3.up));
            axes.Add(Vector3.Cross(e, Vector3.forward));
        }
        foreach (var axis in axes)
        {
            if (axis.sqrMagnitude < 1e-8f) continue;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var point in v)
            {
                float d = Vector3.Dot(point, axis);
                lo = Mathf.Min(lo, d);
                hi = Mathf.Max(hi, d);
            }
            float centre = Vector3.Dot(b.center, axis);
            float extent = Mathf.Abs(axis.x) * b.extents.x + Mathf.Abs(axis.y) * b.extents.y + Mathf.Abs(axis.z) * b.extents.z;
            if (hi < centre - extent || lo > centre + extent) return false;
        }
        return true;
    }

    static Vector3[] Frustum(Light light)
    {
        Transform t = light.transform;
        float s = light.range * Mathf.Tan(light.spotAngle * .5f * Mathf.Deg2Rad);
        Vector3 p = t.position, c = p + t.forward * light.range;
        return new[] { p, c + (t.right + t.up) * s, c + (t.right - t.up) * s, c - (t.right + t.up) * s, c + (t.up - t.right) * s };
    }

    /// <summary>
    /// The farthest the light's lit volume reaches from it along unit direction <paramref name="m"/>: its range for a
    /// point light; for a spot, the support of the cone within its range (URP lights nothing outside either).
    /// </summary>
    public static float Reach(Light light, Vector3 m)
    {
        if (light.type != LightType.Spot) return light.range;
        float beyond = Vector3.Angle(light.transform.forward, m) - light.spotAngle * .5f;
        return beyond <= 0 ? light.range : beyond >= 90 ? 0 : light.range * Mathf.Cos(beyond * Mathf.Deg2Rad);
    }

    /// <summary>The walls' outer faces (and the ceiling's top) the room's light volumes must stay inside.</summary>
    sealed class Faces
    {
        public float West, East, South, North, Top;
    }

    static readonly (Vector3 dir, Func<Faces, Vector3, float> room)[] Sides =
    {
        (Vector3.left, (s, p) => p.x - s.West), (Vector3.right, (s, p) => s.East - p.x),
        (Vector3.back, (s, p) => p.z - s.South), (Vector3.forward, (s, p) => s.North - p.z), (Vector3.up, (s, p) => s.Top - p.y),
    };

    /// <summary>
    /// Why the light's lit volume leaves the room through a wall or the ceiling; null if it stays inside. A lamp on a
    /// recessed panel stands a little behind the measured (pilaster) face, so the light only has to stand inside the
    /// walls' outer faces.
    /// </summary>
    static string Containment(Light light, Faces faces, StartWorkshopFrame f)
    {
        Vector3 p = light.transform.position;
        if (p.x < faces.West || p.x > faces.East || p.z < faces.South || p.z > faces.North || p.y < f.Floor || p.y > f.Ceiling)
            return "stands outside the room";
        foreach (var (dir, room) in Sides)
        {
            float reach = Reach(light, dir), limit = room(faces, p) - OuterMargin;
            if (reach > limit) return FormattableString.Invariant($"reaches {reach:F2} m toward {dir} where the room's outer face is {limit + OuterMargin:F2} m away");
        }
        return null;
    }

    /// <summary>The longest range at which the light, as aimed, stays inside the walls' outer faces.</summary>
    static float ContainedRange(Light light, Faces faces)
    {
        float range = light.range, best = float.MaxValue;
        light.range = 1;
        foreach (var (dir, room) in Sides)
        {
            float unit = Reach(light, dir);
            if (unit > 1e-4f) best = Mathf.Min(best, room(faces, light.transform.position) / unit);
        }
        light.range = range;
        return best;
    }

    static bool IsActor(Transform t) =>
        t.GetComponentInParent<Health>(true) != null || t.GetComponentInParent<PlayerCharacterController>(true) != null ||
        t.GetComponentInParent<Animator>(true) != null;

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    /// <summary>Lamps and the strip are props with meshes; the task light, the fill and the probe are not.</summary>
    public static bool IsFixture(Transform child) => child.GetComponentsInChildren<MeshRenderer>().Length > 0;

    /// <summary>
    /// What the room's lights are measured against: the renderers whose per-object budget they share (floor and ceiling
    /// tiles, wall panels, boundary liners), the renderers outside the room they must not reach, the obstacles new
    /// fixtures must clear, the walls' outer faces, and the floor tiles' grid.
    /// </summary>
    sealed class Context
    {
        public readonly Transform Root;
        public readonly StartWorkshopFrame F;
        public Transform Group;
        public readonly Faces Faces;
        public readonly List<(Renderer r, Bounds b)> Budget = new List<(Renderer, Bounds)>();
        public readonly List<List<Light>> Lit = new List<List<Light>>();
        public readonly List<(Renderer r, Bounds b)> Outside = new List<(Renderer, Bounds)>();
        public readonly List<(Transform t, Bounds b)> Obstacles = new List<(Transform, Bounds)>();
        public readonly List<Bounds> FloorTiles = new List<Bounds>();
        public readonly Dictionary<Wall, (float lo, float hi)> Reserved = new Dictionary<Wall, (float, float)>();
        public readonly StringBuilder Log = new StringBuilder();

        public Context(Transform root, StartWorkshopFrame f)
        {
            Root = root;
            F = f;
            var scene = root.gameObject.scene;
            var shell = StartWorkshopLitMaterials.ShellRenderers(scene, root, f);
            var liners = root.Find(StartWorkshopLiners.GroupName);
            foreach (var r in shell) Budget.Add((r, r.bounds));
            if (liners != null)
                foreach (var r in liners.GetComponentsInChildren<MeshRenderer>())
                    if (r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) Budget.Add((r, r.bounds));
            foreach (var _ in Budget) Lit.Add(new List<Light>());
            foreach (var r in shell)
                if (r.name.Contains("Floor Tile") && r.bounds.center.y < f.Floor) FloorTiles.Add(r.bounds);

            var room = new HashSet<Renderer>(StartWorkshopLitMaterials.RoomRenderers(scene, root, f));
            foreach (var go in scene.GetRootGameObjects())
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || room.Contains(r) || IsActor(r.transform) || StartWorkshopLitMaterials.Kept(r.transform)) continue;
                    // The map's ground plane runs under the floor tiles; what a light reaches of it there is never seen.
                    if (r.bounds.max.y <= f.Floor + .01f) continue;
                    Outside.Add((r, r.bounds));
                }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && !InGroup(r.transform, StartWorkshopLiners.GroupName) &&
                    !InGroup(r.transform, GroupName))
                    Obstacles.Add((r.transform, r.bounds));
            Faces = MeasureFaces(scene, root, f, shell);
        }

        bool InGroup(Transform t, string group)
        {
            for (; t != null && t != Root; t = t.parent) if (t.parent == Root && t.name == group) return true;
            return false;
        }

        /// <summary>Quarter points of each floor tile's span along the wall (the tiles of a row or column differ by a
        /// centimetre or so, so spans within 0.2 m of each other are one span).</summary>
        public IEnumerable<float> Slots(Wall wall)
        {
            bool alongZ = wall == Wall.West || wall == Wall.East;
            var spans = new List<(float lo, float hi)>();
            foreach (var (lo, hi) in FloorTiles.Select(b => alongZ ? (b.min.z, b.max.z) : (b.min.x, b.max.x)).OrderBy(s => s.Item1))
                if (spans.Count == 0 || lo - spans[spans.Count - 1].lo > .2f) spans.Add((lo, hi));
            foreach (var (lo, hi) in spans)
            {
                yield return lo + (hi - lo) * .25f;
                yield return lo + (hi - lo) * .75f;
            }
        }

        /// <summary>The floor-tile corner nearest the room's centre, the distance from it to the central tiles' outer
        /// edges, and the bottom of the floor tiles.</summary>
        public (Vector3 corner, float half, float bottom) CentralTiles()
        {
            if (FloorTiles.Count == 0) throw new InvalidOperationException("No Start Workshop floor tiles were measured for the centre fill.");
            var corners = FloorTiles.SelectMany(b => new[] { new Vector3(b.min.x, 0, b.min.z), new Vector3(b.max.x, 0, b.max.z) });
            Vector3 centre = F.Center;
            Vector3 corner = corners.OrderBy(c => (c.x - centre.x) * (c.x - centre.x) + (c.z - centre.z) * (c.z - centre.z)).First();
            var around = FloorTiles.Where(b => (Mathf.Abs(b.min.x - corner.x) < .05f || Mathf.Abs(b.max.x - corner.x) < .05f) &&
                                               (Mathf.Abs(b.min.z - corner.z) < .05f || Mathf.Abs(b.max.z - corner.z) < .05f)).ToList();
            if (around.Count != 4) throw new InvalidOperationException($"Expected four floor tiles around the room's centre, found {around.Count}.");
            float half = around.Min(b => Mathf.Min(b.size.x, b.size.z));
            return (corner, half, around.Min(b => b.min.y));
        }

        /// <summary>
        /// Adds the lights to the room's budget, or says why not: a light leaves the room, reaches a renderer outside
        /// it, or would put a floor tile, ceiling tile, wall panel or liner over <see cref="MaxLightsPerObject"/>.
        /// </summary>
        public string Commit(params Light[] lights)
        {
            var touched = new List<int>();
            foreach (var light in lights)
            {
                string why = Containment(light, Faces, F);
                if (why != null) return $"{light.name} {why}";
                foreach (var (r, b) in Outside)
                    if (Touches(light, b)) return $"{light.name} reaches {PathOf(r.transform, null)} outside the room";
                for (int i = 0; i < Budget.Count; i++)
                    if (Touches(light, Budget[i].b))
                    {
                        touched.Add(i);
                        if (Lit[i].Count + touched.Count(j => j == i) > MaxLightsPerObject)
                            return $"{Budget[i].r.name} would be lit by more than {MaxLightsPerObject} lights";
                    }
            }
            foreach (var light in lights)
                for (int i = 0; i < Budget.Count; i++)
                    if (Touches(light, Budget[i].b)) Lit[i].Add(light);
            return null;
        }

        /// <summary>A placed fixture is an obstacle for the fixtures after it.</summary>
        public void Occupy(Transform fixture)
        {
            foreach (var r in fixture.GetComponentsInChildren<MeshRenderer>()) Obstacles.Add((r.transform, r.bounds));
        }
    }

    /// <summary>
    /// Each wall's outer face from the shell's wall panels and the kept boundary pieces at that wall (the innermost of
    /// them, where the wall is thinnest); the interior face plus <see cref="ShellAllowance"/> where none is found.
    /// </summary>
    static Faces MeasureFaces(Scene scene, Transform root, StartWorkshopFrame f, List<Renderer> shell)
    {
        var pieces = new List<Bounds>(shell.Where(r => r.name.Contains("Wall")).Select(r => r.bounds));
        foreach (var go in scene.GetRootGameObjects())
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                if (r.enabled && !r.transform.IsChildOf(root) && StartWorkshopLitMaterials.KeepShared.Contains(r.name)) pieces.Add(r.bounds);
        float Outer(Func<Bounds, bool> at, Func<Bounds, float> face, bool max, float fallback)
        {
            var found = pieces.Where(at).Select(face).ToList();
            return found.Count == 0 ? fallback : max ? found.Max() : found.Min();
        }
        return new Faces
        {
            West = Outer(b => b.size.x < 1 && Mathf.Abs(b.max.x - f.West) < .5f, b => b.min.x, true, f.West - ShellAllowance),
            East = Outer(b => b.size.x < 1 && Mathf.Abs(b.min.x - f.East) < .5f, b => b.max.x, false, f.East + ShellAllowance),
            South = Outer(b => b.size.z < 1 && Mathf.Abs(b.max.z - f.South) < .5f, b => b.min.z, true, f.South - ShellAllowance),
            North = Outer(b => b.size.z < 1 && Mathf.Abs(b.min.z - f.North) < .5f, b => b.max.z, false, f.North + ShellAllowance),
            Top = f.Ceiling + ShellAllowance,
        };
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Report and validation

    static string Report(Context c)
    {
        var sb = new StringBuilder();
        var lights = c.Group.GetComponentsInChildren<Light>(true);
        int lamps = c.Group.Cast<Transform>().Count(t => t.name.StartsWith(LampPrefix));
        sb.AppendLine($"Start Workshop lighting rebuilt: {lamps} lamps, {lights.Length} lights ({lights.Count(l => l.shadows != LightShadows.None)} shadowed). {c.F}");
        sb.AppendLine(FormattableString.Invariant($"  outer faces: west {c.Faces.West:F2}, east {c.Faces.East:F2}, south {c.Faces.South:F2}, north {c.Faces.North:F2}"));
        foreach (var l in lights)
            sb.AppendLine(FormattableString.Invariant(
                $"  {PathOf(l.transform, c.Root)}: {l.type} at {l.transform.position:F2}, angle {l.spotAngle:F1}, range {l.range:F2}, intensity {l.intensity:F1}, shadows {l.shadows}"));
        sb.Append(c.Log);
        var counts = c.Lit.Select(l => l.Count).ToList();
        sb.AppendLine($"  per-object budget: {c.Budget.Count} shell renderers, most lit by {counts.DefaultIfEmpty(0).Max()} room light(s) (limit {MaxLightsPerObject}):");
        for (int i = 0; i < c.Budget.Count; i++)
            if (counts[i] > 0) sb.AppendLine($"    {c.Budget[i].r.name}: {counts[i]} ({string.Join(", ", c.Lit[i].Select(l => PathOf(l.transform, c.Group)))})");
        foreach (var (r, b) in c.Obstacles.Select(o => (o.t.GetComponent<Renderer>(), o.b)).Where(o => o.Item1 != null))
        {
            int n = lights.Count(l => Touches(l, b));
            if (n > MaxLightsPerObject) sb.AppendLine($"  NOTE prop {PathOf(r.transform, c.Root)} is lit by {n} room lights; the {MaxLightsPerObject} Unity ranks highest light it.");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Explicit corebuster_render_006 rule: the required fixtures and lights exist; every Light in the room is in this
    /// group, realtime, of bounded range, inside the room, lighting nothing beyond the walls' outer faces and reaching
    /// no renderer outside the room; no shell renderer is over the per-object budget; two to four lights cast shadows;
    /// each wall carries its lamp rhythm; the globes glow; the reflection probe is a baked, box-projected probe whose
    /// box touches nothing outside the room.
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add($"missing {GroupName}; run Tools/CoreBuster/Build Start Workshop Lighting.");
            return;
        }
        foreach (var name in Required)
            if (group.Find(name) == null) errors.Add($"{GroupName} is missing {name}.");
        foreach (var light in root.GetComponentsInChildren<Light>(true))
            if (!light.transform.IsChildOf(group)) errors.Add($"{PathOf(light.transform, root)} is a Light outside {GroupName}; the room's lights belong to the lighting pass.");

        var c = new Context(root, f);
        var lights = group.GetComponentsInChildren<Light>(true);
        foreach (var light in lights)
        {
            string label = $"{GroupName}/{PathOf(light.transform, group)}";
            if (light.type != LightType.Spot && light.type != LightType.Point) errors.Add($"{label} is a {light.type} light; only spot and point lights are bounded.");
            if (light.lightmapBakeType != LightmapBakeType.Realtime) errors.Add($"{label} is {light.lightmapBakeType}; the room has no lightmaps, so its lights are realtime.");
            if (light.range > MaxRange) errors.Add(FormattableString.Invariant($"{label} has a {light.range:F1} m range; the limit is {MaxRange} m."));
            string why = Containment(light, c.Faces, f);
            if (why != null) errors.Add($"{label} {why}; its light would reach beyond the room.");
            foreach (var (r, b) in c.Outside)
                if (Touches(light, b))
                {
                    errors.Add($"{label} reaches {PathOf(r.transform, null)}, outside the room.");
                    break;
                }
            for (int i = 0; i < c.Budget.Count; i++)
                if (Touches(light, c.Budget[i].b)) c.Lit[i].Add(light);
        }
        for (int i = 0; i < c.Budget.Count; i++)
            if (c.Lit[i].Count > MaxLightsPerObject)
                errors.Add($"{c.Budget[i].r.name} is lit by {c.Lit[i].Count} room lights, over URP's per-object limit of {MaxLightsPerObject}: " +
                           string.Join(", ", c.Lit[i].Select(l => PathOf(l.transform, group))) + ".");
        int shadowed = lights.Count(l => l.shadows != LightShadows.None);
        if (shadowed < MinShadowed || shadowed > MaxShadowed)
            errors.Add($"{GroupName} has {shadowed} shadow-casting lights; the budget is {MinShadowed}-{MaxShadowed}.");

        foreach (Wall wall in new[] { Wall.West, Wall.North, Wall.East, Wall.South })
        {
            int n = group.Cast<Transform>().Count(t => t.name.StartsWith($"{LampPrefix}{wall}_")) + (wall == Wall.North ? 2 : 0);
            if (n < MinLampsPerWall) errors.Add($"The {wall} wall has {n} lamps; the rhythm needs at least {MinLampsPerWall}.");
        }
        foreach (Transform t in group)
        {
            if (!t.name.StartsWith(LampPrefix)) continue;
            var globe = t.Find("Globe")?.GetComponent<Renderer>();
            if (globe == null || globe.sharedMaterial == null || !globe.sharedMaterial.IsKeywordEnabled("_EMISSION"))
                errors.Add($"{GroupName}/{t.name} needs an emissive amber globe.");
            if (t.Find(PoolName)?.GetComponent<Light>() == null || t.Find(WashName)?.GetComponent<Light>() == null)
                errors.Add($"{GroupName}/{t.name} needs its {PoolName} and {WashName}.");
        }

        var probes = root.GetComponentsInChildren<ReflectionProbe>(true);
        var probe = group.Find(ProbeName)?.GetComponent<ReflectionProbe>();
        if (probes.Length != 1 || probe == null) errors.Add($"The room needs exactly one reflection probe, {GroupName}/{ProbeName}.");
        else
        {
            if (probe.mode != ReflectionProbeMode.Custom || probe.customBakedTexture == null ||
                AssetDatabase.GetAssetPath(probe.customBakedTexture) != ProbeTexturePath)
                errors.Add($"{GroupName}/{ProbeName} must use its own bake, {ProbeTexturePath}; run Tools/CoreBuster/Build Start Workshop Lighting.");
            if (!probe.boxProjection) errors.Add($"{GroupName}/{ProbeName} must be box-projected.");
            Bounds box = probe.bounds, frame = ProbeBox(f);
            if ((box.min - frame.min).magnitude > .01f || (box.max - frame.max).magnitude > .01f)
                errors.Add($"{GroupName}/{ProbeName}'s box {box.min:F2} to {box.max:F2} is not the room interior {frame.min:F2} to {frame.max:F2}.");
            foreach (var (r, b) in c.Outside)
                if (Mathf.Min(box.max.x, b.max.x) > Mathf.Max(box.min.x, b.min.x) + .001f &&
                    Mathf.Min(box.max.y, b.max.y) > Mathf.Max(box.min.y, b.min.y) + .001f &&
                    Mathf.Min(box.max.z, b.max.z) > Mathf.Max(box.min.z, b.min.z) + .001f)
                    errors.Add($"{GroupName}/{ProbeName}'s box reaches {PathOf(r.transform, null)}, outside the room.");
        }

    }

    static string PathOf(Transform t, Transform root)
    {
        string path = t.name;
        for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    /// <summary>The fixtures' finishes: the workstation's shared steel finishes and two project-owned emissive materials.</summary>
    sealed class Materials
    {
        public readonly Material Steel, Painted, Amber, Strip;

        public Materials()
        {
            var finishes = new StartWorkshopWorkstation.Finishes();
            Steel = finishes.BareSteel;
            Painted = finishes.PaintedSteel;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            Amber = Emissive("M_Lamp_AmberGlass", lit, new Color(1f, .66f, .3f), new Color(7f, 3.8f, 1.2f));
            Strip = Emissive("M_Lamp_WarmStrip", lit, new Color(1f, .86f, .62f), new Color(3.2f, 2.3f, 1.2f));
        }

        static Material Emissive(string name, Shader shader, Color baseColor, Color emission)
        {
            string path = Props + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", .85f);
            m.SetColor("_EmissionColor", emission);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
