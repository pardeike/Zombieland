using System;

namespace UnityEngine.Purchasing 
{
	internal interface ISamsungAppsCallback 
	{
		void OnTransactionsRestored(bool result);
	}
}

