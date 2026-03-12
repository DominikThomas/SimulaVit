using NUnit.Framework;
public class ReplicatorSpawnSystemTests
{
    [Test]
    public void HandleSpontaneousSpawning_PreservesLowPopulationBehavior()
    {
        var system = new ReplicatorSpawnSystem();
        int randomAttempts = 0;

        system.HandleSpontaneousSpawning(
            enableSpontaneousSpawning: true,
            guaranteedFirstSpawnWithinSeconds: 10f,
            spawnAttemptInterval: 0.5f,
            simulationDeltaSeconds: 1f,
            getTotalPopulation: () => 50,
            disableSpontaneousSpawningAtPopulation: 1000,
            reenableSpontaneousSpawningAtPopulation: 200,
            tryGuaranteedSpawn: () => false,
            tryRandomSpontaneousSpawn: () =>
            {
                randomAttempts++;
                return false;
            });

        Assert.That(randomAttempts, Is.EqualTo(2));
    }

    [Test]
    public void HandleSpontaneousSpawning_DisablesWhenPopulationAtOrAboveDisableThreshold()
    {
        var system = new ReplicatorSpawnSystem();
        int randomAttempts = 0;
        int guaranteedAttempts = 0;

        system.HandleSpontaneousSpawning(
            enableSpontaneousSpawning: true,
            guaranteedFirstSpawnWithinSeconds: 0f,
            spawnAttemptInterval: 0.1f,
            simulationDeltaSeconds: 1f,
            getTotalPopulation: () => 1000,
            disableSpontaneousSpawningAtPopulation: 1000,
            reenableSpontaneousSpawningAtPopulation: 200,
            tryGuaranteedSpawn: () =>
            {
                guaranteedAttempts++;
                return true;
            },
            tryRandomSpontaneousSpawn: () =>
            {
                randomAttempts++;
                return true;
            });

        Assert.That(guaranteedAttempts, Is.EqualTo(0));
        Assert.That(randomAttempts, Is.EqualTo(0));
    }

    [Test]
    public void HandleSpontaneousSpawning_RemainsDisabledUntilReenableThreshold()
    {
        var system = new ReplicatorSpawnSystem();
        int population = 1000;
        int randomAttempts = 0;

        // Disabled at/above disable threshold.
        system.HandleSpontaneousSpawning(
            true,
            10f,
            1f,
            1f,
            () => population,
            1000,
            200,
            () => false,
            () =>
            {
                randomAttempts++;
                return false;
            });

        population = 500;
        system.HandleSpontaneousSpawning(
            true,
            10f,
            1f,
            1f,
            () => population,
            1000,
            200,
            () => false,
            () =>
            {
                randomAttempts++;
                return false;
            });

        population = 200;
        system.HandleSpontaneousSpawning(
            true,
            10f,
            1f,
            1f,
            () => population,
            1000,
            200,
            () => false,
            () =>
            {
                randomAttempts++;
                return false;
            });

        Assert.That(randomAttempts, Is.EqualTo(1));
    }

    [Test]
    public void HandleSpontaneousSpawning_MultiStepLoopStopsWhenThresholdReachedMidStep()
    {
        var system = new ReplicatorSpawnSystem();
        int population = 900;
        int randomAttempts = 0;

        system.HandleSpontaneousSpawning(
            enableSpontaneousSpawning: true,
            guaranteedFirstSpawnWithinSeconds: 10f,
            spawnAttemptInterval: 0.25f,
            simulationDeltaSeconds: 1f,
            getTotalPopulation: () => population,
            disableSpontaneousSpawningAtPopulation: 1000,
            reenableSpontaneousSpawningAtPopulation: 200,
            tryGuaranteedSpawn: () => false,
            tryRandomSpontaneousSpawn: () =>
            {
                randomAttempts++;
                population = 1000;
                return true;
            });

        Assert.That(randomAttempts, Is.EqualTo(1));
    }
}
