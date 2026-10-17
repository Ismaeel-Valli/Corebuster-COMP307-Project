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
/// corebuster_env_052 Play-mode route probe, run once on the saved scene before the housings exist (Baseline) and once after
/// (Saved), against the same measured housing footprints. It records NavMesh routes for every hall humanoid and connector, then
/// drives each live humanoid NavMeshAgent along representative cover routes (to the front of its nearer housing, around the far
/// end, and back to its start) with its AI scripts paused, and finally walks the saved player's CharacterController around the
/// lower floor, along and diagonally across each housing face at sprint speed, into each housing, onto it and behind it.
/// Saved results are compared with the baseline. Play-mode changes are discarded and the saved scene hash is checked afterwards.
/// Controlled agent/capsule probe: not live combat, AI decision-making or human input.
/// </summary>
[InitializeOnLoad]
public static class BossHallLowerArenaNavigation
{
    const string Key = "CoreBuster.BossHallLowerArenaNavigation.";
    const string BaselineJson = "Docs/BossHall/LowerArenaRoutesBaseline_052.json";
    const float Dt = .02f, Arrive = .3f, Gravity = 20f, AgentTimeScale = 4f;
    static double enteredAt;
    static bool running;
    static IEnumerator<bool> routine;

    [Serializable] sealed class Footprint { public string name; public int side; public Vector3 min, max; public float wallFace; }
    [Serializable] sealed class PlanData { public List<Footprint> housings = new List<Footprint>(); }
    [Serializable] sealed class Entry { public string label; public bool ok; public float length, time; }
    [Serializable] sealed class Results { public string mode, scene; public List<Entry> routes = new List<Entry>(), legs = new List<Entry>(); }

    static BossHallLowerArenaNavigation()
    {
        EditorApplication.playModeStateChanged += OnState;
        EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Active", false) || SessionState.GetString(Key + "Pending", "") != "")
            Debug.Log($"Lower arena route probe after domain load: active {SessionState.GetBool(Key + "Active", false)}, pending '{SessionState.GetString(Key + "Pending", "")}', playing {EditorApplication.isPlaying}");
        if (SessionState.GetBool(Key + "Active", false)) EditorApplication.delayCall += Resume;
        // A reload while waiting restarts the quiet period.
        if (!string.IsNullOrEmpty(SessionState.GetString(Key + "Pending", ""))) Defer(SessionState.GetString(Key + "Pending", ""));
    }

