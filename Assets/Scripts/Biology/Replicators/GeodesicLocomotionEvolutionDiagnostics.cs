using System;
using System.Globalization;
using System.Text;

/// <summary>
/// Observation only: O(live population) sidecar, 32 bounded root cohorts, no ancestry
/// history, random draws, resource reads or phenotype decisions. Packed swap-back
/// notifications keep lifetime/cohort metadata with the organism's stable ID.
/// </summary>
public sealed class GeodesicLocomotionEvolutionDiagnostics
{
    private const int Types = 4, CohortsPerType = 8;
    public bool Enabled { get; private set; }
    public double CoverageStart { get; private set; }
    public readonly long[] BirthTransitions = new long[16];
    public readonly long[] Attempts = new long[Types], Successful = new long[Types], InheritedBirths = new long[Types], MutantBirths = new long[Types];
    public readonly long[] ParentBirths = new long[Types], Deaths = new long[Types], ObservedAssignments = new long[16];
    public readonly double[] OrganismSeconds = new double[Types], FirstSeen = new double[Types];
    public readonly long[] CompleteLifetimeDeaths = new long[Types], CompleteLifetimeOffspring = new long[Types];
    public readonly long[] NoChangeReasons = new long[Types * Enum.GetValues(typeof(GeodesicLocomotionMutationPolicy.NoChangeReason)).Length];
    public readonly long[] AnchorSelected = new long[Types], AnchorIneligible = new long[Types], ReleaseRolled = new long[Types], UpgradeSelected = new long[Types];
    public readonly long[] BirthDepthMismatches = new long[Types];
    public readonly double[] MaximumBirthDepthMismatchKm = new double[Types];
    public long BaselineOrganisms { get; private set; }
    public long UntrackedMutantRoots { get; private set; }

    private struct Life
    {
        public long id;
        public LocomotionType phenotype;
        public int cohort, offspring;
        public double birthTime;
        public bool completeLifetime;
    }
    public struct Cohort
    {
        public long founderId, parentId, births, deaths;
        public int alive;
        public double firstTime, organismSeconds;
        public LocomotionType parentType, founderType;
    }
    private Life[] lives = Array.Empty<Life>();
    private readonly Cohort[] cohorts = new Cohort[Types * CohortsPerType];
    private bool coverageKnown;

    public GeodesicLocomotionEvolutionDiagnostics() { Clear(); }

    public void SetEnabled(bool value, double time)
    {
        if (value == Enabled) return;
        Clear(); Enabled = value; CoverageStart = time; coverageKnown = value;
    }

    public void Observe(int index, long id, LocomotionType type, float seconds, double time)
    {
        if (!Enabled || (uint)type >= Types) return;
        Ensure(index, id, type, time);
        FirstSeen[(int)type] = Math.Min(FirstSeen[(int)type], time);
        ref Life life = ref lives[index];
        if (life.phenotype != type)
        {
            ObservedAssignments[(int)life.phenotype * Types + (int)type]++;
            life.phenotype = type; // Diagnostic assignment/correction, never a mutation birth.
        }
        OrganismSeconds[(int)type] += seconds;
        if (life.cohort >= 0) cohorts[life.cohort].organismSeconds += seconds;
    }

    private void Ensure(int index, long id, LocomotionType type, double time)
    {
        if (!coverageKnown) { CoverageStart = time; coverageKnown = true; }
        if (index >= lives.Length) Array.Resize(ref lives, Math.Max(index + 1, Math.Max(256, lives.Length * 2)));
        if (lives[index].id == id) return;
        lives[index] = new Life { id = id, phenotype = type, cohort = -1, birthTime = time };
        BaselineOrganisms++;
        FirstSeen[(int)type] = Math.Min(FirstSeen[(int)type], time);
    }

