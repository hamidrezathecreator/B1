#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#if defined(B1_FLARE_DEPTH_ARRAY)
    TEXTURE2D_ARRAY_FLOAT(_CameraDepthTexture);
    #undef DISABLE_TEXTURE2D_X_ARRAY
    #undef LOAD_TEXTURE2D_X_LOD
    #define LOAD_TEXTURE2D_X_LOD(textureName, coordinate, level) LOAD_TEXTURE2D_ARRAY_LOD(textureName, coordinate, _ViewId, level)
#else
    TEXTURE2D_FLOAT(_CameraDepthTexture);
    #define DISABLE_TEXTURE2D_X_ARRAY
    #undef LOAD_TEXTURE2D_X_LOD
    #define LOAD_TEXTURE2D_X_LOD(textureName, coordinate, level) LOAD_TEXTURE2D_LOD(textureName, coordinate, level)
#endif

float4 _CameraDepthTexture_TexelSize;
