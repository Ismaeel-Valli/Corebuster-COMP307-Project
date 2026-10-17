using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// corebuster_game_003. Lays glowing chevrons on the floor along each leg of the keycard progression, in the colour of
/// the card that leg is about (<see cref="FloorGuide"/>, CoreBuster/Floor Guide):
/// <list type="number">
/// <item>tutorial exit to the Workshop door (black card, silver);</item>
/// <item>Workshop terminal to the Centre Room (blue card), once the player holds it;</item>
/// <item>Centre Room terminal to the North-West Room (yellow card);</item>
/// <item>North-West Room terminal to the Master terminal (red card);</item>
/// <item>Master terminal into the Boss Hall, once the player has chosen to fight the core.</item>
/// </list>
/// Each leg follows the baked NavMesh path, pushed toward the middle of the floor and smoothed, so the arrows run down
/// the centre of corridors and round corners rather than along the walls. Rebuilds "===== FLOOR GUIDE =====" each run.
/// </summary>
public static class FloorGuideBuilder
{
    const string RootName = "===== FLOOR GUIDE =====";
    const string ArtRoot = "Assets/FPS/Art/FloorGuide";
    const string CardFolder = "Assets/FPS/Data/Keycards/";
    static readonly Vector3 k_BossEntry = new Vector3(-268f, 6.2f, 115.3f);

    const float Spacing = 2.4f;
    const float WallClearance = .9f;
    const float ChevronLength = .55f, ChevronWidth = .9f, StrokeWidth = .14f;

    [MenuItem("Tools/CoreBuster/Build Floor Guide")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        var doors = Object.FindObjectsByType<SecurityDoor>(FindObjectsInactive.Include).ToDictionary(d => d.name);
        var terminals = Object.FindObjectsByType<KeycardTerminal>(FindObjectsInactive.Include).ToDictionary(t => t.name);
        var master = Object.FindAnyObjectByType<MasterTerminal>();
        var director = Object.FindAnyObjectByType<FinaleDirector>();
        SecurityDoor Door(string name) => doors.TryGetValue(name, out var d) ? d : null;
        Vector3 Terminal(string name) => terminals.TryGetValue(name, out var t) ? t.FocusPosition : Vector3.zero;
        KeycardData Card(string name) => AssetDatabase.LoadAssetAtPath<KeycardData>(CardFolder + name + ".asset");
        if (!Door("Tutorial Wing Exit Door") || !Door("Spawn Room Door (N)") || !master)
        {
            Debug.LogError("Floor guide: open MainScene (keycard progression, tutorial wing and finale built) first.");
            return;
        }

        Directory.CreateDirectory(ArtRoot);
        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject(RootName);
        var guide = root.AddComponent<FloorGuide>();
        guide.Master = master;
        guide.Director = director;
        var report = new StringBuilder();
        var routes = new List<FloorGuide.Route>();
        var centreDoors = new[] { Door("Centre Room Door (S)"), Door("Centre Room Door (W)"), Door("Centre Room Door (N)") }
            .Where(d => d).ToArray();

        void Add(string name, Color color, Vector3[] path, System.Action<FloorGuide.Route> conditions)
        {
            if (path == null)
            {
                report.AppendLine($"  {name}: no NavMesh path, skipped");
                return;
            }
            var points = Refine(path);
            var mesh = Chevrons(points, out int count, out float length);
            mesh.name = "SM_FloorGuide_" + name.Replace(" ", "");
            mesh = Save(mesh, $"{ArtRoot}/{mesh.name}.asset");
            var renderer = new GameObject(name).AddComponent<MeshRenderer>();
            renderer.transform.SetParent(root.transform, false);
            renderer.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer.sharedMaterial = Material(name, color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var route = new FloorGuide.Route { Name = name, Arrows = renderer };
            conditions(route);
            routes.Add(route);
            report.AppendLine($"  {name}: {length:F0} m, {count} arrows");
        }

        // The rooms are separate NavMesh islands (their doors keep enemies in), so a route out of a room is two legs
        // joined through the doorway: the room to its side of the door, then the far side on to the goal.
        // Corridor legs follow the corridor modules themselves (the NavMesh also reaches the ground outside the ship,
        // which a shortest path would happily cut across).
        s_Corridors = CorridorPieces();
        s_Floors = FloorRenderers();
        var northWestDoor = Door("North-West Room Door (E)");
        var workshopDoor = Door("Spawn Room Door (N)");
        Add("To the Workshop", Card("Keycard_Black").Tint,
            CorridorRoute(CorridorSide(Door("Tutorial Wing Exit Door")), CorridorSide(workshopDoor)),
            r => { r.ShowWhenOpened = Door("Tutorial Wing Exit Door"); r.HideWhenOpened = new[] { workshopDoor }; });
        Add("To the Centre Room", Card("Keycard_Blue").Tint,
            OutOfRoom(Terminal("Spawn Room Terminal (Blue)"), new[] { workshopDoor }, centreDoors),
            r => { r.ShowWhenHolding = Card("Keycard_Blue"); r.HideWhenOpened = centreDoors; });
        Add("To the North-West Room", Card("Keycard_Yellow").Tint,
            OutOfRoom(Terminal("Centre Room Terminal (Yellow)"), centreDoors, new[] { northWestDoor }),
            r => { r.ShowWhenHolding = Card("Keycard_Yellow"); r.HideWhenOpened = new[] { northWestDoor }; });
        Add("To the Master Terminal", Card("Keycard_Red").Tint,
            ShortestPath(Terminal("North-West Room Terminal (Red)"), new[] { master.FocusPosition }),
            r => { r.ShowWhenHolding = Card("Keycard_Red"); r.HideWhenMasterUnlocked = true; });
        Add("To the Boss Hall", new Color(1f, .45f, .15f),
            Through(master.FocusPosition, new[] { Door("Boss Hall Door") }, new[] { k_BossEntry }),
            r => { r.ShowDuringFight = true; r.HideWhenArenaStarted = true; });

        guide.Routes = routes.ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"FLOOR GUIDE BUILT: {routes.Count} routes.\n{report}");
    }

