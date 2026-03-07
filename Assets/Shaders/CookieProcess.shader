// Assets/Shaders/CookieProcess.shader
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — GPU 쿠키 텍스처 후처리 셰이더
// ══════════════════════════════════════════════════════════════════════
//
// Graphics.Blit()으로 호출되어, 웹 페이지 색상 텍스처를
// 영역광 쿠키에 적합한 형태로 변환한다.
//
// 처리 파이프라인 (단일 패스):
//   1. 채도 부스트 — 투사 광의 색상 선명도 조절
//   2. 콘트라스트 커브 — 1D LUT 텍스처로 비선형 밝기 매핑
//   3. 강도 승수 — 전체 밝기 스케일링
//
// CPU GetPixels 루프 대비 ~100x 성능 (262K 픽셀 GPU 병렬 처리)

Shader "Hidden/UIShader/CookieProcess"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _ContrastLUT ("Contrast LUT", 2D) = "white" {}
        _SaturationBoost ("Saturation Boost", Float) = 1.0
        _IntensityMultiplier ("Intensity Multiplier", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _ContrastLUT;
            float _SaturationBoost;
            float _IntensityMultiplier;

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);

                // ─── 1. 채도 부스트 (BT.709 휘도 기반 lerp) ───
                // HSV 변환 없이 luminance↔color 보간으로 채도를 조절한다.
                // _SaturationBoost > 1: 색상 강조, < 1: 탈채도
                float lum = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
                float3 gray = float3(lum, lum, lum);
                c.rgb = saturate(lerp(gray, c.rgb, _SaturationBoost));

                // ─── 2. 콘트라스트 커브 (1D LUT 텍스처) ───
                // AnimationCurve를 256x1 LUT로 베이크하여 GPU에서 비선형 매핑.
                // 반텍셀 오프셋으로 정확한 샘플링 보장.
                float lutScale = 255.0 / 256.0;
                float lutOffset = 0.5 / 256.0;
                c.r = tex2D(_ContrastLUT, float2(saturate(c.r) * lutScale + lutOffset, 0.5)).r;
                c.g = tex2D(_ContrastLUT, float2(saturate(c.g) * lutScale + lutOffset, 0.5)).r;
                c.b = tex2D(_ContrastLUT, float2(saturate(c.b) * lutScale + lutOffset, 0.5)).r;

                // ─── 3. 강도 승수 ───
                c.rgb *= _IntensityMultiplier;

                c.a = 1.0;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
