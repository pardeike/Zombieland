
using System;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// The destination type for web request events.
    /// </summary>
    public enum EventDestType
    {
        /// <summary>
        /// Unknown destination.
        /// </summary>
        Unknown,

        /// <summary>
        /// Simple GET using Ads TrackingUrl.
        /// </summary>
        AdsTracking,

        /// <summary>
        /// POST to iap-events.
        /// </summary>
        IAP,

        /// <summary>
        /// A custom or standard Unity Analytics event.
        /// </summary>
        Analytics,

        /// <summary>
        /// Common Data Platform: the official internal unity 2018 API for new event types.
        /// </summary>
        CDP,

        /// <summary>
        /// Used for direct connection to Common Data Platform.
        /// </summary>
        CDPDirect,

        /// <summary>
        /// Inter-Process Communication via Ads SendEvent.
        /// </summary>
        AdsIPC
    }
}
