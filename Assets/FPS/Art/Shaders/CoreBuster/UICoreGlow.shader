// Additive halo behind the CoreBuster logo: a soft radial bloom with slowly turning rays, breathing
// in and out. CoreLogoGlow drives _Intensity so the core pulses like it is about to go critical.
Shader "CoreBuster/UI Core Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Glow", Color) = (0.55, 0.75, 1.6, 1)
        [HDR] _RimColor ("Outer Glow", Color) = (1.1, 0.35, 0.9, 1)
        _Intensity ("Intensity (driven by script)", Range(0, 4)) = 1
        _Falloff ("Falloff", Range(1, 20)) = 5
        _RayCount ("Ray Count", Float) = 9
        _RayStrength ("Ray Strength", Range(0, 1)) = 0.35
        _RaySpin ("Ray Spin", Float) = 0.12
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color, _RimColor;
            float _Intensity, _Falloff, _RayCount, _RayStrength, _RaySpin;

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

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 p = (i.uv - 0.5) * 2.0;
                float r = length(p);
                float angle = atan2(p.y, p.x);

                float core = exp(-r * r * _Falloff);
                float rim = exp(-pow(max(r - 0.45, 0.0), 2.0) * 18.0) * saturate(1.0 - r);
                // Two ray sets turning in opposite directions so the light seems to flicker.
                float rays = pow(0.5 + 0.5 * cos(angle * _RayCount + t * _RaySpin * 6.2831), 8.0)
                           + pow(0.5 + 0.5 * cos(angle * (_RayCount + 4.0) - t * _RaySpin * 4.0), 12.0) * 0.6;
                rays *= _RayStrength * saturate(1.0 - r) * saturate(r * 3.0);

                float3 color = _Color.rgb * (core + rays) + _RimColor.rgb * rim * 0.45;
                color *= _Intensity * i.color.rgb * i.color.a;
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
}
