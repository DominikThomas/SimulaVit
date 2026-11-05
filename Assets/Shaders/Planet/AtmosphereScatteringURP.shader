Shader "SimulaVit/AtmosphereScatteringURP"
{
    Properties
    {
        _Intensity ("Scattering Intensity", Range(0, 4)) = 1.15
        _RayleighColor ("Rayleigh Colour", Color) = (0.12, 0.42, 1, 1)
        _SunsetColor ("Sunset Colour", Color) = (1, 0.24, 0.035, 1)
        _SunsetStrength ("Sunset Strength", Range(0, 2)) = 1
        _MieStrength ("Forward Scattering", Range(0, 1)) = 0.08
        _Density ("Optical Density", Range(0, 3)) = 0.65
        _DensityFalloff ("Density Falloff / Rim Thinness", Range(1, 12)) = 4.5
        _TerminatorSoftness ("Day Night Softness", Range(0.005, 0.2)) = 0.035
        _NightGlow ("Night Residual Glow", Range(0, 0.1)) = 0.003
        [HideInInspector] _PlanetRadius ("Planet Radius (Local)", Float) = 1
        [HideInInspector] _AtmosphereRadius ("Atmosphere Radius (Local)", Float) = 1.04
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "AtmosphericScattering"
            Tags { "LightMode"="UniversalForwardOnly" }
            // One exit face per ray, including when the camera enters the shell.
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RayleighColor;
                float4 _SunsetColor;
                float _Intensity;
                float _SunsetStrength;
                float _MieStrength;
                float _Density;
                float _DensityFalloff;
                float _TerminatorSoftness;
                float _NightGlow;
                float _PlanetRadius;
                float _AtmosphereRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float2 SphereInterval(float3 origin, float3 ray, float radius)
            {
                float b = dot(origin, ray);
                // Cross product avoids cancellation for distant cameras.
                float3 perpendicular = cross(origin, ray);
                float discriminant = radius * radius - dot(perpendicular, perpendicular);
                float root = sqrt(max(0.0, discriminant));
                return discriminant >= 0.0 ? float2(-b - root, -b + root) : float2(1e10, -1e10);
            }

            // Fixed quadrature, not a marching loop. All distances use outer radius = 1.
            float4 ScatteringSample(float3 position, float3 sun, float planet, float thickness, float sunsetView)
            {
                float radius = max(length(position), planet);
                float altitude = saturate((radius - planet) / thickness);
                float density = exp(-altitude * _DensityFalloff) * (1.0 - altitude) * (1.0 - altitude);
                float mu = dot(position / max(radius, 1e-5), sun);
                // The apparent horizon dips with altitude. This is the planet's curved shadow,
                // rather than a normal-dot-light mask glued to the shell surface.
                float horizon = -sqrt(saturate(1.0 - planet * planet / (radius * radius)));
                float sunlight = smoothstep(horizon - _TerminatorSoftness, horizon + _TerminatorSoftness, mu);
                float sunExit = SphereInterval(position, sun, 1.0).y;
                float sunColumn = max(0.0, sunExit) / thickness * density;
                float warm = saturate((1.0 - exp(-sunColumn * 0.55)) * _SunsetStrength * sunsetView);
                warm *= 1.0 - smoothstep(0.02, 0.3, mu);
                // Long sunlight paths lose blue first, then become orange and dim in shadow.
                // Traverse cyan and gold explicitly: direct blue/orange interpolation turns violet.
                float3 cyan = float3(_RayleighColor.r * 0.5, max(_RayleighColor.g, _RayleighColor.b * 0.6), _RayleighColor.b);
                float3 gold = float3(_SunsetColor.r, max(_SunsetColor.g, _SunsetColor.r * 0.58), _SunsetColor.b);
                float3 cool = lerp(_RayleighColor.rgb, cyan, smoothstep(0.0, 0.25, warm));
                float3 warmTint = lerp(gold, _SunsetColor.rgb, smoothstep(0.55, 1.0, warm));
                float3 tint = lerp(cool, warmTint, smoothstep(0.18, 0.65, warm));
                float transmission = exp(-sunColumn * 0.035);
                float3 scatter = tint * sunlight * transmission;
                return float4(scatter * density, density * sunlight);
            }

            half4 frag(Varyings input, out float fragmentDepth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                #if UNITY_REVERSED_Z
                    fragmentDepth = 1.0;
                #else
                    fragmentDepth = 0.0;
                #endif
                float outer = max(_AtmosphereRadius, 1e-4);
                float planet = clamp(_PlanetRadius / outer, 0.01, 0.9999);
                float thickness = 1.0 - planet;
                float3 cameraWS = GetCameraPositionWS();
                float3 rayWS = normalize(input.positionWS - cameraWS);
                // Orthographic rays are parallel; shift their origin onto the camera plane.
                if (unity_OrthoParams.w > 0.5)
                {
                    rayWS = -UNITY_MATRIX_V[2].xyz;
                    cameraWS = input.positionWS - rayWS * dot(input.positionWS - cameraWS, rayWS);
                }
                float3 origin = TransformWorldToObject(cameraWS) / outer;
                float3 ray = normalize(mul((float3x3)unity_WorldToObject, rayWS));
                float2 interval = SphereInterval(origin, ray, 1.0);
                float start = max(0.0, interval.x);
                float end = interval.y;
                // Test the volume's entry against ordinary scene depth, rather than
                // its rasterized exit face. Inside the atmosphere this is the near
                // plane. This preserves foreground occlusion without a depth texture.
                if (start > 0.0)
                {
                    float3 entryWS = TransformObjectToWorld((origin + ray * start) * outer);
                    float4 entryCS = TransformWorldToHClip(entryWS);
                    float depth = entryCS.z / max(entryCS.w, 1e-5);
                    #if !UNITY_REVERSED_Z
                        depth = (depth - UNITY_NEAR_CLIP_VALUE) / (1.0 - UNITY_NEAR_CLIP_VALUE);
                    #endif
                    fragmentDepth = saturate(depth);
                }
                float2 ground = SphereInterval(origin, ray, planet);
                if (ground.x > 0.0) end = min(end, ground.x);
                // No atmosphere below the analytic sea/ground surface (also underwater).
                if (dot(origin, origin) < planet * planet) return 0;

                float path = max(0.0, end - start);
                if (path <= 0.0) return 0;

                Light mainLight = GetMainLight();
                float3 sun = SafeNormalize(mul((float3x3)unity_WorldToObject, mainLight.direction));
                float cosine = dot(rayWS, mainLight.direction);
                float sunsetView = smoothstep(-0.8, -0.15, cosine);
                float4 a = ScatteringSample(origin + ray * (start + path * 0.1127016654), sun, planet, thickness, sunsetView);
                float4 b = ScatteringSample(origin + ray * (start + path * 0.5), sun, planet, thickness, sunsetView);
                float4 c = ScatteringSample(origin + ray * (start + path * 0.8872983346), sun, planet, thickness, sunsetView);
                float4 integral = (a * (5.0 / 18.0) + b * (8.0 / 18.0) + c * (5.0 / 18.0)) * path / thickness;
                float column = max(0.0, integral.a);
                float opticalDepth = column * _Density * 0.16;
                float opacity = 1.0 - exp(-opticalDepth);
                float rayleighPhase = 0.75 * (1.0 + cosine * cosine);
                // Bounded forward lobe avoids a blown-out ring when the Sun is behind the planet.
                float miePhase = pow(saturate(cosine), 12.0) * _MieStrength;
                float3 colour = integral.rgb / max(column, 1e-5);
                float3 radiance = colour * mainLight.color * mainLight.distanceAttenuation
                    * (rayleighPhase + miePhase) * opacity * _Intensity;
                float residual = (1.0 - exp(-path / thickness * 0.02)) * _NightGlow * _Density;
                radiance += _RayleighColor.rgb * residual * _Intensity;
                // Premultiplied, bounded extinction preserves terrain, water and stars.
                return half4(radiance, saturate((opacity + residual) * _Intensity));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
