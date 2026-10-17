using System;
using Unity.FPS.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>Installs the room-owned ambient source on the existing reactor glow.</summary>
public static class ReactorAudioInstaller
{
    const string ClipPath = "Assets/FPS/Audio/SFX/CentreRoom/Reactor_Unstable_Loop.wav";
    const string MixerPath = "Assets/FPS/Audio/MainAudioMixer.mixer";
    const string FieldPath = "Assets/FPS/Art/CentreRoom/Reactor/Materials/M_Reactor_Field.mat";
    const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    const string GlowPath = "Level/Centre_Room/Lighting/Reactor_Core_Glow";

    [MenuItem("Tools/CoreBuster/Attach Centre Room Reactor Audio")]
    public static void AttachMenu()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open MainScene before attaching reactor audio.");
        var glow = GameObject.Find(GlowPath);
        if (!glow) throw new InvalidOperationException("Build Centre Room Lighting before attaching reactor audio.");
        Attach(glow, AssetDatabase.LoadAssetAtPath<Material>(FieldPath));
        EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void AttachBatch()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AttachMenu();
        if (!EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()))
            throw new InvalidOperationException("Could not save reactor audio in MainScene.");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Validate(GameObject.Find(GlowPath));
        Debug.Log("CENTRE ROOM REACTOR AUDIO VERIFIED: saved and reloaded spatial Ambient source.");
    }

    public static void Attach(GameObject glow, Material field)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        var groups = mixer ? mixer.FindMatchingGroups("Ambient") : Array.Empty<AudioMixerGroup>();
        if (!clip || groups.Length != 1 || !field)
            throw new InvalidOperationException("Reactor clip, Ambient mixer group or field material is missing.");

        var source = glow.GetComponent<AudioSource>();
        if (!source) source = glow.AddComponent<AudioSource>();
        source.clip = clip;
        source.outputAudioMixerGroup = groups[0];
        source.playOnAwake = true;
        source.loop = true;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.minDistance = 2f;
        source.maxDistance = 16f;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(.18f, .72f),
                new Keyframe(.55f, .24f), new Keyframe(1f, 0f)));
        source.dopplerLevel = 0f;
        source.spread = 0f;
        source.volume = .7f;

        var driver = glow.GetComponent<ReactorInstabilityAudio>();
        if (!driver) driver = glow.AddComponent<ReactorInstabilityAudio>();
        driver.Configure(field, .7f);
        Validate(glow);
    }

    static void Validate(GameObject glow)
    {
        if (!glow) throw new InvalidOperationException("Reactor glow is missing from MainScene.");
        var sources = glow.GetComponents<AudioSource>();
        var drivers = glow.GetComponents<ReactorInstabilityAudio>();
        if (sources.Length != 1 || drivers.Length != 1 || !sources[0].clip || !sources[0].loop ||
            !sources[0].playOnAwake || sources[0].spatialBlend != 1f || sources[0].maxDistance > 16f ||
            drivers[0].Field != AssetDatabase.LoadAssetAtPath<Material>(FieldPath))
            throw new InvalidOperationException("Reactor audio source is not configured as one bounded spatial loop.");
    }
}
