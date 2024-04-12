using UnityEngine.Purchasing.Interfaces;

namespace UnityEngine.Purchasing
{
    class GoogleLastKnownProductService: IGoogleLastKnownProductService
    {
        string m_LastKnownProductId = null;
        int m_LastKnownProrationMode = GooglePlayProrationMode.k_UnknownProrationMode;

        public string GetLastKnownProductId()
        {
            return m_LastKnownProductId;
        }

        public void SetLastKnownProductId(string lastKnownProductId)
        {
            m_LastKnownProductId = lastKnownProductId;
        }

        public int GetLastKnownProrationMode()
        {
            return m_LastKnownProrationMode;
        }

        public void SetLastKnownProrationMode(int lastKnownProrationMode)
        {
            m_LastKnownProrationMode = lastKnownProrationMode;
        }
    }
}
