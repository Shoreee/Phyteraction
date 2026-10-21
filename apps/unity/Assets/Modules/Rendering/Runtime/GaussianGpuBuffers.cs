using System;
using PhysicsHands.GaussianData;
using UnityEngine;

namespace PhysicsHands.Rendering
{
    internal sealed class GaussianGpuBuffers : IDisposable
    {
        internal readonly ComputeBuffer Source, Deformed, Sh;
        internal readonly bool HasSh;
        bool disposed;
        internal GaussianGpuBuffers(Gaussian[] splats, Vector4[] sh)
        {
            try
            {
                Source = new ComputeBuffer(splats.Length, Gaussian.Stride);
                Source.SetData(splats);
                Deformed = new ComputeBuffer(splats.Length, Gaussian.Stride);
                Deformed.SetData(splats);
                HasSh = sh != null && sh.Length == splats.Length * 3;
                Sh = new ComputeBuffer(HasSh ? splats.Length * 3 : 3, 16);
                if (HasSh)
                    Sh.SetData(sh);
                else
                    Sh.SetData(new Vector4[3]);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Source?.Release();
            Deformed?.Release();
            Sh?.Release();
        }
    }
}
