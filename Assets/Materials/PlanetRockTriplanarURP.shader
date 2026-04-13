Shader "Custom/PlanetRockTriplanarURP"
{
    Properties
    {
        [MainTexture] _BaseMap("Macro Surface (RGBA: color + land mask)", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Base Smoothness", Range(0, 1)) = 0.12

        [NoScaleOffset] _DetailAlbedoMap("Detail Albedo", 2D) = "gray" {}
        [NoScaleOffset] _DetailNormalMap("Detail Normal", 2D) = "bump" {}
        [NoScaleOffset] _DetailHeightMap("Detail Height", 2D) = "gray" {}
        _DetailTiling("Detail Tiling", Float) = 28
        _DetailStrength("Detail Strength", Range(0, 2)) = 0.5
        _DetailNormalStrength("Detail Normal Strength", Range(0, 2)) = 0.9
        _DetailHeightStrength("Detail Height Strength", Range(0, 0.2)) = 0.02
        _ApplyDetailToLandOnly("Apply Detail To Land Only", Float) = 1
        _DetailFadeStartDistance("Detail Fade Start Distance", Float) = 18
        _DetailFadeEndDistance("Detail Fade End Distance", Float) = 120
        _UseParallaxMapping("Use Height Relief Enhancement", Float) = 0
        _ParallaxMinDistance("Parallax Min Distance", Float) = 4
        _ParallaxMaxDistance("Parallax Max Distance", Float) = 32
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float _DetailTiling;
                float _DetailStrength;
                float _DetailNormalStrength;
                float _DetailHeightStrength;
                float _ApplyDetailToLandOnly;
                float _DetailFadeStartDistance;
                float _DetailFadeEndDistance;
                float _UseParallaxMapping;
                float _ParallaxMinDistance;
                float _ParallaxMaxDistance;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DetailAlbedoMap); SAMPLER(sampler_DetailAlbedoMap);
            TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
            TEXTURE2D(_DetailHeightMap); SAMPLER(sampler_DetailHeightMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalize(normalInputs.normalWS);
                output.uv = input.uv;
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            float3 BlendTriplanarWeights(float3 normalWS)
            {
                float3 n = abs(normalWS);
                n = pow(n, 4.0);
                return n / max(dot(n, 1.0), 1e-5);
            }

            float4 SampleTriplanarAlbedo(TEXTURE2D_PARAM(tex, samp), float3 positionWS, float3 weights, float tiling)
            {
                float2 uvX = positionWS.zy * tiling;
                float2 uvY = positionWS.xz * tiling;
                float2 uvZ = positionWS.xy * tiling;

                float4 x = SAMPLE_TEXTURE2D(tex, samp, uvX);
                float4 y = SAMPLE_TEXTURE2D(tex, samp, uvY);
                float4 z = SAMPLE_TEXTURE2D(tex, samp, uvZ);
                return x * weights.x + y * weights.y + z * weights.z;
            }

            float3 SampleTriplanarNormal(float3 positionWS, float3 normalWS, float3 weights, float tiling)
            {
                float2 uvX = positionWS.zy * tiling;
                float2 uvY = positionWS.xz * tiling;
                float2 uvZ = positionWS.xy * tiling;

                float3 nX = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uvX), _DetailNormalStrength);
                float3 nY = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uvY), _DetailNormalStrength);
                float3 nZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uvZ), _DetailNormalStrength);

                float3 wX = normalize(float3(normalWS.x >= 0 ? 1 : -1, nX.y, nX.x));
                float3 wY = normalize(float3(nY.x, normalWS.y >= 0 ? 1 : -1, nY.y));
                float3 wZ = normalize(float3(nZ.x, nZ.y, normalWS.z >= 0 ? 1 : -1));

                return normalize(wX * weights.x + wY * weights.y + wZ * weights.z);
            }

            float SampleTriplanarHeight(float3 positionWS, float3 weights, float tiling)
            {
                float2 uvX = positionWS.zy * tiling;
                float2 uvY = positionWS.xz * tiling;
                float2 uvZ = positionWS.xy * tiling;

                float hX = SAMPLE_TEXTURE2D(_DetailHeightMap, sampler_DetailHeightMap, uvX).r;
                float hY = SAMPLE_TEXTURE2D(_DetailHeightMap, sampler_DetailHeightMap, uvY).r;
                float hZ = SAMPLE_TEXTURE2D(_DetailHeightMap, sampler_DetailHeightMap, uvZ).r;
                return hX * weights.x + hY * weights.y + hZ * weights.z;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float cameraDistance = distance(_WorldSpaceCameraPos.xyz, input.positionWS);

                float detailFade = 1.0 - smoothstep(_DetailFadeStartDistance, max(_DetailFadeStartDistance + 0.001, _DetailFadeEndDistance), cameraDistance);
                float baseLandMask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                float landMask = lerp(1.0, baseLandMask, saturate(_ApplyDetailToLandOnly));
                float detailWeight = saturate(_DetailStrength) * detailFade * landMask;

                float3 triWeights = BlendTriplanarWeights(normalWS);
                float3 detailSamplePosWS = input.positionWS;

                if (_UseParallaxMapping > 0.5)
                {
                    // Seam-safe alternative to full UV parallax: offset world-space sample position along the surface normal.
                    // This avoids longitude seam artifacts while still adding subtle close-up relief on rocky detail.
                    float parallaxDistFade = 1.0 - smoothstep(_ParallaxMinDistance, max(_ParallaxMinDistance + 0.001, _ParallaxMaxDistance), cameraDistance);
                    float facingFade = saturate(dot(normalWS, viewDirWS));
                    float height = SampleTriplanarHeight(detailSamplePosWS, triWeights, _DetailTiling) - 0.5;
                    float offsetAmount = height * _DetailHeightStrength * detailWeight * parallaxDistFade * facingFade * facingFade;
                    detailSamplePosWS += normalWS * offsetAmount;
                }

                float4 macro = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float4 detailAlb = SampleTriplanarAlbedo(TEXTURE2D_ARGS(_DetailAlbedoMap, sampler_DetailAlbedoMap), detailSamplePosWS, triWeights, _DetailTiling);

                float3 albedo = macro.rgb;
                float3 detailTint = lerp(1.0.xxx, saturate(detailAlb.rgb * 1.35), detailWeight);
                albedo *= detailTint;

                float3 detailNormalWS = SampleTriplanarNormal(detailSamplePosWS, normalWS, triWeights, _DetailTiling);
                float3 shadedNormalWS = normalize(lerp(normalWS, detailNormalWS, detailWeight));

                float detailLuma = dot(detailAlb.rgb, float3(0.2126, 0.7152, 0.0722));
                float smoothness = saturate(_Smoothness + (detailLuma - 0.5) * 0.12 * detailWeight);

                InputData lightingInput = (InputData)0;
                lightingInput.positionWS = input.positionWS;
                lightingInput.normalWS = shadedNormalWS;
                lightingInput.viewDirectionWS = viewDirWS;
                lightingInput.shadowCoord = input.shadowCoord;
                lightingInput.fogCoord = input.fogFactor;
                lightingInput.vertexLighting = VertexLighting(input.positionWS, shadedNormalWS);
                lightingInput.bakedGI = SampleSH(shadedNormalWS);
                lightingInput.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lightingInput.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = 0;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1;
                surfaceData.emission = half3(0, 0, 0);
                surfaceData.alpha = 1;

                half4 color = UniversalFragmentPBR(lightingInput, surfaceData);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
