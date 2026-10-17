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
/// corebuster_env_043 read-only Play-mode probe on the carved runtime NavMesh: every baseline-complete survey route,
/// each planned loop walked leg by leg, and a 0.5 m scan of the room for floor that is unreachable from the arena or
/// cornered (fewer than four of sixteen directions open for 2.5 m). Cornered spots are reported, not failed: they are
/// evidence for the scored review and the owner's live-wave walk, which this probe does not replace.
/// </summary>
[InitializeOnLoad]
public static class NorthWestRoomSignoffNavigation
{
    const string Key = "CoreBuster.NorthWestSignoffNavigation.";
    const string Output = "ReviewCaptures/NorthWestRoom/043/Runtime_Navigation.txt";
    const float Step = .5f, Reach = 4f, Open = 2.5f, Detour = 1.6f;
    const int Directions = 16, CorneredBelow = 4;
    static double enteredAt;
    static bool running;

    static NorthWestRoomSignoffNavigation()
    {
        EditorApplication.playModeStateChanged += OnState;
        EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Active", false) && EditorApplication.isPlaying)
        {
            enteredAt = EditorApplication.timeSinceStartup;
            running = true;
        }
    }

    // Run without -quit: the callback exits after Play mode and domain reload finish.
    public static void RunBatch() => StartBatch(false);

    // env_046 keeps the original env_043 evidence and fails if any of the four repaired bays
    // still contains a cornered runtime sample. Other recesses retain report-only semantics.
    public static void RunDropBayBatch() => StartBatch(true);

    static void StartBatch(bool dropBays)
    {
        SessionState.SetBool(Key + "DropBays", dropBays);
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        var layout = NorthWestRoomFramework.MeasureLayout();
        SessionState.SetString(Key + "Report", JsonUtility.ToJson(layout.report));
        SessionState.SetString(Key + "Scene", SceneHash());
        SessionState.SetBool(Key + "Active", true);
        SessionState.SetInt(Key + "Result", 1);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            enteredAt = EditorApplication.timeSinceStartup;
            running = true;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        int result = SessionState.GetInt(Key + "Result", 1);
        if (SceneHash() != SessionState.GetString(Key + "Scene", ""))
        {
            Debug.LogError("Sign-off navigation probe changed the saved scene.");
            result = 1;
        }
        SessionState.EraseBool(Key + "Active");
        SessionState.EraseBool(Key + "DropBays");
        SessionState.EraseString(Key + "Scene");
        SessionState.EraseString(Key + "Report");
        if (Application.isBatchMode) EditorApplication.Exit(result);
    }

