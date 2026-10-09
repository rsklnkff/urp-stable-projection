Shader "Stable Projection/Demo"
{
    Properties { _Color("Color", Color) = (0.3,0.8,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _STABLE_DEMO
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                #if defined(_STABLE_DEMO)
                    output.positionCS = TransformObjectToHClip(input.positionOS);
                #else
                    output.positionCS = mul(UNITY_MATRIX_VP, mul(UNITY_MATRIX_M, float4(input.positionOS, 1)));
                #endif
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
