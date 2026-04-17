using Verse;

namespace ModFixesPack
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
        public static bool RealRuins { get; private set; }
        public static bool HSK { get; private set; }
        public static bool MultiLangColorfulTraits { get; private set; }
        public static bool ShowMeYourHands { get; private set; }

        // Add more as we add fixes for other mods
        // public static bool SomeOtherMod { get; private set; }

        public static void DetectLoadedMods()
        {
            DynamicDiplomacy = ModsConfig.IsActive("nilchei.dynamicdiplomacycontinued");
            GeologicalLandforms = ModsConfig.IsActive("m00nl1ght.GeologicalLandforms");
            RealRuins = ModsConfig.IsActive("Woolstrand.RealRuins");
            HSK = ModsConfig.IsActive("skyarkhangel.hsk");
            MultiLangColorfulTraits = ModsConfig.IsActive("multilangcolorfultraits.pirateby");
            ShowMeYourHands = ModsConfig.IsActive("Mlie.ShowMeYourHands");

            LogStatus("Dynamic Diplomacy", DynamicDiplomacy);
            LogStatus("Geological Landforms", GeologicalLandforms);
            LogStatus("Real Ruins", RealRuins);
            LogStatus("HSK (Hardcore SK)", HSK);
            LogStatus("MultiLang Colorful Traits", MultiLangColorfulTraits);
            LogStatus("Show Me Your Hands", ShowMeYourHands);
        }

        private static void LogStatus(string modName, bool loaded)
        {
            Log.Message($"[Mod Fixes Pack] {modName}: {(loaded ? "DETECTED" : "not loaded")}");
        }
    }
}
