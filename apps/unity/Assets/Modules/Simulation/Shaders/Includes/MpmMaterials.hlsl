// ---- SVD: F = U diag(s) V^T with U, V proper rotations; s.z is negative for inverted F. ----
void Jacobi(inout float3x3 a, inout float3x3 q, int p, int r)
{
    float apr = a[p][r];
    if (abs(apr) < 1e-20) return;
    float theta = (a[r][r] - a[p][p]) / (2 * apr);
    float t = (theta >= 0 ? 1 : -1) / (abs(theta) + sqrt(theta * theta + 1));
    float c = rsqrt(t * t + 1), s = t * c;
    float3x3 j = I3; j[p][p] = c; j[r][r] = c; j[p][r] = s; j[r][p] = -s;
    a = mul(transpose(j), mul(a, j)); q = mul(q, j);
}
void Svd(float3x3 f, out float3x3 u, out float3 s, out float3x3 v)
{
    float3x3 a = mul(transpose(f), f), q = I3;
    [unroll] for (int sweep = 0; sweep < 4; sweep++) { Jacobi(a, q, 0, 1); Jacobi(a, q, 0, 2); Jacobi(a, q, 1, 2); }
    float3 e = float3(a[0][0], a[1][1], a[2][2]);
    float3 v0 = float3(q[0][0], q[1][0], q[2][0]), v1 = float3(q[0][1], q[1][1], q[2][1]), v2 = float3(q[0][2], q[1][2], q[2][2]), tv; float te;
    if (e.y > e.x) { te = e.x; e.x = e.y; e.y = te; tv = v0; v0 = v1; v1 = tv; }
    if (e.z > e.x) { te = e.x; e.x = e.z; e.z = te; tv = v0; v0 = v2; v2 = tv; }
    if (e.z > e.y) { te = e.y; e.y = e.z; e.z = te; tv = v1; v1 = v2; v2 = tv; }
    v2 = cross(v0, v1);
    float3 fv0 = mul(f, v0), fv1 = mul(f, v1), fv2 = mul(f, v2);
    float s0 = length(fv0);
    float3 u0 = s0 > 1e-12 ? fv0 / s0 : float3(1, 0, 0);
    float3 w = fv1 - dot(u0, fv1) * u0; float lw = length(w);
    float3 u1 = lw > 1e-12 ? w / lw : normalize(abs(u0.x) < .9 ? cross(u0, float3(1, 0, 0)) : cross(u0, float3(0, 1, 0)));
    float3 u2 = cross(u0, u1);
    s = float3(s0, dot(u1, fv1), dot(u2, fv2));
    u = Columns(u0, u1, u2); v = Columns(v0, v1, v2);
}

// ---- Return mappings (mpm_utils.py) ----
float3x3 VonMises(float3x3 f, inout float yieldStress)
{
    float3x3 u, v; float3 sig; Svd(f, u, sig, v);
    sig = max(sig, .01); float3 eps = log(sig); float tr = eps.x + eps.y + eps.z;
    float3 tau = 2 * _Mu * eps + _Lambda * tr; float3 dev = tau - (tau.x + tau.y + tau.z) / 3;
    if (length(dev) <= yieldStress) return f;
    float3 epsHat = eps - tr / 3; float n = length(epsHat) + 1e-6;
    float dg = n - yieldStress / (2 * _Mu);
    eps -= dg / n * epsHat;
    if (_Hardening != 0) yieldStress += 2 * _Mu * _Xi * dg;
    return mul(u, mul(Diag(exp(eps)), transpose(v)));
}
float3x3 Viscoplastic(float3x3 f, float yieldStress, float dt, bool always)
{
    float3x3 u, v; float3 sig; Svd(f, u, sig, v);
    sig = max(sig, .01); float3 b = sig * sig; float3 eps = log(sig); float tr = eps.x + eps.y + eps.z;
    float3 s = 2 * _Mu * (eps - tr / 3); float sn = length(s);
    float y = sn - sqrt(2.0 / 3.0) * yieldStress;
    if ((!always && y <= 0) || sn < 1e-12) return f;
    float muHat = _Mu * (b.x + b.y + b.z) / 3;
    float sNew = sn - y / (1 + _Viscosity / (2 * muHat * dt));
    float3 epsNew = sNew / sn * s / (2 * _Mu) + tr / 3;
    return mul(u, mul(Diag(exp(epsNew)), transpose(v)));
}
float3x3 Sand(float3x3 f)
{
    float3x3 u, v; float3 sig; Svd(f, u, sig, v);
    // Cohesion (log strain) shifts the cone apex into tension; zero reproduces PhysFlow.
    float3 eps = log(max(abs(sig), 1e-14)) - _Cohesion / 3; float tr = eps.x + eps.y + eps.z;
    float3 epsHat = eps - tr / 3; float n = length(epsHat);
    float dg = n + (3 * _Lambda + 2 * _Mu) / (2 * _Mu) * tr * _FrictionAlpha;
    if (dg <= 0) return f;
    if (tr > 0) return mul(u, mul(Diag(exp(float3(1, 1, 1) * _Cohesion / 3)), transpose(v)));
    float3 h = eps - epsHat * (dg / max(n, 1e-12)) + _Cohesion / 3;
    return mul(u, mul(Diag(exp(h)), transpose(v)));
}
float3x3 Stress(float3x3 f)
{
    float3x3 u, v; float3 sig; Svd(f, u, sig, v);
    float j = determinant(f);

    float3x3 tau;
    if (_Material == 0) // fixed corotated
        tau = 2 * _Mu * mul(f - mul(u, transpose(v)), transpose(f)) + I3 * (_Lambda * j * (j - 1));
    else if (_Material == 2) // Drucker-Prager (Hencky)
    {
        sig = max(abs(sig), 1e-6); float3 l = log(sig); float t = l.x + l.y + l.z;
        tau = mul(u, mul(Diag((2 * _Mu * l + _Lambda * t) / sig), mul(transpose(v), transpose(f))));
    }
    else // StVK in Hencky strain, as PhysGaussian: U diag(tau) V^T F^T
    {
        sig = max(sig, .01); float3 l = log(sig); float t = l.x + l.y + l.z;
        tau = mul(u, mul(Diag(2 * _Mu * l + _Lambda * t), mul(transpose(v), transpose(f))));
    }
    tau = (tau + transpose(tau)) * .5;
    // Broken material keeps only its resistance to compression.
    return tau;
}

