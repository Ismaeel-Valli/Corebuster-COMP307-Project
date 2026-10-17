using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using static StartWorkshopDresser;

/// <summary>
/// corebuster_audio_001. Gives the Start Workshop an ambient sound bed in the rebuildable <see cref="GroupName"/> group:
/// <list type="bullet">
/// <item>a looping, non-directional room tone (air handling and mains hum) over the room;</item>
/// <item>spatial sources on the things that would make noise: the canister, fuseboxes and centre machines, the robot arm,
/// the task lamp's fluorescent tube, the duct vents (and any wall fan the room gains), and two pipe drops;</item>
/// <item>two muffled "distant machinery" loops near the south and west walls, heard as if through them.</item>
/// </list>
/// Every clip is synthesised here from filtered noise and tones into <see cref="AudioFolder"/> (first-principles audio, no
/// import), looped seamlessly, and every source is routed through the Ambient group under Master in MainAudioMixer, so
/// the player's master volume setting applies. Each source has a custom rolloff that reaches silence at its own maximum
/// distance (a logarithmic curve never does), so nothing is heard across the map. The clips are deliberately free of
/// sharp transients and high frequencies, and the loops have different lengths with detuned pitches, so the bed does not
/// audibly repeat or fatigue. No music, runtime script or gameplay object is touched.
/// </summary>
public static class StartWorkshopAmbience
{
    public const string GroupName = "Ambience_001";
    const string UndoName = "Build Start Workshop Ambience";
    public const string AudioFolder = "Assets/FPS/Audio/SFX/Workshop";
    const string MixerPath = "Assets/FPS/Audio/MainAudioMixer.mixer";
    const string MixerGroupName = "Ambient";
    const string ReportPath = "ReviewCaptures/StartWorkshop/Ambience_001/Report.txt";
    const int SampleRate = 44100;

    // Loudness. Every clip is normalised to the same RMS, so a source's volume is its mix level.
    const float ClipRms = .1f, ClipPeak = .8f, DistantPeak = .45f;
    // A source may never be louder than this, nor audible further than this from where it stands.
    const float MaxVolume = .7f, MaxRadius = 34f;
    const int MinSources = 10;

    const string RoomTone = "Workshop_RoomTone_Loop", Transformer = "Workshop_TransformerHum_Loop", Motor = "Workshop_MotorDrone_Loop",
        Fan = "Workshop_FanWhir_Loop", Tube = "Workshop_TubeBuzz_Loop", Pipe = "Workshop_PipeHiss_Loop", Distant = "Workshop_DistantMachinery_Loop";

    // Where a source stands: on a named prop of the room (by path under the room, or by unique name), or at a point of
    // the room measured from its frame.
    struct Spec
    {
        public string Name, Clip, Anchor;
        public Func<StartWorkshopFrame, Vector3> At;
        public float Volume, Pitch, Min, Max, Spread;
    }

    static Spec On(string name, string clip, string anchor, float volume, float pitch, float min, float max, float spread = 60f) =>
        new Spec { Name = name, Clip = clip, Anchor = anchor, Volume = volume, Pitch = pitch, Min = min, Max = max, Spread = spread };

    static Spec At(string name, string clip, Func<StartWorkshopFrame, Vector3> at, float volume, float pitch, float min, float max, float spread) =>
        new Spec { Name = name, Clip = clip, At = at, Volume = volume, Pitch = pitch, Min = min, Max = max, Spread = spread };

