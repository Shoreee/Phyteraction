Shader "PhysicsHands/Hand Outline"
{
 Properties { [MainColor] _BaseColor("Color",Color)=(.035,.04,.05,.8) _Width("Width in pixels",Float)=2 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-5" "RenderType"="Transparent" }
  Pass
  {
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
   ZWrite Off
   ZTest LEqual
   Cull Front
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor; float _Width;
   CBUFFER_END
   struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct V { float4 positionCS:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
   V Vert(A i)
   {
    UNITY_SETUP_INSTANCE_ID(i); V o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
    float3 normalVS=TransformWorldToViewDir(TransformObjectToWorldNormal(i.normalOS));
    float2 direction=mul((float2x2)UNITY_MATRIX_P,normalVS.xy);
    direction*=rsqrt(max(dot(direction,direction),1e-8));
    o.positionCS.xy+=direction*(2*_Width/_ScaledScreenParams.xy)*o.positionCS.w;
    return o;
   }
   half4 Frag(V i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); return _BaseColor; }
   ENDHLSL
  }
 }
}
