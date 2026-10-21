using PhysicsHands.GaussianData;

namespace PhysicsHands.Simulation
{
    // What GaussianMetrics records for any interactive Gaussian solver.
    public interface ISimulationDiagnostics
    {
        string Provenance
        {
            get;
        }

        string Summary
        {
            get;
        }

        bool Ready
        {
            get;
        }

        long StepId
        {
            get;
        }

        double SolverMs
        {
            get;
        }

        double ContactSampleTime
        {
            get;
        }

        int ShapeCount
        {
            get;
        }

        int PairCount
        {
            get;
        }

        float ReactionForce
        {
            get;
        }

        // Latest samples: RMS displacement from rest (m) and a solver-specific strain measure.
        float RmsDisplacement
        {
            get;
        }

        float StrainMeasure
        {
            get;
        }

        // Solver substeps in the last step and simulated/real time ratio (< 1 when over budget).
        int Substeps
        {
            get;
        }

        float TimeScale
        {
            get;
        }

        void RequestQuality();
    }
}
