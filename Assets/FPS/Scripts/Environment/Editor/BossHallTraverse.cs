using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>
/// corebuster_env_047 read-only Play-mode traverse. The saved player's own CharacterController walks every intended
/// leg of the Boss Hall (incline, lower floor, all stairs, both galleries, the upper network and both bay-door
/// approaches) with gravity and no jumping, steered by runtime NavMesh corners where a path exists. A stair leg that
/// cannot be walked is retried with jump assist. A crouched and a standing capsule are pushed under each wall.
/// Player input, the player script and hall actor colliders are disabled for the probe only; Play-mode changes are
/// discarded and the saved scene hash is checked afterwards. This is not a human input or live-combat test.
/// </summary>
[InitializeOnLoad]
public static class BossHallTraverse
{
    const string Key = "CoreBuster.BossHallTraverse.";
    static string Output => SessionState.GetString(Key + "Output", BossHallSurvey.Output + "/Traverse.txt");
    static string Measurement => SessionState.GetString(Key + "Measurement", BossHallSurvey.Output + "/Measurement.json");
    const float Speed = 4f, Dt = .02f, Arrive = .3f, Gravity = 20f, JumpSpeed = 9f;
    static double enteredAt;
    static bool running;

    static readonly string[][] Legs =
    {
        new[] { "North-West threshold", "Incline landing" }, new[] { "Incline landing", "Entry inside" },
        new[] { "Entry inside", "Floor NE" }, new[] { "Floor NE", "Floor NW" }, new[] { "Floor NW", "Floor SW" }, new[] { "Floor SW", "Floor SE" },
        new[] { "Floor SE", "Lower SE stair foot" }, new[] { "Lower SE stair foot", "Lower SE stair top" },
        new[] { "Lower SE stair top", "Lower SW stair top" }, new[] { "Lower SW stair top", "Lower SW stair foot" },
        new[] { "Lower SW stair foot", "Lower NW stair foot" }, new[] { "Lower NW stair foot", "Lower NW stair top" },
        new[] { "Lower NW stair top", "Lower NE stair top" }, new[] { "Lower NE stair top", "Lower NE stair foot" },
        new[] { "Lower NE stair foot", "Lower SE stair foot" }, new[] { "Lower SE stair foot", "Lower SE stair top" },
        new[] { "Lower SE stair top", "Upper SE stair foot" }, new[] { "Upper SE stair foot", "Upper SE stair top" },
        new[] { "Upper SE stair top", "Upper spine south" }, new[] { "Upper spine south", "East bay-door approach" },
        new[] { "East bay-door approach", "West bay-door approach" }, new[] { "West bay-door approach", "Upper spine north" },
        new[] { "Upper spine north", "Upper NE stair top" }, new[] { "Upper NE stair top", "Upper NE stair foot" },
        new[] { "Upper NE stair foot", "Upper NE stair lane east", "Upper NE stair lane west", "Upper NW stair lane east", "Upper NW stair lane west", "Upper NW stair foot" }, new[] { "Upper NW stair foot", "Upper NW stair top" },
        new[] { "Upper NW stair top", "Upper SW stair top" }, new[] { "Upper SW stair top", "Upper SW stair foot" },
        new[] { "Upper SW stair foot", "Upper SW stair lane west", "Upper SW stair lane east", "Upper SE stair lane west", "Upper SE stair lane east", "Upper SE stair foot" },
        new[] { "Upper SE stair foot", "Lower SE stair top" }, new[] { "Lower SE stair top", "Lower SE stair foot" },
        new[] { "Lower SE stair foot", "Entry inside" }, new[] { "Entry inside", "Incline landing" }, new[] { "Incline landing", "North-West threshold" },
    };

    static BossHallTraverse()
    {
        EditorApplication.playModeStateChanged += OnState;
        EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Active", false) && EditorApplication.isPlaying)
        {
            enteredAt = EditorApplication.timeSinceStartup;
            running = true;
        }
    }

    [MenuItem("Tools/CoreBuster/Run Boss Hall Traverse")]
    public static void RunMenu() => Begin();

