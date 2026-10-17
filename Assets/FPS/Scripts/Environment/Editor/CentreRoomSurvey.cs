using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Read-only, repeatable saved-scene survey for corebuster_env_022. Never saves or builds scene assets.</summary>
public static class CentreRoomSurvey
{
    public const string ScenePath = "Assets/FPS/Scenes/MainScene.unity";
    public const string Output = "ReviewCaptures/CentreRoom/022";
    const float Step = .02f;
    static readonly Vector3 Seed = new Vector3(-198.3f, 1.5f, 66.9f);

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
            onNavMesh = NavMesh.SamplePosition(p, out var hit, .3f, NavMesh.AllAreas);
            navPosition = onNavMesh ? hit.position : p; // JsonUtility otherwise writes non-JSON Infinity values.
        }
    }
    [Serializable] public class Gate
    {
        public string name;
        public Vector3 position, outward, along;
        public bool startsLocked;
        public Box blocker;
        public float clearMin, clearMax, lintelY;
        public float visibleOpenMin, visibleOpenMax, visibleLintelY;
        public Box approach;
        public List<string> inspectedColliders = new List<string>();
    }
    [Serializable] public class Route
    {
        public string name, status;
        public Vector3 start, end;
        public Vector3[] corners;
    }
    [Serializable] public class Report
    {
        public string unityVersion, sceneSha256;
        public Box interior;
        public List<Box> shell = new List<Box>();
        public List<string> floorOverlaps = new List<string>();
        public List<Gate> gates = new List<Gate>();
        public Point terminal, terminalFocus, terminalStanding;
        public Vector3 terminalForward;
        public float terminalRange, terminalEyeDistance;
        public bool terminalVisible, terminalStandingClear;
        public Box terminalKeepOut;
        public List<Point> spawns = new List<Point>();
        public List<Point> occupants = new List<Point>();
        public List<string> waves = new List<string>();
        public List<string> navigation = new List<string>();
        public Point floorSupport;
        public int shellFloorSamplesMissing, ringActorOverlaps;
        public List<Vector3> floorSupportMisses = new List<Vector3>();
        public int navSamples, navHits, ringSamples, ringNavHits, ringBodyBlocked;
        public List<Vector3> navMisses = new List<Vector3>();
        public List<Route> routes = new List<Route>();
        public Vector3 ringCenter;
        public float ringInnerRadius = 4f, ringOuterRadius = 8f, spawnRadius = 2f;
        public List<string> notes = new List<string>();
    }

    static Report last;
    static Collider[] solids;

    [MenuItem("Tools/CoreBuster/Measure Centre Room")]
    public static void MeasureMenu()
    {
        last = Measure();
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/Measurement.json", JsonUtility.ToJson(last, true));
        SceneView.duringSceneGui -= Draw;
        SceneView.duringSceneGui += Draw;
        SceneView.RepaintAll();
        Debug.Log("Centre Room survey written to " + Output + "/Measurement.json. Scene overlay enabled; no scene saved.");
    }

    [MenuItem("Tools/CoreBuster/Hide Centre Room Survey")]
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

    [MenuItem("Tools/CoreBuster/Capture Centre Room Survey")]
    public static void CaptureMenu()
    {
        MeasureMenu();
        var go = new GameObject("Centre Room survey camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 65; camera.nearClipPlane = .05f; camera.farClipPlane = 200;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        camera.GetUniversalAdditionalCameraData().volumeLayerMask = ~0;
        try
        {
            var views = new List<(string name, Vector3 eye, Vector3 target)>();
            foreach (var gate in last.gates)
                views.Add(("Gate_" + gate.name.Substring(gate.name.Length - 2, 1),
                    gate.position - gate.outward * 5 + Vector3.up * 1.6f, gate.position + Vector3.up * 1.5f));
            views.Add(("Terminal", last.terminalStanding.position + Vector3.up * 1.6f, last.terminalFocus.position));
            views.Add(("Room_From_South", new Vector3(last.interior.min.x + 5, last.interior.min.y + 3, last.interior.min.z + 5),
                last.ringCenter + Vector3.up * 2));
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

    static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

    public static Report Measure()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || scene.isDirty || EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Open a clean, saved MainScene alone in Edit mode before measuring. The survey will not save or discard edits.");
        Physics.SyncTransforms();
        solids = All<Collider>(scene).Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
        var report = new Report { unityVersion = Application.unityVersion, sceneSha256 = Hash(ScenePath) };
        var renderers = All<MeshRenderer>(scene).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        // The provisional seed is only a locator. Actual surfaces come from fixed shell collider hits.
        var shellColliders = solids.Where(c => ShellRoot(c.transform) != null).ToArray();
        var tileRenderers = renderers.Where(r => ShellRoot(r.transform) != null &&
            ShellRoot(r.transform).name.StartsWith("Floor Tile", StringComparison.Ordinal) &&
            Mathf.Abs(r.bounds.center.x - Seed.x) < 16.5f && Mathf.Abs(r.bounds.center.z - Seed.z) < 16.5f).ToArray();
        float floorY = tileRenderers.Where(r => r.bounds.center.y < 2).Max(r => r.bounds.max.y);
        float ceilingY = tileRenderers.Where(r => r.bounds.center.y > 5).Min(r => r.bounds.min.y);
        Vector3 low = new Vector3(Surface(shellColliders, Seed, Vector3.left).x,
            floorY, Surface(shellColliders, Seed, Vector3.back).z);
        Vector3 high = new Vector3(Surface(shellColliders, Seed, Vector3.right).x,
            ceilingY, Surface(shellColliders, Seed, Vector3.forward).z);
        var bounds = new Bounds(); bounds.SetMinMax(low, high);
        if (bounds.size.x < 25 || bounds.size.x > 40 || bounds.size.z < 25 || bounds.size.z > 40 || bounds.size.y < 5)
            throw new InvalidOperationException("Unexpected Centre Room shell measurement; inspect the fixed shell anchors.");
        report.interior = new Box("Measured interior", bounds);
        // The saved HoverBot is 3.97 m east of the geometric centre. A 1.5 m westward study offset
        // preserves its 2 m reservation beside a maximum-radius 3 m reactor. This moves no scene object.
        report.ringCenter = new Vector3(bounds.center.x - 1.5f, low.y, bounds.center.z);
        var supportHit = Physics.RaycastAll(Seed, Vector3.down, 3, ~0, QueryTriggerInteraction.Ignore)
            .OrderBy(h => h.distance).First();
        report.floorSupport = new Point(PathOf(supportHit.transform), supportHit.point, supportHit.collider);
        var shellRenderers = renderers.Where(r => ShellRoot(r.transform) != null &&
            r.bounds.center.x >= low.x - 1 && r.bounds.center.x <= high.x + 1 &&
            r.bounds.center.z >= low.z - 1 && r.bounds.center.z <= high.z + 1).OrderBy(r => PathOf(r.transform)).ToArray();
        foreach (var r in shellRenderers) report.shell.Add(new Box(PathOf(r.transform), r.bounds, r));
        var floors = shellRenderers.Where(r => r.bounds.max.y < low.y + .1f && r.bounds.min.y > low.y - 1).ToArray();
        for (int a = 0; a < floors.Length; a++)
            for (int b = a + 1; b < floors.Length; b++)
            {
                var min = Vector3.Max(floors[a].bounds.min, floors[b].bounds.min);
                var max = Vector3.Min(floors[a].bounds.max, floors[b].bounds.max);
                if (max.x - min.x > .05f && max.z - min.z > .05f && max.y - min.y > .01f)
                    report.floorOverlaps.Add(PathOf(floors[a].transform) + " <> " + PathOf(floors[b].transform) +
                        "; intersection " + min.ToString("F3") + " to " + max.ToString("F3"));
            }
        var terminals = All<KeycardTerminal>(scene).Where(t => t.name == "Centre Room Terminal (Yellow)").ToArray();
        if (terminals.Length != 1)
            throw new InvalidOperationException($"Expected exactly one Centre Room Terminal (Yellow), found {terminals.Length}. " +
                "Do not rename, duplicate, move into dressing, or remove the gameplay terminal.");
        var terminal = terminals[0];
        if (terminal.SpawnPoints == null || terminal.SpawnPoints.Length != 8 || terminal.Doors == null || terminal.Doors.Length != 3)
            throw new InvalidOperationException("Centre Room Terminal (Yellow) must reference exactly eight spawn markers and three gates; re-audit the gameplay configuration.");
        if (terminal.Doors.Any(d => !d))
            throw new InvalidOperationException("Centre Room Terminal (Yellow) has a missing gate reference.");
        foreach (var door in terminal.Doors) report.gates.Add(MeasureGate(door, report));
        report.terminal = new Point(PathOf(terminal.transform), terminal.transform.position, terminal);
        report.terminalFocus = new Point("Terminal focus", terminal.FocusPosition);
        report.terminalForward = terminal.transform.forward;
        report.terminalRange = terminal.Range;
        Vector3 standing = terminal.FocusPosition + terminal.transform.forward * 1.6f;
        standing.y = low.y;
        report.terminalStanding = new Point("Terminal standing point", standing);
        Vector3 eye = standing + Vector3.up * 1.6f;
        report.terminalEyeDistance = Vector3.Distance(eye, terminal.FocusPosition);
        report.terminalVisible = !solids.Any(c => !c.transform.IsChildOf(terminal.transform) &&
            c.Raycast(new Ray(eye, (terminal.FocusPosition - eye).normalized), out _, report.terminalEyeDistance - .05f));
        report.terminalStandingClear = BodyClear(standing);
        Bounds terminalBounds = new Bounds(terminal.transform.position, Vector3.zero);
        foreach (var r in terminal.GetComponentsInChildren<Renderer>()) terminalBounds.Encapsulate(r.bounds);
        terminalBounds.Encapsulate(standing + Vector3.right * 1.5f + Vector3.forward * 1.5f);
        terminalBounds.Encapsulate(standing - Vector3.right * 1.5f - Vector3.forward * 1.5f);
        terminalBounds.SetMinMax(new Vector3(terminalBounds.min.x, low.y, terminalBounds.min.z),
            new Vector3(terminalBounds.max.x, low.y + 2.2f, terminalBounds.max.z));
        report.terminalKeepOut = new Box("Terminal access and sightline", terminalBounds);
        foreach (var spawn in terminal.SpawnPoints)
        {
            if (!spawn) throw new InvalidOperationException("Missing terminal spawn reference.");
            report.spawns.Add(new Point(PathOf(spawn), spawn.position, spawn));
        }
        for (int i = 0; i < terminal.Waves.Length; i++)
            report.waves.Add("Wave " + (i + 1) + ": " + string.Join(", ", terminal.Waves[i].Enemies.Select(e => e ? e.name : "MISSING")));
        foreach (var agent in All<NavMeshAgent>(scene))
            if (InsideXZ(agent.transform.position, bounds))
            {
                report.occupants.Add(new Point(PathOf(agent.transform), agent.transform.position, agent));
                report.navigation.Add(FormattableString.Invariant($"{PathOf(agent.transform)}: agentType={agent.agentTypeID}, radius={agent.radius}, height={agent.height}, baseOffset={agent.baseOffset}"));
            }
        foreach (var prefab in terminal.Waves.SelectMany(w => w.Enemies).Where(e => e).Distinct())
            foreach (var agent in prefab.GetComponentsInChildren<NavMeshAgent>(true))
                report.navigation.Add(FormattableString.Invariant($"Wave prefab {prefab.name}: agentType={agent.agentTypeID}, radius={agent.radius}, height={agent.height}"));
        foreach (var component in All<MonoBehaviour>(scene).Where(m => m && m.GetType().Name == "NavMeshSurface"))
        {
            var serialized = new SerializedObject(component);
            var data = serialized.FindProperty("m_NavMeshData")?.objectReferenceValue;
            report.navigation.Add("Surface " + PathOf(component.transform) + ": " + (data ? AssetDatabase.GetAssetPath(data) : "NO DATA"));
        }
        var settings = NavMesh.GetSettingsByID(0);
        report.navigation.Add(FormattableString.Invariant($"Baked agent 0: radius={settings.agentRadius}, height={settings.agentHeight}, climb={settings.agentClimb}, slope={settings.agentSlope}"));
        var triangulation = NavMesh.CalculateTriangulation();
        report.navigation.Add($"Loaded scene NavMesh: {triangulation.vertices.Length} vertices, {triangulation.indices.Length / 3} triangles");
        for (float x = low.x + 1; x < high.x - .5f; x += 1)
            for (float z = low.z + 1; z < high.z - .5f; z += 1)
            {
                var p = new Vector3(x, low.y, z); report.navSamples++;
                if (!shellColliders.Any(c => c.Raycast(new Ray(p + Vector3.up * .5f, Vector3.down), out _, .6f)))
                    report.shellFloorSamplesMissing++;
                if (!Physics.Raycast(p + Vector3.up * .5f, Vector3.down, .8f, ~0, QueryTriggerInteraction.Ignore))
                    report.floorSupportMisses.Add(p);
                if (NavMesh.SamplePosition(p, out _, .3f, NavMesh.AllAreas)) report.navHits++;
                else report.navMisses.Add(p);
            }
        for (float radius = report.ringInnerRadius; radius <= report.ringOuterRadius; radius += 1)
            for (int i = 0; i < 72; i++)
            {
                float angle = i * Mathf.PI * 2 / 72;
                Vector3 p = report.ringCenter + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                report.ringSamples++;
                if (NavMesh.SamplePosition(p, out _, .3f, NavMesh.AllAreas)) report.ringNavHits++;
                var overlaps = BodyOverlaps(p);
                if (overlaps.Any(c => c.GetComponentInParent<NavMeshAgent>() == null)) report.ringBodyBlocked++;
                if (overlaps.Any(c => c.GetComponentInParent<NavMeshAgent>() != null)) report.ringActorOverlaps++;
            }
        foreach (var p in report.spawns) AddRoute(report, p.name + " to ring", p.position, RingEntry(report, p.position));
        foreach (var p in report.occupants)
        {
            Vector3 delta = p.position - report.ringCenter; delta.y = 0;
            if (delta.magnitude < 3 + report.spawnRadius)
                throw new InvalidOperationException("Reactor study envelope conflicts with " + p.name + "; revise the study centre, never move the actor.");
        }
        foreach (var gate in report.gates)
        {
            var inside = gate.position - gate.outward * 3; inside.y = low.y;
            var outside = gate.position + gate.outward * 3; outside.y = low.y;
            AddRoute(report, gate.name + " inside to ring", inside, RingEntry(report, inside));
            AddRoute(report, gate.name + " across saved gate", outside, inside);
        }
        AddRoute(report, "Ring to terminal", RingEntry(report, standing), standing);
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.PI * 2 / 24, b = (i + 1) * Mathf.PI * 2 / 24;
            AddRoute(report, "Ring segment " + (i + 1),
                report.ringCenter + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 6,
                report.ringCenter + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * 6);
        }
        var workshopDoor = All<SecurityDoor>(scene).Single(d => d.name == "Spawn Room Door (N)");
        var south = report.gates.Single(g => g.name == "Centre Room Door (S)");
        var routeStart = workshopDoor.transform.position + workshopDoor.transform.right * 3; routeStart.y = low.y;
        var routeEnd = south.position + south.outward * 3; routeEnd.y = low.y;
        AddRoute(report, "Workshop exit corridor to south gate exterior", routeStart, routeEnd);
        report.notes.Add("All values are world metres. Gate scans use 0.02 m conservative sample intervals at floor+0.2, +1.2 and +1.8 m, through 4 m wall depth. Only moving door leaves and their explicit blocker are excluded to measure an unlocked aperture; no transforms or collider states are changed.");
        report.notes.Add("The visible aperture also tests original shell and fixed gate-frame triangles, plus the leaf triangles translated by the configured fully-open slide. Both collision and mesh tests must clear. The wider collision opening defines the conservative approach reservation.");
        report.notes.Add("NavMesh queries describe loaded Edit-mode data, not runtime gate carving or an AI/Play-mode test. A 0.3 m sample tolerance is used. Body clearance uses a radius 0.4 m, height 1.8 m capsule with 0.05 m floor clearance. Ring static obstructions and movable NavMeshAgent overlaps are counted separately.");
        report.notes.Add("The visual floor top and ceiling underside come from original tile renderer bounds. Floor support is tested separately: these room tiles are model instances without the kit prefab's BoxCollider. The existing ground collider supplies movement support.");
        report.notes.Add("Keep-outs are design reservations: 2 m spawn/NPC discs, opening plus 1 m each side and 4 m inside each gate, terminal access box, 4-8 m central annulus. Navigation paths need a 1.5 m half-width reservation; none authorizes moving existing objects.");
        if (scene.isDirty || Hash(ScenePath) != report.sceneSha256)
            throw new InvalidOperationException("Scene changed during survey; do not accept this report.");
        return report;
    }

    static Gate MeasureGate(SecurityDoor door, Report report)
    {
        if (!door || !door.Blocker || !door.LeafA || !door.LeafB) throw new InvalidOperationException("Incomplete gate anchors.");
        var g = new Gate { name = door.name, position = door.transform.position, outward = door.transform.right,
            along = door.transform.forward, startsLocked = door.StartLocked, blocker = new Box("Closed blocker", door.Blocker.bounds, door.Blocker) };
        float floor = report.interior.min.y;
        // Keep fixed frame and shell colliders, including any adjacent corridor structure. Ignore only moving parts.
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
        while (lintel < report.interior.max.y && !blocked(0, lintel + Step)) lintel += Step;
        g.lintelY = lintel;
        // Imported gate meshes have no colliders. Measure their actual triangles as well so the
        // broad west shell opening is not mistaken for the narrower visible gate aperture.
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
            new Vector3(Mathf.Max(a.x, b.x), floor + 2.2f, Mathf.Max(a.z, b.z)));
        g.approach = new Box(door.name + " approach", approach);
        return g;
    }

    // Two-sided Moller-Trumbore ray/triangle intersection in world metres. It does not need
    // temporary colliders, a scene edit, or the Editor's non-public IntersectRayMesh API.
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

    static Collider[] BodyOverlaps(Vector3 feet) => Physics.OverlapCapsule(feet + Vector3.up * .45f,
        feet + Vector3.up * 1.45f, .4f, ~0, QueryTriggerInteraction.Ignore);
    static bool BodyClear(Vector3 feet) => BodyOverlaps(feet).Length == 0;

    static bool InsideXZ(Vector3 p, Bounds b) => p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z;

    static Vector3 RingEntry(Report report, Vector3 from)
    {
        Vector3 direction = from - report.ringCenter; direction.y = 0;
        if (direction.sqrMagnitude < .01f) throw new InvalidOperationException("Gameplay anchor lies at the proposed reactor centre.");
        return report.ringCenter + direction.normalized * 6;
    }

    static void AddRoute(Report report, string name, Vector3 start, Vector3 end)
    {
        var route = new Route { name = name, start = start, end = end, corners = Array.Empty<Vector3>(), status = "Endpoint off NavMesh" };
        if (NavMesh.SamplePosition(start, out var a, .3f, NavMesh.AllAreas) && NavMesh.SamplePosition(end, out var b, .3f, NavMesh.AllAreas))
        {
            var path = new NavMeshPath();
            NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
            route.status = path.status.ToString(); route.corners = path.corners;
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
        Handles.color = Color.yellow; DrawBox(last.terminalKeepOut);
        foreach (var spawn in last.spawns)
        { Handles.DrawWireDisc(spawn.position, Vector3.up, last.spawnRadius); Handles.Label(spawn.position, spawn.name.Split('/').Last()); }
        foreach (var occupant in last.occupants) Handles.DrawWireDisc(occupant.position, Vector3.up, last.spawnRadius);
        Handles.color = Color.green;
        Handles.DrawWireDisc(last.ringCenter, Vector3.up, last.ringInnerRadius);
        Handles.DrawWireDisc(last.ringCenter, Vector3.up, last.ringOuterRadius);
        foreach (var route in last.routes.Where(r => r.corners.Length > 1)) Handles.DrawAAPolyLine(3, route.corners);
    }
    static void DrawBox(Box box) => Handles.DrawWireCube((box.min + box.max) * .5f, box.max - box.min);
}
