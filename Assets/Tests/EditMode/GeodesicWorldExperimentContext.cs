using NUnit.Framework;
using UnityEngine;

internal static class GeodesicWorldExperimentContext
{
    internal static void RequireDisposableBatchProject()
    {
        if (!Application.isBatchMode)
            Assert.Ignore("Full-world experiment. Select it with Tools/ValidateSulfur.ps1 in a disposable batch project.");

        Assert.That(Application.dataPath.Replace('\\', '/'), Does.Contain("/Temp/FoodTaxisAudit/UnityValidation-"),
            "Full-world experiments require a disposable project under Temp/FoodTaxisAudit.");
    }
}
