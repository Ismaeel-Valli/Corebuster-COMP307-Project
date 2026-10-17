using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// An iris hatch on an armoured enemy's chest (CoreBuster/Core Window shader). While the Disassembler has the
    /// enemy electrocuted it snaps open onto the Unstable Core, which the shader draws deep inside the cavity,
    /// then shuts again. It mirrors the real core's state, so the core it shows surges with it and collapses
    /// as an execution drains it.
    /// </summary>
    [DisallowMultipleComponent]
    public class CoreWindow : MonoBehaviour
    {
        public Renderer WindowRenderer;
        [Tooltip("Light spilling out of the open hatch.")]
        public Light SpillLight;
        public float SpillIntensity = 2.5f;

        [Header("Timing")]
        [Tooltip("Seconds for the hatch to pop out of the armour before the iris starts to turn.")]
        [Min(.01f)] public float PopDuration = .08f;
        [Min(.01f)] public float OpenDuration = .28f;
        [Min(.01f)] public float CloseDuration = .22f;

        [Header("Sound")]
        public AudioSource Source;
        public AudioClip OpenSfx;
        public AudioClip CloseSfx;
        [Range(0f, 1f)] public float Volume = .8f;

        public bool IsOpen { get; private set; }

        static readonly int k_Open = Shader.PropertyToID("_Open");
        static readonly int k_CoreTime = Shader.PropertyToID("_CoreTime");
        static readonly int k_Instability = Shader.PropertyToID("_Instability");
        static readonly int k_Surge = Shader.PropertyToID("_Surge");
        static readonly int k_Collapse = Shader.PropertyToID("_Collapse");
        static readonly int k_Electric = Shader.PropertyToID("_Electric");

        MaterialPropertyBlock m_Properties;
        UnstableCore m_Core;
        Vector3 m_Scale;
        float m_Pop;
        float m_Open;
        float m_Time;
        float m_Surge;
        float m_Collapse;

        void Awake()
        {
            if (!WindowRenderer) WindowRenderer = GetComponentInChildren<Renderer>(true);
            m_Properties = new MaterialPropertyBlock();
            m_Scale = transform.localScale;
            m_Time = Random.Range(0f, 50f);
            SetVisible(false);
        }

        /// <summary>Pops the hatch out and turns the iris open onto the core.</summary>
        public void Open(UnstableCore core)
        {
            m_Core = core;
            if (IsOpen) return;
            IsOpen = true;
            m_Collapse = 0f;
            enabled = true;
            SetVisible(true);
            Play(OpenSfx);
        }

        /// <summary>Turns the iris shut; the hatch sinks back into the armour.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Play(CloseSfx);
        }

        void Update()
        {
            float deltaTime = Time.deltaTime;
            if (IsOpen)
            {
                m_Pop = Mathf.MoveTowards(m_Pop, 1f, deltaTime / PopDuration);
                if (m_Pop >= 1f) m_Open = Mathf.MoveTowards(m_Open, 1f, deltaTime / OpenDuration);
            }
            else
            {
                m_Open = Mathf.MoveTowards(m_Open, 0f, deltaTime / CloseDuration);
                if (m_Open <= 0f) m_Pop = Mathf.MoveTowards(m_Pop, 0f, deltaTime / PopDuration);
                if (m_Pop <= 0f)
                {
                    SetVisible(false);
                    enabled = false;
                    return;
                }
            }

            // Follow the real core: it surges at random, and an execution drains it (it is gone once consumed).
            bool coreLive = m_Core && !m_Core.IsConsumed;
            float absorb = coreLive ? m_Core.AbsorbProgress : 1f;
            m_Collapse = Mathf.Max(m_Collapse, m_Core ? absorb : 0f);
            m_Surge = Mathf.Max(m_Surge * Mathf.Exp(-5f * deltaTime), Random.value < deltaTime * 2.5f ? Random.Range(.4f, 1f) : 0f);
            m_Time += deltaTime * (1f + m_Surge * 1.5f + m_Collapse * 2.5f);

            // The hatch overshoots as it pops out of the armour.
            float pop = EaseOutBack(m_Pop);
            transform.localScale = m_Scale * Mathf.Max(pop, .001f);
            float open = EaseOutBack(m_Open);

            WindowRenderer.GetPropertyBlock(m_Properties);
            m_Properties.SetFloat(k_Open, Mathf.Clamp(open, 0f, 1.05f));
            m_Properties.SetFloat(k_CoreTime, m_Time);
            m_Properties.SetFloat(k_Instability, Mathf.Lerp(.75f, 1f, m_Collapse));
            m_Properties.SetFloat(k_Surge, m_Surge);
            m_Properties.SetFloat(k_Collapse, m_Collapse);
            m_Properties.SetFloat(k_Electric, IsOpen ? 1f : .3f);
            WindowRenderer.SetPropertyBlock(m_Properties);

            if (SpillLight)
            {
                float flicker = .75f + .5f * Mathf.PerlinNoise(m_Time * 12f, 3.1f);
                SpillLight.intensity = SpillIntensity * Mathf.Clamp01(m_Open) * (1f - m_Collapse) * (flicker + m_Surge);
            }
        }

        void SetVisible(bool visible)
        {
            if (WindowRenderer) WindowRenderer.enabled = visible;
            if (SpillLight)
            {
                SpillLight.enabled = visible;
                SpillLight.intensity = 0f;
            }
            if (!visible)
            {
                m_Pop = 0f;
                m_Open = 0f;
                transform.localScale = m_Scale;
            }
        }

        void Play(AudioClip clip)
        {
            if (Source && clip && Source.isActiveAndEnabled) Source.PlayOneShot(clip, Volume);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.7f;
            const float c3 = c1 + 1f;
            t = Mathf.Clamp01(t);
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
