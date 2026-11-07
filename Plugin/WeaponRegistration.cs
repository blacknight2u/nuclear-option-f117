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
            AryxWeaponIntegration.Register();
            ApplyFuelDefaults();
        }

        private static void ApplyFuelDefaults()
        {
            if (Encyclopedia.Lookup == null ||
                !Encyclopedia.Lookup.TryGetValue(Plugin.AircraftKey, out UnitDefinition unit) ||
                !(unit is AircraftDefinition definition) || definition.aircraftParameters == null)
                return;

            // Apply after the optional presets are registered, including when their
            // pack is absent. Change defaults only; never clamp a pilot's selected
            // fuel or alter the fuel in a spawned aircraft.
            AircraftParameters parameters = definition.aircraftParameters;
            parameters.DefaultFuelLevel = Plugin.DefaultFuelRatio;
            if (parameters.StandardLoadouts != null)
                foreach (var preset in parameters.StandardLoadouts)
                    if (preset != null)
                        preset.FuelRatio = Plugin.DefaultFuelRatio;
            Plugin.Log.LogInfo("F-117 default and preset fuel set to 25%; manual fuel selection remains available.");
        }
    }
}
