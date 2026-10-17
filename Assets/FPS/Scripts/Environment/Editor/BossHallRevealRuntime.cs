using System;
using System.Collections;
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
/// corebuster_env_056 Play-mode composition probe: three repeated door-threshold/incline/arena entries and one upper
/// reverse traversal with hall AI live. The saved player's own CharacterController walks under gravity; the player
/// script/input, hall actor colliders and the headless menu are disabled and the player is invincible in Play mode
/// only. Frames are rendered from the player's camera position. Nothing is saved; scene and shared state are checked.
/// </summary>
[InitializeOnLoad]
public static class BossHallRevealRuntime
{
    const string Key = "CoreBuster.BossHallRevealRuntime.";
    const float Speed = 4f, Gravity = 20f;
    static double quietSince, entered;
    static IEnumerator routine;
    static bool failed;

    static BossHallRevealRuntime()
    {
        EditorApplication.playModeStateChanged += State; EditorApplication.update += Tick;
        if (SessionState.GetBool(Key + "Pending", false)) Defer();
    }
    public static void RunBatch() { SessionState.EraseBool(Key + "Graphics"); Defer(); } // Graphics required; omit -quit.
    // Reuse the controlled real-capsule route probe without overwriting env_056 evidence.
    public static void RunGraphicsBatch()
    {
        SessionState.SetBool(Key + "Graphics", true); Defer();
    }
    static void Defer()
    {
        SessionState.SetBool(Key + "Pending", true); quietSince = EditorApplication.timeSinceStartup;
        EditorApplication.update -= WaitForQuiet; EditorApplication.update += WaitForQuiet;
    }
    static void WaitForQuiet()
    {
        if (EditorApplication.timeSinceStartup - quietSince < 30 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= WaitForQuiet; SessionState.EraseBool(Key + "Pending");
        try
        {
            EditorSceneManager.OpenScene(BossHallSurvey.ScenePath, OpenSceneMode.Single);
            var layout = BossHallFramework.MeasureLayout(); BossHallFramework.Validate(layout);
            SessionState.SetString(Key + "Sha", layout.sceneSha);
            SessionState.SetString(Key + "Shared", BossHallSnapshot.Digest(BossHallSnapshot.CaptureAssets()));
            RememberKnownAsset();
            SessionState.SetBool(Key + "Active", true); SessionState.SetBool(Key + "Ok", false);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) { entered = EditorApplication.timeSinceStartup; routine = null; failed = false; }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        string label = SessionState.GetBool(Key + "Graphics", false) ? "ENV057" : "ENV056";
        string known = RestoreKnownAsset(); Debug.Log(label + " known shared-asset check: " + (known ?? "UNEXPECTED CHANGE"));
        string report = SessionState.GetBool(Key + "Graphics", false) ? "Docs/BossHall/GraphicsRuntime_057.txt" : "Docs/BossHall/RevealRuntime_056.txt";
        if (known != null && known != "unchanged" && File.Exists(report)) File.AppendAllText(report, known + ".\n");
        bool ok = known != null && SessionState.GetBool(Key + "Ok", false) && BossHallSurvey.Hash(BossHallSurvey.ScenePath) == SessionState.GetString(Key + "Sha", "") &&
            BossHallSnapshot.Digest(BossHallSnapshot.CaptureAssets()) == SessionState.GetString(Key + "Shared", "");
        foreach (string k in new[] { "Active", "Ok" }) SessionState.EraseBool(Key + k);
        foreach (string k in new[] { "Sha", "Shared" }) SessionState.EraseString(Key + k);
        SessionState.EraseBool(Key + "Graphics");
        Debug.Log(label + " runtime returned to Edit mode; scene/shared assets unchanged and probe passed: " + ok);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false) || !EditorApplication.isPlaying) return;
        foreach (var m in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            if (m.enabled && m.GetType().Name == "InGameMenuManager") m.enabled = false;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        if (EditorApplication.timeSinceStartup - entered < 3 || failed) return;
        if (routine == null) routine = Probe();
        try { if (routine.MoveNext()) return; }
        catch (Exception ex) { Debug.LogException(ex); failed = true; }
        (routine as IDisposable)?.Dispose(); routine = null; EditorApplication.ExitPlaymode();
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    // Known teammate-asset finding (2026-10-07): TutorialWingBuilder sets M_TW_PodGlass _SrcBlend to SrcAlpha while URP Lit's
    // preserve-specular alpha mode requires One, so URP rewrites it during a Play session and the Editor saves it. Probes
    // keep the committed bytes and tolerate exactly that one-line normalisation; any other shared-asset change still fails.
    public const string KnownNormalisedAsset = "Assets/FPS/Art/TutorialWing/Materials/M_TW_PodGlass.mat";
    const string KnownKey = "CoreBuster.BossHallKnownAsset";
    public static void RememberKnownAsset()
    {
        SessionState.SetString(KnownKey, Convert.ToBase64String(File.ReadAllBytes(KnownNormalisedAsset)));
        SessionState.SetString(KnownKey + "Json", EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<Material>(KnownNormalisedAsset)));
    }
    /// <summary>Restores the committed bytes after an exact URP _SrcBlend normalisation; returns a note, or null if the file differs otherwise.</summary>
    public static string RestoreKnownAsset()
    {
        string saved = SessionState.GetString(KnownKey, ""); if (saved.Length == 0) return "known asset not recorded";
        // The Editor saves the in-memory normalisation on quit: revert exactly that change in memory and clear the dirty flag.
        string memory = "unchanged"; var material = AssetDatabase.LoadAssetAtPath<Material>(KnownNormalisedAsset);
        if (material && EditorUtility.IsDirty(material))
        {
            string json = SessionState.GetString(KnownKey + "Json", "");
            bool only = material.GetFloat("_SrcBlend") == 1; material.SetFloat("_SrcBlend", 5); only &= EditorJsonUtility.ToJson(material) == json;
            EditorJsonUtility.FromJsonOverwrite(json, material); EditorUtility.ClearDirty(material);
            if (!only) return null;
            memory = "URP normalised M_TW_PodGlass _SrcBlend 5->1 in memory during Play; reverted and dirty flag cleared so the committed file is not rewritten";
        }
        var original = Convert.FromBase64String(saved); var current = File.ReadAllBytes(KnownNormalisedAsset);
        if (original.SequenceEqual(current)) return memory;
        var a = System.Text.Encoding.UTF8.GetString(original).Split('\n'); var b = System.Text.Encoding.UTF8.GetString(current).Split('\n');
        var diff = a.Length == b.Length ? Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToArray() : null;
        if (diff == null || diff.Length != 1 || a[diff[0]].Trim() != "- _SrcBlend: 5" || b[diff[0]].Trim() != "- _SrcBlend: 1") return null;
        File.WriteAllBytes(KnownNormalisedAsset, original); AssetDatabase.ImportAsset(KnownNormalisedAsset, ImportAssetOptions.ForceUpdate);
        return "URP normalised M_TW_PodGlass _SrcBlend 5->1 during Play; committed bytes restored";
    }
    /// <summary>The tutorial wing starts the player unarmed: take the real sidearm from its locker (Play mode only).</summary>
    public static IEnumerator ArmFromLocker(PlayerCharacterController player, List<string> lines)
    {
        var weapons = player.GetComponent<PlayerWeaponsManager>();
        if (weapons.GetActiveWeapon()) { lines.Add("Player already armed at start."); yield break; }
        var locker = Object.FindObjectsByType<WeaponLocker>(FindObjectsInactive.Include).SingleOrDefault();
        Require(locker && !locker.IsTaken, "Missing untaken tutorial weapon locker");
        var interactor = player.GetComponent<KeycardInteractor>(); Require(interactor, "Player has no KeycardInteractor");
        locker.Interact(interactor);
        double deadline = EditorApplication.timeSinceStartup + 15;
        while (!(weapons.GetActiveWeapon() && weapons.GetActiveWeapon().IsWeaponActive))
        { Require(EditorApplication.timeSinceStartup < deadline, "Sidearm never became active"); yield return null; }
        lines.Add("Player armed through the real tutorial WeaponLocker.Interact: " + weapons.GetActiveWeapon().WeaponName + " (Play mode only).");
    }
    static string F(FormattableString s) => FormattableString.Invariant(s);
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

