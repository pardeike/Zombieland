using System;
using System.Reflection;

using UnityEngine;
using Uniject;

namespace UnityEngine.Purchasing
{
    internal class AdsIPC
    {
        // Qualified Ads class name (minus platform)
        static string adsAdvertisementClassName = "UnityEngine.Advertisements.Purchasing,UnityEngine.Advertisements.";
        static string adsMessageSendName = "SendEvent";
        static Type adsAdvertisementType = null;
        static MethodInfo adsMessageSend = null;

        static internal bool InitAdsIPC(IUtil util)
        {
            // Ads SDK DLLs have unique names per platform
            if(util.platform == RuntimePlatform.IPhonePlayer)
            {
                adsAdvertisementClassName += "iOS";
            }
            else if (util.platform == RuntimePlatform.Android)
            {
                adsAdvertisementClassName += "Android";
            }
            else
            {
                return false;
            }

            if (VerifyMethodExists()) 
            {
                return true;
            } 
            else
            {
                //Fallback to the UnityEngine.Advertisement namespace and see if our SendEvent method exist there.
                adsAdvertisementClassName = "UnityEngine.Advertisements.Purchasing,UnityEngine.Advertisements";
                return VerifyMethodExists();
            }
        }

        static internal bool VerifyMethodExists() {
            try
            {
                // Should be safe for UWP build (even though we never actually get this far on UWP)
                adsAdvertisementType = Type.GetType (adsAdvertisementClassName);
                if(adsAdvertisementType != null)
                {
                    adsMessageSend = adsAdvertisementType.GetMethod (adsMessageSendName);
                    if(adsMessageSend != null)
                    {
                        return true;
                    }
                }
            } 
            catch
            {
                // Console.WriteLine("UnityIAP: Exception while setting up Ads IPC");
            }
            return false;
        }

        static internal bool SendEvent(string json)
        {
            if(adsMessageSend != null)
            {
                adsMessageSend.Invoke (null, new [] { json });
                return true;
            }
            return false;
        }
    }
}
