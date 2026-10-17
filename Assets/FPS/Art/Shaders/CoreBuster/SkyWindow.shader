// CoreBuster/Sky Window: the corridor's octagonal window panes, looking straight out into space.
//
// Behind the panes is really the map's ground plane and the rooms' outer shells, so the pane does not show what is
// behind it: it draws the procedural nebula sky (NebulaSky.hlsl, the skybox's own function) along each pixel's view
// ray, as if the glass opened onto empty space. Over that sit a faint tint and haze, soft glare streaks that slide as
// the viewer walks past, a fresnel rim of reflected corridor light, and a little procedural dust and scratching, so the
// pane reads as glass in front of space. The pane takes no light specular, so wall lamps never put a hot dot on it.
Shader "CoreBuster/Sky Window"
{
    Properties
    {
        [Header(Glass)]
        _Tint             ("Glass Tint", Color) = (0.86, 0.92, 1, 1)
        _SkyExposure      ("Sky Exposure", Range(0, 3)) = 1
        _RimColor         ("Reflection Rim Colour", Color) = (0.55, 0.62, 0.72, 1)
        _RimStrength      ("Reflection Rim Strength", Range(0, 1)) = 0.18
        _RimPower         ("Reflection Rim Falloff", Range(1, 8)) = 4
        _Dust             ("Dust and Scratches", Range(0, 1)) = 0.35
        _DustScale        ("Dust Scale (per metre)", Range(1, 60)) = 18
        _HazeColor        ("Haze Colour", Color) = (0.32, 0.38, 0.46, 1)
        _Haze             ("Haze (the pane's own body)", Range(0, 0.5)) = 0.09
        _SheenColor       ("Glare Streak Colour", Color) = (0.78, 0.86, 1, 1)
        _Sheen            ("Glare Streak Strength", Range(0, 0.5)) = 0.14
        _SheenParallax    ("Glare Streak Parallax", Range(0, 1)) = 0.45

        [Header(Sky (copied from the skybox material))]
        _SkyColorTop      ("Sky Color (Top / Deep Space)", Color) = (0.01, 0.01, 0.03, 1)
        _SkyColorBottom   ("Sky Color (Horizon)", Color) = (0.05, 0.02, 0.05, 1)
        _NebulaColorA     ("Nebula Color A (Warm)", Color) = (1.0, 0.45, 0.1, 1)
        _NebulaColorB     ("Nebula Color B (Cool)", Color) = (0.15, 0.55, 1.0, 1)
        _NebulaColorCore  ("Nebula Core Color (Highlight)", Color) = (0.85, 0.55, 1.0, 1)
        _NebulaDensity    ("Nebula Density", Range(0.1, 4)) = 1.4
        _NebulaScale      ("Nebula Noise Scale", Range(0.2, 8)) = 2.0
        _NebulaSharpness  ("Nebula Edge Sharpness", Range(0.5, 6)) = 2.2
        _CoreIntensity    ("Core Glow Intensity", Range(0, 4)) = 1.6
        _CoreSize         ("Core Glow Size", Range(0.01, 1)) = 0.18
        _CoreDirection    ("Core Glow Direction", Vector) = (0.3, 0.15, 0.6, 0)
        _StarDensity      ("Star Density", Range(0.001, 0.05)) = 0.012
        _StarBrightness   ("Star Brightness", Range(0, 8)) = 3.0
        _StarTwinkleSpeed ("Star Twinkle Speed", Range(0, 5)) = 0.6
        _ScrollSpeed      ("Nebula Drift Speed", Range(0, 0.2)) = 0.01

        [Header(Black Hole)]
        _BlackHoleDirection ("Black Hole Direction", Vector) = (-0.55, 0.3, 0.78, 0)
        _BlackHoleSize      ("Black Hole Shadow Size (radians)", Range(0, 0.2)) = 0.06
        _DiskInner          ("Disk Inner Radius (shadow radii)", Range(1, 4)) = 1.25
        _DiskOuter          ("Disk Outer Radius (shadow radii)", Range(2, 10)) = 5
        _DiskTilt           ("Disk Tilt Towards Camera (degrees)", Range(0, 60)) = 9
        _DiskRoll           ("Disk Roll (degrees)", Range(-90, 90)) = -12
        _DiskColorHot       ("Disk Inner Colour", Color) = (1, 0.9, 0.75, 1)
        _DiskColorCool      ("Disk Outer Colour", Color) = (1, 0.35, 0.08, 1)
        _DiskIntensity      ("Disk Intensity", Range(0, 10)) = 2
        _DiskSpin           ("Disk Spin (radians/s at the inner edge)", Range(0, 3)) = 1.1
        _DiskDoppler        ("Disk Doppler Beaming", Range(0, 1)) = 0.5
        _LensStrength       ("Lensing Strength", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float _SkyExposure;
            half4 _RimColor;
            float _RimStrength;
            float _RimPower;
            float _Dust;
            float _DustScale;
            half4 _HazeColor;
            float _Haze;
            half4 _SheenColor;
            float _Sheen;
            float _SheenParallax;
            half4 _SkyColorTop;
            half4 _SkyColorBottom;
            half4 _NebulaColorA;
            half4 _NebulaColorB;
            half4 _NebulaColorCore;
            float _NebulaDensity;
            float _NebulaScale;
            float _NebulaSharpness;
            float _CoreIntensity;
            float _CoreSize;
            float4 _CoreDirection;
            float _StarDensity;
            float _StarBrightness;
            float _StarTwinkleSpeed;
            float _ScrollSpeed;
            float4 _BlackHoleDirection;
            float _BlackHoleSize;
            float _DiskInner;
            float _DiskOuter;
            float _DiskTilt;
            float _DiskRoll;
            half4 _DiskColorHot;
            half4 _DiskColorCool;
            float _DiskIntensity;
            float _DiskSpin;
            float _DiskDoppler;
            float _LensStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "SkyWindow"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "../NebulaSky.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fog        : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // Two axes in the pane's plane (the panes have no usable UVs): u horizontal, w up the pane.
            void PaneAxes(float3 n, out float3 u, out float3 w)
            {
                u = normalize(cross(abs(n.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0), n));
                w = cross(n, u);
            }

            // Fine dust and a few long straight scratches, from world position.
            float DustAndScratches(float3 positionWS, float3 normalWS)
            {
                float3 u, w;
                PaneAxes(normalize(normalWS), u, w);
                float2 q = float2(dot(positionWS, u), dot(positionWS, w)) * _DustScale;

                float dust = NebulaNoise3(float3(q, 0.0)) * 0.6 + NebulaNoise3(float3(q * 3.1, 4.0)) * 0.4;
                dust = smoothstep(0.55, 0.95, dust);

                // Scratches: thin lines along a rotated axis, broken up along their length.
                float2 r = float2(q.x * 0.866 + q.y * 0.5, -q.x * 0.5 + q.y * 0.866);
                float scratch = 1.0 - smoothstep(0.0, 0.035, abs(frac(r.y * 0.37) - 0.5));
                float broken = step(0.62, NebulaNoise3(float3(r.x * 0.15, r.y * 0.37, 9.0)));
                return saturate(dust * 0.7 + scratch * broken * 0.6);
            }

            // The glare of a glass pane: two soft diagonal streaks, a broad one and a thin one, that slide across the
            // pane as the viewer moves past it (they are fixed partly to the camera, not wholly to the glass), broken
            // up by faint smudges. Returns 0..1.
            float GlareStreaks(float3 positionWS, float3 normalWS)
            {
                float3 u, w;
                PaneAxes(normalize(normalWS), u, w);
                float3 eye = GetCameraPositionWS();
                float2 p = float2(dot(positionWS, u), dot(positionWS, w));
                float2 e = float2(dot(eye, u), dot(eye, w));
                float2 s = p - e * _SheenParallax;
                // Along a 35-degree diagonal, repeating every 2.6 m so every pane catches one.
                float diagonal = (s.x * 0.82 + s.y * 0.57) / 2.6;
                float broad = frac(diagonal) - 0.5;
                float thin = frac(diagonal + 0.21) - 0.5;
                float streaks = exp(-broad * broad / 0.006) + 0.7 * exp(-thin * thin / 0.0006);
                float smudge = NebulaNoise3(float3(p * 1.7, 2.0)) * 0.6 + NebulaNoise3(float3(p * 4.3, 7.0)) * 0.4;
                return saturate(streaks * lerp(0.45, 1.0, smudge));
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 view = i.positionWS - GetCameraPositionWS();
                float3 dir = normalize(view);

                NebulaSkyParams s;
                s.skyColorTop = _SkyColorTop.rgb;
                s.skyColorBottom = _SkyColorBottom.rgb;
                s.nebulaColorA = _NebulaColorA.rgb;
                s.nebulaColorB = _NebulaColorB.rgb;
                s.nebulaColorCore = _NebulaColorCore.rgb;
                s.nebulaDensity = _NebulaDensity;
                s.nebulaScale = _NebulaScale;
                s.nebulaSharpness = _NebulaSharpness;
                s.coreIntensity = _CoreIntensity;
                s.coreSize = _CoreSize;
                s.coreDirection = _CoreDirection.xyz;
                s.starDensity = _StarDensity;
                s.starBrightness = _StarBrightness;
                s.starTwinkleSpeed = _StarTwinkleSpeed;
                s.scrollSpeed = _ScrollSpeed;
                BlackHoleParams bh;
                bh.direction = _BlackHoleDirection.xyz;
                bh.size = _BlackHoleSize;
                bh.diskInner = _DiskInner;
                bh.diskOuter = _DiskOuter;
                bh.diskTilt = _DiskTilt;
                bh.diskRoll = _DiskRoll;
                bh.hotColor = _DiskColorHot.rgb;
                bh.coolColor = _DiskColorCool.rgb;
                bh.intensity = _DiskIntensity;
                bh.spin = _DiskSpin;
                bh.doppler = _DiskDoppler;
                bh.lens = _LensStrength;
                half3 sky = BlackHoleSkyColor(dir, s, bh) * _SkyExposure * _Tint.rgb;

                // Grazing angles reflect the corridor rather than show space.
                float3 n = normalize(i.normalWS);
                float facing = saturate(abs(dot(n, -dir)));
                float rim = pow(1.0 - facing, _RimPower) * _RimStrength;

                float grime = DustAndScratches(i.positionWS, n) * _Dust;
                // The glass itself: a faint haze over space (thicker at grazing angles, as the light's path through
                // the pane lengthens), glare streaks, then the reflected rim and the grime on its surface.
                half3 col = lerp(sky, _HazeColor.rgb, saturate(_Haze * (1.0 + (1.0 - facing) * 1.5)));
                col += _SheenColor.rgb * GlareStreaks(i.positionWS, n) * _Sheen;
                col = lerp(col, _RimColor.rgb, saturate(rim));
                col += _RimColor.rgb * grime * 0.12;

                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half frag (Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 normalWS = NormalizeNormalPerPixel(i.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
