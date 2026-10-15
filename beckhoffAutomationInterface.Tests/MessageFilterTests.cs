using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>A shell that stays busy must not hang the tool for ever: a rejected call is
    /// retried only up to the limit (seen 2026-10-15: activate stuck in Clean for 20+ minutes).</summary>
    public class MessageFilterTests
    {
        const int RetryLater = 2, Rejected = 1, Limit = 600000;

        [Fact]
        public void RetryLater_WithinTheLimit_Retries()
        {
            Assert.Equal(99, MessageFilter.RetryDelay(RetryLater, 0, Limit));
            Assert.Equal(99, MessageFilter.RetryDelay(RetryLater, Limit - 1, Limit));
        }

        [Fact]
        public void RetryLater_AtTheLimit_Cancels()
        {
            Assert.Equal(-1, MessageFilter.RetryDelay(RetryLater, Limit, Limit));
            Assert.Equal(-1, MessageFilter.RetryDelay(RetryLater, int.MaxValue, Limit));
        }

        [Fact]
        public void OtherRejectType_CancelsAtOnce()
        {
            Assert.Equal(-1, MessageFilter.RetryDelay(Rejected, 0, Limit));
        }

        [Fact]
        public void TheLimitIsTenMinutes()
        {
            Assert.Equal(10 * 60 * 1000, MessageFilter.RetryLimitMs);
        }
    }
}
