using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// corebuster_env_040 density check. Reads the bay map in NorthWestRoomDensityPlan.json and measures the saved
/// MainScene without changing it: occupied wall line, clear floor, planned lane and connector widths, open-space
/// areas, loop clearance, budgets, and whether Room 3 body-height colliders stay inside planned footprints.
/// Later Room 3 builders call Check before saving so every pass reports the same numbers.
/// </summary>
public static class NorthWestRoomDensity
{
    public const string PlanPath = NorthWestRoomFramework.AssetRoot + "/NorthWestRoomDensityPlan.json";
    public const string Output = "ReviewCaptures/NorthWestRoom/040";
    const float Grid = .25f, ColumnDepth = 3f, ServicesTop = 6f, OccupiedHeight = 1.8f, BodyBand = 2.2f,
        BodyOccupiedHeight = 1.2f, FlatOverlay = .1f, LaneStep = .5f, LaneCap = 4f, FloorClearance = .15f,
        PlanTolerance = .05f, LoopStep = .25f;
    static readonly float[] LaneHeights = { .3f, 1.0f, 1.8f };

    // ---- Plan data (written by Tools/Art/plan_north_west_room_density.py; world X/Z metres).
    [Serializable] public class XZ { public float x, z; }
    [Serializable] public class Area { public XZ min, max; }
    [Serializable] public class Bay { public string id, wall, heightBand, content, owner; public Area footprint; public float depth, length; public bool built; }
    [Serializable] public class Anchor { public string id, content, owner; public Area footprint; public float maxColliderHeight; }
    [Serializable] public class OpenSpace { public string id, name, carries; public XZ centre; public float radius, projectedClearArea; public XZ[] polygon; }
    [Serializable] public class Lane { public string id, name; public XZ[] polyline; public float requiredWidth; }
    [Serializable] public class Loop { public string id, name, via; public XZ[] polyline; }
    [Serializable] public class Targets { public float occupiedWallLineMin, clearFloorMin, clearFloorMax, laneWidthTarget, laneWidthFloor; public int openSpacesMin, loopRoutesMin; }
    [Serializable] public class TaskCount { public string task; public int count; }
    [Serializable] public class Budget { public int original, proposed, perRendererMax; public TaskCount[] perTask; }
    [Serializable] public class Budgets { public Budget prefabInstances, realtimeLights, carvingObstacles, materials; }
    [Serializable] public class Plan
    {
        public string task, status, surveySceneSha256;
        public Area interior;
        public Targets targets;
        public Budgets budgets;
        public Bay[] bays;
        public Anchor[] anchors;
        public OpenSpace[] openSpaces;
        public Lane[] lanes;
        public Loop[] loops;
    }

    // ---- Measured result.
    [Serializable] public class WallResult { public string wall; public float length, occupied, bodyOccupied, occupiedFraction, bodyOccupiedFraction; }
    [Serializable] public class LaneResult { public string id, name; public bool connector; public float required, width; public Vector3 narrowestAt; }
    [Serializable] public class SpaceResult { public string id, name; public float clearArea, projectedClearArea; }
    [Serializable] public class LoopResult { public string id, name; public int samples, blocked; public string firstBlocker; public bool clear; }
    [Serializable] public class BudgetResult { public string name; public int used, original, proposed; }
    [Serializable] public class Report
    {
        public string task = "corebuster_env_040", unityVersion, sceneSha256, measuredUtc, planStatus;
        public float interiorArea, clearFloorArea, clearFloorFraction, nonReservedWallLength, occupiedWallLine, bodyOccupiedWallLine,
            minPlannedLaneWidth, minConnectorWidth;
        public int clearLoops;
        public bool meetsTargets;
        public List<WallResult> walls = new List<WallResult>();
        public List<LaneResult> lanes = new List<LaneResult>();
        public List<SpaceResult> openSpaces = new List<SpaceResult>();
        public List<LoopResult> loops = new List<LoopResult>();
        public List<BudgetResult> budgets = new List<BudgetResult>();
        public List<string> occupyingColliders = new List<string>();
        public List<string> errors = new List<string>();
        public List<string> targetsNotMet = new List<string>();
    }

