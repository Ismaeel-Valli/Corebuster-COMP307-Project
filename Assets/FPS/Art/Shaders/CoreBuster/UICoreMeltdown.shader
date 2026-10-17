// The player's own core in the escape ending: a molten plasma sphere drawn on a UI Image. Domain-warped
// fractal noise swirls across a shaded disc, fissures tear open across the surface, and a corona bleeds
// past the rim. FinaleOverlay drives _Expand from 0 (rising, dim, intact) to 1 (swollen, torn, white-hot)
// just before the screen bursts into white.
Shader "CoreBuster/UI Core Meltdown"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Plasma", Color) = (1.6, 0.45, 0.12, 1)
        [HDR] _HotColor ("White-Hot", Color) = (3, 2.6, 2.1, 1)
        [HDR] _RimColor ("Corona", Color) = (1.2, 0.25, 0.08, 1)
        _Swirl ("Swirl Speed", Float) = 0.35
        _Expand ("Expand (driven by script)", Range(0, 1)) = 0
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
            #include "UnityCG.cginc"

            float4 _Color, _HotColor, _RimColor;
            float _Swirl, _Expand;

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

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            float fbm(float2 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;
                for (int octave = 0; octave < 5; octave++)
                {
                    sum += noise(p) * amplitude;
                    p = p * 2.03 + 17.1;
                    amplitude *= 0.5;
                }
                return sum;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 p = (i.uv - 0.5) * 2.0;
                // The sphere fills 70% of the quad; the rest is room for the corona.
                float2 s = p / 0.7;
                float r = length(s);

                // Map the disc onto a sphere so the plasma bulges toward the viewer and slides round the limb.
                float z = sqrt(saturate(1.0 - r * r));
                float2 surface = s / (z + 0.6);

                // Domain warping turns plain fbm into slowly churning convection cells.
                float angle = t * _Swirl;
                float2x2 spin = float2x2(cos(angle), -sin(angle), sin(angle), cos(angle));
                float2 q = mul(spin, surface * 2.2);
                float2 warp = float2(fbm(q + t * 0.15), fbm(q + 5.2 - t * 0.12));
                float plasma = fbm(q + warp * 1.6);

                // Fissures: thin seams along the noise's mid-level that widen and brighten as it expands.
                float seam = abs(fbm(surface * 3.4 + warp * 0.8 - t * 0.05) - 0.5);
                float fissures = smoothstep(0.035 + 0.06 * _Expand, 0.0, seam) * (0.25 + 0.75 * _Expand);

                float throb = 0.85 + 0.15 * sin(t * (3.0 + 9.0 * _Expand));
                float shade = 0.35 + 0.65 * z;
                float heat = saturate(plasma * 1.3 * throb + fissures + _Expand * 0.55);
                float3 color = lerp(_Color.rgb * shade, _HotColor.rgb, heat * heat);
                color += _HotColor.rgb * fissures;

                float body = smoothstep(1.0, 0.97, r);
                // The corona swells with the core and drowns the edge in light near the end.
                float corona = exp(-max(r - 1.0, 0.0) * lerp(9.0, 2.5, _Expand)) * (1.0 - body);
                color = lerp(_RimColor.rgb * (1.0 + 2.0 * _Expand) * corona, color, body);
                color = lerp(color, _HotColor.rgb, smoothstep(0.75, 1.0, _Expand) * 0.6);

                float alpha = saturate(body + corona * (0.6 + 0.4 * _Expand));
                return fixed4(color * i.color.rgb, alpha * i.color.a);
            }
            ENDCG
        }
    }
}
