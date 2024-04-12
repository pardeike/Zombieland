using System;

namespace UnityEngine.Purchasing
{
    internal class FakeManagedStoreConfig : IManagedStoreConfig
    {

        private bool catalogDisabled = false;
        private bool testStore = false;
        private string iapBaseUrl = null;
        private string eventBaseUrl = null;
        private bool? trackingOptedOut = null;


        public FakeManagedStoreConfig()
        {

        }


        public bool disableStoreCatalog
        {
            get
            {
                return catalogDisabled;
            }

            set
            {
                catalogDisabled = value;
            }
        }

        public bool? trackingOptOut
        {
            get
            {
                return trackingOptedOut;
            }

            set
            {
                trackingOptedOut = value;
            }
        }


        // The following should all set the test flag and latch it

        public bool storeTestEnabled
        {
            get
            {
                return testStore;
            }
            set
            {
                // Once you start testing you never go back...
                if (testStore == false)
                {
                    testStore = value;
                }
            }
        }

        public string baseIapUrl
        {
            get
            {
                return iapBaseUrl;
            }
            set
            {
                // This can only be set once prior to init (with current code)
                if ((iapBaseUrl == null)&&(value != null))
                {
                    storeTestEnabled = true;
                    iapBaseUrl = value;
                }
            }
        }

        public string baseEventUrl
        {
            get
            {
                return eventBaseUrl;
            }
            set
            {
                storeTestEnabled = true;
                eventBaseUrl = value;
            }
        }

    }
}

