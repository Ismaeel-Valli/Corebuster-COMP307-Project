using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// corebuster_env_054 controlled Play-mode probe: live emitter counts and envelopes, Time.timeScale pause/resume of particles and
/// arc shader time, registry exclusion, moving combat-height views and a real CoreExecutioner regression. Nothing is saved.
/// </summary>
[InitializeOnLoad]
public static class BossHallMeltdownRuntime
{
    const string Key = "CoreBuster.BossHallMeltdownRuntime.";
    static double quietSince, entered;
    static IEnumerator routine;
    static bool failed;
    static BossHallMeltdownRuntime()
    {
        EditorApplication.playModeStateChanged += State; EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Pending", false)) Defer();
        if (SessionState.GetBool(Key + "Active", false) && SessionState.GetBool(Key + "Started", false))
            EditorApplication.delayCall += FailInterrupted;
    }
    static void FailInterrupted()
    {
        Debug.LogError("ENV054 runtime probe interrupted by a domain reload; failing instead of resuming.");
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
            BossHallMeltdown.Validate(layout); SessionState.SetString(Key + "Sha", layout.sceneSha);
            SessionState.SetInt(Key + "Renderers", BossHallMeltdown.Plan(layout).Count);
            // Envelopes are recipe-derived; carry them into Play mode, where the contract layout cannot be re-measured.
            SessionState.SetString(Key + "Envelopes", string.Join(";", BossHallMeltdown.Plan(layout).Where(e => BossHallMeltdown.IsEmitter(e.kind)).Select(e =>
            { var b = BossHallMeltdown.EffectBounds(e); return e.name + "=" + JsonUtility.ToJson(new Box { min = b.min, max = b.max }); })));
            SessionState.SetBool(Key + "Active", true); SessionState.SetBool(Key + "Ok", false); SessionState.EraseBool(Key + "Started"); EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    [Serializable] sealed class Box { public Vector3 min, max; }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { entered = EditorApplication.timeSinceStartup; routine = null; failed = false; }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        // The pause probe sets Time.timeScale; restore it and leave the TimeManager settings asset clean so it is not re-serialized.
        Time.timeScale = 1;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")) EditorUtility.ClearDirty(o);
        bool ok = SessionState.GetBool(Key + "Ok", false) && BossHallSurvey.Hash(BossHallSurvey.ScenePath) == SessionState.GetString(Key + "Sha", "");
        foreach (string k in new[] { "Active", "Ok", "Started" }) SessionState.EraseBool(Key + k);
        SessionState.EraseString(Key + "Sha"); SessionState.EraseString(Key + "Envelopes"); SessionState.EraseInt(Key + "Renderers");
        Debug.Log("ENV054 runtime probe returned to Edit mode; scene bytes unchanged: " + ok);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false) || !EditorApplication.isPlaying) return;
        foreach (var m in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (m.enabled && m.GetType().Name == "InGameMenuManager") { m.enabled = false; Debug.Log("ENV054: headless menu disabled in Play mode only"); }
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (EditorApplication.timeSinceStartup - entered < 3 || failed) return;
        if (routine == null) { routine = Probe(); SessionState.SetBool(Key + "Started", true); }
        try { if (routine.MoveNext()) return; }
        catch (Exception ex) { Debug.LogException(ex); failed = true; }
        Time.timeScale = 1; routine = null; EditorApplication.ExitPlaymode();
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Output => BossHallMeltdown.Output + "/Runtime";
    static byte[] Shot(Vector3 eye, Vector3 target)
    {
        var camera = BossHallRenderingReview.CameraForReview();
        try { return BossHallRenderingReview.Shoot(camera, eye, target); } finally { BossHallRenderingReview.DestroyCamera(camera); }
    }
    static int Changed(byte[] first, byte[] second)
    {
        var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
        try
        {
            a.LoadImage(first); b.LoadImage(second); var pa = a.GetPixels32(); var pb = b.GetPixels32(); int n = 0;
            for (int i = 0; i < pa.Length; i++) if (Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b)) > 4) n++;
            return n;
        }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    static IEnumerator Probe()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        Directory.CreateDirectory(Output); var lines = new List<string>();
        var root = BossHallFramework.FindRoot(); var section = BossHallFramework.FindSection(BossHallFramework.Section.MeltdownVfx);
        var emitters = BossHallMeltdown.Emitters(); var renderers = BossHallMeltdown.Renderers();
        Require(emitters.Length == 5 && renderers.Length == SessionState.GetInt(Key + "Renderers", -1), "Missing saved meltdown effects in Play mode");
        Require(section.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is ParticleSystem || c is ParticleSystemRenderer || c is LineRenderer || c is MeshFilter || c is MeshRenderer),
            "Runtime meltdown component allowlist");
        Require(!section.GetComponentsInChildren<Collider>(true).Any() && !section.GetComponentsInChildren<Light>(true).Any() && !section.GetComponentsInChildren<AudioSource>(true).Any(), "Collider/light/audio in meltdown");
        Require(emitters.All(p => p.isPlaying && !p.main.useUnscaledTime && !p.collision.enabled && !p.trigger.enabled && !p.lights.enabled && !p.subEmitters.enabled), "Emitter not playing or not inert");
        var envelopes = SessionState.GetString(Key + "Envelopes", "").Split(';').Select(x => x.Split(new[] { '=' }, 2))
            .ToDictionary(x => x[0], x => { var b = JsonUtility.FromJson<Box>(x[1]); var r = new Bounds(); r.SetMinMax(b.min, b.max); return r; });

        // Registry exclusion and renderer-toggle invariance.
        var actors = Object.FindAnyObjectByType<ActorsManager>(); Require(actors != null, "Missing ActorsManager");
        var manager = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(m => m.GetType().Name == "EnemyManager");
        var enemiesProperty = manager.GetType().GetProperty("Enemies");
        Object[] Enemies() => ((IEnumerable)enemiesProperty.GetValue(manager)).Cast<Object>().ToArray();
        bool Decorative(Object obj) => obj is Component c && c.transform.IsChildOf(root);
        var enemies = Enemies(); var registeredActors = actors.Actors.ToArray(); var vulnerable = CoreExecutionTarget.Vulnerable.ToArray();
        var targets = Object.FindObjectsByType<CoreExecutionTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Require(!enemies.Any(Decorative) && !registeredActors.Any(Decorative) && !targets.Any(Decorative) && !vulnerable.Any(Decorative), "Scenery registered as enemy/actor/target");
        foreach (var r in renderers) r.enabled = false; yield return null;
        Require(enemies.SequenceEqual(Enemies()) && registeredActors.SequenceEqual(actors.Actors) && vulnerable.SequenceEqual(CoreExecutionTarget.Vulnerable), "VFX toggle changed registries");
        foreach (var r in renderers) r.enabled = true;
        lines.Add($"Registries: {enemies.Length} enemies, {registeredActors.Length} actors, {targets.Length} core targets, {vulnerable.Length} vulnerable; zero dressing entries; renderer toggle leaves them identical.");

        // Live counts and envelopes over 12 s of scaled time.
        var buffers = emitters.ToDictionary(p => p, p => new ParticleSystem.Particle[p.main.maxParticles]);
        var peaks = emitters.ToDictionary(p => p, p => 0); int peakTotal = 0, frames = 0; long sum = 0; float start = Time.time;
        while (Time.time - start < 12)
        {
            int total = 0;
            foreach (var p in emitters)
            {
                int n = p.GetParticles(buffers[p]); total += n; peaks[p] = Mathf.Max(peaks[p], n); var envelope = envelopes[p.name];
                for (int i = 0; i < n; i++) Require(envelope.Contains(buffers[p][i].position), p.name + " live particle outside its recipe envelope");
            }
            peakTotal = Mathf.Max(peakTotal, total); sum += total; frames++; yield return null;
        }
        lines.Add($"Live particles over 12 s ({frames} frames): peak {peakTotal}, mean {sum / (float)Mathf.Max(1, frames):F1}; per emitter " +
            string.Join(", ", emitters.Select(p => $"{p.name} {peaks[p]}/{p.main.maxParticles}")) + ". Every live particle stayed inside its envelope.");

        // Pause: the game pauses through Time.timeScale = 0 (InGameMenuManager). Particles and the arc shader's _Time must hold.
        var ring = section.Find("Ring_Arc_Upper_North").position; var eye = ring + new Vector3(8, .5f, 0);
        Time.timeScale = 0; yield return null; yield return null;
        var frozen = emitters.ToDictionary(p => p, p => { var b = new ParticleSystem.Particle[p.main.maxParticles]; int n = p.GetParticles(b); return (n, b.Take(n).Select(x => x.position).ToArray(), p.time); });
        var pausedA = Shot(eye, ring + Vector3.down * 1.8f); { double until = EditorApplication.timeSinceStartup + 1.2; while (EditorApplication.timeSinceStartup < until) yield return null; } var pausedB = Shot(eye, ring + Vector3.down * 1.8f);
        foreach (var p in emitters)
        {
            var b = new ParticleSystem.Particle[p.main.maxParticles]; int n = p.GetParticles(b);
            Require(n == frozen[p].n && b.Take(n).Select(x => x.position).SequenceEqual(frozen[p].Item2) && p.time == frozen[p].time, p.name + " advanced while paused");
        }
        int pausedDiff = Changed(pausedA, pausedB);
        File.WriteAllBytes(Output + "/Paused_A.png", pausedA); File.WriteAllBytes(Output + "/Paused_B.png", pausedB);
        Time.timeScale = 1; { double until = EditorApplication.timeSinceStartup + .8; while (EditorApplication.timeSinceStartup < until) yield return null; } var resumed = Shot(eye, ring + Vector3.down * 1.8f); File.WriteAllBytes(Output + "/Resumed.png", resumed);
        Require(emitters.Any(p => p.time != frozen[p].time), "Emitters did not resume");
        int resumedDiff = Changed(pausedB, resumed);
        Require(pausedDiff == 0, "Ring arcs/discharge changed while paused (" + pausedDiff + " px)");
        lines.Add($"Pause (Time.timeScale 0, 1.2 s real time): all particle counts/positions and system times identical; ring view {pausedDiff} px changed. Resume: emitters advanced; ring view {resumedDiff} px changed.");

        // Moving combat-height views at floor, gallery and upper tier, with live effects.
        var player = Object.FindAnyObjectByType<PlayerCharacterController>(); float h = BossHallRenderingReview.EyeHeight();
        var focus = section.Find("Ring_Discharge").position; var runway = section.Find("Runway_Sparks_South").position; var clamp = section.Find("Clamp_Steam_South").position;
        var paths = new (string, Vector3, Vector3, Vector3)[]
        {
            ("Floor_Advance", new Vector3(-268, 6.2f + h, 115.3f), new Vector3(-285, 6.2f + h, 110), focus),
            ("Floor_Strafe_South", new Vector3(-300, 6.2f + h, 100), new Vector3(-310, 6.2f + h, 98), clamp),
            ("Upper_Tier_Walk", new Vector3(-275, 16.68f + h, 104), new Vector3(-295, 16.68f + h, 108), runway + Vector3.down * 1.5f),
        };
        foreach (var (name, a, b, target) in paths)
            for (int i = 0; i < 3; i++)
            {
                File.WriteAllBytes(Output + "/" + name + "_" + i + ".png", Shot(Vector3.Lerp(a, b, i / 2f), target));
                { double until = EditorApplication.timeSinceStartup + .25; while (EditorApplication.timeSinceStartup < until) yield return null; }
            }
        lines.Add("Moving views: Floor_Advance, Floor_Strafe_South and Upper_Tier_Walk, three frames each at standing eye height with live effects.");

        // Real execution with effects live: the cue sequence must be unchanged.
        var executor = player.GetComponent<CoreExecutioner>(); Require(executor != null, "Missing saved CoreExecutioner"); player.GetComponent<Health>().Invincible = true;
        var target2 = targets.Where(t => t.Core && t.Health && t.Health.CurrentHealth > 0 && t.transform.position.x < -270 && t.transform.position.x > -330 &&
            t.GetComponents<MonoBehaviour>().Any(m => m.GetType().Name == "ArmouredEnemy")).OrderBy(t => t.transform.position.z).FirstOrDefault();
        Require(target2 != null, "No hall armoured execution target");
        var armour = target2.GetComponents<MonoBehaviour>().Single(m => m.GetType().Name == "ArmouredEnemy");
        float ratio = (float)armour.GetType().GetField("BreakHealthRatio").GetValue(armour);
        target2.Health.TakeDamage(Mathf.Max(0, target2.Health.CurrentHealth - target2.Health.MaxHealth * (ratio - .03f)), player.gameObject);
        yield return null;
        Require(target2.IsVulnerable && CoreExecutionTarget.Vulnerable.Contains(target2), "Armour break failed to expose/register target");
        var capsule = player.GetComponent<CharacterController>(); capsule.enabled = false;
        player.transform.position = target2.transform.position + target2.transform.forward * 2f; capsule.enabled = true;
        bool deathSawConsumed = false; var health = target2.Health; var core = target2.Core;
        health.OnDie += () => deathSawConsumed = core && core.IsConsumed;
        Require(executor.TryExecute(target2), "Real CoreExecutioner refused exposed target");
        Require(!CoreExecutionTarget.Vulnerable.Contains(target2), "Executing target remained vulnerable");
        double began = EditorApplication.timeSinceStartup; bool captured = false;
        while (executor.IsExecuting)
        {
            Require(EditorApplication.timeSinceStartup - began < 30, "Execution timeout");
            Require(!CoreExecutionTarget.Vulnerable.Contains(target2), "Executing target re-entered pool");
            if (!captured && target2.Stage == CoreExecutionTarget.ExecutionStage.Impaled)
            {
                var cam = player.PlayerCamera.transform;
                File.WriteAllBytes(Output + "/Execution.png", Shot(cam.position, cam.position + cam.forward * 5)); captured = true;
            }
            yield return null;
        }
        Require(captured && core.IsConsumed && deathSawConsumed && health.CurrentHealth <= 0, "Core was not consumed before target death");
        Require(!CoreExecutionTarget.Vulnerable.Any(Decorative) && !Enemies().Any(Decorative), "Decoration entered registry during execution");
        lines.Add("Execution with live effects: real armour damage exposed a hall target; CoreExecutioner removed it from the pool; impaled capture recorded; core consumed before Health.OnDie.");
        lines.Add("Player moved/invincible, menu disabled and time scale changed in this discarded Play session only. Not human combat or encounter/win-order verification.");
        string report = "ENV054 controlled Play-mode probe\n" + string.Join("\n", lines) + "\n";
        File.WriteAllText("Docs/BossHall/MeltdownRuntime_054.txt", report); Debug.Log(report); SessionState.SetBool(Key + "Ok", true);
    }
}
