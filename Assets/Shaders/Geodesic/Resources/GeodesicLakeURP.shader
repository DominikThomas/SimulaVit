Shader "SimulaVit/GeodesicLakeURP"
{
    Properties
    {
        _BaseColor("Lake water", Color) = (0.06, 0.38, 0.58, 0.85)
        _Smoothness("Smoothness", Range(0,1)) = 0.92
        _FresnelStrength("Fresnel Strength", Range(0,1)) = 0.22
        _FresnelPower("Fresnel Power", Range(1,8)) = 5
        _ReflectionStrength("Environment Reflection Strength", Range(0,2)) = 0.85
        _SunGlintStrength("Sun Glint Strength", Range(0,8)) = 4
        _ReflectionTint("Sky Reflection Fill", Color) = (0.3, 0.5, 0.7, 1)
        _AmbientStrength("Ambient Strength", Range(0,1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Offset -1, -1
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "../PlanetWaterLighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                half _FresnelStrength;
                half _FresnelPower;
                half _ReflectionStrength;
                half _SunGlintStrength;
                half4 _ReflectionTint;
                half _AmbientStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 normalWS = normalize(input.normalWS);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 illumination = _AmbientStrength.xxx + light.color * (saturate(dot(normalWS, light.direction))
                    * light.distanceAttenuation * light.shadowAttenuation);
                half3 colour = _BaseColor.rgb * illumination + PlanetWaterReflection(normalWS, viewWS, light,
                    _Smoothness, _FresnelStrength, _FresnelPower, _ReflectionStrength, _SunGlintStrength, _ReflectionTint.rgb);
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewWS)), _FresnelPower);
                return half4(colour, lerp(_BaseColor.a, 1.0h, saturate(fresnel * _ReflectionStrength)));
            }
            ENDHLSL
        }
    }
}
