using System.Collections;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// A heavy cable from a reactor console into the Boss Hall reactor, drawn with CoreBuster/Power Cable. Its
    /// conductors glow brighter while the console is ready for cores, and <see cref="Surge"/> sends a slug of energy
    /// down the whole cable into the reactor when the console is overcharged.
    /// </summary>
    public class PowerCable : MonoBehaviour
    {
        public Renderer[] Renderers;
        [Tooltip("Cable length in metres (the mesh's UV.x runs 0 at the console to this at the reactor).")]
        [Min(.1f)] public float Length = 8f;
        [Tooltip("Seconds the surge takes to travel from the console into the reactor.")]
        [Min(.1f)] public float SurgeDuration = 1.6f;

        static readonly int k_Flow = Shader.PropertyToID("_Flow");
        static readonly int k_Charge = Shader.PropertyToID("_Charge");
        static readonly int k_Length = Shader.PropertyToID("_Length");

        MaterialPropertyBlock m_Block;
        float m_Flow = -1f;
        float m_Charge;
        float m_TargetCharge;

        public bool IsSurging { get; private set; }

        void Awake() => Apply();

        /// <summary>0..1: how bright the conductors idle (the console waiting for, or ready to take, cores).</summary>
        public void SetCharge(float charge) => m_TargetCharge = Mathf.Clamp01(charge);

        /// <summary>Sends the overcharge down the cable; finishes as it reaches the reactor.</summary>
        public IEnumerator Surge()
        {
            IsSurging = true;
            for (float t = 0f; t < SurgeDuration; t += Time.deltaTime)
            {
                // Eases in: the surge gathers speed as it runs to the reactor.
                float k = t / SurgeDuration;
                m_Flow = k * k * (2f - k);
                yield return null;
            }
            m_Flow = -1f;
            IsSurging = false;
        }

        void Update()
        {
            m_Charge = Mathf.MoveTowards(m_Charge, m_TargetCharge, Time.deltaTime * 2f);
            Apply();
        }

        void Apply()
        {
            m_Block ??= new MaterialPropertyBlock();
            foreach (var renderer in Renderers)
            {
                if (!renderer) continue;
                renderer.GetPropertyBlock(m_Block);
                m_Block.SetFloat(k_Flow, m_Flow);
                m_Block.SetFloat(k_Charge, m_Charge);
                m_Block.SetFloat(k_Length, Length);
                renderer.SetPropertyBlock(m_Block);
            }
        }
    }
}
