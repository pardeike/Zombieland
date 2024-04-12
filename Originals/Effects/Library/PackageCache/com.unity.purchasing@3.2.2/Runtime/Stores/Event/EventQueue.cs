using System;
using Uniject;
using UnityEngine;


namespace UnityEngine.Purchasing
{

    internal class EventQueue
    {
        private IAsyncWebUtil m_AsyncUtil;

        private static EventQueue QueueInstance;

        internal ProfileData Profile;

        private string TrackingUrl;
        private string EventUrl;

        internal object ProfileDict;

        private const int kMaxRetryDelayInSeconds = (5 * 60);

        private EventQueue(IUtil util, IAsyncWebUtil webUtil)
        {
            m_AsyncUtil = webUtil;
            Profile = ProfileData.Instance(util);
            ProfileDict = Profile.GetProfileDict();
            AdsIPC.InitAdsIPC(util);
        }

        public static EventQueue Instance(IUtil util, IAsyncWebUtil webUtil)
        {
            if(QueueInstance == null)
            {
                QueueInstance = new EventQueue(util, webUtil);
            }
            return QueueInstance;
        }


        internal void SetAdsUrl(string url)
        {
            TrackingUrl = url;
        }

        internal void SetIapUrl(string url)
        {
            EventUrl = url;
        }


// this could certainly be generalized and improved significantly...
// and yes, as the name implies we should be queueing these and batch sending retries etc,
//
        internal bool SendEvent(EventDestType dest, string json, string url = null, int? delayInSeconds = null)
        {
            if(m_AsyncUtil == null)
            {
                return false;
            }

            string target;
            switch(dest)
            {
                case EventDestType.IAP:
                    target = (url != null) ? url : EventUrl;
                    if((target == null)||(json == null))
                    {
                        break;
                    }
                    m_AsyncUtil.Post(target, json,
                        response =>
                        {
                            // Console.WriteLine("IAP Event OK");
                        },
                        error =>
                        {
                            // Console.WriteLine("IAP Event Failed: " + error);
                            if(delayInSeconds != null)
                            {
                                // This is that weird CloudCatalog retry code, needs improvement
                                delayInSeconds = Math.Max(5, (int)delayInSeconds * 2);
                                delayInSeconds = Math.Min(kMaxRetryDelayInSeconds, (int)delayInSeconds);
                                m_AsyncUtil.Schedule(() => SendEvent(dest, json, target, delayInSeconds), (int)delayInSeconds);
                            }
                        }
                        );
                    return true;

                case EventDestType.AdsTracking:
                    target = (url != null) ? url : TrackingUrl;
                    if(target == null)
                        break;
                    m_AsyncUtil.Get(target,
                        response =>
                        {
                            // Console.WriteLine("AdsTracking Event OK");
                        },
                        error =>
                        {
                            // Console.WriteLine("AdsTracking Event Failed: " + error);
                        }
                        );
                    return true;

                case EventDestType.AdsIPC:
                    return AdsIPC.SendEvent(json);

                default:
                    return false;
            }
            return false;
        }


        // This is signature currently used in JSONStore impl for OnPurchaseSucceeded
        internal bool SendEvent(string json)
        {
            SendEvent(EventDestType.AdsTracking, null);
            SendEvent(EventDestType.IAP, json);

            return false;
        }
    }
}
