using System;

/// <summary>
/// Deterministic inventory fixture, not a planet/ecology simulation. The old modes
/// are literal pre-fix accounting references; the corrected mode uses the production
/// transaction planner. Seed controls death/replacement thresholds only.
/// </summary>
public static class HydrogenotrophyConservationReplay
{
    public sealed class Report
    {
        public string mode;
        public int seed;
        public double seconds, initialCarbon, requestedVentCarbon, measuredVentCarbon;
        public double co2Planned, co2Measured, h2Measured, storeGains, carbonProducts, energy;
        public double deathCarbon, deathMeasured, modelResidual, reactionMeasuredResidual, wholeSystemResidual;
        public long inputTransferViolations, zeroMeasuredInputWithEnergy, precisionRejected;
    }

    public static Report[] Run(int seed = 123456)
        => new[] { RunCase(seed, false, false), RunCase(seed, false, true), RunCase(seed, true, false) };

    private static Report RunCase(int seed, bool corrected, bool floatOcean)
    {
        const int nodes = 4, perNode = 8, steps = 8720;
        const double dt = .25d;
        // Current saved physical rates: CO2 2, H2 5, multiplier 10; split across four nodes.
        const double co2Rate = 20d, h2Rate = 50d;
        const float co2Need = .01f, h2Need = .02f, energyPerTick = 8f, activity = .8f;
        double[] volume = { 1000d, 10000d, 100000d, 1000000d };
        double[] co2 = { 1d, 1d, 1d, 1d }, h2 = new double[nodes], dissolvedCarbon = new double[nodes];
        var stores = new float[nodes * perNode]; var deathThreshold = new float[stores.Length];
        var random = new Random(seed);
        for (int i = 0; i < stores.Length; i++) deathThreshold[i] = .3f + (float)random.NextDouble() * .2f;
        var r = new Report { mode = corrected ? "corrected-double-atomic" : floatOcean ? "old-float-commit-reference" : "old-model-double-reference", seed = seed, seconds = steps * dt };
        for (int n = 0; n < nodes; n++) r.initialCarbon += co2[n] * volume[n];
        for (int step = 0; step < steps; step++)
        for (int n = 0; n < nodes; n++)
        {
            double beforeSource = co2[n];
            co2[n] = Store(co2[n] + co2Rate * dt / nodes / volume[n], floatOcean);
            h2[n] = Store(h2[n] + h2Rate * dt / nodes / volume[n], floatOcean);
            r.requestedVentCarbon += co2Rate * dt / nodes;
            r.measuredVentCarbon += (co2[n] - beforeSource) * volume[n];
            double desired = dt / .5d * (corrected ? activity : 1d);
            double factor = Math.Min(1d, Math.Min(co2[n] * volume[n] / (co2Need * desired * perNode), h2[n] * volume[n] / (h2Need * desired * perNode)));
            double extent = desired * Math.Max(0d, factor);
            double co2PerAgent = co2Need * extent, h2PerAgent = h2Need * extent;
            double measuredCo2PerAgent = 0d, measuredH2PerAgent = 0d;
            if (!corrected)
            {
                double beforeCo2 = co2[n], beforeH2 = h2[n];
                co2[n] = Store(Math.Max(0d, co2[n] - co2PerAgent * perNode / volume[n]), floatOcean);
                h2[n] = Store(Math.Max(0d, h2[n] - h2PerAgent * perNode / volume[n]), floatOcean);
                measuredCo2PerAgent = (beforeCo2 - co2[n]) * volume[n] / perNode;
                measuredH2PerAgent = (beforeH2 - h2[n]) * volume[n] / perNode;
            }
            for (int a = 0; a < perNode; a++)
            {
                int i = n * perNode + a;
                float beforeStore = stores[i];
                double plannedCo2, plannedH2, measuredCo2, measuredH2, desiredStore, energy;
                if (corrected)
                {
                    var t = GeodesicHydrogenotrophyBalance.PlanCommit(co2[n], h2[n], volume[n], extent,
                        co2Need, h2Need, energyPerTick, beforeStore, 10f);
                    if (t.status != GeodesicHydrogenotrophyBalance.CommitStatus.Committed)
                    {
                        if (t.status == GeodesicHydrogenotrophyBalance.CommitStatus.PrecisionRejected) r.precisionRejected++;
                        continue;
                    }
                    co2[n] = t.co2After; h2[n] = t.h2After; stores[i] = t.storeAfter;
                    desiredStore = (double)t.storeAfter - beforeStore;
                    plannedCo2 = co2Need * t.extent; plannedH2 = h2Need * t.extent;
                    measuredCo2 = t.co2Withdrawn; measuredH2 = t.h2Withdrawn; energy = t.energy;
                }
                else
                {
                    desiredStore = co2Need * activity * (float)extent;
                    stores[i] = Math.Min(10f, stores[i] + (float)desiredStore);
                    plannedCo2 = co2PerAgent; plannedH2 = h2PerAgent;
                    measuredCo2 = measuredCo2PerAgent; measuredH2 = measuredH2PerAgent;
                    energy = energyPerTick * (float)extent;
                }
                double gain = (double)stores[i] - beforeStore;
                r.co2Planned += plannedCo2; r.co2Measured += measuredCo2; r.h2Measured += measuredH2;
                r.storeGains += gain; r.energy += energy;
                r.modelResidual += plannedCo2 - desiredStore;
                r.reactionMeasuredResidual += measuredCo2 - gain;
                if (!Matches(plannedCo2, measuredCo2) || !Matches(plannedH2, measuredH2)) r.inputTransferViolations++;
                if (energy > 0d && (measuredCo2 == 0d || measuredH2 == 0d)) r.zeroMeasuredInputWithEnergy++;
                if (stores[i] < deathThreshold[i]) continue;
                // Existing death destination: dissolved OrganicC. No metabolic carbon product.
                double beforeDissolved = dissolvedCarbon[n];
                dissolvedCarbon[n] = Store(dissolvedCarbon[n] + stores[i] / volume[n], floatOcean);
                r.deathCarbon += stores[i];
                r.deathMeasured += (dissolvedCarbon[n] - beforeDissolved) * volume[n];
                stores[i] = 0f;
            }
        }
        double finalCarbon = 0d;
        for (int n = 0; n < nodes; n++) finalCarbon += (co2[n] + dissolvedCarbon[n]) * volume[n];
        for (int i = 0; i < stores.Length; i++) finalCarbon += stores[i];
        r.wholeSystemResidual = r.initialCarbon + r.measuredVentCarbon - finalCarbon;
        return r;
    }

    private static double Store(double value, bool asFloat) => asFloat ? (float)value : value;
    private static bool Matches(double planned, double actual) => Math.Abs(planned - actual)
        <= GeodesicHydrogenotrophyBalance.AbsoluteTolerance + GeodesicHydrogenotrophyBalance.RelativeTolerance * Math.Abs(planned);
}
