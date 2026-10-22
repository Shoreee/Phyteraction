using PhysicsHands.Dynamics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace PhysicsHands.Simulation
{
    internal struct CageAxleConstraint
    {
        public int node;
        public float3 centre, axis;
        public float radius, axial;
    }

    internal struct CageRigidBody
    {
        public float inverseMass;
        public float3 centre, velocity;
        public float3x3 inverseInertia;
    }

    // Persistent material coordinates, finite-strain stable Neo-Hookean energy, and XPBD.
    // No plastic rest update, particle release, damage variable or topology deletion occurs here.
    [BurstCompile(CompileSynchronously = true)]
    internal struct CageStepJob : IJob
    {
        public NativeArray<float3> x, previous, velocity;
        [ReadOnly] public NativeArray<float3> rest;
        [ReadOnly] public NativeArray<float> inverseMass, volume, mu, lambda;
        [ReadOnly] public NativeArray<int4> tets;
        [ReadOnly] public NativeArray<int3> surface;
        [ReadOnly] public NativeArray<CageAxleConstraint> axles;
        [ReadOnly] public NativeArray<int> nodeAxle;
        [ReadOnly] public NativeArray<float3x3> inverseRest;
        public NativeArray<float> multipliers;
        [ReadOnly] public NativeArray<ContactShape> shapes;
        [ReadOnly] public NativeArray<int> shapeBody;
        [ReadOnly] public NativeArray<CageRigidBody> bodies;
        public NativeArray<float3> bodyOffset, bodyAngle, bodyDeltaVelocity, bodyDeltaOmega;
        public NativeArray<float3> impulse, torque, contactPoint;
        public NativeArray<float> contactWeight;
        public NativeList<int2> pairs;
        public NativeArray<float4> render;
        public int shapeCount, bodyCount, substeps, iterations;
        public float dt, radius, friction, damping;
        public float3 gravity;

        public void Execute()
        {
            for (int b = 0; b < bodyCount; b++)
            {
                bodyOffset[b] = bodyAngle[b] = bodyDeltaVelocity[b] = bodyDeltaOmega[b] = 0;
                impulse[b] = torque[b] = contactPoint[b] = 0;
                contactWeight[b] = 0;
            }
            // Broad phase uses the actual physics pose and the whole upcoming fixed interval.
            pairs.Clear();
            for (int c = 0; c < shapeCount; c++)
            {
                var s = shapes[c];
                float3 extent = s.isBox != 0 ? math.abs(s.axisX) * s.halfExtents.x + math.abs(s.axisY) * s.halfExtents.y + math.abs(s.axisZ) * s.halfExtents.z : new float3(s.radius);
                float3 lo = math.min(s.a, s.b) - extent - radius;
                float3 hi = math.max(s.a, s.b) + extent + radius;
                float3 sweep = math.max(math.abs(s.velocityA), math.abs(s.velocityB)) * dt;
                lo -= sweep; hi += sweep;
                for (int f = 0; f < surface.Length; f++)
                {
                    var tri = surface[f];
                    float3 low = math.min(x[tri.x], math.min(x[tri.y], x[tri.z]));
                    float3 high = math.max(x[tri.x], math.max(x[tri.y], x[tri.z]));
                    float3 travel = math.max(math.abs(velocity[tri.x]), math.max(math.abs(velocity[tri.y]), math.abs(velocity[tri.z]))) * dt;
                    if (math.all(high + travel >= lo) && math.all(low - travel <= hi)) pairs.Add(new int2(f, c));
                }
            }
            float h = dt / substeps;
            for (int step = 0; step < substeps; step++)
            {
                for (int i = 0; i < x.Length; i++)
                {
                    previous[i] = x[i];
                    if (inverseMass[i] == 0) { x[i] = rest[i]; velocity[i] = 0; continue; }
                    velocity[i] = (velocity[i] + gravity * h) * math.exp(-damping * h);
                    x[i] += velocity[i] * h;
                }
                for (int b = 0; b < bodyCount; b++)
                {
                    bodyOffset[b] += bodyDeltaVelocity[b] * h;
                    bodyAngle[b] += bodyDeltaOmega[b] * h;
                }
                ProjectAxles();
                for (int e = 0; e < multipliers.Length; e++) multipliers[e] = 0;
                for (int it = 0; it < iterations; it++)
                {
                    for (int e = 0; e < tets.Length; e++) Elastic(e, h);
                    ProjectAxles();
                    for (int p = 0; p < pairs.Length; p++) Contact(pairs[p].x, pairs[p].y, (step + 1) * h, h);
                    for (int e = 0; e < tets.Length; e++) PreserveOrientation(e);
                    ProjectAxles();
                }
                for (int i = 0; i < x.Length; i++) velocity[i] = (x[i] - previous[i]) / h;
            }
            for (int i = 0; i < x.Length; i++) render[i] = new float4(x[i], 1);
        }

        float3 Allowed(int node, float3 direction)
        {
            int index = nodeAxle[node];
            if (index < 0) return direction;
            var axle = axles[index];
            float3 tangent = math.normalizesafe(math.cross(axle.axis, x[node] - axle.centre));
            return tangent * math.dot(tangent, direction);
        }

        void ProjectAxles()
        {
            for (int i = 0; i < axles.Length; i++)
            {
                var axle = axles[i];
                float3 delta = x[axle.node] - axle.centre;
                float3 radial = delta - axle.axis * math.dot(delta, axle.axis);
                x[axle.node] = axle.centre + axle.axis * axle.axial + math.normalizesafe(radial) * axle.radius;
            }
        }

        void Elastic(int e, float h)
        {
            int4 ids = tets[e];
            float3x3 f = math.mul(new float3x3(x[ids.y] - x[ids.x], x[ids.z] - x[ids.x], x[ids.w] - x[ids.x]), inverseRest[e]);
            // Stable Neo-Hookean's volumetric coefficient is lambda_Lame + mu;
            // this shift preserves the source E/nu small-strain tangent.
            float j = math.determinant(f), l = (lambda[e] + mu[e]) / mu[e];
            float i1 = math.lengthsq(f.c0) + math.lengthsq(f.c1) + math.lengthsq(f.c2);
            // Psi/mu = (I1-3)/2 -(J-1) + lambda/(2mu)*(J-1)^2.
            // One energy constraint avoids the nonzero-rest drift of sequential hydro/dev projections.
            float energy = .5f * (i1 - 3) - (j - 1) + .5f * l * (j - 1) * (j - 1);
            if (energy < 1e-8f) return;
            float constraint = math.sqrt(2 * energy);
            var cof = new float3x3(math.cross(f.c1, f.c2), math.cross(f.c2, f.c0), math.cross(f.c0, f.c1));
            var gradient = math.mul((f + (l * (j - 1) - 1) * cof) / constraint, math.transpose(inverseRest[e]));
            float3 g0 = Allowed(ids.x, -gradient.c0 - gradient.c1 - gradient.c2), g1 = Allowed(ids.y, gradient.c0), g2 = Allowed(ids.z, gradient.c1), g3 = Allowed(ids.w, gradient.c2);
            float alpha = 1 / (mu[e] * volume[e] * h * h);
            float denominator = inverseMass[ids.x] * math.lengthsq(g0) + inverseMass[ids.y] * math.lengthsq(g1) + inverseMass[ids.z] * math.lengthsq(g2) + inverseMass[ids.w] * math.lengthsq(g3) + alpha;
            float dl = (-constraint - alpha * multipliers[e]) / denominator;
            multipliers[e] += dl;
            x[ids.x] += inverseMass[ids.x] * dl * g0;
            x[ids.y] += inverseMass[ids.y] * dl * g1;
            x[ids.z] += inverseMass[ids.z] * dl * g2;
            x[ids.w] += inverseMass[ids.w] * dl * g3;
        }

        void PreserveOrientation(int e)
        {
            var ids=tets[e];
            float3x3 f=math.mul(new float3x3(x[ids.y]-x[ids.x],x[ids.z]-x[ids.x],x[ids.w]-x[ids.x]),inverseRest[e]);
            float j=math.determinant(f);
            const float minimumDeterminant=1e-3f;
            if(j>=minimumDeterminant)return;
            var cofactor=new float3x3(math.cross(f.c1,f.c2),math.cross(f.c2,f.c0),math.cross(f.c0,f.c1));
            var gradient=math.mul(cofactor,math.transpose(inverseRest[e]));
            float3 g0=Allowed(ids.x,-gradient.c0-gradient.c1-gradient.c2),g1=Allowed(ids.y,gradient.c0),g2=Allowed(ids.z,gradient.c1),g3=Allowed(ids.w,gradient.c2);
            float w=inverseMass[ids.x]*math.lengthsq(g0)+inverseMass[ids.y]*math.lengthsq(g1)+inverseMass[ids.z]*math.lengthsq(g2)+inverseMass[ids.w]*math.lengthsq(g3);
            if(w<1e-12f)return;
            float dl=(minimumDeterminant-j)/w;
            x[ids.x]+=inverseMass[ids.x]*dl*g0;x[ids.y]+=inverseMass[ids.y]*dl*g1;
            x[ids.z]+=inverseMass[ids.z]*dl*g2;x[ids.w]+=inverseMass[ids.w]*dl*g3;
        }

        // Closest-point barycentrics, including edges/vertices (Ericson's Voronoi regions).
        static float3 Closest(float3 p, float3 a, float3 b, float3 c)
        {
            float3 ab=b-a, ac=c-a, ap=p-a;
            float d1=math.dot(ab,ap), d2=math.dot(ac,ap);
            if(d1<=0 && d2<=0)return new float3(1,0,0);
            float3 bp=p-b;float d3=math.dot(ab,bp),d4=math.dot(ac,bp);
            if(d3>=0 && d4<=d3)return new float3(0,1,0);
            float vc=d1*d4-d3*d2;
            if(vc<=0 && d1>=0 && d3<=0){float v=d1/(d1-d3);return new float3(1-v,v,0);}
            float3 cp=p-c;float d5=math.dot(ab,cp),d6=math.dot(ac,cp);
            if(d6>=0 && d5<=d6)return new float3(0,0,1);
            float vb=d5*d2-d1*d6;
            if(vb<=0 && d2>=0 && d6<=0){float w=d2/(d2-d6);return new float3(1-w,0,w);}
            float va=d3*d6-d5*d4;
            if(va<=0 && d4-d3>=0 && d5-d6>=0){float w=(d4-d3)/(d4-d3+d5-d6);return new float3(0,1-w,w);}
            float inv=1/(va+vb+vc);float v2=vb*inv,w2=vc*inv;return new float3(1-v2-w2,v2,w2);
        }

        void Contact(int face, int collider, float t, float h)
        {
            var shape=shapes[collider];
            int count=shape.isBox!=0?9:math.clamp((int)math.ceil(math.distance(shape.a,shape.b)/math.max(shape.radius,1e-4f))+1,2,12);
            for(int i=0;i<count;i++)
            {
                float u=(float)i/(count-1);
                float3 q,shapeVelocity;
                float reach=shape.radius+radius;
                if(shape.isBox!=0)
                {
                    float3 d=i==8?float3.zero:new float3((i&1)!=0?1:-1,(i&2)!=0?1:-1,(i&4)!=0?1:-1)*shape.halfExtents;
                    q=shape.a+shape.axisX*d.x+shape.axisY*d.y+shape.axisZ*d.z+shape.velocityA*t;
                    shapeVelocity=shape.velocityA;reach=radius+(i==8?math.cmin(shape.halfExtents):0);
                }
                else {q=math.lerp(shape.a+shape.velocityA*t,shape.b+shape.velocityB*t,u);shapeVelocity=math.lerp(shape.velocityA,shape.velocityB,u);}
                int body=shapeBody[collider];float3 centre=0;
                if(body>=0)
                {
                    var rigid=bodies[body];centre=rigid.centre+rigid.velocity*t+bodyOffset[body];
                    q+=bodyOffset[body]+math.cross(bodyAngle[body],q-rigid.centre-rigid.velocity*t);
                }
                SurfaceContact(surface[face],q,shapeVelocity,reach,body,centre,h);
            }
        }

        void SurfaceContact(int3 ids,float3 q,float3 shapeVelocity,float reach,int body,float3 centre,float h)
        {
            float3 a=x[ids.x],b=x[ids.y],c=x[ids.z];
            float3 faceNormal=math.cross(b-a,c-a);
            float normalLength=math.length(faceNormal);if(normalLength<1e-12f)return;
            faceNormal/=normalLength;
            float3 bary=Closest(q,a,b,c),p=a*bary.x+b*bary.y+c*bary.z;
            float3 delta=q-p;float distance=math.length(delta);
            float side=math.dot(delta,faceNormal);
            float3 normal=side>=0 && distance>1e-9f ? -delta/distance : -faceNormal;
            float depth=reach-(side>=0?distance:-distance);
            if(distance>reach)
            {
                // Sweep a sphere centre against the moving triangle's plane, then verify its
                // Voronoi region at impact. Thin fingertips need not rely on an explicit CFL step.
                float3 v=shapeVelocity;
                if(body>=0)v+=bodyDeltaVelocity[body]+math.cross(bodyDeltaOmega[body],q-centre);
                float3 q0=q-v*h;
                float d0=math.dot(q0-previous[ids.x],faceNormal),d1=math.dot(q-a,faceNormal);
                if(d0 < -reach || d1>=reach || d0-d1<1e-9f)return;
                float fraction=math.saturate((d0-reach)/(d0-d1));
                float3 ah=math.lerp(previous[ids.x],a,fraction),bh=math.lerp(previous[ids.y],b,fraction),ch=math.lerp(previous[ids.z],c,fraction);
                float3 hit=math.lerp(q0,q,fraction);
                bary=Closest(hit,ah,bh,ch);
                float3 closest=ah*bary.x+bh*bary.y+ch*bary.z;
                if(math.lengthsq(hit-closest)>reach*reach*1.0001f)return;
                p=a*bary.x+b*bary.y+c*bary.z;normal=-faceNormal;depth=reach-math.dot(q-p,faceNormal);
            }
            float3 before=previous[ids.x]*bary.x+previous[ids.y]*bary.y+previous[ids.z]*bary.z;
            float sampleInverseMass=bary.x*bary.x*inverseMass[ids.x]*math.dot(Allowed(ids.x,normal),normal)+bary.y*bary.y*inverseMass[ids.y]*math.dot(Allowed(ids.y,normal),normal)+bary.z*bary.z*inverseMass[ids.z]*math.dot(Allowed(ids.z,normal),normal);
            float3 r=p-centre;
            float bodyNormalMass=0;
            if(body>=0){float3 rn=math.cross(r,normal);bodyNormalMass=bodies[body].inverseMass+math.dot(rn,math.mul(bodies[body].inverseInertia,rn));}
            float denominator=sampleInverseMass+bodyNormalMass;if(denominator<1e-9f)return;
            float3 relative=p-before-shapeVelocity*h;
            if(body>=0)relative-=(bodyDeltaVelocity[body]+math.cross(bodyDeltaOmega[body],r))*h;
            // Zero restitution: overlap stabilization must not inject a depth/h launch velocity.
            float normalTravel=math.min(depth,math.max(0,-math.dot(relative,normal)));
            float3 positionImpulse=normal*(normalTravel/denominator);
            float3 tangent=relative-normal*math.dot(relative,normal);float length=math.length(tangent);
            if(length>1e-9f && friction>0)
            {
                float3 direction=tangent/length;float tangentMass=bary.x*bary.x*inverseMass[ids.x]*math.dot(Allowed(ids.x,direction),direction)+bary.y*bary.y*inverseMass[ids.y]*math.dot(Allowed(ids.y,direction),direction)+bary.z*bary.z*inverseMass[ids.z]*math.dot(Allowed(ids.z,direction),direction);
                if(body>=0){float3 rt=math.cross(r,direction);tangentMass+=bodies[body].inverseMass+math.dot(rt,math.mul(bodies[body].inverseInertia,rt));}
                positionImpulse-=direction*math.min(length/math.max(tangentMass,1e-9f),friction*normalTravel/denominator);
            }
            x[ids.x]+=inverseMass[ids.x]*bary.x*Allowed(ids.x,positionImpulse);
            x[ids.y]+=inverseMass[ids.y]*bary.y*Allowed(ids.y,positionImpulse);
            x[ids.z]+=inverseMass[ids.z]*bary.z*Allowed(ids.z,positionImpulse);
            if(body<0)return;
            float3 angular=math.mul(bodies[body].inverseInertia,math.cross(r,positionImpulse));
            bodyOffset[body]-=bodies[body].inverseMass*positionImpulse;bodyAngle[body]-=angular;
            bodyDeltaVelocity[body]-=bodies[body].inverseMass*positionImpulse/h;bodyDeltaOmega[body]-=angular/h;
            float3 j=positionImpulse/h;impulse[body]+=j;torque[body]+=math.cross(r,j);
            float weight=math.length(j);contactPoint[body]+=weight*p;contactWeight[body]+=weight;
        }
    }
}