    // ------------------------------------------------------------------ paths

    static Vector3 Outside(SecurityDoor door)
    {
        if (!door) return Vector3.zero;
        var t = door.transform;
        // Whichever side of the doorway is the corridor: the one farther from the room's terminal or centre.
        Vector3 best = t.position;
        float bestScore = float.NegativeInfinity;
        foreach (var dir in new[] { t.right, -t.right, t.forward, -t.forward })
        {
            var p = t.position + dir * 3f;
            if (!NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) continue;
            // Corridors are narrow: prefer the side with walls close by.
            float score = -OpenArea(hit.position);
            if (score > bestScore)
            {
                bestScore = score;
                best = hit.position;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ corridor graph

    static List<Rect> s_Corridors;
    static List<Bounds> s_Floors;

    // Each corridor module's footprint (the corridor kit under "Plane (1)").
    static List<Rect> CorridorPieces()
    {
        var result = new List<Rect>();
        var root = GameObject.Find("Plane (1)");
        if (!root) return result;
        foreach (Transform piece in root.transform)
        {
            var renderers = piece.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            if (b.size.x > 60f || b.size.z > 60f) continue;
            result.Add(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z));
        }
        return result;
    }

    static int PieceAt(Vector3 p)
    {
        var q = new Vector2(p.x, p.z);
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < s_Corridors.Count; i++)
        {
            var r = s_Corridors[i];
            float dx = Mathf.Max(r.xMin - q.x, 0f, q.x - r.xMax), dz = Mathf.Max(r.yMin - q.y, 0f, q.y - r.yMax);
            float d = dx * dx + dz * dz;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return bestDistance < 4f ? best : -1;
    }

    static bool InCorridor(Vector3 p)
    {
        var q = new Vector2(p.x, p.z);
        foreach (var r in s_Corridors)
            if (q.x >= r.xMin - .2f && q.x <= r.xMax + .2f && q.y >= r.yMin - .2f && q.y <= r.yMax + .2f) return true;
        return false;
    }

    // Dijkstra over touching corridor modules; the route runs piece centre to piece centre through their shared edges.
    static Vector3[] CorridorRoute(Vector3 from, Vector3 to)
    {
        int start = PieceAt(from), goal = PieceAt(to);
        if (start < 0 || goal < 0) return null;
        int n = s_Corridors.Count;
        var cost = Enumerable.Repeat(float.MaxValue, n).ToArray();
        var previous = Enumerable.Repeat(-1, n).ToArray();
        var done = new bool[n];
        cost[start] = 0f;
        for (int step = 0; step < n; step++)
        {
            int u = -1;
            for (int i = 0; i < n; i++)
                if (!done[i] && (u < 0 || cost[i] < cost[u])) u = i;
            if (u < 0 || cost[u] == float.MaxValue || u == goal) break;
            done[u] = true;
            for (int v = 0; v < n; v++)
            {
                if (done[v] || !Touch(s_Corridors[u], s_Corridors[v])) continue;
                float c = cost[u] + Vector2.Distance(s_Corridors[u].center, s_Corridors[v].center);
                if (c < cost[v])
                {
                    cost[v] = c;
                    previous[v] = u;
                }
            }
        }
        if (start != goal && previous[goal] < 0) return null;

        var chain = new List<int>();
        for (int i = goal; i >= 0; i = previous[i]) chain.Insert(0, i);
        float y = from.y;
        var points = new List<Vector3> { from };
        for (int i = 0; i < chain.Count; i++)
        {
            var r = s_Corridors[chain[i]];
            if (i > 0)
            {
                var shared = Overlap(s_Corridors[chain[i - 1]], r);
                points.Add(new Vector3(shared.center.x, y, shared.center.y));
            }
            if (i > 0 && i < chain.Count - 1) points.Add(new Vector3(r.center.x, y, r.center.y));
        }
        points.Add(to);
        return points.ToArray();
    }

    static bool Touch(Rect a, Rect b)
    {
        var o = Overlap(a, b);
        return o.width > -.3f && o.height > -.3f && (o.width > 1f || o.height > 1f);
    }

    static Rect Overlap(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));

    // The point a few metres out of a door on its corridor side, and on its room side.
    static Vector3 CorridorSide(SecurityDoor door) => DoorSides(door).corridor;
    static Vector3 RoomSide(SecurityDoor door) => DoorSides(door).room;

    static (Vector3 corridor, Vector3 room) DoorSides(SecurityDoor door)
    {
        var t = door.transform;
        foreach (var axis in new[] { t.right, t.forward })
        {
            var a = t.position + axis * 2.5f;
            var b = t.position - axis * 2.5f;
            bool aIn = InCorridor(a), bIn = InCorridor(b);
            if (aIn == bIn) continue;
            var corridor = aIn ? a : b;
            var room = aIn ? b : a;
            if (NavMesh.SamplePosition(corridor, out var c, 1.5f, NavMesh.AllAreas)) corridor = c.position;
            if (NavMesh.SamplePosition(room, out var r, 1.5f, NavMesh.AllAreas)) room = r.position;
            return (corridor, room);
        }
        return (t.position, t.position);
    }

    // Out of a room by whichever exit is best, along the corridors to the nearest goal door.
    static Vector3[] OutOfRoom(Vector3 from, SecurityDoor[] exits, SecurityDoor[] goals)
    {
        Vector3[] best = null;
        float bestLength = float.MaxValue;
        foreach (var exit in exits)
        {
            if (!exit) continue;
            var room = ShortestPath(from, new[] { RoomSide(exit) });
            if (room == null) continue;
            foreach (var goal in goals)
            {
                if (!goal) continue;
                var corridor = CorridorRoute(CorridorSide(exit), CorridorSide(goal));
                if (corridor == null) continue;
                var joined = room.Concat(new[] { new Vector3(exit.transform.position.x, room[room.Length - 1].y, exit.transform.position.z) })
                    .Concat(corridor).ToArray();
                float length = 0f;
                for (int i = 1; i < joined.Length; i++) length += Vector3.Distance(joined[i - 1], joined[i]);
                if (length < bestLength)
                {
                    bestLength = length;
                    best = joined;
                }
            }
        }
        return best;
    }

    // Flat floor surfaces (tiles, gratings): arrows sit on whichever is visible, as some tiles have no collider.
    static List<Bounds> FloorRenderers()
    {
        var result = new List<Bounds>();
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            var b = renderer.bounds;
            if (b.size.y < .45f && b.size.x > .8f && b.size.z > .8f && b.max.y < 8f) result.Add(b);
        }
        return result;
    }

