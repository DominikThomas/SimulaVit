using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Debug = UnityEngine.Debug;

public static partial class PlanetGenerationCache
{
    private const int GeodesicMagic = 0x53475653;
    private const int GeodesicFileVersion = 1;
    private const int DigestLength = 32;
    private const int GeodesicHeaderLength = 6 * sizeof(int) + sizeof(long) + 2 * DigestLength;
    private const long MaximumGeodesicPayloadBytes = 128L * 1024 * 1024;
    private const long MaximumGeodesicDiskBytes = 1024L * 1024 * 1024;
    private const int MaximumGeodesicFiles = 64;

    internal static string GeodesicDirectory =>
        Path.Combine(GetCacheDirectoryPath(), "geodesic", "v" + GeodesicFileVersion);

    internal static byte[] HashBinaryKey(Action<BinaryWriter> write)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using var buffer = new BufferedStream(stream, 16384);
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
        write(writer);
        writer.Flush();
        stream.FlushFinalBlock();
        return hash.Hash;
    }

    internal static string GeodesicPath(int stage, byte[] key) =>
        Path.Combine(GeodesicDirectory, $"{stage}_{BitConverter.ToString(key).Replace("-", "").ToLowerInvariant()}.bin");

    internal static T LoadGeodesic<T>(string path, byte[] key, int stage, int schema, int vertices, int triangles,
        Func<BinaryReader, T> decode) where T : class =>
        LoadGeodesic(path, key, stage, schema, vertices, triangles, decode, out _);

    internal static T LoadGeodesic<T>(string path, byte[] key, int stage, int schema, int vertices, int triangles,
        Func<BinaryReader, T> decode, out byte[] payloadDigest) where T : class
    {
        payloadDigest = null;
        var watch = Stopwatch.StartNew();
        long managedBefore = GC.GetTotalMemory(false);
        try
        {
            T result;
            long length;
            byte[] expectedDigest;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var reader = new BinaryReader(file))
            {
                if (file.Length < GeodesicHeaderLength || reader.ReadInt32() != GeodesicMagic ||
                    reader.ReadInt32() != GeodesicFileVersion || reader.ReadInt32() != stage ||
                    reader.ReadInt32() != schema || reader.ReadInt32() != vertices || reader.ReadInt32() != triangles ||
                    !reader.ReadBytes(DigestLength).AsSpan().SequenceEqual(key))
                    throw new InvalidDataException("Snapshot header does not match the requested stage.");
                length = reader.ReadInt64();
                expectedDigest = reader.ReadBytes(DigestLength);
                if (length <= 0 || length > MaximumGeodesicPayloadBytes || file.Length != GeodesicHeaderLength + length)
                    throw new InvalidDataException("Snapshot length is invalid.");
                using var hash = SHA256.Create();
                if (!hash.ComputeHash(file).AsSpan().SequenceEqual(expectedDigest))
                    throw new InvalidDataException("Snapshot checksum mismatch.");
                file.Position = GeodesicHeaderLength;
                result = decode(reader);
                if (file.Position != file.Length)
                    throw new InvalidDataException("Unexpected snapshot data after the payload.");
            }
            TouchCacheFile(path);
            payloadDigest = expectedDigest;
            Debug.Log(FormattableString.Invariant($"[GeodesicPlanetCache] hit path={path} readMs={watch.Elapsed.TotalMilliseconds:F2} bytes={length + GeodesicHeaderLength} readManagedHeapDeltaBytes={GC.GetTotalMemory(false) - managedBefore}"));
            return result;
        }
        catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
        {
            Debug.Log($"[GeodesicPlanetCache] miss path={path}");
        }
        catch (Exception ex) when (IsGeodesicCacheFailure(ex))
        {
            Debug.LogWarning($"[GeodesicPlanetCache] rejected path={path}: {ex.Message}");
        }
        return null;
    }

    internal static void SaveGeodesic(string path, byte[] key, int stage, int schema, int vertices, int triangles,
        Action<BinaryWriter> encode)
    {
        var watch = Stopwatch.StartNew();
        long managedBefore = GC.GetTotalMemory(false);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            EnsureCacheDirectory(path);
            string directory = Path.GetDirectoryName(path);
            // Serialize writers as well as publication so temporary disk usage is bounded.
            using var cacheLock = OpenGeodesicLock(directory);
            PruneGeodesicFiles(directory, path);
            long length;
            using (var file = new SnapshotWriteStream(temporary))
            {
                using var buffer = new BufferedStream(file, 16384);
                using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
                file.Position = GeodesicHeaderLength;
                encode(writer);
                writer.Flush();
                length = file.Length - GeodesicHeaderLength;
                if (length <= 0 || length > MaximumGeodesicPayloadBytes)
                    throw new InvalidDataException("Snapshot exceeds the size limit.");
                file.Position = GeodesicHeaderLength;
                using var hash = SHA256.Create();
                var digest = hash.ComputeHash(file);
                file.Position = 0;
                writer.Write(GeodesicMagic);
                writer.Write(GeodesicFileVersion);
                writer.Write(stage);
                writer.Write(schema);
                writer.Write(vertices);
                writer.Write(triangles);
                writer.Write(key);
                writer.Write(length);
                writer.Write(digest);
                writer.Flush();
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            Debug.Log(FormattableString.Invariant($"[GeodesicPlanetCache] saved path={path} writeMs={watch.Elapsed.TotalMilliseconds:F2} bytes={length + GeodesicHeaderLength} writeManagedHeapDeltaBytes={GC.GetTotalMemory(false) - managedBefore}"));
        }
        catch (Exception ex) when (IsGeodesicCacheFailure(ex))
        {
            Debug.LogWarning($"[GeodesicPlanetCache] write skipped path={path}: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (IsGeodesicCacheFailure(ex))
            { Debug.LogWarning($"[GeodesicPlanetCache] temporary file retained: {ex.Message}"); }
        }
    }

    public static void ClearGeodesicCacheFiles()
        => ClearGeodesicCacheFiles(GeodesicDirectory);

    internal static void ClearGeodesicCacheFiles(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            using var cacheLock = OpenGeodesicLock(directory);
            foreach (var file in CacheFilesByOldestUse(directory)) file.Delete();
            DeleteGeodesicTemporaryFiles(directory);
            Debug.Log($"[GeodesicPlanetCache] cleared {directory}");
        }
        catch (Exception ex) when (IsGeodesicCacheFailure(ex))
        {
            Debug.LogWarning($"[GeodesicPlanetCache] clear failed: {ex.Message}");
        }
    }

    private static FileStream OpenGeodesicLock(string directory) =>
        new FileStream(Path.Combine(directory, "cache.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);

    private static void DeleteGeodesicTemporaryFiles(string directory)
    {
        // Only the lock owner can be writing here.
        foreach (string path in Directory.GetFiles(directory, "*.tmp")) File.Delete(path);
    }

    private static void PruneGeodesicFiles(string directory, string destination)
    {
        DeleteGeodesicTemporaryFiles(directory);
        var files = CacheFilesByOldestUse(directory);
        long bytes = MaximumGeodesicPayloadBytes + GeodesicHeaderLength;
        int count = 1;
        foreach (var file in files)
        {
            bytes += file.Length;
            if (file.FullName != destination) count++;
        }
        foreach (var file in files)
        {
            if (count <= MaximumGeodesicFiles && bytes <= MaximumGeodesicDiskBytes) break;
            if (file.FullName == destination) continue;
            long length = file.Length;
            file.Delete();
            bytes -= length;
            count--;
        }
        if (bytes > MaximumGeodesicDiskBytes)
            throw new InvalidDataException("Insufficient geodesic cache space.");
    }

    private sealed class SnapshotWriteStream : FileStream
    {
        internal SnapshotWriteStream(string path) : base(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None) { }

        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckLimit(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            CheckLimit(buffer.Length);
            base.Write(buffer);
        }

        private void CheckLimit(int count)
        {
            if (Position + count > GeodesicHeaderLength + MaximumGeodesicPayloadBytes)
                throw new InvalidDataException("Snapshot exceeds the size limit.");
        }
    }

    private static bool IsGeodesicCacheFailure(Exception ex) =>
        ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException ||
        ex is ArgumentException || ex is NotSupportedException || ex is CryptographicException;
}
