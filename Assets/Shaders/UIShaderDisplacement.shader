// Assets/Shaders/UIShaderDisplacement.shader
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — Phase 3 HDRP 변위 셰이더
// ══════════════════════════════════════════════════════════════════════
//
// 깊이 맵(_DepthTex)의 밝기 값을 기반으로 정점을 법선 방향으로 변위한다.
// Phase 0의 Simple 셰이더를 대체하며 다음을 추가:
//   - 중심 차분(central difference) 기반 법선 재계산
//   - 경계 페더링 (EdgeFalloff)
//   - ShadowCaster 패스 (변위 반영 그림자)
//   - DepthForwardOnly 패스 (HDRP 깊이 버퍼 정합성)
//   - 자체 발광 출력 (스크린 = 광원)
//
// 3패스 구조:
//   1. ForwardOnly     — 색상 + 에미션 + 변위 + 법선 재계산
//   2. ShadowCaster    — 변위된 실루엣으로 그림자 투사
//   3. DepthForwardOnly — HDRP 깊이 프리패스 (SSAO, SSR, 접촉 그림자)

Shader "UIShader/DisplacedScreen"
{
    Properties
    {
        // ─── 색상 ───
        _MainTex ("Web Page Color", 2D) = "white" {}
        _BaseColor ("Base Color Tint", Color) = (1, 1, 1, 1)

        // ─── 깊이/변위 ───
        _DepthTex ("Depth Map", 2D) = "black" {}
        _DisplacementScale ("Displacement Scale", Range(0, 2)) = 0.5
        _DisplacementBias ("Displacement Bias", Range(-1, 1)) = 0

        // ─── 경계 페더링 ───
        _EdgeFalloff ("Edge Falloff", Range(0, 0.2)) = 0.05

        // ─── 표면 ───
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _EmissionIntensity ("Emission Intensity", Range(0, 5)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // ═════════════════════════════════════════════
        // Pass 1: ForwardOnly — 색상 + 변위 + 법선 재계산
        // ═════════════════════════════════════════════
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // ─── 텍스처 선언 ───
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_DepthTex);
            SAMPLER(sampler_DepthTex);

            // ─── 프로퍼티 ───
            float4 _DepthTex_TexelSize; // (1/w, 1/h, w, h) — Unity 자동 주입
            float4 _BaseColor;
            float _DisplacementScale;
            float _DisplacementBias;
            float _EdgeFalloff;
            float _Metallic;
            float _Smoothness;
            float _EmissionIntensity;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
            };

            // ─── 경계 감쇠 ───
            // UV 가장자리에서 변위를 부드럽게 0으로 감쇠시켜
            // 메시 경계에서의 급격한 클리핑을 방지한다.
            float ComputeEdgeFalloff(float2 uv)
            {
                float f = _EdgeFalloff;
                if (f <= 0.0) return 1.0;
                float2 edge = smoothstep(0.0, f, uv) * smoothstep(0.0, f, 1.0 - uv);
                return edge.x * edge.y;
            }

            // ─── 변위량 산출 ───
            float SampleDisplacement(float2 uv)
            {
                float depth = SAMPLE_TEXTURE2D_LOD(_DepthTex, sampler_DepthTex, uv, 0).r;
                float edge = ComputeEdgeFalloff(uv);
                return (depth * _DisplacementScale + _DisplacementBias) * edge;
            }

            // ─── 변위 후 법선 재계산 ───
            // 중심 차분(central difference)으로 변위된 표면의 기울기를 측정하여
            // 접선 공간 법선을 산출한다. 이로써 변위된 면에서 조명이 정확히 반응한다.
            float3 ComputeDisplacedNormal(float2 uv, float3 normalOS, float4 tangentOS)
            {
                float texelSize = _DepthTex_TexelSize.x;

                // 인접 4방향 변위 샘플
                float hL = SampleDisplacement(uv + float2(-texelSize, 0));
                float hR = SampleDisplacement(uv + float2( texelSize, 0));
                float hD = SampleDisplacement(uv + float2(0, -texelSize));
                float hU = SampleDisplacement(uv + float2(0,  texelSize));

                // 중심 차분 기울기
                float dX = (hR - hL) / (2.0 * texelSize);
                float dY = (hU - hD) / (2.0 * texelSize);

                // 접선 공간 → 오브젝트 공간 법선 교란
                float3 tangent = tangentOS.xyz;
                float3 bitangent = cross(normalOS, tangent) * tangentOS.w;

                float3 perturbedNormal = normalize(
                    normalOS - dX * tangent - dY * bitangent
                );

                return perturbedNormal;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                // 1. 변위량 산출
                float displacement = SampleDisplacement(input.uv);

                // 2. 정점 변위
                float3 displacedPos = input.positionOS + input.normalOS * displacement;

                // 3. 법선 재계산
                float3 displacedNormal = ComputeDisplacedNormal(
                    input.uv, input.normalOS, input.tangentOS
                );

                // 4. 좌표 변환
                output.positionCS = TransformObjectToHClip(displacedPos);
                output.positionWS = TransformObjectToWorld(displacedPos);
                output.normalWS   = TransformObjectToWorldNormal(displacedNormal);
                output.uv = input.uv;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // 웹 페이지 색상
                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                color *= _BaseColor;

                // 에미션: 스크린이 자체적으로 빛을 내는 효과
                // HDRP Shader Graph 버전(Phase 5+)에서는 Emission 출력으로 연결됨
                float3 emission = color.rgb * _EmissionIntensity;

                return float4(color.rgb + emission, 1.0);
            }
            ENDHLSL
        }

        // ═════════════════════════════════════════════
        // Pass 2: ShadowCaster — 변위 반영 그림자
        // ═════════════════════════════════════════════
        // 변위된 실루엣으로 그림자를 투사하여
        // 환경에 드리워지는 그림자가 변위 형태를 따르게 한다.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_DepthTex);
            SAMPLER(sampler_DepthTex);
            float _DisplacementScale;
            float _DisplacementBias;
            float _EdgeFalloff;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float ComputeEdgeFalloff(float2 uv)
            {
                float f = _EdgeFalloff;
                if (f <= 0.0) return 1.0;
                float2 edge = smoothstep(0.0, f, uv) * smoothstep(0.0, f, 1.0 - uv);
                return edge.x * edge.y;
            }

            Varyings vertShadow(Attributes input)
            {
                Varyings output;

                float depth = SAMPLE_TEXTURE2D_LOD(_DepthTex, sampler_DepthTex, input.uv, 0).r;
                float edge = ComputeEdgeFalloff(input.uv);
                float displacement = (depth * _DisplacementScale + _DisplacementBias) * edge;

                float3 displacedPos = input.positionOS + input.normalOS * displacement;
                output.positionCS = TransformObjectToHClip(displacedPos);

                return output;
            }

            float4 fragShadow(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ═════════════════════════════════════════════
        // Pass 3: DepthForwardOnly — HDRP 깊이 프리패스
        // ═════════════════════════════════════════════
        // HDRP가 깊이 버퍼를 올바르게 채우기 위해 필요.
        // SSAO, SSR, 접촉 그림자 등이 변위된 표면을 인식한다.
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertDepth
            #pragma fragment fragDepth

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_DepthTex);
            SAMPLER(sampler_DepthTex);
            float _DisplacementScale;
            float _DisplacementBias;
            float _EdgeFalloff;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float ComputeEdgeFalloff(float2 uv)
            {
                float f = _EdgeFalloff;
                if (f <= 0.0) return 1.0;
                float2 edge = smoothstep(0.0, f, uv) * smoothstep(0.0, f, 1.0 - uv);
                return edge.x * edge.y;
            }

            Varyings vertDepth(Attributes input)
            {
                Varyings output;

                float depth = SAMPLE_TEXTURE2D_LOD(_DepthTex, sampler_DepthTex, input.uv, 0).r;
                float edge = ComputeEdgeFalloff(input.uv);
                float displacement = (depth * _DisplacementScale + _DisplacementBias) * edge;

                float3 displacedPos = input.positionOS + input.normalOS * displacement;
                output.positionCS = TransformObjectToHClip(displacedPos);

                return output;
            }

            float4 fragDepth(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
