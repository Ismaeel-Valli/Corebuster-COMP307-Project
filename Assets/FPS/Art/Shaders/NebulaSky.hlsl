// The procedural nebula sky as a function of view direction, shared by the skybox (ProceduralNebulaSkybox_URP.shader)
// and the corridor windows that look out onto it (CoreBuster/Sky Window), so both draw the same sky: the same clouds,
// core glow and twinkling stars, drifting together.
#ifndef COREBUSTER_NEBULA_SKY_INCLUDED
#define COREBUSTER_NEBULA_SKY_INCLUDED

struct NebulaSkyParams
{
    half3 skyColorTop;
    half3 skyColorBottom;
    half3 nebulaColorA;
    half3 nebulaColorB;
    half3 nebulaColorCore;
    float nebulaDensity;
    float nebulaScale;
    float nebulaSharpness;
    float coreIntensity;
    float coreSize;
    float3 coreDirection;
    float starDensity;
    float starBrightness;
    float starTwinkleSpeed;
    float scrollSpeed;
};

// ---- Hash / noise helpers ----
float NebulaHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float NebulaNoise3(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = NebulaHash13(i + float3(0,0,0));
    float n100 = NebulaHash13(i + float3(1,0,0));
    float n010 = NebulaHash13(i + float3(0,1,0));
    float n110 = NebulaHash13(i + float3(1,1,0));
    float n001 = NebulaHash13(i + float3(0,0,1));
    float n101 = NebulaHash13(i + float3(1,0,1));
    float n011 = NebulaHash13(i + float3(0,1,1));
    float n111 = NebulaHash13(i + float3(1,1,1));

    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);

    float nxy0 = lerp(nx00, nx10, f.y);
    float nxy1 = lerp(nx01, nx11, f.y);

    return lerp(nxy0, nxy1, f.z);
}

// Fractal Brownian Motion — layers of noise for cloud-like detail
float NebulaFbm(float3 p)
{
    float total = 0.0;
    float amp = 0.5;
    float freq = 1.0;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        total += NebulaNoise3(p * freq) * amp;
        freq *= 2.02;
        amp *= 0.5;
    }
    return total;
}

half3 NebulaSkyColor(float3 dir, NebulaSkyParams s)
{
    dir = normalize(dir);

    // --- Base sky gradient (vertical) ---
    float vertT = saturate(dir.y * 0.5 + 0.5);
    half3 sky = lerp(s.skyColorBottom, s.skyColorTop, vertT);

    // --- Nebula cloud shape via layered fbm ---
    float3 p = dir * s.nebulaScale + float3(0, 0, _Time.y * s.scrollSpeed);
    float n1 = NebulaFbm(p);
    float n2 = NebulaFbm(p * 1.7 + 11.3);
    float cloud = NebulaFbm(p + n1 * 0.8 + n2 * 0.4);
    cloud = pow(saturate(cloud), s.nebulaSharpness) * s.nebulaDensity;

    // --- Warm/cool color mix across the sky using a second noise axis ---
    float colorMix = saturate(NebulaFbm(p * 0.5 + 5.0) * 1.4 - 0.2);
    half3 nebulaColor = lerp(s.nebulaColorA, s.nebulaColorB, colorMix);

    // --- Bright core / hotspot glow ---
    float3 coreDir = normalize(s.coreDirection + 0.0001);
    float coreDot = saturate(dot(dir, coreDir));
    float core = pow(coreDot, 1.0 / max(s.coreSize, 0.001));
    half3 coreColor = s.nebulaColorCore * core * s.coreIntensity;

    half3 col = sky + nebulaColor * cloud + coreColor;

    // --- Starfield ---
    float starCell = NebulaHash13(floor(dir * 800.0));
    float starMask = step(1.0 - s.starDensity, starCell);
    float twinkle = 0.6 + 0.4 * sin(_Time.y * s.starTwinkleSpeed * 6.28 + starCell * 123.4);
    float starBrightness = NebulaHash13(floor(dir * 800.0) + 7.0) * s.starBrightness * twinkle;
    half3 starColor = lerp(half3(1,1,1), half3(0.7,0.85,1), NebulaHash13(floor(dir*800.0)+3.0));
    col += starMask * starBrightness * starColor * (1.0 - cloud * 0.5);

    return col;
}

