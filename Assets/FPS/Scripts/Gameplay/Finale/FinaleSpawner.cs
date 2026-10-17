using System.Collections;
using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Keeps enemies warping in for a finale phase, and the longer the phase runs the harder it gets: over
    /// RampDuration the gap between spawns shrinks from StartInterval to EndInterval and the number of spawned
    /// enemies allowed alive at once grows from StartAlive to EndAlive.
    /// </summary>
    public class FinaleSpawner : MonoBehaviour
    {
        public Transform[] SpawnPoints;
        public GameObject[] Prefabs;
        public GameObject WarpVfx;
        public AudioClip WarpSfx;
        [Range(0f, 1f)] public float SfxVolume = .8f;

        [Header("Escalation")]
        [Min(.1f)] public float StartInterval = 6f;
        [Min(.1f)] public float EndInterval = 1.5f;
        [Min(0)] public int StartAlive = 3;
        [Min(0)] public int EndAlive = 12;
        [Tooltip("Seconds from the first spawn until the spawner reaches its end interval and cap.")]
        [Min(0f)] public float RampDuration = 240f;

        [Header("Placement")]
        [Tooltip("Time between the warp-in effect appearing and the enemy stepping out of it.")]
        [Min(0f)] public float WarpLead = .45f;
        [Tooltip("Spawn points nearer the player than this are only used when no others are free.")]
        [Min(0f)] public float MinSpawnDistance = 8f;

        public bool IsRunning { get; private set; }
        public int AliveCount => m_Alive.Count + m_Pending;

        /// <summary>0 at the start of the phase, 1 once it has fully escalated.</summary>
        public float Escalation => RampDuration <= 0f ? 1f : Mathf.Clamp01((Time.time - m_StartTime) / RampDuration);

        readonly List<Health> m_Alive = new List<Health>();
        float m_StartTime;
        float m_NextSpawn;
        int m_Pending;
        Transform m_Player;

        public void Begin()
        {
            if (IsRunning) return;
            IsRunning = true;
            m_StartTime = Time.time;
            m_NextSpawn = Time.time + 1f;
            var player = FindAnyObjectByType<PlayerCharacterController>();
            if (player) m_Player = player.transform;
        }

        public void Stop() => IsRunning = false;

        void Update()
        {
            if (!IsRunning || Prefabs == null || Prefabs.Length == 0) return;
            // Enemies destroyed without dying (fell out of the world) stop counting too.
            m_Alive.RemoveAll(h => !h || h.CurrentHealth <= 0f);
            if (Time.time < m_NextSpawn) return;

            int cap = Mathf.RoundToInt(Mathf.Lerp(StartAlive, EndAlive, Escalation));
            if (AliveCount >= cap)
            {
                m_NextSpawn = Time.time + .5f;
                return;
            }
            m_NextSpawn = Time.time + Mathf.Lerp(StartInterval, EndInterval, Escalation);
            GameObject prefab = Prefabs[Random.Range(0, Prefabs.Length)];
            if (prefab && TryPickPoint(out Vector3 position)) StartCoroutine(WarpIn(prefab, position));
        }

        bool TryPickPoint(out Vector3 position)
        {
            position = default;
            if (SpawnPoints == null || SpawnPoints.Length == 0) return false;
            var far = new List<Transform>();
            var any = new List<Transform>();
            foreach (var point in SpawnPoints)
            {
                if (!point) continue;
                any.Add(point);
                if (!m_Player || Vector3.Distance(point.position, m_Player.position) >= MinSpawnDistance) far.Add(point);
            }
            var pool = far.Count > 0 ? far : any;
            if (pool.Count == 0) return false;
            position = pool[Random.Range(0, pool.Count)].position;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas)) position = hit.position;
            return true;
        }

        IEnumerator WarpIn(GameObject prefab, Vector3 position)
        {
            // Counted at once, so the cap holds while the warp effect plays.
            m_Pending++;
            if (WarpVfx) Destroy(Instantiate(WarpVfx, position, Quaternion.identity), 4f);
            if (WarpSfx) AudioSource.PlayClipAtPoint(WarpSfx, position, SfxVolume);
            yield return new WaitForSeconds(WarpLead);
            m_Pending--;
            if (!IsRunning) yield break;

            Vector3 toPlayer = m_Player ? m_Player.position - position : transform.forward;
            toPlayer.y = 0f;
            var rotation = toPlayer.sqrMagnitude > .01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
            var enemy = Instantiate(prefab, position, rotation);
            var health = enemy.GetComponentInChildren<Health>();
            if (health) m_Alive.Add(health);
        }
    }
}
