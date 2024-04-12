using System;
using System.Collections.Generic;

namespace UnityEngine.Purchasing
{
    internal class FakeManagedStoreExtensions : IManagedStoreExtensions
    {
        public Product[] storeCatalog
        {
            get
            {
                return new Product[] { };
            }
        }
        public void RefreshCatalog(Action a)
        {
            a();
        }
    }
}
