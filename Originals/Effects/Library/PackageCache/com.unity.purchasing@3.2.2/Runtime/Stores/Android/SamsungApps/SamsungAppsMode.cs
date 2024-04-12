namespace UnityEngine.Purchasing
{
    /// <summary>
    /// The mode in which transactions on a Samsung Galaxy app will operate.
    /// </summary>
	public enum SamsungAppsMode
	{
        /// <summary>
        /// Standard mode which allows the user to choose the purchase method. Use for all apps shipped to Samsung.
        /// </summary>
		Production,

        /// <summary>
        /// Test mode in which all Samsung purchases will behave as if successful.
        /// </summary>
		AlwaysSucceed,

        /// <summary>
        /// Test mode in which all Samsung purchases will behave as if failed.
        /// </summary>
		AlwaysFail
	}
}
