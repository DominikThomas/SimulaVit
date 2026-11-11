using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>Versioned SHA-256 encoding: BinaryWriter little-endian IEEE floats/integers,
/// length-prefixed arrays, UTF-8 strings, ordinal parameter keys. No GetHashCode(),
/// locale-dependent formatting, object instance IDs, or rounded height strings.</summary>
public sealed class GeodesicGenerationFingerprint
{
    private readonly SortedDictionary<string, string> parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
    public void Add(string key, object value)
    {
        if (value == null) { parameters[key] = "null"; return; }
        var type = value.GetType();
        if (value is float f) parameters[key] = "f32:" + f.ToString("R", CultureInfo.InvariantCulture);
        else if (value is double d) parameters[key] = "f64:" + d.ToString("R", CultureInfo.InvariantCulture);
        else if (type.IsPrimitive || type.IsEnum || value is string) parameters[key] = type.FullName + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
        else if (type.IsValueType) AddFields(key, value);
        else parameters[key] = "excluded-reference:" + type.FullName;
    }
    // All public value fields form a deliberate superset of generation controls. Explicit
    // effective properties are added by the owner; Unity object references are not inputs.
    public void AddFields(string prefix, object value)
    {
        foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => f.Name, StringComparer.Ordinal))
            Add(prefix + "." + field.Name, field.GetValue(value));
    }
    public string Manifest => string.Join(";", parameters.Select(p => p.Key + "=" + p.Value));
    public string ParametersHash => Hash(w => { w.Write("SimulaVit-parameters-v1"); w.Write(parameters.Count); foreach (var p in parameters) { w.Write(p.Key); w.Write(p.Value); } });
    public static string Heights(float[] values) => Hash(w => { w.Write("f32-heights-v1"); w.Write(values?.Length ?? -1); if (values != null) foreach (float v in values) w.Write(v); });
    public static string SurfaceHeights(Vector3[] values) => Hash(w => { w.Write("f32-heights-v1"); w.Write(values.Length); foreach (var v in values) w.Write(v.magnitude); });
    public static string Mask(bool[] values) => Hash(w => { w.Write("bool-mask-v1"); w.Write(values?.Length ?? -1); if (values != null) foreach (bool v in values) w.Write(v); });
    public static string Indices(int[] values) => Hash(w => { w.Write("i32-indices-v1"); w.Write(values.Length); foreach (int v in values) w.Write(v); });
    public static string Positions(Vector3[] values) => Hash(w => { w.Write("f32-xyz-v1"); w.Write(values.Length); foreach (var v in values) { w.Write(v.x); w.Write(v.y); w.Write(v.z); } });
    public static string Runoff(GeodesicTinHydrology tin) => Hash(w => { w.Write("f64-local-runoff-v1"); w.Write(tin.TriangleCount); for (int t = 0; t < tin.TriangleCount; t++) w.Write(tin.ParcelRunoff(t)); });
    private static string Hash(Action<BinaryWriter> write)
    {
        using (var hash = SHA256.Create())
        using (var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write))
        {
            using (var buffer = new BufferedStream(stream, 16384))
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true)) { write(writer); writer.Flush(); buffer.Flush(); stream.FlushFinalBlock(); }
            return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
