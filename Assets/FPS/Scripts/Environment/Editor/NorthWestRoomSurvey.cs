using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Read-only, repeatable saved-scene survey of the North-West Room for corebuster_env_029. It never saves,
/// builds, moves or rebakes anything. It shares no code with CentreRoomSurvey, so Centre outputs cannot change.
/// </summary>
public static class NorthWestRoomSurvey
{
    public const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    public const string Output = "ReviewCaptures/NorthWestRoom/029";
    public const string EastDoorName = "North-West Room Door (E)", BossDoorName = "Boss Hall Door",
        RedTerminalName = "North-West Room Terminal (Red)", MasterTerminalName = "Master Terminal",
        InclineName = "Corridor Incline Full", CentreNorthGateName = "Centre Room Door (N)";
    const float Step = .02f, NavTolerance = .3f, WarpRadius = 3f, SpawnRadius = 2f, BodyBand = 2.2f,
        ConnectorHalfWidth = 1.5f, RingInner = 4f, RingOuter = 8f, RingConnector = 6f,
        WallFlushDepth = 1.2f, HeavyDepth = 3f, ServicesTop = 6f, MaxZoneDepth = 8f;
    // Provisional locator from the repository audit. Tiles are found around it; wall rays start 5 m north of it
    // so they miss the boss-door opening on the room's Z centre line. Real surfaces come from the shell hits.
    static readonly Vector3 Locator = new Vector3(-234.75f, 1.5f, 115.45f);
    static readonly Vector3 Seed = Locator + Vector3.forward * 5;

    [Serializable] public class Box
    {
        public string name, id;
        public Vector3 min, max;
        public Box(string label, Bounds bounds, Object source = null)
        {
            name = label; min = bounds.min; max = bounds.max;
            id = source ? GlobalObjectId.GetGlobalObjectIdSlow(source).ToString() : "";
        }
    }
    [Serializable] public class Point
    {
        public string name, id;
        public Vector3 position;
        public bool onNavMesh;
        public Vector3 navPosition;
        public Point(string label, Vector3 p, Object source = null)
        {
            name = label; position = p;
            id = source ? GlobalObjectId.GetGlobalObjectIdSlow(source).ToString() : "";
            onNavMesh = NavMesh.SamplePosition(p, out var hit, NavTolerance, NavMesh.AllAreas);
            navPosition = onNavMesh ? hit.position : p; // JsonUtility otherwise writes non-JSON Infinity values.
        }
    }
    [Serializable] public class ShellPiece
    {
        public string name, id, kind;
        public Vector3 min, max;
        public bool rendererEnabled, active;
        public int enabledColliders;
    }
    [Serializable] public class WallSample
    {
        public string wall;
        public float plane, spread;
        public int hits, openings;
        public List<string> deviations = new List<string>();
    }
    [Serializable] public class Gate
    {
        public string name, id;
        public Vector3 position, outward, along;
        public bool outwardIsTransformRight, startsLocked, hasObstacle;
        public Box blocker, structure;
        public float clearMin, clearMax, lintelY;
        public float visibleOpenMin, visibleOpenMax, visibleLintelY;
        public Box approach;
        public List<Vector3> lamps = new List<Vector3>();
        public Vector3 lampLight;
        public List<string> inspectedColliders = new List<string>();
    }
    [Serializable] public class Terminal
    {
        public string name, id, type;
        public Vector3 position, forward;
        public Point focus, standing;
        public float range, eyeHeight, eyeY, eyeDistance, reachRadiusAtEye, pitchToFocus, maxAimAngle;
        public bool promptWhenLookingLevel, focusVisible, standingClear, standingInside;
        public int reachSamples, reachUsable;
        public Box body, keepOut;
        // Master Terminal: the card-insertion camera turns from the player's view to LookAt, then seats each disk.
        public List<Point> slots = new List<Point>();
        public List<Vector3> slotForwards = new List<Vector3>();
        public List<bool> slotMouthVisible = new List<bool>();
        public Point lookAt;
        public bool lookAtVisible;
        public float lookAtYaw, lookAtPitch;
        public List<string> cards = new List<string>();
        public List<string> doors = new List<string>();
    }
    [Serializable] public class Spawn
    {
        public string name, id;
        public Vector3 position, navPosition, warpPosition;
        public bool onNavMesh, warpOnNavMesh, warpInsideDisc, insideInterior, discInsideInterior, bodyClear;
        public float navOffset, warpOffset, wallClearance, centreDistance;
        public List<string> overlaps = new List<string>();
    }
    [Serializable] public class Route
    {
        public string name, status;
        public bool connector;
        public Vector3 start, end;
        public float length;
        public Vector3[] corners;
    }
    [Serializable] public class ProfileSample
    {
        public float distance, groundY;
        public Vector3 position;
        public string ground;
        public bool onNavMesh, onIncline;
    }
    [Serializable] public class Zone
    {
        public string name, kind, wall;
        public Vector3 min, max;
        public float minFreeDepth;
        public string notes;
    }
    [Serializable] public class Report
    {
        public string unityVersion, sceneSha256, surveyedUtc;
        public Box interior;
        public Vector3 centre;
        public float floorTopY, ceilingUndersideY, physicalGroundY, movementBandTopY;
        public List<WallSample> walls = new List<WallSample>();
        public List<ShellPiece> shell = new List<ShellPiece>();
        public int floorTiles, ceilingTiles, wallPieces, disabledShellRenderers;
        public List<string> floorOverlaps = new List<string>();
        public List<string> ceilingOverlaps = new List<string>();
        public List<Box> existingContent = new List<Box>();
        public List<string> lights = new List<string>();
        public List<Gate> gates = new List<Gate>();
        public Terminal redTerminal, masterTerminal;
        public Point accessMachine, accessStanding;
        public float playerEyeHeight, maxAimAngle, minSpawnDistance;
        public List<Spawn> spawns = new List<Spawn>();
        public List<Point> occupants = new List<Point>();
        public List<string> waves = new List<string>();
        public List<string> navigation = new List<string>();
        public Point floorSupport;
        public List<string> groundColliders = new List<string>();
        public int shellFloorSamplesMissing;
        public List<Vector3> floorSupportMisses = new List<Vector3>();
        public int navSamples, navHits, ringSamples, ringNavHits, ringBodyBlocked, ringActorOverlaps;
        public List<Vector3> navMisses = new List<Vector3>();
        public float ringInnerRadius = RingInner, ringOuterRadius = RingOuter, spawnRadius = SpawnRadius,
            connectorHalfWidth = ConnectorHalfWidth;
        public List<Route> routes = new List<Route>();
        public Box incline;
        public float inclineStartDistance = -1, bossHallFloorDistance = -1, bossHallFloorY;
        public Vector3 bossThreshold, inclineStart, bossHallFloor;
        public List<ProfileSample> bossApproach = new List<ProfileSample>();
        public List<Zone> zones = new List<Zone>();
        public List<string> objectives = new List<string>();
        public List<string> notes = new List<string>();
    }

    static Report last;
    static Collider[] solids;

    [MenuItem("Tools/CoreBuster/Measure North-West Room")]
    public static void MeasureMenu()
    {
        last = Measure();
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Measurement.json", JsonUtility.ToJson(last, true));
        SceneView.duringSceneGui -= Draw;
        SceneView.duringSceneGui += Draw;
        SceneView.RepaintAll();
        Debug.Log("North-West Room survey written to " + Output + "/Measurement.json. Scene overlay enabled; no scene saved.");
    }

    [MenuItem("Tools/CoreBuster/Hide North-West Room Survey")]
    public static void Hide()
    {
        SceneView.duringSceneGui -= Draw;
        last = null;
        SceneView.RepaintAll();
    }

