// The heavy power cables running from the Boss Hall consoles into the reactor (corebuster_game_002).
// A lit (URP PBR) rubber-and-steel sheath with three glowing conductor lines along it. UV.x is the distance along the
// cable in metres (0 at the console, _Length at the reactor) and UV.y goes once around it, so:
//  - ribbing: steel collars every _RibSpacing metres, darker and shinier than the rubber;
//  - idle flow: dim dashes drift along the conductors toward the reactor while the console waits;
//  - surge: when a console is overcharged, PowerCable moves _Flow from 0 to 1 and a long, bright slug of energy
//    travels down the cable into the reactor, lighting the whole sheath from inside as it passes;
//  - _Charge brightens the conductors (a console ready to take cores).
Shader "CoreBuster/Power Cable"
{
    Properties
    {
        _BaseColor ("Sheath", Color) = (0.07, 0.075, 0.085, 1)
        _CollarColor ("Collars", Color) = (0.32, 0.33, 0.35, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        [HDR] _GlowColor ("Energy Colour", Color) = (0.3, 0.75, 2.4, 1)
        [HDR] _SurgeColor ("Surge Colour", Color) = (2.6, 1.4, 4.0, 1)
        _Conductors ("Conductor Lines", Float) = 3
        _LineWidth ("Line Width", Range(0.005, 0.2)) = 0.05
        _RibSpacing ("Collar Spacing (m)", Float) = 0.9
        _DashSpacing ("Idle Dash Spacing (m)", Float) = 1.6
        _DashSpeed ("Idle Dash Speed (m/s)", Float) = 1.2
        _Idle ("Idle Glow", Range(0, 2)) = 0.35
        _Charge ("Charge", Range(0, 1)) = 0
        _Length ("Length (m, set at runtime)", Float) = 8
        _Flow ("Surge Position (0..1, <0 none)", Float) = -1
        _FlowLength ("Surge Length (m)", Float) = 2.2
        _FlowIntensity ("Surge Intensity", Float) = 6
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _CollarColor;
            half _Smoothness;
            half4 _GlowColor;
            half4 _SurgeColor;
            float _Conductors;
            half _LineWidth;
            float _RibSpacing;
            float _DashSpacing;
            float _DashSpeed;
            half _Idle;
            half _Charge;
            float _Length;
            float _Flow;
            float _FlowLength;
            half _FlowIntensity;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs positions = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = positions.positionCS;
                o.positionWS = positions.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float along = i.uv.x;
                float around = i.uv.y;

                // Steel collars every _RibSpacing metres.
                float rib = abs(frac(along / _RibSpacing) - 0.5) * 2.0;
                half collar = smoothstep(0.86, 0.9, rib);
                half3 albedo = lerp(_BaseColor.rgb, _CollarColor.rgb, collar);
                half metallic = collar * 0.8;
                half smoothness = lerp(_Smoothness, 0.6, collar);

                // Conductor lines around the sheath, hidden under the collars.
                float lane = abs(frac(around * _Conductors) - 0.5) * 2.0;
                half conductor = smoothstep(1.0 - _LineWidth * 2.0, 1.0 - _LineWidth * 0.5, 1.0 - lane) * (1.0 - collar);

                // Idle dashes drifting toward the reactor.
                float dashPhase = frac((along - _Time.y * _DashSpeed) / _DashSpacing);
                half dash = smoothstep(0.55, 0.75, dashPhase) * smoothstep(1.0, 0.8, dashPhase);
                half idle = _Idle * (0.35 + 0.65 * dash) * (1.0 + 2.5 * _Charge);

                // The surge: a bright slug with a sharp head and a long tail, lighting the whole cable.
                half surge = 0.0;
                if (_Flow >= 0.0)
                {
                    float head = _Flow * (_Length + _FlowLength);
                    float behind = head - along;
                    surge = behind >= 0.0 ? exp(-behind / max(0.05, _FlowLength * 0.45)) * smoothstep(0.0, 0.15, behind + 0.15)
                                          : exp(behind * 8.0);
                }
                half flicker = 0.85 + 0.15 * sin(_Time.y * 47.0 + along * 3.0);
                half3 energy = lerp(_GlowColor.rgb, _SurgeColor.rgb, saturate(surge));
                half3 emission = energy * conductor * (idle + surge * _FlowIntensity * flicker)
                               + _SurgeColor.rgb * surge * 0.35 * (1.0 - collar * 0.5);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = normalize(i.normalWS);
                inputData.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = metallic;
                surface.smoothness = smoothness;
                surface.occlusion = 1.0;
                surface.emission = emission;
                surface.alpha = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, i.fogFactor);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 ShadowVert(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };

            float4 DepthVert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings NormalsVert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 NormalsFrag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
}
