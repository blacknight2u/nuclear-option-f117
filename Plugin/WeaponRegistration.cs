using System;
using System.Reflection;
using HarmonyLib;

namespace Blacknight2u.F117Nighthawk
{
    [HarmonyPatch]
    internal static class F117WeaponRegistration
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("Blueprinter.PatchRunner:ApplyAllOps")
                ?? throw new MissingMethodException("Blueprinter.PatchRunner.ApplyAllOps");
        }

        [HarmonyPostfix]
        private static void Postfix(Encyclopedia __0)
        {
            // Blueprinter has finished its sorted registry and reference patches here.
            // Do not defer this to a frame timer: network indices must precede gameplay.
            if (!Plugin.TryRegisterUnlockedStockMounts(__0))
                Plugin.Log.LogError("F-117 stock-disabled weapon registration failed after " +
                    "Blueprinter loading. ARAD-45 loadouts may not serialize safely.");
        }
    }
}
