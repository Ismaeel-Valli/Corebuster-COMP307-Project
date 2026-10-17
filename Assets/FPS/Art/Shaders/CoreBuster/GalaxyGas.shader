// Red galaxy gas: each particle is a billowing puff of crimson and magenta nebula with twinkling stars.
// Needs the particle vertex streams Position, Normal, Color, UV and StableRandom.x (the per-puff seed in TEXCOORD0.z).
// Colour is written premultiplied so the smoke alpha-blends while the stars and hot folds add light for bloom.
Shader "CoreBuster/Galaxy Gas"
{
    Properties
    {
        _DeepColor ("Deep Crimson", Color) = (0.16, 0.01, 0.035, 1)
        _MidColor ("Red Nebula", Color) = (0.78, 0.06, 0.09, 1)
        _AccentColor ("Magenta Streaks", Color) = (0.62, 0.07, 0.42, 1)
        _HotColor ("Hot Folds", Color) = (1, 0.42, 0.22, 1)
        _StarColor ("Stars", Color) = (1, 0.86, 0.9, 1)
        _Emission ("Self Illumination", Float) = 1.3
        _Density ("Density", Range(0, 2)) = 0.85
        _NoiseScale ("Billow Scale", Float) = 2.4
        _FlowSpeed ("Billow Speed", Float) = 0.12
        _StarDensity ("Star Density", Float) = 14
        _Fade ("Fade (driven by script)", Range(0, 1)) = 1
        _GroundHeight ("Floor Height (driven by script)", Float) = -100000
        _GroundFade ("Floor Fade Height", Float) = 1.1
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline" "PreviewType"="Plane"
        }

        Pass
        {
            Name "GalaxyGas"
            Tags { "LightMode"="UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _MidColor, _AccentColor, _HotColor, _StarColor;
                float _Emission, _Density, _NoiseScale, _FlowSpeed, _StarDensity, _Fade;
                float _GroundHeight, _GroundFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float3 uv : TEXCOORD0; // xy = quad UV, z = stable random per particle
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float3 uv : TEXCOORD0;
                float heightWS : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                OUT.heightWS = TransformObjectToWorld(IN.positionOS.xyz).y;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x), u.y);
            }

            float fbm(float2 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    sum += valueNoise(p) * amplitude;
                    p = p * 2.03 + 11.7;
                    amplitude *= 0.5;
                }
                return sum / 0.9375;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float seed = IN.uv.z * 97.0;
                float2 p = IN.uv.xy * 2.0 - 1.0;
                float r = length(p);

                // Two drifting noise layers: one shapes the puff's silhouette, one folds the gas inside it.
                float2 flow = float2(t * _FlowSpeed, -t * _FlowSpeed * 0.7);
                float billow = fbm(p * _NoiseScale + seed + flow);
                float folds = fbm(p * _NoiseScale * 2.2 - seed * 1.3 + flow * 1.6);

                float edge = 0.62 + 0.38 * billow;
                float shape = smoothstep(0.0, 0.55, saturate(1.0 - r / edge));
                float density = saturate(shape * (0.5 + 0.6 * folds) * _Density);

                float3 color = lerp(_DeepColor.rgb, _MidColor.rgb, saturate(billow * 1.4 - 0.25));
                color = lerp(color, _AccentColor.rgb, smoothstep(0.55, 0.85, folds) * 0.65);
                color += _HotColor.rgb * pow(saturate(shape * folds * 1.3), 3.0);
                color *= IN.color.rgb * _Emission;

                // Stars caught in the gas.
                float2 starCell = (IN.uv.xy + seed) * _StarDensity;
                float starHash = hash21(floor(starCell));
                float starShape = smoothstep(0.22, 0.0, length(frac(starCell) - 0.5));
                float twinkle = 0.5 + 0.5 * sin(t * 4.0 + starHash * 60.0);
                float star = step(0.955, starHash) * starShape * twinkle * shape;

                // No depth texture for soft particles, so thin the gas out toward the floor it lies on
                // instead of letting the puffs slice into it with a hard edge.
                float floorFade = smoothstep(_GroundHeight - 0.05, _GroundHeight + _GroundFade, IN.heightWS);
                float alpha = density * IN.color.a * _Fade * floorFade;
                float3 result = color * alpha + _StarColor.rgb * star * 3.0 * IN.color.a * _Fade * floorFade;
                return float4(result, alpha);
            }
            ENDHLSL
        }
    }
}
