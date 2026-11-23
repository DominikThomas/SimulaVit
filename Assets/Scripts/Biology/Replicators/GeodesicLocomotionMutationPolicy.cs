using System;

/// <summary>One birth's existing geodesic mutation decision; no environmental selection.</summary>
public static class GeodesicLocomotionMutationPolicy
{
    public struct Settings
    {
        public float mutation, upgrade, anchor, release, diagnosticMutation;
        public bool allowRelease, overrideMutation;
        public float EffectiveMutation => overrideMutation ? diagnosticMutation : mutation;
    }

    public enum NoChangeReason
    {
        None, AttemptGateNotSelected, UpgradeRollNotSelected, AnchorOutsideBottomAndUpgradeNotSelected,
        NoUpgradePath, AnchoredReleaseDisabled, AnchoredReleaseRollFailed, DestinationUnavailable
    }

    public struct Result
    {
        public LocomotionType source, target;
        public bool correctedInheritance, attempted, anchorSelected, anchorEligible, releaseRolled, upgradeSelected;
        public int randomDraws;
        public NoChangeReason reason;
        public bool Changed => source != target;
    }

    // Deliberately preserves branch/RNG order, including the ineffectual upgrade roll
    // for an anchored/flagellar parent after an unsuccessful anchoring/release branch.
    public static Result Sample(LocomotionType source, bool activeBottomLayer, in Settings settings, Random random,
        int allowedLocomotionMask = GeodesicBiologyAvailability.AllLocomotions)
    {
        var result = new Result { source = source, target = source };
        if ((uint)source > (uint)LocomotionType.Anchored)
        {
            result.correctedInheritance = true;
            result.source = result.target = source = LocomotionType.PassiveDrift;
        }
        float chance = settings.EffectiveMutation;
        if (chance <= 0f || Next(random, ref result) >= chance)
        { result.reason = NoChangeReason.AttemptGateNotSelected; return result; }
        result.attempted = true;
        result.anchorSelected = Next(random, ref result) < settings.anchor;
        result.anchorEligible = source != LocomotionType.Anchored && activeBottomLayer;
        LocomotionType candidate = source;
        if (result.anchorSelected)
        {
            if (result.anchorEligible) candidate = LocomotionType.Anchored;
            else if (source == LocomotionType.Anchored && settings.allowRelease)
            {
                result.releaseRolled = true;
                if (Next(random, ref result) < settings.release) candidate = LocomotionType.Amoeboid;
            }
        }
        if (candidate == source)
        {
            result.upgradeSelected = Next(random, ref result) < settings.upgrade;
            if (result.upgradeSelected)
            {
                if (source == LocomotionType.PassiveDrift) candidate = LocomotionType.Amoeboid;
                else if (source == LocomotionType.Amoeboid) candidate = LocomotionType.Flagellum;
            }
        }
        // Retain the parent's type when the chosen destination is unavailable. Keep the original roll order.
        if (candidate != source && !AllowsDestination(allowedLocomotionMask, candidate))
        {
            result.reason = NoChangeReason.DestinationUnavailable;
            return result;
        }
        result.target = candidate;
        if (!result.Changed)
        {
            if (source == LocomotionType.Anchored && result.anchorSelected)
                result.reason = !settings.allowRelease ? NoChangeReason.AnchoredReleaseDisabled : NoChangeReason.AnchoredReleaseRollFailed;
            else if (source == LocomotionType.Anchored || source == LocomotionType.Flagellum)
                result.reason = NoChangeReason.NoUpgradePath;
            else result.reason = result.anchorSelected && !result.anchorEligible
                ? NoChangeReason.AnchorOutsideBottomAndUpgradeNotSelected : NoChangeReason.UpgradeRollNotSelected;
        }
        return result;
    }

    private static double Next(Random random, ref Result result) { result.randomDraws++; return random.NextDouble(); }

    private static bool AllowsDestination(int mask, LocomotionType type) => (mask & (1 << (int)type)) != 0;

    /// <summary>Rows/columns P,A,F,X. Exact probabilities for the sanitized runtime values.</summary>
    public static double[] TransitionMatrix(in Settings settings, bool activeBottomLayer,
        int allowedLocomotionMask = GeodesicBiologyAvailability.AllLocomotions)
    {
        var matrix = new double[16];
        double m = settings.EffectiveMutation, a = settings.anchor, u = settings.upgrade;
        double anchor = activeBottomLayer && AllowsDestination(allowedLocomotionMask, LocomotionType.Anchored) ? m * a : 0d;
        double upgrade = m * (1d - (activeBottomLayer ? a : 0d)) * u;
        bool allowAmoeboid = AllowsDestination(allowedLocomotionMask, LocomotionType.Amoeboid);
        double upgradeToAmoeboid = allowAmoeboid ? upgrade : 0d;
        double upgradeToFlagellum = AllowsDestination(allowedLocomotionMask, LocomotionType.Flagellum) ? upgrade : 0d;
        matrix[0] = 1d - anchor - upgradeToAmoeboid; matrix[1] = upgradeToAmoeboid; matrix[3] = anchor;
        matrix[5] = 1d - anchor - upgradeToFlagellum; matrix[6] = upgradeToFlagellum; matrix[7] = anchor;
        matrix[10] = 1d - anchor; matrix[11] = anchor;
        matrix[13] = settings.allowRelease && allowAmoeboid ? m * a * settings.release : 0d;
        matrix[15] = 1d - matrix[13];
        return matrix;
    }
}
