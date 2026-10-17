// Procedural Nebula Skybox — Universal Render Pipeline (URP)
//
// A fully GPU-generated nebula skybox — no textures, so there is no
// resolution/compression to go blurry no matter how close the camera
// gets or how large the skybox sphere is drawn.
//
// SETUP:
//   1. Put this file anywhere in Assets/ (e.g. Assets/FPS/Art/Shaders/).
//   2. Select your skybox Material (e.g. "SkyBoxShader"), and in the
//      Inspector's Shader dropdown pick "Skybox/ProceduralNebulaURP".
//   3. Window > Rendering > Lighting > Environment > Skybox Material
//      should already point at that material — if not, assign it.
//   4. Tweak the exposed colors/sliders in the Inspector to taste.
//
// Requires the com.unity.render-pipelines.core package, which any URP
// project already has installed.

Shader "Skybox/ProceduralNebulaURP"
{
    Properties
    {
        [Header(Base Sky)]
        _SkyColorTop      ("Sky Color (Top / Deep Space)", Color) = (0.01, 0.01, 0.03, 1)
        _SkyColorBottom   ("Sky Color (Horizon)", Color) = (0.05, 0.02, 0.05, 1)

        [Header(Nebula Colors)]
        _NebulaColorA     ("Nebula Color A (Warm)", Color) = (1.0, 0.45, 0.1, 1)
        _NebulaColorB     ("Nebula Color B (Cool)", Color) = (0.15, 0.55, 1.0, 1)
        _NebulaColorCore  ("Nebula Core Color (Highlight)", Color) = (0.85, 0.55, 1.0, 1)
        _NebulaDensity    ("Nebula Density", Range(0.1, 4)) = 1.4
        _NebulaScale      ("Nebula Noise Scale", Range(0.2, 8)) = 2.0
        _NebulaSharpness  ("Nebula Edge Sharpness", Range(0.5, 6)) = 2.2
        _CoreIntensity    ("Core Glow Intensity", Range(0, 4)) = 1.6
        _CoreSize         ("Core Glow Size", Range(0.01, 1)) = 0.18
        _CoreDirection    ("Core Glow Direction", Vector) = (0.3, 0.15, 0.6, 0)

        [Header(Stars)]
        _StarDensity      ("Star Density", Range(0.001, 0.05)) = 0.012
        _StarBrightness   ("Star Brightness", Range(0, 8)) = 3.0
        _StarTwinkleSpeed ("Star Twinkle Speed", Range(0, 5)) = 0.6

        [Header(Motion)]
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
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
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

            struct appdata
            {
                float4 positionOS : POSITION;
            };

            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            // The sky itself lives in NebulaSky.hlsl, shared with the corridor windows that look out onto it.
            #include "NebulaSky.hlsl"

            half4 frag (v2f i) : SV_Target
            {
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
                return half4(BlackHoleSkyColor(i.dir, s, bh), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
