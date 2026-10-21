Shader "PhysicsHands/Hand Feedback"
{
    Properties { _MainTex("Font atlas", 2D) = "white" {} _UseFont("Use font", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+100" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _UseFont;
            struct Input { float2 uv : TEXCOORD0; float4 position : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varying { float2 uv : TEXCOORD0; float4 position : SV_POSITION; float4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            Varying Vert(Input v) { Varying o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.position = TransformObjectToHClip(v.position.xyz); o.color=v.color; o.uv=v.uv; return o; }
            half4 Frag(Varying v) : SV_Target { half4 color=v.color; if (_UseFont > .5) color.a *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, v.uv).a; return color; }
            ENDHLSL
        }
    }
}
