using System;
using NUnit.Framework;

namespace DmytroUdovychenko.Tracking.Tests
{
    public class RetryPolicyTests
    {
        [Test]
        public void TryGetDelay_ReturnsFalse_OnTheFinalAttempt()
        {
            RetryPolicy policy = new RetryPolicy(maxAttempts: 3, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));

            Assert.IsTrue(policy.TryGetDelay(1, out _), "retry after attempt 1");
            Assert.IsTrue(policy.TryGetDelay(2, out _), "retry after attempt 2");
            Assert.IsFalse(policy.TryGetDelay(3, out _), "no retry after the final attempt");
        }

        [Test]
        public void Delay_GrowsExponentially_WithinEqualJitterBounds()
        {
            // Default (random) jitter => assert the delay falls in [base/2, base].
            RetryPolicy policy = new RetryPolicy(maxAttempts: 10, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(100));

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                Assert.IsTrue(policy.TryGetDelay(attempt, out TimeSpan delay));
                double baseMs = 100 * Math.Pow(2, attempt - 1);
                Assert.GreaterOrEqual(delay.TotalMilliseconds, baseMs * 0.5 - 0.001, "lower bound (base/2)");
                Assert.LessOrEqual(delay.TotalMilliseconds, baseMs + 0.001, "upper bound (base)");
            }
        }

        [Test]
        public void Delay_IsCapped_AtMaxDelay()
        {
            // Jitter pinned to 1.0 => delay == the (capped) base.
            RetryPolicy policy = new RetryPolicy(
                maxAttempts: 20, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(300), jitter: () => 1.0);

            policy.TryGetDelay(1, out TimeSpan d1); // base 100
            policy.TryGetDelay(2, out TimeSpan d2); // base 200
            policy.TryGetDelay(3, out TimeSpan d3); // base 400 -> capped 300
            policy.TryGetDelay(8, out TimeSpan d8); // base huge -> capped 300

            Assert.AreEqual(100, d1.TotalMilliseconds, 0.001);
            Assert.AreEqual(200, d2.TotalMilliseconds, 0.001);
            Assert.AreEqual(300, d3.TotalMilliseconds, 0.001);
            Assert.AreEqual(300, d8.TotalMilliseconds, 0.001);
        }

        [Test]
        public void Delay_WithZeroJitter_IsHalfTheBase()
        {
            RetryPolicy policy = new RetryPolicy(
                maxAttempts: 10, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(100), jitter: () => 0.0);

            policy.TryGetDelay(1, out TimeSpan d1);
            policy.TryGetDelay(2, out TimeSpan d2);
            policy.TryGetDelay(3, out TimeSpan d3);

            Assert.AreEqual(50, d1.TotalMilliseconds, 0.001);
            Assert.AreEqual(100, d2.TotalMilliseconds, 0.001);
            Assert.AreEqual(200, d3.TotalMilliseconds, 0.001);
        }
    }
}
