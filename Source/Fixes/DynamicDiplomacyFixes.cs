using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CarbineActionModFixes.Fixes
{
    /// <summary>
    /// Fix 1: Dynamic Diplomacy tile selection.
    /// Rejects tiles with problematic terrain (extreme cliffs, caves, ocean, impassable).
    /// Retries up to 5 times with expanding distance, then gives up if nothing found.
    /// </summary>
    [HarmonyPatch]
    public static class DD_FindSuitableTile
    {
        static bool Prepare() => ModState.DynamicDiplomacy;

        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("DynamicDiplomacy.UtilsTileCellFinder");
            if (type == null)
            {
                Log.Warning("[CarbineAction Mod Fixes] Could not find DD UtilsTileCellFinder type");
                return null;
            }
            return AccessTools.Method(type, "FindSuitableTile");
        }

        static void Postfix(ref int __result, int nearTile)
        {
            if (__result < 0) return;

            if (TerrainUtils.IsTileProblematic(__result))
            {
                Log.Message($"[CarbineAction Mod Fixes] DD: Rejecting tile {__result} (unsuitable terrain), retrying...");

                var type = AccessTools.TypeByName("DynamicDiplomacy.UtilsTileCellFinder");
                var fallbackMethod = AccessTools.Method(type, "FindSuitableTileFixedModerateTempFirst");

                if (fallbackMethod != null)
                {
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        int minDist = 4 + attempt * 4;
                        int maxDist = 10 + attempt * 8;
                        var args = new object[] { nearTile, minDist, maxDist, true };
                        int newTile = (int)fallbackMethod.Invoke(null, args);

                        if (newTile > 0 && !TerrainUtils.IsTileProblematic(newTile))
                        {
                            Log.Message($"[CarbineAction Mod Fixes] DD: Found replacement tile {newTile} (attempt {attempt + 1})");
                            __result = newTile;
                            return;
                        }
                    }
                }

                Log.Message("[CarbineAction Mod Fixes] DD: No suitable tile found after retries, event will be dropped");
                __result = -1;
            }
        }
    }

    /// <summary>
    /// Fix 2: Dynamic Diplomacy settlement selection.
    /// Rejects settlements on problematic terrain so conquest events don't target them.
    /// </summary>
    [HarmonyPatch]
    public static class DD_RandomSettlement
    {
        static bool Prepare() => ModState.DynamicDiplomacy;

        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("DynamicDiplomacy.IncidentWorker_NPCConquest");
            if (type == null) return null;
            return AccessTools.Method(type, "RandomSettlement");
        }

        static void Postfix(ref Settlement __result)
        {
            if (__result == null) return;

            if (TerrainUtils.IsSettlementProblematic(__result))
            {
                Log.Message($"[CarbineAction Mod Fixes] DD: Rejecting settlement {__result.Name} (unsuitable tile), retrying...");
                __result = FindValidSettlement();
            }
        }

        static Settlement FindValidSettlement()
        {
            var validSettlements = Find.WorldObjects.SettlementBases
                .Where(s => s.Faction != null
                    && !s.Faction.IsPlayer
                    && s.Faction.def.settlementGenerationWeight > 0f
                    && !s.def.defName.Equals("City_Faction")
                    && !s.def.defName.Equals("City_Abandoned")
                    && !s.def.defName.Equals("City_Ghost")
                    && !s.def.defName.Equals("City_Citadel")
                    && !TerrainUtils.IsSettlementProblematic(s))
                .ToList();

            return validSettlements.RandomElementWithFallback();
        }
    }

    /// <summary>
    /// Fix 3: Dynamic Diplomacy battle map — shambler deadlock + hard timeout.
    ///
    /// Bug report: When a faction gets infected (shamblers, Necroa virus) and pawns
    /// switch factions mid-battle, DD's win condition never triggers because the
    /// shambler pawns still "exist" and aren't dead/downed. Battle map stays open forever.
    ///
    /// Fix: Before each Tick, remove pawns from lhs/rhs if they left their original faction.
    /// Also add a 7-day hard timeout failsafe in case something else goes wrong.
    /// </summary>
    [HarmonyPatch]
    public static class DD_ArenaFactionCheck
    {
        static bool Prepare() => ModState.DynamicDiplomacy;

        static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("DynamicDiplomacy.MapParentNPCArena");
            if (type == null) return null;
            return AccessTools.Method(type, "Tick");
        }

        static void Prefix(object __instance)
        {
            try
            {
                var type = __instance.GetType();
                var lhsField = type.GetField("lhs");
                var rhsField = type.GetField("rhs");
                var attackerField = type.GetField("attackerFaction");
                var defenderField = type.GetField("defenderFaction");
                var tickCreatedField = type.GetField("tickCreated");
                var isCombatEndedField = type.GetField("isCombatEnded");

                if (lhsField == null || rhsField == null || attackerField == null || defenderField == null) return;

                var lhs = lhsField.GetValue(__instance) as List<Pawn>;
                var rhs = rhsField.GetValue(__instance) as List<Pawn>;
                var attacker = attackerField.GetValue(__instance) as Faction;
                var defender = defenderField.GetValue(__instance) as Faction;

                if (lhs == null || rhs == null) return;

                // Hard timeout: 7 game days (420000 ticks) — failsafe past DD's normal 120000 tick timeout
                if (tickCreatedField != null && isCombatEndedField != null)
                {
                    int tickCreated = (int)tickCreatedField.GetValue(__instance);
                    int ticksElapsed = Find.TickManager.TicksGame - tickCreated;
                    if (ticksElapsed > 420000)
                    {
                        bool isCombatEnded = (bool)isCombatEndedField.GetValue(__instance);
                        if (!isCombatEnded)
                        {
                            Log.Warning("[CarbineAction Mod Fixes] DD: Battle hard timeout after 7 days — forcing end");
                            isCombatEndedField.SetValue(__instance, true);
                        }
                    }
                }

                // Shambler fix: remove pawns that switched factions (or were destroyed)
                if (attacker != null)
                    lhs.RemoveAll(p => p == null || p.Destroyed || (p.Faction != null && p.Faction != attacker));
                if (defender != null)
                    rhs.RemoveAll(p => p == null || p.Destroyed || (p.Faction != null && p.Faction != defender));
            }
            catch (Exception e)
            {
                Log.Warning("[CarbineAction Mod Fixes] Error in arena faction check: " + e.Message);
            }
        }
    }
}
