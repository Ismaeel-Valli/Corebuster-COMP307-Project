using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Read-only, repeatable saved-scene survey of the Boss Hall for corebuster_env_047. It never saves, builds, moves,
/// reparents or rebakes anything, and shares no code with the Centre or North-West surveys. Geometry comes from the
/// original kit colliders and renderers; navigation from the loaded baked NavMesh; actors from their saved components.
/// </summary>
public static class BossHallSurvey
{
    public const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    public const string Output = "ReviewCaptures/BossHall/047";
    public const string BossDoorName = "Boss Hall Door", InclineName = "Corridor Incline Full";
    const float NavTolerance = .3f, Grid = .25f, BodyBand = 2.6f, Headroom = 2.6f, EnvelopeRadius = 2f,
        ConnectorHalfWidth = 1.5f, WallFlushDepth = 1.2f, HeavyDepth = 3f, MaxZoneDepth = 8f, LowerServicesTop = 6f,
        SightHalfWidth = 2.5f, StairLanding = 2.5f, BayApproach = 4f, Step = .02f;
    // Provisional locator from the repository audit; every reported surface comes from measured hits around it.
    static readonly Vector3 Locator = new Vector3(-295.8f, 6.2f, 115.2f);
    static readonly Vector3 HallMin = new Vector3(-336f, 4f, 75f), HallMax = new Vector3(-258f, 30f, 155f);
    // North-West Room inside point 3 m in from the Boss Hall Door (NORTH_WEST_ROOM.md), for containment paths only.
    static readonly Vector3 NorthWestInside = new Vector3(-248.197f, .18f, 115.455f);

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
        public string name;
        public Vector3 position, navPosition;
        public bool onNavMesh;
        public Point(string label, Vector3 p, float tolerance = NavTolerance)
        {
            name = label; position = p;
            onNavMesh = NavMesh.SamplePosition(p, out var hit, tolerance, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - p.y) < .6f;
            navPosition = onNavMesh ? hit.position : p; // JsonUtility otherwise writes non-JSON Infinity values.
        }
    }
    [Serializable] public class WallSample
    {
        public string wall;
        public float plane, spread;
        public int hits;
        public List<string> deviations = new List<string>();
        public List<string> lowGaps = new List<string>();
    }
    [Serializable] public class Opening
    {
        public Vector3 centre, inward;
        public float clearMin, clearMax, width, lintelY;
        public List<string> inspectedColliders = new List<string>();
    }
    [Serializable] public class Deck
    {
        public string name;
        public int tier, pieces, samples, clearSamples, navSamples, navHits, navReachable;
        public Box bounds;
        public float topMin, topMax, bottomY, area, widthMin, widthP5, widthMedian, clearWidthMin, clearWidthP5, clearWidthMedian,
            maxStep, minHeadroom, centreLine;
        public string longAxis;
        public int railedEdge, wallEdge, openEdge, joinEdge;
        public List<string> openEdges = new List<string>();
        public List<string> gaps = new List<string>();
        public List<string> steps = new List<string>();
        public List<string> unreachable = new List<string>();
        public List<string> pinches = new List<string>();
        public List<string> members = new List<string>();
    }
    [Serializable] public class Stair
    {
        public string name, id, label, footSurface, topSurface, path, colliderLayers, profile, riserHeights;
        public int fromTier, toTier, risers;
        public Box bounds, steps;
        public Vector3 axis;
        public Point foot, top;
        public float footY, topY, rise, run, slopeDeg, width, clearWidth, maxRiser, typicalRiser, pathLength;
        public bool layersCollideWithPlayer;
    }
    [Serializable] public class BayDoor
    {
        public string name, id;
        public Box bounds, approach;
        public float faceX, deckTop;
        public bool closed;
        public string closedBy;
    }
    [Serializable] public class ProfileSample
    {
        public float distance, groundY;
        public Vector3 position;
        public string ground;
        public bool onNavMesh, turretVisible;
    }
    [Serializable] public class ActorInfo
    {
        public string name, id, prefab, kind, pathToEntry, pathToNorthWestRoom, entryBlocker;
        public Vector3 position, scale, aimPoint, navPosition;
        public float eulerY, agentRadius, agentHeight, speed, stoppingDistance, detectionRange, attackRange, maxHealth,
            navOffset, envelopeRadius, pathToEntryLength, entryDistance;
        public bool onNavMesh, visibleFromEntry, visibleFromLanding;
        public Box envelope;
    }
    [Serializable] public class Turret
    {
        public Box footprint, head, aimClearance, sightCorridor;
        public Vector3 pivot, firingOrigin, restForward;
        public float restYaw, detectionRange, attackRange, aimRadius;
        public List<string> visibility = new List<string>();
    }
    [Serializable] public class RailCollider
    {
        public string name;
        public int tier;
        public Box bounds;
        public float topAboveDeck;
        public bool crossesEye;
    }
    [Serializable] public class RailTest
    {
        public string station;
        public int tier, shots, railBlocked, otherBlocked;
        public Vector3 eye, outward;
        public bool levelBlocked;
        public string levelBlocker;
        public List<string> blockers = new List<string>();
    }
    [Serializable] public class ColumnClass
    {
        public string name;
        public int columns;
        public float minHeadroom, maxHeadroom;
    }
    [Serializable] public class Route
    {
        public string name, status;
        public bool connector;
        public Vector3 start, end;
        public float length;
        public Vector3[] corners;
    }
    [Serializable] public class Zone
    {
        public string name, kind, level;
        public Vector3 min, max;
        public float minFreeDepth;
        public string notes;
    }
    [Serializable] public class Report
    {
        public string unityVersion, sceneSha256, surveyedUtc;
        public float playerEyeHeight, playerRadius, playerHeight, playerStepOffset, playerSlopeLimit, crouchHeight;
        public int playerLayer;
        public Box interior;
        public Vector3 centre;
        public float floorTopY, physicalGroundY, ceilingUndersideY, skylightTopY, tier1TopY, tier2TopY, tier1UndersideY,
            tier2UndersideY, highestStructureTopY, movementBandTopY;
        public List<WallSample> walls = new List<WallSample>();
        public int floorTiles, offLevelFloorTiles, ceilingPieces, wallPieces, deckPieces, railPieces, disabledShellRenderers;
        public List<string> duplicateRenderers = new List<string>();
        public List<string> duplicateColliders = new List<string>();
        public List<string> floorOverlaps = new List<string>();
        public List<string> negativeScaleColliders = new List<string>();
        public List<string> materials = new List<string>();
        public List<string> shellNotes = new List<string>();
        public List<string> groundColliders = new List<string>();
        public Opening entry;
        public List<ProfileSample> incline = new List<ProfileSample>();
        public float turretRevealDistance = -1;
        public Vector3 northWestThreshold, inclineLanding, entryInside, entryEye, landingEye;
        public List<Deck> decks = new List<Deck>();
        public List<Box> deckFootprints = new List<Box>();
        public List<Stair> stairs = new List<Stair>();
        public List<BayDoor> bayDoors = new List<BayDoor>();
        public List<RailCollider> rails = new List<RailCollider>();
        public List<RailTest> railTests = new List<RailTest>();
        public List<ColumnClass> columns = new List<ColumnClass>();
        public List<ActorInfo> actors = new List<ActorInfo>();
        public Turret turret;
        public Vector3 focalWestPoint;
        public float focalYaw, focalDistance, focalOffset;
        public List<string> navigation = new List<string>();
        public int floorNavSamples, floorNavHits, floorNavReachable, roofNavSamples, roofNavHits, roofNavReachable;
        public List<string> floorUnreachable = new List<string>();
        public List<Point> keyPoints = new List<Point>();
        public List<Route> routes = new List<Route>();
        public List<Zone> zones = new List<Zone>();
        public List<string> objectives = new List<string>();
        public List<string> findings = new List<string>();
        public List<string> notes = new List<string>();
    }

    static Report last;
    static Collider[] solids;
    static HashSet<Collider> solidSet;

    [MenuItem("Tools/CoreBuster/Measure Boss Hall")]
    public static void MeasureMenu()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            last = Measure();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/Measurement.json", JsonUtility.ToJson(last, true));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        SceneView.duringSceneGui -= Draw;
        SceneView.duringSceneGui += Draw;
        SceneView.RepaintAll();
        Debug.Log("Boss Hall survey written to " + Output + "/Measurement.json. Scene overlay enabled; no scene saved.");
    }

    [MenuItem("Tools/CoreBuster/Hide Boss Hall Survey")]
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

    [MenuItem("Tools/CoreBuster/Capture Boss Hall Survey")]
    public static void CaptureMenu()
    {
        MeasureMenu();
        var r = last;
        var go = new GameObject("Boss Hall survey camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 70; camera.nearClipPlane = .05f; camera.farClipPlane = 300;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        camera.GetUniversalAdditionalCameraData().volumeLayerMask = ~0;
        try
        {
            float eye = r.playerEyeHeight;
            Vector3 turretHead = (r.turret.head.min + r.turret.head.max) * .5f;
            var north = r.decks.Where(d => d.tier == 1).OrderByDescending(d => d.bounds.max.z).First();
            var south = r.decks.Where(d => d.tier == 1).OrderBy(d => d.bounds.min.z).First();
            var upper = r.decks.Where(d => d.tier == 2).OrderByDescending(d => d.pieces).First();
            var bayEast = r.bayDoors.OrderByDescending(b => b.faceX).First();
            var views = new List<(string name, Vector3 eye, Vector3 target)>
            {
                ("Incline_Reveal", r.incline.First(s => s.distance >= Mathf.Max(0, r.turretRevealDistance)).position + Vector3.up * eye, turretHead),
                ("Entry_Landing", r.landingEye, turretHead),
                ("Entry_Inside", r.entryEye, turretHead),
                ("Arena_From_SouthEast", new Vector3(r.interior.max.x - 6, r.floorTopY + eye, r.interior.min.z + 12), turretHead),
                ("Arena_From_NorthWest", new Vector3(r.interior.min.x + 8, r.floorTopY + eye, r.interior.max.z - 12), turretHead),
                ("North_Gallery_West", new Vector3(r.interior.min.x + 10, north.topMax + eye, north.centreLine), new Vector3(r.interior.max.x, north.topMax, north.centreLine)),
                ("South_Gallery_East", new Vector3(r.interior.max.x - 10, south.topMax + eye, south.centreLine), new Vector3(r.interior.min.x, south.topMax, south.centreLine)),
                ("Upper_Reverse_From_West_Bay", new Vector3(r.interior.min.x + 5, upper.topMax + eye, (bayEast.bounds.min.z + bayEast.bounds.max.z) * .5f),
                    new Vector3(r.interior.max.x, r.floorTopY + 3, (bayEast.bounds.min.z + bayEast.bounds.max.z) * .5f)),
                ("Upper_Over_Turret", new Vector3(turretHead.x, upper.topMax + eye, r.interior.min.z + 24), turretHead),
                ("Turret_Close", new Vector3(turretHead.x + 9, r.floorTopY + eye, turretHead.z - 6), turretHead),
            };
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
        if (SceneManager.GetActiveScene().isDirty || Hash(ScenePath) != r.sceneSha256)
            throw new InvalidOperationException("Scene changed during capture; do not save it.");
    }

    // ------------------------------------------------------------------------------------------------------------
    public static Report Measure()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.isDirty || EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Open a clean, saved MainScene alone in Edit mode before measuring. The survey will not save or discard edits.");
        Physics.SyncTransforms();
        solids = All<Collider>(scene).Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy && !IsActor(c)).ToArray();
        solidSet = new HashSet<Collider>(solids);
        var report = new Report { unityVersion = Application.unityVersion, sceneSha256 = Hash(ScenePath),
            surveyedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") };

        // ---- Player body and eye from the saved player, never assumed values.
        var player = All<PlayerCharacterController>(scene).Single();
        var controller = player.GetComponent<CharacterController>();
        report.playerEyeHeight = player.CapsuleHeightStanding * player.CameraHeightRatio;
        report.playerHeight = player.CapsuleHeightStanding; report.crouchHeight = player.CapsuleHeightCrouching;
        report.playerRadius = controller.radius; report.playerStepOffset = controller.stepOffset; report.playerSlopeLimit = controller.slopeLimit;
        report.playerLayer = player.gameObject.layer;

        // ---- Shell pieces in the hall region, classified by their original kit root.
        var hall = new Bounds(); hall.SetMinMax(HallMin, HallMax);
        var hallColliders = solids.Where(c => hall.Contains(c.bounds.center) && RootKind(c.transform) != null).ToArray();
        var floorColliders = hallColliders.Where(c => RootKind(c.transform) == "floor").ToArray();
        if (floorColliders.Length < 50) throw new InvalidOperationException("No original Boss Hall floor tiles near the locator; inspect the shell.");
        float floorY = Mode(floorColliders.Select(c => c.bounds.max.y));
        report.floorTopY = floorY;
        var ceilingColliders = hallColliders.Where(c => RootKind(c.transform) == "ceiling").ToArray();
        report.ceilingUndersideY = Mode(ceilingColliders.Select(c => c.bounds.min.y));
        var wallColliders = hallColliders.Where(c => RootKind(c.transform) == "wall").ToArray();

        // ---- Wall planes: median of wall-collider hits at several heights; low rays expose collider gaps.
        Vector3 seed = new Vector3(Locator.x, floorY, Locator.z + 10); // Off the entry/bay-door axis at Z ~115.
        var provisional = new Bounds();
        provisional.SetMinMax(new Vector3(Surface(wallColliders, seed + Vector3.up * 1.5f, Vector3.left).x, floorY, Surface(wallColliders, seed + Vector3.up * 1.5f, Vector3.back).z),
            new Vector3(Surface(wallColliders, seed + Vector3.up * 1.5f, Vector3.right).x, report.ceilingUndersideY, Surface(wallColliders, seed + Vector3.up * 1.5f, Vector3.forward).z));
        float west = WallPlane(report, "West", wallColliders, provisional, Vector3.left, floorY);
        float east = WallPlane(report, "East", wallColliders, provisional, Vector3.right, floorY);
        float south = WallPlane(report, "South", wallColliders, provisional, Vector3.back, floorY);
        float north = WallPlane(report, "North", wallColliders, provisional, Vector3.forward, floorY);
        var room = new Bounds(); room.SetMinMax(new Vector3(west, floorY, south), new Vector3(east, report.ceilingUndersideY, north));
        if (room.size.x < 55 || room.size.x > 75 || room.size.z < 55 || room.size.z > 75 || room.size.y < 15)
            throw new InvalidOperationException("Unexpected Boss Hall shell measurement; inspect the wall anchors.");
        report.interior = new Box("Measured interior", room);
        report.centre = new Vector3(room.center.x, floorY, room.center.z);
        report.movementBandTopY = floorY + BodyBand;

        // ---- Shell inventory, duplicates and off-level tiles.
        var hallRenderers = All<Renderer>(scene).Where(rd => hall.Contains(rd.bounds.center)).ToArray();
        var shellRenderers = hallRenderers.Where(rd => RootKind(rd.transform) != null && RootKind(rd.transform) != "incline").ToArray();
        report.disabledShellRenderers = shellRenderers.Count(rd => !rd.enabled || !rd.gameObject.activeInHierarchy);
        var liveFloor = floorColliders.Where(c => InsideXZ(c.bounds.center, room, -.5f)).ToArray();
        report.floorTiles = liveFloor.Length;
        report.offLevelFloorTiles = liveFloor.Count(c => Mathf.Abs(c.bounds.max.y - floorY) > .02f);
        foreach (var c in liveFloor.Where(c => Mathf.Abs(c.bounds.max.y - floorY) > .02f))
        {
            bool hidden = liveFloor.Any(o => o != c && Mathf.Abs(o.bounds.max.y - floorY) <= .02f && o.bounds.min.x <= c.bounds.center.x && o.bounds.max.x >= c.bounds.center.x &&
                o.bounds.min.z <= c.bounds.center.z && o.bounds.max.z >= c.bounds.center.z);
            report.shellNotes.Add(F($"Off-level floor tile {PathOf(c.transform)} at ({c.bounds.center.x:F2}, {c.bounds.center.z:F2}): collider top Y {c.bounds.max.y:F3} ({c.bounds.max.y - floorY:+0.000;-0.000} m); ") +
                (hidden ? "covered by a level tile" : "exposed"));
        }
        int exact = liveFloor.GroupBy(c => Key(c.bounds)).Sum(g => g.Count() - 1);
        // Tile coverage: points no level tile covers expose whatever lies below (the physical-ground plane).
        var holes = new List<Vector3>();
        for (float x = west + .25f; x < east; x += .5f)
            for (float z = south + .25f; z < north; z += .5f)
                if (!liveFloor.Any(c => Mathf.Abs(c.bounds.max.y - floorY) <= .02f && x >= c.bounds.min.x && x <= c.bounds.max.x && z >= c.bounds.min.z && z <= c.bounds.max.z))
                    holes.Add(new Vector3(x, floorY, z));
        Func<float, float, bool> covered = (x, z) => liveFloor.Any(c => Mathf.Abs(c.bounds.max.y - floorY) <= .02f && x >= c.bounds.min.x && x <= c.bounds.max.x && z >= c.bounds.min.z && z <= c.bounds.max.z);
        var gaps = new List<string>();
        foreach (var h in Cluster(holes, .6f))
        {
            // Exact uncovered run through the cluster centre at 0.01 m, along each axis.
            Func<Vector3, Vector3, float> run = (o, d) => { float a0 = 0, a1 = 0; while (a0 > -7 && !covered(o.x + d.x * (a0 - .01f), o.z + d.z * (a0 - .01f))) a0 -= .01f; while (a1 < 7 && !covered(o.x + d.x * (a1 + .01f), o.z + d.z * (a1 + .01f))) a1 += .01f; return a1 - a0 + .01f; };
            var o = holes.OrderBy(q => Flat(q - h.at).magnitude).First();
            float wx = run(o, Vector3.right), wz = run(o, Vector3.forward);
            gaps.Add(F($"Level floor tiles leave a {Mathf.Min(wx, wz):F2} m seam, {Mathf.Max(wx, wz):F2} m long, around ({h.at.x:F2}, {h.at.z:F2})"));
        }
        report.shellNotes.AddRange(gaps.Select(g => g + "; the plane below shows through."));
        if (gaps.Count > 0) report.findings.Add($"{gaps.Count} uncovered floor seam(s) between level tiles: " + string.Join("; ", gaps) + ".");
        report.shellNotes.Add($"{liveFloor.Length} floor-tile colliders inside the walls: {exact} are exact duplicates of another tile.");
        report.floorOverlaps.AddRange(Overlaps(liveFloor));
        report.ceilingPieces = ceilingColliders.Select(c => HierarchyFolder.PieceRoot(c.transform)).Distinct().Count();
        report.wallPieces = wallColliders.Select(c => HierarchyFolder.PieceRoot(c.transform)).Distinct().Count();
        report.deckPieces = hallColliders.Where(c => RootKind(c.transform) == "deck").Select(c => HierarchyFolder.PieceRoot(c.transform)).Distinct().Count();
        report.railPieces = hallColliders.Where(c => RootKind(c.transform) == "rail").Select(c => HierarchyFolder.PieceRoot(c.transform)).Distinct().Count();
        foreach (var g in hallRenderers.Where(rd => rd.enabled && rd.gameObject.activeInHierarchy && rd.GetComponent<MeshFilter>() && rd.GetComponent<MeshFilter>().sharedMesh)
                     .GroupBy(rd => rd.GetComponent<MeshFilter>().sharedMesh.GetEntityId() + "|" + Key(rd.bounds)).Where(g => g.Count() > 1))
            report.duplicateRenderers.Add(F($"{g.Count()}x {g.First().GetComponent<MeshFilter>().sharedMesh.name} at {g.First().bounds.center:F2}: ") +
                string.Join(" | ", g.Select(rd => PathOf(rd.transform))));
        foreach (var g in solids.Where(c => hall.Contains(c.bounds.center)).GroupBy(c => c.GetType().Name + "|" + Key(c.bounds)).Where(g => g.Count() > 1))
            report.duplicateColliders.Add(F($"{g.Count()}x {g.First().GetType().Name} at {g.First().bounds.center:F2}: ") + string.Join(" | ", g.Select(c => PathOf(c.transform))));
        foreach (var c in solids.Where(c => hall.Contains(c.bounds.center) && (c.transform.lossyScale.x < 0 || c.transform.lossyScale.y < 0 || c.transform.lossyScale.z < 0)).OrderBy(c => PathOf(c.transform)))
            report.negativeScaleColliders.Add(F($"{PathOf(c.transform)} ({c.GetType().Name}, lossy scale {c.transform.lossyScale:F2}, bounds {c.bounds.min:F2} to {c.bounds.max:F2})"));
        // Shaders on hall and incline renderers (render_010 input); flags missing or error shaders.
        var inclineRoot = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == InclineName);
        var inclineBounds = new Bounds(inclineRoot ? inclineRoot.position : Vector3.zero, Vector3.zero);
        if (inclineRoot) foreach (var rd in inclineRoot.GetComponentsInChildren<Renderer>()) inclineBounds.Encapsulate(rd.bounds);
        inclineBounds.Expand(new Vector3(2, 2, 2));
        var materialRenderers = hallRenderers.Concat(All<Renderer>(scene).Where(rd => inclineRoot && inclineBounds.Intersects(rd.bounds) && !hall.Contains(rd.bounds.center))).Distinct();
        foreach (var g in materialRenderers.Where(rd => rd.enabled && rd.gameObject.activeInHierarchy).SelectMany(rd => rd.sharedMaterials.Select(m => (rd, m)))
                     .GroupBy(x => x.m ? x.m.name + " | " + (x.m.shader ? x.m.shader.name : "NO SHADER") : "MISSING MATERIAL").OrderByDescending(g => g.Count()))
        {
            var m = g.First().m;
            bool bad = !m || !m.shader || !m.shader.isSupported || m.shader.name.Contains("InternalErrorShader");
            report.materials.Add($"{(bad ? "ERROR " : "")}{g.Key}: {g.Count()} renderer slots" + (m ? " (" + AssetDatabase.GetAssetPath(m) + ")" : "") +
                (bad ? "; e.g. " + string.Join(", ", g.Take(4).Select(x => PathOf(x.rd.transform))) : ""));
        }
        if (report.materials.Any(m => m.StartsWith("ERROR "))) report.findings.Add($"{report.materials.Count(m => m.StartsWith("ERROR "))} hall/incline material(s) have a missing, unsupported or error shader (see materials).");
        var skylights = hallRenderers.Where(rd => rd.name.Contains("Skylight_glass") && rd.enabled).ToArray();
        report.skylightTopY = skylights.Length > 0 ? skylights.Max(rd => rd.bounds.max.y) : report.ceilingUndersideY;

        // ---- Physical ground over the lower floor.
        var ground = new Dictionary<string, int>();
        for (float x = west + 1; x < east - .5f; x += 2)
            for (float z = south + 1; z < north - .5f; z += 2)
            {
                var hit = FirstStatic(new Vector3(x, floorY + .3f, z), Vector3.down, 1f);
                string k = hit.HasValue ? PathOf(hit.Value.collider.transform).Split('/')[0] + F($" (Y {hit.Value.point.y:F2})") : "NO GROUND";
                ground[k] = ground.TryGetValue(k, out int n) ? n + 1 : 1;
            }
        report.groundColliders.AddRange(ground.OrderByDescending(kv => kv.Value).Take(12).Select(kv => kv.Key + ": " + kv.Value + " samples (2 m grid)"));
        var under = Physics.RaycastAll(new Vector3(report.centre.x + 12, floorY - .01f, report.centre.z + 12), Vector3.down, 2, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => solidSet.Contains(h.collider)).OrderBy(h => h.distance).ToArray();
        report.physicalGroundY = under.Length > 0 ? under[0].point.y : floorY;

        // ---- Decks (catwalk tiers) clustered by measured top.
        var deckColliders = hallColliders.Where(c => RootKind(c.transform) == "deck" && IsDeckShape(c)).ToArray();
        var tops = deckColliders.Select(c => c.bounds.max.y).ToArray();
        report.tier1TopY = Mode(tops.Where(y => y < floorY + 7));
        report.tier2TopY = Mode(tops.Where(y => y >= floorY + 7));
        report.tier1UndersideY = Mode(deckColliders.Where(c => c.bounds.max.y < floorY + 7).Select(c => c.bounds.min.y));
        report.tier2UndersideY = Mode(deckColliders.Where(c => c.bounds.max.y >= floorY + 7).Select(c => c.bounds.min.y));
        foreach (int tier in new[] { 1, 2 })
        {
            var set = deckColliders.Where(c => (c.bounds.max.y >= floorY + 7) == (tier == 2)).ToList();
            foreach (var cluster in Clusters(set))
            {
                var b = cluster[0].bounds; foreach (var c in cluster) b.Encapsulate(c.bounds);
                string label = tier == 1 ? (b.center.z > room.center.z ? "North gallery" : "South gallery") : "Upper deck network";
                if (report.decks.Any(d => d.name == label)) label += " " + (report.decks.Count(d => d.name.StartsWith(label)) + 1);
                var deck = new Deck { name = label, tier = tier, pieces = cluster.Select(c => HierarchyFolder.PieceRoot(c.transform)).Distinct().Count(),
                    bounds = new Box(label, b), topMin = cluster.Min(c => c.bounds.max.y), topMax = cluster.Max(c => c.bounds.max.y),
                    bottomY = cluster.Min(c => c.bounds.min.y), area = cluster.Sum(c => c.bounds.size.x * c.bounds.size.z) };
                deck.members.AddRange(cluster.Select(c => HierarchyFolder.PieceRoot(c.transform).name).Distinct().OrderBy(n => n));
                report.deckFootprints.AddRange(cluster.OrderBy(c => c.bounds.center.x).ThenBy(c => c.bounds.center.z).Select(c => new Box(label + ": " + HierarchyFolder.PieceRoot(c.transform).name, c.bounds, c)));
                report.decks.Add(deck);
                ScanDeck(report, deck, new HashSet<Collider>(cluster));
            }
        }
        report.decks = report.decks.OrderBy(d => d.tier).ThenBy(d => d.name).ToList();

        // ---- Stairs.
        foreach (var root in hallColliders.Where(c => RootKind(c.transform) == "stairs").Select(c => HierarchyFolder.PieceRoot(c.transform)).Distinct().OrderBy(t => t.name))
            report.stairs.Add(MeasureStair(report, root));

        // ---- Upper bay doors.
        foreach (var root in hallColliders.Where(c => HierarchyFolder.PieceRoot(c.transform).name.StartsWith("Wall BayDoor", StringComparison.Ordinal)).Select(c => HierarchyFolder.PieceRoot(c.transform)).Distinct().OrderBy(t => t.name))
            report.bayDoors.Add(MeasureBayDoor(report, root));

        // ---- East entry opening and the incline from the North-West Room door.
        var bossDoor = Single<SecurityDoor>(scene, BossDoorName);
        var incline = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => t.name == InclineName).ToArray();
        if (incline.Length != 1) throw new InvalidOperationException($"Expected one '{InclineName}', found {incline.Length}.");
        report.entry = MeasureEntry(report, bossDoor.transform.position.z);

        // ---- Actors and the turret.
        var enemyRoots = All<MonoBehaviour>(scene).Where(m => m && m.GetType().Name == "EnemyController" && hall.Contains(m.transform.position))
            .Select(m => m.transform).Distinct().OrderBy(t => t.name).ThenBy(t => t.position.z).ToArray();
        var turretRoot = enemyRoots.Where(t => t.GetComponents<MonoBehaviour>().Any(m => m && m.GetType().Name == "EnemyTurret")).ToArray();
        if (turretRoot.Length != 1) throw new InvalidOperationException("Expected exactly one EnemyTurret in the Boss Hall, found " + turretRoot.Length);
        report.turret = MeasureTurret(report, turretRoot[0]);
        MeasureIncline(report, bossDoor, incline[0]);
        foreach (var t in enemyRoots) report.actors.Add(MeasureActor(report, t, t == turretRoot[0]));
        if (report.actors.Count != 11)
            report.findings.Add($"Actor count is {report.actors.Count}, not the 11 recorded in PLAN.md; reconcile before dressing.");

        // ---- Navigation data, floor and roof coverage, reachability from the entry.
        var settings = NavMesh.GetSettingsByID(0);
        report.navigation.Add(F($"Baked agent 0: radius={settings.agentRadius}, height={settings.agentHeight}, climb={settings.agentClimb}, slope={settings.agentSlope}"));
        foreach (var component in All<MonoBehaviour>(scene).Where(m => m && m.GetType().Name == "NavMeshSurface"))
        {
            var so = new SerializedObject(component);
            var data = so.FindProperty("m_NavMeshData")?.objectReferenceValue;
            report.navigation.Add("Surface " + PathOf(component.transform) + ": " + (data ? AssetDatabase.GetAssetPath(data) : "NO DATA") +
                F($", layer mask {so.FindProperty("m_LayerMask")?.intValue}, collect geometry {so.FindProperty("m_UseGeometry")?.intValue}"));
        }
        var triangulation = NavMesh.CalculateTriangulation();
        report.navigation.Add($"Loaded scene NavMesh: {triangulation.vertices.Length} vertices, {triangulation.indices.Length / 3} triangles");
        report.navigation.Add($"{BossDoorName}: NavMeshObstacle " + (bossDoor.Obstacle ? (bossDoor.Obstacle.carving ? "carving" : "non-carving") + (bossDoor.Obstacle.enabled ? ", enabled" : ", disabled") : "none") +
            ", StartLocked " + bossDoor.StartLocked);
        foreach (var g in triangulation.vertices.Where(v => hall.Contains(v)).GroupBy(v => Mathf.Round(v.y * 2) / 2).OrderBy(g => g.Key))
            report.navigation.Add(F($"NavMesh vertices near Y {g.Key:F1}: {g.Count()} in X [{g.Min(v => v.x):F1}, {g.Max(v => v.x):F1}] Z [{g.Min(v => v.z):F1}, {g.Max(v => v.z):F1}]"));
        Vector3 entryNav = Nav(report.entryInside, 1f) ?? throw new InvalidOperationException("Entry inside point is off the NavMesh.");
        var floorMisses = new List<Vector3>();
        for (float x = west + .5f; x < east; x += 1)
            for (float z = south + .5f; z < north; z += 1)
            {
                report.floorNavSamples++;
                var p = Nav(new Vector3(x, floorY, z), NavTolerance);
                if (p == null) continue;
                report.floorNavHits++;
                if (Complete(entryNav, p.Value, out _)) report.floorNavReachable++; else floorMisses.Add(p.Value);
            }
        report.floorUnreachable.AddRange(Cluster(floorMisses).Select(u => F($"{u.count} samples around ({u.at.x:F1}, {u.at.z:F1})")));
        for (float x = west + 1; x < east; x += 2)
            for (float z = south + 1; z < north; z += 2)
            {
                report.roofNavSamples++;
                if (!NavMesh.SamplePosition(new Vector3(x, report.ceilingUndersideY + .3f, z), out var hit, .6f, NavMesh.AllAreas) || hit.position.y < report.ceilingUndersideY) continue;
                report.roofNavHits++;
                if (Complete(entryNav, hit.position, out _)) report.roofNavReachable++;
            }
        foreach (var deck in report.decks) NavDeck(report, deck, entryNav);

        // ---- Key points for the Play-mode traverse and agent routes.
        BuildKeyPoints(report);
        BuildRoutes(report, entryNav);

        // ---- Rails, firing tests and vertical clearances.
        MeasureRails(report, hallColliders);
        MeasureColumns(report);
        TurretVisibility(report);

        // ---- Visual focal axis: entry eye -> turret -> west wall.
        Vector3 axis = Flat(report.turret.pivot - report.entryEye);
        report.focalDistance = axis.magnitude;
        report.focalYaw = Quaternion.LookRotation(axis).eulerAngles.y;
        report.focalOffset = report.turret.pivot.z - report.entry.centre.z;
        float t2 = (west - report.entryEye.x) / axis.normalized.x;
        report.focalWestPoint = report.entryEye + axis.normalized * t2;
        report.focalWestPoint.y = floorY;

        // ---- Objectives (records the dependency; changes nothing).
        foreach (var objective in All<Objective>(scene))
        {
            string detail = objective is ObjectiveKillEnemies kill ? $", mustKillAll={kill.MustKillAllEnemies}, kills={kill.KillsToCompleteObjective}"
                : objective is ObjectiveKeycards keys ? ", terminal=" + (keys.Terminal ? keys.Terminal.name : "NONE") : "";
            report.objectives.Add($"{PathOf(objective.transform)}: {objective.GetType().Name}, optional={objective.IsOptional}{detail}");
        }

        BuildZones(report);
        BuildFindings(report);

        report.notes.Add("World metres; east is +X, north is +Z. The hall is entered from the east through Corridor Incline Full. Wall planes are the median of shell wall-collider hits at floor +1.5, +3.5, +6.5, +12.5 and +17 m (13 stations each); low rays at floor +0.5 m report collider gaps separately.");
        report.notes.Add("Deck scans use a 0.25 m grid of downward rays onto the cluster's own deck colliders. Local width is the shorter contiguous supported run through a sample (X or Z); clear width also requires the saved player capsule to fit. Edges: 'join' continues onto another surface within 0.35 m of deck height, 'railed' or 'wall' is blocked at hip (+0.9 m) or knee (+0.4 m) within 0.6 m, 'open' is a drop with no collision.");
        report.notes.Add("Stair profiles are 0.05 m centreline samples of the first upward-facing static hit; risers are steps over 0.02 m. Foot and top points are 0.8 m beyond the steps on the lower and upper walking surfaces.");
        report.notes.Add("Sightlines and firing tests ray-cast against all enabled, non-trigger colliders (any layer) in Edit mode. Actor colliders are ignored except the target's. Eye height is CapsuleHeightStanding x CameraHeightRatio above the walking surface.");
        report.notes.Add("NavMesh queries describe the loaded baked data in Edit mode (0.3 m floor tolerance, 0.5 m on decks). They are agent-route evidence only; player traversal is established separately by BossHallTraverse in Play mode.");
        report.notes.Add("Zones are design reservations derived from the measured reservations, not scene objects. Nothing in this report authorises moving or altering doors, enemies, the turret, stairs, catwalks, rails, shell pieces or navigation.");
        if (scene.isDirty || Hash(ScenePath) != report.sceneSha256)
            throw new InvalidOperationException("Scene changed during survey; do not accept this report.");
        return report;
    }

    // ---- Walls ------------------------------------------------------------------------------------------------
    static float WallPlane(Report report, string wall, Collider[] walls, Bounds provisional, Vector3 direction, float floorY)
    {
        bool xWall = direction.x != 0;
        Vector3 along = xWall ? Vector3.forward : Vector3.right;
        float span = xWall ? provisional.size.z : provisional.size.x;
        var hits = new List<(string label, float value)>();
        var low = new List<(string label, Vector3 origin)>();
        var sample = new WallSample { wall = wall };
        for (int k = -6; k <= 6; k++)
            foreach (float h in new[] { .5f, 1.5f, 3.5f, 6.5f, 12.5f, 17f })
            {
                Vector3 origin = new Vector3(provisional.center.x, floorY + h, provisional.center.z) + along * (k * (span / 2 - 1.5f) / 6);
                string label = F($"{(xWall ? "Z" : "X")} {(xWall ? origin.z : origin.x):F2}, +{h:F1} m");
                float distance = float.MaxValue; Vector3 point = default;
                foreach (var c in walls)
                    if (c.Raycast(new Ray(origin, direction), out var hit, 60) && hit.distance < distance) { distance = hit.distance; point = hit.point; }
                if (h < 1)
                {
                    if (distance == float.MaxValue) low.Add((label, origin));
                    continue;
                }
                if (distance == float.MaxValue) sample.deviations.Add(label + ": no wall collider within 60 m");
                else hits.Add((label, xWall ? point.x : point.z));
            }
        if (hits.Count == 0) throw new InvalidOperationException("No " + wall + " wall surface.");
        var sorted = hits.Select(h => h.value).OrderBy(v => v).ToArray();
        float median = sorted[sorted.Length / 2];
        var good = hits.Where(h => Mathf.Abs(h.value - median) <= .25f).ToArray();
        foreach (var h in hits.Where(h => Mathf.Abs(h.value - median) > .25f))
            sample.deviations.Add(h.label + F($": {h.value:F3} ({h.value - median:+0.000;-0.000} m)"));
        sample.plane = median; sample.hits = good.Length;
        sample.spread = good.Max(h => h.value) - good.Min(h => h.value);
        // Below the wall colliders: is anything else there, and would the saved crouched capsule fit under the wall?
        foreach (var (label, origin) in low)
        {
            float toPlane = Mathf.Abs((xWall ? median - origin.x : median - origin.z));
            var other = FirstStatic(origin, direction, toPlane + 3f);
            if (other.HasValue && other.Value.distance > toPlane - .3f) { sample.lowGaps.Add(label + ": no wall collider; closed by " + PathOf(other.Value.collider.transform)); continue; }
            Vector3 feet = origin + direction * toPlane; feet.y = floorY;
            float r = report.playerRadius;
            bool fits = !Physics.OverlapCapsule(feet + Vector3.up * (r + .02f), feet + Vector3.up * (report.crouchHeight - r), r, ~0, QueryTriggerInteraction.Ignore).Any(c => solidSet.Contains(c));
            sample.lowGaps.Add(label + (fits ? ": no collider below the wall; the crouched player capsule fits through the wall plane" : ": no wall collider at +0.5 m; crouched capsule blocked"));
        }
        report.walls.Add(sample);
        return median;
    }

    // ---- Decks ------------------------------------------------------------------------------------------------
    static void ScanDeck(Report report, Deck deck, HashSet<Collider> own)
    {
        var b = deck.bounds.ToBounds();
        int nx = Mathf.CeilToInt(b.size.x / Grid), nz = Mathf.CeilToInt(b.size.z / Grid);
        var support = new bool[nx, nz]; var y = new float[nx, nz]; var clear = new bool[nx, nz]; var covered = new bool[nx, nz];
        Func<int, int, Vector3> at = (i, j) => new Vector3(b.min.x + (i + .5f) * Grid, deck.topMax, b.min.z + (j + .5f) * Grid);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                var p = at(i, j);
                var hit = FirstStatic(p + Vector3.up * .3f, Vector3.down, 1f);
                if (!hit.HasValue || !own.Contains(hit.Value.collider) || hit.Value.point.y < deck.topMin - .08f || hit.Value.point.y > deck.topMax + .08f)
                {
                    // Another surface at or above deck height (stair, walkway, wall) covers the cell: not a hole.
                    covered[i, j] = hit.HasValue && hit.Value.point.y >= deck.topMin - .08f;
                    continue;
                }
                support[i, j] = true; y[i, j] = hit.Value.point.y; deck.samples++;
                clear[i, j] = BodyClear(new Vector3(p.x, y[i, j], p.z), report);
                if (clear[i, j]) deck.clearSamples++;
            }
        deck.longAxis = b.size.x >= b.size.z ? "X" : "Z";
        deck.centreLine = deck.longAxis == "X" ? CentreOf(support, nx, nz, b, false) : CentreOf(support, nx, nz, b, true);
        var widthMap = Widths(support, nx, nz); var clearMap = Widths(clear, nx, nz);
        var widths = Values(widthMap, support); var clearWidths = Values(clearMap, clear);
        if (widths.Count > 0)
        {
            widths.Sort(); deck.widthMin = widths[0]; deck.widthP5 = widths[widths.Count / 20]; deck.widthMedian = widths[widths.Count / 2];
        }
        if (clearWidths.Count > 0)
        {
            clearWidths.Sort(); deck.clearWidthMin = clearWidths[0]; deck.clearWidthP5 = clearWidths[clearWidths.Count / 20]; deck.clearWidthMedian = clearWidths[clearWidths.Count / 2];
        }
        var pinch = new List<Vector3>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                if (clear[i, j] && clearMap[i, j] < 1.5f) pinch.Add(at(i, j));
        foreach (var g in Cluster(pinch, .6f).Where(g => g.count >= 4))
            deck.pinches.Add(F($"clear width under 1.5 m: {g.count} cells around ({g.at.x:F2}, {g.at.z:F2}), extent X {g.size.x + Grid:F2} m, Z {g.size.z + Grid:F2} m, narrowest {MinIn(clearMap, clear, b, g.at, g.size):F2} m"));

        // Joins with gaps between supported cells, and steps between neighbours.
        var gap = new bool[nx, nz];
        Func<int, int, bool> s = (i, j) => i >= 0 && j >= 0 && i < nx && j < nz && support[i, j];
        var gapCells = new List<Vector3>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                if (support[i, j] || covered[i, j]) continue;
                bool spanX = Enumerable.Range(1, 3).Any(k => s(i - k, j)) && Enumerable.Range(1, 3).Any(k => s(i + k, j));
                bool spanZ = Enumerable.Range(1, 3).Any(k => s(i, j - k)) && Enumerable.Range(1, 3).Any(k => s(i, j + k));
                if (!spanX && !spanZ) continue;
                gap[i, j] = true; gapCells.Add(at(i, j));
            }
        foreach (var g in Cluster(gapCells, .6f))
            deck.gaps.Add(F($"{g.count} unsupported 0.25 m cells around ({g.at.x:F2}, {g.at.z:F2}), extent X {g.size.x:F2} m, Z {g.size.z:F2} m"));
        var stepCells = new List<(Vector3 p, float d)>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                if (!support[i, j]) continue;
                if (s(i + 1, j) && Mathf.Abs(y[i + 1, j] - y[i, j]) > .015f) stepCells.Add((at(i, j), Mathf.Abs(y[i + 1, j] - y[i, j])));
                if (s(i, j + 1) && Mathf.Abs(y[i, j + 1] - y[i, j]) > .015f) stepCells.Add((at(i, j), Mathf.Abs(y[i, j + 1] - y[i, j])));
            }
        deck.maxStep = stepCells.Count > 0 ? stepCells.Max(c => c.d) : 0;
        foreach (var g in Cluster(stepCells.Select(c => c.p).ToList(), .6f).Take(12))
            deck.steps.Add(F($"step up to {stepCells.Where(c => Vector3.Distance(Flat(c.p), Flat(g.at)) < g.size.magnitude + 1).Max(c => c.d):F3} m along a join around ({g.at.x:F2}, {g.at.z:F2})"));

        // Edges: join, wall, railed or open.
        var dirs = new[] { (1, 0, Vector3.right, "+X"), (-1, 0, Vector3.left, "-X"), (0, 1, Vector3.forward, "+Z"), (0, -1, Vector3.back, "-Z") };
        var open = new List<(string dir, float fixedCoord, float along)>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                if (!support[i, j]) continue;
                foreach (var (di, dj, d, label) in dirs)
                {
                    int ni = i + di, nj = j + dj;
                    if (s(ni, nj) || (ni >= 0 && nj >= 0 && ni < nx && nj < nz && gap[ni, nj])) continue;
                    var p = new Vector3(at(i, j).x, y[i, j], at(i, j).z);
                    var probe = p + d * .5f;
                    var cont = FirstStatic(probe + Vector3.up * .3f, Vector3.down, .7f);
                    if (cont.HasValue && Mathf.Abs(cont.Value.point.y - p.y) <= .35f) { deck.joinEdge++; continue; }
                    // Rays start 0.5 m back so a rail or wall that overlaps the edge cell is still detected.
                    var hip = FirstStatic(p - d * .5f + Vector3.up * .9f, d, 1.1f + Grid * .5f);
                    var knee = hip.HasValue ? hip : FirstStatic(p - d * .5f + Vector3.up * .4f, d, 1.1f + Grid * .5f);
                    if (knee.HasValue) { if (RootKind(knee.Value.collider.transform) == "wall") deck.wallEdge++; else deck.railedEdge++; continue; }
                    deck.openEdge++;
                    open.Add((label, label.Contains("X") ? p.x : p.z, label.Contains("X") ? p.z : p.x));
                }
            }
        foreach (var g in open.GroupBy(o => (o.dir, Mathf.Round(o.fixedCoord / Grid))))
        {
            var runs = g.Select(o => o.along).OrderBy(v => v).ToList();
            float startRun = runs[0], prev = runs[0];
            for (int k = 1; k <= runs.Count; k++)
            {
                if (k < runs.Count && runs[k] - prev <= Grid * 1.5f) { prev = runs[k]; continue; }
                string axis = g.Key.dir.Contains("X") ? "Z" : "X";
                deck.openEdges.Add(F($"open edge facing {g.Key.dir} at {(axis == "Z" ? "X" : "Z")} {g.Average(o => o.fixedCoord):F2}, {axis} [{startRun - Grid / 2:F2}, {prev + Grid / 2:F2}] ({prev - startRun + Grid:F2} m)"));
                if (k < runs.Count) { startRun = prev = runs[k]; }
            }
        }

        // Headroom above the deck: the lowest static underside above any supported cell.
        deck.minHeadroom = float.MaxValue;
        for (int i = 0; i < nx; i += 4)
            for (int j = 0; j < nz; j += 4)
                if (support[i, j] && clear[i, j])
                {
                    var p = new Vector3(at(i, j).x, y[i, j] + .05f, at(i, j).z);
                    var up = FirstStatic(p, Vector3.up, report.ceilingUndersideY - p.y + .5f);
                    float head = (up.HasValue ? up.Value.point.y : report.ceilingUndersideY) - y[i, j];
                    deck.minHeadroom = Mathf.Min(deck.minHeadroom, head);
                }
        if (deck.minHeadroom == float.MaxValue) deck.minHeadroom = 0;
        deckCache[deck.name] = (support, y, nx, nz, b);
    }

    static readonly Dictionary<string, (bool[,] support, float[,] y, int nx, int nz, Bounds b)> deckCache = new Dictionary<string, (bool[,], float[,], int, int, Bounds)>();

    static IEnumerable<Vector3> DeckCells(Deck deck, int stride)
    {
        var (support, y, nx, nz, b) = deckCache[deck.name];
        for (int i = 0; i < nx; i += stride)
            for (int j = 0; j < nz; j += stride)
                if (support[i, j]) yield return new Vector3(b.min.x + (i + .5f) * Grid, y[i, j], b.min.z + (j + .5f) * Grid);
    }

    static float CentreOf(bool[,] support, int nx, int nz, Bounds b, bool alongZ)
    {
        // Median cross-axis coordinate of supported cells: the deck's walking centreline.
        var values = new List<float>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                if (support[i, j]) values.Add(alongZ ? b.min.x + (i + .5f) * Grid : b.min.z + (j + .5f) * Grid);
        if (values.Count == 0) return alongZ ? b.center.x : b.center.z;
        values.Sort(); return values[values.Count / 2];
    }

    static float[,] Widths(bool[,] ok, int nx, int nz)
    {
        var runX = new int[nx, nz]; var runZ = new int[nx, nz];
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx;)
            {
                if (!ok[i, j]) { i++; continue; }
                int k = i; while (k < nx && ok[k, j]) k++;
                for (int m = i; m < k; m++) runX[m, j] = k - i;
                i = k;
            }
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz;)
            {
                if (!ok[i, j]) { j++; continue; }
                int k = j; while (k < nz && ok[i, k]) k++;
                for (int m = j; m < k; m++) runZ[i, m] = k - j;
                j = k;
            }
        var widths = new float[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
                if (ok[i, j]) widths[i, j] = Mathf.Min(runX[i, j], runZ[i, j]) * Grid;
        return widths;
    }

    static List<float> Values(float[,] map, bool[,] ok)
    {
        var list = new List<float>();
        for (int i = 0; i < map.GetLength(0); i++)
            for (int j = 0; j < map.GetLength(1); j++)
                if (ok[i, j]) list.Add(map[i, j]);
        return list;
    }

    static float MinIn(float[,] map, bool[,] ok, Bounds b, Vector3 at, Vector3 size)
    {
        float min = float.MaxValue;
        for (int i = 0; i < map.GetLength(0); i++)
            for (int j = 0; j < map.GetLength(1); j++)
            {
                float x = b.min.x + (i + .5f) * Grid, z = b.min.z + (j + .5f) * Grid;
                if (ok[i, j] && Mathf.Abs(x - at.x) <= size.x * .5f + .01f && Mathf.Abs(z - at.z) <= size.z * .5f + .01f) min = Mathf.Min(min, map[i, j]);
            }
        return min;
    }

    static void NavDeck(Report report, Deck deck, Vector3 entryNav)
    {
        var misses = new List<Vector3>();
        foreach (var p in DeckCells(deck, 4))
        {
            deck.navSamples++;
            if (!NavMesh.SamplePosition(p, out var hit, .5f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - p.y) > .6f) continue;
            deck.navHits++;
            if (Complete(entryNav, hit.position, out _)) deck.navReachable++; else misses.Add(hit.position);
        }
        deck.unreachable.AddRange(Cluster(misses).Select(u => F($"{u.count} samples around ({u.at.x:F1}, {u.at.z:F1})")));
    }

    // ---- Stairs -----------------------------------------------------------------------------------------------
    static Stair MeasureStair(Report report, Transform root)
    {
        var colliders = root.GetComponentsInChildren<Collider>().Where(c => solidSet.Contains(c)).ToArray();
        var steps = colliders.Where(c => c.name.IndexOf("Step", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        if (steps.Length != 1) throw new InvalidOperationException($"Stair {root.name} must have exactly one step collider; found {steps.Length}.");
        var all = colliders[0].bounds; foreach (var c in colliders) all.Encapsulate(c.bounds);
        var sb = steps[0].bounds;
        var st = new Stair { name = root.name, id = GlobalObjectId.GetGlobalObjectIdSlow(root.gameObject).ToString(), bounds = new Box(root.name, all, root.gameObject),
            steps = new Box(steps[0].name, sb, steps[0]) };
        st.colliderLayers = string.Join(", ", colliders.Select(c => LayerMask.LayerToName(c.gameObject.layer)).Distinct());
        st.layersCollideWithPlayer = colliders.All(c => !Physics.GetIgnoreLayerCollision(report.playerLayer, c.gameObject.layer));
        bool alongX = sb.size.x >= sb.size.z;
        Vector3 axis = alongX ? Vector3.right : Vector3.forward;
        float lo = alongX ? sb.min.x : sb.min.z, hi = alongX ? sb.max.x : sb.max.z;
        Vector3 centre = sb.center;
        var profile = new List<(float s, float y)>();
        for (float s = lo - 1.2f; s <= hi + 1.2f; s += .05f)
        {
            Vector3 p = alongX ? new Vector3(s, all.max.y + .5f, centre.z) : new Vector3(centre.x, all.max.y + .5f, s);
            var hit = Physics.RaycastAll(p, Vector3.down, all.size.y + 3, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => solidSet.Contains(h.collider) && h.normal.y > .5f).OrderBy(h => h.distance).FirstOrDefault();
            if (hit.collider) profile.Add((s, hit.point.y));
        }
        if (profile.Count < 10) throw new InvalidOperationException("No step profile for " + root.name);
        float yLo = profile.Where(p => p.s >= lo && p.s <= lo + .3f).Select(p => p.y).DefaultIfEmpty(float.NaN).Average();
        float yHi = profile.Where(p => p.s <= hi && p.s >= hi - .3f).Select(p => p.y).DefaultIfEmpty(float.NaN).Average();
        bool risesWithS = yHi > yLo;
        if (!risesWithS) axis = -axis;
        st.axis = axis;
        float footS = risesWithS ? lo - .8f : hi + .8f, topS = risesWithS ? hi + .8f : lo - .8f;
        Func<float, Vector3> point = sv =>
        {
            var p = alongX ? new Vector3(sv, 0, centre.z) : new Vector3(centre.x, 0, sv);
            var nearest = profile.OrderBy(q => Mathf.Abs(q.s - sv)).First();
            p.y = nearest.y; return p;
        };
        var footP = point(footS); var topP = point(topS);
        st.foot = new Point(root.name + " foot", footP, .5f); st.top = new Point(root.name + " top", topP, .5f);
        st.footY = footP.y; st.topY = topP.y; st.rise = st.topY - st.footY; st.run = hi - lo;
        st.slopeDeg = Mathf.Atan2(st.rise, st.run) * Mathf.Rad2Deg;
        var inSteps = profile.Where(p => p.s >= lo - .05f && p.s <= hi + .05f).OrderBy(p => p.s).ToList();
        var risers = new List<float>();
        for (int i = 1; i < inSteps.Count; i++) { float d = Mathf.Abs(inSteps[i].y - inSteps[i - 1].y); if (d > .02f) risers.Add(d); }
        st.risers = risers.Count; st.maxRiser = risers.Count > 0 ? risers.Max() : 0;
        // The repeating step height; single larger values are narrow dips where the steps meet a landing.
        st.typicalRiser = risers.Count > 0 ? risers.OrderBy(r => r).ElementAt(risers.Count / 2) : 0;
        st.riserHeights = string.Join(" ", risers.Select(r => r.ToString("F3", CultureInfo.InvariantCulture)));
        st.profile = string.Join(" ", profile.Where(q => q.s >= lo - .4f && q.s <= hi + .4f).Select(q => F($"{q.s:F2}:{q.y:F3}")));
        // Tread width on the step collider and clear width between hip-height obstructions, at mid-run.
        Vector3 across = alongX ? Vector3.forward : Vector3.right;
        var mid = point((lo + hi) * .5f);
        float wLo = 0, wHi = 0;
        Func<float, bool> tread = o => steps[0].Raycast(new Ray(mid + across * o + Vector3.up * 1.5f, Vector3.down), out var h, 3f) && Mathf.Abs(h.point.y - mid.y) < .3f;
        while (wLo > -4 && tread(wLo - .05f)) wLo -= .05f;
        while (wHi < 4 && tread(wHi + .05f)) wHi += .05f;
        st.width = wHi - wLo;
        Func<float, bool> blocked = o => Physics.CheckCapsule(mid + across * o + Vector3.up * .2f, mid + across * o + Vector3.up * 1.2f, .03f, ~0, QueryTriggerInteraction.Ignore);
        float cLo = 0, cHi = 0;
        while (cLo > -4 && !blocked(cLo - .05f)) cLo -= .05f;
        while (cHi < 4 && !blocked(cHi + .05f)) cHi += .05f;
        st.clearWidth = cHi - cLo;
        Func<Vector3, string> surface = p =>
        {
            if (Mathf.Abs(p.y - report.floorTopY) < .3f) return "lower floor";
            var deck = report.decks.FirstOrDefault(d => p.y > d.topMin - .3f && p.y < d.topMax + .3f && InsideXZ(p, d.bounds.ToBounds(), 1f));
            return deck != null ? deck.name : F($"unclassified surface at Y {p.y:F2}");
        };
        st.footSurface = surface(footP); st.topSurface = surface(topP);
        st.fromTier = st.footSurface == "lower floor" ? 0 : st.footSurface.Contains("gallery") ? 1 : 2;
        st.toTier = st.topSurface == "lower floor" ? 0 : st.topSurface.Contains("gallery") ? 1 : 2;
        string quadrant = (topP.z > report.centre.z ? "N" : "S") + (topP.x > report.centre.x ? "E" : "W");
        st.label = (st.fromTier == 0 ? "Lower " : "Upper ") + quadrant;
        if (st.foot.onNavMesh && st.top.onNavMesh)
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(st.foot.navPosition, st.top.navPosition, NavMesh.AllAreas, path);
            st.path = path.status.ToString(); st.pathLength = Length(path.corners);
        }
        else st.path = "Endpoint off NavMesh";
        return st;
    }

    // ---- Bay doors --------------------------------------------------------------------------------------------
    static BayDoor MeasureBayDoor(Report report, Transform root)
    {
        var b = new Bounds(root.position, Vector3.zero); bool first = true;
        foreach (var rd in root.GetComponentsInChildren<Renderer>()) { if (first) { b = rd.bounds; first = false; } else b.Encapsulate(rd.bounds); }
        var bd = new BayDoor { name = root.name, id = GlobalObjectId.GetGlobalObjectIdSlow(root.gameObject).ToString(), bounds = new Box(root.name, b, root.gameObject) };
        bd.faceX = b.center.x > report.centre.x ? b.min.x : b.max.x;
        float inward = b.center.x > report.centre.x ? -1 : 1;
        bd.deckTop = report.tier2TopY;
        var origin = new Vector3(bd.faceX + inward * 2f, report.tier2TopY + 1.2f, b.center.z);
        var hit = FirstStatic(origin, Vector3.right * -inward, 3f);
        bd.closed = hit.HasValue; bd.closedBy = hit.HasValue ? PathOf(hit.Value.collider.transform) + F($" at X {hit.Value.point.x:F2}") : "nothing (open)";
        var a = new Bounds(); a.SetMinMax(new Vector3(Mathf.Min(bd.faceX, bd.faceX + inward * BayApproach), report.tier2TopY, b.min.z),
            new Vector3(Mathf.Max(bd.faceX, bd.faceX + inward * BayApproach), report.tier2TopY + Headroom, b.max.z));
        bd.approach = new Box(root.name + " approach", a);
        return bd;
    }

    // ---- Entry opening ----------------------------------------------------------------------------------------
    static Opening MeasureEntry(Report report, float doorZ)
    {
        var room = report.interior.ToBounds();
        float floor = report.floorTopY, x = room.max.x;
        var e = new Opening { inward = Vector3.left };
        var candidates = solids.Where(c => Vector3.Distance(c.bounds.ClosestPoint(new Vector3(x, floor + 2, doorZ)), new Vector3(x, floor + 2, doorZ)) < 8).ToArray();
        e.inspectedColliders.AddRange(candidates.Select(c => PathOf(c.transform)).Distinct().OrderBy(n => n));
        Func<float, float, bool> blocked = (z, yy) => candidates.Any(c => c.Raycast(new Ray(new Vector3(x - 1.5f, yy, z), Vector3.right), out _, 3f));
        float lo = -6, hi = 6;
        foreach (float h in new[] { .2f, 1.2f, 1.8f })
        {
            if (blocked(doorZ, floor + h)) throw new InvalidOperationException("Boss Hall entry centre blocked at height " + h);
            float l = 0, u = 0;
            while (l > -6 && !blocked(doorZ + l - Step, floor + h)) l -= Step;
            while (u < 6 && !blocked(doorZ + u + Step, floor + h)) u += Step;
            lo = Mathf.Max(lo, l); hi = Mathf.Min(hi, u);
        }
        e.clearMin = doorZ + lo; e.clearMax = doorZ + hi; e.width = hi - lo;
        float lintel = floor + 1.8f;
        while (lintel < report.ceilingUndersideY && !blocked(doorZ, lintel + Step)) lintel += Step;
        e.lintelY = lintel;
        e.centre = new Vector3(x, floor, (e.clearMin + e.clearMax) * .5f);
        report.entryInside = new Vector3(x - 3, floor, e.centre.z);
        report.entryEye = new Vector3(x - .5f, floor + report.playerEyeHeight, e.centre.z);
        return e;
    }

    static void MeasureIncline(Report report, SecurityDoor door, Transform incline)
    {
        Vector3 inward = Vector3.left;
        float groundY = report.floorTopY - 6;
        Vector3 head = (report.turret.head.min + report.turret.head.max) * .5f;
        for (float d = 0; d <= 22; d += .5f)
        {
            Vector3 p = door.transform.position + inward * d;
            var s = new ProfileSample { distance = d, position = new Vector3(p.x, groundY, p.z), groundY = -999, ground = "NO GROUND" };
            var hit = Physics.RaycastAll(new Vector3(p.x, groundY + 2, p.z), Vector3.down, 5, ~0, QueryTriggerInteraction.Ignore)
                .Where(h => solidSet.Contains(h.collider) && !h.collider.transform.IsChildOf(door.transform) && h.normal.y > .3f).OrderBy(h => h.distance).FirstOrDefault();
            if (hit.collider)
            {
                groundY = s.groundY = hit.point.y; s.position = hit.point; s.ground = PathOf(hit.collider.transform);
            }
            s.onNavMesh = NavMesh.SamplePosition(s.position, out _, NavTolerance, NavMesh.AllAreas);
            s.turretVisible = s.groundY > -900 && Visible(s.position + Vector3.up * report.playerEyeHeight, head, report.turret.footprint.id);
            if (s.turretVisible && report.turretRevealDistance < 0) report.turretRevealDistance = d;
            report.incline.Add(s);
        }
        var threshold = report.incline.FirstOrDefault(s => s.distance >= .5f && s.onNavMesh && Mathf.Abs(s.groundY - .18f) < .1f);
        var landing = report.incline.LastOrDefault(s => s.position.x > report.interior.max.x && Mathf.Abs(s.groundY - report.floorTopY) < .1f);
        if (threshold == null || landing == null) throw new InvalidOperationException("Incline profile did not find the North-West threshold and the hall landing.");
        report.northWestThreshold = threshold.position;
        report.inclineLanding = landing.position;
        report.landingEye = landing.position + Vector3.up * report.playerEyeHeight;
    }

    // ---- Actors -----------------------------------------------------------------------------------------------
    static Turret MeasureTurret(Report report, Transform root)
    {
        var t = new Turret();
        var colliders = root.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
        var fb = colliders[0].bounds; foreach (var c in colliders) fb.Encapsulate(c.bounds);
        t.footprint = new Box("Turret collision footprint", fb, root.gameObject);
        var head = colliders.Where(c => c.name.Contains("Top")).DefaultIfEmpty(colliders.OrderByDescending(c => c.bounds.max.y).First()).First();
        t.head = new Box("Turret head hitbox", head.bounds, head);
        var turret = root.GetComponents<MonoBehaviour>().First(m => m && m.GetType().Name == "EnemyTurret");
        var so = new SerializedObject(turret);
        var pivot = so.FindProperty("TurretPivot").objectReferenceValue as Transform;
        var aim = so.FindProperty("TurretAimPoint").objectReferenceValue as Transform;
        t.pivot = pivot ? pivot.position : root.position;
        var detection = Detection(root);
        t.detectionRange = detection.range; t.attackRange = detection.attack;
        // EnemyTurret aims the weapon muzzle at the target, so the muzzle defines the firing origin and rest axis.
        var weapon = root.GetComponentInChildren<WeaponController>(true);
        var muzzle = weapon ? weapon.WeaponMuzzle : null;
        t.firingOrigin = muzzle ? muzzle.position : detection.source ? detection.source.position : aim ? aim.position : head.bounds.center;
        t.restForward = Flat(muzzle ? muzzle.forward : root.forward).normalized;
        if (t.restForward.sqrMagnitude < .01f) t.restForward = Flat(root.forward).normalized;
        t.restYaw = Quaternion.LookRotation(t.restForward).eulerAngles.y;
        // The head yaws about the pole, so its full sweep is a cylinder about the pivot.
        float radius = 0;
        foreach (var corner in Corners(head.bounds)) radius = Mathf.Max(radius, Flat(corner - t.pivot).magnitude);
        t.aimRadius = radius + .5f;
        var clearance = new Bounds(); clearance.SetMinMax(new Vector3(t.pivot.x - t.aimRadius, fb.min.y, t.pivot.z - t.aimRadius),
            new Vector3(t.pivot.x + t.aimRadius, head.bounds.max.y + .5f, t.pivot.z + t.aimRadius));
        t.aimClearance = new Box("Turret head sweep plus 0.5 m", clearance);
        // Entry sight corridor from the hall threshold eye to the turret head.
        var a = report.entryEye; var bpt = head.bounds.center;
        var corridor = new Bounds(); corridor.SetMinMax(new Vector3(Mathf.Min(a.x, bpt.x), report.floorTopY, Mathf.Min(a.z, bpt.z) - SightHalfWidth),
            new Vector3(Mathf.Max(a.x, bpt.x), Mathf.Max(a.y, head.bounds.max.y) + 1, Mathf.Max(a.z, bpt.z) + SightHalfWidth));
        t.sightCorridor = new Box("Entry eye to turret head sight corridor", corridor);
        return t;
    }

    static (float range, float attack, Transform source) Detection(Transform root)
    {
        var m = root.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(c => c && c.GetType().Name == "DetectionModule");
        if (!m) return (0, 0, null);
        var so = new SerializedObject(m);
        return (so.FindProperty("DetectionRange").floatValue, so.FindProperty("AttackRange").floatValue,
            so.FindProperty("DetectionSourcePoint").objectReferenceValue as Transform);
    }

    static ActorInfo MeasureActor(Report report, Transform root, bool isTurret)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(root.gameObject);
        var a = new ActorInfo { name = root.name, id = GlobalObjectId.GetGlobalObjectIdSlow(root.gameObject).ToString(),
            prefab = src ? AssetDatabase.GetAssetPath(src) : "", kind = isTurret ? "turret" : "humanoid",
            position = root.position, eulerY = root.eulerAngles.y, scale = root.lossyScale };
        var agent = root.GetComponent<NavMeshAgent>();
        if (agent)
        {
            a.agentRadius = agent.radius * root.lossyScale.x; a.agentHeight = agent.height * root.lossyScale.y;
            a.speed = agent.speed; a.stoppingDistance = agent.stoppingDistance;
        }
        var health = root.GetComponent<Health>();
        a.maxHealth = health ? health.MaxHealth : 0;
        var detection = Detection(root);
        a.detectionRange = detection.range; a.attackRange = detection.attack;
        a.onNavMesh = NavMesh.SamplePosition(root.position, out var hit, 4f, NavMesh.AllAreas);
        a.navPosition = a.onNavMesh ? hit.position : root.position;
        a.navOffset = a.onNavMesh ? Vector3.Distance(hit.position, root.position) : -1;
        var body = root.GetComponent<Collider>();
        a.aimPoint = isTurret ? (report.turret.head.min + report.turret.head.max) * .5f : body ? body.bounds.center : root.position + Vector3.up;
        if (isTurret)
        {
            a.envelopeRadius = report.turret.aimRadius;
            a.envelope = report.turret.aimClearance;
        }
        else
        {
            a.envelopeRadius = Mathf.Max(EnvelopeRadius, a.agentRadius + 1.5f);
            var e = new Bounds(); e.SetMinMax(new Vector3(root.position.x - a.envelopeRadius, report.floorTopY, root.position.z - a.envelopeRadius),
                new Vector3(root.position.x + a.envelopeRadius, report.floorTopY + Mathf.Max(BodyBand, a.agentHeight + .3f), root.position.z + a.envelopeRadius));
            a.envelope = new Box(root.name + " start envelope", e);
        }
        Vector3? entry = Nav(report.entryInside, 1f), nw = Nav(NorthWestInside, 1f);
        if (a.onNavMesh && entry.HasValue)
        {
            var path = new NavMeshPath(); NavMesh.CalculatePath(a.navPosition, entry.Value, NavMesh.AllAreas, path);
            a.pathToEntry = path.status.ToString(); a.pathToEntryLength = Length(path.corners);
        }
        else a.pathToEntry = "Endpoint off NavMesh";
        if (a.onNavMesh && nw.HasValue)
        {
            var path = new NavMeshPath(); NavMesh.CalculatePath(a.navPosition, nw.Value, NavMesh.AllAreas, path);
            a.pathToNorthWestRoom = path.status + F($" ({Length(path.corners):F1} m)");
        }
        else a.pathToNorthWestRoom = "Endpoint off NavMesh";
        a.entryDistance = Vector3.Distance(report.entryEye, a.aimPoint);
        a.visibleFromEntry = Visible(report.entryEye, a.aimPoint, a.id, out a.entryBlocker);
        a.visibleFromLanding = Visible(report.landingEye, a.aimPoint, a.id, out _);
        return a;
    }

    static void TurretVisibility(Report report)
    {
        var t = report.turret;
        var room = report.interior.ToBounds();
        Action<string, IEnumerable<Vector3>> tally = (label, feet) =>
        {
            int n = 0, inRange = 0, visible = 0, visibleAttack = 0;
            foreach (var f in feet)
            {
                n++;
                var eye = f + Vector3.up * report.playerEyeHeight;
                float d = Vector3.Distance(t.firingOrigin, eye);
                if (d > t.detectionRange) continue;
                inRange++;
                if (!Visible(t.firingOrigin, eye, t.footprint.id)) continue;
                visible++;
                if (d <= t.attackRange) visibleAttack++;
            }
            t.visibility.Add(F($"{label}: {n} standing samples, {inRange} within detection {t.detectionRange:F0} m, {visible} with a clear line from the firing origin ({(inRange > 0 ? 100f * visible / inRange : 0):F0}%), {visibleAttack} of those within attack {t.attackRange:F0} m"));
        };
        var floor = new List<Vector3>();
        for (float x = room.min.x + 1; x < room.max.x; x += 2)
            for (float z = room.min.z + 1; z < room.max.z; z += 2)
            {
                var g = FirstStatic(new Vector3(x, report.floorTopY + .3f, z), Vector3.down, .8f);
                if (g.HasValue && Mathf.Abs(g.Value.point.y - report.floorTopY) < .1f && BodyClear(g.Value.point, report)) floor.Add(g.Value.point);
            }
        tally("Lower floor (2 m grid)", floor);
        foreach (var deck in report.decks) tally(deck.name + " (1 m grid)", DeckCells(deck, 4));
        tally("Incline profile", report.incline.Where(s => s.groundY > -900).Select(s => s.position));
    }

    // ---- Rails ------------------------------------------------------------------------------------------------
    static void MeasureRails(Report report, Collider[] hallColliders)
    {
        var rails = hallColliders.Where(c => RootKind(c.transform) == "rail" || (RootKind(c.transform) == "deck" && !IsDeckShape(c))).ToArray();
        foreach (var c in rails.OrderBy(c => PathOf(c.transform)))
        {
            int tier = c.bounds.min.y >= report.tier2TopY - .2f ? 2 : c.bounds.max.y > report.tier1TopY ? 1 : 0;
            if (tier == 0) continue;
            float deckTop = tier == 2 ? report.tier2TopY : report.tier1TopY;
            report.rails.Add(new RailCollider { name = PathOf(c.transform), tier = tier, bounds = new Box(PathOf(c.transform), c.bounds, c),
                topAboveDeck = c.bounds.max.y - deckTop, crossesEye = c.bounds.max.y >= deckTop + report.playerEyeHeight && c.bounds.min.y <= deckTop + report.playerEyeHeight });
        }
        // Firing stations every ~6 m along railed deck edges, 0.4 m back from the edge, at the saved eye height.
        var room = report.interior.ToBounds();
        var floorTargets = new List<Vector3>();
        for (float x = room.min.x + 2; x < room.max.x; x += 4)
            for (float z = room.min.z + 2; z < room.max.z; z += 4)
                floorTargets.Add(new Vector3(x, report.floorTopY + 1f, z));
        Vector3 turretHead = (report.turret.head.min + report.turret.head.max) * .5f;
        foreach (var deck in report.decks)
        {
            var stations = new List<(Vector3 p, Vector3 d)>();
            foreach (var p in DeckCells(deck, 1))
                foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    var next = p + d * Grid;
                    var support = FirstStatic(next + Vector3.up * .3f, Vector3.down, .7f);
                    if (support.HasValue && Mathf.Abs(support.Value.point.y - p.y) <= .35f) continue;
                    var hip = FirstStatic(p - d * .5f + Vector3.up * .9f, d, 1.1f + Grid * .5f);
                    if (!hip.HasValue || RootKind(hip.Value.collider.transform) == "wall") continue;
                    // Only edges that face the open arena volume (a drop of at least 2 m beyond).
                    var below = FirstStatic(p + d * 1.2f + Vector3.up * .3f, Vector3.down, 30f);
                    if (below.HasValue && p.y - below.Value.point.y < 2f) continue;
                    if (stations.Any(s => Vector3.Distance(s.p, p) < 6f)) continue;
                    stations.Add((p, d));
                }
            foreach (var (p, d) in stations)
            {
                var eye = p - d * .4f + Vector3.up * report.playerEyeHeight;
                var test = new RailTest { station = F($"{deck.name} edge facing {Dir(d)} at ({p.x:F1}, {p.z:F1})"), tier = deck.tier, eye = eye, outward = d };
                var level = FirstStatic(eye, d, 1.5f);
                test.levelBlocked = level.HasValue;
                test.levelBlocker = level.HasValue ? PathOf(level.Value.collider.transform) : "";
                var blockers = new HashSet<string>();
                foreach (var target in floorTargets.Where(q => Vector3.Distance(q, eye) <= 30f && Vector3.Dot(Flat(q - eye), d) > 0).Concat(new[] { turretHead }))
                {
                    test.shots++;
                    var dir = target - eye; float dist = dir.magnitude;
                    var hit = FirstStatic(eye, dir / dist, dist - .1f);
                    if (!hit.HasValue) continue;
                    bool rail = hit.Value.distance < 1.5f && (RootKind(hit.Value.collider.transform) == "rail" ||
                        (RootKind(hit.Value.collider.transform) == "deck" && !IsDeckShape(hit.Value.collider)));
                    if (rail) { test.railBlocked++; blockers.Add(PathOf(hit.Value.collider.transform)); } else test.otherBlocked++;
                }
                test.blockers.AddRange(blockers.OrderBy(n => n));
                report.railTests.Add(test);
            }
        }
    }

    // ---- Vertical clearances ----------------------------------------------------------------------------------
    static void MeasureColumns(Report report)
    {
        var room = report.interior.ToBounds();
        var classes = new Dictionary<string, List<float>>();
        float highest = report.floorTopY;
        for (float x = room.min.x + .5f; x < room.max.x; x += 1)
            for (float z = room.min.z + .5f; z < room.max.z; z += 1)
            {
                var start = new Vector3(x, report.floorTopY + .05f, z);
                var up = FirstStatic(start, Vector3.up, report.ceilingUndersideY - start.y - .05f);
                string name;
                float head;
                if (!up.HasValue) { name = "Open floor to ceiling"; head = report.ceilingUndersideY - report.floorTopY; }
                else
                {
                    string kind = RootKind(up.Value.collider.transform) ?? "other";
                    float uy = up.Value.point.y;
                    name = kind == "deck" ? (uy < report.tier1TopY + .5f ? "Floor under first-tier deck" : "Floor under second-tier deck")
                        : kind == "stairs" ? "Floor under a stair" : kind == "rail" ? "Floor under a rail" : "Floor under " + kind;
                    head = uy - report.floorTopY;
                }
                if (!classes.ContainsKey(name)) classes[name] = new List<float>();
                classes[name].Add(head);
                var down = FirstStatic(new Vector3(x, report.ceilingUndersideY - .05f, z), Vector3.down, report.ceilingUndersideY - report.floorTopY);
                if (down.HasValue && RootKind(down.Value.collider.transform) != "floor") highest = Mathf.Max(highest, down.Value.point.y);
            }
        report.highestStructureTopY = highest;
        foreach (var kv in classes.OrderByDescending(kv => kv.Value.Count))
            report.columns.Add(new ColumnClass { name = kv.Key, columns = kv.Value.Count, minHeadroom = kv.Value.Min(), maxHeadroom = kv.Value.Max() });
    }

    // ---- Key points and routes --------------------------------------------------------------------------------
    static void BuildKeyPoints(Report report)
    {
        Action<string, Vector3, float> add = (n, p, tol) => report.keyPoints.Add(new Point(n, p, tol));
        add("North-West threshold", report.northWestThreshold, NavTolerance);
        add("Incline landing", report.inclineLanding, NavTolerance);
        add("Entry inside", report.entryInside, NavTolerance);
        foreach (var st in report.stairs.OrderBy(s => s.label))
        {
            add(st.label + " stair foot", st.foot.position, .5f);
            add(st.label + " stair top", st.top.position, .5f);
        }
        // Lower floor loop: one point per quadrant, clear of every actor envelope, stair and the turret sweep.
        var room = report.interior.ToBounds();
        foreach (var (label, sx, sz) in new[] { ("SE", 1, -1), ("SW", -1, -1), ("NW", -1, 1), ("NE", 1, 1) })
        {
            Vector3? chosen = null;
            for (float r = 0; r <= 8 && chosen == null; r += 1)
                for (int k = 0; k < 16 && chosen == null; k++)
                {
                    float ang = k * Mathf.PI / 8;
                    var p = new Vector3(report.centre.x + sx * 16 + Mathf.Cos(ang) * r, report.floorTopY, report.centre.z + sz * 13 + Mathf.Sin(ang) * r);
                    if (report.actors.Any(a => Flat(a.position - p).magnitude < a.envelopeRadius + 1) ||
                        report.stairs.Any(s => InsideXZ(p, s.bounds.ToBounds(), 1.5f)) || !BodyClear(p, report) || Nav(p, NavTolerance) == null) continue;
                    chosen = p;
                }
            if (chosen == null) throw new InvalidOperationException("No clear lower-floor loop point in the " + label + " quadrant.");
            add("Floor " + label, chosen.Value, NavTolerance);
        }
        foreach (var st in report.stairs.Where(s => s.fromTier == 1).OrderBy(s => s.label))
        {
            var deck = report.decks.First(d => d.name == st.footSurface);
            var db = deck.bounds.ToBounds(); var sb = st.bounds.ToBounds();
            bool north = db.center.z > report.centre.z;
            float inner = north ? db.min.z : db.max.z, edge = north ? sb.min.z : sb.max.z, dir = north ? -1 : 1;
            // Contiguous capsule-clear run between the stair's arena side and the gallery's inner rail, checked along the stair.
            float best0 = 0, best1 = 0, run0 = float.NaN;
            for (float z = edge + dir * .05f; north ? z > inner : z < inner; z += dir * .05f)
            {
                bool clear = true;
                for (float x = sb.min.x - .6f; x <= sb.max.x + .6f && clear; x += .5f) clear = BodyClear(new Vector3(x, deck.topMax, z), report);
                if (clear) { if (float.IsNaN(run0)) run0 = z; if (Mathf.Abs(z - run0) > Mathf.Abs(best1 - best0)) { best0 = run0; best1 = z; } }
                else run0 = float.NaN;
            }
            if (Mathf.Abs(best1 - best0) < .05f) { report.findings.Add($"{st.label} stair: no capsule-clear gallery lane between the stair and the inner rail."); continue; }
            float laneZ = (best0 + best1) * .5f;
            report.notes.Add(F($"{st.label} stair: gallery lane Z [{Mathf.Min(best0, best1):F2}, {Mathf.Max(best0, best1):F2}] ({Mathf.Abs(best1 - best0) + report.playerRadius * 2:F2} m clear including the capsule) runs past the stair footprint X [{sb.min.x:F2}, {sb.max.x:F2}]."));
            add(st.label + " stair lane east", new Vector3(sb.max.x + .6f, deck.topMax, laneZ), .5f);
            add(st.label + " stair lane west", new Vector3(sb.min.x - .6f, deck.topMax, laneZ), .5f);
        }
        var upper = report.decks.Where(d => d.tier == 2).OrderByDescending(d => d.pieces).First();
        var upperSouth = report.stairs.Where(s => s.toTier == 2 && s.top.position.z < report.centre.z).ToArray();
        var upperNorth = report.stairs.Where(s => s.toTier == 2 && s.top.position.z > report.centre.z).ToArray();
        if (upperSouth.Length > 0 && upperNorth.Length > 0)
        {
            float spineX = (upperSouth.Concat(upperNorth).Min(s => s.top.position.x) + upperSouth.Concat(upperNorth).Max(s => s.top.position.x)) * .5f;
            add("Upper spine south", new Vector3(spineX, upper.topMax, upperSouth.Average(s => s.top.position.z)), .5f);
            add("Upper spine north", new Vector3(spineX, upper.topMax, upperNorth.Average(s => s.top.position.z)), .5f);
        }
        foreach (var bd in report.bayDoors)
        {
            float inward = bd.faceX > report.centre.x ? -1 : 1;
            add((bd.faceX > report.centre.x ? "East" : "West") + " bay-door approach",
                new Vector3(bd.faceX + inward * 2.5f, report.tier2TopY, (bd.bounds.min.z + bd.bounds.max.z) * .5f), .5f);
        }
    }

    static Vector3 Key(Report report, string name) => report.keyPoints.First(k => k.name == name).position;

    static void BuildRoutes(Report report, Vector3 entryNav)
    {
        foreach (var st in report.stairs.Where(s => s.fromTier == 0))
            AddRoute(report, "Entry inside to " + st.label + " stair foot", report.entryInside, st.foot.position, true);
        foreach (var st in report.stairs)
            AddRoute(report, st.label + " stair foot to top", st.foot.position, st.top.position, true);
        foreach (var deck in report.decks.Where(d => d.tier == 1))
        {
            var tops = report.stairs.Where(s => (s.toTier == 1 && s.topSurface == deck.name) || (s.fromTier == 1 && s.footSurface == deck.name))
                .Select(s => s.toTier == 1 ? (s.label + " stair top", s.top.position) : (s.label + " stair foot", s.foot.position)).OrderBy(p => p.Item2.x).ToList();
            for (int i = 0; i + 1 < tops.Count; i++) AddRoute(report, deck.name + ": " + tops[i].Item1 + " to " + tops[i + 1].Item1, tops[i].Item2, tops[i + 1].Item2, true);
        }
        if (report.keyPoints.Any(k => k.name == "Upper spine south"))
        {
            AddRoute(report, "Upper spine south to north", Key(report, "Upper spine south"), Key(report, "Upper spine north"), true);
            foreach (var k in report.keyPoints.Where(k => k.name.EndsWith("bay-door approach")).ToList())
                AddRoute(report, "Upper spine south to " + k.name, Key(report, "Upper spine south"), k.position, true);
        }
        AddRoute(report, "Incline landing to entry inside", report.inclineLanding, report.entryInside, true);
        AddRoute(report, "North-West threshold to entry inside", report.northWestThreshold, report.entryInside, true);
        AddRoute(report, "Boss Hall Door crossing (North-West inside to threshold)", NorthWestInside, report.northWestThreshold);
        foreach (var a in report.actors.Where(a => a.kind != "turret"))
            AddRoute(report, a.name + F($" ({a.position.x:F0}, {a.position.z:F0}) to entry inside"), a.position, report.entryInside);
    }

    static void AddRoute(Report report, string name, Vector3 start, Vector3 end, bool connector = false)
    {
        var route = new Route { name = name, start = start, end = end, connector = connector, corners = Array.Empty<Vector3>(), status = "Endpoint off NavMesh" };
        var a = Nav(start, .6f); var b = Nav(end, .6f);
        if (a.HasValue && b.HasValue)
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(a.Value, b.Value, NavMesh.AllAreas, path);
            route.status = path.status.ToString(); route.corners = path.corners; route.length = Length(path.corners);
        }
        report.routes.Add(route);
    }

    // ---- Zones ------------------------------------------------------------------------------------------------
    static void BuildZones(Report report)
    {
        var room = report.interior.ToBounds();
        float floor = report.floorTopY;
        var reservations = new List<(string name, Func<Vector2, bool> hit)>();
        var entryBox = new Bounds(); entryBox.SetMinMax(new Vector3(room.max.x - 8, floor, report.entry.clearMin - 1), new Vector3(room.max.x, floor + BodyBand, report.entry.clearMax + 1));
        reservations.Add(("Entry approach", p => InBox(p, entryBox)));
        { var a = XZ(report.entryEye); var b = XZ(report.turret.pivot); reservations.Add(("Entry-to-turret sight corridor", p => SegmentDistance(p, a, b) <= SightHalfWidth)); }
        { var c = XZ(report.turret.pivot); float r = report.turret.aimRadius; reservations.Add(("Turret head sweep", p => (p - c).magnitude <= r)); }
        foreach (var a in report.actors.Where(a => a.kind != "turret"))
        { var c = XZ(a.position); float r = a.envelopeRadius; reservations.Add((a.name + F($" ({a.position.x:F0}, {a.position.z:F0}) envelope"), p => (p - c).magnitude <= r)); }
        foreach (var st in report.stairs.Where(s => s.fromTier == 0))
        {
            var b = st.steps.ToBounds();
            var foot = XZ(st.foot.position) + XZ(st.axis) * -StairLanding * .5f;
            b.Encapsulate(new Vector3(foot.x, b.center.y, foot.y)); b.Expand(new Vector3(1, 0, 1));
            reservations.Add((st.label + " stair and landing", p => InBox(p, b)));
        }
        foreach (var route in report.routes.Where(r => r.connector && r.corners.Length > 1 && r.corners.All(c => Mathf.Abs(c.y - floor) < 1f)))
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
        // Underside of overhead structure along a strip limits lower-floor dressing height.
        Func<Vector3, Vector3, float> underside = (a, b) =>
        {
            float top = floor + LowerServicesTop;
            // Inset 0.3 m from the strip edges so the wall collider's own underside is never sampled.
            var lo = Vector3.Min(a, b) + new Vector3(.3f, 0, .3f); var hi = Vector3.Max(a, b) - new Vector3(.3f, 0, .3f);
            for (float t = 0; t <= 1.0001f; t += .1f)
                for (float d = 0; d <= 1.0001f; d += .5f)
                {
                    var p = new Vector3(Mathf.Lerp(lo.x, hi.x, t), floor + .05f, Mathf.Lerp(lo.z, hi.z, d));
                    var up = FirstStatic(p, Vector3.up, LowerServicesTop);
                    if (up.HasValue) top = Mathf.Min(top, up.Value.point.y - .3f);
                }
            return top;
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
            foreach (var (kind, need) in new[] { ("deep-pocket", 5f), ("heavy-perimeter", HeavyDepth), ("wall-flush", WallFlushDepth) })
            {
                int count = 0;
                for (int i = 0; i < n;)
                {
                    if (depth[i] < need) { i++; continue; }
                    int j = i; float minDepth = MaxZoneDepth;
                    while (j < n && depth[j] >= need) { minDepth = Mathf.Min(minDepth, depth[j]); j++; }
                    if ((j - i) * .1f >= 1f)
                    {
                        var a = Corner(origin, along, inward, i * .1f, 0, floor); var b = Corner(origin, along, inward, j * .1f, need, floor);
                        float top = underside(Vector3.Min(a, b), Vector3.Max(a, b));
                        var z = new Zone { name = wall + " " + kind + " " + ++count, kind = kind, level = "Lower floor", min = Vector3.Min(a, b), max = Vector3.Max(a, b), minFreeDepth = minDepth,
                            notes = F($"Floor to Y {top:F2} (the lower of floor+{LowerServicesTop:F0} m and 0.3 m under overhead structure), up to {need:F1} m from the wall face; free depth to the first reservation {minDepth:F1} m.") };
                        z.max.y = top; report.zones.Add(z);
                    }
                    i = j;
                }
            }
            foreach (var (kind, below, above) in new[] { ("reserved", 0f, WallFlushDepth), ("flush-only", WallFlushDepth, HeavyDepth) })
                for (int i = 0; i < n;)
                {
                    if (depth[i] < below || depth[i] >= above) { i++; continue; }
                    int j = i; var names = new HashSet<string>(); float minDepth = MaxZoneDepth;
                    while (j < n && depth[j] >= below && depth[j] < above) { if (blocker[j] != null) names.Add(blocker[j]); minDepth = Mathf.Min(minDepth, depth[j]); j++; }
                    var a = Corner(origin, along, inward, i * .1f, 0, floor); var b = Corner(origin, along, inward, j * .1f, above, report.movementBandTopY);
                    report.zones.Add(new Zone { name = wall + " " + kind + " " + F($"{coord(i):F1}"), kind = kind, level = "Lower floor", min = Vector3.Min(a, b), max = Vector3.Max(a, b), minFreeDepth = minDepth,
                        notes = (kind == "reserved" ? "No body-height dressing: " : "Wall-flush only; limited by: ") + string.Join("; ", names.OrderBy(s => s)) });
                    i = j;
                }
        }
        foreach (var (name, cx, cz, sx, sz) in new[] { ("South-West", room.min.x, room.min.z, 1, 1), ("South-East", room.max.x, room.min.z, -1, 1),
                     ("North-West", room.min.x, room.max.z, 1, -1), ("North-East", room.max.x, room.max.z, -1, -1) })
        {
            float best = 0; string by = null;
            for (float size = .2f; size <= 12 && by == null; size += .2f)
            {
                for (float u = .05f; u <= size && by == null; u += .2f)
                    for (float v = .05f; v <= size && by == null; v += .2f)
                        by = reservedBy(new Vector2(cx + sx * u, cz + sz * v));
                if (by == null) best = size;
            }
            var a = new Vector3(Mathf.Min(cx, cx + sx * best), floor, Mathf.Min(cz, cz + sz * best));
            var b = new Vector3(Mathf.Max(cx, cx + sx * best), floor, Mathf.Max(cz, cz + sz * best));
            float top = best > 0 ? underside(a, b) : floor;
            report.zones.Add(new Zone { name = name + " corner", kind = "corner", level = "Lower floor", min = a, max = new Vector3(b.x, top, b.z), minFreeDepth = best,
                notes = F($"Largest clear square {best:F1} m from the corner, floor to Y {top:F2}; limited by {by ?? "the 12 m search"}.") });
        }

        // Upper levels: every deck, stair and bay-door approach is reserved through the walking headroom.
        foreach (var deck in report.decks)
        {
            var b = deck.bounds.ToBounds();
            report.zones.Add(new Zone { name = deck.name + " walking volume", kind = "reserved-volume", level = deck.tier == 1 ? "First tier" : "Second tier",
                min = new Vector3(b.min.x, deck.topMin, b.min.z), max = new Vector3(b.max.x, deck.topMax + Headroom, b.max.z),
                notes = F($"Bounding box only: the reservation is the union of this deck's piece footprints in deckFootprints, through {Headroom:F1} m headroom. Clear walking width median {deck.clearWidthMedian:F2} m, 5th percentile {deck.clearWidthP5:F2} m. No dressing on the deck; do not narrow it.") });
        }
        foreach (var st in report.stairs)
        {
            var b = st.bounds.ToBounds(); b.Expand(new Vector3(1, 0, 1));
            b.Encapsulate(st.foot.position + st.axis * -StairLanding); b.Encapsulate(st.top.position + st.axis * StairLanding);
            report.zones.Add(new Zone { name = st.label + " stair and landings", kind = "reserved-volume", level = "Stairs", min = new Vector3(b.min.x, st.footY, b.min.z),
                max = new Vector3(b.max.x, st.topY + Headroom, b.max.z), notes = "Steps, rails, both landings and 2.6 m headroom." });
        }
        foreach (var bd in report.bayDoors)
            report.zones.Add(new Zone { name = bd.name + " approach", kind = "reserved-volume", level = "Second tier", min = bd.approach.min, max = new Vector3(bd.approach.max.x, bd.bounds.max.y, bd.approach.max.z),
                notes = "Upper bay-door approach and door face; retain the existing geometry." });
        report.zones.Add(new Zone { name = "Turret head sweep", kind = "reserved-volume", level = "Lower floor", min = report.turret.aimClearance.min, max = report.turret.aimClearance.max,
            notes = "Do not intrude: turret footprint, head sweep and aiming clearance." });
        report.zones.Add(new Zone { name = "Entry-to-turret sight corridor", kind = "reserved-volume", level = "Lower floor", min = report.turret.sightCorridor.min, max = report.turret.sightCorridor.max,
            notes = "The approved entry reveal axis to the final combat focal point; keep it unobstructed." });
        var mid = new Zone { name = "Inter-tier firing volume", kind = "reserved-volume", level = "Air", min = new Vector3(room.min.x + WallFlushDepth, report.movementBandTopY, room.min.z + WallFlushDepth),
            max = new Vector3(room.max.x - WallFlushDepth, report.tier2TopY + Headroom, room.max.z - WallFlushDepth),
            notes = "Open air between the floor movement band and second-tier headroom, inset 1.2 m from the walls. Firing lines between the floor, both tiers and the turret cross it; no hanging dressing here." };
        report.zones.Add(mid);
        float overheadMin = Mathf.Max(report.tier2TopY + Headroom, report.highestStructureTopY + .3f);
        report.zones.Add(new Zone { name = "Overhead", kind = "overhead", level = "Overhead", min = new Vector3(room.min.x + .3f, overheadMin, room.min.z + .3f),
            max = new Vector3(room.max.x - .3f, report.ceilingUndersideY - .3f, room.max.z - .3f),
            notes = F($"Highest existing structure top Y {report.highestStructureTopY:F2}; skylight glass reaches Y {report.skylightTopY:F2} above the turret void. Overhead structure needs an intersection validation and must not change vertical gameplay.") });
        // Wall services above the walking headroom of each gallery, and above the second tier on every wall.
        foreach (var deck in report.decks.Where(d => d.tier == 1))
        {
            bool northSide = deck.bounds.ToBounds().center.z > room.center.z;
            float wz = northSide ? room.max.z : room.min.z, iz = northSide ? -WallFlushDepth : WallFlushDepth;
            var cuts = report.stairs.Where(s => s.toTier == 2 && (s.foot.position.z > room.center.z) == northSide).Select(s => (s.bounds.min.x - .5f, s.bounds.max.x + .5f)).ToList();
            foreach (var b in report.stairs.Where(s => s.fromTier == 1 || s.toTier == 1).Select(s => s.bounds.ToBounds()).Where(b => (b.center.z > room.center.z) == northSide))
                cuts.Add((b.min.x - .5f, b.max.x + .5f));
            int k = 0;
            foreach (var (a, b) in Subtract(room.min.x, room.max.x, cuts))
                if (b - a >= 1f)
                    report.zones.Add(new Zone { name = deck.name + " wall services " + ++k, kind = "wall-services", level = "First tier",
                        min = new Vector3(a, deck.topMax + Headroom, Mathf.Min(wz, wz + iz)), max = new Vector3(b, report.tier2UndersideY - .3f, Mathf.Max(wz, wz + iz)), minFreeDepth = WallFlushDepth,
                        notes = "Wall-mounted services above the gallery headroom and below second-tier structure; stair runs are excluded. Do not drop onto the deck." });
        }
        foreach (var (wall, isX, coordWall, inward) in new[] { ("North", false, room.max.z, -1f), ("South", false, room.min.z, 1f), ("East", true, room.max.x, -1f), ("West", true, room.min.x, 1f) })
        {
            var cuts = report.bayDoors.Where(bd => isX && Mathf.Abs(bd.faceX - coordWall) < 1.5f).Select(bd => (bd.bounds.min.z - 1f, bd.bounds.max.z + 1f)).ToList();
            float a0 = isX ? room.min.z : room.min.x, a1 = isX ? room.max.z : room.max.x;
            int k = 0;
            foreach (var (a, b) in Subtract(a0, a1, cuts))
            {
                if (b - a < 1f) continue;
                var p = isX ? new Vector3(coordWall, overheadMin, a) : new Vector3(a, overheadMin, coordWall);
                var q = isX ? new Vector3(coordWall + inward * WallFlushDepth, report.ceilingUndersideY - .3f, b) : new Vector3(b, report.ceilingUndersideY - .3f, coordWall + inward * WallFlushDepth);
                report.zones.Add(new Zone { name = wall + " upper wall services " + ++k, kind = "wall-services", level = "Upper walls", min = Vector3.Min(p, q), max = Vector3.Max(p, q),
                    minFreeDepth = WallFlushDepth, notes = "Wall-mounted services above second-tier headroom; bay-door faces are excluded." });
            }
        }
        // The entry reveal frame: east wall above and around the opening stays a composition column for env_056.
        var col = new Zone { name = "East entry reveal column", kind = "column", level = "Lower floor", min = new Vector3(room.max.x - WallFlushDepth, floor, report.entry.clearMin - 1),
            max = new Vector3(room.max.x, report.tier1TopY + Headroom, report.entry.clearMax + 1), notes = F($"Entry opening Z [{report.entry.clearMin:F2}, {report.entry.clearMax:F2}], lintel Y {report.entry.lintelY:F2}. Keep the opening and its framing clear; composition belongs to the reveal pass.") };
        report.zones.Add(col);
    }

    static void BuildFindings(Report report)
    {
        if (report.duplicateRenderers.Count > 0) report.findings.Add($"{report.duplicateRenderers.Count} sets of coincident duplicate renderers (same mesh, same bounds). Shell cleanup is out of scope; separately scoped follow-up.");
        if (report.duplicateColliders.Count > 0) report.findings.Add($"{report.duplicateColliders.Count} sets of coincident duplicate colliders.");
        if (report.negativeScaleColliders.Count > 0)
            report.findings.Add($"{report.negativeScaleColliders.Count} hall colliders have negative scale; Unity forces BoxCollider sizes positive, so their collision may differ from the visible piece: " +
                string.Join(", ", report.negativeScaleColliders.Select(n => n.Split(' ')[0] + " " + n.Split(' ')[1])) + ".");
        if (report.floorOverlaps.Count > 0) report.findings.Add($"{report.floorOverlaps.Count} partially overlapping floor-tile collider pairs (exact duplicates excluded).");
        foreach (var w in report.walls.Where(w => w.lowGaps.Count > 0))
            report.findings.Add($"{w.wall} wall: {w.lowGaps.Count} of 13 stations have no wall collider at floor +0.5 m; at {w.lowGaps.Count(g => g.Contains("fits"))} the saved crouched capsule ({report.crouchHeight:F2} m) fits through the wall plane.");
        var crossing = report.rails.Where(r => r.crossesEye).ToList();
        if (crossing.Count > 0)
            report.findings.Add(F($"{crossing.Count} rail colliders cross standing-eye height (eye {report.playerEyeHeight:F2} m above deck); tiers {string.Join(", ", crossing.Select(r => r.tier).Distinct())}; top up to {crossing.Max(r => r.topAboveDeck):F2} m above deck."));
        foreach (var g in report.railTests.GroupBy(t => t.tier))
        {
            int shots = g.Sum(t => t.shots), rail = g.Sum(t => t.railBlocked), level = g.Count(t => t.levelBlocked);
            report.findings.Add(F($"Tier {g.Key} firing tests: {g.Count()} stations, {level} with a level shot blocked by a rail within 1.5 m; {rail}/{shots} downward shots stopped by a rail collider."));
        }
        foreach (var d in report.decks.Where(d => d.openEdge > 0))
            report.findings.Add(F($"{d.name}: {d.openEdge * Grid:F2} m of open drop edge with no rail collision, in {d.openEdges.Count} run(s); the longest is {d.openEdges.Select(e => float.Parse(e.Substring(e.LastIndexOf('(') + 1).Split(' ')[0], CultureInfo.InvariantCulture)).Max():F2} m."));
        foreach (var d in report.decks.Where(d => d.gaps.Count > 0))
            report.findings.Add($"{d.name}: {d.gaps.Count} unsupported join gap(s) inside the deck footprint.");
        foreach (var s in report.stairs.Where(s => s.typicalRiser > report.playerStepOffset + .001f))
            report.findings.Add(F($"{s.label} stair: repeating riser {s.typicalRiser:F3} m ({s.risers} steps) exceeds the player CharacterController step offset {report.playerStepOffset:F2} m; NavMesh agents (climb 1 m) can use it."));
        foreach (var s in report.stairs.Where(s => s.typicalRiser <= report.playerStepOffset + .001f && s.maxRiser > report.playerStepOffset + .001f))
            report.notes.Add(F($"{s.label} stair: repeating riser {s.typicalRiser:F3} m; the single {s.maxRiser:F3} m value is a narrow dip where the steps meet a landing (see profile)."));
        foreach (var s in report.stairs.Where(s => !s.layersCollideWithPlayer))
            report.findings.Add($"{s.label} stair: a collider layer ({s.colliderLayers}) ignores the player layer.");
        foreach (var s in report.stairs.Where(s => s.path != "PathComplete"))
            report.findings.Add($"{s.label} stair: NavMesh foot-to-top route is {s.path}.");
        if (report.roofNavHits > 0)
            report.findings.Add($"Roof: {report.roofNavHits}/{report.roofNavSamples} roof samples carry baked NavMesh above the ceiling; {report.roofNavReachable} reachable from the entry.");
        foreach (var a in report.actors.Where(a => a.pathToNorthWestRoom.StartsWith("PathComplete")))
            report.findings.Add($"{a.name} at ({a.position.x:F1}, {a.position.z:F1}) has a complete baked NavMesh path into the North-West Room (Edit mode, door carving not applied).");
        foreach (var a in report.actors.Where(a => !a.onNavMesh || a.navOffset > .35f))
            report.findings.Add(F($"{a.name} at ({a.position.x:F1}, {a.position.z:F1}) is {(a.onNavMesh ? $"{a.navOffset:F2} m from" : "off")} the NavMesh."));
        foreach (var b in report.bayDoors.Where(b => b.closed))
            report.findings.Add($"{b.name} is closed by collision ({b.closedBy}); the approach is a dead end.");
        report.findings.Add(F($"The turret's rest axis is yaw {report.turret.restYaw:F0} deg; the entry-to-turret focal axis is yaw {report.focalYaw:F0} deg over {report.focalDistance:F1} m, offset {report.focalOffset:+0.00;-0.00} m in Z from the entry centre. First turret sightline on the incline at {report.turretRevealDistance:F1} m from the Boss Hall Door."));
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------
    static string RootKind(Transform t)
    {
        string n = HierarchyFolder.PieceRoot(t).name;
        if (n.StartsWith("Floor Tile", StringComparison.Ordinal)) return "floor";
        if (n.StartsWith("Ceiling", StringComparison.Ordinal) || n.StartsWith("LP_ceiling_tile", StringComparison.Ordinal) || n.StartsWith("LP_Skylight", StringComparison.Ordinal)) return "ceiling";
        if (n.StartsWith("Wall", StringComparison.Ordinal)) return "wall";
        if (n.StartsWith("Catwalk Rails", StringComparison.Ordinal)) return "rail";
        if (n.StartsWith("Catwalk Stairs", StringComparison.Ordinal) || n.StartsWith("CatWalk Stairs", StringComparison.Ordinal)) return "stairs";
        if (n.StartsWith("Catwalk", StringComparison.Ordinal) || n.StartsWith("CatWalk", StringComparison.Ordinal)) return "deck";
        if (n == InclineName) return "incline";
        return null;
    }

    static bool IsDeckShape(Collider c) => c.bounds.size.y < .6f && Mathf.Min(c.bounds.size.x, c.bounds.size.z) >= 1f;

    static bool IsActor(Collider c) => c is CharacterController || c.GetComponentInParent<NavMeshAgent>(true) != null ||
        c.GetComponentInParent<PlayerCharacterController>(true) != null;

    static RaycastHit? FirstStatic(Vector3 origin, Vector3 direction, float distance)
    {
        var hits = Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore);
        RaycastHit best = default; bool found = false;
        foreach (var h in hits) if (solidSet.Contains(h.collider) && (!found || h.distance < best.distance)) { best = h; found = true; }
        return found ? best : (RaycastHit?)null;
    }

    static bool Visible(Vector3 eye, Vector3 target, string ownerId) => Visible(eye, target, ownerId, out _);

    // Clear when no static collider, and no collider of another actor, lies between; the target actor never occludes itself.
    static bool Visible(Vector3 eye, Vector3 target, string ownerId, out string blocker)
    {
        blocker = "";
        Vector3 delta = target - eye; float distance = delta.magnitude;
        if (distance < .05f) return true;
        foreach (var h in Physics.RaycastAll(eye, delta / distance, distance - .05f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (h.collider is CharacterController || h.collider.GetComponentInParent<PlayerCharacterController>(true)) continue;
            var agent = h.collider.GetComponentInParent<NavMeshAgent>(true);
            if (agent && GlobalObjectId.GetGlobalObjectIdSlow(agent.gameObject).ToString() == ownerId) continue;
            if (!agent && !solidSet.Contains(h.collider)) continue;
            blocker = PathOf(h.collider.transform);
            return false;
        }
        return true;
    }

    static bool BodyClear(Vector3 feet, Report report)
    {
        float r = report.playerRadius;
        return !Physics.OverlapCapsule(feet + Vector3.up * (r + .05f), feet + Vector3.up * (report.playerHeight - r), r, ~0, QueryTriggerInteraction.Ignore)
            .Any(c => solidSet.Contains(c));
    }

    static Vector3? Nav(Vector3 p, float tolerance) =>
        NavMesh.SamplePosition(p, out var hit, tolerance, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - p.y) < .6f ? hit.position : (Vector3?)null;

    static bool Complete(Vector3 a, Vector3 b, out float length)
    {
        var path = new NavMeshPath(); length = 0;
        if (!NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        length = Length(path.corners); return true;
    }

    static float Length(Vector3[] corners)
    {
        float l = 0; for (int i = 1; i < corners.Length; i++) l += Vector3.Distance(corners[i - 1], corners[i]); return l;
    }

    static List<List<Collider>> Clusters(List<Collider> set)
    {
        var parent = Enumerable.Range(0, set.Count).ToArray();
        Func<int, int> find = null; find = i => parent[i] == i ? i : parent[i] = find(parent[i]);
        for (int i = 0; i < set.Count; i++)
            for (int j = i + 1; j < set.Count; j++)
            {
                var a = set[i].bounds; var b = set[j].bounds;
                if (a.min.x - .2f <= b.max.x && b.min.x - .2f <= a.max.x && a.min.z - .2f <= b.max.z && b.min.z - .2f <= a.max.z && Mathf.Abs(a.max.y - b.max.y) < .15f)
                    parent[find(i)] = find(j);
            }
        return Enumerable.Range(0, set.Count).GroupBy(find).Select(g => g.Select(i => set[i]).ToList()).OrderByDescending(g => g.Count).ToList();
    }

    static IEnumerable<(int count, Vector3 at, Vector3 size)> Cluster(List<Vector3> points, float link = 3f)
    {
        var left = new List<Vector3>(points);
        while (left.Count > 0)
        {
            var group = new List<Vector3> { left[0] }; left.RemoveAt(0);
            for (int k = 0; k < group.Count; k++)
            {
                var near = left.Where(q => Flat(q - group[k]).magnitude <= link).ToList();
                group.AddRange(near); left.RemoveAll(near.Contains);
            }
            var b = new Bounds(group[0], Vector3.zero); foreach (var g in group) b.Encapsulate(g);
            yield return (group.Count, b.center, b.size);
        }
    }
    static IEnumerable<(int count, Vector3 at)> Cluster(List<Vector3> points) => Cluster(points, 3f).Select(c => (c.count, c.at));

    static IEnumerable<string> Overlaps(Collider[] tiles)
    {
        for (int a = 0; a < tiles.Length; a++)
            for (int b = a + 1; b < tiles.Length; b++)
            {
                var min = Vector3.Max(tiles[a].bounds.min, tiles[b].bounds.min);
                var max = Vector3.Min(tiles[a].bounds.max, tiles[b].bounds.max);
                if (Key(tiles[a].bounds) == Key(tiles[b].bounds)) continue; // Exact duplicates are listed separately.
                if (max.x - min.x > .05f && max.z - min.z > .05f && max.y - min.y > .01f)
                    yield return PathOf(tiles[a].transform) + " <> " + PathOf(tiles[b].transform) + "; intersection " + min.ToString("F3") + " to " + max.ToString("F3");
            }
    }

    static IEnumerable<Vector3> Corners(Bounds b)
    {
        for (int i = 0; i < 8; i++) yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
    }

    static float Mode(IEnumerable<float> values)
    {
        var list = values.ToList();
        if (list.Count == 0) throw new InvalidOperationException("No values to take a mode of; inspect the shell.");
        return list.GroupBy(v => Mathf.Round(v * 100) / 100).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
    }

    static string Key(Bounds b) => F($"{Mathf.Round(b.center.x * 50):F0},{Mathf.Round(b.center.y * 50):F0},{Mathf.Round(b.center.z * 50):F0},{Mathf.Round(b.size.x * 50):F0},{Mathf.Round(b.size.y * 50):F0},{Mathf.Round(b.size.z * 50):F0}");

    static Vector3 Surface(Collider[] colliders, Vector3 origin, Vector3 direction)
    {
        float distance = float.MaxValue; Vector3 result = default;
        foreach (var c in colliders)
            if (c.Raycast(new Ray(origin, direction), out var hit, 60) && hit.distance < distance) { distance = hit.distance; result = hit.point; }
        if (distance == float.MaxValue) throw new InvalidOperationException("No wall surface from " + origin + " along " + direction);
        return result;
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

    static Vector3 Corner(Vector2 origin, Vector2 along, Vector2 inward, float s, float d, float y)
    {
        Vector2 p = origin + along * s + inward * d;
        return new Vector3(p.x, y, p.y);
    }

    static string Dir(Vector3 d) => d.x > .5f ? "+X" : d.x < -.5f ? "-X" : d.z > .5f ? "+Z" : "-Z";
    static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    static T Single<T>(Scene scene, string name) where T : Component
    {
        var found = All<T>(scene).Where(c => c.name == name).ToArray();
        if (found.Length != 1) throw new InvalidOperationException($"Expected exactly one {typeof(T).Name} named '{name}', found {found.Length}.");
        return found[0];
    }
    static bool InsideXZ(Vector3 p, Bounds b, float margin) => p.x > b.min.x - margin && p.x < b.max.x + margin && p.z > b.min.z - margin && p.z < b.max.z + margin;
    static bool InBox(Vector2 p, Bounds b) => p.x >= b.min.x && p.x <= b.max.x && p.y >= b.min.z && p.y <= b.max.z;
    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }
    static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
    public static Bounds ToBounds(this Box box) { var b = new Bounds(); b.SetMinMax(box.min, box.max); return b; }
    static string F(FormattableString s) => FormattableString.Invariant(s);
    public static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;

    static void Draw(SceneView view)
    {
        if (last == null || SceneManager.GetActiveScene().path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Handles.color = Color.cyan; DrawBox(last.interior);
        Handles.color = Color.green; foreach (var d in last.decks) DrawBox(d.bounds);
        Handles.color = Color.yellow; foreach (var s in last.stairs) { DrawBox(s.bounds); Handles.Label(s.top.position + Vector3.up, s.label); }
        Handles.color = Color.red;
        foreach (var a in last.actors) { Handles.DrawWireDisc(a.position, Vector3.up, a.envelopeRadius); Handles.Label(a.position + Vector3.up * 3, a.name); }
        DrawBox(last.turret.sightCorridor);
        Handles.color = new Color(1, .5f, .2f);
        foreach (var z in last.zones.Where(z => z.kind == "heavy-perimeter" || z.kind == "corner")) Handles.DrawWireCube((z.min + z.max) * .5f, z.max - z.min);
        foreach (var r in last.routes.Where(r => r.connector && r.corners.Length > 1)) Handles.DrawAAPolyLine(3, r.corners);
    }
    static void DrawBox(Box box) => Handles.DrawWireCube((box.min + box.max) * .5f, box.max - box.min);
}
