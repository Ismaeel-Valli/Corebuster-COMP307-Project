using System;
using System.Collections.Generic;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The Start Workshop interior in MainScene (bounds confirmed by corebuster_env_001), measured from the placed wall,
/// floor-tile and door geometry so dressing sits flush against real surfaces, plus the gameplay keep-out zones.
/// </summary>
public class StartWorkshopFrame
{
    // Wall pivot lines and tile levels from corebuster_env_001. The walls are 1.09-scaled "Wall Plain" segments whose
    // pivot is their room-facing side; the east and north runs face outward, so their thickness intrudes and is measured.
    const float WestLine = -186.59f, EastLine = -154.34f, SouthLine = 2.22f, NorthLine = 34.4f;
    const float FloorLevel = .18f, CeilingLevel = 11.42f;
    // "Alternate Wall Door (4)" fills this gap in the north wall; it is the exit to the corridor.
    const float DoorMinX = -167.16f, DoorMaxX = -160.66f;
    // The shell's actual walkable cut-out, measured with the door open. The door renderer spans a much wider panel;
    // using its bounds as a fallback falsely treats the side panels as part of the opening.
    const float WalkableDoorMinX = -164.96f, WalkableDoorMaxX = -162.81f;
    static readonly Vector3 k_DocumentedSpawn = new Vector3(-167.98f, 3.5f, 17.96f);

    // Keep-out sizes (metres). The player capsule radius is .35; these leave comfortable walk and sprint clearance.
    public const float SpawnClearRadius = 4f;
    public const float RouteHalfWidth = 1.75f;
    public const float DoorApproachDepth = 4f;
    public const float OccupantClearRadius = 1.5f;
    // Props entirely above this height cannot obstruct movement.
    public const float HeadHeight = 2.2f;

    public float West, East, South, North, Floor, Ceiling;
    public float OpeningMinX, OpeningMaxX;
    public Vector3 Spawn;
    public readonly List<Vector3> Occupants = new List<Vector3>();
    public readonly List<string> Notes = new List<string>();

    public Vector3 Center => new Vector3((West + East) * .5f, Floor, (South + North) * .5f);
    public float OpeningCenter => (OpeningMinX + OpeningMaxX) * .5f;
    public Vector3 RouteEnd => new Vector3(OpeningCenter, Floor, North - .5f);