    static void Teleport(CharacterController cc, Vector3 p)
    {
        cc.enabled = false; cc.transform.SetPositionAndRotation(p + Vector3.up * .05f, cc.transform.rotation); cc.enabled = true; Physics.SyncTransforms();
    }

    sealed class Walk { public bool reached; public float metres, seconds, minY = float.MaxValue, maxY = float.MinValue; public int frames; public string note = ""; }

    /// <summary>Walks straight through measured points at 4 m/s on real frames, rendering a frame every <c>every</c> metres.</summary>
    static IEnumerator WalkRoute(CharacterController cc, Camera eyeCamera, Vector3[] points, float yaw, float pitch, float every,
        Walk result, List<Color32[]> frames, string folder, string prefix)
    {
        Teleport(cc, points[0]); float vy = 0, sinceFrame = every, stall = 0; double deadline = EditorApplication.timeSinceStartup + 120;
        var camera = cc.GetComponent<PlayerCharacterController>().PlayerCamera;
        for (int i = 1; i < points.Length; i++)
        {
            while (Flat(points[i] - cc.transform.position).magnitude > .3f)
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Walk timed out");
                float dt = Mathf.Clamp(Time.deltaTime, .005f, .05f); var before = cc.transform.position;
                vy -= Gravity * dt; cc.Move(Flat(points[i] - before).normalized * Speed * dt + Vector3.up * vy * dt);
                if (cc.isGrounded && vy < 0) vy = -1;
                var after = cc.transform.position; float moved = Flat(after - before).magnitude;
                result.metres += moved; result.seconds += dt; sinceFrame += moved;
                result.minY = Mathf.Min(result.minY, after.y); result.maxY = Mathf.Max(result.maxY, after.y);
                stall = moved < Speed * dt * .3f ? stall + dt : 0;
                if (stall > 1.5f) { result.note = F($"stalled at {after:F2}"); yield break; }
                if (after.y < points.Min(p => p.y) - 1) { result.note = F($"fell to {after:F2}"); yield break; }
                if (sinceFrame >= every && eyeCamera)
                {
                    sinceFrame = 0; eyeCamera.transform.SetPositionAndRotation(camera.transform.position, Quaternion.Euler(-pitch, yaw, 0));
                    var frame = Render(eyeCamera); frames?.Add(frame); result.frames++;
                    if (folder != null) File.WriteAllBytes(F($"{folder}/{prefix}_{result.metres:00.0}m.png"), Png(frame));
                }
                yield return null;
            }
        }
        result.reached = true;
    }
    static Color32[] Render(Camera c)
    {
        int w = BossHallRenderingReview.Width, h = BossHallRenderingReview.Height;
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32); var previous = RenderTexture.active; var t = new Texture2D(w, h, TextureFormat.RGB24, false);
        try { c.targetTexture = rt; c.Render(); RenderTexture.active = rt; t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply(); return t.GetPixels32(); }
        finally { c.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(t); }
    }
    static byte[] Png(Color32[] p)
    {
        var t = new Texture2D(BossHallRenderingReview.Width, BossHallRenderingReview.Height, TextureFormat.RGB24, false);
        try { t.SetPixels32(p); t.Apply(); return t.EncodeToPNG(); } finally { Object.DestroyImmediate(t); }
    }

    static IEnumerator Probe()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        bool graphics = SessionState.GetBool(Key + "Graphics", false);
        string output = (graphics ? BossHallGraphics.Output : BossHallReveal.Output) + "/Runtime"; Directory.CreateDirectory(output);
        var layout = new BossHallFramework.Layout(JsonUtility.FromJson<BossHallFramework.Contract>(File.ReadAllText(BossHallFramework.ContractPath)));
        var r = layout.Report;
        var lines = new List<string> { "BOSS HALL " + (graphics ? "GRAPHICS RUNTIME env_057" : "REVEAL RUNTIME env_056") + " (Play mode, hall AI live; controlled capsule walk, not human input)" };
        var player = Object.FindAnyObjectByType<PlayerCharacterController>(); var cc = player.GetComponent<CharacterController>();
        player.GetComponent<Health>().Invincible = true; player.enabled = false;
        var input = player.GetComponent<PlayerInputHandler>(); if (input) input.enabled = false;
        var hall = r.interior.ToBounds(); hall.Expand(new Vector3(4, 10, 4)); int disabled = 0;
        foreach (var agent in Object.FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Exclude).Where(a => hall.Contains(a.transform.position)))
            foreach (var c in agent.GetComponentsInChildren<Collider>()) { c.enabled = false; disabled++; }
        var door = Object.FindObjectsByType<SecurityDoor>(FindObjectsInactive.Include).Single(d => d.name == BossHallSurvey.BossDoorName);
        Require(door.IsLocked, "Boss Hall Door should start locked");
        int shots = 0;
        foreach (var weapon in Object.FindObjectsByType<WeaponController>(FindObjectsInactive.Include).Where(w => !w.transform.IsChildOf(player.transform) && hall.Contains(w.transform.position)))
            weapon.OnShootProcessed += () => shots++;
        float eye = player.PlayerCamera.transform.position.y - player.transform.position.y;
        var arm = ArmFromLocker(player, lines); while (arm.MoveNext()) yield return arm.Current;
        lines.Add(F($"Player camera {eye:F2} m above the capsule base; {disabled} hall actor colliders disabled so live actors cannot block the walk; player invincible; Boss Hall Door locked {door.IsLocked}."));

        var camera = BossHallRenderingReview.CameraForReview(); camera.fieldOfView = player.GetComponent<PlayerWeaponsManager>().DefaultFov;
        try
        {
            // Measured live: the trimmed contract keeps the entry opening but not the incline key points.
            float eyeHeight = BossHallRenderingReview.EyeHeight();
            var threshold = BossHallRenderingReview.InclineEye(1, layout) - Vector3.up * eyeHeight;
            var landing = BossHallRenderingReview.InclineEye(BossHallReveal.LandingDistance, layout) - Vector3.up * eyeHeight;
            var inside = BossHallReveal.EntryInside(layout); var deep = inside + r.entry.inward * 7;
            var entry = new[] { threshold, landing, inside, deep };
            for (int run = 1; run <= 3; run++)
            {
                var walk = new Walk(); var frames = new List<Color32[]>(); int before = shots;
                var steps = WalkRoute(cc, camera, entry, 270, 0, 1, walk, frames, run == 1 ? output : null, "Entry_Run1");
                while (steps.MoveNext()) yield return steps.Current;
                Require(walk.reached, "Entry run " + run + " did not reach the arena: " + walk.note);
                BossHallRevealSheet(output + "/Entry_Run" + run + ".png", frames);
                lines.Add(F($"Entry run {run}: reached {deep:F2} from the threshold, {walk.metres:F1} m in {walk.seconds:F1} s, Y {walk.minY:F2}..{walk.maxY:F2}, {walk.frames} frames, {shots - before} NPC shots; door still locked {door.IsLocked}."));
                Require(door.IsLocked, "The probe changed the Boss Hall Door state");
            }
            Vector3 Approach(BossHallSurvey.BayDoor d) { var b = d.approach.ToBounds(); return new Vector3(b.center.x, d.deckTop, b.center.z); }
            var bays = r.bayDoors.OrderBy(d => d.faceX).Select(Approach).ToArray(); // West approach, east approach.
            // The bridge does not cross the turret void: follow runtime NavMesh corners around the ring, then finish along the
            // bridge's north half (Z +2.2) clear of the finale's ramp foot (Z 114.1-116.5) to the east approach.
            var east = bays[1] + Vector3.forward * 2.2f; var clearOfRamp = new Vector3(east.x - 16, east.y, east.z);
            var path = new NavMeshPath();
            Require(NavMesh.SamplePosition(bays[0], out var from, 1, NavMesh.AllAreas) && NavMesh.SamplePosition(clearOfRamp, out var to, 1, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete, "No upper NavMesh route");
            var upper = new[] { bays[0] }.Concat(path.corners.Skip(1).Select(c => new Vector3(c.x, bays[0].y, c.z))).Concat(new[] { east }).ToArray();
            var reverse = new Walk(); var upperFrames = new List<Color32[]>(); int upperBefore = shots;
            var walkUpper = WalkRoute(cc, camera, upper, 90, -8, 4, reverse, upperFrames, output, "Upper_Reverse");
            while (walkUpper.MoveNext()) yield return walkUpper.Current;
            Require(reverse.reached, "Upper reverse traversal failed: " + reverse.note);
            BossHallRevealSheet(output + "/Upper_Reverse.png", upperFrames);
            lines.Add(F($"Upper reverse: walked {upper.First():F2} -> {upper.Last():F2} via {upper.Length - 2} NavMesh/ramp-clear waypoints, {reverse.metres:F1} m in {reverse.seconds:F1} s, Y {reverse.minY:F2}..{reverse.maxY:F2}, {reverse.frames} frames facing the entry, {shots - upperBefore} NPC shots."));
            if (graphics)
            {
                BossHallGraphics.ValidateInstalled(layout);
                foreach (var view in BossHallGraphicsReview.Approaches(layout).Where(v => v.name.EndsWith("_Gallery")))
                {
                    var basePoint = view.eye - Vector3.up * eyeHeight;
                    var shortWalk = new Walk(); var galleryFrames = new List<Color32[]>();
                    var steps = WalkRoute(cc, camera, new[] { basePoint + Vector3.right * 2, basePoint }, 270, -15, .5f,
                        shortWalk, galleryFrames, output, view.name);
                    while (steps.MoveNext()) yield return steps.Current;
                    Require(shortWalk.reached, view.name + " approach walk failed: " + shortWalk.note);
                    lines.Add(F($"{view.name}: reached measured original deck eye {view.eye:F2}, {shortWalk.metres:F1} m in {shortWalk.seconds:F1} s."));
                }
                BossHallGraphicsReview.Capture(layout, "Runtime/Approaches");
                BossHallGraphics.ValidateInstalled(layout);
                lines.Add("Saved plate facing/UV/material/component bindings unchanged with AI live; floor and both gallery approaches captured in Play mode.");
            }
        }
        finally { BossHallRenderingReview.DestroyCamera(camera); }
        lines.Add("Nothing saved; Play-mode changes are discarded on exit. Human-input moving combat remains corebuster_env_058.");
        BossHallLighting.Write(graphics ? "GraphicsRuntime_057.txt" : "RevealRuntime_056.txt", string.Join("\n", lines) + "\n");
        SessionState.SetBool(Key + "Ok", true);
    }
    static void BossHallRevealSheet(string path, List<Color32[]> frames)
    {
        int w = BossHallRenderingReview.Width / 4, h = BossHallRenderingReview.Height / 4, columns = 5, rows = Mathf.Max(1, (frames.Count + columns - 1) / columns);
        var fill = Enumerable.Repeat(new Color32(12, 12, 12, 255), w * columns * h * rows).ToArray();
        for (int f = 0; f < frames.Count; f++)
        {
            int ox = f % columns * w, oy = (rows - 1 - f / columns) * h;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) fill[(oy + y) * w * columns + ox + x] = frames[f][(y * 4) * BossHallRenderingReview.Width + x * 4];
        }
        var sheet = new Texture2D(w * columns, h * rows, TextureFormat.RGB24, false);
        try { sheet.SetPixels32(fill); sheet.Apply(); File.WriteAllBytes(path, sheet.EncodeToPNG()); } finally { Object.DestroyImmediate(sheet); }
    }
}