    public void Birth(int parentIndex, long parentId, LocomotionType parentType,
        int childIndex, long childId, LocomotionType childType, double time,
        in GeodesicLocomotionMutationPolicy.Result mutation)
    {
        if (!Enabled || (uint)parentType >= Types || (uint)childType >= Types) return;
        Ensure(parentIndex, parentId, parentType, time);
        lives[parentIndex].offspring++;
        ParentBirths[(int)parentType]++;
        BirthTransitions[(int)parentType * Types + (int)childType]++;
        bool changed = parentType != childType;
        if (changed) MutantBirths[(int)childType]++; else InheritedBirths[(int)childType]++;
        if (mutation.attempted)
        {
            Attempts[(int)parentType]++;
            if (mutation.Changed) Successful[(int)parentType]++;
            else NoChangeReasons[(int)parentType * (NoChangeReasons.Length / Types) + (int)mutation.reason]++;
            if (mutation.anchorSelected) AnchorSelected[(int)parentType]++;
            if (mutation.anchorSelected && parentType != LocomotionType.Anchored && !mutation.anchorEligible) AnchorIneligible[(int)parentType]++;
            if (mutation.releaseRolled) ReleaseRolled[(int)parentType]++;
            if (mutation.upgradeSelected) UpgradeSelected[(int)parentType]++;
        }
        else NoChangeReasons[(int)parentType * (NoChangeReasons.Length / Types)
            + (int)GeodesicLocomotionMutationPolicy.NoChangeReason.AttemptGateNotSelected]++;

        int cohort = lives[parentIndex].cohort;
        if (cohort < 0 && changed)
        {
            int first = (int)childType * CohortsPerType;
            for (int i = first; i < first + CohortsPerType; i++)
                if (cohorts[i].founderId == 0)
                {
                    cohort = i;
                    cohorts[i] = new Cohort { founderId = childId, parentId = parentId, firstTime = time, parentType = parentType, founderType = childType };
                    break;
                }
            if (cohort < 0) UntrackedMutantRoots++;
        }
        if (childIndex >= lives.Length) Array.Resize(ref lives, Math.Max(childIndex + 1, Math.Max(256, lives.Length * 2)));
        lives[childIndex] = new Life { id = childId, phenotype = childType, cohort = cohort, birthTime = time, completeLifetime = true };
        FirstSeen[(int)childType] = Math.Min(FirstSeen[(int)childType], time);
        if (cohort >= 0) { cohorts[cohort].alive++; cohorts[cohort].births++; }
    }

    public void Death(int index, long id, LocomotionType type, double time)
    {
        if (!Enabled || (uint)type >= Types) return;
        Ensure(index, id, type, time);
        ref Life life = ref lives[index];
        Deaths[(int)type]++;
        if (life.completeLifetime)
        {
            CompleteLifetimeDeaths[(int)type]++;
            CompleteLifetimeOffspring[(int)type] += life.offspring;
        }
        if (life.cohort >= 0) { cohorts[life.cohort].alive--; cohorts[life.cohort].deaths++; }
    }

    public void SwapBackRemoval(int index, int lastIndex)
    {
        if (!Enabled || index < 0 || index >= lives.Length) return;
        lives[index] = lastIndex >= 0 && lastIndex < lives.Length ? lives[lastIndex] : default;
        if (lastIndex >= 0 && lastIndex < lives.Length) lives[lastIndex] = default;
    }

    public void ObserveBirthDepth(LocomotionType type, double outsideLayerKm)
    {
        if (!Enabled || (uint)type >= Types || !(outsideLayerKm > 0d) || !double.IsFinite(outsideLayerKm)) return;
        BirthDepthMismatches[(int)type]++;
        MaximumBirthDepthMismatchKm[(int)type] = Math.Max(MaximumBirthDepthMismatchKm[(int)type], outsideLayerKm);
    }

    public Cohort GetCohort(int slot) => cohorts[slot];

