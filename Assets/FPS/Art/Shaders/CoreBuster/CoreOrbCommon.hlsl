// Shared shading for the Unstable Core orb: a bright cyan rim, spiralling vortex arms, a dark eye and a blue haze.
// Used by the orb mesh (CoreBuster/Unstable Orb), its halo (CoreBuster/Core Halo) and the galaxy body shader,
// which draws the orb inside the chest, so the core looks the same inside the body and after it is torn out.
#ifndef COREBUSTER_CORE_ORB_INCLUDED
#define COREBUSTER_CORE_ORB_INCLUDED

static const float3 kOrbDeep = float3(0.01, 0.03, 0.15);
static const float3 kOrbBlue = float3(0.1, 0.36, 1.0);
static const float3 kOrbRim = float3(0.5, 0.85, 1.0);

float OrbHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float OrbNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(OrbHash(i + float3(0, 0, 0)), OrbHash(i + float3(1, 0, 0)), f.x),
                     lerp(OrbHash(i + float3(0, 1, 0)), OrbHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(OrbHash(i + float3(0, 0, 1)), OrbHash(i + float3(1, 0, 1)), f.x),
                     lerp(OrbHash(i + float3(0, 1, 1)), OrbHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float OrbFbm(float3 p)
{
    float sum = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 3; i++)
    {
        sum += OrbNoise(p) * amplitude;
        p = p * 2.03 + 7.1;
        amplitude *= 0.5;
    }
    return sum / 0.875;
}

// Two axes perpendicular to the view, for effects that should always face the viewer.
void OrbViewBasis(float3 viewDir, out float3 right, out float3 up)
{
    float3 reference = abs(viewDir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
    right = normalize(cross(reference, viewDir));
    up = cross(viewDir, right);
}

// Colour (HDR) of the orb's surface point with the given outward normal, seen along viewDir (surface -> eye).
// instability, surge and collapse (absorption) run 0..1.
float3 ShadeCoreOrb(float3 normal, float3 viewDir, float time, float instability, float surge, float collapse)
{
    float3 right, up;
    OrbViewBasis(viewDir, right, up);
    // Polar coordinates across the disc the viewer sees: 0 at the centre, 1 at the rim.
    float2 disc = float2(dot(normal, right), dot(normal, up));
    float radius = length(disc);
    float angle = atan2(disc.y, disc.x);

    // A vortex spiralling into the eye, churned by 3D noise so it never repeats; absorption winds it tighter.
    float churn = OrbFbm(normal * 3.0 + time * 0.35);
    float spin = time * (1.6 + 3.0 * collapse);
    float phase = angle * 2.0 + log(max(radius, 1e-3)) * (5.0 + 7.0 * collapse) - spin * 3.0 + churn * 4.5;
    // Thin luminous arcs with a soft glow around each.
    float wave = sin(phase) * 0.5 + 0.5;
    float arcs = (pow(wave, 10.0) + pow(wave, 3.0) * 0.25) * smoothstep(0.08, 0.55, radius);

    // Dark in the middle, a deep blue glow building toward the rim.
    float3 color = kOrbDeep + kOrbBlue * (0.12 + 0.35 * radius * radius);
    color += lerp(kOrbBlue, kOrbRim, 0.35) * arcs * (1.1 + 0.8 * instability);

    // The thin bright ring hugging the rim, crackling as it goes.
    float ringWidth = 0.045 + 0.04 * surge;
    float ring = exp(-pow((radius - 0.9) / ringWidth, 2.0));
    // Noise read from the direction around the rim (not the raw angle), so there is no seam where it wraps.
    float2 around = disc / max(radius, 1e-4);
    ring *= 0.65 + 0.7 * OrbNoise(float3(around * 3.0, time * 4.0 + churn * 3.0));
    color += kOrbRim * ring * (1.7 + 2.5 * surge);

    // Fresnel sheen and the dark eye in the middle.
    float facing = saturate(dot(normal, viewDir));
    color += kOrbBlue * pow(1.0 - facing, 4.0) * 0.8;
    color *= 1.0 - smoothstep(0.3, 0.0, radius) * 0.9 * (1.0 - collapse);

    float flicker = 1.0 + (OrbNoise(float3(time * 21.0, 0.0, 0.0)) - 0.5) * 0.45 * instability;
    return color * flicker * (1.0 + collapse * 1.6 + surge * 0.9);
}

// The blue haze and faint rays around the orb. passDistance is how close the view ray passes to the orb's
// centre in orb radii (1 = grazing the rim), angle is the direction of that pass around the orb.
float3 CoreOrbHaze(float passDistance, float angle, float time, float surge)
{
    float outside = max(passDistance - 1.0, 0.0);
    float glow = exp(-outside * 2.2);
    // Sampled around a circle so the rays wrap seamlessly.
    float2 around = float2(cos(angle), sin(angle));
    float rays = pow(OrbNoise(float3(around * 4.5, time * 0.6)), 3.0) * exp(-outside * 0.9);
    return kOrbBlue * (glow * 0.9 + rays * 0.8) * (1.0 + surge * 1.2);
}

#endif
