using UnityEngine;
using UnityEngine.AI;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// A sliding sci-fi gate across a room's doorway. Unlocked it stands open; locked it slides shut, blocks the
    /// doorway (player and navmesh) and its status lamps turn red. Room terminals lock their doors for the fight,
    /// access machines and the Master terminal unlock them. With <see cref="KeepEnemiesInRoom"/> the doorway stays
    /// carved out of the NavMesh while open, so each room is its own NavMesh island and enemies hold their room.
    /// </summary>
    public class SecurityDoor : MonoBehaviour
    {
        [Tooltip("Starts locked (shut) rather than open.")]
        public bool StartLocked = true;

        [Header("Leaves")]
        public Transform LeafA;
        public Transform LeafB;
        [Tooltip("How far each leaf slides, in its parent's space, from shut to open.")]
        public Vector3 SlideA = new Vector3(0f, 0f, 1.8f);
        public Vector3 SlideB = new Vector3(0f, 0f, -1.8f);
        [Min(.05f)] public float SlideDuration = .7f;

        [Header("Blocking")]
        [Tooltip("Spans the whole doorway; solid only while shut.")]
        public Collider Blocker;
        public NavMeshObstacle Obstacle;
        [Tooltip("Keep the doorway carved out of the NavMesh even while the door is open. The player walks " +
                 "through; enemies stay in their own rooms instead of following the player through every door.")]
        public bool KeepEnemiesInRoom = true;

        [Header("Status")]
        public Renderer[] Lamps;
        public Light LampLight;
        public Color LockedColor = new Color(1f, .12f, .08f);
        public Color OpenColor = new Color(.15f, 1f, .35f);
        [Tooltip("Colour while shut for a fight; the lamps pulse.")]
        public Color AlarmColor = new Color(1f, .35f, .05f);
        public float LampIntensity = 4f;

        [Header("Sound")]
        public AudioClip OpenSfx;
        public AudioClip CloseSfx;
        [Range(0f, 1f)] public float SfxVolume = 1f;

        public bool IsLocked { get; private set; }
        public bool IsAlarm { get; private set; }
        /// <summary>Has been open at some point; an access machine only opens a door that never has been.</summary>
        public bool HasBeenOpened { get; private set; }

        Vector3 m_ShutA, m_ShutB;
        float m_Openness;
        AudioSource m_Audio;

        void Awake()
        {
            if (LeafA) m_ShutA = LeafA.localPosition;
            if (LeafB) m_ShutB = LeafB.localPosition;
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 1f;
            m_Audio.minDistance = 3f;
            m_Audio.maxDistance = 35f;
            m_Audio.rolloffMode = AudioRolloffMode.Linear;

            IsLocked = StartLocked;
            HasBeenOpened = !IsLocked;
            m_Openness = IsLocked ? 0f : 1f;
            Apply();
        }

        /// <summary>Slides shut and locks. Alarm pulses the lamps (a room terminal's fight).</summary>
        public void Lock(bool alarm = false)
        {
            IsAlarm = alarm;
            if (IsLocked) return;
            IsLocked = true;
            Play(CloseSfx);
        }

        /// <summary>Unlocks and slides open.</summary>
        public void Unlock()
        {
            IsAlarm = false;
            HasBeenOpened = true;
            if (!IsLocked) return;
            IsLocked = false;
            Play(OpenSfx);
        }

        void Update()
        {
            m_Openness = Mathf.MoveTowards(m_Openness, IsLocked ? 0f : 1f, Time.deltaTime / SlideDuration);
            Apply();
        }

        void Apply()
        {
            float k = m_Openness * m_Openness * (3f - 2f * m_Openness);
            if (LeafA) LeafA.localPosition = m_ShutA + SlideA * k;
            if (LeafB) LeafB.localPosition = m_ShutB + SlideB * k;

            // Solid as soon as it starts shutting, so nothing slips through the closing gap.
            bool solid = IsLocked || m_Openness < .5f;
            if (Blocker) Blocker.enabled = solid;
            if (Obstacle) Obstacle.enabled = solid || KeepEnemiesInRoom;

            Color color = !IsLocked ? OpenColor : IsAlarm ? AlarmColor : LockedColor;
            float pulse = IsAlarm ? StatusLamps.Pulse(5f) : 1f;
            StatusLamps.Set(Lamps, LampLight, color, LampIntensity * pulse);
        }

        void Play(AudioClip clip)
        {
            if (clip && m_Audio) m_Audio.PlayOneShot(clip, SfxVolume);
        }
    }
}
