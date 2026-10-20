// System gaze hover for Unity's Metal (Compositor Services) mode, visionOS 26+.
//
// Unity's closed display plugin queries one drawable per frame with the deprecated
// cp_frame_query_drawable and never registers tracking areas. These two shims, linked into
// UnityFramework ahead of CompositorServices, (1) answer that query with the first drawable of
// cp_frame_query_drawables, which enables the visionOS 26 drawable features, and (2) just before
// present register one tracking area with the system's automatic hover effect per quad that C#
// supplies, drawing each quad's render value into the tracking-areas texture of every view.
// The system then highlights what the eyes rest on; the app never learns where that is.
// Without tracking textures (older visionOS, configuration not patched) both calls pass through.

#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#import <CompositorServices/CompositorServices.h>
#import <ARKit/ARKit.h>
#include <dlfcn.h>
#include <simd/simd.h>
#include <string.h>

namespace
{
    const int MaxQuads = 32;
    // Quad corners in ARKit origin space (right-handed), four per quad, set from C# each frame.
    simd_float3 corners[MaxQuads * 4];
    int quadCount = 0;
    id<MTLRenderPipelineState> pipeline;
    bool failed = false;

    typedef cp_drawable_t (*QueryDrawable)(cp_frame_t);
    typedef void (*EncodePresent)(cp_drawable_t, id<MTLCommandBuffer>);
    void* Real(const char* name)
    {
        static void* framework = dlopen("/System/Library/Frameworks/CompositorServices.framework/CompositorServices", RTLD_LAZY | RTLD_NOLOAD);
        return framework != nullptr ? dlsym(framework, name) : nullptr;
    }

