// Guide chevrons painted in light on the floor (corebuster_game_003). Each route is one mesh of chevron strokes;
// UV.x is the distance along the route in metres and UV.y runs across a stroke (0..1), so the shader can:
//  - soften each stroke's edges, like light spilling onto the deck;
//  - send bright pulses down the route toward its goal, so the arrows read as flowing even at a glance;
//  - flicker faintly, in keeping with the ship's failing power;
//  - fade the whole route in and out (_Fade, driven by FloorGuide as objectives change).
// Additive, so the arrows glow in the dark corridors and never darken what is under them.
Shader "CoreBuster/Floor Guide"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.82, 0.2, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.6
        _Base ("Resting Glow", Range(0, 1)) = 0.3
        _PulseSpacing ("Pulse Spacing (m)", Float) = 7
        _PulseSpeed ("Pulse Speed (m/s)", Float) = 5
        _PulseWidth ("Pulse Width (m)", Float) = 2.5
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-50" "IgnoreProjector"="True" }

        Pass
        {
            Name "FloorGuide"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _Base;
                float _PulseSpacing;
                float _PulseSpeed;
                float _PulseWidth;
                half _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float along = i.uv.x;
                half across = i.uv.y;

                // Soft stroke edges with a brighter spine.
                half edge = smoothstep(0.0, 0.3, across) * smoothstep(1.0, 0.7, across);
                half spine = 1.0 - abs(across * 2.0 - 1.0);

                // Pulses travel toward the goal: a sharp leading edge and a fading tail.
                // tail = distance behind the nearest pulse head (0 at the head, growing back toward the start).
                float phase = frac((along - _Time.y * _PulseSpeed) / _PulseSpacing) * _PulseSpacing;
                float tail = _PulseSpacing - phase;
                half pulse = exp(-tail / max(0.1, _PulseWidth * 0.5)) * smoothstep(_PulseSpacing, _PulseSpacing - 0.3, tail);

                half flicker = 0.92 + 0.08 * sin(_Time.y * 31.0 + along * 0.7);
                half glow = (_Base + pulse * (1.0 - _Base) * 1.6) * (0.75 + 0.25 * spine);
                return half4(_Color.rgb * glow * edge * flicker * _Intensity * _Fade, 1.0);
            }
            ENDHLSL
        }
    }
}
