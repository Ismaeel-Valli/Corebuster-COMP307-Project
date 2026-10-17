Shader "Custom/GalaxyHuskShaderURP_Textured"
{
    Properties
    {
        [Header(Nebula Textures)]
        _NebulaTexA ("Nebula Texture A", 2D) = "black" {}
        _NebulaTexB ("Nebula Texture B (blended with A for shifting look)", 2D) = "black" {}
        _BlendSpeed ("Cross-fade Speed Between A and B", Float) = 0.05
        _ScrollSpeedA ("Scroll Speed - Texture A", Vector) = (0.01, 0.005, 0, 0)
        _ScrollSpeedB ("Scroll Speed - Texture B", Vector) = (-0.007, 0.012, 0, 0)

        [Header(Triplanar Projection)]
        _TriplanarScale ("Triplanar Tiling Scale (compensate for mesh units, e.g. cm)", Float) = 0.01
        _TriplanarBlendSharpness ("Triplanar Blend Sharpness", Range(1, 16)) = 4.0

        [Header(Stars Overlay)]
        _StarScale ("Star Density Scale", Float) = 4.0
        _StarThreshold ("Star Threshold (higher = fewer)", Range(0.9, 0.999)) = 0.99
        _TwinkleSpeed ("Twinkle Speed", Float) = 2.0
        _StarColor ("Star Color", Color) = (1, 1, 1, 1)

        [Header(Fresnel Shift)]
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FresnelColorA ("Fresnel Color A", Color) = (0.4, 0.6, 1, 1)
        _FresnelColorB ("Fresnel Color B", Color) = (1, 0.4, 0.7, 1)
        _FresnelShiftSpeed ("Fresnel Shift Speed", Float) = 0.5

        [Header(Emissive Core Chest)]
        _CoreUV ("Core Position (UV coords)", Vector) = (0.336, 0.285, 0, 0)
        _CoreRadius ("Core Radius", Range(0.01, 0.5)) = 0.1
        _CoreColor ("Core Glow Color", Color) = (0.6, 0.85, 1, 1)
        _CoreIntensity ("Core Base Intensity", Float) = 3.0
        _PulseSpeed ("Pulse Speed", Float) = 1.2

        [Header(Self Illumination)]
        _MinBrightness ("Minimum Brightness Floor (self-lit, dark scenes)", Range(0, 2)) = 0.35
        _NebulaBrightness ("Nebula Texture Brightness Multiplier", Range(0.1, 4)) = 1.0

        [Header(Critical Overload)]
        _HeatColor ("Overload Color", Color) = (1, 0.08, 0.06, 1)
        _Heat ("Overload Tint (driven by script)", Range(0, 1)) = 0
        _HeatGlow ("Overload Glow (driven by script)", Range(0, 4)) = 0

        [Header(Unstable Core Inside The Body)]
        _OrbExposure ("Core Visibility (driven by script)", Range(0, 1)) = 0
        _OrbCenter ("Core Centre, world (driven by script)", Vector) = (0, 0, 0, 0)
        _OrbRadius ("Core Radius, world (driven by script)", Float) = 0.08
        _OrbIntensity ("Core HDR Intensity", Float) = 2.2
        _OrbTime ("Core Time (driven by script)", Float) = 0
        _OrbInstability ("Core Instability (driven by script)", Range(0, 1)) = 0.5
        _OrbSurge ("Core Surge (driven by script)", Range(0, 1)) = 0
        _OrbCollapse ("Core Collapse (driven by script)", Range(0, 1)) = 0
        _OrbVeil ("Galaxy Veil Over The Core", Range(0, 1)) = 0.35
        _OrbWindow ("Parallax Window Size (core radii)", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/FPS/Art/Shaders/CoreBuster/CoreOrbCommon.hlsl"

            TEXTURE2D(_NebulaTexA); SAMPLER(sampler_NebulaTexA);
            TEXTURE2D(_NebulaTexB); SAMPLER(sampler_NebulaTexB);

            CBUFFER_START(UnityPerMaterial)
                float4 _NebulaTexA_ST;
                float4 _NebulaTexB_ST;
                float _BlendSpeed;
                float4 _ScrollSpeedA;
                float4 _ScrollSpeedB;
                float _TriplanarScale, _TriplanarBlendSharpness;
                float _StarScale, _StarThreshold, _TwinkleSpeed;
                float4 _StarColor;
                float _FresnelPower, _FresnelShiftSpeed;
                float4 _FresnelColorA, _FresnelColorB;
                float4 _CoreUV;
                float _CoreRadius, _CoreIntensity, _PulseSpeed;
                float4 _CoreColor;
                float _MinBrightness, _NebulaBrightness;
                float4 _HeatColor;
                float _Heat, _HeatGlow;
                float4 _OrbCenter;
                float _OrbExposure, _OrbRadius, _OrbIntensity, _OrbTime, _OrbInstability, _OrbSurge, _OrbCollapse;
                float _OrbVeil, _OrbWindow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 normalOS   : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS = IN.normalOS;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Samples a texture using triplanar projection from object-space position/normal,
            // so it wraps cleanly around the body regardless of the mesh's actual UV layout.
            float3 triplanarSample(TEXTURE2D_PARAM(tex, samp), float3 posOS, float3 normalOS, float scale, float2 scrollOffset, float blendSharpness)
            {
                float3 blendWeights = pow(abs(normalOS), blendSharpness);
                blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z + 1e-5);

                float2 uvX = posOS.zy * scale + scrollOffset;
                float2 uvY = posOS.xz * scale + scrollOffset;
                float2 uvZ = posOS.xy * scale + scrollOffset;

                float3 colX = SAMPLE_TEXTURE2D(tex, samp, uvX).rgb;
                float3 colY = SAMPLE_TEXTURE2D(tex, samp, uvY).rgb;
                float3 colZ = SAMPLE_TEXTURE2D(tex, samp, uvZ).rgb;

                return colX * blendWeights.x + colY * blendWeights.y + colZ * blendWeights.z;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float3 normalOS = normalize(IN.normalOS);

                // --- Sample two nebula textures via triplanar projection, each scrolling ---
                float3 nebulaA = triplanarSample(TEXTURE2D_ARGS(_NebulaTexA, sampler_NebulaTexA),
                                                   IN.positionOS, normalOS, _TriplanarScale,
                                                   t * _ScrollSpeedA.xy, _TriplanarBlendSharpness);
                float3 nebulaB = triplanarSample(TEXTURE2D_ARGS(_NebulaTexB, sampler_NebulaTexB),
                                                   IN.positionOS, normalOS, _TriplanarScale,
                                                   t * _ScrollSpeedB.xy, _TriplanarBlendSharpness);

                // Slowly cross-fade between the two so the pattern evolves over time
                float blend = 0.5 + 0.5 * sin(t * _BlendSpeed);
                float3 nebula = lerp(nebulaA, nebulaB, blend) * _NebulaBrightness;

                // --- Stars overlay, independent of the texture (in case the photo has none) ---
                float2 posCoord = float2(IN.positionOS.x + IN.positionOS.z, IN.positionOS.y) * _TriplanarScale;
                float2 sUV = posCoord * _StarScale;
                float starN = hash21(floor(sUV));
                float twinkle = 0.5 + 0.5 * sin(t * _TwinkleSpeed + starN * 6.2831);
                float star = step(_StarThreshold, starN) * twinkle;

                float3 baseColor = nebula + star * _StarColor.rgb;

                // --- Fresnel rim, shifting color over time ---
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 normalWS = normalize(IN.normalWS);
                float fres = pow(1.0 - saturate(dot(viewDirWS, normalWS)), _FresnelPower);
                float shift = 0.5 + 0.5 * sin(t * _FresnelShiftSpeed);
                float3 fresnelColor = lerp(_FresnelColorA.rgb, _FresnelColorB.rgb, shift);
                baseColor += fres * fresnelColor;

                // --- Emissive pulsing core (chest), still driven by UV since it needs a fixed spot ---
                float distToCore = distance(IN.uv, _CoreUV.xy);
                float coreMask = saturate(1.0 - distToCore / _CoreRadius);
                coreMask = pow(coreMask, 2.0);
                float pulse = _CoreIntensity + sin(t * _PulseSpeed) * (_CoreIntensity * 0.4);
                float3 coreGlow = _CoreColor.rgb * coreMask * pulse;

                float3 emission = coreGlow + (star * _StarColor.rgb * 0.6) + (fres * fresnelColor * 0.5);

                // --- Lighting: self-lit floor keeps nebula visible regardless of scene lighting,
                // but as a FLOOR (max) rather than additive, so it doesn't stack with real light
                // and wash out the texture's natural contrast ---
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 realLight = (mainLight.color * NdotL) + SampleSH(normalWS);
                float3 lightAmount = max(realLight, _MinBrightness);
                float3 litBase = baseColor * lightAmount;

                float3 finalColor = litBase + emission;

                // --- Critical overload: a dying body burns red before it bursts (driven by ExecutedCorpse) ---
                float luminance = dot(finalColor, float3(0.3, 0.55, 0.15));
                finalColor = lerp(finalColor, luminance * _HeatColor.rgb * 2.5, saturate(_Heat) * 0.7);
                finalColor += _HeatColor.rgb * (0.35 + fres * 2.5 + star) * _HeatGlow;

                // Mild tonemap so bright areas roll off softly instead of clipping to flat white
                finalColor = finalColor / (1.0 + finalColor * 0.4);

                // --- The Unstable Core, seen deep inside the body (driven by UnstableCore once the armour breaks) ---
                // Each pixel's view ray is followed on into the body and hit against the core's sphere, so the orb
                // stays put at depth while the surface slides over it (true parallax). Around it, a deeper layer of
                // nebula shows through and the core's glow bleeds into the galaxy. After the tonemap, so it can bloom.
                if (_OrbExposure > 0.001)
                {
                    float3 eye = GetCameraPositionWS();
                    float3 toSurface = IN.positionWS - eye;
                    float surfaceDistance = length(toSurface);
                    float3 ray = toSurface / surfaceDistance;
                    float3 fromCore = eye - _OrbCenter.xyz;
                    float along = -dot(fromCore, ray);
                    float3 closest = fromCore + ray * along;
                    float passDistance = length(closest) / _OrbRadius;
                    // Only while the core is inside, behind this surface; once torn out the orb mesh takes over.
                    float inside = step(surfaceDistance, along + _OrbRadius) * _OrbExposure;

                    float windowMask = saturate(1.0 - (passDistance - 1.0) / max(_OrbWindow, 0.01)) * inside;
                    float depth = max(0.0, along - surfaceDistance);
                    float3 rayOS = TransformWorldToObjectDir(ray);
                    float3 deepNebula = triplanarSample(TEXTURE2D_ARGS(_NebulaTexA, sampler_NebulaTexA),
                                                        IN.positionOS + rayOS * depth * 1.5, normalOS, _TriplanarScale,
                                                        t * _ScrollSpeedA.xy * 1.7, _TriplanarBlendSharpness) * _NebulaBrightness;
                    finalColor = lerp(finalColor, finalColor * 0.35 + deepNebula * 0.8, windowMask * 0.75);

                    float3 right, up;
                    OrbViewBasis(-ray, right, up);
                    float hazeAngle = atan2(dot(closest, up), dot(closest, right));
                    finalColor += CoreOrbHaze(max(passDistance, 1.0), hazeAngle, _OrbTime, _OrbSurge) * _OrbIntensity * 0.6 * inside;

                    float c = dot(fromCore, fromCore) - _OrbRadius * _OrbRadius;
                    float h = along * along - c;
                    if (h > 0.0)
                    {
                        float hitDistance = along - sqrt(h);
                        if (hitDistance > surfaceDistance)
                        {
                            float3 normal = (eye + ray * hitDistance - _OrbCenter.xyz) / _OrbRadius;
                            float3 orb = ShadeCoreOrb(normal, -ray, _OrbTime, _OrbInstability, _OrbSurge, _OrbCollapse) * _OrbIntensity;
                            // A thin veil of galaxy stays over the core, thicker the deeper it sits.
                            float veil = _OrbVeil * saturate((hitDistance - surfaceDistance) / (_OrbRadius * 3.0));
                            finalColor = lerp(finalColor, orb + finalColor * veil, _OrbExposure * (1.0 - veil * 0.6));
                        }
                    }
                }

                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
