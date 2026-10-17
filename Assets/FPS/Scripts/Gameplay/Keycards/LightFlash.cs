using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>Drives a light's intensity along a curve once, from when it is spawned (the enemy warp-in flash).</summary>
    [RequireComponent(typeof(Light))]
    public class LightFlash : MonoBehaviour
    {
        [Tooltip("Intensity over time, in seconds.")]
        public AnimationCurve Intensity = AnimationCurve.EaseInOut(0f, 0f, .5f, 5f);

        Light m_Light;
        float m_Start;

        void OnEnable()
        {
            m_Light = GetComponent<Light>();
            m_Start = Time.time;
        }

        void Update() => m_Light.intensity = Mathf.Max(0f, Intensity.Evaluate(Time.time - m_Start));
    }
}
