#ifndef SIMULAVIT_PLANET_WATER_LIGHTING_INCLUDED
#define SIMULAVIT_PLANET_WATER_LIGHTING_INCLUDED

// Existing environment cubemap plus an inexpensive Main Light glint. No screen
// reflection, generated texture, or additional camera is required.
half3 PlanetWaterReflection(half3 normalWS, half3 viewWS, Light sun,
    half smoothness, half fresnelStrength, half fresnelPower,
    half reflectionStrength, half glintStrength, half3 skyTint)
{
    half nv = saturate(dot(normalWS, viewWS));
    half nl = saturate(dot(normalWS, sun.direction));
    half fresnel = lerp(0.02h, 0.12h, saturate(fresnelStrength));
    fresnel += (1.0h - fresnel) * pow(1.0h - nv, max(1.0h, fresnelPower));
    half3 reflected = reflect(-viewWS, normalWS);
    half3 environment = GlossyEnvironmentReflection(reflected, 1.0h - smoothness, 1.0h);
    environment /= 1.0h + max(environment.r, max(environment.g, environment.b));
    // A restrained sky fill keeps Fresnel legible when space has no baked probe.
    half day = smoothstep(-0.08h, 0.2h, dot(normalWS, sun.direction));
    half3 sky = skyTint * (0.015h + 0.22h * day) * sun.color;
    half3 reflection = (environment * skyTint * (0.05h + 0.95h * day) + sky) * fresnel * reflectionStrength;
    half3 halfway = SafeNormalize(sun.direction + viewWS);
    float exponent = lerp(32.0, 768.0, smoothness * smoothness);
    float glint = pow(saturate(dot(normalWS, halfway)), exponent);
    half glintFresnel = 0.15h + 0.85h * pow(1.0h - saturate(dot(viewWS, halfway)), 5.0h);
    return reflection + sun.color * (glint * glintFresnel * glintStrength * nl
        * sun.distanceAttenuation * sun.shadowAttenuation);
}
#endif
