using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DDGLCompat
{
    [StaticConstructorOnStartup]
    public static class DDGLCompat
    {
        public static bool GeologicalLandformsLoaded { get; private set; }
        public static bool DynamicDiplomacyLoaded { get; private set; }

        static DDGLCompat()
        {
            // Check if both mods are loaded
            DynamicDiplomacyLoaded = ModsConfig.IsActive("nilchei.dynamicdiplomacycontinued");
            GeologicalLandformsLoaded = ModsConfig.IsActive("m00nl1ght.GeologicalLandforms");

            if (!DynamicDiplomacyLoaded)
            {
                Log.Message("[DD-GL Compat] Dynamic Diplomacy not loaded, patch inactive.");
                return;
            }

            if (!GeologicalLandformsLoaded)
            {
                Log.Message("[DD-GL Compat] Geological Landforms not loaded, patch inactive (vanilla hilliness check only).");
            }

            try
            {
                var harmony = new Harmony("CarbineAction.DDGLCompat");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[DD-GL Compat] Harmony patches applied successfully.");
            }
            catch (Exception e)
            {
                Log.Error("[DD-GL Compat] Failed to apply Harmony patches: " + e);
            }
        }

        /// <summary>
        /// Checks if a tile is unsuitable for NPC battle simulation due to pathing issues.
        /// Returns true if the tile is problematic (impassable/extreme cliffs/caves).
        /// </summary>
        public static bool IsTileProblematic(int tileId)
        {
            if (tileId < 0 || Find.World == null) return true;

            // Check vanilla hilliness first (works without GL)
            var tile = Find.World.grid[tileId];
            if (tile == null) return true;

            // Impassable = mountain map, battles can't happen
            if (tile.hilliness == Hilliness.Impassable) return true;

            // If Geological Landforms is loaded, check for problematic topology
            if (GeologicalLandformsLoaded)
            {
                return IsGLTileProblematic(tileId);
            }

            return false;
        }

        /// <summary>
        /// Separated into its own method so JIT doesn't try to load GL types if GL isn't present.
        /// </summary>
        private static bool IsGLTileProblematic(int tileId)
        {
            try
            {
                var tileInfo = GeologicalLandforms.WorldTileInfo.Get(tileId);
                if (tileInfo == null) return false;

                var topology = tileInfo.Topology;

                // Reject topology types that cause severe NPC pathing issues
                switch (topology)
                {
                    case GeologicalLandforms.Topology.CliffAllSides:
                    case GeologicalLandforms.Topology.CliffValley:
                    case GeologicalLandforms.Topology.CliffThreeSides:
                    case GeologicalLandforms.Topology.CaveEntrance:
                    case GeologicalLandforms.Topology.CaveTunnel:
                    case GeologicalLandforms.Topology.Ocean:
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception e)
            {
                Log.Warning("[DD-GL Compat] Error checking GL tile: " + e.Message);
                return false; // Don't reject tile on error
            }
        }
    }

    /// <summary>
    /// Postfix DD's FindSuitableTile to reject tiles with problematic landforms.
    /// If the result is problematic, return -1 to signal DD that no suitable tile was found.
    /// DD handles -1 by dropping the event gracefully.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_FindSuitableTile
    {
        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("DynamicDiplomacy.UtilsTileCellFinder");
            if (type == null)
            {
                Log.Warning("[DD-GL Compat] Could not find DynamicDiplomacy.UtilsTileCellFinder type");
                return null;
            }
            return AccessTools.Method(type, "FindSuitableTile");
        }

        static bool Prepare()
        {
            return DDGLCompat.DynamicDiplomacyLoaded;
        }

        static void Postfix(ref int __result, int nearTile)
        {
            if (__result < 0) return; // DD already failed to find a tile, leave it

            if (DDGLCompat.IsTileProblematic(__result))
            {
                Log.Message($"[DD-GL Compat] Rejecting tile {__result} (unsuitable terrain), retrying...");

                // Try to find a replacement tile via DD's fallback method
                var type = AccessTools.TypeByName("DynamicDiplomacy.UtilsTileCellFinder");
                var fallbackMethod = AccessTools.Method(type, "FindSuitableTileFixedModerateTempFirst");

                if (fallbackMethod != null)
                {
                    // Try up to 5 times with increasing distance to find a good tile
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        int minDist = 4 + attempt * 4;
                        int maxDist = 10 + attempt * 8;
                        var args = new object[] { nearTile, minDist, maxDist, true };
                        int newTile = (int)fallbackMethod.Invoke(null, args);

                        if (newTile > 0 && !DDGLCompat.IsTileProblematic(newTile))
                        {
                            Log.Message($"[DD-GL Compat] Found replacement tile {newTile} (attempt {attempt + 1})");
                            __result = newTile;
                            return;
                        }
                    }
                }

                // All attempts failed, return -1 so DD skips the event
                Log.Message("[DD-GL Compat] No suitable tile found after retries, event will be dropped");
                __result = -1;
            }
        }
    }
}
