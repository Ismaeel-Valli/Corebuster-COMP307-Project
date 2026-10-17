using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>Scaled-time, visual-only motion for the Start Workshop's ambient pass.</summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopAmbientMotion : MonoBehaviour
    {
        public enum MotionKind { Fan, Lamp, Led }

        [SerializeField] MotionKind kind;
        [SerializeField] float speed = 90f;
        [SerializeField] float phase;
        [SerializeField] float minimum = .45f;
        [SerializeField] float maximum = 1f;

        [SerializeField] Transform movingPart;
        [SerializeField] Light lamp;
        [SerializeField] Renderer display;
        MaterialPropertyBlock block;
        Color baseEmission;
        float baseIntensity;
        float elapsed;

        public void ConfigureFan(Transform rotor, float degreesPerSecond, float phaseOffset)
        {
            kind = MotionKind.Fan;
            movingPart = rotor;
            speed = degreesPerSecond;
            phase = phaseOffset;
        }

        public void ConfigureLamp(Light target, float phaseOffset)
        {
            kind = MotionKind.Lamp;
            lamp = target;
            phase = phaseOffset;
        }

        public void ConfigureLed(Renderer target, float phaseOffset)
        {
            kind = MotionKind.Led;
            display = target;
            phase = phaseOffset;
        }

        void Awake()
        {
            if (kind == MotionKind.Fan && movingPart == null)
                movingPart = transform.Find("LP_vent_fan_snaps");
            if (kind == MotionKind.Lamp && lamp == null)
                lamp = GetComponent<Light>();
            if (kind == MotionKind.Led && display == null)
                display = GetComponent<Renderer>();
            if (lamp != null) baseIntensity = lamp.intensity;
            if (display != null && display.sharedMaterial != null)
            {
                baseEmission = display.sharedMaterial.GetColor("_EmissionColor");
                block = new MaterialPropertyBlock();
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            elapsed += dt;
            if (kind == MotionKind.Fan)
            {
                // The kit wall-fan mesh faces along its local X axis.
                if (movingPart != null) movingPart.Rotate(Vector3.right, speed * dt, Space.Self);
                return;
            }

            // No unscaled time, random state, allocations, material mutation or physics calls.
            float wave = kind == MotionKind.Lamp
                ? .5f + .32f * Mathf.Sin(elapsed * 3.1f + phase) + .18f * Mathf.Sin(elapsed * 8.7f + phase * 1.7f)
                : .5f + .5f * Mathf.Sin(elapsed * 2.4f + phase);
            float factor = Mathf.Lerp(minimum, maximum, Mathf.Clamp01(wave));
            if (kind == MotionKind.Lamp && lamp != null) lamp.intensity = baseIntensity * factor;
            if (kind == MotionKind.Led && display != null)
            {
                display.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", baseEmission * factor);
                display.SetPropertyBlock(block);
            }
        }

        void OnDisable()
        {
            if (lamp != null) lamp.intensity = baseIntensity;
            if (display != null && block != null)
            {
                display.GetPropertyBlock(block);
                block.SetColor("_EmissionColor", baseEmission);
                display.SetPropertyBlock(block);
            }
        }
    }
}