    [MenuItem("Tools/CoreBuster/Measure North-West Room Density")]
    public static void MeasureMenu()
    {
        using var culture = new InvariantNumbers();
        var scene = SceneManager.GetActiveScene();
        var report = Measure(scene, NorthWestRoomFramework.MeasureLayout());
        Write(report);
        Log(report, false);
    }

    /// <summary>Batch entry point: measures the saved scene, writes the report, and proves the scene is unchanged.</summary>
    public static void MeasureBatch()
    {
        using var culture = new InvariantNumbers();
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        string before = Hash(NorthWestRoomSurvey.ScenePath);
        var scene = SceneManager.GetActiveScene();
        var layout = NorthWestRoomFramework.MeasureLayout();
        var report = Measure(scene, layout);
        Write(report);
        // Read-only regression guard: the saved room must still pass the framework and its approved budgets.
        bool frameworkValid = NorthWestRoomFramework.Validate(scene, layout, true);
        string after = Hash(NorthWestRoomSurvey.ScenePath);
        if (scene.isDirty || before != after || report.sceneSha256 != before)
            throw new InvalidOperationException($"MainScene changed during the density check (before {before}, after {after}, dirty {scene.isDirty}).");
        Log(report, false);
        if (report.errors.Count > 0) throw new InvalidOperationException("North-West Room density plan errors; see " + Output + "/Density.json.");
        if (!frameworkValid) throw new InvalidOperationException("North-West Room framework validation failed on the saved scene.");
    }

    /// <summary>For section builders: plan, lane and footprint errors always fail; target misses fail when requireTargets.</summary>
    public static bool Check(Scene scene, NorthWestRoomFramework.Layout layout, bool requireTargets, bool log)
    {
        using var culture = new InvariantNumbers();
        var report = Measure(scene, layout);
        if (log) Log(report, requireTargets);
        return report.errors.Count == 0 && (!requireTargets || report.meetsTargets);
    }

