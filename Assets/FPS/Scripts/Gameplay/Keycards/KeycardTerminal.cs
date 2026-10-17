using System.Collections;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The security terminal in a major room that holds its keycard. Accessing it triggers a lockdown: the room's
    /// doors slam shut and waves of enemies warp in. Once every wave is dead the doors open and the terminal
    /// ejects its floppy disk for the player to take (and look over).
    /// </summary>
    public class KeycardTerminal : KeycardInteractable
    {
        [System.Serializable]
        public class Wave
        {
            public GameObject[] Enemies;
        }

        public enum State { Idle, Lockdown, Ejected, Taken }

        [Header("Keycard")]
        public KeycardData Reward;
        [Tooltip("The drive slot the disk pops out of; its forward points out of the terminal.")]
        public Transform EjectSlot;
        [Tooltip("How far the disk's centre ends up out of the slot.")]
        public float EjectDistance = .05f;
        [Tooltip("The disk's rotation in the slot's space.")]
        public Vector3 EjectEuler = new Vector3(0f, -90f, 0f);
        public float EjectDuration = .45f;

        [Header("Lockdown")]
        public SecurityDoor[] Doors;
        public Transform[] SpawnPoints;
        public Wave[] Waves;
        public float FirstWaveDelay = 1.5f;
        public float WaveDelay = 2.5f;
        [Tooltip("Time between the warp-in effect appearing and the enemy stepping out of it.")]
        public float WarpLead = .45f;
        public float SpawnInterval = .3f;
        [Tooltip("Spawn points nearer the player than this are only used when no others are free.")]
        public float MinSpawnDistance = 7f;
        public GameObject WarpVfx;

        [Header("Status")]
        public Renderer[] Lamps;
        public Light LampLight;
        public Color AlarmColor = new Color(1f, .12f, .08f);
        public Color SecuredColor = new Color(.15f, 1f, .35f);

        [Header("Sound")]
        public AudioClip ActivateSfx;
        public AudioClip AlarmLoop;
        public AudioClip WarpSfx;
        public AudioClip ClearedSfx;
        public AudioClip EjectSfx;

        public State Current { get; private set; }
        public int EnemiesRemaining => m_Alive.Count;
        public int WaveIndex { get; private set; }

        public UnityAction<KeycardTerminal> OnLockdown;
        public UnityAction<KeycardTerminal> OnSecured;

        readonly List<Health> m_Alive = new List<Health>();
        GameObject m_Disk;
        Light m_DiskLight;
        AudioSource m_Alarm;

        public override bool IsAvailable => Current == State.Idle || Current == State.Ejected;

        public override string PromptAction(KeycardInteractor user) =>
            Current == State.Ejected ? "TAKE " + Reward.RichName : "ACCESS TERMINAL";

        public override string PromptDetail(KeycardInteractor user) =>
            Current == State.Ejected ? "SECURITY KEYCARD" : "WARNING: TRIGGERS A SECURITY LOCKDOWN";

        void Awake()
        {
            m_Alarm = gameObject.AddComponent<AudioSource>();
            m_Alarm.playOnAwake = false;
            m_Alarm.loop = true;
            m_Alarm.spatialBlend = .6f;
            m_Alarm.minDistance = 8f;
            m_Alarm.maxDistance = 45f;
            m_Alarm.rolloffMode = AudioRolloffMode.Linear;
            if (!FocusPoint) FocusPoint = EjectSlot;
        }

        public override void Interact(KeycardInteractor user)
        {
            if (Current == State.Idle) StartCoroutine(Lockdown());
            else if (Current == State.Ejected) Take(user);
        }

        IEnumerator Lockdown()
        {
            Current = State.Lockdown;
            foreach (var door in Doors)
                if (door) door.Lock(true);
            PlayAt(ActivateSfx, FocusPosition);
            if (AlarmLoop)
            {
                m_Alarm.clip = AlarmLoop;
                m_Alarm.volume = .55f * SfxVolume;
                m_Alarm.Play();
            }
            ShowMessage("SECURITY LOCKDOWN - ELIMINATE ALL HOSTILES");
            OnLockdown?.Invoke(this);

            yield return new WaitForSeconds(FirstWaveDelay);
            for (WaveIndex = 0; WaveIndex < Waves.Length; WaveIndex++)
            {
                if (WaveIndex > 0)
                {
                    ShowMessage($"WAVE {WaveIndex + 1} OF {Waves.Length} INCOMING");
                    yield return new WaitForSeconds(WaveDelay);
                }
                yield return SpawnWave(Waves[WaveIndex]);
                while (CountAlive() > 0) yield return null;
            }

            m_Alarm.Stop();
            foreach (var door in Doors)
                if (door) door.Unlock();
            PlayAt(ClearedSfx, FocusPosition);
            ShowMessage("ROOM SECURED");
            OnSecured?.Invoke(this);
            yield return new WaitForSeconds(.6f);
            yield return Eject();
        }

        IEnumerator SpawnWave(Wave wave)
        {
            if (wave == null || wave.Enemies == null) yield break;
            var points = PickSpawnPoints(wave.Enemies.Length);
            for (int i = 0; i < wave.Enemies.Length; i++)
            {
                if (!wave.Enemies[i]) continue;
                StartCoroutine(WarpIn(wave.Enemies[i], points[i]));
                yield return new WaitForSeconds(SpawnInterval);
            }
            // Let the last one land before counting the living.
            yield return new WaitForSeconds(WarpLead + .1f);
        }

        IEnumerator WarpIn(GameObject prefab, Vector3 position)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas)) position = hit.position;
            // Reserve its place in the count at once, so the wave is never briefly "cleared" mid-warp.
            m_Alive.Add(null);
            if (WarpVfx) Destroy(Instantiate(WarpVfx, position, Quaternion.identity), 4f);
            PlayAt(WarpSfx, position, .8f);
            yield return new WaitForSeconds(WarpLead);
            m_Alive.Remove(null);

            Vector3 toPlayer = FocusPosition - position;
            var player = FindAnyObjectByType<PlayerCharacterController>();
            if (player) toPlayer = player.transform.position - position;
            toPlayer.y = 0f;
            var rotation = toPlayer.sqrMagnitude > .01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
            var enemy = Instantiate(prefab, position, rotation);
            var health = enemy.GetComponentInChildren<Health>();
            if (!health) yield break;
            m_Alive.Add(health);
            health.OnDie += () => m_Alive.Remove(health);
        }

        int CountAlive()
        {
            // Enemies destroyed without dying (fell out of the world) stop counting too.
            m_Alive.RemoveAll(h => !ReferenceEquals(h, null) && (!h || h.CurrentHealth <= 0f));
            return m_Alive.Count;
        }

        // Spread the wave over the points furthest from the player, reusing points (nudged apart) if short.
        List<Vector3> PickSpawnPoints(int count)
        {
            var player = FindAnyObjectByType<PlayerCharacterController>();
            Vector3 from = player ? player.transform.position : FocusPosition;
            var candidates = new List<Transform>();
            foreach (var point in SpawnPoints)
                if (point) candidates.Add(point);
            var far = candidates.FindAll(p => Vector3.Distance(p.position, from) >= MinSpawnDistance);
            if (far.Count > 0) candidates = far;
            // Shuffle so waves don't always use the same corners.
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            var result = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                if (candidates.Count == 0)
                {
                    result.Add(FocusPosition + transform.forward * 4f);
                    continue;
                }
                Vector3 position = candidates[i % candidates.Count].position;
                if (i >= candidates.Count)
                {
                    Vector2 nudge = Random.insideUnitCircle.normalized * 1.5f;
                    position += new Vector3(nudge.x, 0f, nudge.y);
                }
                result.Add(position);
            }
            return result;
        }

        IEnumerator Eject()
        {
            Current = State.Ejected;
            Transform slot = EjectSlot ? EjectSlot : transform;
            if (Reward && Reward.Model) m_Disk = Instantiate(Reward.Model, slot, false);
            else
            {
                m_Disk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m_Disk.transform.SetParent(slot, false);
                m_Disk.transform.localScale = new Vector3(.09f, .003f, .09f);
            }
            m_Disk.name = "Ejected Disk";
            if (!m_Disk.GetComponentInChildren<Collider>())
            {
                var box = m_Disk.AddComponent<BoxCollider>();
                box.size = new Vector3(.16f, .02f, .15f);
            }

            var lightObject = new GameObject("Disk Glow");
            lightObject.transform.SetParent(slot, false);
            lightObject.transform.localPosition = new Vector3(0f, .12f, .1f);
            m_DiskLight = lightObject.AddComponent<Light>();
            m_DiskLight.type = LightType.Point;
            m_DiskLight.range = 1.6f;
            m_DiskLight.shadows = LightShadows.None;
            m_DiskLight.color = Reward ? Reward.Tint : Color.white;

            PlayAt(EjectSfx, slot.position);
            // Pops out of the slot like a disk from a drive, with a small overshoot.
            m_Disk.transform.localRotation = Quaternion.Euler(EjectEuler);
            Vector3 inside = new Vector3(0f, 0f, -.12f), outside = new Vector3(0f, 0f, EjectDistance);
            for (float t = 0f; t < EjectDuration; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / EjectDuration) - 1f;
                k = k * k * (2.4f * k + 1.4f) + 1f;
                m_Disk.transform.localPosition = Vector3.LerpUnclamped(inside, outside, k);
                yield return null;
            }
            m_Disk.transform.localPosition = outside;
            ShowMessage((Reward ? Reward.DisplayName : "KEYCARD") + " EJECTED");
        }

        void Take(KeycardInteractor user)
        {
            if (!user.Inspect(Reward, () =>
                {
                    user.Inventory.Add(Reward);
                    ShowMessage(Reward.DisplayName + " ACQUIRED");
                }))
                return;
            Current = State.Taken;
            if (m_Disk) Destroy(m_Disk);
            if (m_DiskLight) Destroy(m_DiskLight.gameObject);
        }

        void Update()
        {
            Color tint = Reward ? Reward.Tint : Color.white;
            switch (Current)
            {
                case State.Idle:
                    StatusLamps.Set(Lamps, LampLight, tint, 3f * StatusLamps.Pulse(1.6f));
                    break;
                case State.Lockdown:
                    StatusLamps.Set(Lamps, LampLight, AlarmColor, 5f * StatusLamps.Pulse(6f), 2f);
                    break;
                case State.Ejected:
                    float pulse = StatusLamps.Pulse(4f);
                    StatusLamps.Set(Lamps, LampLight, tint, 5f * pulse);
                    if (m_DiskLight) m_DiskLight.intensity = 1.5f * pulse;
                    break;
                default:
                    StatusLamps.Set(Lamps, LampLight, SecuredColor, 2.5f);
                    break;
            }
        }
    }
}
