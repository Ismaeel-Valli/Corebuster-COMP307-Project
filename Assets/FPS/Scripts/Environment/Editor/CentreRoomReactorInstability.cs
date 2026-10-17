using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.FPS.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Section = CentreRoomFramework.Section;

/// <summary>
/// corebuster_render_008 authoring and review: refreshes the reactor's energy materials, rebuilds the
/// room lighting that binds the flicker, and records capture strips, signal limits and a pause probe.
/// </summary>
[InitializeOnLoad]
public static class CentreRoomReactorInstability
{
    public const string Output = "ReviewCaptures/CentreRoom/render_008";
    const string ReviewTimeProperty = "_ReviewTime";
    const string Key = "CoreBuster.ReactorInstability.";
    // Photosensitivity cap from the task: no more than three full-intensity flashes in any second.
    const int FlashCapPerSecond = 3;
    // The design itself never puts two flash slots in a row, so any one second holds at most two.
    const int FlashDesignLimit = 2;
    const float FlashThreshold = 1.6f, FlashRearm = 1.3f;

    static double phaseStart;
    static int phase = -1;
    static readonly StringBuilder probe = new StringBuilder();

    static CentreRoomReactorInstability()
    {
        EditorApplication.playModeStateChanged += OnState;
        EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Active", false) && EditorApplication.isPlaying) StartProbe();
    }

    [MenuItem("Tools/CoreBuster/Build Centre Room Reactor Instability")]
    public static void BuildMenu()
    {
        var layout = CentreRoomFramework.MeasureLayout();
        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build Centre Room Reactor Instability");
        try
        {
            Build(layout);
            Undo.CollapseUndoOperations(undo);
        }
        catch
        {
            Undo.RevertAllDownToGroup(undo);
            throw;
        }
    }

