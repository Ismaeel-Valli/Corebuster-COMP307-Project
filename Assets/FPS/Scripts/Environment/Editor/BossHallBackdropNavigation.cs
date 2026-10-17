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
/// corebuster_env_049 read-only Play-mode probe. With the cradle's carving footprint active it checks the carve, the
/// NavMesh ring around the cradle, every hall humanoid's path to the entry, the entry-to-west-stair connectors and the
/// Rifleman-to-Rifleman route. The saved player's CharacterController then walks the cradle front and both ends, is
/// pushed into the hull and tries to jump onto it. Player input/script and hall actor colliders are disabled for the
/// probe only; Play-mode changes are discarded and the saved scene hash is checked afterwards. Not a live-combat test.
/// </summary>
[InitializeOnLoad]
public static class BossHallBackdropNavigation
{
    const string Key = "CoreBuster.BossHallBackdropNavigation.";
    static bool Arms => SessionState.GetBool(Key + "Arms", false);
    static string Output => (Arms ? BossHallMechanicalArms.Output : BossHallBackdrop.Output) + "/Navigation.txt";
    const float Speed = 4f, Dt = .02f, Arrive = .3f, Gravity = 20f, JumpSpeed = 9f;
    static double enteredAt;
    static bool running;

    static BossHallBackdropNavigation()
    {
        EditorApplication.playModeStateChanged += OnState;
        EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Active", false) && EditorApplication.isPlaying) { enteredAt = EditorApplication.timeSinceStartup; running = true; }
    }

