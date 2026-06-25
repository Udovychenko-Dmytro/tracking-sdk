using System.Collections.Generic;
using System.Threading.Tasks;

namespace DmytroUdovychenko.Tracking
{
    /// <summary>
    /// Public tracking API. The two methods are the entire surface a consumer needs; everything
    /// production-grade (queueing, batching, retries, persistence, lifecycle flush) lives behind
    /// this seam in the implementation.
    /// </summary>
    public interface ITracker
    {
        /// <summary>
        /// Records a single message event. Non-blocking: never waits on the network.
        /// </summary>
        /// <param name="message">Non-empty event message.</param>
        /// <returns>
        /// <c>true</c> if the event was accepted into the pipeline (valid input and tracking enabled);
        /// <c>false</c> on invalid input or when tracking is disabled.
        /// </returns>
        bool SendMessage(string message);

        /// <summary>
        /// Records a structured (key/value) event.
        /// </summary>
        /// <param name="map">Non-empty payload. A snapshot is taken; later mutation of the caller's
        /// dictionary does not affect the recorded event.</param>
        /// <returns>
        /// A task whose result reports delivery. In the full pipeline (Phase 2+) it resolves
        /// <c>true</c> when the batch containing this event is actually delivered, or <c>false</c>
        /// after retries are exhausted / on invalid input / when disabled.
        /// </returns>
        Task<bool> SendMapAsync(Dictionary<string, object> map);
    }
}