    // A domain reload during Play mode (e.g. a package forcing a refresh) drops the running probe. Resume only a probe that had
    // not started; otherwise fail and leave Play mode rather than hang or report a half-repeated run.
    static void Resume()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (EditorApplication.isPlaying && !SessionState.GetBool(Key + "Started", false)) { enteredAt = EditorApplication.timeSinceStartup; running = true; return; }
        Debug.LogError("Lower arena route probe interrupted by a domain reload; rerun it (batch: -nographics).");
        SessionState.SetInt(Key + "Result", 1);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        else if (!EditorApplication.isPlayingOrWillChangePlaymode) { SessionState.EraseBool(Key + "Active"); if (Application.isBatchMode) EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/CoreBuster/Run Boss Hall Lower Arena Route Probe")]
    public static void RunMenu() => Begin(BossHallFramework.FindSection(BossHallFramework.Section.LowerArenaDressing).childCount == 0 ? "Baseline" : "Saved");
    // Run without -quit: the callback exits after Play mode ends. Batch start-up can force a late domain reload (licence
    // entitlement resolution, then asset indexing), so Play mode waits until the editor has been reload-free for a while.
    public static void RunBaselineBatch() => Defer("Baseline");
    public static void RunSavedBatch() => Defer("Saved");
    const double QuietSeconds = 45;
    static double quietSince;
    static void Defer(string mode)
    {
        SessionState.SetString(Key + "Pending", mode); quietSince = EditorApplication.timeSinceStartup;
        EditorApplication.update -= WaitForQuiet; EditorApplication.update += WaitForQuiet;
    }
    static void WaitForQuiet()
    {
        if (EditorApplication.timeSinceStartup - quietSince < QuietSeconds || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= WaitForQuiet;
        string mode = SessionState.GetString(Key + "Pending", ""); SessionState.EraseString(Key + "Pending");
        try { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); Begin(mode); }
        catch (Exception ex) { Debug.LogException(ex); if (Application.isBatchMode) EditorApplication.Exit(1); }
    }

    static void Begin(string mode)
    {
        var layout = BossHallFramework.MeasureLayout();
        if (mode == "Baseline")
        {
            if (BossHallFramework.FindSection(BossHallFramework.Section.LowerArenaDressing).childCount != 0) throw new InvalidOperationException("Baseline probe expects no housings.");
            BossHallServices.Validate(layout);
        }
        else
        {
            BossHallLowerArena.Validate(layout);
            if (!File.Exists(BaselineJson)) throw new InvalidOperationException("Record the baseline route probe before the saved probe.");
        }
        var data = new PlanData();
        foreach (var p in BossHallLowerArena.Plan(layout)) data.housings.Add(new Footprint { name = p.name, side = p.side, min = p.hull.min, max = p.hull.max, wallFace = p.wallFace });
        SessionState.SetString(Key + "Plan", JsonUtility.ToJson(data)); SessionState.SetString(Key + "Mode", mode);
        SessionState.SetString(Key + "Scene", BossHallSurvey.Hash(BossHallSurvey.ScenePath));
        SessionState.SetBool(Key + "Active", true); SessionState.EraseBool(Key + "Started"); SessionState.SetInt(Key + "Result", 1);
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { enteredAt = EditorApplication.timeSinceStartup; running = true; }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        int result = SessionState.GetInt(Key + "Result", 1);
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != SessionState.GetString(Key + "Scene", "")) { Debug.LogError("Lower arena probe changed the saved scene."); result = 1; }
        SessionState.EraseBool(Key + "Active"); SessionState.EraseBool(Key + "Started"); SessionState.EraseString(Key + "Scene"); SessionState.EraseString(Key + "Plan"); SessionState.EraseString(Key + "Mode");
        if (Application.isBatchMode) EditorApplication.Exit(result);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        // Headless Play mode: InGameMenuManager.Update throws without a mouse (known headless exception), and Error Pause
        // then freezes frames. Live agents need frames, so the probe disables that menu and keeps Play mode running.
        if (SessionState.GetBool(Key + "Active", false))
        {
            foreach (var m in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (m.enabled && m.GetType().Name == "InGameMenuManager") { m.enabled = false; Debug.Log("LOWER ARENA PROBE: headless InGameMenuManager disabled (Play mode only)"); }
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        }
        if (routine == null)
        {
            if (!running || EditorApplication.timeSinceStartup - enteredAt < 3) return;
            running = false; routine = Probe(); SessionState.SetBool(Key + "Started", true);
            Debug.Log("LOWER ARENA PROBE starting in Play mode");
        }
        bool more;
        try { more = routine.MoveNext(); }
        catch (Exception ex) { Debug.LogException(ex); SessionState.SetInt(Key + "Result", 1); more = false; }
        if (!more) { routine = null; Time.timeScale = 1; EditorApplication.ExitPlaymode(); }
    }

    static IEnumerator<bool> Probe()
    {
        var culture = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = SessionState.GetString(Key + "Mode", ""); bool saved = mode == "Saved";
        var contract = JsonUtility.FromJson<BossHallFramework.Contract>(File.ReadAllText(BossHallFramework.ContractPath));
        var layout = new BossHallFramework.Layout(contract); var r = layout.Report; var room = layout.interior;
        var housings = JsonUtility.FromJson<PlanData>(SessionState.GetString(Key + "Plan", "")).housings;
        Bounds Hull(Footprint h) { var b = new Bounds(); b.SetMinMax(h.min, h.max); return b; }
        float Inward(Footprint h) => h.side == 0 ? 1 : -1;
        float Front(Footprint h) => h.side == 0 ? h.max.z : h.min.z;
        var lines = new List<string>(); int failures = 0; var results = new Results { mode = mode, scene = SessionState.GetString(Key + "Scene", "") };
        void Check(bool ok, string text) { lines.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) failures++; }
        void Info(string text) => lines.Add("INFO " + text);

        // 1. Carves and the NavMesh lanes in front of and beside each footprint.
        var section = GameObject.Find(BossHallFramework.RootPath + "/Lower_Arena_Dressing").transform;
        var obstacles = section.GetComponentsInChildren<NavMeshObstacle>();
        if (saved) Check(obstacles.Length == 2 && obstacles.All(o => o.enabled && o.carving), $"{obstacles.Count(o => o.enabled && o.carving)}/2 housing carving obstacles enabled and carving in Play mode");
        else Check(section.childCount == 0, "baseline: no housing exists; footprints are the measured plan");
        Vector3? Nav(Vector3 p, float tolerance = .4f) => NavMesh.SamplePosition(p, out var h, tolerance, NavMesh.AllAreas) && Mathf.Abs(h.position.y - p.y) < .6f ? h.position : (Vector3?)null;
        var entryInside = r.entry.centre + r.entry.inward * 3f;
        var entry = Nav(entryInside) ?? throw new InvalidOperationException("Entry inside point is off the NavMesh");
        bool Complete(Vector3 a, Vector3 b, out NavMeshPath path) { path = new NavMeshPath(); return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete; }
        foreach (var h in housings)
        {
            var hb = Hull(h); int inside = 0, onMesh = 0;
            for (float x = hb.min.x + .25f; x < hb.max.x; x += .5f)
                for (float z = hb.min.z + .25f; z < hb.max.z; z += .5f)
                {
                    inside++;
                    if (NavMesh.SamplePosition(new Vector3(x, r.floorTopY, z), out var hit, .2f, NavMesh.AllAreas) &&
                        hit.position.x > hb.min.x && hit.position.x < hb.max.x && hit.position.z > hb.min.z && hit.position.z < hb.max.z) onMesh++;
                }
            if (saved) Check(onMesh == 0, $"{h.name} carve: {onMesh}/{inside} samples inside the footprint are on the NavMesh");
            else Info($"{h.name} footprint before placement: {onMesh}/{inside} samples on the baked NavMesh");
            var ring = new List<Vector3>();
            for (float x = hb.min.x - 1.2f; x <= hb.max.x + 1.21f; x += 1f) ring.Add(new Vector3(x, r.floorTopY, Front(h) + Inward(h) * 1.2f));
            foreach (float x in new[] { hb.min.x - 1.2f, hb.max.x + 1.2f }) ring.Add(new Vector3(x, r.floorTopY, hb.center.z));
            int on = 0, reach = 0;
            foreach (var p in ring) { var n = Nav(p); if (n == null) continue; on++; if (Complete(entry, n.Value, out _)) reach++; }
            Check(on == ring.Count && reach == ring.Count, $"{h.name} lane: {on}/{ring.Count} samples 1.2 m in front of and beside the footprint on the NavMesh, {reach}/{ring.Count} reachable from the entry");
        }

        // 2. NavMesh routes: humanoids to the entry, connectors, and every humanoid to the front and both ends of each housing.
        var keeps = housings.Select(h => { var b = Hull(h); b.Expand(new Vector3(.6f, 20, .6f)); return b; }).ToArray();
        bool Crosses(NavMeshPath path)
        {
            for (int i = 1; i < path.corners.Length; i++)
                for (int s = 0; s <= 20; s++)
                { var p = Vector3.Lerp(path.corners[i - 1], path.corners[i], s / 20f); if (keeps.Any(k => p.x > k.min.x && p.x < k.max.x && p.z > k.min.z && p.z < k.max.z && p.y < r.floorTopY + 1)) return true; }
            return false;
        }
        float Length(NavMeshPath p) { float l = 0; for (int i = 1; i < p.corners.Length; i++) l += Vector3.Distance(p.corners[i - 1], p.corners[i]); return l; }
        void Route(string label, Vector3 a, Vector3 b)
        {
            var na = Nav(a, .6f); var nb = Nav(b, .6f);
            if (na == null || nb == null) { Check(false, label + ": endpoint off the NavMesh"); results.routes.Add(new Entry { label = label }); return; }
            bool ok = Complete(na.Value, nb.Value, out var path); bool crosses = saved && Crosses(path);
            Check(ok && !crosses, $"{label}: {(ok ? "complete" : path.status.ToString())} {Length(path):F1} m{(crosses ? ", crosses a housing" : "")}");
            results.routes.Add(new Entry { label = label, ok = ok && !crosses, length = Length(path) });
        }
        Vector3 FrontTarget(Footprint h) => new Vector3(Hull(h).center.x, r.floorTopY, Front(h) + Inward(h) * 1.2f);
        Vector3 EndTarget(Footprint h, int end) => new Vector3(end < 0 ? h.min.x - 1.2f : h.max.x + 1.2f, r.floorTopY, h.wallFace + Inward(h) * 1.2f);
        var humanoids = r.actors.Where(a => a.kind != "turret").ToArray();
        foreach (var a in humanoids) Route(a.name + F($" ({a.position.x:F0}, {a.position.z:F0}) to entry inside"), a.position, entryInside);
        foreach (var route in r.routes.Where(x => x.connector && x.corners.Length > 1)) Route("Connector " + route.name, route.corners[0], route.corners[route.corners.Length - 1]);
        foreach (var a in humanoids)
            foreach (var h in housings)
            {
                string tag = a.name + F($" ({a.position.x:F0}, {a.position.z:F0})");
                Route(tag + " to " + h.name + " front", a.position, FrontTarget(h));
                Route(tag + " to " + h.name + " west end", a.position, EndTarget(h, -1));
                Route(tag + " to " + h.name + " east end", a.position, EndTarget(h, 1));
            }

        // 3. Live agents with carving active.
        var hall = room; hall.Expand(new Vector3(4, 10, 4));
        var agents = Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None).Where(x => hall.Contains(x.transform.position) || Vector3.Distance(x.transform.position, r.entry.centre) < 20).ToArray();
        Check(agents.Length == r.actors.Count && agents.All(x => x.isOnNavMesh), $"live hall agents on the NavMesh: {agents.Count(x => x.isOnNavMesh)}/{agents.Length} (survey actors {r.actors.Count})");

