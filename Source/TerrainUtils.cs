using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CarbineActionModFixes
{
    /// <summary>
    /// Shared utilities for checking if a world tile is suitable for various events.
    /// Used by multiple fix modules.
    /// </summary>
    public static class TerrainUtils
    {
        /// <summary>
        /// Returns true if a tile is unsuitable for NPC battle simulation
        /// due to pathing issues (impassable, ocean, extreme cliffs from GL, etc.)
        /// </summary>
        public static bool IsTileProblematic(int tileId)
        {
            if (tileId < 0 || Find.World == null) return true;

            var tile = Find.World.grid[tileId];
            if (tile == null) return true;

            if (tile.hilliness == Hilliness.Impassable) return true;
            if (tile.WaterCovered) return true;

            if (ModState.GeologicalLandforms)
            {
                return IsGLTileProblematic(tileId);
            }

            return false;
        }

        /// <summary>
        /// Separated so JIT doesn't try to load GL types if GL isn't present.
        /// </summary>
        private static bool IsGLTileProblematic(int tileId)
        {
            try
            {
                var tileInfo = GeologicalLandforms.WorldTileInfo.Get(tileId);
                if (tileInfo == null) return false;

                switch (tileInfo.Topology)
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
                Log.Warning("[CarbineAction Mod Fixes] Error checking GL tile: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Returns true if a settlement is on a tile that can't host a proper battle map.
        /// </summary>
        public static bool IsSettlementProblematic(Settlement settlement)
        {
            if (settlement == null || settlement.Faction == null) return true;
            return IsTileProblematic(settlement.Tile);
        }
    }
}
