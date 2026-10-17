using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// Scaled-time, visual-only flicker for a Centre Room reactor light. It follows the same
    /// surge signal as the reactor's containment shader, so the room light pulses with the field.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class ReactorInstabilityLight : MonoBehaviour
    {
        /// <summary>Material property holding the single speed/amplitude control (0 steady, 1 full design).</summary>
        public const string InstabilityProperty = "_Instability";

        /// <summary>Flash and dropout events start on slots of this rate, so at most this many flashes occur per second.</summary>
        public const float EventSlotsPerSecond = 2f;

        [SerializeField] Material field;
        [SerializeField] float minimum = .55f;
        [SerializeField] float maximum = 2.2f;

        Light lamp;
        float baseIntensity;
        int instabilityId;

        public Material Field => field;
        public float Minimum => minimum;
        public float Maximum => maximum;

        public void Configure(Material containmentField, float minimumFactor, float maximumFactor)
        {
            field = containmentField;
            minimum = minimumFactor;
            maximum = maximumFactor;
        }

        /// <summary>Reads the shared control from the containment material; 1 if it is missing.</summary>
        public static float InstabilityOf(Material containmentField)
        {
            return containmentField != null && containmentField.HasProperty(InstabilityProperty)
                ? Mathf.Clamp01(containmentField.GetFloat(InstabilityProperty))
                : 1f;
        }

        /// <summary>Light multiplier for time t, clamped so the room never drops to dark.</summary>
        public float Factor(float time, float instability) => Mathf.Clamp(Surge(time, instability), minimum, maximum);

        void Awake()
        {
            lamp = GetComponent<Light>();
            baseIntensity = lamp.intensity;
            instabilityId = Shader.PropertyToID(InstabilityProperty);
        }

        void Update()
        {
            // Paused or zero time scale: hold the current level, as the shader's scaled _Time does.
            if (Time.deltaTime <= 0f) return;
            float instability = field != null && field.HasProperty(instabilityId) ? Mathf.Clamp01(field.GetFloat(instabilityId)) : 1f;
            // URP feeds the shader's _Time.y from Time.time in Play mode.
            lamp.intensity = baseIntensity * Factor(Time.time, instability);
        }

        void OnDisable()
        {
            if (lamp != null) lamp.intensity = baseIntensity;
        }

        // Keep Hash, Noise and Surge identical to ReactorHash, ReactorNoise and ReactorSurge in
        // Assets/FPS/Art/Shaders/CoreBuster/ReactorContainment.shader.
        static float Hash(float n)
        {
            n = Frac(n * .1031f);
            n *= n + 33.33f;
            n *= n + n;
            return Frac(n);
        }

        static float Noise(float x)
        {
            float i = Mathf.Floor(x);
            float f = x - i;
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash(i), Hash(i + 1f), f);
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Brief event envelope inside one slot: a fast rise and a short decay.</summary>
        public static float EventPulse(float time) =>
            SmoothStep(0f, .06f, Frac(time * EventSlotsPerSecond)) * (1f - SmoothStep(.1f, .42f, Frac(time * EventSlotsPerSecond)));

        /// <summary>1 when idle; irregular surges, brief dropouts and occasional hot flashes around it.</summary>
        public static float Surge(float time, float instability)
        {
            float slow = Noise(time * .83f);
            float fast = Noise(time * 3.7f + 19f);
            float roll = Hash(Mathf.Floor(time * EventSlotsPerSecond) + 71f);
            float previous = Hash(Mathf.Floor(time * EventSlotsPerSecond) + 70f);
            float pulse = EventPulse(time);
            // Never two flash slots in a row, so any one second holds at most two flashes.
            float flash = roll > .8f && previous <= .8f ? pulse : 0f;
            float dropout = roll < .14f ? pulse : 0f;
            return Mathf.Max(.1f, 1f + instability * ((slow - .5f) * .8f + (fast - .5f) * .3f + flash * 1.5f - dropout * .6f));
        }
    }
}