    const char* Source = R"(
#include <metal_stdlib>
using namespace metal;
struct Out { float4 position [[position]]; };
vertex Out QuadVertex(uint id [[vertex_id]], constant float4x4& viewProjection [[buffer(0)]], constant float4* corners [[buffer(1)]])
{
    const uint order[6] = { 0, 1, 2, 0, 2, 3 };
    Out o; o.position = viewProjection * float4(corners[order[id]].xyz, 1); return o;
}
fragment ushort QuadFragment(constant ushort& value [[buffer(0)]]) { return value; }
)";

    id<MTLRenderPipelineState> Pipeline(id<MTLDevice> device, MTLPixelFormat format)
    {
        if (pipeline != nil || failed) return pipeline;
        NSError* error = nil;
        id<MTLLibrary> library = [device newLibraryWithSource:@(Source) options:nil error:&error];
        if (library == nil) { NSLog(@"GazeHover: shader compile failed %@", error); failed = true; return nil; }
        auto d = [MTLRenderPipelineDescriptor new];
        d.vertexFunction = [library newFunctionWithName:@"QuadVertex"];
        d.fragmentFunction = [library newFunctionWithName:@"QuadFragment"];
        d.colorAttachments[0].pixelFormat = format;
        pipeline = [device newRenderPipelineStateWithDescriptor:d error:&error];
        if (pipeline == nil) { NSLog(@"GazeHover: pipeline failed %@", error); failed = true; }
        return pipeline;
    }

    API_AVAILABLE(visionos(26.0))
    void DrawTrackingAreas(cp_drawable_t drawable, id<MTLCommandBuffer> commands)
    {
        if (quadCount == 0 || cp_drawable_get_tracking_areas_texture_count(drawable) == 0) return;
        ar_device_anchor_t anchor = cp_drawable_get_device_anchor(drawable);
        if (anchor == nullptr) return;
        simd_float4x4 originFromDevice = ar_anchor_get_origin_from_anchor_transform(anchor);
        id<MTLTexture> first = cp_drawable_get_tracking_areas_texture(drawable, 0);
        auto state = Pipeline(commands.device, first.pixelFormat);
        if (state == nil) return;

        uint16_t values[MaxQuads];
        for (int q = 0; q < quadCount; q++)
        {
            cp_tracking_area_t area = cp_drawable_add_tracking_area(drawable, (cp_tracking_area_identifier)(q + 1));
            cp_tracking_area_add_automatic_hover_effect(area);
            values[q] = cp_tracking_area_get_render_value(area);
        }
        simd_float4 packed[MaxQuads * 4];
        for (int i = 0; i < quadCount * 4; i++) packed[i] = simd_make_float4(corners[i], 1);

        bool cleared[8] = {};
        size_t views = cp_drawable_get_view_count(drawable), maps = cp_drawable_get_rasterization_rate_map_count(drawable);
        for (size_t v = 0; v < views; v++)
        {
            cp_view_t view = cp_drawable_get_view(drawable, v);
            cp_view_texture_map_t map = cp_view_get_view_texture_map(view);
            size_t textureIndex = cp_view_texture_map_get_texture_index(map), slice = cp_view_texture_map_get_slice_index(map);
            if (textureIndex >= cp_drawable_get_tracking_areas_texture_count(drawable)) continue;
            id<MTLTexture> target = cp_drawable_get_tracking_areas_texture(drawable, textureIndex);
            auto pass = [MTLRenderPassDescriptor renderPassDescriptor];
            pass.colorAttachments[0].texture = target;
            pass.colorAttachments[0].slice = slice;
            size_t key = (textureIndex * 4 + slice) & 7;
            pass.colorAttachments[0].loadAction = cleared[key] ? MTLLoadActionLoad : MTLLoadActionClear;
            pass.colorAttachments[0].clearColor = MTLClearColorMake(0, 0, 0, 0);
            pass.colorAttachments[0].storeAction = MTLStoreActionStore;
            cleared[key] = true;
            if (maps > 0) pass.rasterizationRateMap = cp_drawable_get_rasterization_rate_map(drawable, textureIndex < maps ? textureIndex : 0);
            simd_float4x4 viewMatrix = simd_inverse(simd_mul(originFromDevice, cp_view_get_transform(view)));
            simd_float4x4 viewProjection = simd_mul(cp_drawable_compute_projection(drawable, cp_axis_direction_convention_right_up_back, v), viewMatrix);
            id<MTLRenderCommandEncoder> encoder = [commands renderCommandEncoderWithDescriptor:pass];
            [encoder setRenderPipelineState:state];
            [encoder setViewport:cp_view_texture_map_get_viewport(map)];
            [encoder setCullMode:MTLCullModeNone];
            [encoder setVertexBytes:&viewProjection length:sizeof(viewProjection) atIndex:0];
            for (int q = 0; q < quadCount; q++)
            {
                if (values[q] == cp_tracking_area_render_value_invalid) continue;
                [encoder setVertexBytes:&packed[q * 4] length:sizeof(simd_float4) * 4 atIndex:1];
                [encoder setFragmentBytes:&values[q] length:sizeof(uint16_t) atIndex:0];
                [encoder drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:6];
            }
            [encoder endEncoding];
        }
    }
}

extern "C"
{
    // Unity calls this deprecated query; return the first drawable of the visionOS 26 query instead.
    cp_drawable_t cp_frame_query_drawable(cp_frame_t frame)
    {
        if (@available(visionOS 26.0, *))
        {
            cp_drawable_array_t drawables = cp_frame_query_drawables(frame);
            return drawables != nullptr && cp_drawable_array_get_count(drawables) > 0 ? cp_drawable_array_get_drawable(drawables, 0) : nullptr;
        }
        static QueryDrawable real = (QueryDrawable)Real("cp_frame_query_drawable");
        return real != nullptr ? real(frame) : nullptr;
    }

    void cp_drawable_encode_present(cp_drawable_t drawable, id<MTLCommandBuffer> commands)
    {
        if (@available(visionOS 26.0, *)) DrawTrackingAreas(drawable, commands);
        static EncodePresent real = (EncodePresent)Real("cp_drawable_encode_present");
        if (real != nullptr) real(drawable, commands);
    }

    // Quads as 12 floats each (four corners, Unity session space, left-handed).
    void GazeHover_SetQuads(const float* values, int count)
    {
        quadCount = count < 0 ? 0 : (count > MaxQuads ? MaxQuads : count);
        for (int i = 0; i < quadCount * 4; i++) corners[i] = simd_make_float3(values[i * 3], values[i * 3 + 1], -values[i * 3 + 2]);
    }
}