        // 4. Drive each live humanoid: to the front of its nearer housing, around the far end, and back to its start.
        // Agents are matched by name, then nearest start: Robot1ChaseController pursues the player even undetected, so Robot1s
        // may leave their starts during the settle. That displacement is recorded as an observation, then each agent is warped back.
        int paused = 0; var driven = new List<(string name, NavMeshAgent agent, Vector3 start)>(); var used = new HashSet<NavMeshAgent>();
        float settle = (float)(EditorApplication.timeSinceStartup - enteredAt);
        foreach (var a in humanoids)
        {
            var agent = agents.Where(x => !used.Contains(x) && x.name == a.name).OrderBy(x => Vector3.Distance(x.transform.position, a.position)).FirstOrDefault();
            if (!agent) { Check(false, a.name + ": no live agent of that name in the hall"); continue; }
            used.Add(agent);
            string name = a.name + F($" ({a.position.x:F0}, {a.position.z:F0})");
            Info(F($"{name}: {Vector3.Distance(Flat(agent.transform.position), Flat(a.position)):F2} m from its saved start after the {settle:F1} s settle with unmodified AI (now at {agent.transform.position.x:F1}, {agent.transform.position.z:F1})."));
            foreach (var m in agent.GetComponentsInChildren<MonoBehaviour>()) if (m.enabled) { m.enabled = false; paused++; }
            driven.Add((name, agent, Nav(a.position, .6f) ?? a.position));
        }
        foreach (var d in driven) { d.agent.isStopped = true; d.agent.ResetPath(); d.agent.Warp(d.start); }
        Info($"Agent setup: {paused} AI/weapon/health scripts on {driven.Count} hall humanoids paused for the probe only; agents keep their saved speed and radius, stopping distance 0.25 m, time scale {AgentTimeScale:F0}x.");
        Time.timeScale = AgentTimeScale;
        Debug.Log($"LOWER ARENA PROBE ({mode}): static NavMesh checks done ({lines.Count} lines, {failures} failures); driving {driven.Count} agents.");
        foreach (var d in driven)
        {
            var agent = d.agent; agent.stoppingDistance = .25f; agent.isStopped = false;
            var near = housings.OrderBy(h => Mathf.Abs(Hull(h).center.z - d.start.z)).First();
            int far = d.start.x < Hull(near).center.x ? 1 : -1;
            var legs = new[] { ("front of " + near.name, FrontTarget(near)), ((far < 0 ? "west" : "east") + " end of " + near.name, EndTarget(near, far)), ("back to start", d.start) };
            foreach (var (legName, goal) in legs)
            {
                string label = d.name + " live: " + legName;
                var target = Nav(goal, .6f) ?? goal; agent.SetDestination(target);
                float started = Time.time, travelled = 0, clearance = float.MaxValue; var last = agent.transform.position;
                var probe = new NavMeshPath(); NavMesh.CalculatePath(agent.transform.position, target, NavMesh.AllAreas, probe);
                float limit = Length(probe) / Mathf.Max(.5f, agent.speed) * 2.5f + 8f; bool arrived = false;
                // Real-time cap: headless Play mode can advance game time slowly; never wait unbounded.
                double wall = EditorApplication.timeSinceStartup; int frame0 = Time.frameCount;
                while (Time.time - started < limit && EditorApplication.timeSinceStartup - wall < 90)
                {
                    yield return true;
                    var p = agent.transform.position; travelled += Vector3.Distance(Flat(p), Flat(last)); last = p;
                    foreach (var h in housings) clearance = Mathf.Min(clearance, RectDistance(Hull(h), p));
                    if (!agent.pathPending && Vector3.Distance(Flat(p), Flat(target)) <= .6f) { arrived = true; break; }
                }
                float elapsed = Time.time - started;
                Debug.Log(F($"LOWER ARENA PROBE leg {label}: arrived {arrived}, game {elapsed:F1} s, real {EditorApplication.timeSinceStartup - wall:F1} s, frames {Time.frameCount - frame0}"));
                bool clear = !saved || clearance >= agent.radius - .1f;
                Check(arrived && clear, F($"{label}: {(arrived ? "arrived" : "not arrived")} in {elapsed:F1} s game time, {travelled:F1} m travelled, nearest housing footprint {clearance:F2} m (agent radius {agent.radius:F2})"));
                results.legs.Add(new Entry { label = label, ok = arrived && clear, length = travelled, time = elapsed });
            }
            agent.isStopped = true; agent.ResetPath();
        }
        Time.timeScale = 1;

