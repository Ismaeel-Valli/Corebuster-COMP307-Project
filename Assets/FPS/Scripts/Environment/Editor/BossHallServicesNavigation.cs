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
/// corebuster_env_050 read-only Play-mode probe. With all four under-gallery carving footprints and the env_049 cradle
/// carve active it checks each carve, NavMesh lanes in front of and beside every hull, every humanoid's path to the entry,
/// every surveyed connector (entry to lower stairs, stairs, galleries, spine and bay-door approaches) and live agents.
/// The saved player's CharacterController then walks both under-gallery lanes end to end, is pushed into each hull and
/// tries to jump onto it. Player input/script and hall actor colliders are disabled for the probe only; Play-mode changes
/// are discarded and the saved scene hash is checked afterwards. Not a live-combat or human-input test.
/// </summary>
[InitializeOnLoad]
public static class BossHallServicesNavigation
{
    const string Key = "CoreBuster.BossHallServicesNavigation.";
    const float Speed = 4f, Dt = .02f, Arrive = .3f, Gravity = 20f, JumpSpeed = 9f;
    static double enteredAt;
    static bool running;

    static BossHallServicesNavigation()
    {
        EditorApplication.playModeStateChanged += OnState;
        EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Active", false) && EditorApplication.isPlaying) { enteredAt = EditorApplication.timeSinceStartup; running = true; }
    }

