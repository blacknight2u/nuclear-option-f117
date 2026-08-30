using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace Blacknight2u.F117Nighthawk
{
    internal static class F117RequisitionIntegration
    {
        private const string EqualizerPluginGuid = "com.equalizer.unified";

        // The F-117 ($120M, rank 4) follows the two stock rank-4 airframes whose
        // production values are closest to it: KR-67 Ifrit ($126M) and EW-25
        // Medusa ($145M). Restricting the sources to stock keys prevents another
        // mod aircraft from creating a production loop.
        private static readonly HashSet<string> ProductionPeerKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Multirole1",
                "EW1"
            };

        private static AircraftDefinition f117Definition;
        private static bool loggedEqualizerDelegation;
        private static bool warnedMissingDefinition;

        internal static void AddInitialSupply(FactionHQ hq)
        {
            if (!CanManageSupply(hq) || IsEqualizerActive())
                return;

            AircraftDefinition f117;
            if (!TryGetF117Definition(out f117) || IsRestricted(hq, f117))
                return;

            List<int> availablePeerCounts = FindProductionPeers()
                .Select(hq.GetUnitSupply)
                .Where(count => count > 0)
                .ToList();

            // Every normal mission receives at least one requisitionable F-117.
            // When the mission already stocks rank-4 peers, match the scarcer
            // available peer rather than inventing a larger allocation.
            int targetCount = availablePeerCounts.Count == 0
                ? 1
                : availablePeerCounts.Min();
            int currentCount = hq.GetUnitSupply(f117);
            if (currentCount >= targetCount)
                return;

            int amount = targetCount - currentCount;
            hq.AddSupplyUnit(f117, amount);
            Plugin.Log.LogInfo("F-117 requisition inventory added " + amount +
                " airframe(s); target supply " + targetCount + ".");
        }

        internal static void MirrorFactoryProduction(Factory factory)
        {
            if (factory == null || IsEqualizerActive())
                return;

            FactionHQ hq = factory.attachedUnit == null
                ? null
                : factory.attachedUnit.NetworkHQ;
            if (!CanManageSupply(hq))
                return;

            AircraftDefinition produced = factory.ProductionUnit as AircraftDefinition;
            if (produced == null || !ProductionPeerKeys.Contains(produced.jsonKey))
                return;

            AircraftDefinition f117;
            if (!TryGetF117Definition(out f117) || IsRestricted(hq, f117))
                return;

            hq.AddSupplyUnit(f117, 1);
            Plugin.Log.LogDebug("F-117 production added one requisition airframe alongside stock " +
                produced.jsonKey + ".");
        }

        private static bool CanManageSupply(FactionHQ hq)
        {
            return hq != null && hq.IsServer;
        }

        private static bool IsEqualizerActive()
        {
            bool active = Chainloader.PluginInfos.ContainsKey(EqualizerPluginGuid);
            if (active && !loggedEqualizerDelegation)
            {
                loggedEqualizerDelegation = true;
                Plugin.Log.LogInfo("Equalizer is active; F-117 requisition integration is delegated " +
                    "to com.equalizer.unified to prevent duplicate inventory and production.");
            }
            return active;
        }

        private static bool TryGetF117Definition(out AircraftDefinition definition)
        {
            if (f117Definition != null)
            {
                definition = f117Definition;
                return true;
            }

            UnitDefinition registered;
            if (Encyclopedia.Lookup.TryGetValue(Plugin.AircraftKey, out registered))
                f117Definition = registered as AircraftDefinition;
            if (f117Definition == null)
            {
                f117Definition = Resources.FindObjectsOfTypeAll<AircraftDefinition>()
                    .FirstOrDefault(candidate => candidate != null &&
                        string.Equals(candidate.jsonKey, Plugin.AircraftKey,
                            StringComparison.Ordinal));
            }

            definition = f117Definition;
            if (definition == null && !warnedMissingDefinition)
            {
                warnedMissingDefinition = true;
                Plugin.Log.LogWarning("F-117 requisition integration could not find its aircraft definition.");
            }
            return definition != null;
        }

        private static IEnumerable<AircraftDefinition> FindProductionPeers()
        {
            return Resources.FindObjectsOfTypeAll<AircraftDefinition>()
                .Where(definition => definition != null &&
                    ProductionPeerKeys.Contains(definition.jsonKey));
        }

        private static bool IsRestricted(FactionHQ hq, AircraftDefinition definition)
        {
            return hq.restrictedAircraft != null && hq.restrictedAircraft.Any(key =>
                string.Equals(key, definition.jsonKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    [HarmonyPatch]
    internal static class F117InitialRequisitionSupplyPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(FactionHQ), "OnMissionLoad");
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(FactionHQ __instance)
        {
            F117RequisitionIntegration.AddInitialSupply(__instance);
        }
    }

    [HarmonyPatch]
    internal static class F117FactoryProductionPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Factory), "ProduceUnit");
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Factory __instance)
        {
            F117RequisitionIntegration.MirrorFactoryProduction(__instance);
        }
    }
}