    static readonly Spec[] Specs =
    {
        // Non-directional bed hung from the ceiling over the room centre: it fills the room and falls away past the doorway.
        At("RoomTone_Centre", RoomTone, f => new Vector3(f.Center.x, f.Ceiling - 2f, f.Center.z), .6f, 1f, 12f, 34f, 180f),

        // Muffled machinery beyond the south and west walls (the sources stand just inside them, high up).
        At("Distant_South", Distant, f => new Vector3(f.Center.x, f.Ceiling - 1.5f, f.South + .6f), .6f, 1f, 8f, 30f, 120f),
        At("Distant_West", Distant, f => new Vector3(f.West + .6f, f.Ceiling - 1.5f, f.Center.z + 6f), .45f, .92f, 8f, 30f, 120f),

        // Electrical hum: the canister, the two fuseboxes and the centre battery (pitches differ so they do not beat together).
        On("Canister_Hum", Transformer, "Maintenance_EastWall_Detail/Power_Canister", .55f, 1f, 1.5f, 14f),
        On("Fusebox_A_Hum", Transformer, "Services_RearWall/Fusebox_A", .4f, 1.06f, 1f, 10f),
        On("Fusebox_B_Hum", Transformer, "Services_RearWall/Fusebox_B", .4f, .94f, 1f, 10f),
        On("Battery_Hum", Transformer, "Machine_Battery", .4f, .8f, 1.5f, 11f),

        // Moving machinery: the centre rotor and the robot arm's servos.
        On("Rotor_Drone", Motor, "Machine_Rotor", .55f, 1f, 2f, 14f),
        On("Arm_Servo_Hum", Motor, "Workstation_010/Robot_Arm_Cell/Robot_Arm", .35f, 1.4f, 1.5f, 10f),

        // The task lamp's fluorescent tube over the bench.
        On("Task_Tube_Buzz", Tube, "Workstation_010/Task_Lamp/Tube", .5f, 1f, 1f, 9f),

        // Air through the ceiling duct vents, and pressure leaking from two pipe drops.
        On("Duct_Vent_East_Air", Fan, "Duct_Vent_East", .5f, 1f, 1.5f, 14f),
        On("Duct_Vent_West_Air", Fan, "Duct_Vent_West", .5f, .93f, 1.5f, 14f),
        On("Pipe_Drop_West_Hiss", Pipe, "Pipework_009/Drop_West", .35f, 1f, 1.5f, 10f),
        On("Pipe_Drop_East_Hiss", Pipe, "Pipework_009/Drop_East", .35f, .95f, 1.5f, 10f),
    };

    // A kit Wall Fan the room gains (corebuster_env_017) is given a whir of its own.
    const string WallFanPrefix = "Wall Fan";
    const float WallFanVolume = .5f, WallFanMin = 1.5f, WallFanMax = 14f;

    static readonly string[] ClipNames = { RoomTone, Transformer, Motor, Fan, Tube, Pipe, Distant };

    // ---------------------------------------------------------------------------------------------------------------
    // Menu and batch entry points

    [MenuItem("Tools/CoreBuster/Build Start Workshop Ambience")]
    public static void Rebuild() => RunMenu(false);

    [MenuItem("Tools/CoreBuster/Regenerate Start Workshop Ambience Clips")]
    public static void RebuildAndRegenerate() => RunMenu(true);

