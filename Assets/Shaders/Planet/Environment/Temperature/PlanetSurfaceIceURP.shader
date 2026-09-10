Shader "SimulaVit/PlanetSurfaceIceVertexURP"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _IceColor("Ice Color", Color) = (0.9,0.95,1,1)
        _IceStrength("Ice Strength", Range(0,2)) = 1
        _ForceVertexIcePreview("Force Vertex Ice Preview", Float) = 0
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _IceColor;
            float4 _BaseMap_ST;
            float _IceStrength;
            float _ForceVertexIcePreview;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                half iceAmount = saturate(IN.color.a);
                half blend = saturate(iceAmount * _IceStrength);

                if (_ForceVertexIcePreview > 0.5)
                {
                    return half4(iceAmount, iceAmount, iceAmount, 1.0h);
                }

                half3 col = lerp(baseCol.rgb, _IceColor.rgb, blend);
                return half4(col, baseCol.a);
            }
            ENDHLSL
        }
    }
}