        // 5. Player capsule: lower-floor circuits, sprint passes, and (saved) push, mount, headroom and wall-gap checks.
        var player = Object.FindFirstObjectByType<PlayerCharacterController>(); var cc = player.GetComponent<CharacterController>();
        float walk = player.MaxSpeedOnGround, sprint = player.MaxSpeedOnGround * player.SprintSpeedModifier, jump = player.JumpForce;
        player.enabled = false; var input = player.GetComponent<PlayerInputHandler>(); if (input) input.enabled = false;
        var health = player.GetComponent<Health>(); if (health) health.Invincible = true;
        int disabled = 0;
        foreach (var x in agents) foreach (var c in x.GetComponentsInChildren<Collider>()) { c.enabled = false; disabled++; }
        Physics.SyncTransforms();
        Info(F($"Player setup: PlayerCharacterController and input disabled, Health invincible, {disabled} hall actor colliders disabled (Play mode only); walk {walk:F1} m/s, sprint {sprint:F1} m/s, jump {jump:F1} m/s."));
        float y = r.floorTopY, south = room.min.z + 3.2f, north = room.max.z - 3.2f, east = room.max.x - 8, west = room.min.x + 8;
        Check(Walk(cc, new[] { new Vector3(room.max.x - 3.5f, y, south), new Vector3(room.min.x + 3.5f, y, south), new Vector3(room.max.x - 3.5f, y, south) }, walk, 0, out _) == "Reached",
            "player walks the south under-gallery lane (wall distance 3.2 m) east to west and back");
        Check(Walk(cc, new[] { new Vector3(room.max.x - 3.5f, y, north), new Vector3(room.min.x + 3.5f, y, north), new Vector3(room.max.x - 3.5f, y, north) }, walk, 0, out _) == "Reached",
            "player walks the north under-gallery lane (wall distance 3.2 m) east to west and back");
        string circuit = Walk(cc, new[] { new Vector3(east, y, south), new Vector3(west, y, south), new Vector3(west, y, north), new Vector3(east, y, north), new Vector3(east, y, south) }, sprint, 0, out _);
        Check(circuit == "Reached", $"player sprints the lower-floor perimeter circuit (X {west:F1}..{east:F1}, Z {south:F1}..{north:F1}): {circuit}");
        foreach (var h in housings)
        {
            var hb = Hull(h); float face = Front(h), inward = Inward(h);
            float pass = face + inward * (cc.radius + .55f);
            string close = Walk(cc, new[] { new Vector3(hb.min.x - 2, y, pass), new Vector3(hb.max.x + 2, y, pass), new Vector3(hb.min.x - 2, y, pass) }, sprint, 0, out _);
            Check(close == "Reached", $"player sprints along {h.name} 0.55 m off its face and back: {close}");
            string slide = Walk(cc, new[] { new Vector3(hb.min.x - 2.5f, y, h.wallFace + inward * 2.7f), new Vector3(hb.max.x + 2.5f, y, h.wallFace + inward * 1.0f) }, sprint, 0, out _);
            Check(slide == "Reached", $"player sprints diagonally into {h.name}'s face and slides past its far corner without snagging: {slide}");
            if (!saved) continue;
            var push = new Vector3(0, 0, -inward); float lane = face + inward * 1.2f;
            Teleport(cc, new Vector3(hb.center.x, y, lane));
            for (int i = 0; i < 150; i++) cc.Move(push * walk * Dt + Vector3.down * 2 * Dt);
            float stop = Mathf.Abs(cc.transform.position.z - face);
            Check(stop >= cc.radius - .08f, F($"player pushed into {h.name} stops {stop:F2} m from its face (radius {cc.radius:F2})"));
            Walk(cc, new[] { new Vector3(hb.center.x + 1, y, lane), new Vector3(hb.center.x + 1, y, hb.center.z) }, walk, jump, out float top);
            bool mounted = top > hb.max.y - .05f;
            Info(F($"{h.name}: jump-assisted highest foot Y {top:F2} against cover top {hb.max.y:F2} ({(mounted ? "mountable low cover" : "not mounted")})."));
            Teleport(cc, new Vector3(hb.center.x, hb.max.y + .02f, hb.center.z));
            var ignore = new HashSet<Collider>(cc.GetComponentsInChildren<Collider>());
            float head = Physics.RaycastAll(new Vector3(hb.center.x, hb.max.y + .05f, hb.center.z), Vector3.up, 6, ~0, QueryTriggerInteraction.Ignore)
                .Where(x => !ignore.Contains(x.collider)).Select(x => x.distance).DefaultIfEmpty(6).Min();
            Check(head > cc.height + .2f, F($"standing on {h.name}: {head:F2} m headroom to the next collider above (player {cc.height:F2} m)"));
            string off = Walk(cc, new[] { new Vector3(hb.center.x, hb.max.y + .02f, hb.center.z), new Vector3(hb.center.x, y, lane + inward * 1f) }, walk, 0, out _);
            Check(off == "Reached", $"player walks off the top of {h.name} into the arena: {off}");
            // The 0.35 m wall stand-off is narrower than the standing capsule: a player cannot enter it, so cannot be wedged.
            float gap = h.wallFace + inward * cc.radius, endX = hb.max.x + cc.radius + .3f;
            string behind = Walk(cc, new[] { new Vector3(endX, y, gap), new Vector3(hb.center.x, y, gap) }, walk, 0, out _);
            float reached = cc.transform.position.x - hb.max.x;
            Check(behind != "Reached" && reached > cc.radius - .1f, F($"standing player cannot enter the stand-off gap behind {h.name}: stops {reached:F2} m beyond its end"));
            string back = Walk(cc, new[] { cc.transform.position, new Vector3(endX + 1, y, lane) }, walk, 0, out _);
            Check(back == "Reached", $"player leaves the end of {h.name} freely: {back}");
        }

