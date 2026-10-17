// Armour for the giant core in the Boss Hall, one shader with a style per layer. ArmourPiece sets _Damage
// (0..1) and _Seed per plate through a MaterialPropertyBlock, and the damage is drawn procedurally:
//   Steel   - heavy outer plates: bevelled edges, rim bolts and a hazard band. Damage crumples the plate
//             (vertex displacement), scorches the paint and lights its seams as it tears loose.
//   Ceramic - composite middle tiles: a 3D Voronoi crack network spreads cell by cell with damage, the
//             cracks glowing with the core's light behind them, until the tile shatters.
//   Lead    - radiation-shielding inner lining with a stencilled trefoil. Damage presses dents into it
//             (displacement with faceted normals), then a jagged split opens across the panel.
// Vertex colour red marks the outer face; only it is displaced, so the panel's edges stay closed.
Shader "CoreBuster/Core Armour"
{
    Properties
    {
        [KeywordEnum(Steel, Ceramic, Lead)] _Style ("Style", Float) = 0
        _BaseColor ("Base Colour", Color) = (0.32, 0.34, 0.37, 1)
        _DetailColor ("Marking Colour", Color) = (0.95, 0.72, 0.08, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0.75
        _Smoothness ("Smoothness", Range(0, 1)) = 0.45
        [HDR] _GlowColor ("Damage Glow", Color) = (4, 1.1, 0.25, 1)
        _DentDepth ("Dent / Buckle Depth", Float) = 0.07
        _CrackScale ("Crack Scale", Float) = 6
        [Header(Per Plate)]
        _Damage ("Damage", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _DetailColor;
            half4 _GlowColor;
            half _Metallic;
            half _Smoothness;
            float _DentDepth;
            float _CrackScale;
            float _Damage;
            float _Seed;
        CBUFFER_END

        float2 ArmourHash22(float2 p)
        {
            p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
            return frac(sin(p) * 43758.5453);
        }

        float3 ArmourHash33(float3 p)
        {
            p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)),
                       dot(p, float3(113.5, 271.9, 124.6)));
            return frac(sin(p) * 43758.5453);
        }

        float ArmourNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = ArmourHash33(i).x;
            float n100 = ArmourHash33(i + float3(1, 0, 0)).x;
            float n010 = ArmourHash33(i + float3(0, 1, 0)).x;
            float n110 = ArmourHash33(i + float3(1, 1, 0)).x;
            float n001 = ArmourHash33(i + float3(0, 0, 1)).x;
            float n101 = ArmourHash33(i + float3(1, 0, 1)).x;
            float n011 = ArmourHash33(i + float3(0, 1, 1)).x;
            float n111 = ArmourHash33(i + float3(1, 1, 1)).x;
            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                        lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }

        // How far the outer face moves along its normal: lead dents deepen and multiply with damage,
        // steel bulges and crumples across the plate.
        float ArmourDisplacement(float2 uv, float3 positionOS)
        {
        #if defined(_STYLE_LEAD)
            float dent = 0.0;
            [unroll] for (int k = 0; k < 6; k++)
            {
                float2 centre = ArmourHash22(float2(_Seed + k * 7.31, k * 3.17)) * 0.76 + 0.12;
                float appear = saturate((_Damage - k / 6.0) * 6.0);
                float d = distance(uv, centre) / (0.16 + 0.05 * frac(_Seed + k * 0.37));
                dent += appear * (1.0 - smoothstep(0.0, 1.0, d));
            }
            return -dent * _DentDepth;
        #elif defined(_STYLE_STEEL)
            float bulge = sin(uv.x * PI) * sin(uv.y * PI);
            float crumple = ArmourNoise(positionOS * 3.0 + _Seed) - 0.5;
            return (bulge * 0.6 + crumple) * _Damage * _DentDepth;
        #else
            return 0.0;
        #endif
        }

        float3 ArmourDisplace(float3 positionOS, float3 normalOS, float2 uv, float outerFace)
        {
            return positionOS + normalOS * ArmourDisplacement(uv, positionOS) * outerFace;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _STYLE_STEEL _STYLE_CERAMIC _STYLE_LEAD
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionOS = ArmourDisplace(v.positionOS.xyz, v.normalOS, v.uv, v.color.r);
                o.positionWS = TransformObjectToWorld(positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Distance to the nearest Voronoi cell border, and that cell's id.
            float Voronoi(float3 p, out float3 cell)
            {
                float3 base = floor(p);
                float f1 = 8.0, f2 = 8.0;
                cell = base;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 c = base + float3(x, y, z);
                    float d = distance(p, c + ArmourHash33(c));
                    if (d < f1) { f2 = f1; f1 = d; cell = c; }
                    else if (d < f2) f2 = d;
                }
                return f2 - f1;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                half3 albedo = _BaseColor.rgb;
                half metallic = _Metallic;
                half smoothness = _Smoothness;
                half3 emission = 0;
                float faceting = 0.0;

            #if defined(_STYLE_STEEL)
                float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float bevel = smoothstep(0.0, 0.06, edge);
                albedo *= lerp(0.5, 1.0, bevel);
                albedo *= 0.85 + 0.3 * ArmourNoise(i.positionOS * float3(40.0, 3.0, 40.0));
                // Bolt heads along the top and bottom rims.
                float2 boltCell = float2(frac(uv.x * 5.0) - 0.5, (uv.y < 0.5 ? uv.y - 0.07 : uv.y - 0.93) * 5.0);
                float bolt = 1.0 - smoothstep(0.08, 0.12, length(boltCell));
                albedo = lerp(albedo, albedo * 1.6, bolt);
                // A hazard band of diagonal stripes.
                float band = step(0.14, uv.y) * step(uv.y, 0.24);
                float stripes = step(0.5, frac((uv.x * 2.0 + uv.y) * 6.0));
                albedo = lerp(albedo, lerp(_DetailColor.rgb, half3(0.04, 0.04, 0.04), stripes), band);
                // Damage: scorched paint, dulled finish and glowing seams as the plate tears loose.
                float scorch = _Damage * (1.0 - bevel * 0.5) * (0.5 + 0.5 * ArmourNoise(i.positionOS * 5.0 + _Seed));
                albedo *= 1.0 - 0.7 * scorch;
                smoothness *= 1.0 - 0.6 * _Damage;
                emission = _GlowColor.rgb * pow(1.0 - bevel, 3.0) * smoothstep(0.45, 1.0, _Damage) * 0.8;
                faceting = saturate(_Damage * 2.0) * 0.6;
            #elif defined(_STYLE_CERAMIC)
                albedo *= 0.88 + 0.12 * ArmourNoise(i.positionOS * 30.0);
                // Each piece is a 3x2 block of composite tiles: dark grout between them, a slight tint per tile,
                // a bevelled outer edge and a thin stencilled marking stripe.
                float2 tileGrid = uv * float2(3.0, 2.0);
                float2 tileUV = frac(tileGrid);
                float grout = min(min(tileUV.x, 1.0 - tileUV.x), min(tileUV.y, 1.0 - tileUV.y));
                albedo *= lerp(0.3, 1.0, smoothstep(0.015, 0.05, grout));
                albedo *= 0.9 + 0.2 * ArmourHash33(float3(floor(tileGrid), _Seed)).x;
                float pieceEdge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                albedo *= lerp(0.45, 1.0, smoothstep(0.0, 0.04, pieceEdge));
                float marking = step(0.84, uv.y) * step(uv.y, 0.88) * step(0.08, uv.x) * step(uv.x, 0.6);
                albedo = lerp(albedo, half3(0.55, 0.12, 0.06), marking * 0.85);
                float3 cell;
                float border = Voronoi(i.positionOS * _CrackScale + _Seed * 3.1, cell);
                // Each cell's edge opens once damage passes that cell's own threshold, so cracks spread.
                float threshold = ArmourHash33(cell + 0.5).x;
                float open = smoothstep(threshold, threshold + 0.1, _Damage * 1.2);
                float crack = (1.0 - smoothstep(0.0, 0.03 + 0.05 * _Damage, border)) * open;
                // Finer hairline cracks inside the opened cells.
                float3 fine;
                float hair = (1.0 - smoothstep(0.0, 0.025, Voronoi(i.positionOS * _CrackScale * 3.3 + _Seed, fine))) *
                             open * smoothstep(0.4, 0.9, _Damage);
                crack = saturate(crack + hair * 0.6);
                albedo = lerp(albedo, half3(0.07, 0.06, 0.06), crack);
                smoothness *= 1.0 - crack;
                emission = _GlowColor.rgb * crack * (0.3 + 1.7 * smoothstep(0.2, 1.0, _Damage));
            #elif defined(_STYLE_LEAD)
                albedo *= 0.75 + 0.35 * ArmourNoise(i.positionOS * 7.0 + _Seed);
                // A radiation trefoil stencilled on a yellow disc.
                float2 c = (uv - 0.5) * 2.0;
                float r = length(c);
                float sector = frac(atan2(c.y, c.x) / (2.0 * PI) * 3.0 + 0.25);
                float blades = step(0.2, r) * step(r, 0.56) * step(sector, 0.5);
                float hub = step(r, 0.11);
                float disc = step(r, 0.7) * (1.0 - smoothstep(0.66, 0.7, r));
                half3 stencil = lerp(_DetailColor.rgb, half3(0.04, 0.04, 0.04), saturate(blades + hub));
                albedo = lerp(albedo, stencil, disc * 0.95 * (1.0 - 0.4 * _Damage));
                // Badly dented, it splits along a jagged seam that glows from the heart inside.
                float seam = abs(uv.y - 0.5 - 0.08 * sin(uv.x * 23.0 + _Seed) - 0.035 * sin(uv.x * 57.0 + _Seed * 2.0));
                float split = (1.0 - smoothstep(0.0, 0.012 + 0.03 * _Damage, seam)) * smoothstep(0.5, 0.85, _Damage);
                albedo *= 1.0 - split;
                emission = _GlowColor.rgb * split * 2.0;
                smoothness *= 1.0 - 0.3 * _Damage;
                faceting = saturate(_Damage * 3.0);
            #endif

                // Dents and crumples read through faceted normals from the displaced surface itself.
                float3 normalWS = normalize(i.normalWS);
                float3 faceNormal = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(faceNormal, normalWS) < 0.0) faceNormal = -faceNormal;
                normalWS = normalize(lerp(normalWS, faceNormal, faceting));

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
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
            #pragma shader_feature_local _STYLE_STEEL _STYLE_CERAMIC _STYLE_LEAD
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            float4 ShadowVert(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(ArmourDisplace(v.positionOS.xyz, v.normalOS, v.uv, v.color.r));
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
            #pragma shader_feature_local _STYLE_STEEL _STYLE_CERAMIC _STYLE_LEAD

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            float4 DepthVert(Attributes v) : SV_POSITION
            {
                return TransformObjectToHClip(ArmourDisplace(v.positionOS.xyz, v.normalOS, v.uv, v.color.r));
            }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