    [MenuItem("Tools/CoreBuster/Run Boss Hall Backdrop Navigation Probe")]
    public static void RunMenu() { SessionState.SetBool(Key + "Arms", false); Begin(); }
    // Run without -quit: the callback exits after Play mode ends.
    public static void RunBatch() { SessionState.SetBool(Key + "Arms", false); EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); Begin(); }

    [MenuItem("Tools/CoreBuster/Run Boss Hall Mechanical Arms Navigation Probe")]
    public static void RunMechanicalArmsMenu() { SessionState.SetBool(Key + "Arms", true); Begin(); }
    public static void RunMechanicalArmsBatch() { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); RunMechanicalArmsMenu(); }

    static void Begin()
    {
        BossHallBackdrop.Validate(BossHallFramework.MeasureLayout());
        if (Arms) BossHallMechanicalArms.Validate(BossHallFramework.MeasureLayout());
        SessionState.SetString(Key + "Scene", BossHallSurvey.Hash(BossHallSurvey.ScenePath));
        SessionState.SetBool(Key + "Active", true); SessionState.SetInt(Key + "Result", 1);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { enteredAt = EditorApplication.timeSinceStartup; running = true; }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        int result = SessionState.GetInt(Key + "Result", 1);
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != SessionState.GetString(Key + "Scene", "")) { Debug.LogError("Backdrop probe changed the saved scene."); result = 1; }
        SessionState.EraseBool(Key + "Active"); SessionState.EraseString(Key + "Scene");
        SessionState.EraseBool(Key + "Arms");
        if (Application.isBatchMode) EditorApplication.Exit(result);
    }

    static void Tick()
    {
        if (!running || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup - enteredAt < 3) return;
        running = false;
        var culture = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var contract = JsonUtility.FromJson<BossHallFramework.Contract>(File.ReadAllText(BossHallFramework.ContractPath));
            var layout = new BossHallFramework.Layout(contract); var r = layout.Report; var f = BossHallBackdrop.Measure(layout);
            var hullObject = GameObject.Find(BossHallFramework.RootPath + "/Assembly_Backdrop/Assembly_Cradle/" + BossHallBackdrop.HullName);
            var hull = hullObject ? hullObject.GetComponent<BoxCollider>() : throw new InvalidOperationException("Missing cradle hull in Play mode");
            var obstacle = hullObject.GetComponent<NavMeshObstacle>();
            var hb = hull.bounds; var lines = new List<string>(); int failures = 0;
            void Check(bool ok, string text) { lines.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) failures++; }
            if (Arms)
            {
                var section = GameObject.Find(BossHallFramework.RootPath + "/Mechanical_Arms");
                Check(section && section.GetComponentsInChildren<Collider>().Length == 0 && section.GetComponentsInChildren<NavMeshObstacle>().Length == 0,
                    "mechanical arm cells add no runtime collision or carving");
                float capsuleTop = r.tier2TopY + 1.8f;
                Check(section && section.GetComponentsInChildren<Renderer>().All(x => x.bounds.min.y > capsuleTop),
                    "every arm renderer is above a standing player on the second tier (remaining jump clearance enforced by the contract)");
                lines.Add("Art_006 probe repeats all backdrop traversal/routes with both saved arm cells present. Containment, win ordering and live combat are not tested.");
            }
            Check(obstacle && obstacle.enabled && obstacle.carving, "cradle carving obstacle is enabled and carving in Play mode");

            // 1. Carve: no NavMesh within the hull footprint.
            int inside = 0, onMesh = 0;
            for (float x = hb.min.x + .5f; x < hb.max.x; x += .5f)
                for (float z = hb.min.z + .5f; z < hb.max.z; z += .5f)
                {
                    inside++;
                    if (NavMesh.SamplePosition(new Vector3(x, r.floorTopY, z), out var hit, .2f, NavMesh.AllAreas) &&
                        hit.position.x > hb.min.x && hit.position.x < hb.max.x && hit.position.z > hb.min.z && hit.position.z < hb.max.z) onMesh++;
                }
            Check(onMesh == 0, $"carve: {onMesh}/{inside} samples inside the hull footprint are on the NavMesh");

            // 2. Ring around the cradle front and ends, 1.2 m out: on the NavMesh and reachable from the entry.
            Vector3? Nav(Vector3 p, float tolerance = .4f) => NavMesh.SamplePosition(p, out var h, tolerance, NavMesh.AllAreas) && Mathf.Abs(h.position.y - p.y) < .6f ? h.position : (Vector3?)null;
            // The trimmed contract zeroes entryInside; the survey defines it as 3 m in from the measured opening centre.
            var entryInside = r.entry.centre + r.entry.inward * 3f;
            var entry = Nav(entryInside) ?? throw new InvalidOperationException("Entry inside point is off the NavMesh");
            bool Complete(Vector3 a, Vector3 b, out NavMeshPath path) { path = new NavMeshPath(); return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete; }
            var ring = new List<(string, Vector3)>();
            for (float z = hb.min.z - 1.2f; z <= hb.max.z + 1.21f; z += 1f) ring.Add(("front", new Vector3(hb.max.x + 1.2f, r.floorTopY, z)));
            for (float x = f.wallX + 1f; x < hb.max.x + 1.2f; x += 1f) { ring.Add(("south end", new Vector3(x, r.floorTopY, hb.min.z - 1.2f))); ring.Add(("north end", new Vector3(x, r.floorTopY, hb.max.z + 1.2f))); }
            foreach (var g in ring.GroupBy(p => p.Item1))
            {
                int total = g.Count(), on = 0, reach = 0;
                foreach (var (_, p) in g) { var n = Nav(p); if (n == null) continue; on++; if (Complete(entry, n.Value, out _)) reach++; }
                Check(on == total && reach == total, $"ring {g.Key}: {on}/{total} samples on the NavMesh, {reach}/{total} reachable from the entry");
            }

            // 3. Routes: hall humanoids to the entry, entry to the west stair feet, Rifleman to Rifleman; none crosses the hull.
            var keep = hb; keep.Expand(new Vector3(.6f, 10, .6f));
            bool Crosses(NavMeshPath path)
            {
                for (int i = 1; i < path.corners.Length; i++)
                    for (int s = 0; s <= 20; s++) { var p = Vector3.Lerp(path.corners[i - 1], path.corners[i], s / 20f); if (p.x > keep.min.x && p.x < keep.max.x && p.z > keep.min.z && p.z < keep.max.z) return true; }
                return false;
            }
            float Length(NavMeshPath p) { float l = 0; for (int i = 1; i < p.corners.Length; i++) l += Vector3.Distance(p.corners[i - 1], p.corners[i]); return l; }
            void Route(string label, Vector3 a, Vector3 b)
            {
                var na = Nav(a, .6f); var nb = Nav(b, .6f);
                if (na == null || nb == null) { Check(false, label + ": endpoint off the NavMesh"); return; }
                bool ok = Complete(na.Value, nb.Value, out var path);
                Check(ok && !Crosses(path), $"{label}: {(ok ? "complete" : path.status.ToString())} {Length(path):F1} m{(Crosses(path) ? ", crosses the cradle" : "")}");
            }
            foreach (var a in r.actors.Where(a => a.kind != "turret")) Route(a.name + F($" ({a.position.x:F0}, {a.position.z:F0}) to entry inside"), a.position, entryInside);
            foreach (var s in r.stairs.Where(s => s.label.StartsWith("Lower NW") || s.label.StartsWith("Lower SW"))) Route("Entry inside to " + s.label + " foot", entryInside, s.foot.position);
            var riflemen = r.actors.Where(a => a.kind != "turret" && a.envelope.min.x < f.pocketFront + 1 && a.envelope.max.x > f.backX).OrderBy(a => a.position.z).ToArray();
            if (riflemen.Length == 2) Route("Rifleman south to Rifleman north (past the cradle front)", riflemen[0].position, riflemen[1].position);
            else Check(false, "expected two actors flanking the cradle");

            // 4. Live agents after carving.
            var hall = layout.interior; hall.Expand(new Vector3(4, 10, 4));
            var agents = Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None).Where(a => hall.Contains(a.transform.position)).ToArray();
            // Ten humanoids plus the turret, which also carries a (stationary) NavMeshAgent.
            Check(agents.Length == r.actors.Count && agents.All(a => a.isOnNavMesh), $"live hall agents on the NavMesh with carving active: {agents.Count(a => a.isOnNavMesh)}/{agents.Length} (survey actors {r.actors.Count})");

            // 5. Player capsule around and against the cradle.
            var player = Object.FindFirstObjectByType<PlayerCharacterController>(); var cc = player.GetComponent<CharacterController>();
            player.enabled = false; var input = player.GetComponent<PlayerInputHandler>(); if (input) input.enabled = false;
            var health = player.GetComponent<Health>(); if (health) health.Invincible = true;
            int disabled = 0;
            foreach (var agent in agents) foreach (var c in agent.GetComponentsInChildren<Collider>()) { c.enabled = false; disabled++; }
            Physics.SyncTransforms();
            lines.Add($"Probe setup: PlayerCharacterController and input disabled, Health invincible, {disabled} hall actor colliders disabled (Play mode only).");
            float laneX = hb.max.x + 1.2f, y = r.floorTopY;
            var sw = r.stairs.Single(s => s.label.StartsWith("Lower SW")).foot.position; var nw = r.stairs.Single(s => s.label.StartsWith("Lower NW")).foot.position;
            var front = Walk(cc, new[] { sw, new Vector3(laneX, y, hb.min.z - 1.2f), new Vector3(laneX, y, hb.max.z + 1.2f), nw }, false, out float maxY);
            Check(front == "Reached", "player walks Lower SW stair foot -> cradle front lane -> Lower NW stair foot: " + front);
            foreach (var (label, z) in new[] { ("south", hb.min.z - 1.2f), ("north", hb.max.z + 1.2f) })
            {
                var end = Walk(cc, new[] { new Vector3(laneX, y, z), new Vector3(f.wallX + 1f, y, z), new Vector3(laneX, y, z) }, false, out _);
                Check(end == "Reached", $"player walks the {label} end gap to 1 m from the west wall and back: {end}");
            }
            Teleport(cc, new Vector3(laneX, y, f.axisZ));
            for (int i = 0; i < 150; i++) { cc.Move(Vector3.left * Speed * Dt + Vector3.down * 2 * Dt); }
            float stop = cc.transform.position.x;
            Check(stop >= hb.max.x + cc.radius - .08f, F($"player pushed into the cradle front stops at X {stop:F2} (hull face {hb.max.x:F2}, radius {cc.radius:F2})"));
            Walk(cc, new[] { new Vector3(laneX, y, f.axisZ + 4), new Vector3(hb.max.x - 1.5f, y, f.axisZ + 4) }, true, out maxY);
            // Standing on the cradle would put the feet at the hull top; the jump apex must stay clearly below it.
            Check(maxY < hb.max.y - .2f, F($"player with jump assist cannot get onto the cradle: highest foot Y {maxY:F2} (apex), hull top {hb.max.y:F2}"));

            lines.Insert(0, $"BOSS HALL {(Arms ? "MECHANICAL ARMS 006" : "BACKDROP")} NAVIGATION PROBE: {(failures == 0 ? "PASS" : failures + " FAILURES")}. Physics capsule and NavMesh probe with the cradle carve active; not live combat or human input.");
            Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, string.Join("\n", lines) + "\n");
            Debug.Log(string.Join("\n", lines));
            SessionState.SetInt(Key + "Result", failures == 0 ? 0 : 1);
        }
        catch (Exception ex) { Debug.LogException(ex); SessionState.SetInt(Key + "Result", 1); }
        finally { CultureInfo.CurrentCulture = culture; EditorApplication.ExitPlaymode(); }
    }

    // Straight segments through the points, with gravity; optional jump assist when stalled.
    static string Walk(CharacterController cc, Vector3[] points, bool jumpAssist, out float maxY)
    {
        Teleport(cc, points[0]); maxY = float.MinValue; float vy = 0, stall = 0;
        foreach (var g in points.Skip(1))
        {
            float best = float.MaxValue; int sinceBest = 0;
            for (int step = 0; ; step++)
            {
                var pos = cc.transform.position; var flat = Flat(g - pos);
                if (flat.magnitude < Arrive) break;
                if (step > 4000 || sinceBest > 75) return F($"Stuck near ({pos.x:F2}, {pos.y:F2}, {pos.z:F2}) heading for ({g.x:F2}, {g.z:F2})");
                vy -= Gravity * Dt;
                if (jumpAssist && cc.isGrounded && stall > .25f) { vy = JumpSpeed; stall = 0; }
                var before = pos; cc.Move(flat.normalized * Speed * Dt + Vector3.up * vy * Dt);
                if (cc.isGrounded && vy < 0) vy = -1;
                var after = cc.transform.position; maxY = Mathf.Max(maxY, after.y);
                stall = Flat(after - before).magnitude < Speed * Dt * .3f ? stall + Dt : 0;
                float d = Flat(g - after).magnitude; if (d < best - .05f) { best = d; sinceBest = 0; } else sinceBest++;
            }
        }
        return "Reached";
    }

    static void Teleport(CharacterController cc, Vector3 p) { cc.enabled = false; cc.transform.position = p + Vector3.up * .05f; cc.enabled = true; Physics.SyncTransforms(); }
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    static string F(FormattableString s) => FormattableString.Invariant(s);
}
