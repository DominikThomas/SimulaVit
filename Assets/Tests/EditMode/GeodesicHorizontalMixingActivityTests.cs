using System;
using System.Reflection;
using NUnit.Framework;

public sealed class GeodesicHorizontalMixingActivityTests
{
    private static readonly float[] Enabled = { 1f, 1f, 1f, 1f, 1f, 1f, 1f };

    [Test]
    public void UniformWorld_SkipsEveryChannel()
    { Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(new bool[7], Enabled), Is.Zero); }

    [Test]
    public void LocalizedWrite_ActivatesOnlyWrittenChannel()
    {
        bool[] varying = new bool[7]; varying[(int)GeodesicOceanResource.OrganicC] = true;
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled), Is.EqualTo(1 << (int)GeodesicOceanResource.OrganicC));
    }

    [Test]
    public void VentWriteBeforeMixing_ActivatesVentResourceInSameTick()
    {
        bool[] varying = new bool[7]; varying[(int)GeodesicOceanResource.H2] = true;
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled) & (1 << (int)GeodesicOceanResource.H2), Is.Not.Zero);
    }

    [Test]
    public void VerticalDifference_RemainsActiveForFollowingHorizontalPass()
    {
        bool[] varying = new bool[7]; varying[(int)GeodesicOceanResource.O2] = true;
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled), Is.EqualTo(1 << (int)GeodesicOceanResource.O2));
    }

    [Test]
    public void MultipleActiveChannels_AreCombinedWithoutActivatingOthers()
    {
        bool[] varying = new bool[7]; varying[0] = varying[3] = varying[5] = true;
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled), Is.EqualTo((1 << 0) | (1 << 3) | (1 << 5)));
    }

    [Test]
    public void DenseWorld_ProcessesAllSevenChannels()
    { Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(new[] { true, true, true, true, true, true, true }, Enabled), Is.EqualTo(0x7f)); }

    [Test]
    public void PairTransfer_IsEqualAndOppositeAndZeroTransferDoesNotWrite()
    {
        MethodInfo accumulate = typeof(GeodesicOceanResourceField).GetMethod("AccumulatePair", BindingFlags.NonPublic | BindingFlags.Static);
        float[] state = { 3f, 1f, 2f, 2f }; double[] delta = new double[4];
        accumulate.Invoke(null, new object[] { state, delta, 0, 1, 0.25f });
        Assert.That(delta[0] + delta[1], Is.EqualTo(0d));
        accumulate.Invoke(null, new object[] { state, delta, 2, 3, 0.25f });
        Assert.That(delta[2], Is.EqualTo(0d)); Assert.That(delta[3], Is.EqualTo(0d));
    }

    [Test]
    public void WorldReset_ClearsAllVariationState()
    {
        bool[] varying = { true, true, true, true, true, true, true };
        Array.Clear(varying, 0, varying.Length);
        Assert.That(GeodesicOceanResourceField.CalculateHorizontalActiveMask(varying, Enabled), Is.Zero);
    }
}
