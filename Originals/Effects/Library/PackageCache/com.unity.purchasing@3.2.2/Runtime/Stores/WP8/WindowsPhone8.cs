using System;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Class containing store information for Windows 8 builds.
    /// </summary>
	public class WindowsPhone8
	{
        /// <summary>
        /// The name of the store used for Windows Phone 8 builds.
        /// </summary>
		[Obsolete("Use WindowsStore.Name for Universal Windows Builds")]
		public const string Name = WindowsStore.Name;
	}
}