    public string Summary(double time)
    {
        var text = new StringBuilder(1800);
        text.Append("[GeodesicLocomotionEvolution] cumulative=true order=P/A/F/X simTime=").Append(F(time))
            .Append(" coverageStart=").Append(F(CoverageStart)).Append(" baselineLifetimeCensored=").Append(BaselineOrganisms)
            .Append(" birthMatrix=").Append(string.Join(",", BirthTransitions))
            .Append(" inheritedBirths=").Append(string.Join("/", InheritedBirths)).Append(" mutantBirths=").Append(string.Join("/", MutantBirths))
            .Append(" attempts/successes=").Append(string.Join("/", Attempts)).Append(';').Append(string.Join("/", Successful))
            .Append(" stage(anchorSelected/ineligible/releaseRolled/upgradeSelected)=").Append(string.Join("/", AnchorSelected)).Append(';')
            .Append(string.Join("/", AnchorIneligible)).Append(';').Append(string.Join("/", ReleaseRolled)).Append(';').Append(string.Join("/", UpgradeSelected))
            .Append(" noChangeReasonsByParent=").Append(string.Join(",", NoChangeReasons)).Append(" reasonOrder=")
            .Append(string.Join(",", Enum.GetNames(typeof(GeodesicLocomotionMutationPolicy.NoChangeReason))))
            .Append(" observedAssignments=").Append(string.Join(",", ObservedAssignments)).Append(" deaths=").Append(string.Join("/", Deaths))
            .Append(" organismSeconds=");
        for (int i = 0; i < Types; i++) text.Append(i == 0 ? "" : "/").Append(F(OrganismSeconds[i]));
        text.Append(" parentBirthsPerOrgSecond=");
        for (int i = 0; i < Types; i++) text.Append(i == 0 ? "" : "/").Append(F(OrganismSeconds[i] > 0d ? ParentBirths[i] / OrganismSeconds[i] : double.NaN));
        text.Append(" firstSeen=");
        for (int i = 0; i < Types; i++) text.Append(i == 0 ? "" : "/").Append(double.IsPositiveInfinity(FirstSeen[i]) ? "none" : F(FirstSeen[i]));
        text.Append(" completeLifetimeDeaths/offspring=").Append(string.Join("/", CompleteLifetimeDeaths)).Append(';').Append(string.Join("/", CompleteLifetimeOffspring))
            .Append(" birthDepthOutsideAssignedLayer=").Append(string.Join("/", BirthDepthMismatches)).Append(" maxOutsideKm=");
        for (int i = 0; i < Types; i++) text.Append(i == 0 ? "" : "/").Append(F(MaximumBirthDepthMismatchKm[i]));
        return text.ToString();
    }

    public string CohortSummary()
    {
        var text = new StringBuilder(2400);
        text.Append("[GeodesicLocomotionCohorts] sample=first8UntrackedMutantRootsPerDestination descendantsIncludeLaterPhenotypes=true untrackedRoots=")
            .Append(UntrackedMutantRoots).Append(" roots=");
        for (int i = 0; i < cohorts.Length; i++)
        {
            Cohort c = cohorts[i]; if (c.founderId == 0) continue;
            text.Append('{').Append(c.founderId).Append(" parent=").Append(c.parentId).Append(" path=").Append(c.parentType).Append('>').Append(c.founderType)
                .Append(" first=").Append(F(c.firstTime)).Append(" alive/born/dead=").Append(c.alive).Append('/').Append(c.births).Append('/').Append(c.deaths)
                .Append(" orgSeconds=").Append(F(c.organismSeconds)).Append('}');
        }
        return text.ToString();
    }

    private static string F(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
    public void Clear()
    {
        Enabled = false; CoverageStart = 0d; coverageKnown = false; BaselineOrganisms = UntrackedMutantRoots = 0;
        Array.Clear(lives, 0, lives.Length); Array.Clear(cohorts, 0, cohorts.Length);
        foreach (var array in new Array[] { BirthTransitions, Attempts, Successful, InheritedBirths, MutantBirths, ParentBirths, Deaths,
            ObservedAssignments, OrganismSeconds, CompleteLifetimeDeaths, CompleteLifetimeOffspring, NoChangeReasons, AnchorSelected,
            AnchorIneligible, ReleaseRolled, UpgradeSelected, BirthDepthMismatches, MaximumBirthDepthMismatchKm }) Array.Clear(array, 0, array.Length);
        for (int i = 0; i < Types; i++) FirstSeen[i] = double.PositiveInfinity;
    }
}
