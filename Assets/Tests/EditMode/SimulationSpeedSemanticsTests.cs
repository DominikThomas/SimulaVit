using NUnit.Framework;

public class SimulationSpeedSemanticsTests
{
    private static readonly int[] RequestedMultipliers = { 0, 1, 2, 5, 10, 20, 50, 100 };

    [TestCaseSource(nameof(RequestedMultipliers))]
    public void RequestedMultiplierControlsAuthoritativeClock(int requestedMultiplier)
    {
        double actual = Integrate(10, 0.02f, requestedMultiplier);
        Assert.That(actual, Is.EqualTo(0.2d * requestedMultiplier).Within(1e-5));
    }

    [Test]
    public void PauseProducesNoAdvance()
    {
        Assert.That(Integrate(10, 0.02f, 0), Is.Zero);
    }

    [Test]
    public void FramePartitionDoesNotChangeIntegratedTimeBelowClamp()
    {
        Assert.That(Integrate(20, 0.01f, 100), Is.EqualTo(Integrate(10, 0.02f, 100)).Within(1e-5));
    }

    [Test]
    public void SpeedTransitionsIntegrateEachRequestedMultiplier()
    {
        double tenToHundred = Integrate(1, 0.02f, 10) + Integrate(1, 0.02f, 100);
        double hundredToOne = Integrate(1, 0.02f, 100) + Integrate(1, 0.02f, 1);
        Assert.That(tenToHundred, Is.EqualTo(2.2d).Within(1e-5));
        Assert.That(hundredToOne, Is.EqualTo(2.02d).Within(1e-5));
    }

    private static double Integrate(int frameCount, float frameDelta, int requestedMultiplier)
    {
        double total = 0d;
        for (int frame = 0; frame < frameCount; frame++)
        {
            total += ReplicatorSimulationPipeline.CalculateAuthoritativeFrameAdvance(
                frameDelta,
                requestedMultiplier,
                1f / 30f);
        }
        return total;
    }
}
