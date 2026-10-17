using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// The "Unstable" reactive core: a 3D energy orb that wobbles and spikes, throws off embers, surges at random,
    /// flickers its light and crackles. Inside an enemy it sits deep in the chest, hidden by the body, and the
    /// host's galaxy shader draws it through the surface with parallax once the armour breaks (Ignite).
    /// A core execution drags it out of the body (BeginAbsorb / SetAbsorbProgress) and into the player's gun
    /// (CompleteAbsorb); outside the body the orb mesh and its halo take over.
    /// It also animates in edit mode so the prefab can be previewed in the Scene view.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class UnstableCore : MonoBehaviour
    {
        [Header("Parts")]
        [Tooltip("The orb: a sphere using the CoreBuster/Unstable Orb shader.")]
        public Renderer OrbRenderer;
        [Tooltip("Camera-facing haze around the orb (CoreBuster/Core Halo). Only shown outside a host body.")]
        public Renderer HaloRenderer;
        [Tooltip("Galaxy bodies the core sits inside. They draw it through their surface while it is within them.")]
        public Renderer[] HostRenderers;
        public Light CoreLight;
        [Tooltip("Sparks constantly spat out of the core.")]
        public ParticleSystem Embers;
        [Tooltip("Emitted in bursts when the core surges, ignites or is consumed.")]
        public ParticleSystem SurgeBurst;
        [Tooltip("Expanding ring played when the core ignites or is impaled.")]
        public ParticleSystem Shockwave;
        [Tooltip("World-space particles pulled into the absorb target during an execution.")]
        public ParticleSystem AbsorbStream;
        [Tooltip("Looping hum; surges play through it too.")]
        public AudioSource HumSource;

        [Header("Behaviour")]
        [Tooltip("Visible as soon as it spawns. Enemies turn this off and ignite the core when their armour breaks.")]
        public bool IgniteOnStart = true;
        [Range(0f, 1f)] public float Instability = .5f;
        [Tooltip("Seconds between random surges at full instability (min, max). Lower instability spaces them out.")]
        public Vector2 SurgeInterval = new Vector2(.45f, 1.6f);
        [Tooltip("How quickly a surge dies back down.")]
        [Min(.01f)] public float SurgeDecay = 4f;
        [Tooltip("Speed of the orb's animation.")]
        [Min(0f)] public float TimeScale = 1f;
        [Min(.01f)] public float IgniteDuration = .4f;

        [Header("Light")]
        public float LightIntensity = 3f;
        public float LightRange = 3.5f;

        [Header("Sound")]
        public AudioClip SurgeSfx;
        [Range(0f, 1f)] public float SurgeVolume = .45f;
        public AudioClip IgniteSfx;

        [Header("Absorption")]
        [Tooltip("Fraction of the absorption spent tearing at the core before it starts to travel to the target.")]
        [Range(0f, .9f)] public float TravelStart = .3f;
        [Tooltip("Size the core shrinks to by the time it reaches the target.")]
        [Range(.01f, 1f)] public float AbsorbedScale = .12f;
        public Vector2 StreamEmissionRate = new Vector2(70f, 260f);
        public Vector2 StreamSpeed = new Vector2(2.5f, 11f);

        public bool IsIgnited { get; private set; }
        public bool IsAbsorbing { get; private set; }
        public bool IsConsumed { get; private set; }
        /// <summary>0 while intact, 1 once fully drawn into the absorb target.</summary>
        public float AbsorbProgress { get; private set; }

        static readonly int k_CoreTime = Shader.PropertyToID("_CoreTime");
        static readonly int k_Instability = Shader.PropertyToID("_Instability");
        static readonly int k_Surge = Shader.PropertyToID("_Surge");
        static readonly int k_Collapse = Shader.PropertyToID("_Collapse");
        static readonly int k_Fade = Shader.PropertyToID("_Fade");
        static readonly int k_OrbExposure = Shader.PropertyToID("_OrbExposure");
        static readonly int k_OrbCenter = Shader.PropertyToID("_OrbCenter");
        static readonly int k_OrbRadius = Shader.PropertyToID("_OrbRadius");
        static readonly int k_OrbTime = Shader.PropertyToID("_OrbTime");
        static readonly int k_OrbInstability = Shader.PropertyToID("_OrbInstability");
        static readonly int k_OrbSurge = Shader.PropertyToID("_OrbSurge");
        static readonly int k_OrbCollapse = Shader.PropertyToID("_OrbCollapse");

        MaterialPropertyBlock m_Properties;
        Transform m_Orb;
        Vector3 m_OrbScale;
        float m_MeshRadius = .5f;
        float m_Seed;
        float m_ShaderTime;
        float m_Clock;
        float m_Surge;
        float m_NextSurge;
        float m_Ignite = 1f;
        float m_LastRealtime;

        Transform m_Anchor;
        Vector3 m_AnchorLocalPosition;
        Transform m_AbsorbTarget;
        ParticleSystem.Particle[] m_StreamParticles;

        bool HasHost => HostRenderers != null && HostRenderers.Length > 0;

        void Awake()
        {
            m_Properties = new MaterialPropertyBlock();
            if (OrbRenderer)
            {
                m_Orb = OrbRenderer.transform;
                m_OrbScale = m_Orb.localScale;
                // Radius of the orb mesh itself (Unity's built-in sphere asset is 1, the primitive 0.5).
                var filter = OrbRenderer.GetComponent<MeshFilter>();
                m_MeshRadius = filter && filter.sharedMesh ? filter.sharedMesh.bounds.extents.x : .5f;
            }
            m_Seed = Random.Range(0f, 100f);
            m_ShaderTime = Random.Range(0f, 50f);
            m_LastRealtime = Time.realtimeSinceStartup;
        }

        void Start()
        {
            // Something may already have ignited it (armour broken the frame it spawned).
            if (!Application.isPlaying || IsIgnited) return;
            if (IgniteOnStart) Ignite();
            else SetDormant();
        }

        void OnDisable()
        {
            if (Application.isPlaying) SetHostExposure(0f);
        }

        /// <summary>Flares the core into life: grows it from nothing with a shockwave, burst and sound.</summary>
        public void Ignite()
        {
            if (IsConsumed) return;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            IsIgnited = true;
            m_Ignite = 0f;
            ScheduleSurge();

            if (OrbRenderer) OrbRenderer.enabled = true;
            if (CoreLight) CoreLight.enabled = true;
            if (!Application.isPlaying) return;

            if (Embers) Embers.Play(true);
            if (Shockwave) Shockwave.Play(true);
            if (SurgeBurst) SurgeBurst.Emit(30);
            if (HumSource)
            {
                if (HumSource.clip && !HumSource.isPlaying) HumSource.Play();
                if (IgniteSfx) HumSource.PlayOneShot(IgniteSfx, 1f);
            }
        }

        /// <summary>Hides the core without destroying it (the armour still covers it).</summary>
        public void SetDormant()
        {
            IsIgnited = false;
            if (OrbRenderer) OrbRenderer.enabled = false;
            if (HaloRenderer) HaloRenderer.enabled = false;
            if (CoreLight) CoreLight.enabled = false;
            if (Embers) Embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (AbsorbStream) AbsorbStream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (HumSource) HumSource.Stop();
            SetHostExposure(0f);
        }

        /// <summary>A violent flare, e.g. when the core is impaled. Strength 0..1.</summary>
        public void Surge(float strength = 1f)
        {
            m_Surge = Mathf.Max(m_Surge, Mathf.Clamp01(strength));
            if (!Application.isPlaying || !IsIgnited) return;
            if (SurgeBurst) SurgeBurst.Emit(Mathf.RoundToInt(Mathf.Lerp(6f, 18f, strength)));
            if (HumSource && SurgeSfx) HumSource.PlayOneShot(SurgeSfx, SurgeVolume * Mathf.Lerp(.5f, 1f, strength));
        }

        /// <summary>
        /// Starts tearing the core loose toward the target (the gun's muzzle). The core leaves its parent
        /// so it can travel, but keeps following the spot it was ripped from until it is pulled free.
        /// </summary>
        public void BeginAbsorb(Transform target)
        {
            if (IsConsumed) return;
            if (!IsIgnited) Ignite();
            IsAbsorbing = true;
            AbsorbProgress = 0f;
            m_AbsorbTarget = target;
            m_Anchor = transform.parent;
            m_AnchorLocalPosition = m_Anchor ? m_Anchor.InverseTransformPoint(transform.position) : transform.position;
            transform.SetParent(null, true);

            if (Shockwave) Shockwave.Play(true);
            Surge(1f);
            if (AbsorbStream)
            {
                var emission = AbsorbStream.emission;
                emission.rateOverTime = StreamEmissionRate.x;
                AbsorbStream.Play(true);
            }
        }

        public void SetAbsorbProgress(float progress)
        {
            AbsorbProgress = Mathf.Clamp01(progress);
        }

        /// <summary>The core vanishes into the target in a final flash, then cleans itself up.</summary>
        public void CompleteAbsorb()
        {
            if (IsConsumed) return;
            IsConsumed = true;
            AbsorbProgress = 1f;
            if (m_AbsorbTarget) transform.position = m_AbsorbTarget.position;

            if (OrbRenderer) OrbRenderer.enabled = false;
            if (HaloRenderer) HaloRenderer.enabled = false;
            if (CoreLight) CoreLight.enabled = false;
            if (Embers) Embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (AbsorbStream) AbsorbStream.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            if (SurgeBurst) SurgeBurst.Emit(40);
            if (HumSource) HumSource.Stop();
            SetHostExposure(0f);

            if (Application.isPlaying) Destroy(gameObject, 1.5f);
        }

        void ScheduleSurge()
        {
            float rarity = Mathf.Lerp(3f, 1f, Instability);
            m_NextSurge = m_Clock + Random.Range(SurgeInterval.x, SurgeInterval.y) * rarity;
        }

        void Update()
        {
            float deltaTime;
            if (Application.isPlaying)
            {
                deltaTime = Time.deltaTime;
            }
            else
            {
                float now = Time.realtimeSinceStartup;
                deltaTime = Mathf.Clamp(now - m_LastRealtime, 0f, .1f);
                m_LastRealtime = now;
                IsIgnited = true;
            }
            if (m_Properties == null) Awake();

            if (!IsIgnited || IsConsumed) return;

            m_Clock += deltaTime;
            float collapse = IsAbsorbing ? AbsorbProgress : 0f;
            float instability = Mathf.Lerp(Instability, 1f, collapse);

            if (m_Clock >= m_NextSurge)
            {
                Surge(Random.Range(.45f, 1f) * Mathf.Lerp(.6f, 1f, instability));
                ScheduleSurge();
                // Being ripped out makes it surge constantly.
                if (IsAbsorbing) m_NextSurge = m_Clock + Random.Range(.08f, .25f);
            }
            m_Surge *= Mathf.Exp(-SurgeDecay * deltaTime);

            m_ShaderTime += deltaTime * TimeScale * (1f + instability * .6f + m_Surge * 1.5f + collapse * 2.5f);
            m_Ignite = Mathf.MoveTowards(m_Ignite, 1f, deltaTime / IgniteDuration);

            if (IsAbsorbing) UpdateAbsorbMotion(collapse);
            UpdateVisuals(instability, collapse);
        }

        void UpdateAbsorbMotion(float progress)
        {
            Vector3 anchor = m_Anchor ? m_Anchor.TransformPoint(m_AnchorLocalPosition) : transform.position;
            float travel = Mathf.Pow(Mathf.InverseLerp(TravelStart, 1f, progress), 2.2f);
            // While it is still tearing loose it thrashes about its socket.
            Vector3 thrash = Random.insideUnitSphere * (.02f * (1f - travel) * progress);
            transform.position = m_AbsorbTarget
                ? Vector3.Lerp(anchor, m_AbsorbTarget.position, travel) + thrash
                : anchor + thrash;

            if (AbsorbStream)
            {
                var emission = AbsorbStream.emission;
                emission.rateOverTime = travel < .95f
                    ? Mathf.Lerp(StreamEmissionRate.x, StreamEmissionRate.y, progress)
                    : 0f;
            }
        }

        void UpdateVisuals(float instability, float collapse)
        {
            bool playing = Application.isPlaying;
            float ignite = EaseOutBack(m_Ignite);
            float tremble = (Mathf.PerlinNoise(m_Clock * 9f, m_Seed) - .5f) * .08f * instability;
            float size = ignite * (1f + m_Surge * .14f + tremble) *
                         Mathf.Lerp(1f, AbsorbedScale, Mathf.Pow(collapse, 1.3f));
            // Edit mode only animates the shaders, so nothing serialized is touched by the preview.
            if (m_Orb && playing) m_Orb.localScale = m_OrbScale * size;

            SetShaderState(OrbRenderer, instability, collapse, 1f);
            if (HaloRenderer)
            {
                // Inside a body the host's galaxy shader draws the haze; the halo is for the orb out in the open.
                bool showHalo = !HasHost || IsAbsorbing || !playing;
                if (playing) HaloRenderer.enabled = showHalo;
                SetShaderState(HaloRenderer, instability, collapse, Mathf.Clamp01(m_Ignite * 3f));
            }
            if (playing) SetHostExposure(Mathf.Clamp01(m_Ignite * 2f), instability, collapse);

            if (CoreLight && CoreLight.enabled && playing)
            {
                float flicker = .75f + .5f * Mathf.PerlinNoise(m_Clock * 17f, m_Seed + 3f) * instability;
                CoreLight.intensity = LightIntensity * ignite * (flicker + m_Surge * 1.4f + collapse * 1.5f);
                CoreLight.range = LightRange * (1f + m_Surge * .35f) * Mathf.Lerp(1f, .6f, collapse);
            }

            if (HumSource && playing)
                HumSource.pitch = 1f + m_Surge * .08f + collapse * .6f;
        }

        void SetShaderState(Renderer target, float instability, float collapse, float fade)
        {
            if (!target) return;
            target.GetPropertyBlock(m_Properties);
            m_Properties.SetFloat(k_CoreTime, m_ShaderTime);
            m_Properties.SetFloat(k_Instability, instability);
            m_Properties.SetFloat(k_Surge, Mathf.Clamp01(m_Surge));
            m_Properties.SetFloat(k_Collapse, collapse);
            m_Properties.SetFloat(k_Fade, fade);
            target.SetPropertyBlock(m_Properties);
        }

        // Tells the host bodies where the orb is and how it looks, so they can draw it through their surface.
        void SetHostExposure(float exposure, float instability = 0f, float collapse = 0f)
        {
            if (!HasHost) return;
            if (m_Properties == null) m_Properties = new MaterialPropertyBlock();
            float radius = m_Orb ? m_MeshRadius * m_Orb.lossyScale.x : .08f;
            Vector3 center = m_Orb ? m_Orb.position : transform.position;
            foreach (var host in HostRenderers)
            {
                if (!host) continue;
                host.GetPropertyBlock(m_Properties);
                m_Properties.SetFloat(k_OrbExposure, exposure);
                m_Properties.SetVector(k_OrbCenter, center);
                m_Properties.SetFloat(k_OrbRadius, Mathf.Max(radius, .001f));
                m_Properties.SetFloat(k_OrbTime, m_ShaderTime);
                m_Properties.SetFloat(k_OrbInstability, instability);
                m_Properties.SetFloat(k_OrbSurge, Mathf.Clamp01(m_Surge));
                m_Properties.SetFloat(k_OrbCollapse, collapse);
                host.SetPropertyBlock(m_Properties);
            }
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || !AbsorbStream || !m_AbsorbTarget) return;
            SteerStream(Time.deltaTime);
        }

        // Pull every stream particle toward the target, killing it once it arrives.
        void SteerStream(float deltaTime)
        {
            int max = AbsorbStream.main.maxParticles;
            if (m_StreamParticles == null || m_StreamParticles.Length < max)
                m_StreamParticles = new ParticleSystem.Particle[max];

            int count = AbsorbStream.GetParticles(m_StreamParticles);
            if (count == 0) return;

            Vector3 target = m_AbsorbTarget.position;
            float speed = Mathf.Lerp(StreamSpeed.x, StreamSpeed.y, AbsorbProgress);
            float steer = 1f - Mathf.Exp(-9f * deltaTime);
            for (int i = 0; i < count; i++)
            {
                Vector3 toTarget = target - m_StreamParticles[i].position;
                float distance = toTarget.magnitude;
                if (distance < .07f)
                {
                    m_StreamParticles[i].remainingLifetime = 0f;
                    continue;
                }
                Vector3 desired = toTarget / distance * speed;
                m_StreamParticles[i].velocity = Vector3.Lerp(m_StreamParticles[i].velocity, desired, steer);
                // Don't let a particle fade out before it gets there.
                m_StreamParticles[i].remainingLifetime =
                    Mathf.Max(m_StreamParticles[i].remainingLifetime, Mathf.Min(distance / speed, .6f));
            }
            AbsorbStream.SetParticles(m_StreamParticles, count);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.9f;
            const float c3 = c1 + 1f;
            t = Mathf.Clamp01(t);
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
