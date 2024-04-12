using System;
using UnityEngine.Purchasing;

namespace UnityEngine.Purchasing 
{
	// Bridge from Java to C# for Samsung specific functionality.
	internal class SamsungAppsJavaBridge : AndroidJavaProxy, ISamsungAppsCallback 
	{
		private ISamsungAppsCallback forwardTo;

		public SamsungAppsJavaBridge(ISamsungAppsCallback forwardTo) : base("com.unity.purchasing.samsung.ISamsungAppsCallback")
		{
			this.forwardTo = forwardTo;
		}

		public void OnTransactionsRestored(bool result)
		{
			forwardTo.OnTransactionsRestored(result);
		}
	}
}