    // A route from inside a room, out through whichever of its doors makes it shortest, on to the nearest target.
    static Vector3[] Through(Vector3 from, SecurityDoor[] exits, Vector3[] targets)
    {
        Vector3[] best = null;
        float bestLength = float.MaxValue;
        foreach (var door in exits)
        {
            if (!door) continue;
            var t = door.transform;
            foreach (var axis in new[] { t.right, t.forward })
            {
                if (!NavMesh.SamplePosition(t.position + axis * 2.5f, out var a, 1.5f, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(t.position - axis * 2.5f, out var b, 1.5f, NavMesh.AllAreas)) continue;
                foreach (var (inside, outside) in new[] { (a.position, b.position), (b.position, a.position) })
                {
                    var room = ShortestPath(from, new[] { inside });
                    var onward = room != null ? ShortestPath(outside, targets) : null;
                    if (onward == null) continue;
                    var doorway = new Vector3(t.position.x, (inside.y + outside.y) * .5f, t.position.z);
                    var joined = room.Concat(new[] { doorway }).Concat(onward).ToArray();
                    float length = 0f;
                    for (int i = 1; i < joined.Length; i++) length += Vector3.Distance(joined[i - 1], joined[i]);
                    if (length < bestLength)
                    {
                        bestLength = length;
                        best = joined;
                    }
                }
            }
        }
        return best;
    }

    static float OpenArea(Vector3 p) =>
        NavMesh.FindClosestEdge(p, out var edge, NavMesh.AllAreas) ? edge.distance : 0f;

    static Vector3[] ShortestPath(Vector3 from, Vector3[] targets)
    {
        if (!NavMesh.SamplePosition(from, out var start, 3f, NavMesh.AllAreas)) return null;
        Vector3[] best = null;
        float bestLength = float.MaxValue;
        var path = new NavMeshPath();
        foreach (var target in targets)
        {
            if (!NavMesh.SamplePosition(target, out var end, 3f, NavMesh.AllAreas)) continue;
            if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete) continue;
            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            if (length < bestLength)
            {
                bestLength = length;
                best = path.corners;
            }
        }
        return best;
    }