    public static StartWorkshopFrame Measure(Scene scene, Transform exclude)
    {
        var f = new StartWorkshopFrame
        {
            West = WestLine, East = EastLine, South = SouthLine, North = NorthLine,
            Floor = FloorLevel, Ceiling = CeilingLevel, OpeningMinX = DoorMinX, OpeningMaxX = DoorMaxX,
            Spawn = new Vector3(k_DocumentedSpawn.x, FloorLevel, k_DocumentedSpawn.z),
        };
        float floorTop = float.MinValue, ceilingBottom = float.MaxValue;
        float west = float.MinValue, east = float.MaxValue, south = float.MinValue, north = float.MaxValue;
        float doorMin = float.MaxValue, doorMax = float.MinValue;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || Excluded(r.transform, exclude)) continue;
                Bounds b = r.bounds;
                Vector3 c = b.center;
                bool inX = c.x > WestLine - 1 && c.x < EastLine + 1;
                bool inZ = c.z > SouthLine - 1 && c.z < NorthLine + 1;
                if (Named(r.transform, "Floor Tile"))
                {
                    if (!inX || !inZ) continue;
                    if (c.y < FloorLevel + 3) floorTop = Mathf.Max(floorTop, b.max.y);
                    else if (c.y > FloorLevel + 5) ceilingBottom = Mathf.Min(ceilingBottom, b.min.y);
                }
                else if (Named(r.transform, "Wall") && b.min.y < FloorLevel + 3 && b.max.y > FloorLevel + 1)
                {
                    if (inZ && c.x > WestLine - 2 && c.x < WestLine + 1.5f) west = Mathf.Max(west, b.max.x);
                    if (inZ && c.x > EastLine - 1.5f && c.x < EastLine + 2) east = Mathf.Min(east, b.min.x);
                    if (inX && c.z > SouthLine - 2 && c.z < SouthLine + 1.5f) south = Mathf.Max(south, b.max.z);
                    if (inX && c.z > NorthLine - 1.5f && c.z < NorthLine + 2)
                    {
                        north = Mathf.Min(north, b.min.z);
                        if (Named(r.transform, "Wall Door")) { doorMin = Mathf.Min(doorMin, b.min.x); doorMax = Mathf.Max(doorMax, b.max.x); }
                    }
                }
            }
        }
        f.Floor = Accept(f, "floor", floorTop, FloorLevel, .5f);
        f.Ceiling = Accept(f, "ceiling", ceilingBottom, CeilingLevel, 1.5f);
        f.West = Accept(f, "west wall", west, WestLine, 1.2f);
        f.East = Accept(f, "east wall", east, EastLine, 1.2f);
        f.South = Accept(f, "south wall", south, SouthLine, 1.2f);
        f.North = Accept(f, "north wall", north, NorthLine, 1.2f);
        if (doorMin < doorMax) { f.OpeningMinX = doorMin; f.OpeningMaxX = doorMax; }
        else f.Notes.Add("Door renderer not found; using documented doorway X range.");
        f.MeasureOpening(exclude);
        f.FindOccupants(scene, exclude);
        return f;
    }

    /// <summary>Scans the doorway at waist height for the gap the player actually walks through.</summary>
    void MeasureOpening(Transform exclude)
    {
        Physics.SyncTransforms();
        const float step = .05f;
        float y = Floor + 1.2f, z = North - 1.2f, start = OpeningMinX - 1f;
        int samples = Mathf.CeilToInt((OpeningMaxX - OpeningMinX + 2f) / step), runStart = -1;
        float bestMin = 0, bestMax = 0;
        // The extra final iteration is a closed sentinel that ends any open run.
        for (int i = 0; i <= samples; i++)
        {
            bool open = i < samples && !DoorShellHit(new Vector3(start + i * step, y, z), exclude, out _);
            if (open) { if (runStart < 0) runStart = i; continue; }
            if (runStart < 0) continue;
            float runMin = start + runStart * step, runMax = start + (i - 1) * step;
            if (runMax - runMin > bestMax - bestMin) { bestMin = runMin; bestMax = runMax; }
            runStart = -1;
        }
        if (bestMax - bestMin >= 1f && bestMax - bestMin <= 4f) { OpeningMinX = bestMin; OpeningMaxX = bestMax; }
        else
        {
            OpeningMinX = WalkableDoorMinX;
            OpeningMaxX = WalkableDoorMaxX;
            Notes.Add("Doorway gap not resolved from shell colliders; using the documented walkable cut-out, not the door renderer bounds.");
        }
    }

    /// <summary>Only the fixed north shell defines the opening; actors and other gameplay colliders may move through it.</summary>
    bool DoorShellHit(Vector3 origin, Transform exclude, out RaycastHit nearest)
    {
        nearest = default;
        float reach = North + .6f - origin.z;
        float best = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(origin, Vector3.forward, reach, ~0, QueryTriggerInteraction.Ignore))
            if (!Excluded(hit.transform, exclude) && Named(hit.transform, "Wall") && hit.distance < best)
            {
                nearest = hit;
                best = hit.distance;
            }
        return best < float.MaxValue;
    }

    /// <summary>Lowest solid height above the doorway (the lintel) and the room-facing surface there.</summary>
    public bool FindLintel(Transform exclude, out float bottom, out float surfaceZ)
    {
        Physics.SyncTransforms();
        for (float y = Floor + 1.8f; y < Ceiling - .5f; y += .05f)
        {
            Vector3 origin = new Vector3(OpeningCenter, y, North - 1.2f);
            if (DoorShellHit(origin, exclude, out RaycastHit hit) &&
                DoorShellHit(origin + Vector3.up * .8f, exclude, out _))
            {
                bottom = y;
                surfaceZ = hit.point.z;
                return true;
            }
        }
        bottom = surfaceZ = 0;
        return false;
    }

    void FindOccupants(Scene scene, Transform exclude)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (Excluded(root.transform, exclude)) continue;
            var player = root.GetComponentInChildren<PlayerCharacterController>(true);
            if (player != null) Spawn = new Vector3(player.transform.position.x, Floor, player.transform.position.z);
            foreach (var t in Actors(root))
            {
                if (t.GetComponentInParent<PlayerCharacterController>(true) != null) continue;
                Vector3 p = t.position;
                if (p.x > West && p.x < East && p.z > South && p.z < North && p.y < Floor + 3) Occupants.Add(p);
            }
        }
    }

    static IEnumerable<Transform> Actors(GameObject root)
    {
        // Enemies own Health; decorative characters only own an Animator. Count each actor once.
        var seen = new HashSet<Transform>();
        foreach (var h in root.GetComponentsInChildren<Health>(true)) if (seen.Add(h.transform)) yield return h.transform;
        foreach (var a in root.GetComponentsInChildren<Animator>(true))
            if (a.GetComponentInParent<Health>(true) == null && seen.Add(a.transform)) yield return a.transform;
    }

    public bool IsReachable(Bounds b) => b.min.y < Floor + HeadHeight;

    /// <summary>Why a floor-level bounds would obstruct spawn, the spawn-to-exit route, the doorway or an NPC; null if clear.</summary>
    public string KeepOutViolation(Bounds b)
    {
        if (!IsReachable(b)) return null;
        if (FlatDistance(Spawn, b) < SpawnClearRadius) return "inside the spawn clearing";
        Vector3 end = RouteEnd;
        for (float t = 0; t <= 1f; t += .02f)
            if (FlatDistance(Vector3.Lerp(Spawn, end, t), b) < RouteHalfWidth) return "on the spawn-to-exit route";
        if (b.max.x > OpeningMinX - 1f && b.min.x < OpeningMaxX + 1f && b.max.z > North - DoorApproachDepth)
            return "in the doorway approach";
        foreach (var o in Occupants)
            if (FlatDistance(o, b) < OccupantClearRadius) return "crowding an NPC at " + o.ToString("F1");
        return null;
    }

    public static float FlatDistance(Vector3 p, Bounds b)
    {
        float dx = Mathf.Max(b.min.x - p.x, 0, p.x - b.max.x);
        float dz = Mathf.Max(b.min.z - p.z, 0, p.z - b.max.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public override string ToString() => FormattableString.Invariant(
        $"Workshop interior X [{West:F2}, {East:F2}] Z [{South:F2}, {North:F2}] Y [{Floor:F2}, {Ceiling:F2}], ") +
        FormattableString.Invariant($"exit gap X [{OpeningMinX:F2}, {OpeningMaxX:F2}], spawn {Spawn:F2}, {Occupants.Count} NPC(s) in room") +
        (Notes.Count > 0 ? "\n  " + string.Join("\n  ", Notes) : "");

    static float Accept(StartWorkshopFrame f, string what, float measured, float expected, float tolerance)
    {
        if (Mathf.Abs(measured - expected) <= tolerance) return measured;
        f.Notes.Add($"No plausible {what} geometry measured; using documented {expected:F2}.");
        return expected;
    }

    /// <summary>Nearest non-trigger hit that is not part of <paramref name="exclude"/>.</summary>
    public static bool Blocked(Vector3 origin, Vector3 direction, float distance, Transform exclude, out RaycastHit nearest)
    {
        nearest = default;
        float best = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (Excluded(hit.transform, exclude) || hit.distance >= best) continue;
            best = hit.distance;
            nearest = hit;
        }
        return best < float.MaxValue;
    }

    static bool Excluded(Transform t, Transform exclude) => exclude != null && t.IsChildOf(exclude);

    static bool Named(Transform t, string token)
    {
        // Hierarchy folders ("Walls", "Floors") group pieces; only the pieces' own names count.
        for (; t != null && !t.GetComponent<HierarchyFolder>(); t = t.parent) if (t.name.Contains(token)) return true;
        return false;
    }
}