    // Run without -quit: the callback exits after Play mode and domain reload finish.
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        Begin();
    }

    /// <summary>Read-only env_051 prerequisite evidence; keeps env_047's published survey/traverse files intact.</summary>
    public static void RunCatwalkBaselineBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        BossHallFramework.MeasureLayout();
        Begin(true);
    }

    /// <summary>Read-only saved env_051 regression probe; preserves both earlier traverse reports.</summary>
    public static void RunCatwalkSavedBatch()
    {
        EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
        BossHallCatwalk.Validate(BossHallFramework.MeasureLayout());
        Begin(false, true);
    }

    static void Begin(bool catwalkBaseline = false, bool catwalkSaved = false)
    {
        SessionState.SetBool(Key + "CatwalkSaved", catwalkSaved);
        if (catwalkBaseline || catwalkSaved)
        {
            Directory.CreateDirectory(BossHallCatwalk.Output);
            string path = BossHallCatwalk.Output + (catwalkSaved ? "/SavedMeasurement.json" : "/BaselineMeasurement.json");
            var measured = BossHallSurvey.Measure();
            if (catwalkSaved) CompareCatwalkGeometry(measured);
            File.WriteAllText(path, JsonUtility.ToJson(measured, true));
            SessionState.SetString(Key + "Measurement", path);
            SessionState.SetString(Key + "Output", catwalkSaved ? "Docs/BossHall/CatwalkTraverseSaved_051.txt" : "Docs/BossHall/CatwalkTraverseBaseline_051.txt");
        }
        else
        {
            SessionState.EraseString(Key + "Measurement"); SessionState.EraseString(Key + "Output");
            BossHallSurvey.MeasureMenu(); // Writes the measured key points this probe walks.
        }
        SessionState.SetString(Key + "Scene", BossHallSurvey.Hash(BossHallSurvey.ScenePath));
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
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != SessionState.GetString(Key + "Scene", ""))
        {
            Debug.LogError("Boss Hall traverse changed the saved scene.");
            result = 1;
        }
        SessionState.EraseBool(Key + "Active");
        SessionState.EraseBool(Key + "CatwalkSaved");
        SessionState.EraseString(Key + "Scene");
        SessionState.EraseString(Key + "Measurement"); SessionState.EraseString(Key + "Output");
        if (Application.isBatchMode) EditorApplication.Exit(result);
    }

    class Leg
    {
        public string from, to, status, guide;
        public float planned, walked, seconds, minY, maxY;
        public Vector3 end;
        public bool jumped;
    }

    static void Tick()
    {
        if (!running || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup - enteredAt < 3) return;
        running = false;
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var report = JsonUtility.FromJson<BossHallSurvey.Report>(File.ReadAllText(Measurement));
            var keys = report.keyPoints.ToDictionary(k => k.name, k => k.position);
            var player = Object.FindFirstObjectByType<PlayerCharacterController>();
            var cc = player.GetComponent<CharacterController>();
            player.enabled = false;
            var input = player.GetComponent<PlayerInputHandler>(); if (input) input.enabled = false;
            var health = player.GetComponent<Health>(); if (health) health.Invincible = true;
            var hall = report.interior.ToBounds(); hall.Expand(new Vector3(4, 10, 4));
            int disabled = 0;
            foreach (var agent in Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None).Where(a => hall.Contains(a.transform.position)))
                foreach (var c in agent.GetComponentsInChildren<Collider>()) { c.enabled = false; disabled++; }
            Physics.SyncTransforms();
            var lines = new List<string>
            {
                F($"Player CharacterController: radius {cc.radius:F2}, height {cc.height:F2}, centre {cc.center.y:F2}, step offset {cc.stepOffset:F2}, slope limit {cc.slopeLimit:F0}; speed {Speed} m/s, gravity {Gravity} m/s2, no jumping unless stated."),
                $"Disabled for the probe only: PlayerCharacterController, PlayerInputHandler, {disabled} hall actor colliders; player Health invincible.",
            };
            if (SessionState.GetBool(Key + "CatwalkSaved", false)) CheckCatwalkNavigation(report, hall, lines);
            var legs = new List<Leg>();
            foreach (var pair in Legs)
            {
                var missing = pair.Where(n => !keys.ContainsKey(n)).ToArray();
                if (missing.Length > 0) { lines.Add($"SKIPPED {string.Join(" -> ", pair)}: key point(s) missing from the survey: {string.Join(", ", missing)}."); continue; }
                var points = pair.Select(n => keys[n]).ToArray();
                var leg = Walk(cc, points, false);
                leg.from = pair[0]; leg.to = string.Join(" -> ", pair.Skip(1));
                if (leg.status != "Reached" && pair.Length == 2 && pair[0].Contains("stair") && pair[1].Contains("stair"))
                {
                    var assisted = Walk(cc, points, true);
                    leg.status += "; with jump assist: " + assisted.status + F($" ({assisted.seconds:F1} s)");
                    leg.jumped = assisted.status == "Reached";
                }
                if (!leg.status.StartsWith("Reached")) Teleport(cc, points[points.Length - 1]); // Continue the remaining legs from the intended end.
                legs.Add(leg);
            }
            // Stair matrix: every stair walked up and down on its own, independent of the route order above.
            var stairLines = new List<string>();
            int stairsUp = 0, stairsUpAssisted = 0, stairsDown = 0;
            foreach (var st in report.stairs.OrderBy(t => t.label))
            {
                var up = Walk(cc, new[] { st.foot.position, st.top.position }, false);
                string upText = up.status;
                if (up.status == "Reached") stairsUp++;
                else
                {
                    var assisted = Walk(cc, new[] { st.foot.position, st.top.position }, true);
                    upText += "; with jump assist: " + assisted.status;
                    if (assisted.status == "Reached") stairsUpAssisted++;
                }
                var down = Walk(cc, new[] { st.top.position, st.foot.position }, false);
                if (down.status == "Reached") stairsDown++;
                stairLines.Add(F($"STAIR {st.label} ({st.name}, riser {st.maxRiser:F3} m max): up {(up.status == "Reached" ? "PASS" : "FAIL")} ({upText}, {up.seconds:F1} s); down {(down.status == "Reached" ? "PASS" : "FAIL")} ({down.status}, {down.seconds:F1} s)"));
            }
            stairLines.Insert(0, $"Stair matrix: {stairsUp}/{report.stairs.Count} climbed by walking, {stairsUpAssisted} more only with jump assist; {stairsDown}/{report.stairs.Count} descended by walking.");

            foreach (var leg in legs)
                lines.Add(F($"{(leg.status.StartsWith("Reached") ? "PASS" : "FAIL")} {leg.from} -> {leg.to}: {leg.status}; planned {leg.planned:F1} m ({leg.guide}), walked {leg.walked:F1} m in {leg.seconds:F1} s, Y {leg.minY:F2}..{leg.maxY:F2}, end ({leg.end.x:F2}, {leg.end.y:F2}, {leg.end.z:F2})"));

            lines.AddRange(stairLines);

            // Under-wall probes: crouched and standing capsules pushed outward from 1 m inside each wall plane.
            float crouch = player.CapsuleHeightCrouching, stand = cc.height;
            var room = report.interior.ToBounds();
            var probes = new[]
            {
                ("West", new Vector3(room.min.x + 1, report.floorTopY, 100f), Vector3.left, room.min.x),
                ("East", new Vector3(room.max.x - 1, report.floorTopY, 100f), Vector3.right, room.max.x),
                ("South", new Vector3(-300f, report.floorTopY, room.min.z + 1), Vector3.back, room.min.z),
                ("North", new Vector3(-300f, report.floorTopY, room.max.z - 1), Vector3.forward, room.max.z),
            };
            foreach (var (wall, start, dir, plane) in probes)
                foreach (var (label, height) in new[] { ("crouched", crouch), ("standing", stand) })
                {
                    cc.height = height; cc.center = Vector3.up * height * .5f;
                    Teleport(cc, start);
                    float vy = 0;
                    for (int i = 0; i < 150; i++)
                    {
                        vy -= Gravity * Dt;
                        cc.Move(dir * Speed * Dt + Vector3.up * vy * Dt);
                        if (cc.isGrounded) vy = -1;
                    }
                    var p = cc.transform.position;
                    float beyond = Vector3.Dot(p - new Vector3(dir.x != 0 ? plane : p.x, p.y, dir.z != 0 ? plane : p.z), dir);
                    lines.Add(F($"{(beyond > .4f ? "PASSES" : "BLOCKED")} {wall} wall, {label} capsule {height:F2} m: ends {beyond:+0.00;-0.00} m beyond the wall plane at ({p.x:F2}, {p.y:F2}, {p.z:F2})"));
                }
            cc.height = stand; cc.center = Vector3.up * stand * .5f;

            int pass = legs.Count(l => l.status.StartsWith("Reached")), assistedPass = legs.Count(l => l.jumped);
            string head = $"BOSS HALL TRAVERSE RAN: {pass}/{legs.Count} legs walked without jumping; {assistedPass} more reached only with jump assist; {legs.Count - pass - assistedPass} not reached. This is a physics capsule probe, not a human player-input or live-combat test.";
            lines.Insert(0, head);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, string.Join("\n", lines) + "\n");
            Debug.Log(string.Join("\n", lines));
            if (SessionState.GetBool(Key + "CatwalkSaved", false))
            {
                var baseline = File.ReadAllLines("Docs/BossHall/CatwalkTraverseBaseline_051.txt");
                var previousLegs = baseline.Where(l => l.StartsWith("PASS ") || l.StartsWith("FAIL ")).Where(l => !l.Contains("STAIR "))
                    .Select(l => l.Split(':')[0]).ToArray();
                var currentLegs = lines.Where(l => l.StartsWith("PASS ") || l.StartsWith("FAIL ")).Where(l => !l.Contains("STAIR "))
                    .Select(l => l.Split(':')[0]).ToArray();
                if (!previousLegs.SequenceEqual(currentLegs) || legs.Count != Legs.Length || pass + assistedPass != legs.Count ||
                    baseline.First(l => l.StartsWith("Stair matrix:")) != stairLines[0])
                    throw new InvalidOperationException("Saved maintenance changed baseline route/stair dispositions or left an intended leg unreached.");
                File.AppendAllText(Output, "PASS saved/baseline route and stair dispositions match; every intended leg reached, including the original jump-assisted upper stairs.\n");
            }
            SessionState.SetInt(Key + "Result", 0);
        }
        catch (Exception ex) { Debug.LogException(ex); SessionState.SetInt(Key + "Result", 1); }
        finally { CultureInfo.CurrentCulture = culture; EditorApplication.ExitPlaymode(); }
    }

    static void CompareCatwalkGeometry(BossHallSurvey.Report saved)
    {
        var baseline = JsonUtility.FromJson<BossHallSurvey.Report>(File.ReadAllText("Docs/BossHall/CatwalkBaselineGeometry_051.json"));
        if (baseline.sceneSha256 != "398f28174fd740665d66bb1a4cf2e0cd0b5898052d10c4d35c5b1495844803b1")
            throw new InvalidOperationException("Unexpected pre-placement catwalk baseline.");
        var lines = new List<string>();
        void Same<T>(string label, IEnumerable<T> before, IEnumerable<T> after)
        {
            var a = before.Select(x => JsonUtility.ToJson(x)).ToArray();
            var b = after.Select(x => JsonUtility.ToJson(x)).ToArray();
            if (a.Length == 0 || !a.SequenceEqual(b)) throw new InvalidOperationException("Saved catwalk regression: " + label);
            lines.Add($"PASS {label}: {a.Length} complete measured records identical before/after.");
        }
        Same("decks, usable widths, headroom and joins", baseline.decks, saved.decks);
        Same("stair/landing geometry and clearance", baseline.stairs, saved.stairs);
        Same("original rail collider bounds and eye-height crossings", baseline.rails, saved.rails);
        Same("rail-opening level/downward firing tests and blockers", baseline.railTests, saved.railTests);
        if (baseline.turretRevealDistance != saved.turretRevealDistance || baseline.entry.width != saved.entry.width ||
            !baseline.incline.Select(x => x.turretVisible).SequenceEqual(saved.incline.Select(x => x.turretVisible)))
            throw new InvalidOperationException("Saved catwalk changed the entry/turret reveal.");
        lines.Add("PASS entry width, incline turret visibility and first reveal unchanged.");
        foreach (var deck in saved.decks)
            lines.Add(F($"{deck.name}: clear width minimum {deck.clearWidthMin:F2}, median {deck.clearWidthMedian:F2} m; minimum headroom {deck.minHeadroom:F2} m."));
        lines.Insert(0, "BOSS HALL CATWALK 051 SAVED GEOMETRY: PASS. Original upper stair/rail defects retained under the approved first-tier scope.");
        lines.Add("Baseline scene SHA-256: " + baseline.sceneSha256);
        lines.Add("Saved scene SHA-256: " + saved.sceneSha256);
        File.WriteAllText("Docs/BossHall/CatwalkGeometry_051.txt", string.Join("\n", lines) + "\n");
    }

    static void CheckCatwalkNavigation(BossHallSurvey.Report report, Bounds hall, List<string> lines)
    {
        Vector3? Nav(Vector3 p) => NavMesh.SamplePosition(p, out var h, .6f, NavMesh.AllAreas) && Mathf.Abs(h.position.y - p.y) < .6f ? h.position : (Vector3?)null;
        void Route(string label, Vector3 a, Vector3 b)
        {
            var na = Nav(a); var nb = Nav(b); var path = new NavMeshPath();
            if (na == null || nb == null || !NavMesh.CalculatePath(na.Value, nb.Value, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException("Saved catwalk navigation: " + label + " has no complete runtime path.");
            lines.Add("NAV PASS " + label + ": complete runtime path with existing carving active.");
        }
        var humanoids = report.actors.Where(a => a.kind != "turret").ToArray();
        var connectors = report.routes.Where(r => r.connector && r.corners.Length > 1).ToArray();
        if (humanoids.Length != 10 || connectors.Length != 23) throw new InvalidOperationException("Unexpected saved hall actor/connector inventory.");
        foreach (var a in humanoids) Route(a.name + " to entry", a.position, report.entryInside);
        foreach (var r in connectors) Route("Connector " + r.name, r.corners[0], r.corners[r.corners.Length - 1]);
        var agents = Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None).Where(a => hall.Contains(a.transform.position)).ToArray();
        if (agents.Length != 11 || agents.Any(a => !a.isOnNavMesh)) throw new InvalidOperationException("Saved hall agents are off the runtime NavMesh.");
        lines.Add("NAV PASS 10 humanoid-to-entry paths, 23 connectors and 11/11 live agents on the runtime NavMesh.");
    }

    // Two points: steer by runtime NavMesh corners where a complete path exists. More points: straight segments through them.
    static Leg Walk(CharacterController cc, Vector3[] points, bool jumpAssist)
    {
        var leg = new Leg { minY = float.MaxValue, maxY = float.MinValue };
        Vector3 from = points[0], to = points[points.Length - 1];
        Teleport(cc, from);
        var guide = new List<Vector3>();
        var path = new NavMeshPath();
        if (points.Length > 2) { guide.AddRange(points.Skip(1)); leg.guide = "straight segments through measured gallery-lane points"; }
        else if (NavMesh.SamplePosition(from, out var a, .8f, NavMesh.AllAreas) && NavMesh.SamplePosition(to, out var b, .8f, NavMesh.AllAreas) &&
            NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
        {
            guide.AddRange(path.corners.Skip(1));
            if (guide.Count == 0) guide.Add(to); else guide[guide.Count - 1] = to;
            leg.guide = "runtime NavMesh corners";
        }
        else { guide.Add(to); leg.guide = "straight line, no complete runtime NavMesh path"; }
        var all = new[] { from }.Concat(guide).ToArray();
        for (int i = 1; i < all.Length; i++) leg.planned += Flat(all[i] - all[i - 1]).magnitude;
        float floorLimit = points.Min(q => q.y) - 1f, vy = 0, stall = 0;
        leg.status = "Reached";
        foreach (var g in guide)
        {
            int budget = Mathf.CeilToInt(Flat(g - cc.transform.position).magnitude / Speed / Dt * 3) + 250;
            float best = float.MaxValue; int sinceBest = 0;
            for (int step = 0; ; step++)
            {
                var pos = cc.transform.position;
                var flat = Flat(g - pos);
                if (flat.magnitude < Arrive) break;
                if (step > budget || sinceBest > 75) { leg.status = F($"Stuck near ({pos.x:F2}, {pos.y:F2}, {pos.z:F2}) heading for ({g.x:F2}, {g.y:F2}, {g.z:F2})"); goto done; }
                vy -= Gravity * Dt;
                bool grounded = cc.isGrounded;
                if (jumpAssist && grounded && stall > .25f) { vy = JumpSpeed; stall = 0; }
                var before = pos;
                cc.Move(flat.normalized * Speed * Dt + Vector3.up * vy * Dt);
                if (cc.isGrounded && vy < 0) vy = -1;
                var after = cc.transform.position;
                float moved = Flat(after - before).magnitude;
                stall = moved < Speed * Dt * .3f ? stall + Dt : 0;
                leg.walked += moved; leg.seconds += Dt;
                leg.minY = Mathf.Min(leg.minY, after.y); leg.maxY = Mathf.Max(leg.maxY, after.y);
                if (after.y < floorLimit) { leg.status = F($"Fell to Y {after.y:F2} near ({after.x:F2}, {after.z:F2})"); goto done; }
                float d = Flat(g - after).magnitude;
                if (d < best - .05f) { best = d; sinceBest = 0; } else sinceBest++;
            }
        }
        if (Mathf.Abs(cc.transform.position.y - to.y) > .6f) leg.status = F($"Arrived in plan at the wrong level (Y {cc.transform.position.y:F2}, expected {to.y:F2})");
        done:
        leg.end = cc.transform.position;
        return leg;
    }

    static void Teleport(CharacterController cc, Vector3 p)
    {
        cc.enabled = false;
        cc.transform.position = p + Vector3.up * .05f;
        cc.enabled = true;
        Physics.SyncTransforms();
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    static string F(FormattableString s) => FormattableString.Invariant(s);
}
