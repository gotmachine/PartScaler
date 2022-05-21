using UnityEngine;
using System.Linq;
using System;

namespace PartScaler
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    internal class PrefabDryCostWriter : SingletonBehavior<PrefabDryCostWriter>
    {
        private void Start()
        {
            Debug.Log("TweakScale::PrefabDryCostWriter: Start");
            WriteDryCost();
        }

        private void WriteDryCost()
        {
            var partsList = PartLoader.LoadedPartsList;

            foreach (var p in partsList)
            {
                var prefab = p.partPrefab;
                if (prefab == null)
                {
                    Tools.LogWf("partPrefab is null: " + p.name);
                    continue;
                }
                try
                {
                    if (prefab.Modules == null)
                    {
                        Tools.LogWf("partPrefab.Modules is null: " + p.name);
                        continue;
                    }
                    if (!prefab.Modules.Contains("TweakScale"))
                        continue;

                    var m = prefab.Modules["TweakScale"] as PartScaler;
                    m.DryCost = (float)(p.cost - prefab.Resources.Cast<PartResource>().Aggregate(0.0, (a, b) => a + b.maxAmount * b.info.unitCost));
                    if (prefab.Modules.Contains("FSfuelSwitch"))
                        m.ignoreResourcesForCost = true;

                    if (m.DryCost < 0)
                    {
                        if (m.DryCost < -0.5)
                        {
                            Debug.LogError("TweakScale::PrefabDryCostWriter: negative dryCost: part=" + p.name + ", DryCost=" + m.DryCost.ToString());
                        }
                        m.DryCost = 0;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[TweakScale] Exception on writeDryCost: " +e.ToString());
                    Debug.Log("[TweakScale] part="+p.name +" ("+p.title+")");
                }
            }
        }
    }
}
