using System;
using System.Collections;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Renders the ship room by room. The level is split into zones: the rooms (each an XZ area) and the
    /// corridors (everything else). Only the zone the camera is in is drawn, plus any zone behind an open door the
    /// camera can see. Hidden zones' renderers skip rendering (<see cref="Renderer.forceRenderingOff"/>, so their
    /// colliders, scripts and AI carry on) and their plain lights switch off, which also drops their shadow maps.
    /// <list type="bullet">
    /// <item>Geometry within <see cref="DoorFrameRadius"/> of a door is always drawn, so a door and its frame never
    /// hang in a void when seen from the far side.</item>
    /// <item>A keycard reader holds its door shut for a moment ("processing credentials") while the zone behind it
    /// is drawn early behind the closed door, warming its shaders and textures before it is revealed
    /// (<see cref="OpenWhenReady"/>).</item>
    /// <item>While the loading screen is up, every zone is drawn once from its centre, so first sight of a room does
    /// not stall on shader compilation.</item>
    /// </list>
    /// Things that move (characters, weapons, pickups, doors, terminals) and anything spawned later are never hidden.
    /// </summary>
    public class ZoneStreamer : MonoBehaviour
    {
        [Serializable]
        public class Zone
        {
            public string Name;
            [Tooltip("The room's floor area in world X/Z (min and max).")]
            public Rect Area;

            [NonSerialized] public readonly List<Renderer> Renderers = new List<Renderer>();
            [NonSerialized] public readonly List<Light> Lights = new List<Light>();
            [NonSerialized] public readonly List<SecurityDoor> Doors = new List<SecurityDoor>();
            [NonSerialized] public bool Visible = true;
            [NonSerialized] public float VisibleUntil;
            [NonSerialized] public float PrewarmUntil;
        }

        [Tooltip("Off draws every zone, as before streaming (for comparisons).")]
        public bool Streaming = true;

        [Tooltip("The rooms; anything outside them belongs to the corridors.")]
        public Zone[] Rooms =
        {
            new Zone { Name = "Tutorial Wing", Area = Rect.MinMaxRect(-262f, -43f, -231.3f, .5f) },
            new Zone { Name = "Workshop", Area = Rect.MinMaxRect(-187f, 1f, -154f, 34.7f) },
            new Zone { Name = "Centre Room", Area = Rect.MinMaxRect(-215f, 50.6f, -182f, 83.4f) },
            new Zone { Name = "North-West Room", Area = Rect.MinMaxRect(-252f, 99f, -218.3f, 132f) },
            new Zone { Name = "Boss Hall", Area = Rect.MinMaxRect(-336f, 80f, -251.5f, 152f) },
        };

        [Tooltip("The corridor kit's root. Its pieces always belong to the corridors, even where a corridor wall " +
                 "runs along a room and falls inside the room's area; otherwise it vanished with the room.")]
        public string CorridorRootName = "Plane (1)";
        [Tooltip("How far outside a room's area its walls still count as the room's.")]
        [Min(0f)] public float WallMargin = .6f;
        [Tooltip("Geometry this close to a door is always drawn.")]
        [Min(0f)] public float DoorFrameRadius = 4f;
        [Tooltip("Open doors farther than this are not checked for line of sight.")]
        [Min(1f)] public float MaxDoorSightDistance = 70f;
        [Tooltip("A zone seen through a door stays drawn this long after the door leaves sight.")]
        [Min(0f)] public float LingerSeconds = 1.25f;
        [Min(.02f)] public float UpdateInterval = .1f;
        [Tooltip("Corridor lights farther than this from the camera are switched off (0 = never). The corridors run the " +
                 "length of the ship with two lights per lamp, and URP draws at most 256 lights in view.")]
        [Min(0f)] public float CorridorLightReach = 40f;
        [Tooltip("Shortest time a keycard reader spends processing while the zone behind it is drawn early.")]
        [Min(0f)] public float MinProcessingSeconds = 1.4f;

        public static ZoneStreamer Instance { get; private set; }

        readonly List<Zone> m_Zones = new List<Zone>();
        readonly List<SecurityDoor> m_Doors = new List<SecurityDoor>();
        readonly Dictionary<SecurityDoor, Zone[]> m_DoorZones = new Dictionary<SecurityDoor, Zone[]>();
        readonly Dictionary<Light, bool> m_LightWasEnabled = new Dictionary<Light, bool>();
        // Room walls that also close off a corridor, each with its room: drawn while either side is.
        readonly List<(Renderer renderer, Zone room)> m_SharedWalls = new List<(Renderer, Zone)>();
        readonly RaycastHit[] m_Hits = new RaycastHit[16];
        Zone m_Corridors;
        Camera m_Camera;
        Transform m_Player;
        float m_NextUpdate;
        bool m_Ready;

        public bool IsReady => m_Ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            Instance = this;
            LoadingGate.Begin(this);
        }

        void OnDestroy()
        {
            LoadingGate.End(this);
            if (Instance == this) Instance = null;
        }

        IEnumerator Start()
        {
            var player = FindAnyObjectByType<PlayerCharacterController>();
            m_Player = player ? player.transform : null;
            m_Camera = player && player.PlayerCamera ? player.PlayerCamera : Camera.main;

            Collect();
            yield return Warmup();
            m_Ready = true;
            Refresh(true);
            // One frame drawn with the final visibility before the loading screen lifts.
            yield return null;
            LoadingGate.End(this);
        }

        void Update()
        {
            if (!m_Ready || Time.unscaledTime < m_NextUpdate) return;
            m_NextUpdate = Time.unscaledTime + UpdateInterval;
            Refresh(false);
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Draws the zones on both sides of these doors for a while even though the doors are shut, so they are ready
        /// (shaders compiled, textures uploaded) by the time the doors open.
        /// </summary>
        public void Prewarm(IEnumerable<SecurityDoor> doors, float seconds)
        {
            if (doors == null) return;
            foreach (var door in doors)
                if (door && m_DoorZones.TryGetValue(door, out var zones))
                    foreach (var zone in zones)
                        zone.PrewarmUntil = Mathf.Max(zone.PrewarmUntil, Time.unscaledTime + seconds);
            if (m_Ready) Refresh(true);
        }

        /// <summary>
        /// The keycard readers' "processing credentials" pause: prewarms the zones behind the doors, waits at least
        /// <see cref="MinProcessingSeconds"/> and a few rendered frames, then opens the doors. Works without a streamer
        /// in the scene too (just the pause).
        /// </summary>
        public static IEnumerator OpenWhenReady(SecurityDoor[] doors, float minSeconds = -1f)
        {
            var streamer = Instance;
            float wait = minSeconds >= 0f ? minSeconds : streamer ? streamer.MinProcessingSeconds : 1.2f;
            if (streamer) streamer.Prewarm(doors, wait + 3f);
            float start = Time.unscaledTime;
            int frames = 0;
            while (Time.unscaledTime - start < wait || frames < 4)
            {
                frames++;
                yield return null;
            }
            if (doors == null) yield break;
            foreach (var door in doors)
                if (door) door.Unlock();
        }

        // ------------------------------------------------------------------ setup

        void Collect()
        {
            m_Zones.Clear();
            foreach (var room in Rooms)
            {
                room.Renderers.Clear();
                room.Lights.Clear();
                room.Doors.Clear();
                m_Zones.Add(room);
            }
            m_Corridors = new Zone { Name = "Corridors" };
            m_Zones.Add(m_Corridors);

            m_Doors.Clear();
            m_Doors.AddRange(FindObjectsByType<SecurityDoor>(FindObjectsInactive.Include));
            foreach (var door in m_Doors)
            {
                var zones = ZonesAround(door.transform);
                m_DoorZones[door] = zones;
                foreach (var zone in zones) zone.Doors.Add(door);
            }

            int hidden = 0;
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                if (IsDynamic(renderer.transform)) continue;
                var bounds = renderer.bounds;
                if (NearDoor(bounds)) continue;
                (InCorridorKit(renderer.transform) ? m_Corridors : ZoneAt(bounds.center, WallMargin)).Renderers.Add(renderer);
                hidden++;
            }
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light.type == LightType.Directional || IsDynamic(light.transform) || IsScripted(light)) continue;
                var position = light.transform.position;
                if (NearDoor(new Bounds(position, Vector3.zero))) continue;
                (InCorridorKit(light.transform) ? m_Corridors : ZoneAt(position, 0f)).Lights.Add(light);
            }
            CollectSharedWalls();
            var summary = new System.Text.StringBuilder($"ZoneStreamer: {hidden} renderers in {m_Zones.Count} zones, " +
                                                        $"{m_SharedWalls.Count} room walls shared with the corridors");
            foreach (var zone in m_Zones)
                summary.Append($"; {zone.Name} {zone.Renderers.Count}r/{zone.Lights.Count}l/{zone.Doors.Count}d");
            Debug.Log(summary.ToString());
        }

        // Each zone drawn once from its centre, under the loading screen, so its shaders and textures are ready.
        IEnumerator Warmup()
        {
            foreach (var zone in m_Zones) SetVisible(zone, true);
            if (!m_Camera) yield break;
            var go = new GameObject("Zone Warmup Camera");
            var camera = go.AddComponent<Camera>();
            camera.CopyFrom(m_Camera);
            camera.enabled = false;
            camera.fieldOfView = 100f;
            var target = RenderTexture.GetTemporary(320, 180, 24);
            camera.targetTexture = target;
            foreach (var zone in m_Zones)
            {
                if (zone.Renderers.Count == 0) continue;
                // One zone at a time, lit as it will be in play.
                foreach (var other in m_Zones) SetVisible(other, other == zone);
                var bounds = zone.Renderers[0].bounds;
                foreach (var r in zone.Renderers) bounds.Encapsulate(r.bounds.center);
                var centre = new Vector3(bounds.center.x, Mathf.Max(bounds.min.y + 1.7f, bounds.center.y), bounds.center.z);
                CullCorridorLights(centre);
                for (int i = 0; i < 4; i++)
                {
                    go.transform.SetPositionAndRotation(centre, Quaternion.Euler(10f, i * 90f, 0f));
                    try { camera.Render(); }
                    catch (Exception e) { Debug.LogWarning("ZoneStreamer warm-up render failed: " + e.Message); }
                }
                yield return null;
            }
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            Destroy(go);
        }

        Zone[] ZonesAround(Transform door)
        {
            var found = new List<Zone>();
            foreach (var dir in new[] { door.right, -door.right, door.forward, -door.forward })
            {
                var zone = ZoneAt(door.position + dir * 2.5f, 0f);
                if (!found.Contains(zone)) found.Add(zone);
            }
            return found.ToArray();
        }

        Zone ZoneAt(Vector3 position, float margin)
        {
            var p = new Vector2(position.x, position.z);
            foreach (var room in Rooms)
            {
                var area = room.Area;
                if (p.x >= area.xMin - margin && p.x <= area.xMax + margin &&
                    p.y >= area.yMin - margin && p.y <= area.yMax + margin) return room;
            }
            return m_Corridors;
        }

        bool NearDoor(Bounds bounds)
        {
            float r2 = DoorFrameRadius * DoorFrameRadius;
            foreach (var door in m_Doors)
                if (door && bounds.SqrDistance(door.transform.position + Vector3.up * 1.5f) < r2) return true;
            return false;
        }

        bool InCorridorKit(Transform t) => !string.IsNullOrEmpty(CorridorRootName) && t.root.name == CorridorRootName;

        // Anything that moves, is used or is UI stays drawn.
        static bool IsDynamic(Transform t)
        {
            for (; t; t = t.parent)
            {
                if (t.TryGetComponent<Health>(out _) || t.TryGetComponent<NavMeshAgent>(out _) ||
                    t.TryGetComponent<PlayerCharacterController>(out _) || t.TryGetComponent<WeaponController>(out _) ||
                    t.TryGetComponent<Pickup>(out _) || t.TryGetComponent<SecurityDoor>(out _) ||
                    t.TryGetComponent<KeycardInteractable>(out _) || t.TryGetComponent<Canvas>(out _) ||
                    t.TryGetComponent<Camera>(out _) || t.TryGetComponent<ProjectileBase>(out _) ||
                    // Guide routes cross zones; they are cheap and always drawn.
                    t.TryGetComponent<FloorGuide>(out _))
                    return true;
                if (!t.parent && t.name == "===== KEYCARD PROGRESSION =====") return true;
            }
            return false;
        }

        // Lights other scripts switch on and off are left to them.
        static bool IsScripted(Light light)
        {
            foreach (var behaviour in light.GetComponents<MonoBehaviour>())
                if (behaviour && !(behaviour.GetType().Namespace ?? "").StartsWith("UnityEngine", StringComparison.Ordinal))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ visibility

        void Refresh(bool force)
        {
            if (!m_Camera) m_Camera = Camera.main;
            if (!m_Camera) return;
            Vector3 eye = m_Camera.transform.position;
            Zone current = ZoneAt(eye, 0f);
            float now = Time.unscaledTime;

            current.VisibleUntil = now + LingerSeconds;
            foreach (var door in current.Doors)
            {
                if (!door || door.IsLocked || !CanSee(eye, door)) continue;
                foreach (var zone in m_DoorZones[door])
                    zone.VisibleUntil = now + LingerSeconds;
            }

            foreach (var zone in m_Zones)
            {
                bool visible = !Streaming || zone == current || zone.VisibleUntil > now || zone.PrewarmUntil > now;
                if (force || visible != zone.Visible) SetVisible(zone, visible);
            }

            if (Streaming) CullCorridorLights(eye);
            foreach (var (renderer, room) in m_SharedWalls)
                if (renderer) renderer.forceRenderingOff = !(room.Visible || m_Corridors.Visible);
        }

        // Some corridors dead-end on a room's outer wall rather than on a wall of their own. That wall is the room's
        // (it is drawn with the room), but it is also the corridor's end: hidden with a locked room, the corridor would
        // open onto empty ground. A room renderer within the wall margin of its room's edge, with corridor floor just
        // outside that stretch of edge, is shared.
        void CollectSharedWalls()
        {
            m_SharedWalls.Clear();
            var floors = new List<Bounds>();
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!InCorridorKit(renderer.transform)) continue;
                var b = renderer.bounds;
                // Floor tiles: flat, a few metres across, at floor level (not the map-wide ground plane).
                if (b.size.y < .5f && b.center.y < 1f && b.size.x > 2.5f && b.size.z > 2.5f && b.size.x < 10f && b.size.z < 10f)
                    floors.Add(b);
            }
            bool OnFloor(Vector3 p)
            {
                foreach (var f in floors)
                    if (p.x >= f.min.x && p.x <= f.max.x && p.z >= f.min.z && p.z <= f.max.z) return true;
                return false;
            }

            const float margin = 1f, probeOut = 1.5f;
            foreach (var room in Rooms)
            {
                var a = room.Area;
                foreach (var renderer in room.Renderers)
                {
                    if (!renderer) continue;
                    var b = renderer.bounds;
                    bool shared = false;
                    // West, east, south and north edges: (the edge's line, outward normal, extent along the edge).
                    if (Mathf.Abs(b.center.x - a.xMin) < margin) shared = Probe(new Vector3(a.xMin - probeOut, 0f, 0f), Vector3.forward, b.min.z, b.max.z);
                    if (!shared && Mathf.Abs(b.center.x - a.xMax) < margin) shared = Probe(new Vector3(a.xMax + probeOut, 0f, 0f), Vector3.forward, b.min.z, b.max.z);
                    if (!shared && Mathf.Abs(b.center.z - a.yMin) < margin) shared = Probe(new Vector3(0f, 0f, a.yMin - probeOut), Vector3.right, b.min.x, b.max.x);
                    if (!shared && Mathf.Abs(b.center.z - a.yMax) < margin) shared = Probe(new Vector3(0f, 0f, a.yMax + probeOut), Vector3.right, b.min.x, b.max.x);
                    if (shared) m_SharedWalls.Add((renderer, room));
                }
            }

            // Samples the line just outside an edge, every half metre along the renderer's extent.
            bool Probe(Vector3 line, Vector3 along, float from, float to)
            {
                for (float t = from; t <= to + .01f; t += .5f)
                    if (OnFloor(line + along * t)) return true;
                return false;
            }
        }

        // Far down a corridor a lamp's pool is not seen; its light only takes one of URP's 256 slots.
        void CullCorridorLights(Vector3 eye)
        {
            if (CorridorLightReach <= 0f || !m_Corridors.Visible) return;
            float reach2 = CorridorLightReach * CorridorLightReach;
            foreach (var light in m_Corridors.Lights)
                if (light) SetLight(light, (light.transform.position - eye).sqrMagnitude <= reach2);
        }

        bool CanSee(Vector3 eye, SecurityDoor door)
        {
            Vector3 centre = door.transform.position + Vector3.up * 1.5f;
            if ((centre - eye).sqrMagnitude > MaxDoorSightDistance * MaxDoorSightDistance) return false;
            // The doorway's middle and both sides: any clear line counts.
            Vector3 across = door.transform.forward * 1.1f;
            return Clear(eye, centre) || Clear(eye, centre + across) || Clear(eye, centre - across);
        }

        bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < .01f) return true;
            int count = Physics.RaycastNonAlloc(from, delta / distance, m_Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var collider = m_Hits[i].collider;
                // Characters and props that move do not block sight; the level's walls do.
                if (collider.attachedRigidbody || collider is CharacterController || IsDynamic(collider.transform)) continue;
                return false;
            }
            return true;
        }

        void SetVisible(Zone zone, bool visible)
        {
            zone.Visible = visible;
            foreach (var renderer in zone.Renderers)
                if (renderer) renderer.forceRenderingOff = !visible;
            foreach (var light in zone.Lights)
                if (light) SetLight(light, visible);
        }

        // Switches a light off, remembering whether it was on, or back to how it was.
        void SetLight(Light light, bool on)
        {
            if (!on)
            {
                if (!m_LightWasEnabled.ContainsKey(light)) m_LightWasEnabled[light] = light.enabled;
                light.enabled = false;
            }
            else if (m_LightWasEnabled.TryGetValue(light, out bool was))
            {
                light.enabled = was;
                m_LightWasEnabled.Remove(light);
            }
        }
    }
}