        // 6. Compare with the baseline recorded before placement.
        if (saved)
        {
            var baseline = JsonUtility.FromJson<Results>(File.ReadAllText(BaselineJson));
            int routeCount = 0, legCount = 0; float worstRoute = 0, worstLeg = 0;
            foreach (var e in results.routes)
            {
                var b = baseline.routes.FirstOrDefault(x => x.label == e.label);
                if (b == null) { Check(false, "No baseline route " + e.label); continue; }
                routeCount++; worstRoute = Mathf.Max(worstRoute, e.length - b.length);
                if (!b.ok || !e.ok || e.length > b.length * 1.25f + 2f) Check(false, F($"route detour {e.label}: {b.length:F1} -> {e.length:F1} m"));
            }
            foreach (var e in results.legs)
            {
                var b = baseline.legs.FirstOrDefault(x => x.label == e.label);
                if (b == null) { Check(false, "No baseline live leg " + e.label); continue; }
                legCount++; worstLeg = Mathf.Max(worstLeg, e.time - b.time);
                if (!b.ok || !e.ok || e.time > b.time * 1.5f + 3f) Check(false, F($"live leg slowdown {e.label}: {b.time:F1} -> {e.time:F1} s"));
            }
            Check(routeCount == baseline.routes.Count && legCount == baseline.legs.Count,
                F($"baseline comparison: {routeCount}/{baseline.routes.Count} NavMesh routes (largest added length {worstRoute:F2} m) and {legCount}/{baseline.legs.Count} live legs (largest added time {worstLeg:F2} s) within 25% + 2 m / 50% + 3 s"));
        }

