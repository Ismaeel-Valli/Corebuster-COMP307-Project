// Decorative reactor field. Animation is entirely on the GPU; no executable-core state is involved.
// corebuster_render_008: blue palette and an unstable surge (irregular swells, brief dropouts and
// occasional hot flashes). ReactorInstabilityLight mirrors ReactorSurge so the room lights pulse with it.
Shader "CoreBuster/Reactor Containment"
{
    Properties
    {
        _Color ("Blue", Color) = (0.08, 0.32, 1, 1)
        _Accent ("Ice", Color) = (0.3, 0.75, 1, 1)
        _Core ("Core (blue-white)", Color) = (0.78, 0.9, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 2.2
        // Single speed/amplitude control: 0 is a steady field, 1 the full unstable design.
        // Flashes can only start on 0.5 s slots, so no value exceeds two flashes per second.
        _Instability ("Instability", Range(0, 1)) = 1
        // Review captures pin the animation to one instant; -1 uses live shader time.
        [HideInInspector] _ReviewTime ("Review Time", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Containment"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Accent, _Core;
                float _Intensity, _Instability, _ReviewTime;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            // Keep these three identical to ReactorInstabilityLight.Hash, Noise and Surge.
            float ReactorHash(float n)
            {
                n = frac(n * 0.1031);
                n *= n + 33.33;
                n *= n + n;
                return frac(n);
            }
            float ReactorNoise(float x)
            {
                float i = floor(x);
                float f = x - i;
                f = f * f * (3.0 - 2.0 * f);
                return lerp(ReactorHash(i), ReactorHash(i + 1.0), f);
            }
            float ReactorPulse(float t)
            {
                float local = frac(t * 2.0);
                return smoothstep(0.0, 0.06, local) * (1.0 - smoothstep(0.1, 0.42, local));
            }
            float ReactorSurge(float t, float k, out float flash)
            {
                float slow = ReactorNoise(t * 0.83);
                float fast = ReactorNoise(t * 3.7 + 19.0);
                float roll = ReactorHash(floor(t * 2.0) + 71.0);
                float previous = ReactorHash(floor(t * 2.0) + 70.0);
                float pulse = ReactorPulse(t);
                // Never two flash slots in a row, so any one second holds at most two flashes.
                flash = roll > 0.8 && previous <= 0.8 ? pulse : 0.0;
                float dropout = roll < 0.14 ? pulse : 0.0;
                return max(0.1, 1.0 + k * ((slow - 0.5) * 0.8 + (fast - 0.5) * 0.3 + flash * 1.5 - dropout * 0.6));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float k = saturate(_Instability);
                float time = _ReviewTime >= 0 ? _ReviewTime : _Time.y;
                float flash;
                float surge = ReactorSurge(time, k, flash);
                float height = input.uv.y;
                float angle = input.uv.x * 6.283185;
                // Filaments speed up and tangle as instability rises; noise jitter breaks the regular sweep.
                float t = time * lerp(0.55, 1.6, k);
                float jitter = (ReactorNoise(time * 5.3 + height * 2.1) - 0.5) * 1.6 * k;
                float warp = sin(height * 3.1 - t) * 0.65 + sin(height * 7.3 + t * 1.7) * 0.18
                    + sin(height * 13.1 - t * 2.9 + angle * 3.0) * 0.22 * k + jitter;
                float filament = pow(saturate(0.5 + 0.5 * sin(angle * 5.0 + warp)), 28);
                float crackle = pow(saturate(0.5 + 0.5 * sin(angle * 9.0 - warp * 1.3 + t * 0.7)), 40) * k;
                float coils = pow(saturate(0.5 + 0.5 * sin(height * 9.0 - angle * 2.0 - t * 1.4)), 22);
                float3 normal = normalize(input.normalWS);
                float facing = saturate(dot(normal, GetWorldSpaceNormalizeViewDir(input.positionWS)));
                float edge = pow(1.0 - facing, 2);
                // Facing texels look through the axis: a brighter blue-white core column.
                float core = pow(facing, 4);
                float envelope = smoothstep(0, 0.25, height) * (1 - smoothstep(4.15, 4.4, height));
                float3 color = _Color.rgb * (0.1 + filament * 0.9 + crackle * 0.5 + edge * 0.3)
                    + _Accent.rgb * coils * 0.35 + _Core.rgb * core * 0.45;
                // Hot flashes whiten the filaments as well as brightening the field.
                color = color * surge + _Core.rgb * flash * k * (0.25 + filament + crackle) * 0.6;
                return half4(color * envelope * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
