Shader "PhysicsHands/Hand Toon"
{
 Properties
 {
  [MainColor] _BaseColor("Color", Color) = (.84,.84,.84,.5)
  _SrcBlend("Source blend", Float) = 5
  _DstBlend("Destination blend", Float) = 10
  _ZWrite("Depth write", Float) = 0
  _StencilComp("Skeleton mask comparison", Float) = 8
  _StencilPass("Skeleton mask operation", Float) = 0
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
   ZWrite [_ZWrite]
   ZTest LEqual
   Cull Back
   // The public URP stencil bits are 0..3. The skeleton occupies bit 3;
   // skin leaves those pixels visible while both layers retain scene depth.
   Stencil { Ref 8 ReadMask 8 WriteMask 8 Comp [_StencilComp] Pass [_StencilPass] }
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;
   CBUFFER_END
   struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct V { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 positionWS:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
   V Vert(A i)
   {
    UNITY_SETUP_INSTANCE_ID(i); V o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
    o.positionCS=TransformWorldToHClip(o.positionWS);
    o.normalWS=TransformObjectToWorldNormal(i.normalOS); return o;
   }
   half4 Frag(V i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half3 n=normalize(i.normalWS); Light light=GetMainLight();
    half diffuse=dot(n,light.direction);
    half band=.48+.25*smoothstep(-.02,.02,diffuse)+.27*smoothstep(.53,.57,diffuse);
    half rim=smoothstep(.12,.24,saturate(dot(n,GetWorldSpaceNormalizeViewDir(i.positionWS))));
    return half4(_BaseColor.rgb*band*lerp(.25,1,rim),_BaseColor.a);
   }
   ENDHLSL
  }
 }
}