    public static Plan LoadPlan()
    {
        if (!File.Exists(PlanPath)) throw new FileNotFoundException("Missing corebuster_env_040 bay map.", PlanPath);
        var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath));
        if (plan == null || plan.interior == null || plan.targets == null || plan.bays == null || plan.bays.Length == 0)
            throw new InvalidOperationException("Unreadable corebuster_env_040 bay map at " + PlanPath + ".");
        return plan;
    }

    public static Report Measure(Scene scene, NorthWestRoomFramework.Layout layout)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (scene.path != NorthWestRoomSurvey.ScenePath) throw new InvalidOperationException("Open MainScene before measuring Room 3 density.");
        var plan = LoadPlan();
        var survey = layout.report;
        var report = new Report
        {
            unityVersion = Application.unityVersion, sceneSha256 = Hash(NorthWestRoomSurvey.ScenePath),
            measuredUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), planStatus = plan.status,
        };
        Physics.SyncTransforms();
        var shellRoots = ShellRoots(survey);
        var root = FindRoot(scene);
        Bounds interior = layout.interior;
        float floor = layout.Floor, top = layout.MovementTop;

        ValidatePlan(plan, layout, report.errors);
        var footprints = plan.bays.Select(b => ToRect(b.footprint)).Concat((plan.anchors ?? new Anchor[0]).Select(a => ToRect(a.footprint))).ToArray();

        // ---- Clear floor: 0.25 m cells whose movement-band column touches no collider other than the shell.
        int nx = Mathf.FloorToInt(interior.size.x / Grid), nz = Mathf.FloorToInt(interior.size.z / Grid);
        var occupied = new bool[nx, nz];
        var buffer = new Collider[64];
        var occupying = new HashSet<string>(StringComparer.Ordinal);
        float bandHalf = (top - floor - FloorClearance) * .5f;
        int occupiedCount = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                Vector3 c = Cell(interior, i, j, floor + FloorClearance + bandHalf);
                int n = Physics.OverlapBoxNonAlloc(c, new Vector3(Grid * .4f, bandHalf, Grid * .4f), buffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    if (IsShell(buffer[k], shellRoots)) continue;
                    occupied[i, j] = true;
                    occupying.Add(PathOf(buffer[k].transform));
                }
                if (occupied[i, j]) occupiedCount++;
            }
        report.interiorArea = interior.size.x * interior.size.z;
        report.clearFloorFraction = 1f - occupiedCount / (float)(nx * nz);
        report.clearFloorArea = report.clearFloorFraction * report.interiorArea;
        report.occupyingColliders = occupying.OrderBy(s => s, StringComparer.Ordinal).ToList();

        // ---- Occupied wall line: non-reserved stations whose 3 m wall column holds at least 1.8 m of Room 3 content.
        var renderers = root ? root.GetComponentsInChildren<Renderer>(true)
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.bounds.size.y >= FlatOverlay && !NorthWestRoomFramework.IsRenderingSkin(r.transform)).Select(r => r.bounds).ToArray() : new Bounds[0];
        float totalLength = 0, totalOccupied = 0, totalBody = 0;
        foreach (var wall in new[] { "South", "North", "West", "East" })
        {
            bool alongX = wall == "South" || wall == "North";
            float start = alongX ? interior.min.x : interior.min.z, end = alongX ? interior.max.x : interior.max.z;
            var reserved = survey.zones.Where(z => z.kind == "reserved" && z.wall == wall)
                .Select(z => alongX ? new Vector2(z.min.x, z.max.x) : new Vector2(z.min.z, z.max.z)).ToArray();
            var result = new WallResult { wall = wall };
            for (float t = start + Grid * .5f; t < end; t += Grid)
            {
                if (reserved.Any(r => t >= r.x && t <= r.y)) continue;
                result.length += Grid;
                var column = WallColumn(interior, wall, t, floor);
                float all = CoveredHeight(renderers, column, floor, floor + ServicesTop);
                float body = CoveredHeight(renderers, column, floor, floor + BodyBand);
                if (all >= OccupiedHeight) result.occupied += Grid;
                if (body >= BodyOccupiedHeight) result.bodyOccupied += Grid;
            }
            result.occupiedFraction = result.length > 0 ? result.occupied / result.length : 0;
            result.bodyOccupiedFraction = result.length > 0 ? result.bodyOccupied / result.length : 0;
            report.walls.Add(result);
            totalLength += result.length; totalOccupied += result.occupied; totalBody += result.bodyOccupied;
        }
        report.nonReservedWallLength = totalLength;
        report.occupiedWallLine = totalLength > 0 ? totalOccupied / totalLength : 0;
        report.bodyOccupiedWallLine = totalLength > 0 ? totalBody / totalLength : 0;

        // ---- Lanes: planned lanes plus every survey connector, measured with perpendicular rays at three heights.
        foreach (var lane in plan.lanes ?? new Lane[0])
            report.lanes.Add(MeasureLane(lane.id, lane.name, false, lane.requiredWidth, lane.polyline.Select(p => new Vector3(p.x, floor, p.z)).ToArray(), interior));
        foreach (var route in survey.routes.Where(r => r.connector && r.corners != null && r.corners.Length > 1))
            report.lanes.Add(MeasureLane("C:" + route.name, route.name, true, survey.connectorHalfWidth * 2, route.corners.Select(p => new Vector3(p.x, floor, p.z)).ToArray(), interior));
        report.minPlannedLaneWidth = report.lanes.Where(l => !l.connector).Select(l => l.width).DefaultIfEmpty(0).Min();
        report.minConnectorWidth = report.lanes.Where(l => l.connector).Select(l => l.width).DefaultIfEmpty(0).Min();
        foreach (var lane in report.lanes.Where(l => l.width + .005f < l.required))
            report.errors.Add($"Lane '{lane.id}' is {lane.width:F2} m clear at {lane.narrowestAt:F2}; it must stay at least {lane.required:F1} m.");

        // ---- Open spaces: clear cells inside each planned space (secondary spaces exclude the arena disc).
        Vector3 centre = survey.centre;
        foreach (var space in plan.openSpaces ?? new OpenSpace[0])
        {
            bool disc = space.radius > 0;
            var polygon = disc ? null : space.polygon.Select(p => new Vector2(p.x, p.z)).ToArray();
            int clear = 0;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (occupied[i, j]) continue;
                    Vector3 c = Cell(interior, i, j, floor);
                    float r = FlatDistance(c, centre);
                    if (disc ? r <= space.radius : r > survey.ringOuterRadius && InPolygon(new Vector2(c.x, c.z), polygon)) clear++;
                }
            report.openSpaces.Add(new SpaceResult { id = space.id, name = space.name, clearArea = clear * Grid * Grid, projectedClearArea = space.projectedClearArea });
        }

        // ---- Loops: a player capsule must fit at every sample around each closed route.
        foreach (var loop in plan.loops ?? new Loop[0])
        {
            var result = new LoopResult { id = loop.id, name = loop.name };
            var points = loop.polyline.Select(p => new Vector3(p.x, floor, p.z)).ToArray();
            for (int s = 0; s < points.Length; s++)
            {
                Vector3 a = points[s], b = points[(s + 1) % points.Length];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / LoopStep));
                for (int k = 0; k < steps; k++)
                {
                    Vector3 feet = Vector3.Lerp(a, b, k / (float)steps);
                    result.samples++;
                    var hit = Physics.OverlapCapsule(feet + Vector3.up * .45f, feet + Vector3.up * 1.45f, .4f, ~0, QueryTriggerInteraction.Ignore)
                        .FirstOrDefault(c => !IsShell(c, shellRoots));
                    if (!hit) continue;
                    result.blocked++;
                    if (result.firstBlocker == null) result.firstBlocker = PathOf(hit.transform) + $" at {feet:F2}";
                }
            }
            result.clear = result.blocked == 0;
            report.loops.Add(result);
        }
        report.clearLoops = report.loops.Count(l => l.clear);

        // ---- Plan conformance: every Room 3 body-height collider stays inside the planned footprints.
        if (root)
            foreach (var collider in root.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy))
            {
                Bounds b = collider.bounds;
                if (b.min.y >= top) continue;
                if (!InsideFootprints(b, footprints))
                    report.errors.Add($"{PathOf(collider.transform)} reaches body height outside every planned bay or anchor footprint.");
                foreach (var anchor in plan.anchors ?? new Anchor[0])
                    if (ContainsXZ(ToRect(anchor.footprint), b) && b.max.y > floor + anchor.maxColliderHeight + PlanTolerance)
                        report.errors.Add($"{PathOf(collider.transform)} in loop anchor {anchor.id} is {b.max.y - floor:F2} m tall (limit {anchor.maxColliderHeight:F1} m).");
            }

        // ---- Budgets, counted as NorthWestRoomFramework counts them.
        if (root)
        {
            AddBudget(report, "prefabInstances", root.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)), plan.budgets?.prefabInstances);
            AddBudget(report, "realtimeLights", root.GetComponentsInChildren<Light>(true).Count(l => l.enabled), plan.budgets?.realtimeLights);
            AddBudget(report, "carvingObstacles", root.GetComponentsInChildren<NavMeshObstacle>(true).Count(o => o.enabled && o.carving), plan.budgets?.carvingObstacles);
            AddBudget(report, "materials", root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count(), plan.budgets?.materials);
        }
        foreach (var budget in report.budgets.Where(b => b.proposed > 0 && b.used > b.proposed))
            report.errors.Add($"Room 3 uses {budget.used} {budget.name}; the approved corebuster_env_040 ceiling is {budget.proposed}.");

        // ---- Targets. The baseline before the dressing passes is expected to miss the density targets.
        var targets = plan.targets;
        if (report.occupiedWallLine < targets.occupiedWallLineMin) report.targetsNotMet.Add($"Occupied wall line {report.occupiedWallLine:P0} is below {targets.occupiedWallLineMin:P0}.");
        if (report.clearFloorFraction > targets.clearFloorMax) report.targetsNotMet.Add($"Clear floor {report.clearFloorFraction:P0} is above {targets.clearFloorMax:P0} (room too sparse).");
        if (report.clearFloorFraction < targets.clearFloorMin) report.targetsNotMet.Add($"Clear floor {report.clearFloorFraction:P0} is below {targets.clearFloorMin:P0} (room too enclosed).");
        int spaces = report.openSpaces.Count(s => s.clearArea > 0);
        if (spaces < targets.openSpacesMin) report.targetsNotMet.Add($"Only {spaces} open spaces have clear floor; {targets.openSpacesMin} are required.");
        if (report.clearLoops < targets.loopRoutesMin) report.targetsNotMet.Add($"Only {report.clearLoops} loop routes are clear; {targets.loopRoutesMin} are required.");
        var anchorsBuilt = (plan.anchors ?? new Anchor[0]).Count(a => AnchorBuilt(a, occupied, interior, nx, nz));
        if (anchorsBuilt < targets.loopRoutesMin) report.targetsNotMet.Add($"Only {anchorsBuilt} loop anchors are built; loops need an obstacle to circle (corebuster_env_034).");
        report.meetsTargets = report.targetsNotMet.Count == 0;
        return report;
    }

    static void ValidatePlan(Plan plan, NorthWestRoomFramework.Layout layout, List<string> errors)
    {
        var interior = layout.interior;
        if (Mathf.Abs(plan.interior.min.x - interior.min.x) > PlanTolerance || Mathf.Abs(plan.interior.min.z - interior.min.z) > PlanTolerance ||
            Mathf.Abs(plan.interior.max.x - interior.max.x) > PlanTolerance || Mathf.Abs(plan.interior.max.z - interior.max.z) > PlanTolerance)
            errors.Add("The bay map was drawn for a different measured interior; re-derive it from a fresh corebuster_env_029 survey.");
        var ids = plan.bays.Select(b => b.id).Concat((plan.anchors ?? new Anchor[0]).Select(a => a.id)).ToArray();
        foreach (var dup in ids.GroupBy(i => i).Where(g => g.Count() > 1)) errors.Add($"Bay map reuses ID {dup.Key}.");
        foreach (var bay in plan.bays)
        {
            string violation = layout.KeepOutViolation(BodyBounds(bay.footprint, layout));
            if (violation != null) errors.Add($"Planned bay {bay.id} {violation}.");
            if (string.IsNullOrEmpty(bay.owner)) errors.Add($"Planned bay {bay.id} has no owning task.");
        }
        foreach (var anchor in plan.anchors ?? new Anchor[0])
        {
            string violation = layout.KeepOutViolation(BodyBounds(anchor.footprint, layout));
            if (violation != null) errors.Add($"Planned loop anchor {anchor.id} {violation}.");
        }
        foreach (var lane in plan.lanes ?? new Lane[0])
            if (lane.requiredWidth < plan.targets.laneWidthFloor) errors.Add($"Planned lane {lane.id} requires only {lane.requiredWidth:F1} m; the floor is {plan.targets.laneWidthFloor:F1} m.");
    }

    static LaneResult MeasureLane(string id, string name, bool connector, float required, Vector3[] points, Bounds interior)
    {
        var result = new LaneResult { id = id, name = name, connector = connector, required = required, width = float.MaxValue };
        for (int s = 1; s < points.Length; s++)
        {
            Vector3 a = points[s - 1], b = points[s], along = b - a; along.y = 0;
            if (along.sqrMagnitude < 1e-6f) continue;
            Vector3 side = Vector3.Cross(Vector3.up, along.normalized);
            int steps = Mathf.Max(1, Mathf.CeilToInt(along.magnitude / LaneStep));
            for (int k = 0; k <= steps; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, k / (float)steps);
                float width = float.MaxValue;
                foreach (float h in LaneHeights)
                {
                    Vector3 origin = new Vector3(p.x, p.y + h, p.z);
                    width = Physics.CheckSphere(origin, .05f, ~0, QueryTriggerInteraction.Ignore) ? 0
                        : Mathf.Min(width, Cast(origin, side, interior) + Cast(origin, -side, interior));
                    if (width == 0) break;
                }
                if (width < result.width) { result.width = width; result.narrowestAt = p; }
            }
        }
        if (result.width == float.MaxValue) result.width = 0;
        return result;
    }

    static float Cast(Vector3 origin, Vector3 direction, Bounds interior)
    {
        float limit = LaneCap;
        if (direction.x > 1e-4f) limit = Mathf.Min(limit, (interior.max.x - origin.x) / direction.x);
        if (direction.x < -1e-4f) limit = Mathf.Min(limit, (interior.min.x - origin.x) / direction.x);
        if (direction.z > 1e-4f) limit = Mathf.Min(limit, (interior.max.z - origin.z) / direction.z);
        if (direction.z < -1e-4f) limit = Mathf.Min(limit, (interior.min.z - origin.z) / direction.z);
        limit = Mathf.Max(0, limit);
        return Physics.Raycast(origin, direction, out var hit, limit, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : limit;
    }

    static Bounds WallColumn(Bounds interior, string wall, float t, float floor)
    {
        var b = new Bounds();
        float y0 = floor, y1 = floor + ServicesTop, h = Grid * .5f;
        switch (wall)
        {
            case "South": b.SetMinMax(new Vector3(t - h, y0, interior.min.z), new Vector3(t + h, y1, interior.min.z + ColumnDepth)); break;
            case "North": b.SetMinMax(new Vector3(t - h, y0, interior.max.z - ColumnDepth), new Vector3(t + h, y1, interior.max.z)); break;
            case "West": b.SetMinMax(new Vector3(interior.min.x, y0, t - h), new Vector3(interior.min.x + ColumnDepth, y1, t + h)); break;
            default: b.SetMinMax(new Vector3(interior.max.x - ColumnDepth, y0, t - h), new Vector3(interior.max.x, y1, t + h)); break;
        }
        return b;
    }

    /// <summary>Union of the renderer heights inside a wall column, clipped to [low, high].</summary>
    static float CoveredHeight(Bounds[] renderers, Bounds column, float low, float high)
    {
        var spans = renderers.Where(r => r.min.x < column.max.x && r.max.x > column.min.x && r.min.z < column.max.z && r.max.z > column.min.z)
            .Select(r => new Vector2(Mathf.Max(low, r.min.y), Mathf.Min(high, r.max.y))).Where(s => s.y > s.x).OrderBy(s => s.x).ToArray();
        float covered = 0, start = float.MinValue, end = float.MinValue;
        foreach (var s in spans)
        {
            if (s.x > end) { if (end > start) covered += end - start; start = s.x; end = s.y; }
            else end = Mathf.Max(end, s.y);
        }
        if (end > start) covered += end - start;
        return covered;
    }

    static bool AnchorBuilt(Anchor anchor, bool[,] occupied, Bounds interior, int nx, int nz)
    {
        var r = ToRect(anchor.footprint);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                if (!occupied[i, j]) continue;
                Vector3 c = Cell(interior, i, j, 0);
                if (r.Contains(new Vector2(c.x, c.z))) return true;
            }
        return false;
    }

    static bool InsideFootprints(Bounds b, Rect[] footprints)
    {
        float x0 = b.min.x + PlanTolerance, x1 = b.max.x - PlanTolerance, z0 = b.min.z + PlanTolerance, z1 = b.max.z - PlanTolerance;
        if (x1 < x0) x0 = x1 = b.center.x;
        if (z1 < z0) z0 = z1 = b.center.z;
        int sx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / Grid)), sz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / Grid));
        for (int i = 0; i <= sx; i++)
            for (int j = 0; j <= sz; j++)
            {
                var p = new Vector2(Mathf.Lerp(x0, x1, i / (float)sx), Mathf.Lerp(z0, z1, j / (float)sz));
                if (!footprints.Any(f => f.Contains(p))) return false;
            }
        return true;
    }

    static bool ContainsXZ(Rect r, Bounds b) =>
        b.min.x >= r.xMin - PlanTolerance && b.max.x <= r.xMax + PlanTolerance && b.min.z >= r.yMin - PlanTolerance && b.max.z <= r.yMax + PlanTolerance;

    static void AddBudget(Report report, string name, int used, Budget budget) =>
        report.budgets.Add(new BudgetResult { name = name, used = used, original = budget?.original ?? 0, proposed = budget?.proposed ?? 0 });

    static HashSet<Transform> ShellRoots(NorthWestRoomSurvey.Report survey)
    {
        var roots = new HashSet<Transform>();
        foreach (var piece in survey.shell)
            if (GlobalObjectId.TryParse(piece.id, out var id) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is Component c)
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(c.gameObject);
                roots.Add(root ? root.transform : c.transform);
            }
        if (roots.Count == 0) throw new InvalidOperationException("Could not resolve any surveyed shell piece; re-run the corebuster_env_029 survey.");
        return roots;
    }

    static bool IsShell(Collider collider, HashSet<Transform> roots)
    {
        for (var t = collider.transform; t; t = t.parent) if (roots.Contains(t)) return true;
        return false;
    }

    static void Write(Report report)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Density.json", JsonUtility.ToJson(report, true));
        File.WriteAllText(Output + "/Density.txt", Summary(report, false) + "\n");
    }

    static void Log(Report report, bool requireTargets)
    {
        string text = Summary(report, requireTargets);
        if (report.errors.Count > 0 || requireTargets && !report.meetsTargets) Debug.LogError(text); else Debug.Log(text);
    }

    static string Summary(Report r, bool requireTargets)
    {
        var lines = new List<string>
        {
            $"NORTH-WEST ROOM DENSITY ({(r.errors.Count > 0 ? "PLAN ERRORS" : r.meetsTargets ? "TARGETS MET" : requireTargets ? "TARGETS NOT MET" : "BASELINE, TARGETS NOT YET MET")}): scene {r.sceneSha256}",
            $"  Occupied wall line {r.occupiedWallLine:P1} of {r.nonReservedWallLength:F2} m non-reserved (body height {r.bodyOccupiedWallLine:P1}); " +
                string.Join(", ", r.walls.Select(w => $"{w.wall} {w.occupiedFraction:P0}")),
            $"  Clear floor {r.clearFloorFraction:P1} = {r.clearFloorArea:F1} of {r.interiorArea:F1} m2",
            $"  Planned lanes: " + string.Join(", ", r.lanes.Where(l => !l.connector).Select(l => $"{l.id} {l.width:F2} m")) + $"; narrowest connector {r.minConnectorWidth:F2} m",
            $"  Open spaces: " + string.Join(", ", r.openSpaces.Select(s => $"{s.id} {s.clearArea:F0} m2")),
            $"  Loops clear: {r.clearLoops}/{r.loops.Count}",
            $"  Budgets: " + string.Join(", ", r.budgets.Select(b => $"{b.name} {b.used} (ceiling {b.proposed}, original {b.original})")),
        };
        lines.AddRange(r.errors.Select(e => "  ERROR: " + e));
        lines.AddRange(r.targetsNotMet.Select(e => "  Target: " + e));
        return string.Join("\n", lines);
    }

    /// <summary>Reports and logs use '.' decimals whatever the machine locale is.</summary>
    sealed class InvariantNumbers : IDisposable
    {
        readonly CultureInfo previous = CultureInfo.CurrentCulture;
        public InvariantNumbers() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        public void Dispose() => CultureInfo.CurrentCulture = previous;
    }

    static Bounds BodyBounds(Area a, NorthWestRoomFramework.Layout layout)
    {
        var b = new Bounds();
        b.SetMinMax(new Vector3(a.min.x, layout.Floor, a.min.z), new Vector3(a.max.x, layout.Floor + BodyBand, a.max.z));
        return b;
    }
    static Rect ToRect(Area a) => Rect.MinMaxRect(a.min.x - PlanTolerance, a.min.z - PlanTolerance, a.max.x + PlanTolerance, a.max.z + PlanTolerance);
    static Vector3 Cell(Bounds interior, int i, int j, float y) => new Vector3(interior.min.x + (i + .5f) * Grid, y, interior.min.z + (j + .5f) * Grid);
    static float FlatDistance(Vector3 a, Vector3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
    static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }
    static Transform FindRoot(Scene scene)
    {
        var level = scene.GetRootGameObjects().Select(g => g.transform).SingleOrDefault(t => t.name == NorthWestRoomFramework.LevelName);
        return level ? level.Cast<Transform>().SingleOrDefault(t => t.name == NorthWestRoomFramework.RootName) : null;
    }
    static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
}
