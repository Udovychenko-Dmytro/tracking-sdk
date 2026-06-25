using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    /// <summary>
    /// Phase 0 smoke test. Its only job is to prove the test harness is wired correctly:
    /// the Tests assembly compiles, references the Runtime assembly, and the Unity Test Runner
    /// discovers and executes EditMode tests. Real behavioural tests arrive in Phase 1+.
    /// </summary>
    public class SmokeTests
    {
        [Test]
        public void Runtime_Assembly_IsReachable_AndVersionIsSet()
        {
            Assert.That(TrackingSdk.VERSION, Is.EqualTo("1.0.0"));
        }
    }
}
