using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>Read-only Play-mode check of Room 3 navigation after loop-anchor cover carving.</summary>
[InitializeOnLoad]
public static class NorthWestRoomCoverNavigation
{
    const string Key = "CoreBuster.NorthWestCoverNavigation.";
    const string Output = "ReviewCaptures/NorthWestRoom/034/Runtime_Navigation.txt";
    static double enteredAt;
    static bool running;

    static NorthWestRoomCoverNavigation()
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
    public static void RunBatch()
    {
        EditorSceneManager.OpenScene(NorthWestRoomSurvey.ScenePath, OpenSceneMode.Single);
        NorthWestRoomCover.ValidateMenu();
        var report = NorthWestRoomFramework.MeasureLayout().report;
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
        if (state != PlayModeStateChange.EnteredEditMode) return;
        int result = SessionState.GetInt(Key + "Result", 1);
        if (SceneHash() != SessionState.GetString(Key + "Scene", ""))
        {
            Debug.LogError("Cover navigation probe changed the saved scene.");
            result = 1;
        }
        SessionState.EraseBool(Key + "Active");
        SessionState.EraseString(Key + "Scene");
        SessionState.EraseString(Key + "Report");
        if (Application.isBatchMode) EditorApplication.Exit(result);
    }

    static void Tick()
    {
        if (!running || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup - enteredAt < 3) return;
        running = false;
        try
        {
            var report = JsonUtility.FromJson<NorthWestRoomSurvey.Report>(SessionState.GetString(Key + "Report", ""));
            if (report == null || report.spawns.Count != 8)
                throw new InvalidOperationException("Missing measured Room 3 navigation report.");
            int samples = 0, paths = 0;
            for (int radius = 4; radius <= 8; radius++)
                for (int i = 0; i < 72; i++)
                {
                    float angle = i * Mathf.PI / 36f;
                    Vector3 point = report.centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    Sample(point);
                    samples++;
                }
            foreach (var spawn in report.spawns) Sample(spawn.position);
            foreach (var route in report.routes.Where(r => r.status == "PathComplete"))
            {
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(Sample(route.start), Sample(route.end), NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("Runtime route is incomplete: " + route.name);
                paths++;
            }
            var cover = GameObject.Find(NorthWestRoomFramework.RootPath + "/Cover");
            if (!cover || cover.GetComponentsInChildren<NavMeshObstacle>(true).Count(o => o.enabled && o.carving) != 3)
                throw new InvalidOperationException("All three cover carving obstacles must be active in Play mode.");
            // Each anchor must be circled: a path from the aisle side to the opposite side exists and goes around the piece.
            foreach (var piece in cover.transform.Cast<Transform>())
            {
                var box = piece.GetComponent<BoxCollider>().bounds;
                box.center = new Vector3(box.center.x, box.min.y, box.center.z);
                bool wide = box.size.x > box.size.z;
                Vector3 a = box.center + (wide ? Vector3.forward : Vector3.right) * (box.extents.z * (wide ? 1 : 0) + box.extents.x * (wide ? 0 : 1) + .8f);
                Vector3 b = box.center - (a - box.center);
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(Sample(a), Sample(b), NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("No path around cover " + piece.name);
                paths++;
            }
            string result = $"NORTH-WEST COVER RUNTIME NAVIGATION PASSED: {samples} ring samples, 8 spawn samples, " +
                $"{paths} complete routes (including 3 around-the-anchor routes) and 3 active cover carving obstacles. This checks navigation, not player sprint/dodge or live wave combat.";
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, result + "\n");
            Debug.Log(result);
            SessionState.SetInt(Key + "Result", 0);
        }
        catch (Exception ex) { Debug.LogException(ex); SessionState.SetInt(Key + "Result", 1); }
        finally { EditorApplication.ExitPlaymode(); }
    }

    static Vector3 Sample(Vector3 point)
    {
        if (!NavMesh.SamplePosition(point, out var hit, .35f, NavMesh.AllAreas))
            throw new InvalidOperationException("Missing runtime NavMesh at " + point);
        return hit.position;
    }

    static string SceneHash()
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(NorthWestRoomSurvey.ScenePath))
            return BitConverter.ToString(sha.ComputeHash(stream));
    }
}

