using Microsoft.AspNetCore.SignalR.Client;

namespace Elysium.WorkStation.Services
{
    /// <summary>
    /// Centralized retry policies for SignalR HubConnection automatic reconnection.
    /// </summary>
    public sealed class MinuteRetryPolicy : IRetryPolicy
    {
        private readonly TimeSpan _delay;

        public MinuteRetryPolicy() : this(TimeSpan.FromMinutes(1)) { }

        public MinuteRetryPolicy(TimeSpan delay)
        {
            _delay = delay;
        }

        public TimeSpan? NextRetryDelay(RetryContext? retryContext)
            => _delay;
    }
}
