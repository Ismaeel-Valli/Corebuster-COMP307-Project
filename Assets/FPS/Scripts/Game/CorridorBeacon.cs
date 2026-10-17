using UnityEngine;

namespace Unity.FPS.Game
{
    /// <summary>
    /// A corridor lamp during the lockdown. Lamps on a route shimmer in a keycard's colour, marking the way to the
    /// door that card opens; their light is a separate, mostly grey source with only a hint of that colour, so the
    /// corridor stays dark and the colour reads as a subtle cue rather than a wash. Lamps off every route keep the
    /// fitting's red emergency glow and red light, flickering slightly. The corridors' caged lamps have a second light,
    /// the wash onto their wall, which wavers with the first. Set up by Tools/CoreBuster/Build Corridor Lockdown.
    /// </summary>
    public class CorridorBeacon : MonoBehaviour
    {
        public Renderer Globe;
        public Renderer Bulb;
        public Light Light;
        [Tooltip("Optional second light that wavers with the first (the caged lamp's wash onto its wall).")]
        public Light WashLight;

        [Tooltip("The globe's and bulb's glow colour (a keycard's tint, or emergency red off the routes).")]
        public Color GlowColor = Color.white;
        [Min(0f)] public float GlowIntensity = 3f;
        [Tooltip("The bulb's glow; it burns hotter than the glass around it.")]
        [Min(0f)] public float BulbIntensity = 7f;
        [Tooltip("The globe glass's own colour.")]
        public Color GlassColor = new Color(.5f, .5f, .5f, .55f);
        [Tooltip("How far the glow dips while shimmering (0 = steady).")]
        [Range(0f, 1f)] public float Shimmer = .35f;
        [Min(0f)] public float ShimmerSpeed = 1.6f;
        [Tooltip("The light's intensity; it wavers with the glow, but much less.")]
        [Min(0f)] public float LightIntensity = 3f;
        [Min(0f)] public float WashIntensity = 1f;

        static readonly int k_Emission = Shader.PropertyToID("_EmissionColor");
        static readonly int k_Base = Shader.PropertyToID("_BaseColor");
        static MaterialPropertyBlock s_Block;

        float m_Phase;

        void Awake()
        {
            // Lamps on the same corridor drift in and out of step instead of pulsing together.
            var p = transform.position;
            m_Phase = Mathf.Repeat(p.x * .37f + p.z * .61f, 100f);
            Apply(1f);
        }

        // Shows the colours in the editor too (property blocks are not saved with the scene).
        void OnValidate() => Apply(1f);

        void Update()
        {
            if (Shimmer <= 0f) return;
            // Only work for lamps something can see.
            bool visible = (Globe && Globe.isVisible) || (Bulb && Bulb.isVisible);
            float t = Time.time * ShimmerSpeed + m_Phase;
            float wave = .5f + .5f * Mathf.Sin(t) * Mathf.Sin(t * .53f + 1.7f);
            float flicker = Mathf.PerlinNoise(t * 3.1f, m_Phase) * .25f;
            float level = 1f - Shimmer * Mathf.Clamp01(wave * .8f + flicker);
            float waver = 1f - .12f * (1f - level);
            if (Light) Light.intensity = LightIntensity * waver;
            if (WashLight) WashLight.intensity = WashIntensity * waver;
            if (visible) Apply(level);
        }

        void Apply(float level)
        {
            s_Block ??= new MaterialPropertyBlock();
            if (Globe)
            {
                Globe.GetPropertyBlock(s_Block);
                s_Block.SetColor(k_Base, GlassColor);
                s_Block.SetColor(k_Emission, GlowColor * (GlowIntensity * level));
                Globe.SetPropertyBlock(s_Block);
            }
            if (Bulb)
            {
                Bulb.GetPropertyBlock(s_Block);
                s_Block.SetColor(k_Base, Color.Lerp(GlowColor, Color.white, .3f));
                s_Block.SetColor(k_Emission, Color.Lerp(GlowColor, Color.white, .25f) * (BulbIntensity * level));
                Bulb.SetPropertyBlock(s_Block);
            }
        }
    }
}
