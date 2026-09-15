// Verifies temporary factory database overrides do not leak between Play sessions.
using System.Reflection;
using NotAI;
using NUnit.Framework;

public sealed class NAIStateManagerRuntimePathTests
{
    [Test]
    public void SubsystemRegistrationClearsConfiguredDatabasePath()
    {
        var configuredPath = typeof(NAIStateManager).GetField(
            "configuredDatabasePath",
            BindingFlags.NonPublic | BindingFlags.Static);
        var resetMethod = typeof(NAIStateManager).GetMethod(
            "ResetConfiguredDatabasePath",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(configuredPath, Is.Not.Null);
        Assert.That(resetMethod, Is.Not.Null);

        var previousPath = configuredPath!.GetValue(null);
        try
        {
            configuredPath.SetValue(null, "Temp/factory-test-world-stale.db");
            resetMethod!.Invoke(null, null);

            Assert.That(configuredPath.GetValue(null), Is.EqualTo(string.Empty));
        }
        finally
        {
            configuredPath.SetValue(null, previousPath);
        }
    }
}