    public static void MeasureBatch()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        MeasureMenu();
    }

    public static void CaptureBatch()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        CaptureMenu();
    }

    [MenuItem("Tools/CoreBuster/Capture North-West Room Survey")]
    public static void CaptureMenu()
    {
        MeasureMenu();
        var go = new GameObject("North-West Room survey camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 65; camera.nearClipPlane = .05f; camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        camera.GetUniversalAdditionalCameraData().volumeLayerMask = ~0;
        try
        {
            // Player eye above the measured ground under each camera, so the boss-side view stands on the incline steps.
            Func<Vector3, Vector3> eye = p =>
            {
                var profile = last.bossApproach.FirstOrDefault(s => Flat(s.position - p).magnitude < .3f && s.groundY > -900);
                return At(p, (profile != null ? profile.groundY : GroundY(p, last.floorTopY)) + last.playerEyeHeight);
            };
            var views = new List<(string name, Vector3 eye, Vector3 target)>();
            foreach (var gate in last.gates)
            {
                string tag = gate.name == EastDoorName ? "East_Gate" : "Boss_Door";
                Vector3 at = new Vector3(gate.position.x, last.floorTopY + 1.5f, gate.position.z);
                views.Add((tag + "_Inside", eye(gate.position - gate.outward * 6), at));
                views.Add((tag + "_Outside", eye(gate.position + gate.outward * 5), at));
            }
            views.Add(("Red_Terminal", At(last.redTerminal.standing.position, last.redTerminal.eyeY), last.redTerminal.focus.position));
            views.Add(("Master_Terminal", At(last.masterTerminal.standing.position, last.masterTerminal.eyeY), last.masterTerminal.lookAt.position));
            views.Add(("Room_From_SouthEast", new Vector3(last.interior.max.x - 3, last.floorTopY + 3, last.interior.min.z + 3), last.centre + Vector3.up * 2));
            views.Add(("Room_From_NorthWest", new Vector3(last.interior.min.x + 3, last.floorTopY + 3, last.interior.max.z - 3), last.centre + Vector3.up * 2));
            foreach (var view in views)
            {
                camera.transform.SetPositionAndRotation(view.eye, Quaternion.LookRotation(view.target - view.eye));
                var rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = rt;
                    camera.Render(); camera.Render();
                    RenderTexture.active = rt;
                    texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                    File.WriteAllBytes(Output + "/" + view.name + ".png", texture.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null; RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(texture);
                }
            }
        }
        finally { Object.DestroyImmediate(go); }
        if (SceneManager.GetActiveScene().isDirty || Hash(ScenePath) != last.sceneSha256)
            throw new InvalidOperationException("Scene changed during capture; do not save it.");
    }

    static Vector3 At(Vector3 p, float y) => new Vector3(p.x, y, p.z);

    static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

    static T Single<T>(Scene scene, string name) where T : Component
    {
        var found = All<T>(scene).Where(c => c.name == name).ToArray();
        if (found.Length != 1)
            throw new InvalidOperationException($"Expected exactly one {typeof(T).Name} named '{name}', found {found.Length}. " +
                "Do not rename, duplicate, move into dressing, or remove gameplay objects.");
        return found[0];
    }

    public static Report Measure()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.isDirty || EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Open a clean, saved MainScene alone in Edit mode before measuring. The survey will not save or discard edits.");
        Physics.SyncTransforms();
        solids = All<Collider>(scene).Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
        var report = new Report { unityVersion = Application.unityVersion, sceneSha256 = Hash(ScenePath),
            surveyedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") };
        var allRenderers = All<MeshRenderer>(scene);
        var renderers = allRenderers.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();

        // ---- Shell frame: tile renderer levels, then wall planes from fixed shell collider hits.
        var shellColliders = solids.Where(c => ShellRoot(c.transform) != null).ToArray();
        var tileRenderers = renderers.Where(r => IsTile(r.transform) &&
            Mathf.Abs(r.bounds.center.x - Locator.x) < 16.5f && Mathf.Abs(r.bounds.center.z - Locator.z) < 16.5f).ToArray();
        if (!tileRenderers.Any(r => r.bounds.center.y < 2) || !tileRenderers.Any(r => r.bounds.center.y > 5))
            throw new InvalidOperationException("No original floor/ceiling tiles around the North-West Room locator; inspect the shell.");
        float floorY = tileRenderers.Where(r => r.bounds.center.y < 2).Max(r => r.bounds.max.y);
        float ceilingY = tileRenderers.Where(r => r.bounds.center.y > 5).Min(r => r.bounds.min.y);
        Vector3 seed = new Vector3(Seed.x, floorY + 1.3f, Seed.z);
        var provisional = new Bounds();
        provisional.SetMinMax(new Vector3(Surface(shellColliders, seed, Vector3.left).x, floorY, Surface(shellColliders, seed, Vector3.back).z),
            new Vector3(Surface(shellColliders, seed, Vector3.right).x, ceilingY, Surface(shellColliders, seed, Vector3.forward).z));
        var west = WallPlane(report, "West", shellColliders, provisional, Vector3.left, floorY);
        var east = WallPlane(report, "East", shellColliders, provisional, Vector3.right, floorY);
        var south = WallPlane(report, "South", shellColliders, provisional, Vector3.back, floorY);
        var north = WallPlane(report, "North", shellColliders, provisional, Vector3.forward, floorY);
        Vector3 low = new Vector3(west, floorY, south), high = new Vector3(east, ceilingY, north);
        var bounds = new Bounds(); bounds.SetMinMax(low, high);
        if (bounds.size.x < 25 || bounds.size.x > 40 || bounds.size.z < 25 || bounds.size.z > 40 || bounds.size.y < 5)
            throw new InvalidOperationException("Unexpected North-West Room shell measurement; inspect the fixed shell anchors.");
        report.interior = new Box("Measured interior", bounds);
        report.floorTopY = floorY; report.ceilingUndersideY = ceilingY; report.movementBandTopY = floorY + BodyBand;
        report.centre = new Vector3(bounds.center.x, floorY, bounds.center.z); // True centre: no occupant offset here.

        // ---- Shell renderer states, including disabled pieces and duplicates.
        foreach (var r in allRenderers.Where(r => ShellRoot(r.transform) != null && Near(r.bounds, bounds, 1)).OrderBy(r => PathOf(r.transform)))
        {
            var root = ShellRoot(r.transform);
            string kind = IsTile(r.transform) ? (r.bounds.center.y < 2 ? "floor" : r.bounds.center.y > 5 ? "ceiling" : "tile") : "wall";
            report.shell.Add(new ShellPiece { name = PathOf(r.transform), id = GlobalObjectId.GetGlobalObjectIdSlow(r).ToString(), kind = kind,
                min = r.bounds.min, max = r.bounds.max, rendererEnabled = r.enabled, active = r.gameObject.activeInHierarchy,
                enabledColliders = root.GetComponentsInChildren<Collider>(true).Count(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger) });
        }
        var liveShell = report.shell.Where(s => s.rendererEnabled && s.active).ToList();
        report.floorTiles = liveShell.Count(s => s.kind == "floor" && Inside2(s, bounds));
        report.ceilingTiles = liveShell.Count(s => s.kind == "ceiling" && Inside2(s, bounds));
        report.wallPieces = liveShell.Count(s => s.kind == "wall");
        report.disabledShellRenderers = report.shell.Count(s => !s.rendererEnabled || !s.active);
        report.floorOverlaps.AddRange(Overlaps(liveShell.Where(s => s.kind == "floor").ToList()));
        report.ceilingOverlaps.AddRange(Overlaps(liveShell.Where(s => s.kind == "ceiling").ToList()));

        // ---- Existing non-shell content and lights inside the room.
        foreach (var group in renderers.Where(r => ShellRoot(r.transform) == null && !NorthWestRoomFramework.IsRenderingSkin(r.transform) && InsideXZ(r.bounds.center, bounds))
                     .GroupBy(r => GroupKey(r.transform)).OrderBy(g => g.Key))
        {
            var union = group.First().bounds;
            foreach (var r in group) union.Encapsulate(r.bounds);
            report.existingContent.Add(new Box(group.Key + " (" + group.Count() + " renderers)", union));
        }
        foreach (var light in All<Light>(scene).Where(l => l.enabled && l.gameObject.activeInHierarchy && InsideXZ(l.transform.position, bounds)))
            report.lights.Add(FormattableString.Invariant($"{PathOf(light.transform)}: {light.type}, range {light.range:F2}, intensity {light.intensity:F2}, at {light.transform.position.ToString("F2")}"));

        // ---- Physical ground (tiles are visual only in the other rooms; confirm here).
        var supportHit = Physics.RaycastAll(report.centre + Vector3.up * 1.3f, Vector3.down, 3, ~0, QueryTriggerInteraction.Ignore)
            .OrderBy(h => h.distance).FirstOrDefault();
        if (!supportHit.collider) throw new InvalidOperationException("No physical ground below the North-West Room centre.");
        report.floorSupport = new Point(PathOf(supportHit.transform), supportHit.point, supportHit.collider);
        report.physicalGroundY = supportHit.point.y;

        // ---- Player eye and interaction rules from the saved player, not assumed values.
        var player = All<PlayerCharacterController>(scene).FirstOrDefault();
        report.playerEyeHeight = player ? player.CapsuleHeightStanding * player.CameraHeightRatio : 1.6f;
        var interactor = All<KeycardInteractor>(scene).FirstOrDefault();
        report.maxAimAngle = interactor ? interactor.MaxAimAngle : 30f;

        // ---- Gates.
        var eastDoor = Single<SecurityDoor>(scene, EastDoorName);
        var bossDoor = Single<SecurityDoor>(scene, BossDoorName);
        report.gates.Add(MeasureGate(eastDoor, report));
        report.gates.Add(MeasureGate(bossDoor, report));

        // ---- Red terminal (room reward) and its encounter.
        var red = Single<KeycardTerminal>(scene, RedTerminalName);
        if (red.SpawnPoints == null || red.SpawnPoints.Length != 8 || red.SpawnPoints.Any(s => !s))
            throw new InvalidOperationException(RedTerminalName + " must reference exactly eight spawn markers; re-audit the gameplay configuration.");
        if (red.Doors == null || red.Doors.Any(d => !d))
            throw new InvalidOperationException(RedTerminalName + " has a missing lockdown door reference.");
        report.redTerminal = MeasureTerminal(red, report, "KeycardTerminal");
        report.redTerminal.cards.Add(red.Reward ? red.Reward.name : "NO REWARD");
        report.redTerminal.doors.AddRange(red.Doors.Select(d => d.name));
        report.minSpawnDistance = red.MinSpawnDistance;
        for (int i = 0; i < red.Waves.Length; i++)
            report.waves.Add("Wave " + (i + 1) + ": " + string.Join(", ", red.Waves[i].Enemies.Select(e => e ? e.name : "MISSING")));

        // ---- Master terminal and the card-insertion camera path.
        var master = Single<MasterTerminal>(scene, MasterTerminalName);
        if (master.Slots == null || master.Slots.Length != 3 || master.Slots.Any(s => !s) || !master.LookAt)
            throw new InvalidOperationException("Master Terminal must have three slots and a LookAt; re-audit the gameplay configuration.");
        report.masterTerminal = MeasureTerminal(master, report, "MasterTerminal");
        var mt = report.masterTerminal;
        mt.cards.AddRange(master.RequiredCards.Select(c => c ? c.name : "MISSING"));
        mt.doors.AddRange(master.Doors.Select(d => d ? d.name : "MISSING"));
        mt.lookAt = new Point(PathOf(master.LookAt), master.LookAt.position, master.LookAt);
        Vector3 masterEye = At(mt.standing.position, mt.eyeY);
        mt.lookAtVisible = Visible(masterEye, master.LookAt.position, master);
        Vector3 flat = Vector3.ProjectOnPlane(master.LookAt.position - masterEye, Vector3.up);
        mt.lookAtYaw = Quaternion.LookRotation(flat).eulerAngles.y;
        mt.lookAtPitch = -Mathf.Atan2(master.LookAt.position.y - masterEye.y, Mathf.Max(.05f, flat.magnitude)) * Mathf.Rad2Deg;
        foreach (var slot in master.Slots)
        {
            mt.slots.Add(new Point(PathOf(slot), slot.position, slot));
            mt.slotForwards.Add(slot.forward);
            mt.slotMouthVisible.Add(Visible(masterEye, slot.position - slot.forward * .16f, master));
        }
        // Reach area counts only positions from which the prompt, LookAt and every slot mouth are clear.
        mt.keepOut = KeepOut(master, mt, report, p => Visible(p, master.LookAt.position, master) &&
            master.Slots.All(s => Visible(p, s.position - s.forward * .16f, master)));

        // ---- Access machine outside the east door.
        var access = All<KeycardAccessPoint>(scene).Where(a => a.Doors != null && a.Doors.Contains(eastDoor)).ToArray();
        if (access.Length != 1) throw new InvalidOperationException($"Expected one access machine for {EastDoorName}, found {access.Length}.");
        report.accessMachine = new Point(PathOf(access[0].transform) + " (" + (access[0].RequiredCard ? access[0].RequiredCard.name : "NO CARD") + ")",
            access[0].transform.position, access[0]);
        var reader = access[0].Reader ? access[0].Reader : access[0].transform;
        Vector3 tapStand = reader.position + Vector3.ProjectOnPlane(reader.forward, Vector3.up).normalized * (interactor ? interactor.TapReach : .85f);
        tapStand.y = GroundY(tapStand, floorY);
        report.accessStanding = new Point("Access machine tap standing point", tapStand);

        // ---- Spawn markers.
        foreach (var marker in red.SpawnPoints)
        {
            var s = new Spawn { name = PathOf(marker), id = GlobalObjectId.GetGlobalObjectIdSlow(marker).ToString(), position = marker.position };
            s.onNavMesh = NavMesh.SamplePosition(marker.position, out var hit, NavTolerance, NavMesh.AllAreas);
            s.navPosition = s.onNavMesh ? hit.position : marker.position;
            s.navOffset = s.onNavMesh ? Vector3.Distance(hit.position, marker.position) : -1;
            // KeycardTerminal.WarpIn projects each marker within 3 m before instantiating the enemy.
            s.warpOnNavMesh = NavMesh.SamplePosition(marker.position, out var warp, WarpRadius, NavMesh.AllAreas);
            s.warpPosition = s.warpOnNavMesh ? warp.position : marker.position;
            s.warpOffset = s.warpOnNavMesh ? Flat(warp.position - marker.position).magnitude : -1;
            s.warpInsideDisc = s.warpOnNavMesh && s.warpOffset <= SpawnRadius;
            s.insideInterior = InsideXZ(marker.position, bounds);
            s.wallClearance = Mathf.Min(marker.position.x - low.x, high.x - marker.position.x, marker.position.z - low.z, high.z - marker.position.z);
            s.discInsideInterior = s.wallClearance >= SpawnRadius;
            s.bodyClear = BodyClear(new Vector3(marker.position.x, floorY, marker.position.z));
            s.centreDistance = Flat(marker.position - report.centre).magnitude;
            report.spawns.Add(s);
        }
        for (int i = 0; i < report.spawns.Count; i++)
        {
            var s = report.spawns[i];
            Vector2 p = XZ(s.position);
            foreach (var gate in report.gates)
                if (DiscHitsBox(p, SpawnRadius, gate.approach)) s.overlaps.Add(gate.name + " approach");
            if (DiscHitsBox(p, SpawnRadius, report.redTerminal.keepOut)) s.overlaps.Add("Red Terminal keep-out");
            if (DiscHitsBox(p, SpawnRadius, report.masterTerminal.keepOut)) s.overlaps.Add("Master Terminal keep-out");
            if (s.centreDistance - SpawnRadius < RingOuter && s.centreDistance + SpawnRadius > RingInner) s.overlaps.Add("Central annulus");
            for (int j = 0; j < report.spawns.Count; j++)
                if (i != j && Flat(report.spawns[j].position - s.position).magnitude < SpawnRadius * 2) s.overlaps.Add("Spawn " + (j + 1) + " disc");
        }

        // ---- Occupants, agents and NavMesh data.
        foreach (var agent in All<NavMeshAgent>(scene))
            if (InsideXZ(agent.transform.position, bounds))
            {
                report.occupants.Add(new Point(PathOf(agent.transform), agent.transform.position, agent));
                report.navigation.Add(FormattableString.Invariant($"{PathOf(agent.transform)}: agentType={agent.agentTypeID}, radius={agent.radius}, height={agent.height}, baseOffset={agent.baseOffset}"));
            }
        foreach (var prefab in red.Waves.SelectMany(w => w.Enemies).Where(e => e).Distinct())
            foreach (var agent in prefab.GetComponentsInChildren<NavMeshAgent>(true))
                report.navigation.Add(FormattableString.Invariant($"Wave prefab {prefab.name}: agentType={agent.agentTypeID}, radius={agent.radius}, height={agent.height}"));
        foreach (var component in All<MonoBehaviour>(scene).Where(m => m && m.GetType().Name == "NavMeshSurface"))
        {
            var data = new SerializedObject(component).FindProperty("m_NavMeshData")?.objectReferenceValue;
            report.navigation.Add("Surface " + PathOf(component.transform) + ": " + (data ? AssetDatabase.GetAssetPath(data) : "NO DATA"));
        }
        var settings = NavMesh.GetSettingsByID(0);
        report.navigation.Add(FormattableString.Invariant($"Baked agent 0: radius={settings.agentRadius}, height={settings.agentHeight}, climb={settings.agentClimb}, slope={settings.agentSlope}"));
        var triangulation = NavMesh.CalculateTriangulation();
        report.navigation.Add($"Loaded scene NavMesh: {triangulation.vertices.Length} vertices, {triangulation.indices.Length / 3} triangles");
        foreach (var door in new[] { eastDoor, bossDoor })
            report.navigation.Add($"{door.name}: NavMeshObstacle {(door.Obstacle ? (door.Obstacle.carving ? "carving" : "non-carving") + (door.Obstacle.enabled ? ", enabled" : ", disabled") : "none")}");

        // ---- Interior grid: shell floor colliders, physical support and NavMesh.
        var ground = new Dictionary<string, int>();
        for (float x = low.x + 1; x < high.x - .5f; x += 1)
            for (float z = low.z + 1; z < high.z - .5f; z += 1)
            {
                var p = new Vector3(x, floorY, z); report.navSamples++;
                if (!shellColliders.Any(c => c.Raycast(new Ray(p + Vector3.up * .5f, Vector3.down), out _, .6f)))
                    report.shellFloorSamplesMissing++;
                if (Physics.Raycast(p + Vector3.up * .5f, Vector3.down, out var g, .8f, ~0, QueryTriggerInteraction.Ignore))
                {
                    string key = PathOf(g.transform) + FormattableString.Invariant($" (Y {g.point.y:F3})");
                    ground[key] = ground.TryGetValue(key, out int n) ? n + 1 : 1;
                }
                else report.floorSupportMisses.Add(p);
                if (NavMesh.SamplePosition(p, out _, NavTolerance, NavMesh.AllAreas)) report.navHits++;
                else report.navMisses.Add(p);
            }
        report.groundColliders.AddRange(ground.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + ": " + kv.Value + " samples"));

        // ---- Central combat annulus about the true centre.
        for (float radius = RingInner; radius <= RingOuter; radius += 1)
            for (int i = 0; i < 72; i++)
            {
                float angle = i * Mathf.PI * 2 / 72;
                Vector3 p = report.centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                report.ringSamples++;
                if (NavMesh.SamplePosition(p, out _, NavTolerance, NavMesh.AllAreas)) report.ringNavHits++;
                var overlaps = BodyOverlaps(p);
                if (overlaps.Any(c => c.GetComponentInParent<NavMeshAgent>() == null)) report.ringBodyBlocked++;
                if (overlaps.Any(c => c.GetComponentInParent<NavMeshAgent>() != null)) report.ringActorOverlaps++;
            }

        // ---- Routes. Connectors (spawn, gate and terminal to ring) define the 1.5 m half-width reservations.
        var eastGate = report.gates[0]; var bossGate = report.gates[1];
        Vector3 eastInside = Floor(eastGate.position - eastGate.outward * 3, floorY), eastOutside = Floor(eastGate.position + eastGate.outward * 3, floorY);
        Vector3 bossInside = Floor(bossGate.position - bossGate.outward * 3, floorY);
        Vector3 redStand = report.redTerminal.standing.position, masterStand = report.masterTerminal.standing.position;
        for (int i = 0; i < report.spawns.Count; i++)
        {
            var p = report.spawns[i].position; string n = "Spawn " + (i + 1);
            AddRoute(report, n + " to ring", p, RingEntry(report, p), true);
            AddRoute(report, n + " to east door inside", p, eastInside);
            AddRoute(report, n + " to Red Terminal", p, redStand);
            AddRoute(report, n + " to Master Terminal", p, masterStand);
        }
        AddRoute(report, EastDoorName + " inside to ring", eastInside, RingEntry(report, eastInside), true);
        AddRoute(report, BossDoorName + " inside to ring", bossInside, RingEntry(report, bossInside), true);
        AddRoute(report, "Ring to Red Terminal", RingEntry(report, redStand), redStand, true);
        AddRoute(report, "Ring to Master Terminal", RingEntry(report, masterStand), masterStand, true);
        AddRoute(report, "East door inside to Master Terminal", eastInside, masterStand);
        // The progression lane: after card insertion the player walks to, and should see, the unlocking boss door.
        AddRoute(report, "Master Terminal to boss door inside", masterStand, bossInside, true);
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2 / 24, b = (i + 1) * Mathf.PI * 2 / 24;
            AddRoute(report, "Ring segment " + (i + 1), report.centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * RingConnector,
                report.centre + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * RingConnector);
        }
        AddRoute(report, EastDoorName + " across saved gate", eastOutside, eastInside);
        var centreNorth = Single<SecurityDoor>(scene, CentreNorthGateName);
        Vector3 centreOut = centreNorth.transform.right;
        if (Vector3.Dot(centreOut, eastDoor.transform.position - centreNorth.transform.position) < 0) centreOut = -centreOut;
        Vector3 centreExterior = Floor(centreNorth.transform.position + centreOut * 3, floorY);
        AddRoute(report, "Centre Room north gate exterior to east door exterior", centreExterior, eastOutside);
        AddRoute(report, "Centre Room north gate exterior to access machine", centreExterior, tapStand);
        AddRoute(report, "Access machine to east door exterior", tapStand, eastOutside);

        // ---- Boss-door approach profile up to (and onto) Corridor Incline Full.
        var incline = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name == InclineName).ToArray();
        if (incline.Length != 1) throw new InvalidOperationException($"Expected one '{InclineName}', found {incline.Length}.");
        var inclineBounds = new Bounds(incline[0].position, Vector3.zero);
        foreach (var r in incline[0].GetComponentsInChildren<Renderer>()) inclineBounds.Encapsulate(r.bounds);
        foreach (var c in incline[0].GetComponentsInChildren<Collider>()) inclineBounds.Encapsulate(c.bounds);
        report.incline = new Box(PathOf(incline[0]), inclineBounds, incline[0].gameObject);
        float groundY = floorY;
        for (float d = 0; d <= 24; d += .5f)
        {
            Vector3 p = bossGate.position + bossGate.outward * d;
            var sample = new ProfileSample { distance = d, position = new Vector3(p.x, groundY, p.z), groundY = float.NaN, ground = "NO GROUND" };
            var hits = Physics.RaycastAll(new Vector3(p.x, groundY + 2, p.z), Vector3.down, 5, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => !h.collider.transform.IsChildOf(bossDoor.transform)).OrderBy(h => h.distance).ToArray();
            if (hits.Length > 0)
            {
                groundY = sample.groundY = hits[0].point.y;
                sample.position = hits[0].point;
                sample.ground = PathOf(hits[0].transform);
                sample.onIncline = hits[0].transform.IsChildOf(incline[0]);
            }
            else sample.groundY = -999;
            sample.onNavMesh = NavMesh.SamplePosition(sample.position, out _, NavTolerance, NavMesh.AllAreas);
            report.bossApproach.Add(sample);
            // The incline object's flat threshold begins at the door; its start is the first rise above the room floor.
            if (sample.onIncline && sample.groundY > floorY + .05f && report.inclineStartDistance < 0)
            { report.inclineStartDistance = d; report.inclineStart = sample.position; }
            if (!sample.onIncline && report.inclineStartDistance >= 0 && sample.groundY > floorY + 5 && report.bossHallFloorDistance < 0)
            { report.bossHallFloorDistance = d; report.bossHallFloor = sample.position; report.bossHallFloorY = sample.groundY; }
        }
        // First threshold sample beyond the door leaves that lies on the loaded NavMesh.
        var threshold = report.bossApproach.FirstOrDefault(s => s.distance >= .5f && s.onNavMesh && Mathf.Abs(s.groundY - floorY) < .05f);
        if (threshold == null || report.inclineStartDistance < 0 || report.bossHallFloorDistance < 0)
            throw new InvalidOperationException("Boss-door approach profile did not find the threshold, incline rise and Boss Hall floor.");
        report.bossThreshold = threshold.position;
        AddRoute(report, BossDoorName + " across saved gate", bossInside, report.bossThreshold);
        AddRoute(report, BossDoorName + " threshold to incline start", report.bossThreshold, report.inclineStart);
        AddRoute(report, BossDoorName + " threshold to Boss Hall floor", report.bossThreshold, report.bossHallFloor);

        // ---- Objectives and game flow (records the possible-early-win risk; changes nothing).
        foreach (var objective in All<Objective>(scene))
        {
            string detail = objective is ObjectiveKillEnemies kill ? $", mustKillAll={kill.MustKillAllEnemies}, kills={kill.KillsToCompleteObjective}"
                : objective is ObjectiveKeycards keys ? ", terminal=" + (keys.Terminal ? keys.Terminal.name : "NONE") : "";
            report.objectives.Add($"{PathOf(objective.transform)}: {objective.GetType().Name}, optional={objective.IsOptional}{detail}");
        }
        foreach (var flow in All<GameFlowManager>(scene))
            report.objectives.Add(FormattableString.Invariant($"{PathOf(flow.transform)}: GameFlowManager, win scene {flow.WinSceneName}, end delay {flow.EndSceneLoadDelay:F1} s"));

        BuildZones(report);

        report.notes.Add("All values are world metres; east is +X, north is +Z. Wall planes are the median of 26 shell-collider hits per wall (13 stations at floor+1.2 and +5 m); hits more than 0.25 m from the median are listed as openings or recesses.");
        report.notes.Add("Gate scans use 0.02 m conservative sample intervals at floor+0.2, +1.2 and +1.8 m through 4 m depth. Only moving leaves and the explicit blocker are excluded; the visible aperture also tests shell/frame triangles and the leaves translated by their configured open slide. No transform or collider state is changed.");
        report.notes.Add("Terminal eyes use the saved player's CapsuleHeightStanding x CameraHeightRatio above the physical ground under the standing point; the prompt rule is KeycardInteractor's range, aim-angle and occlusion test. Reach samples are 0.25 m floor points whose eye is in range, whose 0.4 m capsule is clear and whose sightlines are clear.");
        report.notes.Add("Spawn warp positions repeat KeycardTerminal.WarpIn's 3 m NavMesh projection. A wave larger than the far-enough marker set reuses markers nudged 1.5 m, inside the 2 m disc.");
        report.notes.Add("NavMesh queries describe loaded Edit-mode data, not runtime door carving or an AI/Play-mode test (0.3 m sample tolerance). Across-gate paths describe the saved locked doors.");
        report.notes.Add("Zones are design reservations derived from the measured keep-outs, not scene objects. Nothing in this report authorises moving doors, terminals, markers, access machines or triggers.");
        if (scene.isDirty || Hash(ScenePath) != report.sceneSha256)
            throw new InvalidOperationException("Scene changed during survey; do not accept this report.");
        return report;
    }

    static float WallPlane(Report report, string wall, Collider[] shell, Bounds provisional, Vector3 direction, float floorY)
    {
        bool xWall = direction.x != 0;
        Vector3 along = xWall ? Vector3.forward : Vector3.right;
        float span = xWall ? provisional.size.z : provisional.size.x;
        var hits = new List<(string label, float value)>();
        var sample = new WallSample { wall = wall };
        for (int k = -6; k <= 6; k++)
            foreach (float h in new[] { 1.2f, 5f })
            {
                Vector3 origin = new Vector3(provisional.center.x, floorY + h, provisional.center.z) + along * (k * (span / 2 - 1.5f) / 6);
                string label = FormattableString.Invariant($"{(xWall ? "Z" : "X")} {(xWall ? origin.z : origin.x):F2}, +{h:F1} m");
                float distance = float.MaxValue; Vector3 point = default;
                foreach (var c in shell)
                    if (c.Raycast(new Ray(origin, direction), out var hit, 40) && hit.distance < distance) { distance = hit.distance; point = hit.point; }
                if (distance == float.MaxValue) sample.deviations.Add(label + ": no shell hit within 40 m");
                else hits.Add((label, xWall ? point.x : point.z));
            }
        if (hits.Count == 0) throw new InvalidOperationException("No " + wall + " wall surface.");
        var sorted = hits.Select(h => h.value).OrderBy(v => v).ToArray();
        float median = sorted[sorted.Length / 2];
        var good = hits.Where(h => Mathf.Abs(h.value - median) <= .25f).ToArray();
        foreach (var h in hits.Where(h => Mathf.Abs(h.value - median) > .25f))
            sample.deviations.Add(h.label + FormattableString.Invariant($": {h.value:F3} ({h.value - median:+0.000;-0.000} m)"));
        sample.plane = median; sample.hits = good.Length; sample.openings = hits.Count - good.Length;
        sample.spread = good.Max(h => h.value) - good.Min(h => h.value);
        report.walls.Add(sample);
        return median;
    }

    static Terminal MeasureTerminal(KeycardInteractable terminal, Report report, string type)
    {
        var t = new Terminal { name = PathOf(terminal.transform), id = GlobalObjectId.GetGlobalObjectIdSlow(terminal).ToString(), type = type,
            position = terminal.transform.position, forward = terminal.transform.forward, range = terminal.Range,
            eyeHeight = report.playerEyeHeight, maxAimAngle = report.maxAimAngle };
        if (Vector3.Dot(Flat(t.forward), Flat(report.centre - t.position)) <= 0)
            throw new InvalidOperationException(terminal.name + " does not face into the room; re-audit its placement.");
        t.focus = new Point("Focus", terminal.FocusPosition);
        Vector3 standing = terminal.FocusPosition + Flat(terminal.transform.forward).normalized * 1.6f;
        standing.y = report.floorTopY;
        t.standing = new Point("Standing point", standing);
        t.standingInside = InsideXZ(standing, report.interior.ToBounds());
        t.standingClear = BodyClear(standing);
        t.eyeY = GroundY(standing, report.floorTopY) + t.eyeHeight;
        Vector3 eye = new Vector3(standing.x, t.eyeY, standing.z);
        t.eyeDistance = Vector3.Distance(eye, terminal.FocusPosition);
        t.focusVisible = Visible(eye, terminal.FocusPosition, terminal);
        Vector3 flat = Flat(terminal.FocusPosition - eye);
        t.pitchToFocus = Mathf.Atan2(eye.y - terminal.FocusPosition.y, flat.magnitude) * Mathf.Rad2Deg;
        t.promptWhenLookingLevel = t.eyeDistance <= t.range && Mathf.Abs(t.pitchToFocus) <= t.maxAimAngle && t.focusVisible;
        float dy = terminal.FocusPosition.y - t.eyeY;
        t.reachRadiusAtEye = dy * dy < t.range * t.range ? Mathf.Sqrt(t.range * t.range - dy * dy) : 0;
        var body = new Bounds(terminal.transform.position, Vector3.zero);
        foreach (var r in terminal.GetComponentsInChildren<Renderer>()) body.Encapsulate(r.bounds);
        t.body = new Box("Terminal body", body, terminal);
        t.keepOut = KeepOut(terminal, t, report, null);
        return t;
    }

    // Terminal body, the 3 x 3 m standing square and every sampled position the prompt is usable from, through the movement band.
    static Box KeepOut(KeycardInteractable terminal, Terminal t, Report report, Func<Vector3, bool> extra)
    {
        Bounds box = t.body.ToBounds();
        box.Encapsulate(t.standing.position + new Vector3(1.5f, 0, 1.5f));
        box.Encapsulate(t.standing.position - new Vector3(1.5f, 0, 1.5f));
        var room = report.interior.ToBounds();
        t.reachSamples = t.reachUsable = 0;
        Vector3 focus = terminal.FocusPosition;
        for (float x = -t.range; x <= t.range; x += .25f)
            for (float z = -t.range; z <= t.range; z += .25f)
            {
                Vector3 feet = new Vector3(focus.x + x, report.floorTopY, focus.z + z);
                if (!InsideXZ(feet, room)) continue;
                Vector3 eye = new Vector3(feet.x, GroundY(feet, report.floorTopY) + t.eyeHeight, feet.z);
                if (Vector3.Distance(eye, focus) > t.range) continue;
                t.reachSamples++;
                if (!BodyClear(feet) || !Visible(eye, focus, terminal) || (extra != null && !extra(eye))) continue;
                t.reachUsable++;
                box.Encapsulate(feet + new Vector3(.4f, 0, .4f));
                box.Encapsulate(feet - new Vector3(.4f, 0, .4f));
            }
        box.SetMinMax(new Vector3(Mathf.Max(box.min.x, room.min.x), report.floorTopY, Mathf.Max(box.min.z, room.min.z)),
            new Vector3(Mathf.Min(box.max.x, room.max.x), report.movementBandTopY, Mathf.Min(box.max.z, room.max.z)));
        return new Box(terminal.name + " access, reach and sightline", box);
    }

    static Gate MeasureGate(SecurityDoor door, Report report)
    {
        if (!door || !door.Blocker || !door.LeafA || !door.LeafB) throw new InvalidOperationException("Incomplete gate anchors on " + door?.name);
        Vector3 outward = door.transform.right;
        bool agrees = Vector3.Dot(Flat(outward), Flat(door.transform.position - report.centre)) > 0;
        if (!agrees) outward = -outward;
        var g = new Gate { name = door.name, id = GlobalObjectId.GetGlobalObjectIdSlow(door).ToString(), position = door.transform.position,
            outward = outward, outwardIsTransformRight = agrees, along = door.transform.forward, startsLocked = door.StartLocked,
            hasObstacle = door.Obstacle, blocker = new Box("Closed blocker", door.Blocker.bounds, door.Blocker) };
        var structure = new Bounds(door.transform.position, Vector3.zero);
        foreach (var r in door.GetComponentsInChildren<Renderer>()) structure.Encapsulate(r.bounds);
        g.structure = new Box(door.name + " structure", structure);
        if (door.Lamps != null) g.lamps.AddRange(door.Lamps.Where(l => l).Select(l => l.bounds.center));
        g.lampLight = door.LampLight ? door.LampLight.transform.position : Vector3.zero;
        float floor = report.floorTopY;
        // Keep fixed frame and shell colliders, including adjacent corridor structure. Ignore only moving parts.
        var candidates = solids.Where(c => c != door.Blocker && !c.transform.IsChildOf(door.LeafA) && !c.transform.IsChildOf(door.LeafB) &&
            Vector3.Distance(c.bounds.ClosestPoint(g.position + Vector3.up), g.position + Vector3.up) < 7).ToArray();
        g.inspectedColliders.AddRange(candidates.Select(c => PathOf(c.transform)).OrderBy(n => n));
        Func<float, float, bool> blocked = (offset, y) => candidates.Any(c => c.Raycast(
            new Ray(new Vector3(g.position.x, y, g.position.z) + g.along * offset - g.outward * 2, g.outward), out _, 4));
        float lo = -5, hi = 5;
        foreach (float height in new[] { .2f, 1.2f, 1.8f })
        {
            // Select the run containing the measured gate pivot, never the largest neighbouring opening.
            if (blocked(0, floor + height)) throw new InvalidOperationException(door.name + " centre blocked at height " + height);
            float l = 0, h = 0;
            while (l > -5 && !blocked(l - Step, floor + height)) l -= Step;
            while (h < 5 && !blocked(h + Step, floor + height)) h += Step;
            if (l <= -5 || h >= 5) throw new InvalidOperationException("Unbounded opening at " + door.name);
            lo = Mathf.Max(lo, l); hi = Mathf.Min(hi, h);
        }
        g.clearMin = lo; g.clearMax = hi;
        float lintel = floor + 1.8f;
        while (lintel < report.ceilingUndersideY && !blocked(0, lintel + Step)) lintel += Step;
        g.lintelY = lintel;
        // Imported gate meshes may carry no colliders, so test their triangles for the visible aperture too.
        var meshes = new List<MeshProbe>();
        foreach (var filter in All<MeshFilter>(door.gameObject.scene))
        {
            var renderer = filter.GetComponent<Renderer>();
            if (filter.sharedMesh && renderer && renderer.enabled && filter.gameObject.activeInHierarchy &&
                ShellRoot(filter.transform) != null && Vector3.Distance(renderer.bounds.ClosestPoint(g.position + Vector3.up), g.position + Vector3.up) < 7)
                meshes.Add(new MeshProbe(filter, Vector3.zero));
        }
        foreach (var filter in door.GetComponentsInChildren<MeshFilter>())
        {
            Vector3 offset = Vector3.zero;
            if (filter.transform.IsChildOf(door.LeafA)) offset = door.LeafA.parent.TransformVector(door.SlideA);
            else if (filter.transform.IsChildOf(door.LeafB)) offset = door.LeafB.parent.TransformVector(door.SlideB);
            else if (filter.name != "Frame") continue;
            meshes.Add(new MeshProbe(filter, offset));
        }
        Func<float, float, bool> visibleBlocked = (offset, y) => blocked(offset, y) || meshes.Any(m => m.Hit(
            new Ray(new Vector3(g.position.x, y, g.position.z) + g.along * offset - g.outward * 2, g.outward), 4));
        float visibleLo = lo, visibleHi = hi;
        foreach (float height in new[] { .2f, 1.2f, 1.8f })
        {
            if (visibleBlocked(0, floor + height)) throw new InvalidOperationException(door.name + " open mesh blocks centre at height " + height);
            float l = 0, h = 0;
            while (l - Step >= lo && !visibleBlocked(l - Step, floor + height)) l -= Step;
            while (h + Step <= hi && !visibleBlocked(h + Step, floor + height)) h += Step;
            visibleLo = Mathf.Max(visibleLo, l); visibleHi = Mathf.Min(visibleHi, h);
        }
        g.visibleOpenMin = visibleLo; g.visibleOpenMax = visibleHi;
        float visibleTop = floor + 1.8f;
        while (visibleTop < lintel && !visibleBlocked(0, visibleTop + Step)) visibleTop += Step;
        g.visibleLintelY = visibleTop;
        Vector3 a = g.position + g.along * (lo - 1) - g.outward * 4;
        Vector3 b = g.position + g.along * (hi + 1);
        var approach = new Bounds();
        approach.SetMinMax(new Vector3(Mathf.Min(a.x, b.x), floor, Mathf.Min(a.z, b.z)),
            new Vector3(Mathf.Max(a.x, b.x), floor + BodyBand, Mathf.Max(a.z, b.z)));
        g.approach = new Box(door.name + " approach", approach);
        return g;
    }

    // ---- Safe dressing zones, derived only from the measured reservations above.
    static void BuildZones(Report report)
    {
        var room = report.interior.ToBounds();
        float floor = report.floorTopY;
        var reservations = new List<(string name, Func<Vector2, bool> hit)>();
        foreach (var gate in report.gates) { var b = gate.approach; reservations.Add((gate.name + " approach", p => InBox(p, b))); }
        { var b = report.redTerminal.keepOut; reservations.Add(("Red Terminal keep-out", p => InBox(p, b))); }
        { var b = report.masterTerminal.keepOut; reservations.Add(("Master Terminal keep-out", p => InBox(p, b))); }
        for (int i = 0; i < report.spawns.Count; i++)
        { var c = XZ(report.spawns[i].position); reservations.Add(("Spawn " + (i + 1) + " disc", p => (p - c).magnitude <= SpawnRadius)); }
        { var c = XZ(report.centre); reservations.Add(("Central annulus and open centre", p => (p - c).magnitude <= RingOuter)); }
        foreach (var route in report.routes.Where(r => r.connector && r.corners.Length > 1))
        {
            var pts = route.corners.Select(XZ).ToArray();
            reservations.Add(("Connector: " + route.name, p =>
            {
                for (int i = 0; i + 1 < pts.Length; i++) if (SegmentDistance(p, pts[i], pts[i + 1]) <= ConnectorHalfWidth) return true;
                return false;
            }));
        }
        Func<Vector2, string> reservedBy = p =>
        {
            foreach (var r in reservations) if (r.hit(p)) return r.name;
            return null;
        };

        var walls = new[]
        {
            ("North", new Vector2(room.min.x, room.max.z), new Vector2(1, 0), new Vector2(0, -1), room.size.x),
            ("South", new Vector2(room.min.x, room.min.z), new Vector2(1, 0), new Vector2(0, 1), room.size.x),
            ("East", new Vector2(room.max.x, room.min.z), new Vector2(0, 1), new Vector2(-1, 0), room.size.z),
            ("West", new Vector2(room.min.x, room.min.z), new Vector2(0, 1), new Vector2(1, 0), room.size.z),
        };
        foreach (var (wall, origin, along, inward, length) in walls)
        {
            int n = Mathf.FloorToInt(length / .1f);
            var depth = new float[n]; var blocker = new string[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 at = origin + along * (i * .1f + .05f);
                depth[i] = MaxZoneDepth;
                for (float d = .05f; d <= MaxZoneDepth; d += .1f)
                {
                    string by = reservedBy(at + inward * d);
                    if (by == null) continue;
                    depth[i] = Mathf.Max(0, d - .1f); blocker[i] = by; break;
                }
            }
            Func<int, float> coord = i => (along.x != 0 ? origin.x : origin.y) + i * .1f;
            // Body-height floor zones: heavy machinery (3 m deep) and wall-flush services (1.2 m deep).
            foreach (var (kind, need) in new[] { ("heavy-perimeter", HeavyDepth), ("wall-flush", WallFlushDepth) })
            {
                int count = 0;
                for (int i = 0; i < n;)
                {
                    if (depth[i] < need) { i++; continue; }
                    int j = i; float minDepth = MaxZoneDepth;
                    while (j < n && depth[j] >= need) { minDepth = Mathf.Min(minDepth, depth[j]); j++; }
                    if ((j - i) * .1f >= 1f)
                        report.zones.Add(WallZone(wall + " " + kind + " " + ++count, kind, wall, origin, along, inward, i, j, need, minDepth,
                            floor, floor + ServicesTop, FormattableString.Invariant($"Floor to floor+{ServicesTop:F0} m, up to {need:F1} m from the wall face.")));
                    i = j;
                }
            }
            // Stretches where body-height content is limited, naming the first reservation met from the wall:
            // "reserved" allows none (free depth under 1.2 m); "flush-only" allows wall-flush content but no heavy machinery.
            foreach (var (kind, below, above) in new[] { ("reserved", 0f, WallFlushDepth), ("flush-only", WallFlushDepth, HeavyDepth) })
                for (int i = 0; i < n;)
                {
                    if (depth[i] < below || depth[i] >= above) { i++; continue; }
                    int j = i; var names = new HashSet<string>(); float minDepth = MaxZoneDepth;
                    while (j < n && depth[j] >= below && depth[j] < above)
                    { if (blocker[j] != null) names.Add(blocker[j]); minDepth = Mathf.Min(minDepth, depth[j]); j++; }
                    report.zones.Add(WallZone(wall + " " + kind + " " + FormattableString.Invariant($"{coord(i):F1}"), kind, wall, origin, along, inward, i, j,
                        above, minDepth, floor, report.movementBandTopY, (kind == "reserved" ? "No body-height dressing: " : "Wall-flush only; limited by: ") +
                        string.Join("; ", names.OrderBy(s => s))));
                    i = j;
                }
            // Wall services above the movement band, excluding the vertical columns over gates and terminals.
            var columns = new List<(float a, float b, float top, string name)>();
            foreach (var gate in report.gates.Where(g => OnWall(g.position, room) == wall))
            {
                float a = Coord(gate.approach.min, along), b = Coord(gate.approach.max, along);
                columns.Add((a, b, Mathf.Max(gate.structure.max.y, gate.lintelY) + .5f, gate.name));
            }
            foreach (var t in new[] { report.redTerminal, report.masterTerminal }.Where(t => OnWall(t.position, room) == wall))
                columns.Add((Coord(t.body.min, along) - .5f, Coord(t.body.max, along) + .5f, t.body.max.y + 1f, t.name.Split('/').Last()));
            float start = along.x != 0 ? origin.x : origin.y, end = start + length;
            foreach (var c in columns)
            {
                var a = Corner(origin, along, inward, c.a - start, 0, floor);
                var b = Corner(origin, along, inward, c.b - start, WallFlushDepth, c.top);
                report.zones.Add(new Zone { name = wall + " column " + c.name, kind = "column", wall = wall, min = Vector3.Min(a, b), max = Vector3.Max(a, b),
                    notes = FormattableString.Invariant($"Keep clear from the floor to Y {c.top:F2} (status lamps, lintel, prompt and readability); wall services may pass above.") });
            }
            int s = 0;
            foreach (var (a, b) in Subtract(start, end, columns.Select(c => (c.a, c.b))))
                if (b - a >= 1f)
                {
                    var lo = Corner(origin, along, inward, a - start, 0, report.movementBandTopY);
                    var hi = Corner(origin, along, inward, b - start, WallFlushDepth, floor + ServicesTop);
                    report.zones.Add(new Zone { name = wall + " services " + ++s, kind = "wall-services", wall = wall,
                        min = Vector3.Min(lo, hi), max = Vector3.Max(lo, hi), minFreeDepth = WallFlushDepth,
                        notes = "Wall-mounted conduits, trays and panels above the movement band; do not drop to the floor inside reserved stretches." });
                }
        }
        // Corners: the largest square from each corner clear of every reservation.
        foreach (var (name, cx, cz, sx, sz) in new[] { ("South-West", room.min.x, room.min.z, 1, 1), ("South-East", room.max.x, room.min.z, -1, 1),
                     ("North-West", room.min.x, room.max.z, 1, -1), ("North-East", room.max.x, room.max.z, -1, -1) })
        {
            float best = 0; string by = null;
            for (float size = .2f; size <= 10 && by == null; size += .2f)
            {
                for (float u = .05f; u <= size && by == null; u += .2f)
                    for (float v = .05f; v <= size && by == null; v += .2f)
                        by = reservedBy(new Vector2(cx + sx * u, cz + sz * v));
                if (by == null) best = size;
            }
            report.zones.Add(new Zone { name = name + " corner", kind = "corner", wall = name,
                min = new Vector3(Mathf.Min(cx, cx + sx * best), floor, Mathf.Min(cz, cz + sz * best)),
                max = new Vector3(Mathf.Max(cx, cx + sx * best), floor + ServicesTop, Mathf.Max(cz, cz + sz * best)), minFreeDepth = best,
                notes = FormattableString.Invariant($"Largest clear square {best:F1} m; limited by {by ?? "the 10 m search"}.") });
        }
        // Overhead band: above every existing non-shell object in the room and the wall-services band, below the ceiling.
        float existingTop = report.existingContent.Count > 0 ? report.existingContent.Max(b => b.max.y) : floor;
        float overheadMin = Mathf.Max(floor + ServicesTop, existingTop + .5f);
        report.zones.Add(new Zone { name = "Overhead", kind = "overhead", wall = "All",
            min = new Vector3(room.min.x + .3f, overheadMin, room.min.z + .3f), max = new Vector3(room.max.x - .3f, report.ceilingUndersideY - .3f, room.max.z - .3f),
            notes = FormattableString.Invariant($"Highest existing non-shell content top Y {existingTop:F2}. Any overhead structure needs an intersection validation (PLAN lesson 11) and must not change vertical gameplay.") });
    }

    static Zone WallZone(string name, string kind, string wall, Vector2 origin, Vector2 along, Vector2 inward, int i, int j, float depth, float minDepth, float y0, float y1, string notes)
    {
        var a = Corner(origin, along, inward, i * .1f, 0, y0); var b = Corner(origin, along, inward, j * .1f, depth, y1);
        return new Zone { name = name, kind = kind, wall = wall, min = Vector3.Min(a, b), max = Vector3.Max(a, b), minFreeDepth = minDepth, notes = notes };
    }

    static Vector3 Corner(Vector2 origin, Vector2 along, Vector2 inward, float s, float d, float y)
    {
        Vector2 p = origin + along * s + inward * d;
        return new Vector3(p.x, y, p.y);
    }

    static float Coord(Vector3 p, Vector2 along) => along.x != 0 ? p.x : p.z;

    static string OnWall(Vector3 p, Bounds room)
    {
        var d = new[] { ("North", room.max.z - p.z), ("South", p.z - room.min.z), ("East", room.max.x - p.x), ("West", p.x - room.min.x) };
        return d.OrderBy(x => Mathf.Abs(x.Item2)).First().Item1;
    }

    static IEnumerable<(float, float)> Subtract(float start, float end, IEnumerable<(float a, float b)> cuts)
    {
        float cursor = start;
        foreach (var c in cuts.OrderBy(c => c.a))
        {
            if (c.a > cursor) yield return (cursor, Mathf.Min(c.a, end));
            cursor = Mathf.Max(cursor, c.b);
        }
        if (cursor < end) yield return (cursor, end);
    }

    // Two-sided Moller-Trumbore ray/triangle intersection in world metres; no temporary colliders or scene edits.
    sealed class MeshProbe
    {
        readonly Vector3[] vertices;
        readonly int[] triangles;
        public MeshProbe(MeshFilter filter, Vector3 offset)
        {
            vertices = filter.sharedMesh.vertices.Select(v => filter.transform.TransformPoint(v) + offset).ToArray();
            triangles = filter.sharedMesh.triangles;
        }
        public bool Hit(Ray ray, float reach)
        {
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], e1 = vertices[triangles[i + 1]] - a, e2 = vertices[triangles[i + 2]] - a;
                Vector3 p = Vector3.Cross(ray.direction, e2);
                float determinant = Vector3.Dot(e1, p);
                if (Mathf.Abs(determinant) < .000001f) continue;
                float inverse = 1 / determinant;
                Vector3 t = ray.origin - a;
                float u = Vector3.Dot(t, p) * inverse;
                if (u < 0 || u > 1) continue;
                Vector3 q = Vector3.Cross(t, e1);
                float v = Vector3.Dot(ray.direction, q) * inverse;
                if (v < 0 || u + v > 1) continue;
                float distance = Vector3.Dot(e2, q) * inverse;
                if (distance >= 0 && distance <= reach) return true;
            }
            return false;
        }
    }

    static Vector3 Surface(Collider[] colliders, Vector3 origin, Vector3 direction)
    {
        float distance = float.MaxValue; Vector3 result = default;
        foreach (var c in colliders)
            if (c.Raycast(new Ray(origin, direction), out var hit, 50) && hit.distance < distance)
            { distance = hit.distance; result = hit.point; }
        if (distance == float.MaxValue) throw new InvalidOperationException("No fixed shell surface from " + origin + " along " + direction);
        return result;
    }

    static Transform ShellRoot(Transform t)
    {
        // Original shell prefab instance roots only: generated dressing names never define the room.
        var root = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
        if (!root || root.transform.parent != null) return null;
        string path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root));
        return path.StartsWith("Assets/SciFi Warehouse Kit/", StringComparison.Ordinal) &&
            (root.name.StartsWith("Wall ", StringComparison.Ordinal) || root.name.StartsWith("Alternate Wall", StringComparison.Ordinal) ||
             root.name.StartsWith("Floor Tile", StringComparison.Ordinal)) ? root.transform : null;
    }

    static bool IsTile(Transform t)
    {
        var root = ShellRoot(t);
        return root && root.name.StartsWith("Floor Tile", StringComparison.Ordinal);
    }

    static string GroupKey(Transform t)
    {
        var root = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
        if (root) return PathOf(root.transform);
        var parts = PathOf(t).Split('/');
        return string.Join("/", parts.Take(Mathf.Min(2, parts.Length)));
    }

    static IEnumerable<string> Overlaps(List<ShellPiece> pieces)
    {
        for (int a = 0; a < pieces.Count; a++)
            for (int b = a + 1; b < pieces.Count; b++)
            {
                var min = Vector3.Max(pieces[a].min, pieces[b].min);
                var max = Vector3.Min(pieces[a].max, pieces[b].max);
                if (max.x - min.x > .05f && max.z - min.z > .05f && max.y - min.y > .01f)
                    yield return pieces[a].name + " <> " + pieces[b].name + "; intersection " + min.ToString("F3") + " to " + max.ToString("F3");
            }
    }

    static bool Visible(Vector3 eye, Vector3 target, KeycardInteractable owner)
    {
        Vector3 delta = target - eye;
        float distance = delta.magnitude;
        if (distance < .05f) return true;
        // Mirrors KeycardInteractor.IsOccluded: the terminal's own colliders never occlude it.
        return !Physics.RaycastAll(eye, delta / distance, distance - .05f, ~0, QueryTriggerInteraction.Ignore)
            .Any(h => !h.collider.transform.IsChildOf(owner.transform) && h.collider.GetComponentInParent<KeycardInteractable>() != owner);
    }

    static float GroundY(Vector3 p, float fallback) =>
        Physics.Raycast(new Vector3(p.x, fallback + 1.3f, p.z), Vector3.down, out var hit, 3, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : fallback;

    static Collider[] BodyOverlaps(Vector3 feet) => Physics.OverlapCapsule(feet + Vector3.up * .45f,
        feet + Vector3.up * 1.45f, .4f, ~0, QueryTriggerInteraction.Ignore);
    static bool BodyClear(Vector3 feet) => BodyOverlaps(feet).Length == 0;

    static bool InsideXZ(Vector3 p, Bounds b) => p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z;
    static bool Inside2(ShellPiece s, Bounds b) => InsideXZ((s.min + s.max) * .5f, b);
    static bool Near(Bounds r, Bounds room, float margin) => r.center.x >= room.min.x - margin && r.center.x <= room.max.x + margin &&
        r.center.z >= room.min.z - margin && r.center.z <= room.max.z + margin;
    static bool InBox(Vector2 p, Box b) => p.x >= b.min.x && p.x <= b.max.x && p.y >= b.min.z && p.y <= b.max.z;
    static bool DiscHitsBox(Vector2 c, float r, Box b)
    {
        float dx = Mathf.Max(b.min.x - c.x, 0, c.x - b.max.x), dz = Mathf.Max(b.min.z - c.y, 0, c.y - b.max.z);
        return dx * dx + dz * dz < r * r;
    }
    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }
    static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    static Vector3 Floor(Vector3 p, float y) => new Vector3(p.x, y, p.z);
    static Bounds ToBounds(this Box box) { var b = new Bounds(); b.SetMinMax(box.min, box.max); return b; }

    static Vector3 RingEntry(Report report, Vector3 from)
    {
        Vector3 direction = Flat(from - report.centre);
        if (direction.sqrMagnitude < .01f) throw new InvalidOperationException("Gameplay anchor lies at the room centre.");
        return report.centre + direction.normalized * RingConnector;
    }

    static void AddRoute(Report report, string name, Vector3 start, Vector3 end, bool connector = false)
    {
        var route = new Route { name = name, start = start, end = end, connector = connector, corners = Array.Empty<Vector3>(), status = "Endpoint off NavMesh" };
        if (NavMesh.SamplePosition(start, out var a, NavTolerance, NavMesh.AllAreas) && NavMesh.SamplePosition(end, out var b, NavTolerance, NavMesh.AllAreas))
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
            route.status = path.status.ToString(); route.corners = path.corners;
            for (int i = 0; i + 1 < path.corners.Length; i++) route.length += Vector3.Distance(path.corners[i], path.corners[i + 1]);
        }
        report.routes.Add(route);
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

    static void Draw(SceneView view)
    {
        if (last == null || SceneManager.GetActiveScene().path != ScenePath ||
            SceneManager.GetActiveScene().isDirty || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Handles.color = Color.cyan;
        DrawBox(last.interior);
        foreach (var gate in last.gates) { DrawBox(gate.approach); Handles.Label(gate.position + Vector3.up * 2, gate.name); }
        Handles.color = Color.yellow;
        DrawBox(last.redTerminal.keepOut); DrawBox(last.masterTerminal.keepOut);
        for (int i = 0; i < last.spawns.Count; i++)
        { Handles.DrawWireDisc(last.spawns[i].position, Vector3.up, last.spawnRadius); Handles.Label(last.spawns[i].position, "Spawn " + (i + 1)); }
        Handles.color = Color.green;
        Handles.DrawWireDisc(last.centre, Vector3.up, last.ringInnerRadius);
        Handles.DrawWireDisc(last.centre, Vector3.up, last.ringOuterRadius);
        foreach (var route in last.routes.Where(r => r.connector && r.corners.Length > 1)) Handles.DrawAAPolyLine(3, route.corners);
        Handles.color = new Color(1, .5f, .2f);
        foreach (var zone in last.zones.Where(z => z.kind == "heavy-perimeter" || z.kind == "corner"))
            Handles.DrawWireCube((zone.min + zone.max) * .5f, zone.max - zone.min);
    }
    static void DrawBox(Box box) => Handles.DrawWireCube((box.min + box.max) * .5f, box.max - box.min);
}
