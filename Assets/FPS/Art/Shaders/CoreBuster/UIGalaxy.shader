// The Galaxy Husk nebula as a full-screen UI background: the same two nebula photos scrolling and
// cross-fading, with twinkling stars. _Wormhole folds the view into a spiralling tunnel that drags
// everything toward a blazing centre - the boot screen animates it from 0 to 1 to suck the player in.
// Drawn on a full-screen RawImage (menus, boot screen, end screens).
Shader "CoreBuster/UI Galaxy"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}

        [Header(Nebula)]
        _NebulaTexA ("Nebula A", 2D) = "black" {}
        _NebulaTexB ("Nebula B", 2D) = "black" {}
        _BlendSpeed ("Cross-fade Speed", Float) = 0.05
        _ScrollA ("Scroll A", Vector) = (0.01, 0.005, 0, 0)
        _ScrollB ("Scroll B", Vector) = (-0.007, 0.012, 0, 0)
        _Tiling ("Screen Tiling", Float) = 0.8
        _Brightness ("Brightness", Range(0, 4)) = 1.4
        _Tint ("Tint", Color) = (1, 1, 1, 1)

        [Header(Stars)]
        _StarDensity ("Star Density", Float) = 90
        _StarThreshold ("Star Threshold (higher = fewer)", Range(0.9, 0.999)) = 0.985
        _TwinkleSpeed ("Twinkle Speed", Float) = 2
        _StarColor ("Star Color", Color) = (1, 0.95, 1, 1)

        [Header(Wormhole)]
        _Wormhole ("Wormhole (0 = open sky, 1 = tunnel)", Range(0, 1)) = 0
        _WarpOffset ("Travel Along Tunnel (driven by script)", Float) = 0
        _Swirl ("Swirl", Float) = 0.6
        _HoleColor ("Singularity Color", Color) = (0.75, 0.85, 1, 1)
        _HoleGlow ("Singularity Glow", Range(0, 8)) = 1.5
        _HoleSize ("Singularity Size", Range(0.01, 0.6)) = 0.12

        [Header(Framing)]
        _Vignette ("Vignette", Range(0, 1)) = 0.55
        _Zoom ("Zoom (driven by script)", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            Texture2D _NebulaTexA, _NebulaTexB;
            // The nebula photos don't tile, so every other repeat is mirrored to hide the seams. The photos
            // import with Repeat wrap (the in-game galaxy material needs it); mirroring the coordinates by
            // hand still let bilinear and mip filtering at each fold reach across to the photo's opposite
            // edge, drawing a thin vertical line. Hardware mirror addressing reflects the filter footprint too.
            SamplerState nebula_trilinear_mirror_sampler;
            float _BlendSpeed, _Tiling, _Brightness;
            float4 _ScrollA, _ScrollB, _Tint, _StarColor, _HoleColor;
            float _StarDensity, _StarThreshold, _TwinkleSpeed;
            float _Wormhole, _WarpOffset, _Swirl, _HoleGlow, _HoleSize;
            float _Vignette, _Zoom;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Both nebula photos, scrolling in opposite directions and slowly trading places.
            // scaleB offsets the second layer's tiling so the two don't line up.
            float3 Nebula(float2 uv, float2 dx, float2 dy, float t, float2 scaleB)
            {
                float3 a = _NebulaTexA.SampleGrad(nebula_trilinear_mirror_sampler, uv + t * _ScrollA.xy, dx, dy).rgb;
                float3 b = _NebulaTexB.SampleGrad(nebula_trilinear_mirror_sampler, uv * scaleB + t * _ScrollB.xy,
                    dx * scaleB, dy * scaleB).rgb;
                return lerp(a, b, 0.5 + 0.5 * sin(t * _BlendSpeed)) * _Brightness;
            }

            float Stars(float2 p, float t)
            {
                float2 cell = floor(p);
                float2 f = frac(p) - 0.5;
                float n = hash21(cell);
                float2 offset = (float2(hash21(cell + 7.1), hash21(cell + 3.7)) - 0.5) * 0.6;
                float point_ = saturate(1.0 - length(f - offset) * 6.0);
                float twinkle = 0.55 + 0.45 * sin(t * _TwinkleSpeed + n * 6.2831);
                return step(_StarThreshold, n) * point_ * point_ * twinkle * 2.0;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 p = (i.uv - 0.5) * float2(aspect, 1.0) / max(_Zoom, 0.01);
                float r = length(p);

                // --- Open sky: the nebula laid flat across the screen ---
                float2 skyUV = p * _Tiling + 0.5;
                float3 sky = Nebula(skyUV, ddx(skyUV), ddy(skyUV), t, 1.13);
                sky += Stars(p * _StarDensity * 0.5, t) * _StarColor.rgb;

                // --- Wormhole: polar coordinates become a tunnel. Depth grows toward the centre, the angle
                // twists with depth, and _WarpOffset slides the walls past the viewer. ---
                float3 tunnel = 0;
                if (_Wormhole > 0.001)
                {
                    float angle = atan2(p.y, p.x) / 6.2831853;
                    float depth = 0.32 / max(r, 0.015);
                    // An even number of repeats around the tunnel keeps the mirrored tiling seamless.
                    float u = angle * 4.0 + depth * _Swirl + t * 0.03;
                    float v = depth * 0.35 + _WarpOffset;
                    float2 tunnelUV = float2(u, v);
                    // atan2 jumps at the negative x axis; take derivatives from a copy cut elsewhere so
                    // the seam does not drop to the smallest mip.
                    float2 cutUV = float2((frac(angle + 0.5) - 0.5) * 4.0 + depth * _Swirl, v);
                    float2 dx = ddx(tunnelUV), dy = ddy(tunnelUV);
                    float2 dx2 = ddx(cutUV), dy2 = ddy(cutUV);
                    if (dot(dx2, dx2) < dot(dx, dx)) dx = dx2;
                    if (dot(dy2, dy2) < dot(dy, dy)) dy = dy2;
                    // Only stretch the second layer along the tunnel, or its repeats would not close up.
                    tunnel = Nebula(tunnelUV, dx, dy, t, float2(1.0, 1.13));

                    // Stars stretch into streaks as they rush down the tunnel.
                    // A cell's angular width grows with the radius on screen, so narrow the streak by r
                    // to keep it a few pixels wide everywhere.
                    float2 streakCell = float2(angle * 140.0, v * 9.0);
                    float2 sf = frac(streakCell) - 0.5;
                    float sn = hash21(floor(streakCell));
                    float thin = saturate(1.0 - abs(sf.x) * max(r * 25.0, 2.0));
                    float streak = step(0.95, sn) * thin * thin * saturate(1.0 - abs(sf.y) * 1.2);
                    tunnel += streak * _StarColor.rgb * saturate(r * 3.0);

                    // Spiral arms of light winding into the throat.
                    float arms = pow(0.5 + 0.5 * sin(angle * 6.2831853 * 3.0 + depth * 2.5 - _WarpOffset * 6.0), 6.0);
                    tunnel += arms * _HoleColor.rgb * 0.35 * saturate(1.2 - r * 1.6);

                    // The far end of the tunnel falls into darkness before the singularity.
                    tunnel *= saturate(r * 4.0);
                }

                float3 color = lerp(sky, tunnel, _Wormhole);

                // The singularity at the heart of it, swelling as the wormhole opens.
                float hole = exp(-(r * r) / max(_HoleSize * _HoleSize, 0.0001));
                color += _HoleColor.rgb * hole * _HoleGlow * _Wormhole;

                float vignette = 1.0 - _Vignette * smoothstep(0.35, 1.25, length((i.uv - 0.5) * 2.0));
                color *= vignette * _Tint.rgb * i.color.rgb;

                return fixed4(color, i.color.a * _Tint.a);
            }
            ENDCG
        }
    }
}
