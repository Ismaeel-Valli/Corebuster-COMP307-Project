// Additive particle shader for the Unstable Core's embers, surges, shockwaves and absorption stream.
// Shapes are procedural (soft dot with a hot centre, or a thin ring) so no textures are needed.
// Particle colours are LDR, so _Intensity lifts them into HDR for bloom.
Shader "CoreBuster/Core Energy Additive"
{
    Properties
    {
        _TintColor ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("HDR Intensity", Float) = 3
        [Enum(Dot, 0, Ring, 1)] _Shape ("Shape", Float) = 0
        _RingWidth ("Ring Width", Range(0.01, 0.4)) = 0.1
        _HotCore ("White-Hot Centre", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent" "Queue"="Transparent+20" "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline" "PreviewType"="Plane"
        }

        Pass
        {
            Name "CoreEnergy"
            Tags { "LightMode"="UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float _Intensity, _Shape, _RingWidth, _HotCore;
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
                float r = length(IN.uv * 2.0 - 1.0);

                float dotShape = saturate(1.0 - r);
                dotShape *= dotShape;
                float ringShape = exp(-pow((r - (0.85 - _RingWidth)) / _RingWidth, 2.0)) * saturate((1.0 - r) * 12.0);
                float shape = lerp(dotShape, ringShape, _Shape);

                float3 color = IN.color.rgb * _TintColor.rgb * shape;
                // Bright particles burn toward white in the middle.
                color += pow(dotShape, 3.0) * _HotCore * (1.0 - _Shape);
                return float4(color * _Intensity * IN.color.a, 0);
            }
            ENDHLSL
        }
    }
}
