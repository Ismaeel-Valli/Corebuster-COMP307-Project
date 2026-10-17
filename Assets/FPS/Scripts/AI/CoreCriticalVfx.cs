using UnityEngine;

namespace Unity.FPS.AI
{
    /// <summary>
    /// Warning effects around an executed enemy's ruptured core in the seconds before it bursts:
    /// red sparks and smoke leaking from the chest, a flickering light, and a ring on the floor marking
    /// the blast radius that collapses inward faster and faster. ExecutedCorpse drives it every frame.
    /// </summary>
    public class CoreCriticalVfx : MonoBehaviour
    {
        [Header("Anchors")]
        [Tooltip("Follows the ruptured chest.")]
        public Transform ChestGroup;
        [Tooltip("Sits on the floor under the chest, where the gas will burst.")]
        public Transform GroundGroup;

        [Header("Parts")]
        public ParticleSystem Sparks;
        public ParticleSystem Smoke;
        [Tooltip("Rings the size of the blast that shrink toward the body.")]
        public ParticleSystem GroundRing;
        public Light WarningLight;
        public float LightIntensity = 5f;
        [Tooltip("Plays the beeps and the rising whine.")]
        public AudioSource Audio;

        [Header("Emission (start, just before the blast)")]
        public Vector2 SparkRate = new Vector2(25f, 160f);
        public Vector2 SmokeRate = new Vector2(4f, 16f);
        public Vector2 RingRate = new Vector2(1.5f, 8f);

        bool m_Released;

        void Start()
        {
            foreach (var particles in new[] { Sparks, Smoke, GroundRing })
                if (particles) particles.Play(true);
        }

        /// <summary>Progress runs 0..1 toward the blast; pulse is the 0..1 heartbeat of the overload.</summary>
        public void SetState(Vector3 chest, Vector3 ground, float progress, float pulse)
        {
            if (m_Released) return;
            if (ChestGroup) ChestGroup.position = chest;
            if (GroundGroup) GroundGroup.position = ground;
            SetRate(Sparks, SparkRate, progress);
            SetRate(Smoke, SmokeRate, progress);
            SetRate(GroundRing, RingRate, progress);
            if (WarningLight) WarningLight.intensity = LightIntensity * (.25f + .75f * pulse) * (.5f + progress);
        }

        static void SetRate(ParticleSystem particles, Vector2 range, float progress)
        {
            if (!particles) return;
            var emission = particles.emission;
            emission.rateOverTimeMultiplier = Mathf.Lerp(range.x, range.y, progress * progress);
        }

        /// <summary>Stops emitting and cleans up once the last particles are gone.</summary>
        public void Release()
        {
            if (m_Released) return;
            m_Released = true;
            foreach (var particles in new[] { Sparks, Smoke, GroundRing })
                if (particles) particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (WarningLight) WarningLight.enabled = false;
            if (Audio) Audio.Stop();
            Destroy(gameObject, 1.5f);
        }
    }
}
