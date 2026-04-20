using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class PlanetGenerationCache
{
    public const int PlanetCacheFormatVersion = 1;
    public const int ResourceCacheFormatVersion = 1;
    public const int SurfaceTextureCacheFormatVersion = 1;

    private const string CacheFolderName = "SimulaVitPlanetCache";
    private const string PlanetMagic = "SV_PLANET_CACHE_V1";
    private const string ResourceMagic = "SV_RESOURCE_CACHE_V1";
    private const string SurfaceTextureMagic = "SV_SURFACE_TEX_CACHE_V1";
    private const int MaxCacheEntries = 256;
    private const int MaxTotalCacheSizeMb = 1024;

    public sealed class PlanetData
    {
        public Vector3[] UnitVertices;
        public int[] Triangles;
        public float[] FinalTerrainRadii;
        public byte[] OceanMaskByCell;
        public float[] LocalOceanDepthByCell;
        public float[] OceanDistanceToShoreByCell;
        public float OceanNoiseThreshold;
    }

    public sealed class ResourceData
    {
        public Vector3[] CellDirections;
        public byte[] OceanMask;
        public float[] Phosphorus;
        public float[] Iron;
        public float[] Silicon;
        public float[] Calcium;
        public byte[] VentMask;
        public float[] VentStrength;
        public int[] VentCells;
    }

    public sealed class SurfaceTextureData
    {
        public int Width;
        public int Height;
        public TextureFormat Format;
        public bool LinearColorSpace;
        public byte[] RawTextureData;
    }

    public static string BuildPlanetCachePath(string keyString)
    {
        return BuildCachePath("planet", keyString);
    }

    public static string BuildResourceCachePath(string keyString)
    {
        return BuildCachePath("resource", keyString);
    }

    public static string BuildSurfaceTextureCachePath(string keyString)
    {
        return BuildCachePath("surface_texture", keyString);
    }

    public static string BuildPlanetCacheKeyString(PlanetGenerator generator)
    {
        StringBuilder sb = new StringBuilder(512);
        AppendKey(sb, "format", PlanetCacheFormatVersion);
        AppendKey(sb, "seed", generator.randomSeed);
        AppendKey(sb, "resolution", generator.resolution);
        AppendKey(sb, "radius", generator.radius);
        AppendKey(sb, "noiseMagnitude", generator.noiseMagnitude);
        AppendKey(sb, "noiseRoughness", generator.noiseRoughness);
        AppendKey(sb, "numLayers", generator.numLayers);
        AppendKey(sb, "persistence", generator.persistence);
        AppendKey(sb, "noiseOffset", generator.noiseOffset);
        AppendKey(sb, "enableOcean", generator.enableOcean);
        AppendKey(sb, "oceanCoveragePercent", generator.oceanCoveragePercent);
        AppendKey(sb, "oceanDepth", generator.oceanDepth);
        AppendKey(sb, "enableBathymetry", generator.enableBathymetry);
        AppendKey(sb, "shelfDistance", generator.shelfDistance);
        AppendKey(sb, "shelfDepth", generator.shelfDepth);
        AppendKey(sb, "slopeStrength", generator.slopeStrength);
        AppendKey(sb, "maxOceanDepth", generator.maxOceanDepth);
        AppendKey(sb, "basinNoiseScale", generator.basinNoiseScale);
        AppendKey(sb, "basinNoiseStrength", generator.basinNoiseStrength);
        AppendKey(sb, "basinNoiseOffset", generator.basinNoiseOffset);
        AppendKey(sb, "bathymetrySmoothPasses", generator.bathymetrySmoothPasses);
        AppendKey(sb, "bathymetrySmoothStrength", generator.bathymetrySmoothStrength);
        AppendKey(sb, "shorelinePreservationDistance", generator.shorelinePreservationDistance);
        AppendKey(sb, "bathymetryVisualStrength", generator.bathymetryVisualStrength);
        return sb.ToString();
    }

    public static string BuildResourceCacheKeyString(PlanetGenerator generator, PlanetResourceMap resourceMap, int resolution)
    {
        StringBuilder sb = new StringBuilder(512);
        AppendKey(sb, "format", ResourceCacheFormatVersion);
        AppendKey(sb, "planetKey", BuildPlanetCacheKeyString(generator));
        AppendKey(sb, "seed", generator != null ? generator.randomSeed : 0);
        AppendKey(sb, "resolution", resolution);
        AppendKey(sb, "phosphorusScale", resourceMap.phosphorusScale);
        AppendKey(sb, "ironScale", resourceMap.ironScale);
        AppendKey(sb, "siliconPatchScale", resourceMap.siliconPatchScale);
        AppendKey(sb, "calciumPatchScale", resourceMap.calciumPatchScale);
        AppendKey(sb, "baselineSi", resourceMap.baselineSi);
        AppendKey(sb, "baselineCa", resourceMap.baselineCa);
        AppendKey(sb, "ventFrequency", resourceMap.ventFrequency);
        AppendKey(sb, "ventStrengthMin", resourceMap.ventStrengthMin);
        AppendKey(sb, "ventStrengthMax", resourceMap.ventStrengthMax);
        AppendKey(sb, "ventNoiseScale", resourceMap.ventNoiseScale);
        AppendKey(sb, "ventThreshold", resourceMap.ventThreshold);
        AppendKey(sb, "ventReferenceResolution", resourceMap.ventReferenceResolution);
        AppendKey(sb, "ventReferencePlanetRadius", resourceMap.ventReferencePlanetRadius);
        AppendKey(sb, "ventSelectionModel", 2);
        return sb.ToString();
    }

    public static string BuildSurfaceTextureCacheKeyString(PlanetGenerator generator, int width, int height, TextureFormat format, bool linearColorSpace)
    {
        StringBuilder sb = new StringBuilder(640);
        AppendKey(sb, "format", SurfaceTextureCacheFormatVersion);
        AppendKey(sb, "planetKey", BuildPlanetCacheKeyString(generator));
        AppendKey(sb, "seed", generator.randomSeed);
        AppendKey(sb, "resolution", generator.resolution);
        AppendKey(sb, "width", width);
        AppendKey(sb, "height", height);
        AppendKey(sb, "textureFormat", (int)format);
        AppendKey(sb, "linear", linearColorSpace);
        AppendKey(sb, "largeNoiseScale", generator.largeNoiseScale);
        AppendKey(sb, "mediumNoiseScale", generator.mediumNoiseScale);
        AppendKey(sb, "detailNoiseScale", generator.detailNoiseScale);
        AppendKey(sb, "contrast", generator.contrast);
        AppendKey(sb, "crackDarkening", generator.crackDarkening);
        AppendKey(sb, "noiseOffset", generator.noiseOffset);
        AppendKey(sb, "darkRockColor", generator.darkRockColor);
        AppendKey(sb, "midRockColor", generator.midRockColor);
        AppendKey(sb, "lightRockColor", generator.lightRockColor);
        return sb.ToString();
    }

    public static bool TryLoadPlanet(string cachePath, int expectedCellCount, out PlanetData data)
    {
        data = null;
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(cachePath);
            using BinaryReader reader = new BinaryReader(stream);
            string magic = reader.ReadString();
            if (!string.Equals(magic, PlanetMagic, StringComparison.Ordinal))
            {
                return false;
            }

            Vector3[] unitVertices = ReadVector3Array(reader);
            int[] triangles = ReadIntArray(reader);
            float[] finalTerrainRadii = ReadFloatArray(reader);
            byte[] oceanMask = ReadByteArray(reader);
            float[] localDepth = ReadFloatArray(reader);
            float[] distanceToShore = ReadFloatArray(reader);
            float oceanNoiseThreshold = reader.ReadSingle();

            if (unitVertices == null || finalTerrainRadii == null || oceanMask == null || localDepth == null || distanceToShore == null)
            {
                return false;
            }

            if (unitVertices.Length != expectedCellCount ||
                finalTerrainRadii.Length != expectedCellCount ||
                oceanMask.Length != expectedCellCount ||
                localDepth.Length != expectedCellCount ||
                distanceToShore.Length != expectedCellCount)
            {
                return false;
            }

            data = new PlanetData
            {
                UnitVertices = unitVertices,
                Triangles = triangles,
                FinalTerrainRadii = finalTerrainRadii,
                OceanMaskByCell = oceanMask,
                LocalOceanDepthByCell = localDepth,
                OceanDistanceToShoreByCell = distanceToShore,
                OceanNoiseThreshold = oceanNoiseThreshold
            };
            TouchCacheFile(cachePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerationCache] Planet cache load failed: {ex.Message}");
            return false;
        }
    }

    public static void SavePlanet(string cachePath, PlanetData data)
    {
        if (data == null)
        {
            return;
        }

        try
        {
            EnsureCacheDirectory(cachePath);
            using FileStream stream = File.Open(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(PlanetMagic);
            WriteVector3Array(writer, data.UnitVertices);
            WriteIntArray(writer, data.Triangles);
            WriteFloatArray(writer, data.FinalTerrainRadii);
            WriteByteArray(writer, data.OceanMaskByCell);
            WriteFloatArray(writer, data.LocalOceanDepthByCell);
            WriteFloatArray(writer, data.OceanDistanceToShoreByCell);
            writer.Write(data.OceanNoiseThreshold);
            PruneCacheIfNeeded();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerationCache] Planet cache save failed: {ex.Message}");
        }
    }

    public static bool TryLoadResource(string cachePath, int expectedCellCount, out ResourceData data)
    {
        data = null;
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(cachePath);
            using BinaryReader reader = new BinaryReader(stream);
            string magic = reader.ReadString();
            if (!string.Equals(magic, ResourceMagic, StringComparison.Ordinal))
            {
                return false;
            }

            Vector3[] cellDirs = ReadVector3Array(reader);
            byte[] oceanMask = ReadByteArray(reader);
            float[] phosphorus = ReadFloatArray(reader);
            float[] iron = ReadFloatArray(reader);
            float[] silicon = ReadFloatArray(reader);
            float[] calcium = ReadFloatArray(reader);
            byte[] ventMask = ReadByteArray(reader);
            float[] ventStrength = ReadFloatArray(reader);
            int[] ventCells = ReadIntArray(reader);

            if (cellDirs == null || oceanMask == null || phosphorus == null || iron == null || silicon == null || calcium == null || ventMask == null || ventStrength == null || ventCells == null)
            {
                return false;
            }

            if (cellDirs.Length != expectedCellCount ||
                oceanMask.Length != expectedCellCount ||
                phosphorus.Length != expectedCellCount ||
                iron.Length != expectedCellCount ||
                silicon.Length != expectedCellCount ||
                calcium.Length != expectedCellCount ||
                ventMask.Length != expectedCellCount ||
                ventStrength.Length != expectedCellCount)
            {
                return false;
            }

            data = new ResourceData
            {
                CellDirections = cellDirs,
                OceanMask = oceanMask,
                Phosphorus = phosphorus,
                Iron = iron,
                Silicon = silicon,
                Calcium = calcium,
                VentMask = ventMask,
                VentStrength = ventStrength,
                VentCells = ventCells
            };
            TouchCacheFile(cachePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerationCache] Resource cache load failed: {ex.Message}");
            return false;
        }
    }

    public static void SaveResource(string cachePath, ResourceData data)
    {
        if (data == null)
        {
            return;
        }

        try
        {
            EnsureCacheDirectory(cachePath);
            using FileStream stream = File.Open(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(ResourceMagic);
            WriteVector3Array(writer, data.CellDirections);
            WriteByteArray(writer, data.OceanMask);
            WriteFloatArray(writer, data.Phosphorus);
            WriteFloatArray(writer, data.Iron);
            WriteFloatArray(writer, data.Silicon);
            WriteFloatArray(writer, data.Calcium);
            WriteByteArray(writer, data.VentMask);
            WriteFloatArray(writer, data.VentStrength);
            WriteIntArray(writer, data.VentCells);
            PruneCacheIfNeeded();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerationCache] Resource cache save failed: {ex.Message}");
        }
    }

    public static bool TryLoadSurfaceTexture(string cachePath, int expectedWidth, int expectedHeight, TextureFormat expectedFormat, bool expectedLinearColorSpace, out SurfaceTextureData data)
    {
        data = null;
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(cachePath);
            using BinaryReader reader = new BinaryReader(stream);
            string magic = reader.ReadString();
            if (!string.Equals(magic, SurfaceTextureMagic, StringComparison.Ordinal))
            {
                return false;
            }

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            TextureFormat format = (TextureFormat)reader.ReadInt32();
            bool linearColorSpace = reader.ReadBoolean();
            byte[] rawTextureData = ReadByteArray(reader);

            if (width != expectedWidth || height != expectedHeight || format != expectedFormat || linearColorSpace != expectedLinearColorSpace || rawTextureData == null)
            {
                return false;
            }

            int expectedBytes = width * height * 4;
            if (rawTextureData.Length != expectedBytes)
            {
                return false;
            }

            data = new SurfaceTextureData
            {
                Width = width,
                Height = height,
                Format = format,
                LinearColorSpace = linearColorSpace,
                RawTextureData = rawTextureData
            };
            TouchCacheFile(cachePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerationCache] Surface texture cache load failed: {ex.Message}");
            return false;
        }
    }

    public static void SaveSurfaceTexture(string cachePath, SurfaceTextureData data)
    {
        if (data == null || data.RawTextureData == null)
        {
            return;
        }

        try
        {
            EnsureCacheDirectory(cachePath);
            using FileStream stream = File.Open(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(SurfaceTextureMagic);
            writer.Write(data.Width);
            writer.Write(data.Height);
            writer.Write((int)data.Format);
            writer.Write(data.LinearColorSpace);
            WriteByteArray(writer, data.RawTextureData);
            PruneCacheIfNeeded();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerationCache] Surface texture cache save failed: {ex.Message}");
        }
    }

    public static int ClearAllCacheFiles()
    {
        string cacheDirectory = GetCacheDirectoryPath();
        if (!Directory.Exists(cacheDirectory))
        {
            return 0;
        }

        int deletedCount = 0;
        foreach (string path in Directory.GetFiles(cacheDirectory, "*.bin", SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(path);
                deletedCount++;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlanetGenerationCache] Failed to delete cache file '{path}': {ex.Message}");
            }
        }

        if (deletedCount > 0)
        {
            Debug.Log($"[PlanetGenerationCache] Cleared {deletedCount} cache file(s).");
        }

        return deletedCount;
    }

    private static string BuildCachePath(string kind, string keyString)
    {
        string keyHash = HashKey(keyString);
        string fileName = $"{kind}_{keyHash}.bin";
        return Path.Combine(GetCacheDirectoryPath(), fileName);
    }

    private static string HashKey(string key)
    {
        using SHA256 sha = SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(key ?? string.Empty);
        byte[] hash = sha.ComputeHash(bytes);
        StringBuilder sb = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static void EnsureCacheDirectory(string cachePath)
    {
        string directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string GetCacheDirectoryPath()
    {
        return Path.Combine(Application.persistentDataPath, CacheFolderName);
    }

    private static void TouchCacheFile(string cachePath)
    {
        try
        {
            File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow);
        }
        catch
        {
            // Best effort only; cache remains valid even if touch fails.
        }
    }

    private static void PruneCacheIfNeeded()
    {
        try
        {
            string cacheDirectory = GetCacheDirectoryPath();
            if (!Directory.Exists(cacheDirectory))
            {
                return;
            }

            FileInfo[] files = new DirectoryInfo(cacheDirectory)
                .GetFiles("*.bin", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToArray();
            if (files.Length == 0)
            {
                return;
            }

            long maxBytes = MaxTotalCacheSizeMb * 1024L * 1024L;
            long totalBytes = 0;
            for (int i = 0; i < files.Length; i++)
            {
                totalBytes += files[i].Length;
            }

            int filesToDeleteForCount = Math.Max(0, files.Length - MaxCacheEntries);
            int filesToDelete = filesToDeleteForCount;
            long bytesAfterCountPrune = totalBytes;
            for (int i = 0; i < filesToDeleteForCount; i++)
            {
                bytesAfterCountPrune -= files[i].Length;
            }

            while (bytesAfterCountPrune > maxBytes && filesToDelete < files.Length)
            {
                bytesAfterCountPrune -= files[filesToDelete].Length;
                filesToDelete++;
            }

            if (filesToDelete <= 0)
            {
                return;
            }

            long prunedBytes = 0;
            int prunedFiles = 0;
            for (int i = 0; i < filesToDelete; i++)
            {
                try
                {
                    long fileSize = files[i].Length;
                    files[i].Delete();
                    prunedBytes += fileSize;
                    prunedFiles++;
                    totalBytes -= fileSize;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PlanetGenerationCache] Failed to prune cache file '{files[i].Name}': {ex.Message}");
                }
            }

            if (prunedFiles > 0)
            {
                double prunedMb = prunedBytes / (1024d * 1024d);
                double remainingMb = Math.Max(0d, totalBytes) / (1024d * 1024d);
                Debug.Log($"[PlanetGenerationCache] Pruned {prunedFiles} cache file(s) ({prunedMb:F1} MB). Remaining cache size: {remainingMb:F1} MB.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlanetGenerationCache] Cache prune failed: {ex.Message}");
        }
    }

    private static void AppendKey(StringBuilder sb, string name, int value)
    {
        sb.Append(name).Append('=').Append(value).Append(';');
    }

    private static void AppendKey(StringBuilder sb, string name, bool value)
    {
        sb.Append(name).Append('=').Append(value ? 1 : 0).Append(';');
    }

    private static void AppendKey(StringBuilder sb, string name, float value)
    {
        sb.Append(name).Append('=').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
    }

    private static void AppendKey(StringBuilder sb, string name, Vector3 value)
    {
        sb.Append(name).Append('=')
            .Append(value.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.z.ToString("R", CultureInfo.InvariantCulture)).Append(';');
    }

    private static void AppendKey(StringBuilder sb, string name, Color value)
    {
        sb.Append(name).Append('=')
            .Append(value.r.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.g.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.b.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.a.ToString("R", CultureInfo.InvariantCulture)).Append(';');
    }

    private static void AppendKey(StringBuilder sb, string name, string value)
    {
        sb.Append(name).Append('=').Append(value).Append(';');
    }

    private static void WriteVector3Array(BinaryWriter writer, Vector3[] values)
    {
        if (values == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            writer.Write(values[i].x);
            writer.Write(values[i].y);
            writer.Write(values[i].z);
        }
    }

    private static Vector3[] ReadVector3Array(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0)
        {
            return null;
        }

        Vector3[] values = new Vector3[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        return values;
    }

    private static void WriteFloatArray(BinaryWriter writer, float[] values)
    {
        if (values == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static float[] ReadFloatArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0)
        {
            return null;
        }

        float[] values = new float[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = reader.ReadSingle();
        }

        return values;
    }

    private static void WriteIntArray(BinaryWriter writer, int[] values)
    {
        if (values == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static int[] ReadIntArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0)
        {
            return null;
        }

        int[] values = new int[length];
        for (int i = 0; i < length; i++)
        {
            values[i] = reader.ReadInt32();
        }

        return values;
    }

    private static void WriteByteArray(BinaryWriter writer, byte[] values)
    {
        if (values == null)
        {
            writer.Write(-1);
            return;
        }

        writer.Write(values.Length);
        writer.Write(values);
    }

    private static byte[] ReadByteArray(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0)
        {
            return null;
        }

        return reader.ReadBytes(length);
    }
}
