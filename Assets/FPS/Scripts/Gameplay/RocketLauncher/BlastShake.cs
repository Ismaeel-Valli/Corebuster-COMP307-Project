using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// A short view shake for a nearby blast, on the player's camera (added on first use). It adds a decaying
    /// rotation on top of whatever set the camera this frame. Whoever owns the camera (the character controller, an
    /// execution) sets its rotation afresh each frame, so the offset never accumulates; if nobody did, the last
    /// frame's offset is taken back off first.
    /// </summary>
    public class BlastShake : MonoBehaviour
    {
        [Tooltip("Degrees of pitch/yaw/roll at full trauma.")]
        public Vector3 MaxAngles = new Vector3(3.5f, 2.5f, 2f);
        [Tooltip("Trauma lost per second.")]
        public float Decay = 2.6f;
        public float Frequency = 22f;

        static Camera s_Camera;

        float m_Trauma;
        float m_Seed;
        Quaternion m_Applied = Quaternion.identity;
        Quaternion m_Left = Quaternion.identity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Camera = null;

        /// <summary>Shakes the player's view for a blast at origin: full strength at the blast, none beyond radius.</summary>
        public static void Shake(Vector3 origin, float radius, float strength)
        {
            if (radius <= 0f || strength <= 0f) return;
            if (!s_Camera)
            {
                var player = FindAnyObjectByType<PlayerCharacterController>();
                if (!player) return;
                s_Camera = player.PlayerCamera;
                if (!s_Camera) return;
            }
            float falloff = 1f - Vector3.Distance(origin, s_Camera.transform.position) / radius;
            if (falloff <= 0f) return;
            var shake = s_Camera.GetComponent<BlastShake>();
            if (!shake) shake = s_Camera.gameObject.AddComponent<BlastShake>();
            shake.m_Trauma = Mathf.Clamp01(shake.m_Trauma + strength * falloff * falloff);
        }

        void Awake() => m_Seed = Random.Range(0f, 100f);

        void LateUpdate()
        {
            // Nobody set the camera since our last offset: take it back off before applying this frame's.
            if (transform.localRotation == m_Left) transform.localRotation *= Quaternion.Inverse(m_Applied);
            m_Applied = Quaternion.identity;
            if (m_Trauma > 0f)
            {
                float shake = m_Trauma * m_Trauma;
                float t = Time.time * Frequency;
                m_Applied = Quaternion.Euler(
                    MaxAngles.x * shake * (Mathf.PerlinNoise(m_Seed, t) * 2f - 1f),
                    MaxAngles.y * shake * (Mathf.PerlinNoise(m_Seed + 13f, t) * 2f - 1f),
                    MaxAngles.z * shake * (Mathf.PerlinNoise(m_Seed + 29f, t) * 2f - 1f));
                transform.localRotation *= m_Applied;
                m_Trauma = Mathf.Max(0f, m_Trauma - Decay * Time.deltaTime);
            }
            m_Left = transform.localRotation;
        }
    }
}