    static void Tick()
    {
        if (!running || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup - enteredAt < 3) return;
        running = false;
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var report = JsonUtility.FromJson<NorthWestRoomSurvey.Report>(SessionState.GetString(Key + "Report", ""));
            if (report == null || report.spawns.Count != 8) throw new InvalidOperationException("Missing measured Room 3 report.");
            var plan = NorthWestRoomDensity.LoadPlan();
            var lines = new List<string>();
            float floor = report.floorTopY;
            Vector3 centre = Sample(report.centre, 1f) ?? throw new InvalidOperationException("Arena centre is off the runtime NavMesh.");

            int routes = 0;
            foreach (var route in report.routes.Where(r => r.status == "PathComplete"))
            {
                var a = Sample(route.start, .35f); var b = Sample(route.end, .35f);
                if (a == null || b == null || !Complete(a.Value, b.Value, out _)) throw new InvalidOperationException("Runtime route is incomplete: " + route.name);
                routes++;
            }
            lines.Add($"Survey routes complete: {routes}");

            foreach (var loop in plan.loops ?? new NorthWestRoomDensity.Loop[0])
            {
                float worst = 0;
                for (int i = 0; i < loop.polyline.Length; i++)
                {
                    var p = loop.polyline[i]; var q = loop.polyline[(i + 1) % loop.polyline.Length];
                    var a = Sample(new Vector3(p.x, floor, p.z), .5f); var b = Sample(new Vector3(q.x, floor, q.z), .5f);
                    if (a == null || b == null || !Complete(a.Value, b.Value, out float length))
                        throw new InvalidOperationException($"{loop.id} leg {i} is not walkable on the runtime NavMesh.");
                    float straight = Vector3.Distance(a.Value, b.Value);
                    if (straight > .5f) worst = Mathf.Max(worst, length / straight);
                }
                lines.Add($"{loop.id} ({loop.via}): every leg complete, worst detour ratio {worst:F2}" + (worst > Detour ? " (DETOUR: a leg is blocked and routes around)" : ""));
            }

            var interior = new Rect(plan.interior.min.x, plan.interior.min.z, plan.interior.max.x - plan.interior.min.x, plan.interior.max.z - plan.interior.min.z);
            int samples = 0, unreachable = 0;
            var cornered = new List<(Vector3 p, int open, string region)>();
            var unreachableAt = new List<Vector3>();
            for (float x = interior.xMin + Step * .5f; x < interior.xMax; x += Step)
                for (float z = interior.yMin + Step * .5f; z < interior.yMax; z += Step)
                {
                    var hit = Sample(new Vector3(x, floor, z), .2f);
                    if (hit == null) continue;
                    var p = hit.Value;
                    samples++;
                    if (!Complete(centre, p, out _)) { unreachable++; unreachableAt.Add(p); continue; }
                    int open = 0;
                    for (int d = 0; d < Directions; d++)
                    {
                        float angle = d * 2 * Mathf.PI / Directions;
                        Vector3 to = p + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Reach;
                        float distance = NavMesh.Raycast(p, to, out var edge, NavMesh.AllAreas) ? edge.distance : Reach;
                        if (distance >= Open) open++;
                    }
                    if (open < CorneredBelow) cornered.Add((p, open, Region(plan, p)));
                }
            lines.Add($"Floor scan: {samples} walkable samples at {Step} m; {unreachable} unreachable from the arena centre; {cornered.Count} cornered (< {CorneredBelow}/{Directions} directions open for {Open} m).");
            var room = NorthWestRoomFramework.FindSection(SceneManager.GetActiveScene(), NorthWestRoomFramework.Section.PressureSystems).parent;
            var hulls = room.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
            foreach (var group in cornered.GroupBy(c => c.region).OrderByDescending(g => g.Count()))
            {
                int inHull = group.Count(c => hulls.Any(h => h.bounds.Contains(c.p + Vector3.up)));
                lines.Add($"  cornered in {group.Key}: {group.Count()} samples ({inHull} centres inside solid machinery at 1 m), e.g. " +
                    string.Join("; ", group.OrderBy(c => c.open).Take(4).Select(c => $"({c.p.x:F1}, {c.p.z:F1}) {c.open}/{Directions}")));
            }
            foreach (var u in Cluster(unreachableAt)) lines.Add($"  unreachable cluster: {u.count} samples around ({u.at.x:F1}, {u.at.z:F1})");
            bool dropBays = SessionState.GetBool(Key + "DropBays", false);
            string[] repaired = { "N6", "E5", "N3", "W2" };
            foreach (var id in repaired)
            {
                var bay = plan.bays.Single(b => b.id == id);
                // Include a 0.5 m apron outside the row face, so the old slot mouth cannot
                // disappear from the assertion merely by being relabelled as other floor.
                int trapped = cornered.Count(c => c.p.x >= bay.footprint.min.x - .5f && c.p.x <= bay.footprint.max.x + .5f &&
                    c.p.z >= bay.footprint.min.z - .5f && c.p.z <= bay.footprint.max.z + .5f);
                lines.Add($"Drop bay {id} and 0.5 m apron: {trapped} cornered samples.");
                if (dropBays && trapped != 0) throw new InvalidOperationException($"Drop bay {id} still has {trapped} cornered samples.");
            }
            string text = "NORTH-WEST SIGN-OFF RUNTIME NAVIGATION PASSED (routes and loops). Cornered/unreachable floor is reported for review; this is not a live-wave or player-input test.\n" + string.Join("\n", lines);
            string output = dropBays ? "ReviewCaptures/NorthWestRoom/046/Runtime_Navigation.txt" : Output;
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, text + "\n");
            Debug.Log(text);
            SessionState.SetInt(Key + "Result", 0);
        }
        catch (Exception ex) { Debug.LogException(ex); SessionState.SetInt(Key + "Result", 1); }
        finally { CultureInfo.CurrentCulture = culture; EditorApplication.ExitPlaymode(); }
    }

    static string Region(NorthWestRoomDensity.Plan plan, Vector3 p)
    {
        bool In(NorthWestRoomDensity.Area a) => p.x >= a.min.x && p.x <= a.max.x && p.z >= a.min.z && p.z <= a.max.z;
        var bay = plan.bays.FirstOrDefault(b => In(b.footprint));
        if (bay != null) return "bay " + bay.id;
        var space = plan.openSpaces.FirstOrDefault(s => s.polygon != null && InPolygon(p, s.polygon));
        if (space != null) return "open space " + space.id;
        return "other floor";
    }

    static bool InPolygon(Vector3 p, NorthWestRoomDensity.XZ[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].z > p.z) != (poly[j].z > p.z) && p.x < (poly[j].x - poly[i].x) * (p.z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x)
                inside = !inside;
        return inside;
    }

    static IEnumerable<(int count, Vector3 at)> Cluster(List<Vector3> points)
    {
        var left = new List<Vector3>(points);
        while (left.Count > 0)
        {
            var seed = left[0]; var group = left.Where(q => Vector3.Distance(q, seed) < 3f).ToList();
            left.RemoveAll(group.Contains);
            yield return (group.Count, new Vector3(group.Average(q => q.x), 0, group.Average(q => q.z)));
        }
    }

    static bool Complete(Vector3 a, Vector3 b, out float length)
    {
        var path = new NavMeshPath(); length = 0;
        if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return true;
    }

    static Vector3? Sample(Vector3 point, float radius) =>
        NavMesh.SamplePosition(point, out var hit, radius, NavMesh.AllAreas) && Mathf.Abs(hit.position.x - point.x) < radius && Mathf.Abs(hit.position.z - point.z) < radius
            ? hit.position : (Vector3?)null;

    static string SceneHash()
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(NorthWestRoomSurvey.ScenePath))
            return BitConverter.ToString(sha.ComputeHash(stream));
    }
}