// Plastic projection preserves U and V. Reuse that spectral frame for stress
// instead of decomposing the reconstructed F a second time every substep.
float3x3 ProjectAndStress(inout float3x3 f, inout float yieldStress)
{
    float3x3 u, v; float3 sig; Svd(f, u, sig, v);
    bool projected = false;
    if (_Material == 1 || _Material == 4)
    {
        float3 clamped = max(sig, .01), eps = log(clamped); float tr = eps.x + eps.y + eps.z;
        float3 tau = 2 * _Mu * eps + _Lambda * tr;
        float3 dev = tau - (tau.x + tau.y + tau.z) / 3;
        if (length(dev) > yieldStress)
        {
            float3 epsHat = eps - tr / 3; float n = length(epsHat) + 1e-6;
            float dg = n - yieldStress / (2 * _Mu);
            sig = exp(eps - dg / n * epsHat); projected = true;
            if (_Hardening != 0) yieldStress += 2 * _Mu * _Xi * dg;
        }
    }
    else if (_Material == 3 || _Material == 6)
    {
        float3 clamped = max(sig, .01), b = clamped * clamped, eps = log(clamped);
        float tr = eps.x + eps.y + eps.z;
        float3 sh = 2 * _Mu * (eps - tr / 3); float sn = length(sh);
        float y = sn - sqrt(2.0 / 3.0) * yieldStress;
        if ((_Material == 6 || y > 0) && sn >= 1e-12)
        {
            float muHat = _Mu * (b.x + b.y + b.z) / 3;
            float sNew = sn - y / (1 + _Viscosity / (2 * muHat * _Dt));
            sig = exp(sNew / sn * sh / (2 * _Mu) + tr / 3); projected = true;
        }
    }
    else if (_Material == 2)
    {
        float3 eps = log(max(abs(sig), 1e-14)) - _Cohesion / 3;
        float tr = eps.x + eps.y + eps.z;
        float3 epsHat = eps - tr / 3; float n = length(epsHat);
        float dg = n + (3 * _Lambda + 2 * _Mu) / (2 * _Mu) * tr * _FrictionAlpha;
        if (dg > 0)
        {
            sig = tr > 0 ? exp(float3(1,1,1) * _Cohesion / 3)
                : exp(eps - epsHat * (dg / max(n, 1e-12)) + _Cohesion / 3);
            projected = true;
        }
    }
    if (projected) f = mul(u, mul(Diag(sig), transpose(v)));
    float j = determinant(f);
    float3x3 tau;
    if (_Material == 0)
        tau = 2 * _Mu * mul(f - mul(u, transpose(v)), transpose(f)) + I3 * (_Lambda * j * (j - 1));
    else
    {
        float3 sv = _Material == 2 ? max(abs(sig), 1e-6) : max(sig, .01);
        float3 l = log(sv); float tr = l.x + l.y + l.z;
        float3 stress = 2 * _Mu * l + _Lambda * tr;
        if (_Material == 2) stress /= sv;
        tau = mul(u, mul(Diag(stress), mul(transpose(v), transpose(f))));
    }
    tau = (tau + transpose(tau)) * .5;
    return tau;
}

