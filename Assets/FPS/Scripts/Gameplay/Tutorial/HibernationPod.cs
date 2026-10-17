using System.Collections;
using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The emergency officer's hibernation pod. Starts sealed; Open vents its cold mist and swings the canopy up and
    /// back as the officer is revived, and its interior light fades from revival white to standby blue.
    /// </summary>
    public class HibernationPod : MonoBehaviour
    {
        [Tooltip("Hinged at its head end; rotated by OpenEuler (local) when open.")]
        public Transform Canopy;
        public Vector3 OpenEuler = new Vector3(-62f, 0f, 0f);
        public float OpenDuration = 2.4f;

        [Header("Effects")]
        public ParticleSystem[] Mist;
        public Light InteriorLight;
        public Color RevivalColor = new Color(.85f, .95f, 1f);
        public Color StandbyColor = new Color(.2f, .55f, 1f);
        public float RevivalIntensity = 6f;
        public float StandbyIntensity = 1.6f;

        [Header("Sound")]
        public AudioClip HissSfx;
        public AudioClip LatchSfx;
        [Range(0f, 1f)] public float SfxVolume = .9f;

        public bool IsOpen { get; private set; }

        Quaternion m_Closed;
        AudioSource m_Audio;

        void Awake()
        {
            if (Canopy) m_Closed = Canopy.localRotation;
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = .8f;
            m_Audio.minDistance = 2f;
            m_Audio.maxDistance = 25f;
            if (InteriorLight)
            {
                InteriorLight.color = RevivalColor;
                InteriorLight.intensity = RevivalIntensity;
            }
        }

        /// <summary>Unseals the pod (once). Immediate skips the animation and the effects.</summary>
        public void Open(bool immediate = false)
        {
            if (IsOpen) return;
            IsOpen = true;
            if (immediate)
            {
                if (Canopy) Canopy.localRotation = m_Closed * Quaternion.Euler(OpenEuler);
                if (InteriorLight)
                {
                    InteriorLight.color = StandbyColor;
                    InteriorLight.intensity = StandbyIntensity;
                }
                return;
            }
            StartCoroutine(OpenSequence());
        }

        IEnumerator OpenSequence()
        {
            if (LatchSfx) m_Audio.PlayOneShot(LatchSfx, SfxVolume);
            yield return new WaitForSeconds(.35f);
            if (HissSfx) m_Audio.PlayOneShot(HissSfx, SfxVolume);
            if (Mist != null)
                foreach (var mist in Mist)
                    if (mist) mist.Play(true);

            Quaternion open = m_Closed * Quaternion.Euler(OpenEuler);
            for (float t = 0f; t < OpenDuration; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / OpenDuration);
                // Pops off its seals, then swings up smoothly and settles.
                float swing = k < .12f ? k / .12f * .04f : .04f + .96f * Mathf.SmoothStep(0f, 1f, (k - .12f) / .88f);
                if (Canopy) Canopy.localRotation = Quaternion.Slerp(m_Closed, open, swing);
                if (InteriorLight)
                {
                    InteriorLight.color = Color.Lerp(RevivalColor, StandbyColor, k);
                    InteriorLight.intensity = Mathf.Lerp(RevivalIntensity, StandbyIntensity, k);
                }
                yield return null;
            }
            if (Canopy) Canopy.localRotation = open;
        }
    }
}
