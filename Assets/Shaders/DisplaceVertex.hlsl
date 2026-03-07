// Assets/Shaders/DisplaceVertex.hlsl
// ══════════════════════════════════════════════════════════════════════
// Depthweaver — Shader Graph Custom Function Node용 변위 함수
// ══════════════════════════════════════════════════════════════════════
//
// Shader Graph의 Custom Function Node에서 이 파일을 참조하여
// HDRP Lit의 모든 기능(GI, 반사 프로브, SSR, SSAO)을 상속받으면서
// 정점 변위와 법선 재계산을 수행한다.
//
// Shader Graph 설정:
//   1. Shader Graph 생성 (HDRP Lit, Opaque)
//   2. Vertex Stage에 Custom Function 노드 추가
//   3. Source: File, Name: "DisplaceVertex"
//   4. 파일 경로: Assets/Shaders/DisplaceVertex.hlsl
//
// _float 접미사: Shader Graph의 float precision 컨벤션

// ─── 경계 감쇠 헬퍼 ───
float ComputeEdgeMask(float2 uv, float falloff)
{
    if (falloff <= 0.0) return 1.0;
    float2 edge = smoothstep(0.0, falloff, uv) * smoothstep(0.0, falloff, 1.0 - uv);
    return edge.x * edge.y;
}

// ─── 메인 변위 함수 ───
// Shader Graph Custom Function Node 인터페이스:
//   Input:  UV, Position, Normal, DepthTex, SS, DisplacementScale, DisplacementBias, EdgeFalloff, TexelSize
//   Output: DisplacedPosition, DisplacedNormal
void DisplaceVertex_float(
    float2 UV,
    float3 Position,
    float3 Normal,
    UnityTexture2D DepthTex,
    UnitySamplerState SS,
    float DisplacementScale,
    float DisplacementBias,
    float EdgeFalloff,
    float TexelSize,
    out float3 DisplacedPosition,
    out float3 DisplacedNormal)
{
    // ─── 1. 중심 깊이 샘플링 및 변위 ───
    float depth = SAMPLE_TEXTURE2D_LOD(DepthTex, SS, UV, 0).r;
    float edgeMask = ComputeEdgeMask(UV, EdgeFalloff);
    float displacement = (depth * DisplacementScale + DisplacementBias) * edgeMask;

    DisplacedPosition = Position + Normal * displacement;

    // ─── 2. 법선 재계산 (중심 차분) ───
    // 인접 4방향의 변위를 측정하여 표면 기울기를 산출
    float hL = SAMPLE_TEXTURE2D_LOD(DepthTex, SS, UV + float2(-TexelSize, 0), 0).r;
    float hR = SAMPLE_TEXTURE2D_LOD(DepthTex, SS, UV + float2( TexelSize, 0), 0).r;
    float hD = SAMPLE_TEXTURE2D_LOD(DepthTex, SS, UV + float2(0, -TexelSize), 0).r;
    float hU = SAMPLE_TEXTURE2D_LOD(DepthTex, SS, UV + float2(0,  TexelSize), 0).r;

    // 경계 감쇠 적용
    hL = (hL * DisplacementScale + DisplacementBias) * ComputeEdgeMask(UV + float2(-TexelSize, 0), EdgeFalloff);
    hR = (hR * DisplacementScale + DisplacementBias) * ComputeEdgeMask(UV + float2( TexelSize, 0), EdgeFalloff);
    hD = (hD * DisplacementScale + DisplacementBias) * ComputeEdgeMask(UV + float2(0, -TexelSize), EdgeFalloff);
    hU = (hU * DisplacementScale + DisplacementBias) * ComputeEdgeMask(UV + float2(0,  TexelSize), EdgeFalloff);

    // 중심 차분 기울기
    float dX = (hR - hL) * 0.5;
    float dY = (hU - hD) * 0.5;

    // 법선 교란 (오브젝트 공간)
    // XY 평면 메시를 가정: 접선 = +X, 바이탄젠트 = +Y
    DisplacedNormal = normalize(Normal + float3(-dX, -dY, 0));
}

// ─── half precision 버전 (모바일/VR 호환) ───
void DisplaceVertex_half(
    half2 UV,
    half3 Position,
    half3 Normal,
    UnityTexture2D DepthTex,
    UnitySamplerState SS,
    half DisplacementScale,
    half DisplacementBias,
    half EdgeFalloff,
    half TexelSize,
    out half3 DisplacedPosition,
    out half3 DisplacedNormal)
{
    float3 outPos, outNorm;
    DisplaceVertex_float(
        (float2)UV, (float3)Position, (float3)Normal,
        DepthTex, SS,
        (float)DisplacementScale, (float)DisplacementBias,
        (float)EdgeFalloff, (float)TexelSize,
        outPos, outNorm
    );
    DisplacedPosition = (half3)outPos;
    DisplacedNormal = (half3)outNorm;
}
