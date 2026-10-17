// Screen cue for standing in core gas: red smoke creeping in from the screen edges, thicker the deeper the
// player is in the cloud, flashing on each damage tick. Drawn on a full-screen UI RawImage by ToxicGasHUD.
Shader "CoreBuster/UI Toxic Gas Overlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _GasColor ("Gas", Color) = (0.45, 0.02, 0.05, 1)
        _EdgeColor ("Burning Edge", Color) = (1, 0.18, 0.12, 1)
        _Exposure ("Exposure (driven by script)", Range(0, 1)) = 0
        _Hit ("Damage Flash (driven by script)", Range(0, 1)) = 0
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

            float4 _GasColor, _EdgeColor;
            float _Exposure, _Hit;

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
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), u.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), u.x), u.y);
            }

            float fbm(float2 p)
            {
                float sum = 0.0, amplitude = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    sum += valueNoise(p) * amplitude;
                    p = p * 2.03 + 11.7;
                    amplitude *= 0.5;
                }
                return sum / 0.9375;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 q = (i.uv - 0.5) * 2.0;
                q.x *= aspect;
                // 0 at the centre of the screen, 1 in the corners.
                float edge = length(q) / length(float2(aspect, 1.0));

                float2 gasUV = float2(i.uv.x * aspect, i.uv.y) * 3.0;
                float smoke = fbm(gasUV + float2(t * 0.18, t * 0.11));
                float wisps = fbm(gasUV * 2.3 - float2(t * 0.25, -t * 0.2));

                // The more exposed the player, the further the smoke reaches toward the crosshair.
                float reach = lerp(0.95, 0.35, _Exposure);
                float cover = smoothstep(reach, reach + 0.45, edge + (smoke - 0.5) * 0.45);
                float3 color = lerp(_GasColor.rgb, _EdgeColor.rgb, saturate(wisps * 1.3 - 0.2));
                float pulse = 0.85 + 0.15 * sin(t * 5.0);
                float alpha = cover * saturate(_Exposure * 1.6) * 0.85 * pulse;

                // Damage ticks flash the edges.
                float flash = _Hit * smoothstep(0.35, 1.0, edge);
                color = lerp(color, _EdgeColor.rgb, flash);
                alpha = saturate(alpha + flash * 0.55);

                return fixed4(color, alpha * i.color.a);
            }
            ENDCG
        }
    }
}
