using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace CarbineActionModFixes
{
    /// <summary>
    /// Entry point for the patch pack. Applies all Harmony patches on startup.
    /// Individual fixes use Harmony's Prepare() method to conditionally activate
    /// only if their target mod is loaded — no errors or overhead if a mod is missing.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Core
    {
        public const string HarmonyId = "CarbineAction.ModFixes";

        static Core()
        {
            Log.Message("[CarbineAction Mod Fixes] Loading patches...");
            ModState.DetectLoadedMods();

            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[CarbineAction Mod Fixes] Patches applied successfully.");
            }
            catch (Exception e)
            {
                Log.Error("[CarbineAction Mod Fixes] Failed to apply patches: " + e);
            }
        }
    }
}
