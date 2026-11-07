using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace Blacknight2u.F117Nighthawk
{
    // References the separately installed pack's registered mounts. No Aryx assets,
    // definitions or network identities are copied or changed by this integration.
    internal static class AryxWeaponIntegration
    {
        internal const string Pgm = "aryx_glidebomb400_single";
        internal const string Pyre4 = "Aryx_FastAGM1_Internal_x4";
        internal const string Pyre6 = "Aryx_FastAGM1_Internal_x6";
        internal const string Jester = "Aryx_HeavyRocket1_x2";
        internal const string Atlatl = "Aryx_SmallCruiseMissile1_Quad_Internal";
        internal const string Broadsword = "Aryx_LRIRM1_Triple_Internal";
        internal const string Gladius = "Aryx_SRIRM1_Double";
        private static readonly Dictionary<string, int> Candidates = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { Pgm, 1 }, { Pyre4, 4 }, { Pyre6, 6 }, { Jester, 2 },
            { Atlatl, 4 }, { Broadsword, 3 }, { Gladius, 2 }
        };
        private static readonly HashSet<WeaponMount> Supported = new HashSet<WeaponMount>();
        private static readonly Vector3 BaySize = new Vector3(0.78f, 0.82f, 4.2f);
        // Fixed cavity center relative to either authored LOC_Weapon socket.
        private static readonly Vector3 BayCenter = new Vector3(0f, -0.31f, 0.10f);
        private static readonly FieldInfo RailDirection = AccessTools.Field(typeof(MountedMissile), "railDirection");
        private static readonly FieldInfo RailLength = AccessTools.Field(typeof(MountedMissile), "railLength");
        private static readonly FieldInfo RailSpeed = AccessTools.Field(typeof(MountedMissile), "railSpeed");
        private static readonly FieldInfo RailDelay = AccessTools.Field(typeof(MountedMissile), "railDelay");

        internal static void Register()
        {
            if (Encyclopedia.Lookup == null || Encyclopedia.WeaponLookup == null ||
                !Encyclopedia.Lookup.TryGetValue(Plugin.AircraftKey, out UnitDefinition unit) ||
                !(unit is AircraftDefinition definition) || definition.unitPrefab == null)
                return;
            Aircraft aircraft = definition.unitPrefab.GetComponent<Aircraft>();
            HardpointSet[] sets = aircraft?.weaponManager?.hardpointSets;
            AircraftParameters parameters = definition.aircraftParameters;
            if (sets == null || parameters == null)
                return;
            int left = Array.FindIndex(sets, set => set != null && set.name == "Left Weapon Bay");
            int right = Array.FindIndex(sets, set => set != null && set.name == "Right Weapon Bay");
            int jammer = Array.FindIndex(sets, set => set != null && set.name == Plugin.FixedJammerHardpointName);
            if (left < 0 || right < 0 || jammer < 0 ||
                !Plugin.IsF117InternalWeaponSet(sets[left]) || !Plugin.IsF117InternalWeaponSet(sets[right]))
                return;
            WeaponMount fixedJammer = Plugin.FindFixedJammerMount(aircraft);
            if (fixedJammer == null)
                return;

            var available = new Dictionary<string, WeaponMount>(StringComparer.Ordinal);
            foreach (var candidate in Candidates)
            {
                if (!Encyclopedia.WeaponLookup.TryGetValue(candidate.Key, out WeaponMount mount))
                    continue; // Optional pack, including versions with fewer mounts.
                if (!ValidateMount(mount, candidate.Value, out string reason))
                {
                    Plugin.Log.LogWarning("F-117 skipped Aryx mount '" + candidate.Key + "': " + reason);
                    continue;
                }
                available.Add(candidate.Key, mount);
                Supported.Add(mount);
                foreach (int index in new[] { left, right })
                    if (!sets[index].weaponOptions.Contains(mount))
                        sets[index].weaponOptions.Add(mount);
            }

            var presets = new[]
            {
                new[] { "2× PGM-400LR — Stealth Strike", Pgm, Pgm },
                new[] { "8× Pyre — Anti-Armor", Pyre4, Pyre4 },
                new[] { "12× Pyre — Anti-Armor", Pyre6, Pyre6 },
                new[] { "4× Jester — Laser Strike", Jester, Jester },
                new[] { "8× Atlatl — Standoff", Atlatl, Atlatl },
                new[] { "6× Broadsword — Air Defense", Broadsword, Broadsword },
                new[] { "4× Gladius — Air Defense", Gladius, Gladius },
                new[] { "PGM-400LR + 6× Pyre — Mixed Strike", Pgm, Pyre6 },
                new[] { "PGM-400LR + 3× Broadsword — Defensive Strike", Pgm, Broadsword },
                new[] { "PGM-400LR + 2× Gladius — Defensive Strike", Pgm, Gladius }
            };
            var standards = (parameters.StandardLoadouts ?? new StandardLoadout[0]).ToList();
            int added = 0;
            foreach (string[] preset in presets)
            {
                if (!available.TryGetValue(preset[1], out WeaponMount leftMount) ||
                    !available.TryGetValue(preset[2], out WeaponMount rightMount) ||
                    standards.Any(standard => standard != null && standard.Name == preset[0]))
                    continue;
                var loadout = new Loadout { weapons = Enumerable.Repeat<WeaponMount>(null, sets.Length).ToList() };
                loadout.weapons[left] = leftMount;
                loadout.weapons[right] = rightMount;
                loadout.weapons[jammer] = fixedJammer;
                standards.Add(new StandardLoadout { Name = preset[0], FuelRatio = Plugin.DefaultFuelRatio, loadout = loadout });
                if (parameters.loadouts == null)
                    parameters.loadouts = new List<Loadout>();
                parameters.loadouts.Add(loadout);
                added++;
            }
            parameters.StandardLoadouts = standards.ToArray();
            Plugin.Log.LogInfo("F-117 optional Aryx integration: " + available.Count + " mounts, " +
                added + " new presets. The original weapon definitions and network IDs are retained.");
        }

        private static bool ValidateMount(WeaponMount mount, int expectedCount, out string reason)
        {
            reason = "missing weapon, prefab or registered network identity";
            if (mount == null || mount.prefab == null || mount.info == null ||
                mount.info.weaponPrefab == null || !((INetworkDefinition)mount).LookupIndex.HasValue)
                return false;
            MountedMissile[] missiles = mount.prefab.GetComponentsInChildren<MountedMissile>(true);
            reason = "unexpected store count or missing weapon visuals";
            if (missiles.Length != expectedCount || mount.ammo != expectedCount ||
                missiles.Any(missile => missile.info == null ||
                    !TryMeasure(missile.transform, missile.transform, out _)))
                return false;
            reason = "mount exceeds the internal bay envelope (including clearance)";
            if (!TryMeasure(mount.prefab.transform, mount.prefab.transform, out Bounds bounds) ||
                bounds.size.x > BaySize.x - 0.02f || bounds.size.y > BaySize.y - 0.02f ||
                bounds.size.z > BaySize.z - 0.02f)
                return false;
            reason = null;
            return true;
        }

        internal static bool Applies(Aircraft aircraft, Hardpoint hardpoint, WeaponMount mount)
        {
            return Plugin.IsF117(aircraft) && mount != null && Supported.Contains(mount) &&
                hardpoint?.transform != null && hardpoint.bayDoors != null && hardpoint.bayDoors.Length > 0 &&
                (hardpoint.transform.name == "LOC_Weapon_Left" || hardpoint.transform.name == "LOC_Weapon_Right");
        }

        internal static void ConfigureRelease(MountedMissile missile, Aircraft aircraft, Hardpoint hardpoint, WeaponMount mount)
        {
            if (missile == null || !Applies(aircraft, hardpoint, mount))
                return;
            RailDirection.SetValue(missile, MountedMissile.RailDirection.Down);
            RailLength.SetValue(missile, 2f);
            RailSpeed.SetValue(missile, 4f);
            // Authored doors take 0.5 seconds to open. Only the mounted instance is changed.
            RailDelay.SetValue(missile, 0.55f);
        }

        internal static void PositionMount(GameObject instance, Aircraft aircraft, Hardpoint hardpoint, WeaponMount mount)
        {
            if (instance == null || !Applies(aircraft, hardpoint, mount))
                return;
            if (!TryMeasure(hardpoint.transform, instance.transform, out Bounds bounds))
                return;
            // Move the whole rack so rearming retains the native per-store local positions.
            instance.transform.position += hardpoint.transform.TransformVector(BayCenter - bounds.center);
        }

        internal static bool TryMeasure(Transform reference, Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            bool found = false;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                AddBounds(reference, filter.transform, filter.sharedMesh.bounds, ref bounds, ref found);
            }
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.sharedMesh != null)
                    AddBounds(reference, renderer.transform, renderer.localBounds, ref bounds, ref found);
            return found;
        }

        private static void AddBounds(Transform reference, Transform transform, Bounds source, ref Bounds bounds, ref bool found)
        {
            for (int index = 0; index < 8; index++)
            {
                Vector3 corner = source.center + Vector3.Scale(source.extents,
                    new Vector3((index & 1) == 0 ? -1 : 1, (index & 2) == 0 ? -1 : 1, (index & 4) == 0 ? -1 : 1));
                Vector3 point = reference.InverseTransformPoint(transform.TransformPoint(corner));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
    }

    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.SpawnMount))]
    internal static class F117AryxMountPositionPatch
    {
        private static void Postfix(Hardpoint __instance, Aircraft __0, WeaponMount __1, GameObject __result)
        {
            AryxWeaponIntegration.PositionMount(__result, __0, __instance, __1);
        }
    }
}