    // Resample, push off the walls toward the middle of the floor, smooth, and drop onto the floor.
    static List<Vector3> Refine(Vector3[] corners)
    {
        var samples = new List<Vector3>();
        for (int i = 1; i < corners.Length; i++)
        {
            float length = Vector3.Distance(corners[i - 1], corners[i]);
            for (float d = 0f; d < length; d += .25f) samples.Add(Vector3.Lerp(corners[i - 1], corners[i], d / length));
        }
        samples.Add(corners[corners.Length - 1]);

        for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < samples.Count; i++)
                if (NavMesh.FindClosestEdge(samples[i], out var edge, NavMesh.AllAreas) && edge.distance < WallClearance)
                {
                    var away = samples[i] - edge.position;
                    away.y = 0f;
                    if (away.sqrMagnitude < 1e-6f) away = edge.normal;
                    var pushed = samples[i] + away.normalized * (WallClearance - edge.distance);
                    if (NavMesh.SamplePosition(pushed, out var hit, .5f, NavMesh.AllAreas)) samples[i] = hit.position;
                }

        var smooth = new List<Vector3>(samples);
        for (int pass = 0; pass < 4; pass++)
        {
            var previous = new List<Vector3>(smooth);
            for (int i = 2; i < previous.Count - 2; i++)
                smooth[i] = (previous[i - 2] + previous[i - 1] + previous[i] + previous[i + 1] + previous[i + 2]) / 5f;
        }
        for (int i = 0; i < smooth.Count; i++) smooth[i] = OnFloor(smooth[i]);
        return smooth;
    }

    static Vector3 OnFloor(Vector3 p)
    {
        float best = float.NegativeInfinity;
        foreach (var hit in Physics.RaycastAll(p + Vector3.up * 1.2f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.collider.GetComponentInParent<Health>() && !hit.collider.GetComponentInParent<SecurityDoor>())
                best = Mathf.Max(best, hit.point.y);
        if (float.IsNegativeInfinity(best)) best = p.y;
        // The visible floor may sit above the collision (uncollided tiles, grating over a sub-floor).
        if (s_Floors != null)
            foreach (var b in s_Floors)
                if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z &&
                    b.max.y > best && b.max.y < best + .45f)
                    best = b.max.y;
        return new Vector3(p.x, best + .03f, p.z);
    }

    // ------------------------------------------------------------------ mesh

    // ">" chevrons every Spacing metres along the path, pointing toward its end. UV.x is the distance along the route
    // (for the shader's travelling pulses), UV.y runs across each stroke.
    static Mesh Chevrons(List<Vector3> points, out int count, out float length)
    {
        var distances = new float[points.Count];
        for (int i = 1; i < points.Count; i++) distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        length = distances[distances.Length - 1];

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        count = 0;
        int index = 0;
        for (float d = 1.5f; d < length - 1f; d += Spacing)
        {
            while (index < points.Count - 2 && distances[index + 1] < d) index++;
            float k = Mathf.InverseLerp(distances[index], distances[index + 1], d);
            var p = Vector3.Lerp(points[index], points[index + 1], k);
            var ahead = PointAt(points, distances, d + .6f) - PointAt(points, distances, d - .6f);
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 1e-4f) continue;
            var forward = ahead.normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var tip = p + forward * (ChevronLength * .5f);
            foreach (float side in new[] { -1f, 1f })
            {
                var back = p - forward * (ChevronLength * .5f) + right * (side * ChevronWidth * .5f);
                Stroke(vertices, uvs, triangles, back, tip, p, forward, d);
            }
            count++;
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector3 PointAt(List<Vector3> points, float[] distances, float d)
    {
        d = Mathf.Clamp(d, 0f, distances[distances.Length - 1]);
        int i = 0;
        while (i < points.Count - 2 && distances[i + 1] < d) i++;
        return Vector3.Lerp(points[i], points[i + 1], Mathf.InverseLerp(distances[i], distances[i + 1], d));
    }

    static void Stroke(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, Vector3 from, Vector3 to,
        Vector3 centre, Vector3 forward, float distance)
    {
        var direction = (to - from).normalized;
        var offset = Vector3.Cross(Vector3.up, direction) * (StrokeWidth * .5f);
        // Run the stroke a little past the tip so the two meet in a point.
        to += direction * (StrokeWidth * .5f);
        Vector3[] corners = { from - offset, from + offset, to + offset, to - offset };
        int start = vertices.Count;
        foreach (var corner in corners)
        {
            vertices.Add(corner);
            float along = distance + Vector3.Dot(corner - centre, forward);
            float across = Vector3.Dot(corner - from, Vector3.Cross(Vector3.up, direction)) / StrokeWidth + .5f;
            uvs.Add(new Vector2(along, across));
        }
        // Face up: (a, b, c) faces where cross(b - a, c - a) points.
        bool up = Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), Vector3.up) > 0f;
        triangles.AddRange(up ? new[] { start, start + 1, start + 2, start, start + 2, start + 3 }
                              : new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
    }

    static Mesh Save(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!existing)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        Overwrite(existing, mesh);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    // Rewrites an existing mesh asset's data in place (keeping its GUID). CopySerialized leaves the editor drawing the
    // old geometry; setting the buffers directly re-uploads them.
    static void Overwrite(Mesh target, Mesh source)
    {
        target.Clear();
        target.indexFormat = source.indexFormat;
        target.SetVertices(source.vertices);
        target.SetNormals(source.normals);
        if (source.tangents.Length == source.vertexCount) target.SetTangents(source.tangents);
        if (source.colors.Length == source.vertexCount) target.SetColors(source.colors);
        var uvs = new System.Collections.Generic.List<Vector2>();
        source.GetUVs(0, uvs);
        if (uvs.Count == source.vertexCount) target.SetUVs(0, uvs);
        target.subMeshCount = source.subMeshCount;
        for (int i = 0; i < source.subMeshCount; i++) target.SetTriangles(source.GetTriangles(i), i);
        target.RecalculateBounds();
    }

    static Material Material(string name, Color color)
    {
        string path = $"{ArtRoot}/M_FloorGuide_{name.Replace(" ", "")}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("CoreBuster/Floor Guide");
        if (!material)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_Color", color * 2f);
        EditorUtility.SetDirty(material);
        return material;
    }
}
