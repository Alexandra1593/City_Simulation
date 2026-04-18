//***************************************************************************************
// Default.hlsl by Frank Luna (C) 2015 All Rights Reserved.
//***************************************************************************************

// Defaults for number of lights.
#ifndef NUM_DIR_LIGHTS
    #define NUM_DIR_LIGHTS 3
#endif

#ifndef NUM_POINT_LIGHTS
    #define NUM_POINT_LIGHTS 40
#endif

#ifndef NUM_SPOT_LIGHTS
    #define NUM_SPOT_LIGHTS 0
#endif

// Include common HLSL code.
#include "Common.hlsl"   

struct VertexIn
{
    float3 PosL    : POSITION;
    float3 NormalL : NORMAL;
    float2 TexC    : TEXCOORD;
   
};

struct VertexOut
{
    float4 PosH    : SV_POSITION;
    float3 PosW    : POSITION;
    float3 NormalW : NORMAL;
    float2 TexC    : TEXCOORD;
    float4 ShadowPosH : TEXCOORD1;
};

VertexOut VS(VertexIn vin)
{
    VertexOut vout = (VertexOut)0.0f;

    // Fetch the material data.
    MaterialData matData = gMaterialData[gMaterialIndex];

    // Transform to world space.
    float4 posW = mul(float4(vin.PosL, 1.0f), gWorld);
    vout.PosW = posW.xyz;

    // Assumes nonuniform scaling; otherwise, need to use inverse-transpose of world matrix.
    vout.NormalW = mul(vin.NormalL, (float3x3)gWorld);

    // Transform to homogeneous clip space.
    vout.PosH = mul(posW, gViewProj);

    // Output vertex attributes for interpolation across triangle.
    float4 texC = mul(float4(vin.TexC, 0.0f, 1.0f), gTexTransform);
    vout.TexC = mul(texC, matData.MatTransform).xy;

    
    
    vout.ShadowPosH = mul(posW, gShadowTransform);
    
    
    return vout;
}
float CalcShadowFactor(float4 shadowPosH)
{
    shadowPosH.xyz /= shadowPosH.w;

    float2 shadowTexC = shadowPosH.xy;
    float depth = shadowPosH.z;

    if (shadowTexC.x < 0.0f || shadowTexC.x > 1.0f ||
        shadowTexC.y < 0.0f || shadowTexC.y > 1.0f ||
        depth < 0.0f || depth > 1.0f)
    {
        return 1.0f;
    }

    float shadowDepth = gShadowMap.Sample(gsamLinearClamp, shadowTexC).r;

    float bias = 0.0005f;

    if (depth - bias > shadowDepth)
        return 0.25f;

    return 1.0f;
}


float4 PS(VertexOut pin) : SV_Target
{
    // Fetch the material data.
    MaterialData matData = gMaterialData[gMaterialIndex];
    float4 diffuseAlbedo = matData.DiffuseAlbedo;
    float3 fresnelR0 = matData.FresnelR0;
    float  roughness = matData.Roughness;
    uint diffuseTexIndex = matData.DiffuseMapIndex;

    // Dynamically look up the texture in the array.
    diffuseAlbedo *= gDiffuseMap[diffuseTexIndex].Sample(gsamAnisotropicWrap, pin.TexC);

    // Interpolating normal can unnormalize it, so renormalize it.
    pin.NormalW = normalize(pin.NormalW);

    // Vector from point being lit to eye.
    float3 toEyeW = normalize(gEyePosW - pin.PosW);

    // Light terms.
    float4 ambient = gAmbientLight * diffuseAlbedo;

    float shadow = CalcShadowFactor(pin.ShadowPosH);
    const float shininess = 1.0f - roughness;
    Material mat = { diffuseAlbedo, fresnelR0, shininess };
    

    float3 shadowFactor = float3(shadow, 1.0f, 1.0f);

    float4 directLight = ComputeLighting(
    gLights,
    mat,
    pin.PosW,
    pin.NormalW,
    toEyeW,
    shadowFactor
);

    float4 litColor = ambient + directLight;

    litColor.rgb = max(litColor.rgb, diffuseAlbedo.rgb * 0.25f);
    litColor.a = diffuseAlbedo.a;
    
   // return float4(shadow, shadow, shadow, 1.0f);
 
 
    return litColor;
}
