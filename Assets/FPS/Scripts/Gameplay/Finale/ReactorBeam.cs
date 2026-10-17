using System.Collections;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The beam the Boss Hall reactor fires into the underside of the ship's core (CoreBuster/Reactor Beam).
    /// <see cref="Overcharge"/> swells it when a console's surge reaches the reactor: thicker, faster, brighter and
    /// shifting to white-violet, with its light and a camera-felt crackle following.
    /// </summary>
    public class ReactorBeam : MonoBehaviour
    {
        public Renderer[] Renderers;
        public Light BeamLight;
        [Tooltip("Beam length in metres (the mesh's UV.y runs 0 at the reactor to 1 at the core).")]
        [Min(.1f)] public float Length = 4f;
        [Min(0f)] public float LightIntensity = 10f;
        public AudioSource Hum;
        public AudioClip SurgeSfx;

        static readonly int k_Overcharge = Shader.PropertyToID("_Overcharge");
        static readonly int k_Length = Shader.PropertyToID("_Length");

        MaterialPropertyBlock m_Block;
        float m_HumPitch = 1f;

        /// <summary>0..1: how overcharged the beam is right now.</summary>
        public float Level { get; private set; }

        void Awake()
        {
            if (Hum) m_HumPitch = Hum.pitch;
            Apply();
        }

        /// <summary>Swells the beam over rise seconds, holds it for hold, then lets it settle over fall.</summary>
        public IEnumerator Overcharge(float rise, float hold, float fall)
        {
            if (Hum && SurgeSfx) Hum.PlayOneShot(SurgeSfx);
            for (float t = 0f; t < rise; t += Time.deltaTime)
            {
                Level = Mathf.SmoothStep(0f, 1f, t / rise);
                yield return null;
            }
            Level = 1f;
            yield return new WaitForSeconds(hold);
            for (float t = 0f; t < fall; t += Time.deltaTime)
            {
                Level = 1f - Mathf.SmoothStep(0f, 1f, t / fall);
                yield return null;
            }
            Level = 0f;
        }

        void Update()
        {
            Apply();
            if (BeamLight)
            {
                float flicker = .85f + .3f * Mathf.PerlinNoise(Time.time * (12f + 30f * Level), 3.1f);
                BeamLight.intensity = LightIntensity * (1f + 3f * Level) * flicker;
            }
            if (Hum) Hum.pitch = m_HumPitch * (1f + .6f * Level);
        }

        void Apply()
        {
            m_Block ??= new MaterialPropertyBlock();
            foreach (var renderer in Renderers)
            {
                if (!renderer) continue;
                renderer.GetPropertyBlock(m_Block);
                m_Block.SetFloat(k_Overcharge, Level);
                m_Block.SetFloat(k_Length, Length);
                renderer.SetPropertyBlock(m_Block);
            }
        }
    }
}
