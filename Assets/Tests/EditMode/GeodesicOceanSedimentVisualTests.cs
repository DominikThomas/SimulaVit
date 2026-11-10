using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public sealed class GeodesicOceanSedimentVisualTests
{
    [Test]
    public void DefaultRefreshIntervalIsOneRealSecond()
    {
        GameObject owner = new GameObject("sediment visual interval test");
        try
        {
            GeodesicOceanSedimentVisual visual = owner.AddComponent<GeodesicOceanSedimentVisual>();
            Assert.That(visual.RefreshIntervalSeconds, Is.EqualTo(1f));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void FullFeSAndRustBothContributeToMixedTint()
    {
        Vector4 result = GeodesicOceanSedimentVisual.ComposeTint(new Vector3(0f, 1f, 1f), Color.yellow, Color.red, Color.black);
        Assert.That(result, Is.EqualTo(new Vector4(1f, 0.5f, 0f, 0f)));
    }

    [Test]
    public void EmptyInventoryProducesTransparentTint()
    {
        Assert.That(GeodesicOceanSedimentVisual.ComposeTint(Vector3.zero, Color.yellow, Color.red, Color.black), Is.EqualTo(Vector4.zero));
    }

    [Test]
    public void SedimentKindsProduceDistinctColours()
    {
        Vector4 sulfur = GeodesicOceanSedimentVisual.ComposeTint(Vector3.right, Color.yellow, Color.red, Color.black);
        Vector4 rust = GeodesicOceanSedimentVisual.ComposeTint(Vector3.up, Color.yellow, Color.red, Color.black);
        Vector4 sulphide = GeodesicOceanSedimentVisual.ComposeTint(Vector3.forward, Color.yellow, Color.red, Color.black);

        Assert.That(sulfur, Is.Not.EqualTo(rust));
        Assert.That(rust, Is.Not.EqualTo(sulphide));
        Assert.That(sulphide, Is.Not.EqualTo(sulfur));
    }

    [Test]
    public void VisualRevisionChangesOnlyForNonZeroDepositsAndResetsOnCleanup()
    {
        GameObject owner = new GameObject("sediment revision test");
        try
        {
            GeodesicOceanSedimentField field = owner.AddComponent<GeodesicOceanSedimentField>();
            field.Initialize(1);
            Assert.That(field.VisualRevision, Is.Zero);

            Deposit(field, 0d, 0d, 0d);
            Assert.That(field.VisualRevision, Is.Zero);

            Deposit(field, 1d, 0d, 0d);
            Assert.That(field.VisualRevision, Is.EqualTo(1UL));
            Assert.That(field.GetElementalSulfurInventory(0), Is.EqualTo(1d));

            Deposit(field, 0d, 2d, 3d);
            Assert.That(field.VisualRevision, Is.EqualTo(2UL));
            Assert.That(field.GetOxidizedIronInventory(0), Is.EqualTo(2d));
            Assert.That(field.GetIronSulphideInventory(0), Is.EqualTo(3d));

            field.Clear();
            Assert.That(field.VisualRevision, Is.Zero);
            Assert.That(field.IsInitialized, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void UnchangedRevisionSkipsRefreshAndMultipleDepositsCoalesce()
    {
        GameObject owner = new GameObject("sediment visual revision test");
        Mesh mesh = new Mesh();
        try
        {
            mesh.vertices = new[] { Vector3.up };
            mesh.colors = new[] { Color.blue };
            GeodesicOceanSedimentField field = owner.AddComponent<GeodesicOceanSedimentField>();
            field.Initialize(1);
            GeodesicOceanSedimentVisual visual = owner.AddComponent<GeodesicOceanSedimentVisual>();
            MeshFilter terrainFilter = owner.AddComponent<MeshFilter>();
            terrainFilter.sharedMesh = mesh;

            SetPrivate(visual, "sediments", field);
            SetPrivate(visual, "mesh", mesh);
            SetPrivate(visual, "terrainFilter", terrainFilter);
            SetPrivate(visual, "oceanMask", new[] { true });
            SetPrivate(visual, "tintStream", new List<Vector4> { Vector4.zero });
            InvokePrivate(visual, "Refresh");
            Assert.That(visual.FullVisualRefreshCount, Is.EqualTo(1UL));

            InvokePrivate(visual, "Update");
            Assert.That(visual.FullVisualRefreshCount, Is.EqualTo(1UL), "an unchanged revision must do no mesh rebuild");

            Deposit(field, 1d, 0d, 0d);
            Deposit(field, 0d, 1d, 0d);
            SetPrivate(visual, "nextRefresh", -1f);
            InvokePrivate(visual, "Update");
            Assert.That(visual.FullVisualRefreshCount, Is.EqualTo(2UL));
            Assert.That(visual.LastAppliedRevision, Is.EqualTo(field.VisualRevision));

            InvokePrivate(visual, "Update");
            Assert.That(visual.FullVisualRefreshCount, Is.EqualTo(2UL), "coalesced revisions require only one later refresh");

            visual.ClearVisual();
            Assert.That(visual.FullVisualRefreshCount, Is.Zero);
            Assert.That(visual.LastAppliedRevision, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(owner);
        }
    }

    private static void Deposit(GeodesicOceanSedimentField field, double sulfur, double iron, double sulphide)
    {
        MethodInfo method = typeof(GeodesicOceanSedimentField).GetMethod("DepositSameColumn", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(field, new object[] { 0, sulfur, iron, sulphide });
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(target, null);
    }

    private static void SetPrivate(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }
}
