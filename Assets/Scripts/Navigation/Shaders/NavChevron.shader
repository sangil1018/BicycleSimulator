// 노면 네비게이션 셰브론 전용 언릿 셰이더.
// _BaseColor를 인스턴싱 프로퍼티로 선언해, RoadNavigationGuide가 셰브론마다
// MaterialPropertyBlock으로 색/알파를 넣어도 드로우콜이 합쳐지도록 했다.
// (URP 기본 Unlit은 _BaseColor가 UnityPerMaterial에 있어 MPB를 쓰면 배칭이 끊긴다.)
Shader "Bicycle/NavChevron"
{
    Properties
    {
        [MainTexture] _BaseMap   ("Chevron Texture", 2D)      = "white" {}
        [MainColor]   _BaseColor ("Color", Color)             = (0.25, 1, 0.85, 1)
                      _Intensity ("Intensity", Range(1, 8))   = 1
                      _Fade      ("Distance Fade Power", Range(0.1, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend  SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest  LEqual
            Cull   Off
            Offset -1, -1   // 노면과의 Z-파이팅 완화

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float  _Intensity;
                float  _Fade;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex   = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 color = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);

                half3 rgb = tex.rgb * color.rgb * _Intensity;
                half  a   = pow(saturate(tex.a * color.a), _Fade);

                clip(a - 0.003);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
