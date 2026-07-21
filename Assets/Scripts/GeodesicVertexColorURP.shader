Shader "SimulaVit/GeodesicVertexColorURP"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _LightDirection("Light Direction", Vector) = (0.35,0.75,0.55,0)
        _AmbientStrength("Ambient Strength", Range(0,1)) = 0.35
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
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _LightDirection;
            float _AmbientStrength;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 normalWS = normalize(IN.normalWS);
                half3 lightDir = normalize(_LightDirection.xyz);
                half diffuse = saturate(dot(normalWS, lightDir));
                half light = saturate(_AmbientStrength + diffuse * (1.0h - _AmbientStrength));
                half4 col = IN.color * _BaseColor;
                return half4(col.rgb * light, col.a);
            }
            ENDHLSL
        }
    }
}
