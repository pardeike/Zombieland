using System;

namespace UnityEngine.Purchasing
{
    class GooglePlayConfigurationInternal: IGooglePlayConfigurationInternal
    {
        GooglePlayConfiguration m_GooglePlayConfiguration;

        public void SetGooglePlayConfiguration(GooglePlayConfiguration googlePlayConfiguration)
        {
            m_GooglePlayConfiguration = googlePlayConfiguration;
        }

        public void NotifyInitializationConnectionFailed()
        {
            m_GooglePlayConfiguration?.NotifyInitializationConnectionFailed();
        }
    }
}