    static void RunMenu(bool regenerate)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var level = scene.path == ScenePath ? FindLevel(scene) : null;
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"Open {ScenePath} and run Tools/CoreBuster/Dress Start Workshop first; the ambience belongs under {LevelName}/{ContainerName}.");
            return;
        }
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        RebuildIn(scene, root, regenerate);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeTransform = root.Find(GroupName);
        StartWorkshopChecks.Run();
    }

    /// <summary>
    /// Batch: Unity.exe -batchmode -nographics -projectPath &lt;path&gt; -executeMethod StartWorkshopAmbience.BuildBatch -quit
    /// [-regenerate]. Opens MainScene, rebuilds the ambience, saves, validates, and writes <see cref="ReportPath"/>.
    /// </summary>
    public static void BuildBatch()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var level = FindLevel(scene);
        var root = level != null ? level.Find(ContainerName) : null;
        if (root == null)
        {
            Debug.LogError($"No {LevelName}/{ContainerName} in {ScenePath}; run Tools/CoreBuster/Dress Start Workshop first.");
            EditorApplication.Exit(1);
            return;
        }
        RebuildIn(scene, root, Environment.GetCommandLineArgs().Contains("-regenerate"));
        EditorSceneManager.SaveScene(scene);
        bool ok = StartWorkshopChecks.Run();
        WriteReport(root, StartWorkshopFrame.Measure(scene, root));
        if (!ok) EditorApplication.Exit(1);
    }

    static void RebuildIn(Scene scene, Transform root, bool regenerate)
    {
        var existing = root.Find(GroupName);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        if (regenerate)
        {
            foreach (var name in ClipNames)
                AssetDatabase.DeleteAsset($"{AudioFolder}/{name}.wav");
        }
        var f = StartWorkshopFrame.Measure(scene, root);
        Build(root, f);
        ApplyStaticConvention(root.Find(GroupName), GameObjectUtility.GetStaticEditorFlags(root.parent.gameObject));
    }

    public static bool IsGroup(Transform group) => group != null && group.name == GroupName;

    // ---------------------------------------------------------------------------------------------------------------
    // Scene build

    /// <summary>Called by the full workshop rebuild after the props, and by the isolated menu action.</summary>
    public static void Build(Transform root, StartWorkshopFrame f)
    {
        if (root.Find(GroupName) != null) throw new InvalidOperationException(GroupName + " already exists.");
        Physics.SyncTransforms();
        var clips = LoadClips();
        var output = FindMixerGroup() ?? throw new InvalidOperationException($"Master/{MixerGroupName} is missing from {MixerPath}.");
        var group = Group(root, GroupName);
        Undo.RegisterCreatedObjectUndo(group.gameObject, UndoName);
        try
        {
            foreach (var spec in Specs)
                AddSource(group, spec.Name, clips[spec.Clip], output, Position(root, group, f, spec), spec.Volume, spec.Pitch, spec.Min, spec.Max, spec.Spread);
            foreach (var fan in WallFans(root, group))
                AddSource(group, fan.name + "_Whir", clips[Fan], output, B(fan.gameObject).center, WallFanVolume, 1f + .03f * fan.GetSiblingIndex(), WallFanMin, WallFanMax, 60f);
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(group.gameObject);
            throw;
        }
        Debug.Log($"{GroupName}: {group.childCount} looping sources on {MixerGroupName}.\n{Levels(group, f)}");
    }

    static Vector3 Position(Transform root, Transform group, StartWorkshopFrame f, Spec spec)
    {
        if (spec.At != null) return spec.At(f);
        return B(Resolve(root, group, spec.Anchor).gameObject).center;
    }

    static Transform Resolve(Transform root, Transform group, string anchor)
    {
        var found = anchor.Contains("/") ? root.Find(anchor) : FindByName(root, anchor, group);
        return found ?? throw new InvalidOperationException(
            $"Missing {anchor}; run Tools/CoreBuster/Dress Start Workshop (or the pass that builds it) first.");
    }

    static Transform FindByName(Transform parent, string name, Transform skip)
    {
        foreach (Transform child in parent)
        {
            if (child == skip) continue;
            if (child.name == name) return child;
            var deeper = FindByName(child, name, skip);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // Outermost kit wall fans in the room: each one, not its parts.
    static IEnumerable<Transform> WallFans(Transform root, Transform group)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith(WallFanPrefix, StringComparison.Ordinal) || t.IsChildOf(group)) continue;
            bool nested = false;
            for (var p = t.parent; p != null && p != root; p = p.parent)
                if (p.name.StartsWith(WallFanPrefix, StringComparison.Ordinal)) nested = true;
            if (!nested && t.GetComponentInChildren<MeshRenderer>() != null) yield return t;
        }
    }

    static void AddSource(Transform group, string name, AudioClip clip, AudioMixerGroup output, Vector3 position,
        float volume, float pitch, float min, float max, float spread)
    {
        var go = new GameObject(name);
        go.transform.SetParent(group, false);
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.outputAudioMixerGroup = output;
        source.loop = true;
        source.playOnAwake = true;
        source.volume = volume;
        source.pitch = pitch;
        source.spatialBlend = 1f;
        source.dopplerLevel = 0f;
        source.spread = spread;
        // Ambience yields to gameplay sound when the voice limit is reached (256 is the lowest priority).
        source.priority = 200;
        source.minDistance = min;
        source.maxDistance = max;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff(min, max));
    }

    /// <summary>
    /// Inverse-distance falloff past the minimum distance, faded to exactly zero at the maximum. The x axis is distance as a
    /// fraction of the maximum.
    /// </summary>
    static AnimationCurve Rolloff(float min, float max)
    {
        float minFraction = min / max;
        var keys = new List<Keyframe> { new Keyframe(0f, 1f), new Keyframe(minFraction, 1f) };
        const int steps = 10;
        for (int k = 1; k <= steps; k++)
        {
            float x = Mathf.Lerp(minFraction, 1f, k / (float)steps);
            float t = (x - minFraction) / (1f - minFraction);
            keys.Add(new Keyframe(x, Mathf.Min(1f, minFraction / x) * (1f - t * t)));
        }
        var curve = new AnimationCurve(keys.ToArray());
        for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }

    static AudioMixerGroup FindMixerGroup()
    {
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (mixer == null) return null;
        return mixer.FindMatchingGroups("Master/" + MixerGroupName).FirstOrDefault(g => g.name == MixerGroupName);
    }

    static Dictionary<string, AudioClip> LoadClips()
    {
        var clips = new Dictionary<string, AudioClip>();
        Directory.CreateDirectory(AudioFolder);
        foreach (var (name, synth) in Synths())
        {
            string path = $"{AudioFolder}/{name}.wav";
            // A clip already on disk is kept, so it can be replaced by hand; Regenerate deletes it first.
            if (!File.Exists(path))
            {
                WriteWav(path, synth());
                AssetDatabase.ImportAsset(path);
            }
            clips[name] = AssetDatabase.LoadAssetAtPath<AudioClip>(path)
                ?? throw new InvalidOperationException("Could not import " + path);
        }
        return clips;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Validation

    /// <summary>
    /// The group exists with at least <see cref="MinSources"/> sources. Each one loops, plays on awake, is fully spatial, on a
    /// project-owned clip from <see cref="AudioFolder"/>, routed through Master/Ambient, no louder than
    /// <see cref="MaxVolume"/>, on a custom rolloff that falls to silence within <see cref="MaxRadius"/>, inside the room, and
    /// standing on its anchor. The group holds no collider or renderer.
    /// </summary>
    public static void Validate(Transform root, StartWorkshopFrame f, List<string> errors)
    {
        var group = root.Find(GroupName);
        if (group == null)
        {
            errors.Add(GroupName + " is missing; run Tools/CoreBuster/Build Start Workshop Ambience.");
            return;
        }
        if (group.GetComponentsInChildren<Collider>(true).Length > 0) errors.Add(GroupName + " must have no colliders.");
        if (group.GetComponentsInChildren<Renderer>(true).Length > 0) errors.Add(GroupName + " must have no renderers.");
        var sources = group.GetComponentsInChildren<AudioSource>(true);
        if (sources.Length < MinSources) errors.Add($"{GroupName} has {sources.Length} sources; at least {MinSources} are expected.");
        var output = FindMixerGroup();
        if (output == null) errors.Add($"Master/{MixerGroupName} is missing from {MixerPath}.");

        foreach (var s in sources)
        {
            string name = $"{GroupName}/{s.name}";
            string clipPath = s.clip != null ? AssetDatabase.GetAssetPath(s.clip) : "";
            if (!clipPath.StartsWith(AudioFolder + "/", StringComparison.Ordinal))
                errors.Add($"{name} must play a project-owned clip from {AudioFolder}.");
            if (!s.loop || !s.playOnAwake) errors.Add($"{name} must loop and play on awake.");
            if (output != null && s.outputAudioMixerGroup != output) errors.Add($"{name} must be routed through Master/{MixerGroupName}.");
            if (!Mathf.Approximately(s.spatialBlend, 1f)) errors.Add($"{name} must be fully spatial.");
            if (s.volume > MaxVolume) errors.Add($"{name} volume {s.volume:F2} exceeds {MaxVolume:F2}.");
            if (s.maxDistance > MaxRadius) errors.Add($"{name} is audible to {s.maxDistance:F0} m; the most allowed is {MaxRadius:F0} m.");
            var curve = s.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            if (s.rolloffMode != AudioRolloffMode.Custom || curve == null || curve.length < 2 || curve.Evaluate(1f) > .01f)
                errors.Add($"{name} needs a custom rolloff that falls to silence at its maximum distance.");
            Vector3 p = s.transform.position;
            if (p.x < f.West || p.x > f.East || p.z < f.South || p.z > f.North || p.y < f.Floor || p.y > f.Ceiling)
                errors.Add($"{name} stands outside the room at {p:F2}.");
        }

        foreach (var spec in Specs)
        {
            var source = group.Find(spec.Name);
            if (source == null) { errors.Add($"{GroupName}/{spec.Name} is missing."); continue; }
            if (spec.At != null) continue;
            var anchor = Resolve(root, group, spec.Anchor);
            if (Vector3.Distance(source.position, B(anchor.gameObject).center) > .05f)
                errors.Add($"{GroupName}/{spec.Name} is not on its anchor {spec.Anchor}.");
        }
        foreach (var fan in WallFans(root, group))
            if (group.Find(fan.name + "_Whir") == null) errors.Add($"{fan.name} has no whir; rebuild {GroupName}.");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Evidence: what the player is expected to hear where, and the clips' own measurements

    static readonly (string name, Func<StartWorkshopFrame, Vector3> at)[] Listeners =
    {
        ("Spawn", f => f.Spawn + Vector3.up * 1.44f),
        ("Workbench (east wall)", f => new Vector3(f.East - 1.5f, f.Floor + 1.44f, f.Center.z)),
        ("Doorway", f => f.RouteEnd + Vector3.up * 1.44f),
        ("Corridor, 10 m past the exit", f => f.RouteEnd + new Vector3(0, 1.44f, 10f)),
        ("10 m past the west wall", f => new Vector3(f.West - 10f, f.Floor + 1.44f, f.Center.z)),
        ("10 m past the east wall", f => new Vector3(f.East + 10f, f.Floor + 1.44f, f.Center.z)),
        ("10 m past the south wall", f => new Vector3(f.Center.x, f.Floor + 1.44f, f.South - 10f)),
    };

    static float Gain(AudioSource s, Vector3 listener)
    {
        float d = Vector3.Distance(s.transform.position, listener);
        if (d >= s.maxDistance) return 0f;
        return s.volume * Mathf.Max(0f, s.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(d / s.maxDistance));
    }

    static float ToDb(float linear) => 20f * Mathf.Log10(Mathf.Max(linear, 1e-5f));

    /// <summary>
    /// Estimated level at listener positions. Each source plays a clip at <see cref="ClipRms"/>, so a source's level is its
    /// gain plus that; the bed is the power sum of the sources (they are uncorrelated). Before the mixer's Ambient attenuation
    /// and the player's master volume.
    /// </summary>
    static string Levels(Transform group, StartWorkshopFrame f)
    {
        var sources = group.GetComponentsInChildren<AudioSource>(true);
        var text = new StringBuilder();
        text.AppendLine("Estimated ambience level (dBFS, RMS, before the Ambient mix level and master volume) and loudest contributor:");
        foreach (var (name, at) in Listeners)
        {
            Vector3 listener = at(f);
            float power = 0f, best = 0f;
            string loudest = "-";
            foreach (var s in sources)
            {
                float g = Gain(s, listener) * ClipRms;
                power += g * g;
                if (g > best) { best = g; loudest = s.name; }
            }
            text.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  {0,-30} {1,7:F1} dBFS   loudest: {2} ({3:F1} dBFS)", name, ToDb(Mathf.Sqrt(power)), loudest, ToDb(best)));
        }
        return text.ToString();
    }

    static void WriteReport(Transform root, StartWorkshopFrame f)
    {
        var group = root.Find(GroupName);
        if (group == null) return;
        var text = new StringBuilder();
        text.AppendLine($"{GroupName} report ({DateTime.Now:yyyy-MM-dd})");
        text.AppendLine();
        text.AppendLine("Sources:");
        foreach (var s in group.GetComponentsInChildren<AudioSource>(true))
            text.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  {0,-24} {1,-34} vol {2:F2} pitch {3:F2} range {4:F1}-{5:F1} m at {6:F2}",
                s.name, s.clip != null ? s.clip.name : "(none)", s.volume, s.pitch, s.minDistance, s.maxDistance, s.transform.position));
        text.AppendLine();
        text.AppendLine(Levels(group, f));
        text.AppendLine("Clips (measured from the imported assets):");
        foreach (var name in ClipNames)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/{name}.wav");
            if (clip == null) { text.AppendLine($"  {name}: missing"); continue; }
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            double sum = 0; float peak = 0;
            foreach (float v in data) { sum += v * v; peak = Mathf.Max(peak, Mathf.Abs(v)); }
            float rms = (float)Math.Sqrt(sum / data.Length);
            // The jump across the loop point, against the largest step anywhere else in the clip.
            float seam = Mathf.Abs(data[0] - data[data.Length - 1]), maxStep = 0;
            for (int i = 1; i < data.Length; i++) maxStep = Mathf.Max(maxStep, Mathf.Abs(data[i] - data[i - 1]));
            text.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  {0,-34} {1,5:F1} s  rms {2:F1} dBFS  peak {3:F1} dBFS  loop-point step {4:F4} (largest step in clip {5:F4})",
                name, clip.length, ToDb(rms), ToDb(peak), seam, maxStep));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, text.ToString());
        Debug.Log(text.ToString());
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Sound synthesis. Each clip mixes components that are first brought to unit RMS, so the weights below are relative
    // levels. Noise is looped by an equal-power crossfade of its own tail; tones use whole numbers of cycles per loop, and
    // slow modulations repeat once per loop, so every clip is seamless.

    static IEnumerable<(string name, Func<float[]> synth)> Synths()
    {
        yield return (RoomTone, () => SynthRoomTone(new System.Random(3011)));
        yield return (Transformer, () => SynthTransformerHum(new System.Random(3023)));
        yield return (Motor, () => SynthMotorDrone(new System.Random(3037)));
        yield return (Fan, () => SynthFanWhir(new System.Random(3049)));
        yield return (Tube, () => SynthTubeBuzz());
        yield return (Pipe, () => SynthPipeHiss(new System.Random(3061)));
        yield return (Distant, () => SynthDistantMachinery(new System.Random(3079)));
    }

    const float Tau = 2f * Mathf.PI;

    // Low air handling, a soft airy band that slowly breathes, and a faint 50 Hz mains hum. Nothing above about 1.5 kHz.
    static float[] SynthRoomTone(System.Random random)
    {
        const float length = 12f;
        int n = (int)(length * SampleRate);
        var rumble = UnitRms(NoiseLoop(n, 1.5f, random, LowPassChain(150f, 2)));
        var air = UnitRms(NoiseLoop(n, 1.5f, random, BandChain(520f, .8f)));
        var clip = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate, u = t / length;
            float breath = .7f + .2f * Mathf.Sin(Tau * u) + .1f * Mathf.Sin(Tau * 2f * u + 1.3f);
            float mains = Mathf.Sin(Tau * 50f * t) + .6f * Mathf.Sin(Tau * 100f * t + .7f) + .25f * Mathf.Sin(Tau * 150f * t + 1.9f);
            clip[i] = rumble[i] * .9f + air[i] * .35f * breath + mains * .2f;
        }
        return NormalizeRms(clip, ClipRms, ClipPeak);
    }

    // A mains transformer: the 100 Hz hum with harmonics, a slightly detuned partner that makes a slow beat, and a little
    // core noise.
    static float[] SynthTransformerHum(System.Random random)
    {
        const float length = 4f;
        int n = (int)(length * SampleRate);
        var core = UnitRms(NoiseLoop(n, .4f, random, BandChain(1200f, 1f)));
        var clip = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate, u = t / length;
            float hum = Mathf.Sin(Tau * 100f * t) + .55f * Mathf.Sin(Tau * 200f * t + .4f) + .35f * Mathf.Sin(Tau * 300f * t + 1.1f) +
                        .15f * Mathf.Sin(Tau * 400f * t + 2.3f) + .3f * Mathf.Sin(Tau * 101f * t + .9f);
            clip[i] = hum * (1f + .05f * Mathf.Sin(Tau * 2f * u)) + core[i] * .12f;
        }
        return NormalizeRms(clip, ClipRms, ClipPeak);
    }

    // A slow electric motor: a low fundamental with harmonics, an 8 Hz flutter and a gentle phase drift, over bearing noise.
    static float[] SynthMotorDrone(System.Random random)
    {
        const float length = 6f;
        int n = (int)(length * SampleRate);
        var bearing = UnitRms(NoiseLoop(n, .5f, random, BandChain(300f, 1f)));
        var clip = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate, u = t / length;
            float drift = .4f * Mathf.Sin(Tau * u);
            float tone = Mathf.Sin(Tau * 60f * t + drift) + .6f * Mathf.Sin(Tau * 120f * t + 2f * drift + .5f) +
                         .3f * Mathf.Sin(Tau * 180f * t + 3f * drift + 1.4f) + .2f * Mathf.Sin(Tau * 240f * t + 4f * drift + 2.2f);
            float flutter = .85f + .15f * Mathf.Sin(Tau * 8f * t);
            clip[i] = tone * flutter + bearing[i] * .35f;
        }
        return NormalizeRms(clip, ClipRms, ClipPeak);
    }

    // Air moving through a vent: band-limited noise pulsed at a 7 Hz blade rate, over a steady low whoosh and a faint
    // motor tone.
    static float[] SynthFanWhir(System.Random random)
    {
        const float length = 3f;
        int n = (int)(length * SampleRate);
        var air = UnitRms(NoiseLoop(n, .4f, random, BandChain(1100f, .9f)));
        var whoosh = UnitRms(NoiseLoop(n, .4f, random, LowPassChain(300f, 2)));
        var clip = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float blades = .6f + .4f * (.5f + .5f * Mathf.Sin(Tau * 7f * t));
            clip[i] = air[i] * .8f * blades + whoosh[i] * .5f + Mathf.Sin(Tau * 210f * t) * .12f;
        }
        return NormalizeRms(clip, ClipRms, ClipPeak);
    }

    // A fluorescent tube: the 100 Hz ballast buzz, rich in harmonics, with a slight instability so it is not a dead tone.
    static float[] SynthTubeBuzz()
    {
        const float length = 2f;
        int n = (int)(length * SampleRate);
        var clip = new float[n];
        float[] amps = { 1f, .7f, .5f, .4f, .3f, 0f, .15f, 0f, 0f, .06f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate, u = t / length;
            float buzz = 0f;
            for (int h = 0; h < amps.Length; h++)
                buzz += amps[h] * Mathf.Sin(Tau * 100f * (h + 1) * t + h * .8f);
            float wobble = 1f + .04f * Mathf.Sin(Tau * u) + .03f * Mathf.Sin(Tau * 3f * u + .6f);
            clip[i] = buzz * wobble + Mathf.Sin(Tau * 2000f * t) * .03f;
        }
        return NormalizeRms(clip, ClipRms, ClipPeak);
    }

    // Gas leaking from a pipe: a soft hiss that slowly swells and ebbs, over a low rumble and a faint top.
    static float[] SynthPipeHiss(System.Random random)
    {
        const float length = 5f;
        int n = (int)(length * SampleRate);
        var hiss = UnitRms(NoiseLoop(n, .5f, random, BandChain(1600f, .9f)));
        var rumble = UnitRms(NoiseLoop(n, .5f, random, LowPassChain(500f, 2)));
        var top = UnitRms(NoiseLoop(n, .5f, random, BandChain(3800f, .8f)));
        var clip = new float[n];
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)SampleRate / length;
            float swell = .65f + .35f * Mathf.Sin(Tau * u);
            clip[i] = hiss[i] * swell + rumble[i] * .3f + top[i] * .15f;
        }
        return NormalizeRms(clip, ClipRms, ClipPeak);
    }

    // Machinery heard through walls: a low rumble that slowly swells, irregular dull thumps and, less often, a muffled clank,
    // everything low-passed. 20 s long and irregular, so it does not read as a repeating loop.
    static float[] SynthDistantMachinery(System.Random random)
    {
        const float length = 20f;
        int n = (int)(length * SampleRate);
        var dry = new float[n];
        var rumble = UnitRms(NoiseLoop(n, 2f, random, LowPassChain(110f, 2)));
        for (int i = 0; i < n; i++)
            dry[i] = rumble[i] * .6f * (.75f + .25f * Mathf.Sin(Tau * i / n));
        for (float at = .8f + (float)random.NextDouble() * .5f; at < length; at += 1.6f + (float)random.NextDouble() * 2.4f)
            AddEvent(dry, at, .7f, (float)(.7 + .3 * random.NextDouble()), t => Mathf.Sin(Tau * 52f * t) * Mathf.Exp(-t * 7f));
        for (float at = 2.2f + (float)random.NextDouble() * 2f; at < length; at += 4f + (float)random.NextDouble() * 5f)
            AddEvent(dry, at, .5f, (float)(.12 + .08 * random.NextDouble()),
                t => (Mathf.Sin(Tau * 170f * t) + .6f * Mathf.Sin(Tau * 331f * t)) * Mathf.Exp(-t * 14f));
        // Muffle it. Run once to settle the filters on the wrapped signal, then keep the second pass.
        var muffle = LowPassChain(420f, 2);
        for (int i = 0; i < n; i++) muffle(dry[i]);
        for (int i = 0; i < n; i++) dry[i] = muffle(dry[i]);
        return NormalizeRms(dry, ClipRms, DistantPeak);
    }

    // Adds a sound that starts at <at> seconds and lasts <duration>, wrapping past the end of the loop.
    static void AddEvent(float[] samples, float at, float duration, float amplitude, Func<float, float> shape)
    {
        int start = (int)(at * SampleRate), count = (int)(duration * SampleRate);
        for (int k = 0; k < count; k++)
            samples[(start + k) % samples.Length] += shape(k / (float)SampleRate) * amplitude * Mathf.Clamp01(k / (.004f * SampleRate));
    }

    // White noise through <filter>, looped by crossfading the last <fade> seconds into the start with equal power.
    static float[] NoiseLoop(int n, float fade, System.Random random, Func<float, float> filter)
    {
        int f = (int)(fade * SampleRate);
        var raw = new float[n + f];
        for (int i = 0; i < raw.Length; i++) raw[i] = filter((float)random.NextDouble() * 2f - 1f);
        var loop = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (i >= f) { loop[i] = raw[i]; continue; }
            float k = i / (float)f * Mathf.PI * .5f;
            loop[i] = raw[i] * Mathf.Sin(k) + raw[n + i] * Mathf.Cos(k);
        }
        return loop;
    }

    static float[] UnitRms(float[] samples)
    {
        double sum = 0;
        foreach (float s in samples) sum += s * s;
        float rms = (float)Math.Sqrt(sum / samples.Length);
        if (rms > 0f)
            for (int i = 0; i < samples.Length; i++) samples[i] /= rms;
        return samples;
    }

    // Scales to <rms>, or lower if that would push a peak past <peakLimit>.
    static float[] NormalizeRms(float[] samples, float rms, float peakLimit)
    {
        double sum = 0;
        float peak = 0f;
        foreach (float s in samples) { sum += s * s; peak = Mathf.Max(peak, Mathf.Abs(s)); }
        float current = (float)Math.Sqrt(sum / samples.Length);
        if (current <= 0f) return samples;
        float scale = Mathf.Min(rms / current, peakLimit / peak);
        for (int i = 0; i < samples.Length; i++) samples[i] *= scale;
        return samples;
    }

    // A cascade of <poles> one-pole low-passes.
    static Func<float, float> LowPassChain(float cutoff, int poles)
    {
        float a = 1f - Mathf.Exp(-Tau * cutoff / SampleRate);
        var state = new float[poles];
        return x =>
        {
            for (int p = 0; p < poles; p++) { state[p] += a * (x - state[p]); x = state[p]; }
            return x;
        };
    }

    // A state-variable band-pass.
    static Func<float, float> BandChain(float centre, float resonance)
    {
        float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(centre, SampleRate * .2f) / SampleRate);
        float q = 1f / Mathf.Max(.5f, resonance);
        float low = 0f, band = 0f;
        return x =>
        {
            low += f * band;
            float high = x - low - q * band;
            band += f * high;
            return band;
        };
    }

    static void WriteWav(string path, float[] samples)
    {
        using (var stream = new FileStream(path, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            int dataSize = samples.Length * 2;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            foreach (float s in samples)
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
        }
    }
}
