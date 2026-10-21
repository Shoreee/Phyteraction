using PhysicsHands.Dynamics;
using Unity.Mathematics;

namespace PhysicsHands.Simulation
{
    internal static class ModalContactProjection
    {
        internal static bool TryProject(ContactShape shape, float3 p, float3 previous, float t, float h, float radius, float friction, out float3 correction)
        {
            correction = float3.zero;
            float3 n, shapeVelocity;
            float penetration;
            if (shape.isBox != 0)
            {
                float3 centre = shape.a + shape.velocityA * t, d = p - centre;
                float3 local = new(math.dot(d, shape.axisX), math.dot(d, shape.axisY), math.dot(d, shape.axisZ));
                float3 clamped = math.clamp(local, -shape.halfExtents, shape.halfExtents);
                shapeVelocity = shape.velocityA;
                if (math.any(local != clamped))
                {
                    float3 delta = d - (shape.axisX * clamped.x + shape.axisY * clamped.y + shape.axisZ * clamped.z);
                    float dist = math.length(delta);
                    if (dist >= radius || dist < 1e-9f)
                        return false;
                    n = delta / dist;
                    penetration = radius - dist;
                }
                else
                {
                    float3 gap = shape.halfExtents - math.abs(local);
                    int axis = gap.x < gap.y ? (gap.x < gap.z ? 0 : 2) : (gap.y < gap.z ? 1 : 2);
                    float3 direction = axis == 0 ? shape.axisX : axis == 1 ? shape.axisY : shape.axisZ;
                    n = direction * (local[axis] < 0 ? -1 : 1);
                    penetration = gap[axis] + radius;
                }
            }
            else
            {
                float3 a = shape.a + shape.velocityA * t, ab = shape.b + shape.velocityB * t - a;
                float u = math.saturate(math.dot(p - a, ab) / math.max(math.dot(ab, ab), 1e-12f));
                float3 delta = p - (a + u * ab);
                float dist = math.length(delta), reach = shape.radius + radius;
                if (dist >= reach)
                    return false;
                n = dist > 1e-7f ? delta / dist : new float3(0, 1, 0);
                penetration = reach - dist;
                shapeVelocity = math.lerp(shape.velocityA, shape.velocityB, u);
            }

            float3 relative = p - previous - shapeVelocity * h, tangent = relative - n * math.dot(relative, n);
            float tangentLength = math.length(tangent);
            correction = n * penetration;
            if (tangentLength > 1e-9f)
                correction -= tangent * math.min(1, friction * penetration / tangentLength);
            return true;
        }
    }
}
