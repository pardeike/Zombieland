using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;


namespace UnityEngine.Purchasing
{
    internal class PurchasingEvent
    {
        private Dictionary<string, object> EventDict;

        public PurchasingEvent(Dictionary<string, object> eventDict)
        {
            EventDict = eventDict;
        }

        public string FlatJSON(Dictionary<string, object> profileDict)
        {
            // This is not safe if there are duplicate keys...
            Dictionary<string, object> flatEvent = profileDict.Concat(EventDict).ToDictionary(s => s.Key, s => s.Value);
            string result =  MiniJSON.Json.Serialize(flatEvent);

            return (result != null) ? result : String.Empty;
        }

        // TODO: structured version for CDP, possibly build event dictionary here instead?

    }
}
