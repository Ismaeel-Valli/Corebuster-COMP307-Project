// The Unstable Core itself: a 3D energy orb whose surface wobbles and spikes as it destabilises.
// Shading comes from CoreOrbCommon.hlsl, shared with the galaxy body that shows the orb inside the chest.
// UnstableCore.cs drives _CoreTime, _Instability, _Surge and _Collapse every frame.
Shader "CoreBuster/Unstable Orb"
{
    Properties
    {
        _Intensity ("HDR Intensity", Float) = 2.2
        _Wobble ("Surface Wobble", Range(0, 0.3)) = 0.08

        [Header(Driven By Script)]
        _CoreTime ("Core Time", Float) = 0
        _Instability ("Instability", Range(0, 1)) = 0.5
        _Surge ("Surge", Range(0, 1)) = 0
        _Collapse ("Collapse (absorption)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "UnstableOrb"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/FPS/Art/Shaders/CoreBuster/CoreOrbCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity, _Wobble;
                float _CoreTime, _Instability, _Surge, _Collapse;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // Bulge the surface with moving 3D noise; surges spike it outward.
                float wobble = OrbFbm(IN.positionOS.xyz * 4.0 + _CoreTime * 0.8) - 0.5;
                float spike = pow(OrbNoise(IN.positionOS.xyz * 9.0 + _CoreTime * 2.0), 6.0);
                float amount = _Wobble * (wobble * (0.4 + _Instability) + spike * _Surge * 3.0) * (1.0 - _Collapse * 0.5);
                float3 displaced = IN.positionOS.xyz + IN.normalOS * amount;

                VertexPositionInputs positions = GetVertexPositionInputs(displaced);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 normal = normalize(IN.normalWS);
                float3 viewDir = normalize(GetCameraPositionWS() - IN.positionWS);
                float3 color = ShadeCoreOrb(normal, viewDir, _CoreTime, _Instability, _Surge, _Collapse);
                return float4(color * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
