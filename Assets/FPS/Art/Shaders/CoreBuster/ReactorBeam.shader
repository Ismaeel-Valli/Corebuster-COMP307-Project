// The energy beam the Boss Hall reactor fires up into the underside of the ship's core (corebuster_game_002).
// Drawn on an open tube whose UV.y runs from the reactor (0) to the core (1); ReactorBeam sets _Length in metres so
// the pattern keeps its scale. The look is built entirely here:
//  - a white-hot centre line where the tube's surface faces the viewer, falling off to a coloured sheath;
//  - electric filaments: domain-warped value noise stretched along the beam and scrolled upward;
//  - bright pulses climbing the beam, and an irregular flicker;
//  - _Overcharge (0..1, a console's surge) thickens the core, speeds and multiplies everything and shifts the
//    colour toward white-violet.
Shader "CoreBuster/Reactor Beam"
{
    Properties
    {
        [HDR] _Color ("Sheath Colour", Color) = (0.15, 0.45, 1.6, 1)
        [HDR] _HotColor ("Core Colour", Color) = (1.4, 1.9, 3.0, 1)
        [HDR] _OverchargeColor ("Overcharge Colour", Color) = (3.0, 1.6, 4.0, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.6
        _CoreSharpness ("Core Sharpness", Range(0.5, 16)) = 5
        _FilamentScale ("Filament Scale", Float) = 1.4
        _FilamentSpeed ("Filament Speed", Float) = 3.5
        _PulseSpacing ("Pulse Spacing (m)", Float) = 2.4
        _PulseSpeed ("Pulse Speed (m/s)", Float) = 6
        _Length ("Length (m, set at runtime)", Float) = 4
        _Overcharge ("Overcharge", Range(0, 1)) = 0
        _EndFade ("End Fade", Range(0.001, 0.5)) = 0.06
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True" }

        Pass
        {
            Name "ReactorBeam"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _HotColor;
                half4 _OverchargeColor;
                half _Intensity;
                half _CoreSharpness;
                float _FilamentScale;
                float _FilamentSpeed;
                float _PulseSpacing;
                float _PulseSpeed;
                float _Length;
                half _Overcharge;
                half _EndFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Value noise, wrapping around the tube (x has period `wrap`).
            float Noise(float2 p, float wrap)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(float2(fmod(i.x, wrap), i.y));
                float b = Hash(float2(fmod(i.x + 1.0, wrap), i.y));
                float c = Hash(float2(fmod(i.x, wrap), i.y + 1.0));
                float d = Hash(float2(fmod(i.x + 1.0, wrap), i.y + 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs positions = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = positions.positionCS;
                o.positionWS = positions.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                half over = _Overcharge;
                float along = i.uv.y * _Length;

                // Facing ratio: 1 through the middle of the beam's silhouette, 0 at its edges.
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half facing = abs(dot(normalize(i.normalWS), view));
                half core = pow(facing, lerp(_CoreSharpness, _CoreSharpness * 0.35, over));
                half sheath = pow(facing, 1.2);

                // Filaments: noise warped by noise, stretched along the beam and rising.
                float speed = _FilamentSpeed * (1.0 + 2.5 * over);
                const float wrap = 8.0;
                float2 p = float2(i.uv.x * wrap, along * _FilamentScale - t * speed);
                float warp = Noise(p * float2(1.0, 0.5) + float2(0.0, t * 0.7), wrap);
                float filament = Noise(float2(p.x + warp * 3.0, p.y * 0.6), wrap);
                filament = pow(saturate(1.0 - abs(filament * 2.0 - 1.0) * 3.2), 3.0);

                // Pulses climbing from the reactor into the core.
                float spacing = max(0.2, _PulseSpacing * (1.0 - 0.6 * over));
                float phase = frac((along - t * _PulseSpeed * (1.0 + 1.5 * over)) / spacing);
                half pulse = smoothstep(0.75, 1.0, phase) * smoothstep(1.0, 0.92, phase) * 4.0;

                half flicker = 0.82 + 0.18 * Noise(float2(t * 23.0, 3.7), 1000.0) + over * 0.6 * Noise(float2(t * 61.0, 9.1), 1000.0);
                half ends = smoothstep(0.0, _EndFade, i.uv.y) * smoothstep(1.0, 1.0 - _EndFade, i.uv.y);

                half3 sheathColor = lerp(_Color.rgb, _OverchargeColor.rgb, over * 0.6);
                half3 hotColor = lerp(_HotColor.rgb, _OverchargeColor.rgb, over);
                half3 color = sheathColor * sheath * (0.35 + filament * 1.4 + pulse * 0.5)
                            + hotColor * core * (1.0 + pulse);
                return half4(color * _Intensity * (1.0 + 2.2 * over) * flicker * ends, 1.0);
            }
            ENDHLSL
        }
    }
}