// ---- The black hole ----
// A Schwarzschild hole one unit from the camera (so angles and distances near it are interchangeable), with a thin
// accretion disk. Rays are bent towards it by an angle that grows from the weak-field 2rs/b without bound towards the
// capture radius; rays inside that radius fall in (the shadow). The disk's near side is drawn by the straight ray in
// front of the hole, its far side by the bent ray behind it: light from the back of the disk curves over and under the
// shadow, which is the arc that frames a lensed black hole.
struct BlackHoleParams
{
    float3 direction;   // towards the hole
    float size;         // the shadow's angular radius (radians)
    float diskInner;    // the disk's inner and outer radius, in shadow radii
    float diskOuter;
    float diskTilt;     // degrees the disk's upper face is tipped towards the camera (0 = edge-on)
    float diskRoll;     // degrees the disk is rolled about the line of sight
    half3 hotColor;     // the disk's inner edge
    half3 coolColor;    // and its outer reaches
    float intensity;
    float spin;         // orbital speed at the inner edge (radians per second)
    float doppler;      // how much brighter the side turning towards the camera burns
    float lens;         // bending strength (1 = physical)
};

// The disk's angular speed (radians per second) at r shadow radii: Keplerian, b.spin at the inner edge.
float BlackHoleOmega(float r, BlackHoleParams b)
{
    return b.spin * pow(b.diskInner / r, 1.5);
}

// One generation of the disk's gas: streaks seeded afresh each cycle, then wound by Keplerian shear for `age` seconds.
float BlackHoleGas(float phi, float r, float age, float cycle, BlackHoleParams b)
{
    float3 seed = float3(NebulaHash13(float3(cycle, 1.7, 0.3)), NebulaHash13(float3(cycle, 4.1, 0.7)),
        NebulaHash13(float3(cycle, 9.3, 0.1))) * 40.0;
    float swirl = phi + age * BlackHoleOmega(r, b) + 1.8 * log(r);
    float2 ring = float2(cos(swirl), sin(swirl));
    return NebulaNoise3(float3(ring * 2.5, r * 1.7) + seed) * 0.6 + NebulaNoise3(float3(ring * 7.0, r * 5.3 + 3.1) + seed.zxy) * 0.4;
}

// Hot spots: clumps of gas that flare up near the inner edge, ride their orbit for a few seconds, smeared into an arc
// by the shear as they go, and fade. Each slot respawns at a new radius and angle every lifetime.
float BlackHoleHotSpots(float phi, float r, BlackHoleParams b)
{
    const float life = 7.0;
    float total = 0.0;
    [unroll]
    for (int k = 0; k < 4; k++)
    {
        float clock = _Time.y / life + k * 0.25;
        float cycle = floor(clock), age = frac(clock);
        float3 id = float3(cycle, k, 2.3);
        float radius = lerp(b.diskInner * 1.1, b.diskInner * 2.6, NebulaHash13(id));
        float angle = NebulaHash13(id + 5.1) * 6.2832 + BlackHoleOmega(radius, b) * age * life;
        float d = phi - angle;
        d -= 6.2832 * round(d / 6.2832);
        float along = d * radius / (0.5 + 1.2 * age);
        float across = (r - radius) / 0.3;
        float fade = sin(age * 3.14159);
        total += exp(-along * along - across * across) * fade * fade;
    }
    return total;
}

