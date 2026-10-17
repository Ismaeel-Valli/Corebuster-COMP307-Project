using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Controlled Play-mode registry and real CoreExecutioner regression; changes are discarded, never saved.</summary>
[InitializeOnLoad]
public static class BossHallEnergyRuntime
{
    const string Key = "CoreBuster.BossHallEnergyRuntime.";
    static double quietSince, entered;
    static IEnumerator routine;
    static bool failed;
    static BossHallEnergyRuntime()
    {
        EditorApplication.playModeStateChanged += State; EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Pending", false)) Defer();
        if (SessionState.GetBool(Key + "Active", false) && SessionState.GetBool(Key + "Started", false))
            EditorApplication.delayCall += FailInterrupted;
    }
    static void FailInterrupted()
    {
        Debug.LogError("ENV053 runtime probe interrupted by a domain reload; failing instead of resuming a partially executed target.");
        SessionState.SetBool(Key + "Ok", false);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else EditorApplication.Exit(1);
    }
    public static void RunBatch() => Defer(); // Omit -quit; waits for domain reloads and exits after returning to Edit mode.
    static void Defer()
    {
        SessionState.SetBool(Key + "Pending", true); quietSince = EditorApplication.timeSinceStartup;
        EditorApplication.update -= WaitForQuiet; EditorApplication.update += WaitForQuiet;
    }
    static void WaitForQuiet()
    {
        if (EditorApplication.timeSinceStartup - quietSince < 45 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= WaitForQuiet; SessionState.EraseBool(Key + "Pending");
        try
        {
            EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single); var layout = BossHallFramework.MeasureLayout();
            BossHallEnergy.Validate(layout); SessionState.SetString(Key + "Sha", layout.sceneSha);
            SessionState.SetBool(Key + "Active", true); SessionState.SetBool(Key + "Ok", false); SessionState.EraseBool(Key + "Started"); EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { entered = EditorApplication.timeSinceStartup; routine = null; failed = false; }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        bool ok = SessionState.GetBool(Key + "Ok", false) && BossHallSurvey.Hash(BossHallSurvey.ScenePath) == SessionState.GetString(Key + "Sha", "");
        SessionState.EraseBool(Key + "Active"); SessionState.EraseBool(Key + "Ok"); SessionState.EraseString(Key + "Sha");
        SessionState.EraseBool(Key + "Started");
        Debug.Log("ENV053 runtime probe returned to Edit mode; scene bytes unchanged: " + ok);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false) || !EditorApplication.isPlaying) return;
        foreach (var m in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (m.enabled && m.GetType().Name == "InGameMenuManager") { m.enabled = false; Debug.Log("ENV053: headless menu disabled in Play mode only"); }
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (EditorApplication.timeSinceStartup - entered < 3 || failed) return;
        if (routine == null) { routine = Probe(); SessionState.SetBool(Key + "Started", true); }
        try { if (routine.MoveNext()) return; }
        catch (Exception ex) { Debug.LogException(ex); failed = true; }
        routine = null; EditorApplication.ExitPlaymode();
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static IEnumerator Probe()
    {
        var root = BossHallFramework.FindRoot(); var groups = BossHallEnergy.Groups().ToArray(); var renderers = BossHallEnergy.Renderers();
        Require(groups.Length == 7 && renderers.Length == 9, "Missing saved energy in Play mode");
        Require(groups.SelectMany(g => g.GetComponentsInChildren<Component>(true)).All(c => c is Transform || c is MeshFilter || c is MeshRenderer), "Runtime energy component allowlist");
        var actors = Object.FindAnyObjectByType<ActorsManager>(); Require(actors != null, "Missing ActorsManager");
        var manager = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(m => m.GetType().Name == "EnemyManager");
        var enemiesProperty = manager.GetType().GetProperty("Enemies");
        Object[] Enemies() => ((IEnumerable)enemiesProperty.GetValue(manager)).Cast<Object>().ToArray();
        bool Decorative(Object obj) => obj is Component c && c.transform.IsChildOf(root);
        var enemies = Enemies(); var registeredActors = actors.Actors.ToArray(); var vulnerable = CoreExecutionTarget.Vulnerable.ToArray();
        var targets = Object.FindObjectsByType<CoreExecutionTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var cores = Object.FindObjectsByType<UnstableCore>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Require(!enemies.Any(Decorative) && !registeredActors.Any(Decorative) && !targets.Any(Decorative) && !cores.Any(Decorative) && !vulnerable.Any(Decorative), "Scenery registered as an enemy/core/actor/target");
        foreach (var r in renderers) r.enabled = false;
        Require(enemies.SequenceEqual(Enemies()) && registeredActors.SequenceEqual(actors.Actors) && vulnerable.SequenceEqual(CoreExecutionTarget.Vulnerable), "Energy toggle changed runtime registries");
        foreach (var r in renderers) r.enabled = true;
        Debug.Log($"ENV053 registry proof: {enemies.Length} enemies, {registeredActors.Length} actors, {targets.Length} targets, {cores.Length} cores, {vulnerable.Length} vulnerable; zero scenery entries.");
        var player = Object.FindAnyObjectByType<PlayerCharacterController>(); var executor = player.GetComponent<CoreExecutioner>();
        Require(executor != null, "Missing saved CoreExecutioner"); player.GetComponent<Health>().Invincible = true;
        var target = targets.Where(t => t.Core && t.Health && t.Health.CurrentHealth > 0 && t.transform.position.x < -270 && t.transform.position.x > -330 &&
            t.GetComponents<MonoBehaviour>().Any(m => m.GetType().Name == "ArmouredEnemy")).OrderBy(t => t.transform.position.z).FirstOrDefault();
        Require(target != null, "No hall armoured execution target");
        // Drive existing armour-break damage and the real execution coroutine, without altering runtime production code.
        var armour = target.GetComponents<MonoBehaviour>().Single(m => m.GetType().Name == "ArmouredEnemy");
        float ratio = (float)armour.GetType().GetField("BreakHealthRatio").GetValue(armour);
        target.Health.TakeDamage(Mathf.Max(0, target.Health.CurrentHealth - target.Health.MaxHealth * (ratio - .03f)), player.gameObject);
        yield return null;
        Require(target.IsVulnerable && CoreExecutionTarget.Vulnerable.Contains(target), "Armour break failed to expose/register target");
        var capsule = player.GetComponent<CharacterController>(); capsule.enabled = false;
        player.transform.position = target.transform.position + target.transform.forward * 2f; capsule.enabled = true;
        bool deathSawConsumed = false; var health = target.Health; var core = target.Core;
        health.OnDie += () => deathSawConsumed = core && core.IsConsumed;
        Require(executor.TryExecute(target), "Real CoreExecutioner refused exposed target");
        Require(!CoreExecutionTarget.Vulnerable.Contains(target), "Executing target remained vulnerable");
        double start = EditorApplication.timeSinceStartup; bool captured = false;
        while (executor.IsExecuting)
        {
            Require(EditorApplication.timeSinceStartup - start < 30, "Execution timeout");
            Require(!CoreExecutionTarget.Vulnerable.Contains(target), "Executing target re-entered pool");
            if (!captured && target.Stage == CoreExecutionTarget.ExecutionStage.Impaled)
            {
                var camera = BossHallRenderingReview.CameraForReview();
                try
                {
                    Directory.CreateDirectory(BossHallEnergy.Output + "/Runtime");
                    File.WriteAllBytes(BossHallEnergy.Output + "/Runtime/Execution.png", BossHallRenderingReview.Shoot(camera,
                        player.PlayerCamera.transform.position, player.PlayerCamera.transform.position + player.PlayerCamera.transform.forward * 5));
                }
                finally { BossHallRenderingReview.DestroyCamera(camera); }
                captured = true;
            }
            yield return null;
        }
        Require(captured && core.IsConsumed && deathSawConsumed && health.CurrentHealth <= 0, "Core was not consumed before target death");
        Require(!CoreExecutionTarget.Vulnerable.Any(Decorative) && !Enemies().Any(Decorative), "Decoration entered registry during execution");
        string report = $"ENV053 controlled Play-mode regression: {enemies.Length} enemies, {registeredActors.Length} actors, {targets.Length} core targets, {cores.Length} cores; zero dressing entries.\n" +
            "Energy renderer toggle leaves enemy/actor/vulnerable registries identical. Scenery allowlist remains Transform/MeshFilter/MeshRenderer.\n" +
            "Real hall armour damage exposed target; CoreExecutioner.TryExecute removed it from pool; impaled capture recorded; core consumed before Health.OnDie; execution finished.\n" +
            "Player moved/invincible and headless menu disabled in Play mode only. This is controlled execution regression, not human combat or encounter/win-order verification.\n";
        File.WriteAllText("Docs/BossHall/EnergyRuntime_053.txt", report); Debug.Log(report); SessionState.SetBool(Key + "Ok", true);
    }
}