        string verdict = failures == 0 ? "PASS" : failures + " FAILURES";
        lines.Insert(0, $"BOSS HALL LOWER ARENA 052 ROUTE PROBE ({mode.ToUpperInvariant()}): {verdict}. Saved scene {results.scene}. NavMesh routes, live NavMeshAgents driven with AI paused, and player capsule; not live combat, AI decisions or human input.");
        Directory.CreateDirectory("Docs/BossHall"); Directory.CreateDirectory(BossHallLowerArena.Output);
        string text = string.Join("\n", lines) + "\n";
        if (saved) { File.WriteAllText("Docs/BossHall/LowerArenaRoutes_052.txt", text); File.WriteAllText(BossHallLowerArena.Output + "/Routes.txt", text); }
        else { File.WriteAllText("Docs/BossHall/LowerArenaRoutesBaseline_052.txt", text); if (failures == 0) File.WriteAllText(BaselineJson, JsonUtility.ToJson(results, true) + "\n"); }
        Debug.Log(text);
        SessionState.SetInt(Key + "Result", failures == 0 ? 0 : 1);
        CultureInfo.CurrentCulture = culture;
    }

    // Straight segments through the points, with gravity; the step is clamped to the goal. With a jump speed, jumps when stalled.
    static string Walk(CharacterController cc, Vector3[] points, float speed, float jumpSpeed, out float maxY)
    {
        Teleport(cc, points[0]); maxY = float.MinValue; float vy = 0, stall = 0;
        foreach (var g in points.Skip(1))
        {
            float best = float.MaxValue; int sinceBest = 0;
            for (int step = 0; ; step++)
            {
                var pos = cc.transform.position; var flat = Flat(g - pos);
                if (flat.magnitude < Arrive) break;
                if (step > 8000 || sinceBest > 75) { if (jumpSpeed > 0) break; return F($"Stuck near ({pos.x:F2}, {pos.y:F2}, {pos.z:F2}) heading for ({g.x:F2}, {g.z:F2})"); }
                vy -= Gravity * Dt;
                if (jumpSpeed > 0 && cc.isGrounded && stall > .25f) { vy = jumpSpeed; stall = 0; }
                var before = pos; cc.Move(flat.normalized * Mathf.Min(speed * Dt, flat.magnitude) + Vector3.up * vy * Dt);
                if (cc.isGrounded && vy < 0) vy = -1;
                var after = cc.transform.position; maxY = Mathf.Max(maxY, after.y);
                stall = Flat(after - before).magnitude < speed * Dt * .3f ? stall + Dt : 0;
                float d = Flat(g - after).magnitude; if (d < best - .05f) { best = d; sinceBest = 0; } else sinceBest++;
            }
        }
        return "Reached";
    }

    static float RectDistance(Bounds b, Vector3 p)
    {
        float dx = Mathf.Max(b.min.x - p.x, 0, p.x - b.max.x), dz = Mathf.Max(b.min.z - p.z, 0, p.z - b.max.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
    static void Teleport(CharacterController cc, Vector3 p) { cc.enabled = false; cc.transform.position = p + Vector3.up * .05f; cc.enabled = true; Physics.SyncTransforms(); }
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    static string F(FormattableString s) => FormattableString.Invariant(s);
}
