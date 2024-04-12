namespace UnityEngine.Purchasing
{
    internal class GooglePlayProrationMode
    {
        internal static readonly int k_NullProrationMode = -1;

        // The integer value of BillingFlowParams.ProrationMode.UNKNOWN_SUBSCRIPTION_UPGRADE_DOWNGRADE_POLICY
        // See: https://developer.android.com/reference/com/android/billingclient/api/BillingFlowParams.ProrationMode#UNKNOWN_SUBSCRIPTION_UPGRADE_DOWNGRADE_POLICY
        internal static readonly int k_UnknownProrationMode = 0;

        // The integer value of BillingFlowParams.ProrationMode.IMMEDIATE_WITHOUT_PRORATION
        // See: https://developer.android.com/reference/com/android/billingclient/api/BillingFlowParams.ProrationMode#IMMEDIATE_WITHOUT_PRORATION
        internal static readonly int k_ImmediateWithoutProration = 3;

        // The integer value of BillingFlowParams.ProrationMode.DEFERRED
        // See: https://developer.android.com/reference/com/android/billingclient/api/BillingFlowParams.ProrationMode#DEFERRED
        internal static readonly int k_DeferredProrationMode = 4;
    }
}
