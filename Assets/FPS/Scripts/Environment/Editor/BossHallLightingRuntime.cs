using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>env_055 controlled lighting/combat/execution regression and Editor frame samples. No persistent gameplay changes.</summary>
[InitializeOnLoad]
public static class BossHallLightingRuntime
{
    const string Key = "CoreBuster.BossHallLightingRuntime.";
    static double quietSince, entered;
    static IEnumerator routine;
    static bool failed;
    static readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
    static BossHallLightingRuntime()
    {
        EditorApplication.playModeStateChanged += State; EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Pending", false)) Defer();
        if (SessionState.GetBool(Key + "Active", false) && SessionState.GetBool(Key + "Started", false))
            EditorApplication.delayCall += () => {
                Debug.LogError("ENV055 runtime interrupted by domain reload"); SessionState.SetBool(Key + "Ok", false);
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else EditorApplication.Exit(1);
            };
    }
    public static void RunBatch() => Defer(); // Graphics required; omit -quit.
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
            EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
            var layout = BossHallFramework.MeasureLayout(); BossHallFramework.Validate(layout);
            Require(BossHallLighting.Lights().Length == 24, "Saved lighting is missing");
            SessionState.SetString(Key + "Sha", layout.sceneSha);
            SessionState.SetString(Key + "Shared", BossHallSnapshot.Digest(BossHallSnapshot.CaptureAssets()));
            BossHallRevealRuntime.RememberKnownAsset();
            SessionState.SetBool(Key + "Active", true); SessionState.SetBool(Key + "Ok", false); SessionState.EraseBool(Key + "Started");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { entered = EditorApplication.timeSinceStartup; routine = null; failed = false; }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        Time.timeScale = 1;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")) EditorUtility.ClearDirty(o);
        string known = BossHallRevealRuntime.RestoreKnownAsset(); Debug.Log("ENV055 known shared-asset check: " + (known ?? "UNEXPECTED CHANGE"));
        bool ok = known != null && SessionState.GetBool(Key + "Ok", false) && BossHallSurvey.Hash(BossHallSurvey.ScenePath) == SessionState.GetString(Key + "Sha", "") &&
            BossHallSnapshot.Digest(BossHallSnapshot.CaptureAssets()) == SessionState.GetString(Key + "Shared", "");
        foreach (var recorder in recorders) recorder.Dispose(); recorders.Clear();
        foreach (string k in new[] { "Active", "Ok", "Started" }) SessionState.EraseBool(Key + k);
        SessionState.EraseString(Key + "Sha"); SessionState.EraseString(Key + "Shared");
        Debug.Log("ENV055 runtime returned to Edit mode; scene/shared assets unchanged: " + ok);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false) || !EditorApplication.isPlaying) return;
        foreach (var m in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            if (m.enabled && m.GetType().Name == "InGameMenuManager") m.enabled = false;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (EditorApplication.timeSinceStartup - entered < 3 || failed) return;
        if (routine == null) { routine = Probe(); SessionState.SetBool(Key + "Started", true); }
        try { if (routine.MoveNext()) return; }
        catch (Exception ex) { Debug.LogException(ex); failed = true; }
        (routine as IDisposable)?.Dispose(); routine = null; Time.timeScale = 1; EditorApplication.ExitPlaymode();
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Output => BossHallLighting.Output + "/Runtime";
    static byte[] Shot(Vector3 eye, Vector3 target)
    {
        var camera = BossHallRenderingReview.CameraForReview();
        try { return BossHallRenderingReview.Shoot(camera, eye, target); }
        finally { BossHallRenderingReview.DestroyCamera(camera); }
    }
    static void MovePlayer(PlayerCharacterController player, Vector3 position, Vector3 target)
    {
        var body = player.GetComponent<CharacterController>(); body.enabled = false;
        player.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.ProjectOnPlane(target - position, Vector3.up)));
        body.enabled = true;
    }
    static IEnumerator Probe()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        Directory.CreateDirectory(Output); var lines = new List<string>(); var root = BossHallFramework.FindRoot(); var lamps = BossHallLighting.Lights();
        Require(lamps.Length == 24 && lamps.All(l => l.enabled && l.intensity > 0), "Missing/disabled runtime lights");
        var flicker = lamps.Where(l => l.GetComponent<ReactorInstabilityLight>() || l.GetComponent<WorkshopAmbientMotion>()).ToArray();
        Require(flicker.Length == 3, "Missing reserved flicker bindings");
        var section = BossHallFramework.FindSection(BossHallFramework.Section.Lighting);
        Require(!section.GetComponentsInChildren<Renderer>(true).Any() && !section.GetComponentsInChildren<Collider>(true).Any() &&
            !section.GetComponentsInChildren<AudioSource>(true).Any(), "Lighting acquired geometry, collision or audio");
        var actors = Object.FindAnyObjectByType<ActorsManager>();
        var manager = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude).Single(m => m.GetType().Name == "EnemyManager");
        var enemiesProperty = manager.GetType().GetProperty("Enemies");
        Object[] Enemies() => ((IEnumerable)enemiesProperty.GetValue(manager)).Cast<Object>().ToArray();
        bool Decorative(Object obj) => obj is Component c && c.transform.IsChildOf(root);
        var enemies = Enemies(); var registeredActors = actors.Actors.ToArray(); var vulnerable = CoreExecutionTarget.Vulnerable.ToArray();
        var targets = Object.FindObjectsByType<CoreExecutionTarget>(FindObjectsInactive.Include);
        var hallTargets = targets.Where(t => t.transform.position.x > -330 && t.transform.position.x < -263).ToArray();
        Require(!enemies.Any(Decorative) && !registeredActors.Any(Decorative) && !targets.Any(Decorative) && !vulnerable.Any(Decorative), "Decoration entered gameplay registry");
        foreach (var l in lamps) l.enabled = false;
        Require(enemies.SequenceEqual(Enemies()) && registeredActors.SequenceEqual(actors.Actors) && vulnerable.SequenceEqual(CoreExecutionTarget.Vulnerable), "Lighting toggle changed registries");
        foreach (var l in lamps) l.enabled = true;
        lines.Add($"Registries: {enemies.Length} enemies, {registeredActors.Length} actors, {targets.Length} targets; zero dressing entries; light toggle leaves registries identical.");

        var player = Object.FindAnyObjectByType<PlayerCharacterController>(); player.GetComponent<Health>().Invincible = true;
        // env_056: the merged tutorial wing starts the player unarmed; CoreExecutioner needs an active weapon.
        var arm = BossHallRevealRuntime.ArmFromLocker(player, lines); while (arm.MoveNext()) yield return arm.Current;
        int npcShots = 0;
        var weapons = Object.FindObjectsByType<WeaponController>(FindObjectsInactive.Include)
            .Where(w => !w.transform.IsChildOf(player.transform) && w.transform.position.x > -330 && w.transform.position.x < -263).ToArray();
        // These are scene instances; leaving Play mode discards the observation delegates with the instances.
        foreach (var weapon in weapons) weapon.OnShootProcessed += () => npcShots++;
        MovePlayer(player, new Vector3(-280, 6.25f, 115.3f), new Vector3(-296, 9, 115.3f));
        var seenMin = flicker.Select(l => l.intensity).ToArray(); var seenMax = (float[])seenMin.Clone();
        float start = Time.time; double deadline = EditorApplication.timeSinceStartup + 30;
        while (Time.time - start < 2)
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Flicker probe stopped advancing");
            for (int i = 0; i < flicker.Length; i++) { seenMin[i] = Mathf.Min(seenMin[i], flicker[i].intensity); seenMax[i] = Mathf.Max(seenMax[i], flicker[i].intensity); }
            yield return null;
        }
        Require(Enumerable.Range(0, 3).All(i => seenMax[i] > seenMin[i] + .01f), "A flicker binding did not animate");
        Time.timeScale = 0; yield return null; yield return null;
        var frozen = flicker.Select(l => l.intensity).ToArray(); double until = EditorApplication.timeSinceStartup + 1.2;
        while (EditorApplication.timeSinceStartup < until)
        { Require(frozen.SequenceEqual(flicker.Select(l => l.intensity)), "Lighting advanced during pause"); yield return null; }
        Time.timeScale = 1; start = Time.time;
        while (Time.time - start < .8f) yield return null;
        Require(!frozen.SequenceEqual(flicker.Select(l => l.intensity)), "Lighting did not resume");
        lines.Add("Three bound lamps animate, freeze exactly over 1.2 s real time at timeScale 0, and resume. Observed intensity ranges: " +
            string.Join(", ", Enumerable.Range(0, 3).Select(i => $"{flicker[i].name} {seenMin[i]:F2}-{seenMax[i]:F2}")) + ".");

        // Available engine counters, measured on actual advancing Play-mode frames. Never label readback/PNG time GPU time.
        var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
        var names = new[] { "Main Thread", "Render Thread", "GPU Frame Time", "Draw Calls Count", "SetPass Calls Count", "Triangles Count", "Shadow Casters Count" };
        var counters = handles.Select(ProfilerRecorderHandle.GetDescription).Where(d => names.Contains(d.Name)).GroupBy(d => d.Name).Select(g => g.First()).ToArray();
        foreach (var d in counters) recorders.Add(ProfilerRecorder.StartNew(d.Category, d.Name, 1));
        lines.Add("Available frame counters: " + string.Join(", ", counters.Select(d => d.Name + " [" + d.UnitType + "]")));
        var views = new[] {
            (name: "Entry_Combat", foot: new Vector3(-267, 6.25f, 115.3f), target: new Vector3(-296, 9, 115.3f)),
            (name: "Wide_Arena_Combat", foot: new Vector3(-280, 6.25f, 100), target: new Vector3(-305, 9, 115)),
            (name: "Gallery_Combat", foot: new Vector3(-313, 10.75f, 143), target: new Vector3(-296, 9, 115.3f)),
            (name: "Upper_Reverse", foot: new Vector3(-315, 16.73f, 115.3f), target: new Vector3(-275, 9, 115.3f)) };
        var renderCamera = BossHallRenderingReview.CameraForReview();
        var rt = new RenderTexture(BossHallRenderingReview.Width, BossHallRenderingReview.Height, 24); rt.Create(); renderCamera.targetTexture = rt;
        try
        {
            foreach (var view in views)
            {
                int shotsBefore = npcShots;
                MovePlayer(player, view.foot, view.target); float eye = BossHallRenderingReview.EyeHeight();
                renderCamera.transform.SetPositionAndRotation(view.foot + Vector3.up * eye, Quaternion.LookRotation(view.target - view.foot - Vector3.up * eye));
                foreach (bool on in new[] { false, true })
                {
                    foreach (var l in lamps) l.enabled = on;
                    var samples = counters.Select(_ => new List<long>()).ToArray(); var frameIntervals = new List<double>();
                    int previousFrame = -1, frames = 0; double last = EditorApplication.timeSinceStartup; deadline = last + 60;
                    while (frames < 50)
                    {
                        Require(EditorApplication.timeSinceStartup < deadline, "Profiling frames timed out");
                        if (Time.frameCount != previousFrame)
                        {
                            previousFrame = Time.frameCount; renderCamera.Render(); FrameTimingManager.CaptureFrameTimings();
                            double now = EditorApplication.timeSinceStartup;
                            if (frames >= 10)
                            {
                                frameIntervals.Add((now - last) * 1000);
                                for (int j = 0; j < recorders.Count; j++) if (recorders[j].Valid) samples[j].Add(recorders[j].LastValue);
                            }
                            last = now; frames++;
                        }
                        yield return null;
                    }
                    lines.Add($"{view.name} lighting {(on ? "on" : "off")}: 40 measured advancing Editor frames after 10 warmup; mean wall interval {frameIntervals.Average():F2} ms. " +
                        string.Join("; ", counters.Select((d, j) => d.Name + " mean " + (samples[j].Any(v => v > 0) ? samples[j].Average().ToString("F1") : "unavailable/zero") + " " + d.UnitType)));
                    var timing = new FrameTiming[1]; uint n = FrameTimingManager.GetLatestTimings(1, timing);
                    lines.Add(n > 0 && timing[0].gpuFrameTime > 0 ? $"FrameTiming GPU {timing[0].gpuFrameTime:F2} ms, CPU {timing[0].cpuFrameTime:F2} ms." : "FrameTiming GPU unavailable; no GPU timing claimed.");
                }
                File.WriteAllBytes(Output + "/" + view.name + ".png", Shot(view.foot + Vector3.up * eye, view.target));
                lines.Add(view.name + ": " + (npcShots - shotsBefore) + " observed NPC weapon shots during this view.");
                Debug.Log("ENV055: combat/frame sample captured at " + view.name);
            }
        }
        finally
        {
            foreach (var l in lamps) l.enabled = true;
            renderCamera.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); BossHallRenderingReview.DestroyCamera(renderCamera);
            foreach (var recorder in recorders) recorder.Dispose(); recorders.Clear();
        }
        lines.Add("NPC AI remained active during camera/lighting frame samples; player moved and made invincible in Play mode only. These are controlled combat-height views, not a human encounter/win-order run.");

        // Two independent real armour-break/CoreExecutioner sequences, on floor and a measured first-tier gallery.
        var executor = player.GetComponent<CoreExecutioner>(); Require(executor, "Missing CoreExecutioner");
        for (int tier = 0; tier < 2; tier++)
        {
            var target = hallTargets.Where(t => t && t.Core && t.Health && t.Health.CurrentHealth > 0 && !t.Core.IsConsumed &&
                t.GetComponents<MonoBehaviour>().Any(m => m.GetType().Name == "ArmouredEnemy")).FirstOrDefault();
            Require(target, "Missing live armoured target for tier " + tier);
            var armour = target.GetComponents<MonoBehaviour>().Single(m => m.GetType().Name == "ArmouredEnemy");
            float ratio = (float)armour.GetType().GetField("BreakHealthRatio").GetValue(armour);
            target.Health.TakeDamage(Mathf.Max(0, target.Health.CurrentHealth - target.Health.MaxHealth * (ratio - .03f)), player.gameObject);
            yield return null;
            Require(target.IsVulnerable && CoreExecutionTarget.Vulnerable.Contains(target), "Armour did not expose target");
            var foot = tier == 0 ? new Vector3(-302, 6.25f, 115) : new Vector3(-313, 10.75f, 143);
            var nav = target.GetComponent<NavMeshAgent>(); if (nav) nav.enabled = false;
            target.transform.position = foot; target.transform.rotation = Quaternion.LookRotation(Vector3.right);
            MovePlayer(player, foot + Vector3.right * 2, foot + Vector3.up);
            bool consumedOnDeath = false; var health = target.Health; var core = target.Core;
            health.OnDie += () => consumedOnDeath = core && core.IsConsumed;
            Require(executor.TryExecute(target), "Execution refused on tier " + tier);
            Require(!CoreExecutionTarget.Vulnerable.Contains(target), "Executing target remained vulnerable");
            deadline = EditorApplication.timeSinceStartup + 30; bool captured = false;
            while (executor.IsExecuting)
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Execution timeout");
                Require(!CoreExecutionTarget.Vulnerable.Contains(target), "Executing target re-entered vulnerable pool");
                if (!captured && target.Stage == CoreExecutionTarget.ExecutionStage.Impaled)
                {
                    File.WriteAllBytes(Output + "/Execution_" + (tier == 0 ? "Floor" : "Gallery") + ".png", Shot(player.PlayerCamera.transform.position,
                        player.PlayerCamera.transform.position + player.PlayerCamera.transform.forward * 5)); captured = true;
                }
                yield return null;
            }
            Require(captured && core.IsConsumed && consumedOnDeath && health.CurrentHealth <= 0, "Consumption/death order failed on tier " + tier);
            lines.Add((tier == 0 ? "Floor" : "Gallery") + ": real armour damage exposed target, real execution removed target from vulnerable pool, impaled capture saved, core consumed before Health.OnDie, execution completed.");
        }
        Require(!Enemies().Any(Decorative) && !CoreExecutionTarget.Vulnerable.Any(Decorative), "Lighting entered gameplay registries");
        lines.Add("No gameplay assets or scene saved. Full encounter, human review, player-build profiling and GPU capture (if unavailable above) remain final-signoff work.");
        BossHallLighting.Write("LightingRuntime_055.txt", string.Join("\n", lines) + "\n"); SessionState.SetBool(Key + "Ok", true);
    }
}
