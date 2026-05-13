Shader "SimulaVit/PlanetSurfaceIceURP"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _IceMask("Ice Mask", 2D) = "black" {}
        _IceColor("Ice Color", Color) = (0.9,0.95,1,1)
        _IceStrength("Ice Strength", Range(0,2)) = 1
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS: POSITION; float3 normalOS: NORMAL; float2 uv: TEXCOORD0; };
            struct Varyings { float4 positionHCS: SV_POSITION; float2 uv: TEXCOORD0; };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_IceMask); SAMPLER(sampler_IceMask);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _IceColor;
            float4 _BaseMap_ST;
            float4 _IceMask_ST;
            float _IceStrength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                half mask = SAMPLE_TEXTURE2D(_IceMask, sampler_IceMask, IN.uv).r;
                half blend = saturate(mask * _IceStrength);
                half3 col = lerp(baseCol.rgb, _IceColor.rgb, blend);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
