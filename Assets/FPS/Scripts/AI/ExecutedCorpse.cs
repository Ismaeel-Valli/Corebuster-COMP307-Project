using System.Collections.Generic;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.FPS.AI
{
    /// <summary>
    /// What happens to an enemy after its core is torn out: the body is knocked back and topples to the ground,
    /// overloads for DetonateDelay seconds with escalating warnings (red-hot pulsing body, sparks, a blast-radius
    /// ring on the floor, speeding beeps), then bursts into a CoreGasCloud.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CoreExecutionTarget))]
    public class ExecutedCorpse : MonoBehaviour
    {
        [Header("Collapse")]
        [Min(.05f)] public float FallDuration = .7f;
        [Tooltip("How far the body tips back; about 90 lays it flat.")]
        public float FallAngle = 86f;
        [Tooltip("The body pivots around a point this far behind its feet, so it lands on its back rather than sinking.")]
        public float HeelOffset = .22f;
        [Tooltip("Degrees it bounces back up when it hits the floor.")]
        public float BounceAngle = 6f;

        [Header("Detonation")]
        [Tooltip("Seconds from death to the gas burst.")]
        [Min(0f)] public float DetonateDelay = 2f;
        public CoreGasCloud GasCloudPrefab;
        public CoreCriticalVfx CriticalVfxPrefab;
        [Tooltip("The chest bone the core sat in. Defaults to the core's parent.")]
        public Transform CoreSocket;

        [Header("Warning Sound")]
        public AudioClip BeepSfx;
        public AudioClip WhineSfx;
        [Tooltip("Seconds between beeps at death and just before the blast.")]
        public Vector2 BeepInterval = new Vector2(.42f, .06f);
        [Range(0f, 1f)] public float BeepVolume = .8f;

        /// <summary>Corpses counting down to a blast (for HUD warnings).</summary>
        public static readonly List<ExecutedCorpse> Pending = new List<ExecutedCorpse>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Pending.Clear();

        public bool IsCritical { get; private set; }
        /// <summary>0 at death, 1 at the blast.</summary>
        public float Progress => DetonateDelay > 0f ? Mathf.Clamp01(m_Time / DetonateDelay) : 1f;
        public float BlastRadius => GasCloudPrefab ? GasCloudPrefab.Radius : 3f;
        /// <summary>Where the gas will burst: on the floor under the ruptured chest.</summary>
        public Vector3 BlastCenter { get; private set; }

        static readonly int k_Heat = Shader.PropertyToID("_Heat");
        static readonly int k_HeatGlow = Shader.PropertyToID("_HeatGlow");

        CoreExecutionTarget m_Target;
        CoreCriticalVfx m_Vfx;
        MaterialPropertyBlock m_Properties;
        Vector3 m_StartPosition;
        Quaternion m_StartRotation;
        Vector3 m_Pivot;
        Vector3 m_FallAxis;
        float m_Time;
        float m_Phase;
        float m_NextBeep;

        void Awake()
        {
            m_Target = GetComponent<CoreExecutionTarget>();
            m_Target.OnExecutionFinished += Begin;
            if (!CoreSocket && m_Target.Core) CoreSocket = m_Target.Core.transform.parent;
        }

        void OnDestroy()
        {
            if (m_Target) m_Target.OnExecutionFinished -= Begin;
            Pending.Remove(this);
            if (m_Vfx) m_Vfx.Release();
        }

        // Called as the core is torn out, just before the enemy is killed.
        void Begin()
        {
            if (IsCritical) return;
            IsCritical = true;
            m_Time = 0f;
            m_Properties = new MaterialPropertyBlock();

            // The death explosion is replaced by the gas burst, and the body must outlive the countdown.
            var enemy = GetComponent<EnemyController>();
            if (enemy)
            {
                enemy.DeathVfx = null;
                enemy.DeathDuration = Mathf.Max(enemy.DeathDuration, DetonateDelay + 1f);
                enemy.enabled = false;
            }
            var chase = GetComponent<Robot1ChaseController>();
            if (chase) chase.enabled = false;
            var procedural = GetComponent<Robot1ProceduralAnimator>();
            if (procedural) procedural.enabled = false;
            // Freeze the Animator so the body keeps its final agonised pose as it falls.
            var animator = GetComponent<Animator>();
            if (animator) animator.enabled = false;
            var agent = GetComponent<NavMeshAgent>();
            if (agent) agent.enabled = false;
            // Shots and the player pass through the corpse.
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;

            m_StartPosition = transform.position;
            m_StartRotation = transform.rotation;
            m_FallAxis = transform.right;
            m_Pivot = m_StartPosition - transform.forward * HeelOffset;
            BlastCenter = GroundUnder(ChestPosition());

            if (CriticalVfxPrefab)
            {
                m_Vfx = Instantiate(CriticalVfxPrefab, BlastCenter, Quaternion.identity);
                if (m_Vfx.Audio && WhineSfx) m_Vfx.Audio.PlayOneShot(WhineSfx, BeepVolume);
            }
            m_NextBeep = 0f;
            Pending.Add(this);
        }

        void Update()
        {
            if (!IsCritical) return;
            float deltaTime = Time.deltaTime;
            m_Time += deltaTime;

            UpdateFall();

            float progress = Progress;
            Vector3 chest = ChestPosition();
            BlastCenter = GroundUnder(chest);

            // The overload's heartbeat speeds up toward the blast.
            m_Phase += deltaTime * Mathf.Lerp(3f, 14f, progress) * Mathf.PI * 2f;
            float pulse = .5f + .5f * Mathf.Sin(m_Phase);
            ApplyHeat(Mathf.SmoothStep(0f, 1f, progress / .6f), pulse * (.3f + 1.9f * progress));

            if (m_Vfx)
            {
                m_Vfx.SetState(chest, BlastCenter, progress, pulse);
                if (m_Time >= m_NextBeep)
                {
                    if (m_Vfx.Audio && BeepSfx) m_Vfx.Audio.PlayOneShot(BeepSfx, BeepVolume);
                    m_NextBeep = m_Time + Mathf.Lerp(BeepInterval.x, BeepInterval.y, Mathf.Pow(progress, 1.4f));
                }
            }

            if (m_Time >= DetonateDelay) Detonate();
        }

        // Tips back around the heels, accelerating like it's falling, with a small bounce on landing.
        void UpdateFall()
        {
            float angle;
            if (m_Time < FallDuration)
            {
                float k = m_Time / FallDuration;
                angle = FallAngle * k * k;
            }
            else
            {
                float after = m_Time - FallDuration;
                angle = FallAngle - BounceAngle * Mathf.Abs(Mathf.Sin(after * 11f)) * Mathf.Exp(-after * 7f);
            }
            Quaternion tip = Quaternion.AngleAxis(-angle, m_FallAxis);
            transform.SetPositionAndRotation(m_Pivot + tip * (m_StartPosition - m_Pivot), tip * m_StartRotation);
        }

        void Detonate()
        {
            IsCritical = false;
            Pending.Remove(this);
            if (GasCloudPrefab) Instantiate(GasCloudPrefab, BlastCenter, Quaternion.identity);
            if (m_Vfx) m_Vfx.Release();
            m_Vfx = null;
            foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            Destroy(gameObject);
        }

        Vector3 ChestPosition() => CoreSocket ? CoreSocket.position : transform.position + transform.up;

        // The floor the enemy stood on, under the given point.
        Vector3 GroundUnder(Vector3 point) => new Vector3(point.x, m_StartPosition.y, point.z);

        void ApplyHeat(float heat, float glow)
        {
            if (m_Target.BodyRenderers == null) return;
            foreach (var body in m_Target.BodyRenderers)
            {
                if (!body) continue;
                body.GetPropertyBlock(m_Properties);
                m_Properties.SetFloat(k_Heat, heat);
                m_Properties.SetFloat(k_HeatGlow, glow);
                body.SetPropertyBlock(m_Properties);
            }
        }
    }
}
