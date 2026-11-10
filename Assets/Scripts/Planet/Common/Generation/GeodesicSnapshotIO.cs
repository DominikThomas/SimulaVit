using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Canonical encoding for generation data. Validate counts before allocating runtime arrays.</summary>
internal static class GeodesicSnapshotIO
{
    internal static void RuntimeKey(BinaryWriter w)
    {
        w.Write(Application.unityVersion);
        w.Write((int)Application.platform);
        w.Write((int)RuntimeInformation.ProcessArchitecture);
#if ENABLE_IL2CPP
        w.Write(true);
#else
        w.Write(false);
#endif
    }

    internal static void Write<T>(BinaryWriter w, IReadOnlyCollection<T> values, Action<BinaryWriter, T> item)
    {
        w.Write(values.Count);
        foreach (var value in values) item(w, value);
    }

    internal static T[] Read<T>(BinaryReader r, int limit, int width, Func<BinaryReader, T> item, bool exact = false)
    {
        int count = Count(r, limit, width);
        Require(!exact || count == limit, "Snapshot array length does not match topology.");
        var result = new T[count];
        for (int i = 0; i < count; i++) result[i] = item(r);
        return result;
    }

    internal static int Count(BinaryReader r, int limit, int width)
    {
        int count = r.ReadInt32();
        Require(count >= 0 && count <= limit && (long)count * width <= r.BaseStream.Length - r.BaseStream.Position,
            "Invalid snapshot count.");
        return count;
    }

    internal static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidDataException(message);
    }

    internal static int Id(BinaryReader r, int limit, int minimum = 0)
    {
        int value = r.ReadInt32();
        Require(value >= minimum && value < limit, "Snapshot index is out of range.");
        return value;
    }

    internal static bool Flag(BinaryReader r)
    {
        byte value = r.ReadByte();
        Require(value <= 1, "Invalid snapshot flag.");
        return value == 1;
    }

    internal static byte IdByte(BinaryReader r, int limit)
    {
        byte value = r.ReadByte();
        Require(value < limit, "Invalid snapshot enum value.");
        return value;
    }

    internal static float Float(BinaryReader r)
    {
        float value = r.ReadSingle();
        Require(float.IsFinite(value), "Nonfinite snapshot float.");
        return value;
    }

    internal static double Double(BinaryReader r)
    {
        double value = r.ReadDouble();
        Require(double.IsFinite(value), "Nonfinite snapshot double.");
        return value;
    }

    internal static void Floats(BinaryWriter w, IReadOnlyCollection<float> values) => Write(w, values, (b, v) => b.Write(v));
    internal static float[] Floats(BinaryReader r, int count) => Read(r, count, 4, Float, exact: true);
    internal static void Doubles(BinaryWriter w, IReadOnlyCollection<double> values) => Write(w, values, (b, v) => b.Write(v));
    internal static void Ints(BinaryWriter w, IReadOnlyCollection<int> values) => Write(w, values, (b, v) => b.Write(v));
    internal static void Flags(BinaryWriter w, IReadOnlyCollection<bool> values) => Write(w, values, (b, v) => b.Write(v));
    internal static bool[] Flags(BinaryReader r, int count) => Read(r, count, 1, Flag, exact: true);

    internal static void Vector(BinaryWriter w, Vector3 value) { w.Write(value.x); w.Write(value.y); w.Write(value.z); }
    internal static Vector3 Vector(BinaryReader r) => new Vector3(Float(r), Float(r), Float(r));
    internal static void Point(BinaryWriter w, TinPoint point)
    { w.Write(point.Triangle); w.Write(point.A); w.Write(point.B); w.Write(point.C); }
    internal static TinPoint Point(BinaryReader r, int count, int minimum = 0) =>
        new TinPoint(Id(r, count, minimum), Double(r), Double(r), Double(r));
    internal static void OptionalPoint(BinaryWriter w, TinPoint? point)
    { w.Write(point.HasValue); if (point.HasValue) Point(w, point.Value); }
    internal static TinPoint? OptionalPoint(BinaryReader r, int count) => Flag(r) ? Point(r, count) : (TinPoint?)null;

    internal static TinTermination Termination(BinaryReader r)
    {
        byte value = r.ReadByte();
        Require(value <= (byte)TinTermination.Confluence, "Invalid TIN termination.");
        return (TinTermination)value;
    }

    internal static void Text(BinaryWriter w, string value)
    {
        w.Write(value != null);
        if (value == null) return;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Require(bytes.Length <= 16384, "Oversized snapshot text.");
        w.Write(bytes.Length); w.Write(bytes);
    }

    internal static string Text(BinaryReader r) => Flag(r)
        ? System.Text.Encoding.UTF8.GetString(r.ReadBytes(Count(r, 16384, 1))) : null;
}
