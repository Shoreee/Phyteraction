Shader "GaussianWorld/Elastic Splats"
{
 Properties { }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass
  {
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
   ZWrite Off
   ZTest LEqual
   Cull Off
   HLSLPROGRAM
   #pragma target 4.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #pragma instancing_options procedural:SetupSplat
   void SetupSplat() {}
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
   #include "../../GaussianData/ShaderLibrary/Gaussian.hlsl"
   StructuredBuffer<Gaussian> _Deformed;
   StructuredBuffer<uint> _Order;
   uint _VisibleCount, _ShEnabled, _DynamicCount;
   StructuredBuffer<float4> _Sh;
   float4x4 _ShFromLocal, _SplatLocalToWorld, _SplatWorldToLocal;
   struct Attributes {float4 positionOS:POSITION; uint id:SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO};
   float2 ProjectAxis(float3 a,float3 v){
    // Perspective Jacobian in NDC; all covariance axes are in view coordinates.
    float z=max(-v.z,1e-4);
    return float2(UNITY_MATRIX_P._m00*(a.x/z+v.x*a.z/(z*z)),UNITY_MATRIX_P._m11*(a.y/z+v.y*a.z/(z*z)));
   }
   float3 LimitSupportAxis(float3 axis, float cap){
    float lengthAxis=length(axis);
    return lengthAxis>cap ? axis*(cap/lengthAxis) : axis;
   }
   Varyings Vert(Attributes input){
    UNITY_SETUP_INSTANCE_ID(input);Varyings o;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    #if UNITY_ANY_INSTANCING_ENABLED
    uint quad=unity_InstanceID;
    #else
    uint quad=0;
    #endif
    if(quad>=_VisibleCount){o.positionCS=float4(2,2,2,1);o.uv=0;o.color=0;return o;}
    uint index=_Order[quad];Gaussian g=_Deformed[index];
    float3 v=TransformWorldToView(mul(_SplatLocalToWorld,float4(g.center.xyz,1)).xyz);
    float3 a=mul((float3x3)UNITY_MATRIX_V,mul((float3x3)_SplatLocalToWorld,g.axis0.xyz));
    float3 b=mul((float3x3)UNITY_MATRIX_V,mul((float3x3)_SplatLocalToWorld,g.axis1.xyz));
    float3 c=mul((float3x3)UNITY_MATRIX_V,mul((float3x3)_SplatLocalToWorld,g.axis2.xyz));
    // Support/background Gaussians were fitted from a few views. Long covariance axes
    // project as needles at hand distance; bound only their footprint, keeping all centres.
    if(index>=_DynamicCount){
     float3 lengths=float3(length(a),length(b),length(c));
     float middle=lengths.x+lengths.y+lengths.z-min(lengths.x,min(lengths.y,lengths.z))-max(lengths.x,max(lengths.y,lengths.z));
     float cap=min(.015,max(.001,6*middle));
     a=LimitSupportAxis(a,cap);b=LimitSupportAxis(b,cap);c=LimitSupportAxis(c,cap);
    }
    float2 aa=ProjectAxis(a,v),bb=ProjectAxis(b,v),cc=ProjectAxis(c,v);
    float xx=aa.x*aa.x+bb.x*bb.x+cc.x*cc.x+1e-8;
    float xy=aa.x*aa.y+bb.x*bb.y+cc.x*cc.y;
    float yy=aa.y*aa.y+bb.y*bb.y+cc.y*cc.y+1e-8;
    // Cholesky square root of the projected covariance avoids unstable eigenvectors.
    float root=sqrt(xx);float2 e0=float2(root,xy/root),e1=float2(0,sqrt(max(yy-xy*xy/xx,1e-10)));
    // A 3 sigma footprint wider than a quarter of the view is a floater or a splat at the eye:
    // shrink it rather than flood the screen (and the fill rate).
    float extent=3*max(length(e0),length(e1));if(extent>.5){e0*=.5/extent;e1*=.5/extent;}
    // Splats closer than 6 cm vanish, fading in by 20 cm, so the head can enter the scene.
    float near=saturate((-v.z-.06)/.14);
    // The quad ends where the Gaussian drops below the 1/255 the fragment discards (at most 3 sigma):
    // faint splats cover far fewer pixels for the same image.
    float opacity=min(.99,g.color.a*near);
    if(opacity<1.0/255){o.positionCS=float4(2,2,2,1);o.uv=0;o.color=0;return o;}
    float radius=min(3,sqrt(2*log(255*opacity)));
    uint corner=input.id%4;float2 uv=float2((corner==1||corner==2)?1:-1,corner>=2?1:-1)*radius;
    o.positionCS=mul(UNITY_MATRIX_P,float4(v,1));
    o.positionCS.xy+=(e0*uv.x+e1*uv.y)*o.positionCS.w;
    float3 rgb=g.color.rgb;
    if(_ShEnabled!=0){
     // First-order SH in the capture frame: view direction camera -> splat, as fitted by 3DGS.
     float3 cam=mul(UNITY_MATRIX_I_V,float4(0,0,0,1)).xyz;
     float3 d=normalize(mul((float3x3)_ShFromLocal,g.center.xyz-mul(_SplatWorldToLocal,float4(cam,1)).xyz));
     float4 s0=_Sh[index*3],s1=_Sh[index*3+1],s2=_Sh[index*3+2];
     float3 k=float3(-d.y,d.z,-d.x)*.4886025;
     rgb=saturate(rgb+float3(dot(k,s0.xyz),dot(k,float3(s0.w,s1.xy)),dot(k,float3(s1.zw,s2.x))));
    }
    o.uv=uv;o.color=float4(SRGBToLinear(rgb),opacity);return o;
   }
   half4 Frag(Varyings input):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float alpha=input.color.a*exp(-.5*dot(input.uv,input.uv));
    clip(alpha-1.0/255);return half4(input.color.rgb,alpha);
   }
   ENDHLSL
  }
 }
}
