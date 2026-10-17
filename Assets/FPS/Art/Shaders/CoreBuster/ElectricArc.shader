// Additive electricity for LineRenderers and TrailRenderers: the Disassembler's laser bolts and the lightning
// that arcs from an electrocuted enemy to the ground.
// Expects the line's texture mode to be Stretch: uv.x runs along the line, uv.y across it. A white-hot core
// sits in a coloured glow, broken into flickering, travelling pulses so the arc never looks like a flat tube.
Shader "CoreBuster/Electric Arc"
{
    Properties
    {
        _Color ("Glow Colour", Color) = (0.25, 0.6, 1, 1)
        _Intensity ("HDR Intensity", Float) = 5
        _CoreWidth ("Core Width", Range(0.02, 0.5)) = 0.12
        _HotCore ("White-Hot Core", Range(0, 3)) = 1.2
        _Pulse ("Pulse Density", Float) = 6
        _PulseSpeed ("Pulse Speed", Float) = 14
        _Flicker ("Flicker", Range(0, 1)) = 0.6
        _TipFade ("Fade At Ends", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent" "Queue"="Transparent+25" "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline" "PreviewType"="Plane"
        }

        Pass
        {
            Name "ElectricArc"
            Tags { "LightMode"="UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity, _CoreWidth, _HotCore, _Pulse, _PulseSpeed, _Flicker, _TipFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            float Noise1(float x)
            {
                float i = floor(x);
                float f = frac(x);
                return lerp(Hash(i), Hash(i + 1.0), f * f * (3.0 - 2.0 * f));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float along = IN.uv.x;
                float across = abs(IN.uv.y * 2.0 - 1.0);
                float time = _Time.y;

                // A thin core that wanders slightly off the line's centre, inside a wider glow.
                float wander = (Noise1(along * 9.0 + time * 23.0) - 0.5) * 0.35;
                float offset = abs(across - wander * (1.0 - across));
                float core = exp(-pow(offset / _CoreWidth, 2.0));
                float glow = pow(saturate(1.0 - across), 2.0);

                // Bright packets travelling along the arc, and a fast strobe over the whole thing.
                float pulses = 0.55 + 0.45 * pow(Noise1(along * _Pulse - time * _PulseSpeed), 2.0) * 2.0;
                float strobe = lerp(1.0, 0.35 + 1.3 * Noise1(time * 45.0 + IN.color.a * 17.0), _Flicker);
                float ends = smoothstep(0.0, _TipFade, along) * smoothstep(0.0, _TipFade, 1.0 - along);

                float3 tint = _Color.rgb * IN.color.rgb;
                float3 color = tint * (glow * 0.6 + core) + core * core * _HotCore;
                return float4(color * pulses * strobe * ends * _Intensity * IN.color.a, 0.0);
            }
            ENDHLSL
        }
    }
}