    /// <summary>Graphics batch (omit -nographics): build, save, reload, validate, analyse and capture.</summary>
    public static void BuildBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(Output);
        if (!File.Exists(Output + "/Before/South_Entry.png"))
            throw new InvalidOperationException("Run CaptureBeforeBatch against the unchanged scene and shader first.");
        var layout = CentreRoomFramework.MeasureLayout();
        var before = CentreRoomLighting.Snapshot();
        Build(layout);
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Scene save failed.");
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        layout = CentreRoomFramework.MeasureLayout();
        CentreRoomLighting.Validate(layout);
        CentreRoomReactor.RequireValid(layout);
        RequirePersisted();
        var after = CentreRoomLighting.Snapshot();
        var changed = before.Where(p => !after.TryGetValue(p.Key, out var v) || v != p.Value).ToList();
        if (before.Count != after.Count || changed.Count > 0)
        {
            File.WriteAllLines(Output + "/Isolation_Differences.txt", changed.Select(p => p.Key + "\nBEFORE: " + p.Value +
                "\nAFTER: " + (after.TryGetValue(p.Key, out var v) ? v : "missing")));
            throw new InvalidOperationException("The pass changed protected scene state; see Isolation_Differences.txt.");
        }
        File.WriteAllText(Output + "/Signal.txt", AnalyseSignal());
        Capture(layout, "After");
        File.WriteAllText(Output + "/Comparison.txt", Compare());
        Debug.Log($"REACTOR INSTABILITY BUILD VERIFIED: saved/reloaded; {before.Count} transforms and protected component states " +
            "outside Lighting unchanged. Owner Play-mode comfort review still required.");
    }

    static void Build(CentreRoomFramework.Layout layout)
    {
        // The rings render through the lighting pass's copies, so refresh the sources first.
        CentreRoomReactor.RefreshEnergyMaterials();
        CentreRoomLighting.Build(layout);
        CentreRoomLighting.Validate(layout);
        CentreRoomReactor.RequireValid(layout);
        string signal = AnalyseSignal();
        if (signal.Contains("FAIL")) throw new InvalidOperationException(signal);
    }

    /// <summary>Graphics batch against the unchanged scene: the reference strip for comparison.</summary>
    public static void CaptureBeforeBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        Capture(CentreRoomFramework.MeasureLayout(), "Before");
    }

    [MenuItem("Tools/CoreBuster/Capture Centre Room Reactor Instability")]
    public static void CaptureMenu()
    {
        Capture(CentreRoomFramework.MeasureLayout(), "After");
        string signal = AnalyseSignal();
        File.WriteAllText(Output + "/Signal.txt", signal);
        if (signal.Contains("FAIL")) throw new InvalidOperationException(signal);
        if (File.Exists(Output + "/Before/South_Entry.png")) File.WriteAllText(Output + "/Comparison.txt", Compare());
    }

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    static void RequirePersisted()
    {
        Material field = CentreRoomReactor.FieldMaterial;
        var errors = new List<string>();
        if (field.shader.name != "CoreBuster/Reactor Containment" || !field.HasProperty(ReactorInstabilityLight.InstabilityProperty) ||
            Mathf.Abs(field.GetFloat(ReactorInstabilityLight.InstabilityProperty) - 1) > .001f || field.GetFloat(ReviewTimeProperty) >= 0 ||
            field.GetColor("_Color").b < .9f || field.GetFloat("_Intensity") < 2)
            errors.Add("M_Reactor_Field lost the blue palette, intensity or live-time settings.");
        var reactor = CentreRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.Reactor);
        foreach (var r in reactor.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("Induction_Ring_")))
        {
            Color emission = r.sharedMaterial.GetColor("_EmissionColor");
            if (emission.b < 2 || emission.r > emission.b * .4f) errors.Add(r.name + " does not carry the saved blue emission copy.");
        }
        if (FieldRenderers().Length != 1) errors.Add("Expected one saved Contained_Field renderer using M_Reactor_Field.");
        if (errors.Count > 0) throw new InvalidOperationException("REACTOR INSTABILITY PERSISTENCE FAILED:\n" + string.Join("\n", errors));
    }

    static Renderer[] FieldRenderers()
    {
        Material field = CentreRoomReactor.FieldMaterial;
        var reactor = CentreRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.Reactor);
        return reactor.GetComponentsInChildren<Renderer>(true).Where(r => r.sharedMaterial == field).ToArray();
    }

    static ReactorInstabilityLight[] DrivenLights() =>
        CentreRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.Lighting).GetComponentsInChildren<ReactorInstabilityLight>(true);

    /// <summary>Samples the shared signal at 1 kHz for an hour of play; fails if any second exceeds the flash cap.</summary>
    static string AnalyseSignal()
    {
        const float step = .001f, duration = 3600;
        int flashes = 0, worstWindow = 0;
        var flashTimes = new List<float>();
        bool armed = true;
        float low = float.MaxValue, high = float.MinValue, steadyError = 0;
        for (int i = 0; i * step < duration; i++)
        {
            float t = i * step, s = ReactorInstabilityLight.Surge(t, 1);
            low = Mathf.Min(low, s); high = Mathf.Max(high, s);
            steadyError = Mathf.Max(steadyError, Mathf.Abs(ReactorInstabilityLight.Surge(t, 0) - 1));
            if (armed && s > FlashThreshold) { flashes++; flashTimes.Add(t); armed = false; }
            else if (!armed && s < FlashRearm) armed = true;
        }
        for (int a = 0, b = 0; b < flashTimes.Count; b++)
        {
            while (flashTimes[b] - flashTimes[a] >= 1) a++;
            worstWindow = Mathf.Max(worstWindow, b - a + 1);
        }
        var gaps = flashTimes.Zip(flashTimes.Skip(1), (x, y) => y - x).ToArray();
        float mean = gaps.Length > 0 ? gaps.Average() : 0;
        float deviation = gaps.Length > 1 ? Mathf.Sqrt(gaps.Select(g => (g - mean) * (g - mean)).Average()) : 0;
        bool pass = worstWindow <= FlashDesignLimit && steadyError < 1e-5f && flashes > 0 && deviation > .25f * mean;
        var lights = SceneManager.GetActiveScene().IsValid() && CentreRoomFramework.FindSection(SceneManager.GetActiveScene(), Section.Lighting)
            ? DrivenLights() : new ReactorInstabilityLight[0];
        string lightRanges = string.Join("\n", lights.Select(l => FormattableString.Invariant(
            $"- {l.name}: factor {l.Factor(0, 1):F2} at t=0; clamped to [{Mathf.Max(l.Minimum, low):F2}, {Mathf.Min(l.Maximum, high):F2}] of base intensity")));
        return FormattableString.Invariant($@"{(pass ? "PASS" : "FAIL")}: reactor surge signal over {duration:F0} s at 1 kHz (instability 1)
Surge range: {low:F3} to {high:F3} (1 = idle); instability 0 steady to within {steadyError:E1}.
Flashes (rising above {FlashThreshold} after falling below {FlashRearm}): {flashes} total, {flashes / duration:F2}/s mean, worst 1 s window {worstWindow} (design limit {FlashDesignLimit}, task cap {FlashCapPerSecond}).
Event slots: {ReactorInstabilityLight.EventSlotsPerSecond}/s hard ceiling; flash gap mean {mean:F2} s, standard deviation {deviation:F2} s (irregular when well above 0).
Driven lights:
{lightRanges}
");
    }

    static List<(string name, Vector3 eye, Vector3 target, bool strip)> Views(CentreRoomFramework.Layout l)
    {
        Vector3 c = l.RingCenter;
        var views = new List<(string, Vector3, Vector3, bool)> {
            ("South_Entry", new Vector3(-191.955f, l.Floor + 1.7f, l.interior.min.z + 1.4f), c + Vector3.up * 3.4f, true),
            ("West_Approach", c + new Vector3(-12, 1.7f, -5), c + Vector3.up * 3.4f, true),
            ("North_Approach", c + new Vector3(5, 1.7f, 12), c + Vector3.up * 3.4f, true),
            ("Containment_Detail", c + new Vector3(5.5f, 2.6f, -6), c + Vector3.up * 4, true),
            ("Circulation_Overview", c + new Vector3(9, 9.8f, -11), c + Vector3.up * 1.7f, true),
            ("Terminal", l.report.terminalStanding.position + Vector3.up * 1.6f, l.report.terminalFocus.position, true) };
        foreach (var gate in l.report.gates)
        {
            views.Add(("Gate_" + gate.name, gate.position - gate.outward * 5 + Vector3.up * 1.6f, gate.position + Vector3.up * 1.5f, false));
            views.Add(("Neighbour_" + gate.name, gate.position + gate.outward * 3 + Vector3.up * 1.6f,
                gate.position + gate.outward * 9 + Vector3.up * 1.6f, false));
            views.Add(("Boundary_" + gate.name, gate.position + gate.outward * 3 + Vector3.up * 1.6f, gate.position + Vector3.up * 1.6f, false));
        }
        return views;
    }

    /// <summary>Deterministic review instants from the first minute: the dimmest, quartiles, idle and the brightest flash.</summary>
    static List<(string label, float time)> ReviewTimes()
    {
        var samples = Enumerable.Range(0, 60 * 240).Select(i => i / 240f).Select(t => (t, s: ReactorInstabilityLight.Surge(t, 1)))
            .OrderBy(p => p.s).ToArray();
        (string, float) At(string label, float quantile) => (label, samples[Mathf.Clamp(Mathf.RoundToInt(quantile * (samples.Length - 1)), 0, samples.Length - 1)].t);
        return new List<(string, float)> { At("1_Dropout", 0), At("2_Low", .25f), At("3_Median", .5f), At("4_High", .75f), At("5_Flash", 1) };
    }

    static void Capture(CentreRoomFramework.Layout layout, string phaseName)
    {
        string folder = Output + "/" + phaseName;
        Directory.CreateDirectory(folder);
        var go = new GameObject("Reactor instability review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 65; camera.nearClipPlane = .05f; camera.farClipPlane = 180;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        bool after = phaseName != "Before";
        var fields = after ? FieldRenderers() : new Renderer[0];
        var driven = after ? DrivenLights() : new ReactorInstabilityLight[0];
        var lights = driven.Select(d => d.GetComponent<Light>()).ToArray();
        float[] bases = lights.Select(l => l.intensity).ToArray();
        float instability = ReactorInstabilityLight.InstabilityOf(CentreRoomReactor.FieldMaterial);
        var block = new MaterialPropertyBlock();
        var times = after ? ReviewTimes() : new List<(string, float)> { ("Live", -1) };
        var log = new StringBuilder();
        try
        {
            foreach (var (label, time) in times)
            {
                if (after)
                {
                    // Pin the shader and the lights to the same instant; nothing here is serialized.
                    block.SetFloat(ReviewTimeProperty, time);
                    foreach (var r in fields) r.SetPropertyBlock(block);
                    for (int i = 0; i < lights.Length; i++) lights[i].intensity = bases[i] * driven[i].Factor(time, instability);
                    log.AppendLine(FormattableString.Invariant($"{label}: t={time:F3} s, surge {ReactorInstabilityLight.Surge(time, instability):F2}; ") +
                        string.Join(", ", lights.Select(l => FormattableString.Invariant($"{l.name} {l.intensity:F0}"))));
                }
                foreach (var view in Views(layout))
                {
                    // Every time point for the reactor views; gate/neighbour views only at the brightest instant (worst spill).
                    if (after && !view.strip && label != "5_Flash") continue;
                    string file = after && view.strip ? $"{folder}/{view.name}_{label}.png" : $"{folder}/{view.name}.png";
                    Render(camera, view.eye, view.target, file);
                }
            }
        }
        finally
        {
            for (int i = 0; i < lights.Length; i++) lights[i].intensity = bases[i];
            foreach (var r in fields) r.SetPropertyBlock(null);
            Object.DestroyImmediate(go);
        }
        if (after) File.WriteAllText(folder + "/Review_Times.txt", log.ToString());
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Capture dirtied the scene; do not save.");
        Debug.Log("Reactor instability captures: " + folder);
    }

    static void Render(Camera camera, Vector3 eye, Vector3 target, string file)
    {
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = rt; camera.Render(); camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(file, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture);
        }
    }

    static Color32[] Pixels(string file)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        try { texture.LoadImage(File.ReadAllBytes(file)); return texture.GetPixels32(); }
        finally { Object.DestroyImmediate(texture); }
    }

    // Mean linear-ish colour over the central box where the reactor sits in the approach views.
    static Vector4 Stats(Color32[] pixels, bool centre)
    {
        double r = 0, g = 0, b = 0; int n = 0;
        for (int y = 0; y < 720; y++)
            for (int x = 0; x < 1280; x++)
            {
                if (centre && (x < 448 || x >= 832 || y < 144 || y >= 576)) continue;
                var p = pixels[y * 1280 + x]; r += p.r; g += p.g; b += p.b; n++;
            }
        r /= n; g /= n; b /= n;
        return new Vector4((float)r, (float)g, (float)b, (float)(.2126 * r + .7152 * g + .0722 * b));
    }

    static string Compare()
    {
        var text = new StringBuilder("Before = saved scene before corebuster_render_008 (live shader time); After = pinned review instants.\n");
        text.AppendLine("Reactor region (central 30% x 60%): mean R, G, B, luminance (0-255).");
        foreach (string view in new[] { "South_Entry", "West_Approach", "North_Approach", "Containment_Detail" })
        {
            var before = Stats(Pixels($"{Output}/Before/{view}.png"), true);
            text.AppendLine(FormattableString.Invariant($"- {view} before: {before.x:F1}, {before.y:F1}, {before.z:F1}, L {before.w:F1}"));
            foreach (var (label, _) in ReviewTimes())
            {
                var after = Stats(Pixels($"{Output}/After/{view}_{label}.png"), true);
                text.AppendLine(FormattableString.Invariant($"  after {label}: {after.x:F1}, {after.y:F1}, {after.z:F1}, L {after.w:F1}"));
            }
        }
        text.AppendLine("Whole-frame luminance, before vs dimmest instant (readability floor):");
        foreach (string view in new[] { "South_Entry", "West_Approach", "North_Approach", "Terminal", "Circulation_Overview" })
        {
            float before = Stats(Pixels($"{Output}/Before/{view}.png"), false).w, dim = Stats(Pixels($"{Output}/After/{view}_1_Dropout.png"), false).w;
            text.AppendLine(FormattableString.Invariant($"- {view}: {before:F1} -> {dim:F1}"));
        }
        text.AppendLine("Gate, neighbour and boundary views at the brightest flash: pixels differing by more than 2/255 in any channel.");
        foreach (string file in Directory.GetFiles(Output + "/Before", "*.png").Select(Path.GetFileName)
            .Where(f => f.StartsWith("Gate_") || f.StartsWith("Neighbour_") || f.StartsWith("Boundary_")).OrderBy(f => f))
        {
            var a = Pixels(Output + "/Before/" + file); var b = Pixels(Output + "/After/" + file);
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i].r - b[i].r) > 2 || Math.Abs(a[i].g - b[i].g) > 2 || Math.Abs(a[i].b - b[i].b) > 2) changed++;
            text.AppendLine($"- {file}: {changed} of {a.Length}");
        }
        return text.ToString();
    }

    // Play-mode probe. Run without -quit and without -nographics; Unity exits when it finishes.
    public static void PlayProbeBatch()
    {
        EditorSceneManager.OpenScene(CentreRoomSurvey.ScenePath, OpenSceneMode.Single);
        SessionState.SetString(Key + "Scene", SceneHash());
        SessionState.SetBool(Key + "Active", true);
        SessionState.SetInt(Key + "Result", 1);
        EditorApplication.EnterPlaymode();
    }

    static void StartProbe()
    {
        phase = 0; phaseStart = EditorApplication.timeSinceStartup; probe.Clear();
    }

    static void OnState(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) StartProbe();
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            int result = SessionState.GetInt(Key + "Result", 1);
            if (SceneHash() != SessionState.GetString(Key + "Scene", "")) { Debug.LogError("Reactor probe modified the saved scene."); result = 1; }
            SessionState.EraseBool(Key + "Active");
            SessionState.EraseString(Key + "Scene");
            if (Application.isBatchMode) EditorApplication.Exit(result);
        }
    }

    // Phases: 0 settle; 1 running (levels must move); 2 time scale 0 (levels and time must hold); 3 resumed.
    static int frameAtPhase;
    static float timeAtPhase, shaderTimeAtPhase;
    static float[] lowSeen, highSeen;

    static void Tick()
    {
        if (phase < 0 || !EditorApplication.isPlaying) return;
        // Batch mode has no Game view to pump frames; advance one paused frame per editor tick.
        if (Application.isBatchMode)
        {
            EditorApplication.isPaused = true;
            EditorApplication.Step();
        }
        double elapsed = EditorApplication.timeSinceStartup - phaseStart;
        try
        {
            var driven = Object.FindObjectsByType<ReactorInstabilityLight>();
            var lights = driven.Select(d => d.GetComponent<Light>()).ToArray();
            float[] now = lights.Select(l => l.intensity).ToArray();
            if (phase == 0)
            {
                if (elapsed < 3) return;
                if (driven.Length != 3) throw new InvalidOperationException($"Expected three driven reactor lights, found {driven.Length}.");
                Next(1, now);
                return;
            }
            for (int i = 0; i < now.Length; i++) { lowSeen[i] = Mathf.Min(lowSeen[i], now[i]); highSeen[i] = Mathf.Max(highSeen[i], now[i]); }
            if (elapsed < 4) return;
            int frames = Time.frameCount - frameAtPhase;
            float shaderTime = Shader.GetGlobalVector("_Time").y;
            string lightsText = string.Join(", ", lights.Select((l, i) => FormattableString.Invariant($"{l.name} {lowSeen[i]:F0}-{highSeen[i]:F0}")));
            if (frames < 20) throw new InvalidOperationException($"Play mode advanced only {frames} frames in phase {phase}; the probe is inconclusive.");
            if (phase == 1 || phase == 3)
            {
                if (lights.Select((l, i) => highSeen[i] - lowSeen[i]).Any(range => range < 1))
                    throw new InvalidOperationException("A reactor light did not flicker while running: " + lightsText);
                probe.AppendLine(FormattableString.Invariant(
                    $"{(phase == 1 ? "Running" : "Resumed")}: {frames} frames, Time.time {timeAtPhase:F2} -> {Time.time:F2}, shader _Time.y {shaderTimeAtPhase:F2} -> {shaderTime:F2}; {lightsText}"));
            }
            if (phase == 2)
            {
                if (Math.Abs(Time.time - timeAtPhase) > 1e-4f || lights.Select((l, i) => highSeen[i] - lowSeen[i]).Any(range => range > 1e-3f) ||
                    Math.Abs(shaderTime - shaderTimeAtPhase) > 1e-3f)
                    throw new InvalidOperationException(FormattableString.Invariant(
                        $"Time scale 0 did not hold the reactor: Time.time {timeAtPhase:F3} -> {Time.time:F3}, _Time.y {shaderTimeAtPhase:F3} -> {shaderTime:F3}; {lightsText}"));
                probe.AppendLine(FormattableString.Invariant(
                    $"Paused (time scale 0): {frames} frames, Time.time held at {Time.time:F3}, shader _Time.y held at {shaderTime:F3}; {lightsText}"));
            }
            if (phase == 1) { Time.timeScale = 0; Next(2, now); return; }
            if (phase == 2) { Time.timeScale = 1; Next(3, now); return; }
            string result = "REACTOR INSTABILITY PLAY-MODE PROBE PASSED\n" + probe +
                "This is a pause/time-scale and flicker probe, not an owner comfort or two-wave review.";
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/Play_Probe.txt", result);
            Debug.Log(result);
            SessionState.SetInt(Key + "Result", 0);
            Finish();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SessionState.SetInt(Key + "Result", 1);
            Finish();
        }
    }

    static void Next(int nextPhase, float[] now)
    {
        phase = nextPhase; phaseStart = EditorApplication.timeSinceStartup;
        frameAtPhase = Time.frameCount; timeAtPhase = Time.time; shaderTimeAtPhase = Shader.GetGlobalVector("_Time").y;
        lowSeen = (float[])now.Clone(); highSeen = (float[])now.Clone();
    }

    static void Finish()
    {
        phase = -1; Time.timeScale = 1;
        EditorApplication.ExitPlaymode();
    }

    static string SceneHash()
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(CentreRoomSurvey.ScenePath))
            return BitConverter.ToString(sha.ComputeHash(stream));
    }
}
