using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class GeodesicPlanetCacheTests
{
    private string directory;
    private string geodesic;
    private string snapshot;
    private byte[] key;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "SimulaVitCacheTests", Guid.NewGuid().ToString("N"));
        geodesic = Path.Combine(directory, "geodesic", "v1");
        snapshot = Path.Combine(geodesic, "snapshot.bin");
        key = PlanetGenerationCache.HashBinaryKey(writer => writer.Write("carved surface"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Test]
    public void GeodesicSnapshotRejectsWrongIdentityAndDamagedPayload()
    {
        Save("original");
        Assert.That(Load(), Is.EqualTo("original"));
        Assert.That(Load(schema: 2), Is.Null);
        Assert.That(Load(vertices: 13), Is.Null);
        var bytes = File.ReadAllBytes(snapshot);
        bytes[bytes.Length - 1] ^= 1;
        File.WriteAllBytes(snapshot, bytes);
        Assert.That(Load(), Is.Null);
        Save("repaired");
        Assert.That(Load(), Is.EqualTo("repaired"));
        Assert.That(Directory.GetFiles(geodesic, "*.tmp"), Is.Empty);
    }

    [Test]
    public void InterruptedWriteAndGeodesicClearLeaveLegacyDataReadable()
    {
        Save("published");
        PlanetGenerationCache.SaveGeodesic(snapshot, key, 1, 1, 12, 20, writer =>
        {
            writer.Write("incomplete");
            throw new IOException("Simulated interruption.");
        });
        Assert.That(Load(), Is.EqualTo("published"));

        string interrupted = snapshot + ".interrupted.tmp";
        File.WriteAllBytes(interrupted, new byte[] { 1, 2, 3 });
        Assert.That(Load(), Is.EqualTo("published"));

        string legacy = Path.Combine(directory, "legacy.bin");
        string resource = Path.Combine(directory, "resource.bin");
        string texture = Path.Combine(directory, "texture.bin");
        PlanetGenerationCache.SavePlanet(legacy, new PlanetGenerationCache.PlanetData
        {
            UnitVertices = new[] { Vector3.up },
            Triangles = new[] { 0, 0, 0 },
            FinalTerrainRadii = new[] { 8f },
            OceanMaskByCell = new byte[] { 1 },
            LocalOceanDepthByCell = new[] { .125f },
            OceanDistanceToShoreByCell = new[] { .25f },
            OceanNoiseThreshold = .5f
        });
        PlanetGenerationCache.SaveResource(resource, new PlanetGenerationCache.ResourceData
        {
            CellDirections = new[] { Vector3.up },
            OceanMask = new byte[] { 1 },
            Phosphorus = new[] { .1f },
            Iron = new[] { .2f },
            Silicon = new[] { .3f },
            Calcium = new[] { .4f },
            VentMask = new byte[] { 1 },
            VentStrength = new[] { .5f },
            VentCells = new[] { 0 }
        });
        PlanetGenerationCache.SaveSurfaceTexture(texture, new PlanetGenerationCache.SurfaceTextureData
        {
            Width = 1,
            Height = 1,
            Format = TextureFormat.RGBA32,
            LinearColorSpace = true,
            RawTextureData = new byte[] { 1, 2, 3, 4 }
        });
        PlanetGenerationCache.ClearGeodesicCacheFiles(geodesic);
        Assert.That(File.Exists(interrupted), Is.False);
        Assert.That(Load(), Is.Null);
        Assert.That(PlanetGenerationCache.TryLoadPlanet(legacy, 1, out var loaded), Is.True);
        Assert.That(loaded.LocalOceanDepthByCell, Is.EqualTo(new[] { .125f }));
        Assert.That(loaded.OceanNoiseThreshold, Is.EqualTo(.5f));
        Assert.That(PlanetGenerationCache.TryLoadResource(resource, 1, out var resources), Is.True);
        Assert.That(resources.Phosphorus, Is.EqualTo(new[] { .1f }));
        Assert.That(resources.VentCells, Is.EqualTo(new[] { 0 }));
        Assert.That(PlanetGenerationCache.TryLoadSurfaceTexture(texture, 1, 1, TextureFormat.RGBA32, true,
            out var surface), Is.True);
        Assert.That(surface.RawTextureData, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void PublishingEvictsOldestGeodesicRecordAtEntryLimit()
    {
        Directory.CreateDirectory(geodesic);
        for (int i = 0; i < 64; i++)
        {
            string old = Path.Combine(geodesic, $"old-{i}.bin");
            File.WriteAllBytes(old, new byte[] { 0 });
            File.SetLastWriteTimeUtc(old, new DateTime(2020, 1, 1, 0, 0, i % 60, DateTimeKind.Utc).AddMinutes(i / 60));
        }
        Save("new");
        Assert.That(Directory.GetFiles(geodesic, "*.bin"), Has.Length.EqualTo(64));
        Assert.That(File.Exists(Path.Combine(geodesic, "old-0.bin")), Is.False);
        Assert.That(Load(), Is.EqualTo("new"));
    }

    private void Save(string value) =>
        PlanetGenerationCache.SaveGeodesic(snapshot, key, 1, 1, 12, 20, writer => writer.Write(value));

    private string Load(int schema = 1, int vertices = 12) =>
        PlanetGenerationCache.LoadGeodesic(snapshot, key, 1, schema, vertices, 20, reader => reader.ReadString());
}
