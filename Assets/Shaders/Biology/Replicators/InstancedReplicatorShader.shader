Shader "Custom/InstancedReplicator"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor("Emission Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        // Set up for transparency so they can fade out
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing // This allows the shader to handle many instances at once

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID // This is required to identify which agent is being drawn
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            // This block allows each instance to have its OWN unique property in the batch
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _EmissionColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // Fetch the unique color data sent from ReplicatorManager.cs
                float4 baseCol = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                float4 emissiveCol = UNITY_ACCESS_INSTANCED_PROP(Props, _EmissionColor);
                
                // Output the unique RGB (emission) and Alpha (transparency)
                return float4(emissiveCol.rgb, baseCol.a);
            }
            ENDHLSL
        }
    }
}