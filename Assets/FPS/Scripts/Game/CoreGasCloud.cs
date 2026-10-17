using System.Collections.Generic;
using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// The red galaxy gas an executed enemy bursts into. It blasts out, lingers, and hurts the player in ticks
    /// while they stand in it. Only MaxActiveClouds stay live at once: a new cloud makes the oldest dissipate.
    /// </summary>
    public class CoreGasCloud : MonoBehaviour
    {
        [Header("Area")]
        [Tooltip("Horizontal reach of the gas from its centre.")]
        [Min(.1f)] public float Radius = 3.2f;
        [Tooltip("How high the gas hangs above its centre.")]
        [Min(.1f)] public float Height = 2.8f;

        [Header("Damage")]
        [Min(0f)] public float DamagePerTick = 6f;
        [Min(.05f)] public float TickInterval = .5f;

        [Header("Lifetime")]
        [Min(.01f)] public float GrowDuration = .6f;
        [Tooltip("Seconds the gas lingers before it starts to thin out.")]
        [Min(0f)] public float Duration = 8f;
        [Min(.01f)] public float DissipateDuration = 1.5f;
        [Tooltip("How quickly the oldest cloud clears when a new one pushes it over the limit.")]
        [Min(.01f)] public float ForcedDissipateDuration = .6f;
        [Tooltip("Clouds allowed at once, to keep the overdraw in check.")]
        [Min(1)] public int MaxActiveClouds = 3;

        [Header("Parts")]
        [Tooltip("Lingering gas; emission and fade follow the cloud's strength.")]
        public ParticleSystem Gas;
        [Tooltip("Stars twinkling in the gas; emission follows the cloud's strength.")]
        public ParticleSystem Stars;
        public Light CloudLight;
        public float LightIntensity = 4f;
        [Tooltip("Looping hiss; the blast sound plays through it too.")]
        public AudioSource Hiss;
        public AudioClip BlastSfx;

        /// <summary>Clouds that are still at full strength, oldest first.</summary>
        public static readonly List<CoreGasCloud> Active = new List<CoreGasCloud>();
        static readonly List<CoreGasCloud> s_All = new List<CoreGasCloud>();
        static float s_NextPlayerTick;

        /// <summary>
        /// True while the player is invincible only because they are performing an execution. That shield
        /// keeps enemies off them, but not the gas: executing inside a cloud still costs health, so the
        /// player can't farm executions from one spot.
        /// </summary>
        public static bool PlayerShieldedByExecution { get; set; }

        /// <summary>Time the player last took gas damage (for the screen cue).</summary>
        public static float LastPlayerDamageTime { get; private set; } = float.NegativeInfinity;

        /// <summary>How deep the player is in any cloud right now, 0..1.</summary>
        public static float PlayerExposure
        {
            get
            {
                float exposure = 0f;
                foreach (var cloud in s_All)
                    if (cloud) exposure = Mathf.Max(exposure, cloud.Exposure);
                return exposure;
            }
        }

        /// <summary>This cloud's hold on the player, 0..1.</summary>
        public float Exposure { get; private set; }
        public bool IsDissipating { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active.Clear();
            s_All.Clear();
            s_NextPlayerTick = 0f;
            PlayerShieldedByExecution = false;
            LastPlayerDamageTime = float.NegativeInfinity;
        }

        static readonly int k_Fade = Shader.PropertyToID("_Fade");
        static readonly int k_GroundHeight = Shader.PropertyToID("_GroundHeight");

        Health m_PlayerHealth;
        Transform m_Player;
        MaterialPropertyBlock m_Properties;
        ParticleSystemRenderer m_GasRenderer;
        float m_Age;
        float m_DissipateAge;
        float m_DissipateLength;
        float m_HissVolume = 1f;
        float m_Seed;

        void Awake()
        {
            m_Properties = new MaterialPropertyBlock();
            m_Seed = Random.Range(0f, 100f);
            if (Gas) m_GasRenderer = Gas.GetComponent<ParticleSystemRenderer>();
            if (Hiss) m_HissVolume = Hiss.volume;
        }

        void OnEnable()
        {
            s_All.Add(this);
            Active.Add(this);
            // Over the limit: the oldest clouds clear out quickly.
            while (Active.Count > Mathf.Max(1, MaxActiveClouds))
                Active[0].Dissipate(Active[0].ForcedDissipateDuration);
        }

        void OnDisable()
        {
            s_All.Remove(this);
            Active.Remove(this);
        }

        void Start()
        {
            var actors = FindAnyObjectByType<ActorsManager>();
            if (actors && actors.Player)
            {
                m_Player = actors.Player.transform;
                m_PlayerHealth = actors.Player.GetComponent<Health>();
            }
            if (Hiss)
            {
                if (Hiss.clip) Hiss.Play();
                if (BlastSfx) Hiss.PlayOneShot(BlastSfx, 1f);
            }
        }

        /// <summary>Stops the gas feeding and lets it thin away over the given time.</summary>
        public void Dissipate(float duration)
        {
            if (IsDissipating) return;
            IsDissipating = true;
            m_DissipateAge = 0f;
            m_DissipateLength = Mathf.Max(.01f, duration);
            Active.Remove(this);
            if (Gas) Gas.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (Stars) Stars.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Update()
        {
            float deltaTime = Time.deltaTime;
            m_Age += deltaTime;
            if (!IsDissipating && m_Age >= GrowDuration + Duration) Dissipate(DissipateDuration);

            float grow = Mathf.Clamp01(m_Age / GrowDuration);
            float fade = 1f;
            if (IsDissipating)
            {
                m_DissipateAge += deltaTime;
                fade = 1f - Mathf.Clamp01(m_DissipateAge / m_DissipateLength);
            }
            float strength = Mathf.Sqrt(grow) * fade;

            ApplyVisuals(strength);
            UpdateDamage(strength, grow);

            if (IsDissipating && fade <= 0f) Destroy(gameObject);
        }

        void ApplyVisuals(float strength)
        {
            if (m_GasRenderer)
            {
                m_GasRenderer.GetPropertyBlock(m_Properties);
                m_Properties.SetFloat(k_Fade, strength);
                m_Properties.SetFloat(k_GroundHeight, transform.position.y);
                m_GasRenderer.SetPropertyBlock(m_Properties);
            }
            if (CloudLight)
            {
                float flicker = .8f + .4f * Mathf.PerlinNoise(Time.time * 6f, m_Seed);
                CloudLight.intensity = LightIntensity * strength * flicker;
            }
            if (Hiss) Hiss.volume = m_HissVolume * strength;
        }

        void UpdateDamage(float strength, float grow)
        {
            Exposure = 0f;
            if (!m_Player || !m_PlayerHealth || m_PlayerHealth.CurrentHealth <= 0f || strength < .3f) return;

            Vector3 offset = m_Player.position - transform.position;
            float horizontal = new Vector2(offset.x, offset.z).magnitude;
            float reach = Radius * Mathf.Lerp(.4f, 1f, grow) * Mathf.Lerp(.6f, 1f, strength);
            if (horizontal > reach || offset.y < -1f || offset.y > Height) return;

            Exposure = Mathf.Clamp01(strength * Mathf.Lerp(1f, .45f, horizontal / reach));

            // Overlapping clouds share one damage clock so they don't stack.
            if (Time.time < s_NextPlayerTick) return;
            s_NextPlayerTick = Time.time + TickInterval;
            bool shielded = m_PlayerHealth.Invincible;
            if (shielded && !PlayerShieldedByExecution) return;
            // Health ignores all damage while invincible, so lift the execution shield for this one tick.
            m_PlayerHealth.Invincible = false;
            m_PlayerHealth.TakeDamage(DamagePerTick, gameObject);
            m_PlayerHealth.Invincible = shielded;
            LastPlayerDamageTime = Time.time;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .1f, .1f, .6f);
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