// The disk where the ray o + d t crosses its plane (0 < t < maxT): emission, and coverage in alpha.
half4 BlackHoleDisk(float3 o, float3 d, float3 hole, float3 n, float3 u, float3 v, BlackHoleParams b, float maxT)
{
    float facing = dot(d, n);
    if (abs(facing) < 1e-5) return 0;
    float t = dot(hole - o, n) / facing;
    if (t <= 0.0 || t > maxT) return 0;
    float3 h = o + d * t - hole;
    float r = length(h) / b.size;
    float edge = smoothstep(b.diskInner, b.diskInner * 1.12, r) * (1.0 - smoothstep(b.diskOuter * 0.55, b.diskOuter, r));
    if (edge <= 0.0) return 0;

    // Keplerian shear: the inner gas laps the outer, winding the streaks into a spiral. Wound for ever, the streaks
    // tighten past the pixel and turn to static, so the gas is two generations, each living one period, cross-faded
    // half a period apart: the disk keeps turning and shearing but never winds up. The blend is renormalised so the
    // streaks keep their contrast mid-fade.
    float phi = atan2(dot(h, v), dot(h, u));
    const float period = 16.0;
    float clock = _Time.y / period;
    float ageA = frac(clock), ageB = frac(clock + 0.5);
    float wA = 1.0 - abs(2.0 * ageA - 1.0), wB = 1.0 - wA;
    float gasA = BlackHoleGas(phi, r, ageA * period, floor(clock), b);
    float gasB = BlackHoleGas(phi, r, ageB * period, floor(clock + 0.5) + 0.5, b);
    float streaks = saturate((gasA * wA + gasB * wB - 0.5) / sqrt(wA * wA + wB * wB) * 2.2 + 0.5);

    // Turbulence: bands of the disk brighten and dim as they churn.
    float flicker = 0.8 + 0.4 * NebulaNoise3(float3(r * 2.2, 0.5, _Time.y * 0.9));

    float temperature = saturate((r - b.diskInner) / (b.diskOuter - b.diskInner));
    half3 col = lerp(b.hotColor, b.coolColor, pow(temperature, 0.55));
    float glow = pow(1.0 - temperature, 1.7) * (0.3 + 1.2 * streaks) * flicker;
    float spots = BlackHoleHotSpots(phi, r, b);
    col = lerp(col, b.hotColor, saturate(spots));
    glow += spots * 2.0 * pow(1.0 - temperature, 0.6);

    // Doppler beaming: gas orbiting towards the camera burns brighter and bluer, the receding side dims.
    float3 velocity = normalize(cross(n, h));
    float beam = max(1.0 + b.doppler * dot(velocity, -d), 0.05);
    beam = beam * beam * beam;
    col = lerp(col, col * half3(0.82, 0.92, 1.25), saturate(beam - 1.0) * 0.4);
    // A soft knee above 1: the beamed side otherwise burns to flat white, and its streaks and hot spots with it.
    float light = glow * beam * b.intensity;
    float over = max(light - 1.0, 0.0);
    light = min(light, 1.0) + over / (1.0 + over * 0.6);
    return half4(col * light, edge);
}

// The nebula sky with the black hole in it.
half3 BlackHoleSkyColor(float3 dir, NebulaSkyParams s, BlackHoleParams b)
{
    dir = normalize(dir);
    float3 hole = normalize(b.direction);
    float closest = dot(dir, hole);
    // Far from the hole (or with no hole) the sky is the plain sky.
    float reach = b.size * b.diskOuter * 1.5;
    if (b.size <= 0.0 || closest < cos(reach)) return NebulaSkyColor(dir, s);

    float3 nearest = dir * closest;
    float3 offset = nearest - hole;
    float impact = length(offset);
    float3 outward = offset / max(impact, 1e-6);

    // The disk's frame: u across the view, n its normal (up, tipped towards the camera), both rolled.
    float3 u0 = normalize(cross(hole, float3(0, 1, 0)));
    float3 v0 = cross(u0, hole);
    float roll = radians(b.diskRoll), tilt = radians(b.diskTilt);
    float3 u = u0 * cos(roll) + v0 * sin(roll);
    float3 up = v0 * cos(roll) - u0 * sin(roll);
    float3 n = normalize(up * cos(tilt) - hole * sin(tilt));
    float3 v = cross(n, u);

    // Bending: 2rs/b far out, steepening sharply towards the capture radius; faded out at the edge of its reach.
    float capture = b.size;
    float rs = capture / 2.598;
    float x = capture / max(impact, capture * 1.0001);
    float alpha = 2.0 * rs / max(impact, 1e-5) * (1.0 + 1.8 * pow(x, 6.0)) * b.lens;
    // Faded from the disk's edge out to the reach, so the sky beyond is untouched and never folds over itself.
    alpha = min(alpha, 3.0) * smoothstep(reach, b.size * b.diskOuter * 0.8, impact);
    float3 bent = normalize(dir * cos(alpha) - outward * sin(alpha));
    // The background is bent more gently away from the shadow: at full strength the nebula a few disk radii out
    // smeared into a broad Einstein ring of concentric bands. Close in, it takes the full bend (the tight lensed rim).
    float alphaSky = alpha * lerp(0.3, 1.0, x * x);
    float3 bentSky = normalize(dir * cos(alphaSky) - outward * sin(alphaSky));

    half3 col = 0;
    if (impact > capture)
    {
        col = NebulaSkyColor(bentSky, s);
        half4 back = BlackHoleDisk(nearest, bent, hole, n, u, v, b, 10.0);
        col = lerp(col, back.rgb, back.a);
    }
    // The photon ring: light that orbited once before escaping, a thin bright rim on the shadow.
    float rim = (impact - capture) / (capture * 0.05);
    col += b.hotColor * exp(-rim * rim) * b.intensity * 0.5;

    half4 front = BlackHoleDisk(0, dir, hole, n, u, v, b, closest);
    return lerp(col, front.rgb, front.a);
}

#endif
