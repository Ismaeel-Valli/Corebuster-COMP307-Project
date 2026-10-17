using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>Read-only Play-mode probe of the real carved NavMesh; does not simulate a two-wave encounter.</summary>
[InitializeOnLoad]
public static class CentreRoomReactorNavigation
{
    const string Key = "CoreBuster.ReactorNavigation.";
    static double enteredAt;
    static bool running;

    static CentreRoomReactorNavigation()
    {
        EditorApplication.playModeStateChanged += OnState;
        EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Active", false) && EditorApplication.isPlaying)
        {
            enteredAt = EditorApplication.timeSinceStartup;
            running = true;
        }
    }

    // Run without -quit: this entry point exits after Play mode and domain reload finish.
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        CentreRoomReactor.ValidateMenu();
        var report = CentreRoomFramework.MeasureLayout().report;
        SessionState.SetString(Key + "Report", JsonUtility.ToJson(report));
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
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            int result = SessionState.GetInt(Key + "Result", 1);
            if (SceneHash() != SessionState.GetString(Key + "Scene", ""))
            { Debug.LogError("Navigation probe modified the saved scene."); result = 1; }
            SessionState.EraseBool(Key + "Active");
            SessionState.EraseString(Key + "Scene");
            SessionState.EraseString(Key + "Report");
            if (Application.isBatchMode) EditorApplication.Exit(result);
        }
    }

    static void Tick()
    {
        if (!running || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup - enteredAt < 3) return;
        running = false;
        try
        {
            var report = JsonUtility.FromJson<CentreRoomSurvey.Report>(SessionState.GetString(Key + "Report", ""));
            Vector3 center = report.ringCenter;
            if (NavMesh.SamplePosition(center, out _, .3f, NavMesh.AllAreas))
                throw new InvalidOperationException("Reactor centre is still walkable after carving.");
            var section = GameObject.Find(CentreRoomFramework.RootPath + "/Reactor").transform;
            if (CoreExecutionTarget.Vulnerable.Any(t => t && t.transform.IsChildOf(section)))
                throw new InvalidOperationException("Decorative reactor entered the vulnerable target pool.");
            var terminal = UnityEngine.Object.FindObjectsByType<KeycardTerminal>()
                .Single(t => t.name == "Centre Room Terminal (Yellow)");
            var agents = terminal.Waves.SelectMany(w => w.Enemies).Select(e => e.GetComponentInChildren<NavMeshAgent>()).ToArray();
            if (agents.Length != 8 || agents.Any(a => !a || a.agentTypeID != 0 || a.radius > .5f))
                throw new InvalidOperationException("Wave agent profiles differ from the measured type-0/radius-0.5 maximum contract.");
            int paths = 0, samples = 0;
            Vector3 Ring(float radius, int i) => center + new Vector3(Mathf.Cos(i * Mathf.PI / 36), 0, Mathf.Sin(i * Mathf.PI / 36)) * radius;
            foreach (float radius in new[] { 4f, 6f, 8f })
                for (int i = 0; i < 72; i++)
                {
                    Vector3 point = Ring(radius, i);
                    Sample(point); samples++;
                    CheckPath(point, Ring(radius, (i + 1) % 72), center); paths++;
                    // Mobile actors are expected in the ring; only static collision fails clearance.
                    Collider[] collisions = Physics.OverlapCapsule(point + Vector3.up * .45f,
                        point + Vector3.up * 1.45f, .4f, ~0, QueryTriggerInteraction.Ignore);
                    if (collisions.Any(c => !c.GetComponentInParent<NavMeshAgent>() && !c.GetComponentInParent<PlayerCharacterController>()))
                        throw new InvalidOperationException("Static collision in reactor circulation ring at " + point);
                }
            foreach (var spawn in report.spawns)
            {
                Vector3 outward = spawn.position - center; outward.y = 0;
                CheckPath(spawn.position, center + outward.normalized * 6, center); paths++;
                CheckPath(spawn.position, center - outward.normalized * 6, center); paths++;
            }
            for (int i = 0; i < 36; i++) { CheckPath(Ring(6, i), Ring(6, i + 36), center); paths++; }
            foreach (var route in report.routes.Where(r => r.name.Contains("to ring") || r.name == "Ring to terminal"))
            { CheckPath(route.start, route.end, center); paths++; }
            string result = $"REACTOR RUNTIME NAVIGATION PASSED: {samples} ring samples; {paths} complete routes " +
                "on the carved NavMesh; reactor centre unwalkable; no route cuts through radius 2.7 m; " +
                "all eight wave prefab agent profiles compatible; static capsule clearance and target pool passed. " +
                "This is a navigation probe, not a player-input or live two-wave combat test.";
            Directory.CreateDirectory(CentreRoomReactor.Output);
            File.WriteAllText(CentreRoomReactor.Output + "/Runtime_Navigation.txt", result);
            Debug.Log(result);
            SessionState.SetInt(Key + "Result", 0);
        }
        catch (Exception e) { Debug.LogException(e); SessionState.SetInt(Key + "Result", 1); }
        finally { EditorApplication.ExitPlaymode(); }
    }

    static Vector3 Sample(Vector3 p)
    {
        if (!NavMesh.SamplePosition(p, out var hit, .35f, NavMesh.AllAreas))
            throw new InvalidOperationException("Missing runtime NavMesh at " + p);
        return hit.position;
    }

    static string SceneHash()
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(CentreRoomSurvey.ScenePath))
            return BitConverter.ToString(sha.ComputeHash(stream));
    }

    static void CheckPath(Vector3 start, Vector3 end, Vector3 center)
    {
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(Sample(start), Sample(end), NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
            throw new InvalidOperationException($"Incomplete runtime route: {start} to {end}");
        for (int i = 1; i < path.corners.Length; i++)
        {
            Vector2 a = new Vector2(path.corners[i - 1].x - center.x, path.corners[i - 1].z - center.z);
            Vector2 b = new Vector2(path.corners[i].x - center.x, path.corners[i].z - center.z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > .00001f ? Mathf.Clamp01(-Vector2.Dot(a, ab) / ab.sqrMagnitude) : 0;
            if ((a + t * ab).magnitude < 2.675f) throw new InvalidOperationException("Runtime path crosses reactor footprint.");
        }
    }
}
