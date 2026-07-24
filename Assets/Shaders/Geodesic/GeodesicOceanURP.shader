Shader "SimulaVit/GeodesicOceanURP"
{
    Properties
    {
        _BaseColor ("Base Water Color", Color) = (0.15966536, 0.30129746, 0.49056602, 0.58)
        _ShallowColor ("Shallow Tint", Color) = (0.10, 0.55, 0.75, 0.58)
        _DeepColor ("Deep Tint", Color) = (0.02, 0.12, 0.28, 0.58)
        _Opacity ("Opacity", Range(0, 1)) = 0.58
        _Smoothness ("Smoothness", Range(0, 1)) = 0.876
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.18
        _FresnelPower ("Fresnel Power", Range(0.001, 8)) = 3
        _AmbientResponse ("Ambient Response", Range(0, 2)) = 1
        _ColorIntensity ("Color Intensity", Range(0, 3)) = 1
        _LightingAmbientStrength ("Lighting Ambient Strength", Range(0, 1)) = 0.08
        _LightingDiffuseStrength ("Lighting Diffuse Strength", Range(0, 2)) = 1
        _Fe2Tint ("Dissolved Fe2 Tint", Color) = (0.18, 0.38, 0.52, 1)
        _FeOxTint ("Suspended FeOx Tint", Color) = (0.72, 0.36, 0.12, 1)
        _SulfurTint ("Suspended Sulfur Tint", Color) = (0.95, 0.82, 0.20, 1)
        _Turbidity ("Turbidity", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShallowColor;
                half4 _DeepColor;
                half _Opacity;
                half _Smoothness;
                half _FresnelStrength;
                half _FresnelPower;
                half _AmbientResponse;
                half _ColorIntensity;
                half _LightingAmbientStrength;
                half _LightingDiffuseStrength;
                half4 _Fe2Tint;
                half4 _FeOxTint;
                half4 _SulfurTint;
                half _Turbidity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half3 viewDirWS : TEXCOORD1;
                half depth01 : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = pos.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(pos.positionWS);
                output.depth01 = saturate(input.color.r);
                output.positionWS = pos.positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(input.viewDirWS);
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
                half depth01 = saturate(input.depth01);
                half3 depthColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
                half3 color = lerp(depthColor, _BaseColor.rgb, saturate(0.25 + fresnel * 0.35));
                color = lerp(color, _DeepColor.rgb, saturate(_Turbidity * 0.35));
                color *= _AmbientResponse * _ColorIntensity;
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 illumination = _LightingAmbientStrength.xxx
                    + mainLight.color * (diffuse * _LightingDiffuseStrength * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                color = color * illumination + fresnel * _FresnelStrength * illumination;
                return half4(color, _Opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
