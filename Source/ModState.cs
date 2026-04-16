using Verse;

namespace CarbineActionModFixes
{
    /// <summary>
    /// Centralized mod detection. Each fix file checks these flags
    /// to determine whether its patches should activate.
    /// </summary>
    public static class ModState
    {
        // Mods we have fixes for
        public static bool DynamicDiplomacy { get; private set; }
        public static bool GeologicalLandforms { get; private set; }

        // Add more as we add fixes for other mods
        // public static bool SomeOtherMod { get; private set; }

        public static void DetectLoadedMods()
        {
            DynamicDiplomacy = ModsConfig.IsActive("nilchei.dynamicdiplomacycontinued");
            GeologicalLandforms = ModsConfig.IsActive("m00nl1ght.GeologicalLandforms");

            LogStatus("Dynamic Diplomacy", DynamicDiplomacy);
            LogStatus("Geological Landforms", GeologicalLandforms);
        }

        private static void LogStatus(string modName, bool loaded)
        {
            Log.Message($"[CarbineAction Mod Fixes] {modName}: {(loaded ? "DETECTED" : "not loaded")}");
        }
    }
}
