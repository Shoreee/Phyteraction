using Unity.Mathematics;

namespace PhysicsHands.Simulation
{
    internal static class ModalIntegrator
    {
        internal static void Advance(float w, float q0, float v0, float h, float beta, out float q, out float qd)
        {
            float e = math.exp(-beta * h), d2 = w * w - beta * beta;
            float c, s, sd; // cos-like, sin/omega_d-like and omega_d*sin-like terms of the damped oscillator
            if (d2 > 1e-8f)
            {
                float wd = math.sqrt(d2);
                math.sincos(wd * h, out s, out c);
                sd = wd * s;
                s /= wd;
            }
            else if (d2 < -1e-8f)
            {
                float g = math.sqrt(-d2), ep = math.exp(g * h), em = 1 / ep;
                c = .5f * (ep + em);
                s = .5f * (ep - em) / g;
                sd = -g * g * s;
            }
            else
            {
                c = 1;
                s = h;
                sd = 0;
            }

            q = e * (q0 * (c + beta * s) + v0 * s);
            qd = e * (v0 * (c - beta * s) - q0 * (sd + beta * beta * s));
        }
    }
}
