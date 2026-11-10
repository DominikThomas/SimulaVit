Shader "SimulaVit/GeodesicVertexColorURP"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _AmbientStrength("Geodesic Surface Ambient Strength", Range(0,1)) = 0.08
        _DiffuseStrength("Geodesic Surface Diffuse Strength", Range(0,2)) = 1
        _SunlightStrength("Sunlight Readability", Range(0,2)) = 1.25
        _AmbientTint("Cool Ambient Tint", Color) = (0.55,0.7,1,1)
        _NightAmbientStrength("Night Ambient Multiplier", Range(0,1)) = 0.5
        _TwilightTint("Twilight Light Tint", Color) = (1,0.8,0.6,1)
        _TwilightWarmth("Twilight Warmth", Range(0,0.3)) = 0.1
        [HideInInspector] _SedimentSeaLevelRadius("Sediment Sea Level Radius", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float4 sedimentTint : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color : COLOR;
                half4 sedimentTint : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _AmbientStrength;
            float _DiffuseStrength;
            float _SunlightStrength;
            float4 _AmbientTint;
            float _NightAmbientStrength;
            float4 _TwilightTint;
            float _TwilightWarmth;
            float _SedimentSeaLevelRadius;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.sedimentTint = IN.sedimentTint;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 normalWS = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                float3 radial = normalize(IN.positionWS - TransformObjectToWorld(float3(0,0,0)));
                half elevation = dot(radial, mainLight.direction);
                half day = smoothstep(-0.08h, 0.2h, elevation);
                half twilight = exp(-elevation * elevation / 0.0144h) * _TwilightWarmth;
                half3 sunlight = mainLight.color * lerp(half3(1,1,1), _TwilightTint.rgb, twilight);
                // DiffuseStrength=0 is also used by unlit diagnostic/river materials.
                half3 ambient = lerp(half3(1,1,1), _AmbientTint.rgb * lerp(_NightAmbientStrength, 1.0h, day), saturate(_DiffuseStrength));
                half3 illumination = _AmbientStrength * ambient
                    + sunlight * (diffuse * _DiffuseStrength * _SunlightStrength * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                float4 terrainColour = IN.color;
                if (_SedimentSeaLevelRadius > 0.0 && IN.sedimentTint.x > 0.0h)
                {
                    float3 positionOS = TransformWorldToObject(IN.positionWS);
                    if (dot(positionOS, positionOS) < _SedimentSeaLevelRadius * _SedimentSeaLevelRadius)
                        terrainColour.rgb = terrainColour.rgb * (1.0h - IN.sedimentTint.x) + IN.sedimentTint.yzw;
                }
                half4 col = terrainColour * _BaseColor;
                return half4(col.rgb * illumination, col.a);
            }
            ENDHLSL
        }
    }
}
