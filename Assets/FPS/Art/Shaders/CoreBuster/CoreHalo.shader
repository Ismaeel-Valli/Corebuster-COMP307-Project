// Blue haze and faint rays around the Unstable Core orb, on a camera-facing quad centred on it.
// The orb's opaque surface hides the middle; outside it the haze fades off with rays streaming away.
// Only shown once the orb is out of the body; inside the chest the galaxy shader draws the haze instead.
Shader "CoreBuster/Core Halo"
{
    Properties
    {
        _Intensity ("HDR Intensity", Float) = 1.6
        _OrbFraction ("Orb Radius (fraction of quad half-size)", Range(0.05, 0.9)) = 0.3

        [Header(Driven By Script)]
        _CoreTime ("Core Time", Float) = 0
        _Surge ("Surge", Range(0, 1)) = 0
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline" "PreviewType"="Plane"
        }

        Pass
        {
            Name "CoreHalo"
            Tags { "LightMode"="UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/FPS/Art/Shaders/CoreBuster/CoreOrbCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity, _OrbFraction;
                float _CoreTime, _Surge, _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 p : TEXCOORD0; // -1..1 across the quad
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // Billboard: expand the quad around the object's origin in view space, sized by its X scale.
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float scale = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float3 positionVS = TransformWorldToView(centerWS) + float3(IN.positionOS.xy * scale, 0);
                OUT.positionCS = TransformWViewToHClip(positionVS);
                OUT.p = IN.positionOS.xy * 2.0;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float r = length(IN.p);
                float angle = atan2(IN.p.y, IN.p.x);
                float3 haze = CoreOrbHaze(r / _OrbFraction, angle, _CoreTime, _Surge);
                // Fade out before the quad's edge.
                haze *= saturate((1.0 - r) * 2.5);
                return float4(haze * _Intensity * _Fade, 0);
            }
            ENDHLSL
        }
    }
}
