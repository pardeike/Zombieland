using System;

namespace UnityEngine.Purchasing
{
    interface IGooglePlayConfigurationInternal
    {
        void SetGooglePlayConfiguration(GooglePlayConfiguration googlePlayConfiguration);

        void NotifyInitializationConnectionFailed();
    }
}
