using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One biology substep's prey positions, with slot mappings maintained through lifecycle removal.</summary>
internal sealed class GeodesicPreyIndex
{
    private struct Bucket
    {
        public int start, count;
    }

    private struct Entry
    {
        public long id;
        public int slot, bucket;
        public bool consumed;
    }

    private readonly Dictionary<Vector3Int, int> bucketByKey = new Dictionary<Vector3Int, int>();
    private Bucket[] buckets = Array.Empty<Bucket>();
    private Entry[] entries = Array.Empty<Entry>();
    private int[] entryBySlot = Array.Empty<int>();
    private int[] entriesInBuckets = Array.Empty<int>();
    private int indexedSlots;
    private double width, rangeSquared;

    internal void Build(List<Replicator> agents, ReplicatorPopulationState state, GeodesicOceanLayerGrid grid, float range)
    {
        width = range;
        rangeSquared = width * width;
        indexedSlots = state.Count;
        if (entries.Length < indexedSlots)
        {
            int capacity = Mathf.NextPowerOfTwo(indexedSlots);
            Array.Resize(ref buckets, capacity);
            Array.Resize(ref entries, capacity);
            Array.Resize(ref entryBySlot, capacity);
            Array.Resize(ref entriesInBuckets, capacity);
        }

        bucketByKey.Clear();
        int count = 0;
        for (int i = 0; i < indexedSlots; i++)
        {
            entryBySlot[i] = -1;
            if (!IsLivingPrey(i, state, grid) || !TryGetKey(state.Position[i], out Vector3Int key)) continue;
            if (!bucketByKey.TryGetValue(key, out int bucket))
            {
                bucket = bucketByKey.Count;
                bucketByKey.Add(key, bucket);
                buckets[bucket] = default;
            }
            buckets[bucket].count++;
            entryBySlot[i] = count;
            entries[count++] = new Entry { id = agents[i].DiagnosticId, slot = i, bucket = bucket };
        }

        int start = 0;
        for (int i = 0; i < bucketByKey.Count; i++)
        {
            int size = buckets[i].count;
            buckets[i] = new Bucket { start = start };
            start += size;
        }
        for (int i = 0; i < count; i++)
        {
            ref Bucket bucket = ref buckets[entries[i].bucket];
            entriesInBuckets[bucket.start + bucket.count++] = i;
        }
    }

    internal static bool IsLivingPrey(int slot, ReplicatorPopulationState state, GeodesicOceanLayerGrid grid)
    {
        float carbon = state.OrganicCStore[slot];
        return state.Metabolism[slot] != MetabolismType.Predation && float.IsFinite(carbon) && carbon > 0f
            && GeodesicBiologyRuntime.ClassifyLifecycleDeath(state.Energy[slot], state.Age[slot], state.MaxLifespan[slot]) == DeathCause.Unknown
            && grid.IsNodeActive(state.GeodesicCellIndex[slot], state.CurrentOceanLayerIndex[slot]);
    }

    internal int FindPrey(Vector3 position, int excludedSlot, uint sample, int budget,
        List<Replicator> agents, ReplicatorPopulationState state, GeodesicOceanLayerGrid grid, out int examined)
    {
        examined = 0;
        if (bucketByKey.Count == 0 || !TryGetKey(position, out Vector3Int origin)) return -1;

        Span<int> localBuckets = stackalloc int[27];
        int localCount = 0, occupants = 0;
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            if (!bucketByKey.TryGetValue(origin + new Vector3Int(x, y, z), out int bucket)) continue;
            localBuckets[localCount++] = bucket;
            occupants += buckets[bucket].count;
        }
        if (occupants == 0) return -1;

        // Randomly rotate an evenly spaced sample across the occupants of all neighboring buckets.
        int limit = Math.Min(occupants, budget);
        long rotation = sample % (uint)occupants;
        // Start at the rotation and walk the logical concatenation of the buckets at most once.
        int local = 0, first = 0;
        for (int k = 0; k < limit; k++)
        {
            int rank = (int)((rotation + (long)k * occupants / limit) % occupants);
            if (rank < first) { local = 0; first = 0; }
            while (rank - first >= buckets[localBuckets[local]].count)
                first += buckets[localBuckets[local++]].count;

            Bucket bucket = buckets[localBuckets[local]];
            Entry entry = entries[entriesInBuckets[bucket.start + rank - first]];
            examined++;
            int slot = entry.slot;
            if (entry.consumed || slot < 0 || slot == excludedSlot || slot >= state.Count || slot >= agents.Count
                || agents[slot].DiagnosticId != entry.id || !IsLivingPrey(slot, state, grid)) continue;
            if (WithinRange(position, state.Position[slot], rangeSquared)) return slot;
        }
        return -1;
    }

    internal bool IsConsumed(int slot, long organismId)
    {
        int entry = GetEntry(slot);
        return entry >= 0 && entries[entry].id == organismId && entries[entry].consumed;
    }

    internal static bool WithinRange(Vector3 first, Vector3 second, double rangeSquared)
    {
        double x = (double)first.x - second.x, y = (double)first.y - second.y, z = (double)first.z - second.z;
        return x * x + y * y + z * z <= rangeSquared;
    }

    internal void MarkConsumed(int slot) => entries[entryBySlot[slot]].consumed = true;

    internal void RemoveAtSwapBack(int slot, int last)
    {
        int removed = GetEntry(slot), moved = GetEntry(last);
        if (removed >= 0) entries[removed].slot = -1;
        if (slot != last && moved >= 0) entries[moved].slot = slot;
        if (slot < indexedSlots) entryBySlot[slot] = slot == last ? -1 : moved;
        if (last < indexedSlots) entryBySlot[last] = -1;
    }

    private int GetEntry(int slot) => slot < indexedSlots ? entryBySlot[slot] : -1;

    private bool TryGetKey(Vector3 position, out Vector3Int key)
    {
        key = default;
        if (!GeodesicBiologyDiagnostics.FinitePosition(position)) return false;
        double x = Math.Floor(position.x / width), y = Math.Floor(position.y / width), z = Math.Floor(position.z / width);
        if (x <= int.MinValue || x >= int.MaxValue || y <= int.MinValue || y >= int.MaxValue
            || z <= int.MinValue || z >= int.MaxValue) return false;
        key = new Vector3Int((int)x, (int)y, (int)z);
        return true;
    }
}
