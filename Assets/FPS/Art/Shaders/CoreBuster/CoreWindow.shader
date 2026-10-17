// The chest window the Disassembler forces open on an armoured enemy: a brass-and-gunmetal iris hatch whose
// blades twist back to reveal a cavity with the Unstable Core burning inside it.
// Drawn on a quad lying on the chest (visible side facing -Z, the cavity running back along +Z). Nothing behind
// the quad is real: the view ray is traced in object space through a cylindrical cavity to the orb, so the core
// has true parallax depth. The orb is shaded by CoreOrbCommon.hlsl, like the core inside a galaxy body.
// CoreWindow.cs drives _Open and the core state (_CoreTime, _Instability, _Surge, _Collapse) every frame.
Shader "CoreBuster/Core Window"
{
    Properties
    {
        _Open ("Open", Range(0, 1)) = 0
        _BladeCount ("Iris Blades", Float) = 7
        _Aperture ("Aperture (fraction of radius)", Range(0.4, 0.95)) = 0.76
        _CavityDepth ("Cavity Depth", Float) = 0.7
        _OrbDepth ("Orb Depth", Float) = 0.42
        _OrbRadius ("Orb Radius", Float) = 0.2
        _FrameColor ("Frame Metal", Color) = (0.55, 0.42, 0.24, 1)
        _BladeColor ("Blade Metal", Color) = (0.13, 0.15, 0.18, 1)
        _GlowColor ("Edge Glow", Color) = (0.3, 0.75, 1, 1)
        _GlowIntensity ("Edge Glow Intensity", Float) = 4
        _CoreIntensity ("Core HDR Intensity", Float) = 2.4

        [Header(Driven By Script)]
        _CoreTime ("Core Time", Float) = 0
        _Instability ("Instability", Range(0, 1)) = 0.8
        _Surge ("Surge", Range(0, 1)) = 0
        _Collapse ("Collapse (absorption)", Range(0, 1)) = 0
        _Electric ("Electrocution", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Open, _BladeCount, _Aperture, _CavityDepth, _OrbDepth, _OrbRadius;
            float4 _FrameColor, _BladeColor, _GlowColor;
            float _GlowIntensity, _CoreIntensity;
            float _CoreTime, _Instability, _Surge, _Collapse, _Electric;
        CBUFFER_END

        // The quad spans -0.5..0.5; the window is the disc inscribed in it.
        void ClipToDisc(float2 uv)
        {
            clip(1.0 - length(uv * 2.0 - 1.0));
        }
        ENDHLSL

        Pass
        {
            Name "CoreWindow"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/FPS/Art/Shaders/CoreBuster/CoreOrbCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.uv = IN.uv;
                return OUT;
            }

            // Simple lit metal for the hatch: a bevel normal (object space) under the main light plus ambient.
            float3 ShadeMetal(float3 albedo, float3 normalOS, float3 viewOS)
            {
                float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
                float3 viewWS = normalize(TransformObjectToWorldDir(viewOS));
                Light light = GetMainLight();
                float diffuse = saturate(dot(normalWS, light.direction));
                float3 halfway = normalize(light.direction + viewWS);
                float specular = pow(saturate(dot(normalWS, halfway)), 48.0) * 0.8;
                float3 ambient = SampleSH(normalWS);
                return albedo * (ambient + light.color * diffuse) + light.color * specular * albedo.r * 2.0;
            }

            // Where a ray starting inside the cavity (a cylinder of radius 'radius' along +Z) meets its wall.
            float CylinderExit(float2 origin, float2 direction, float radius)
            {
                float a = dot(direction, direction);
                float b = dot(origin, direction);
                float c = dot(origin, origin) - radius * radius;
                return (-b + sqrt(max(b * b - a * c, 0.0))) / max(a, 1e-5);
            }

            // Looking into the open hatch: the orb if the ray hits it, otherwise the ribbed cavity lit by it.
            float3 Cavity(float3 origin, float3 direction, float cavityRadius)
            {
                float collapse = _Collapse;
                float3 orbCenter = float3(0.0, 0.0, _OrbDepth);
                float orbRadius = _OrbRadius * lerp(1.0, 0.1, collapse * collapse) *
                                  (1.0 + _Surge * 0.12 + (OrbNoise(float3(_CoreTime * 9.0, 1.7, 0.0)) - 0.5) * 0.06 * _Instability);

                // Ray / sphere.
                float3 toOrigin = origin - orbCenter;
                float b = dot(toOrigin, direction);
                float c = dot(toOrigin, toOrigin) - orbRadius * orbRadius;
                float discriminant = b * b - c;
                // How close the ray passes the orb (in orb radii), and on which side, for the haze around it.
                float3 miss = cross(direction, orbCenter - origin);
                float passDistance = length(miss) / max(orbRadius, 1e-4);
                float passAngle = atan2(miss.y, miss.x);
                float3 haze = CoreOrbHaze(passDistance, passAngle, _CoreTime, _Surge) * (1.0 - collapse);

                float orbDistance = -b - sqrt(max(discriminant, 0.0));
                if (discriminant > 0.0 && orbRadius > 0.002 && orbDistance > 0.0)
                {
                    float3 normal = normalize(origin + direction * orbDistance - orbCenter);
                    float3 orb = ShadeCoreOrb(normal, -direction, _CoreTime, _Instability, _Surge, collapse);
                    return orb * _CoreIntensity + haze * 0.3;
                }

                // The wall of the cavity, or its back plate.
                float t = CylinderExit(origin.xy, direction.xy, cavityRadius);
                float3 hit = origin + direction * t;
                float3 wallNormal = float3(-normalize(hit.xy), 0.0);
                if (hit.z > _CavityDepth)
                {
                    t = (_CavityDepth - origin.z) / max(direction.z, 1e-4);
                    hit = origin + direction * t;
                    wallNormal = float3(0, 0, -1);
                }

                // Machined ribs running round the bore, with conduits glowing where the core lights them.
                float ribs = smoothstep(0.35, 0.5, abs(frac(hit.z * 14.0) - 0.5));
                float angle = atan2(hit.y, hit.x);
                float conduits = pow(saturate(cos(angle * 6.0 + hit.z * 3.0)), 24.0);
                float3 toOrb = orbCenter - hit;
                float orbLight = saturate(dot(wallNormal, normalize(toOrb))) / (1.0 + dot(toOrb, toOrb) * 40.0);
                float live = 1.0 - collapse;
                float3 wall = float3(0.035, 0.04, 0.055) * (0.6 + 0.4 * ribs);
                wall += kOrbBlue * orbLight * 2.2 * live;
                wall += _GlowColor.rgb * conduits * (0.4 + 0.6 * _Electric) * live *
                        (0.6 + 0.4 * sin(_CoreTime * 12.0 - hit.z * 30.0));
                return wall + haze;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                ClipToDisc(IN.uv);
                float2 p = IN.uv * 2.0 - 1.0;
                float r = length(p);
                float angle = atan2(p.y, p.x);

                float3 cameraOS = TransformWorldToObject(GetCameraPositionWS());
                float3 viewOS = normalize(cameraOS - IN.positionOS);
                float electric = _Electric * (0.55 + 0.45 * OrbNoise(float3(_CoreTime * 30.0, angle * 2.0, 0.0)));

                // --- Outer frame: a raised brass ring with a glowing seam ---
                float aperture = _Aperture;
                if (r > aperture)
                {
                    float across = (r - aperture) / (1.0 - aperture);
                    float bevel = cos(across * PI);  // slopes outward then inward
                    float3 normal = normalize(float3(p / max(r, 1e-4) * bevel * 0.7, -1.0));
                    float bolts = step(0.93, cos(angle * 8.0)) * step(0.35, across) * step(across, 0.65);
                    float3 metal = ShadeMetal(_FrameColor.rgb * (1.0 - bolts * 0.5), normal, viewOS);
                    float seam = exp(-pow((across - 0.08) / 0.05, 2.0));
                    float3 glow = _GlowColor.rgb * seam * _GlowIntensity * (0.3 + 0.7 * max(_Open, electric));
                    return float4(metal + glow, 1.0);
                }

                // --- Iris: blades twisting back into the frame as it opens ---
                float open = _Open;
                float hole = aperture * smoothstep(0.0, 1.0, open);
                float blades = max(_BladeCount, 3.0);
                float3 origin = float3(p * 0.5, 0.0);
                if (r > hole)
                {
                    // Each blade is a curved sector; the curve (and a spin) swings them aside as the iris opens.
                    float swirl = angle / (2.0 * PI) * blades + r * 1.6 + open * 1.4;
                    float blade = frac(swirl);
                    float edge = min(blade, 1.0 - blade);
                    float seamLine = exp(-pow(edge / 0.035, 2.0));
                    float lip = exp(-pow((r - hole) / 0.035, 2.0)) * step(0.001, open);
                    // Each blade is tilted across its width, so neighbouring blades catch the light differently.
                    float3 normal = float3(p * 0.25 + float2(cos(angle), sin(angle)) * (blade - 0.5) * 0.5, -1.0);
                    float3 metal = ShadeMetal(_BladeColor.rgb * (0.75 + 0.5 * blade), normalize(normal), viewOS);
                    float3 glow = _GlowColor.rgb * _GlowIntensity *
                                  (seamLine * (0.15 + 0.85 * electric) + lip * (1.0 + electric));
                    return float4(metal * (1.0 - seamLine * 0.6) + glow, 1.0);
                }

                // --- Inside: the cavity and the core ---
                float3 direction = -viewOS;
                // Guard grazing angles: the ray must go into the chest.
                direction.z = max(direction.z, 0.05);
                direction = normalize(direction);
                float3 color = Cavity(origin, direction, aperture * 0.5);
                return float4(color, 1.0);
            }
            ENDHLSL
        }

        // Depth for URP's depth prepass / depth texture, clipped to the disc like the colour pass.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half frag(Varyings IN) : SV_Target
            {
                ClipToDisc(IN.uv);
                return IN.positionCS.z;
            }
            ENDHLSL
        }
    }
}
