using UnityEngine;

namespace Unity.FPS.Gameplay
{
    /// <summary>Colours the emissive status lamps (and their light) on doors, terminals and access machines.</summary>
    public static class StatusLamps
    {
        static readonly int k_Emission = Shader.PropertyToID("_EmissionColor");
        static readonly int k_Base = Shader.PropertyToID("_BaseColor");
        static MaterialPropertyBlock s_Block;

        public static void Set(Renderer[] lamps, Light light, Color color, float glow, float lightIntensity = 1.2f)
        {
            if (lamps != null)
            {
                s_Block ??= new MaterialPropertyBlock();
                s_Block.SetColor(k_Base, color);
                s_Block.SetColor(k_Emission, color * glow);
                foreach (var lamp in lamps)
                    if (lamp) lamp.SetPropertyBlock(s_Block);
            }
            if (light)
            {
                light.color = color;
                light.intensity = lightIntensity * Mathf.Clamp01(glow / 4f + .25f);
            }
        }

        /// <summary>0..1 throb for alarms and attention cues.</summary>
        public static float Pulse(float speed) => .45f + .55f * Mathf.Abs(Mathf.Sin(Time.time * speed));
    }
}