    [MenuItem("Tools/CoreBuster/Run Boss Hall Services Navigation Probe")]
    public static void RunMenu() => Begin();
    // Run without -quit: the callback exits after Play mode ends.
    public static void RunBatch() { EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); Begin(); }

    static void Begin()
    {
        BossHallServices.Validate(BossHallFramework.MeasureLayout());
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
        if (BossHallSurvey.Hash(BossHallSurvey.ScenePath) != SessionState.GetString(Key + "Scene", "")) { Debug.LogError("Services probe changed the saved scene."); result = 1; }
        SessionState.EraseBool(Key + "Active"); SessionState.EraseString(Key + "Scene");
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
            var layout = new BossHallFramework.Layout(contract); var r = layout.Report; var f = BossHallServices.Measure(layout);
            var lines = new List<string>(); int failures = 0;
            void Check(bool ok, string text) { lines.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) failures++; }

            var hulls = new List<(string name, BoxCollider box, NavMeshObstacle carve)>();
            foreach (var section in new[] { "Power_Services", "Cooling_Services" })
                foreach (Transform root in GameObject.Find(BossHallFramework.RootPath + "/" + section).transform)
                {
                    var t = root.Find(BossHallServices.HullName);
                    hulls.Add((root.name, t ? t.GetComponent<BoxCollider>() : null, t ? t.GetComponent<NavMeshObstacle>() : null));
                }
            Check(hulls.Count == 4 && hulls.All(h => h.box && h.carve && h.carve.enabled && h.carve.carving), $"{hulls.Count(h => h.carve && h.carve.carving)}/4 service hull carving obstacles enabled and carving in Play mode");
            var overhead = GameObject.Find(BossHallFramework.RootPath + "/Overhead");
            Check(overhead && overhead.GetComponentsInChildren<Collider>().Length == 0 && overhead.GetComponentsInChildren<NavMeshObstacle>().Length == 0, "overhead feeders add no runtime collision or carving");
            float capsuleTop = r.tier2TopY + 1.8f;
            Check(overhead && overhead.GetComponentsInChildren<Renderer>().All(x => x.bounds.min.y > capsuleTop), F($"every feeder renderer is above a standing player on the second tier (Y {capsuleTop:F2})"));

            Vector3? Nav(Vector3 p, float tolerance = .4f) => NavMesh.SamplePosition(p, out var h, tolerance, NavMesh.AllAreas) && Mathf.Abs(h.position.y - p.y) < .6f ? h.position : (Vector3?)null;
            var entryInside = r.entry.centre + r.entry.inward * 3f;
            var entry = Nav(entryInside) ?? throw new InvalidOperationException("Entry inside point is off the NavMesh");
            bool Complete(Vector3 a, Vector3 b, out NavMeshPath path) { path = new NavMeshPath(); return NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete; }

            // 1-2. Each carve and the NavMesh lanes in front of and beyond each hull.
            foreach (var h in hulls.Where(h => h.box))
            {
                var hb = h.box.bounds; int inside = 0, onMesh = 0;
                for (float x = hb.min.x + .25f; x < hb.max.x; x += .5f)
                    for (float z = hb.min.z + .25f; z < hb.max.z; z += .5f)
                    {
                        inside++;
                        if (NavMesh.SamplePosition(new Vector3(x, r.floorTopY, z), out var hit, .2f, NavMesh.AllAreas) &&
                            hit.position.x > hb.min.x && hit.position.x < hb.max.x && hit.position.z > hb.min.z && hit.position.z < hb.max.z) onMesh++;
                    }
                Check(onMesh == 0, $"{h.name} carve: {onMesh}/{inside} samples inside the hull footprint are on the NavMesh");
                bool south = hb.center.z < f.room.center.z; float front = south ? hb.max.z + 1.2f : hb.min.z - 1.2f;
                var ring = new List<Vector3>();
                for (float x = hb.min.x - 1.2f; x <= hb.max.x + 1.21f; x += 1f) ring.Add(new Vector3(x, r.floorTopY, front));
                foreach (float x in new[] { hb.min.x - 1.2f, hb.max.x + 1.2f }) ring.Add(new Vector3(x, r.floorTopY, hb.center.z));
                int on = 0, reach = 0;
                foreach (var p in ring) { var n = Nav(p); if (n == null) continue; on++; if (Complete(entry, n.Value, out _)) reach++; }
                Check(on == ring.Count && reach == ring.Count, $"{h.name} lane: {on}/{ring.Count} samples 1.2 m in front of and beside the hull on the NavMesh, {reach}/{ring.Count} reachable from the entry");
            }

            // 3. Routes: hall humanoids to the entry and every surveyed connector; none may cross a service hull.
            var keeps = hulls.Where(h => h.box).Select(h => { var b = h.box.bounds; b.Expand(new Vector3(.6f, 20, .6f)); return b; }).ToArray();
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
                if (na == null || nb == null) { Check(false, label + ": endpoint off the NavMesh"); return; }
                bool ok = Complete(na.Value, nb.Value, out var path);
                Check(ok && !Crosses(path), $"{label}: {(ok ? "complete" : path.status.ToString())} {Length(path):F1} m{(Crosses(path) ? ", crosses a service hull" : "")}");
            }
            foreach (var a in r.actors.Where(a => a.kind != "turret")) Route(a.name + F($" ({a.position.x:F0}, {a.position.z:F0}) to entry inside"), a.position, entryInside);
            foreach (var route in r.routes.Where(x => x.connector && x.corners.Length > 1)) Route("Connector " + route.name, route.corners[0], route.corners[route.corners.Length - 1]);

            // 4. Live agents after carving.
            var hall = layout.interior; hall.Expand(new Vector3(4, 10, 4));
            var agents = Object.FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None).Where(a => hall.Contains(a.transform.position)).ToArray();
            Check(agents.Length == r.actors.Count && agents.All(a => a.isOnNavMesh), $"live hall agents on the NavMesh with carving active: {agents.Count(a => a.isOnNavMesh)}/{agents.Length} (survey actors {r.actors.Count})");

            // 5. Player capsule along both under-gallery lanes, into each hull, and a jump attempt onto each hull.
            var player = Object.FindFirstObjectByType<PlayerCharacterController>(); var cc = player.GetComponent<CharacterController>();
            player.enabled = false; var input = player.GetComponent<PlayerInputHandler>(); if (input) input.enabled = false;
            var health = player.GetComponent<Health>(); if (health) health.Invincible = true;
            int disabled = 0;
            foreach (var agent in agents) foreach (var c in agent.GetComponentsInChildren<Collider>()) { c.enabled = false; disabled++; }
            Physics.SyncTransforms();
            lines.Add($"Probe setup: PlayerCharacterController and input disabled, Health invincible, {disabled} hall actor colliders disabled (Play mode only).");
            float y = r.floorTopY;
            for (int side = 0; side < 2; side++)
            {
                float lane = f.Z(side, 3.2f); string tag = side == 0 ? "south" : "north";
                var walk = Walk(cc, new[] { new Vector3(f.room.max.x - 3.5f, y, lane), new Vector3(f.room.min.x + 3.5f, y, lane), new Vector3(f.room.max.x - 3.5f, y, lane) }, false, out _);
                Check(walk == "Reached", $"player walks the {tag} under-gallery lane (wall distance 3.2 m) east to west and back: {walk}");
            }
            foreach (var h in hulls.Where(h => h.box))
            {
                var hb = h.box.bounds; bool south = hb.center.z < f.room.center.z; float face = south ? hb.max.z : hb.min.z, laneZ = face + (south ? 1.2f : -1.2f);
                var push = south ? Vector3.back : Vector3.forward;
                Teleport(cc, new Vector3(hb.center.x, y, laneZ));
                for (int i = 0; i < 150; i++) cc.Move(push * Speed * Dt + Vector3.down * 2 * Dt);
                float stop = Mathf.Abs(cc.transform.position.z - face);
                Check(stop >= cc.radius - .08f, F($"player pushed into {h.name} stops {stop:F2} m from the hull face (radius {cc.radius:F2})"));
                Walk(cc, new[] { new Vector3(hb.center.x + 1, y, laneZ), new Vector3(hb.center.x + 1, y, (hb.min.z + hb.max.z) * .5f) }, true, out float maxY);
                Check(maxY < hb.max.y - .2f, F($"player with jump assist cannot get onto {h.name}: highest foot Y {maxY:F2}, hull top {hb.max.y:F2}"));
            }

            lines.Insert(0, $"BOSS HALL SERVICES 050 NAVIGATION PROBE: {(failures == 0 ? "PASS" : failures + " FAILURES")}. Physics capsule and NavMesh probe with all service and cradle carves active; not live combat or human input.");
            string output = BossHallServices.Output + "/Navigation.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, string.Join("\n", lines) + "\n");
            Directory.CreateDirectory("Docs/BossHall"); File.WriteAllText("Docs/BossHall/Navigation_050.txt", string.Join("\n", lines) + "\n");
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
                if (step > 6000 || sinceBest > 75) { if (jumpAssist) break; return F($"Stuck near ({pos.x:F2}, {pos.y:F2}, {pos.z:F2}) heading for ({g.x:F2}, {g.z:F2})"); }
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
