using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ModFixesPack.Fixes
{
    /// <summary>
    /// Auto-extends Show Me Your Hands (SMYH) hand-position coverage to weapons that
    /// haven't been manually tuned.
    ///
    /// SMYH has a built-in pixel-analysis fallback that guesses hand positions from a
    /// weapon's sprite silhouette, but it's crude — most modded weapons need manual
    /// tuning to look right. Players like Elldar (HSK Discord) have manually tuned
    /// hundreds of weapons and shipped the results as a mod that loads
    /// ClutterHandsTDef defs. That covers the weapons he tuned, but new weapon mods
    /// (e.g. CE conversions of RH2 factions) still fall through to the crude fallback.
    ///
    /// This fix uses Elldar's (or any other) hand-tuned reference data as a
    /// "training set" and auto-matches uncovered weapons to the closest reference
    /// entry by defName keyword overlap + drawSize proximity + weapon category.
    /// The result is injected as a single extra ClutterHandsTDef at startup, which
    /// SMYH's LoadFromDefs picks up later (it reads DefDatabase at main-menu time,
    /// so our [StaticConstructorOnStartup] runs comfortably earlier).
    ///
    /// Requires SMYH loaded (gated by ModState.ShowMeYourHands). Reflection-based
    /// access to ClutterHandsTDef keeps Mod Fixes Pack from needing a hard reference
    /// to the SMYH assembly — inert if SMYH is absent.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ShowMeYourHandsFixes
    {
        // Weapon-family keyword table. A match here is worth a lot more than generic
        // substring matching — "AK" in both names is meaningful signal, but "Gun" is not.
        // Tokens are lowercased at comparison time.
        //
        // Structure: each tuple is a "family" of equivalent tokens. When the target
        // weapon's defName contains any token in the family, a reference weapon with any
        // other token in the same family is a strong match (+10 per family hit).
        private static readonly string[][] WeaponFamilies = new[]
        {
            // Pistols
            new[] { "glock" },
            new[] { "colt", "1911", "m1911" },
            new[] { "beretta", "m9" },
            new[] { "deserteagle", "deagle" },
            new[] { "revolver" },

            // SMGs
            new[] { "mp5", "mp-5" },
            new[] { "mp7" },
            new[] { "ump", "ump45", "ump-45" },
            new[] { "p90" },
            new[] { "uzi" },
            new[] { "mac10", "mac-10" },
            new[] { "skorpion", "cz61", "cz-61" },
            new[] { "vector", "kriss" },

            // Assault Rifles
            new[] { "ak", "ak47", "ak-47", "ak74", "ak-74", "kalashnikov", "akm" },
            new[] { "m4", "m4a1", "m-4" },
            new[] { "m16", "m16a2", "m16a4", "m-16" },
            new[] { "ar15", "ar-15" },
            new[] { "scar" },
            new[] { "g36" },
            new[] { "fal" },
            new[] { "famas" },
            new[] { "aug" },
            new[] { "stg", "stg44", "stg-44" },
            new[] { "galil" },
            new[] { "tavor" },

            // Battle Rifles
            new[] { "garand", "m1garand" },
            new[] { "m14", "ebr" },

            // Snipers
            new[] { "mosin", "mosinnagant" },
            new[] { "kar98", "k98", "kar-98" },
            new[] { "awp" },
            new[] { "barrett", "m82", "m107" },
            new[] { "remington", "m24", "m40" },
            new[] { "svd", "dragunov" },
            new[] { "leenfield", "lee-enfield", "enfield" },

            // Shotguns
            new[] { "winchester", "m1897" },
            new[] { "mossberg", "m500" },
            new[] { "spas", "spas12", "spas-12" },
            new[] { "aa12", "aa-12" },
            new[] { "benelli", "m4super90", "m1014" },

            // LMGs
            new[] { "lmg" },
            new[] { "m60" },
            new[] { "pkm", "pk" },
            new[] { "minigun", "m134" },
            new[] { "bren" },

            // Melee — blades (one-handed short)
            new[] { "knife", "combatknife", "shiv", "tanto", "dirk", "stiletto" },
            new[] { "dagger" },
            new[] { "bayonet" },

            // Melee — blades (one-handed long)
            new[] { "sword", "shortsword", "longsword", "arming", "gladius" },
            new[] { "katana", "wakizashi", "ninjato" },
            new[] { "saber", "sabre", "cutlass", "scimitar", "falchion" },
            new[] { "rapier", "estoc", "smallsword" },
            new[] { "machete", "panga", "bolo", "kukri", "khukuri" },
            new[] { "cleaver", "butcherknife" },

            // Melee — blades (two-handed)
            new[] { "greatsword", "claymore", "zweihander", "flamberge" },
            new[] { "nodachi", "odachi" },

            // Melee — polearms
            new[] { "spear", "pike", "lance", "javelin" },
            new[] { "halberd", "pollaxe", "poleaxe", "bardiche" },
            new[] { "glaive", "naginata", "guandao" },
            new[] { "trident" },

            // Melee — axes
            new[] { "axe", "battleaxe", "handaxe", "hatchet", "tomahawk", "waraxe" },

            // Melee — blunt
            new[] { "club", "cudgel", "baton", "truncheon", "nightstick", "trenchclub" },
            new[] { "hammer", "warhammer", "sledge", "mallet", "maul" },
            new[] { "mace", "morningstar", "flail" },

            // Melee — exotic
            new[] { "sickle", "kama" },
            new[] { "scythe" },
            new[] { "nunchaku", "nunchucks" },
            new[] { "whip" },
            new[] { "chainsaw" },
            new[] { "fist", "knuckles", "brassknuckles", "punchdagger" },

            // Heavy
            new[] { "rpg" },
            new[] { "grenade" },
            new[] { "launcher" },
            new[] { "flamethrower", "flamer" },

            // Bows
            new[] { "bow", "longbow", "shortbow" },
            new[] { "crossbow" },
        };

        // Defined-by-us tokens that should always be stripped from defNames before
        // keyword comparison, since they are source-mod prefixes that contribute nothing
        // to weapon identity.
        private static readonly HashSet<string> PrefixNoise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gun", "meleeweapon", "weapon", "hsk", "rh2", "rf", "hmc", "pa", "rn",
            "rngun", "rnmelee", "rk", "tfj", "vpe", "norbal", "norbalmelee", "norbalmeleeweapon",
            "mlie", "sk", "cp", "mag",
            // Generic descriptor tokens — too broad, cause cross-family false matches
            // (e.g. "combat" letting CombatKnife match CombatShotgun on shared token alone).
            "combat", "tactical", "military", "modern", "advanced", "heavy", "light",
            "standard", "basic", "default", "custom", "improved", "upgraded", "vintage",
            "old", "new", "the", "of", "and",
        };

        // Minimum match score below which we don't inject — better to let SMYH's crude
        // pixel-analysis fallback run than to copy totally unrelated hand positions.
        //
        // Split thresholds because melee and ranged have different forgivingness:
        //  - Melee hand positions are highly specific (knife grip vs sword pommel vs
        //    spear shaft). A weak match looks visibly wrong. Strict threshold.
        //  - Ranged hand positions are more standardized (pistol grip + foregrip across
        //    most guns). Even a rough family miss on a pistol usually looks OK because
        //    both hands land somewhere near the grip. More forgiving threshold.
        //
        // Score tiers for reference:
        //   3  = category match only (no signal)
        //   6  = category + weak generic token overlap
        //   8  = category + moderate token overlap
        //   10 = family hit OR 2+ token overlaps (strong signal)
        //   18+ = family + category + drawSize — rock-solid
        private const int MinMatchScoreMelee  = 10;
        private const int MinMatchScoreRanged = 6;

        /// <summary>
        /// Reference entry extracted from an existing ClutterHandsTDef.CompTargets entry.
        /// Key data for the similarity matcher.
        /// </summary>
        private struct Reference
        {
            public string DefName;           // the weapon this reference targets
            public Vector3 MainHand;
            public Vector3 SecHand;
            public float MainRotation;
            public float SecRotation;
            public HashSet<string> Tokens;   // extracted from defName + label
            public float DrawSize;           // from weapon.graphicData.drawSize
            public bool IsRanged;            // ranged-vs-melee classification
        }

        static ShowMeYourHandsFixes()
        {
            if (!ModState.ShowMeYourHands) return;

            try
            {
                Run();
            }
            catch (Exception e)
            {
                Log.Warning("[Mod Fixes Pack] ShowMeYourHands auto-match failed: " + e.Message);
            }
        }

        private static void Run()
        {
            // Resolve SMYH types via reflection (we don't hard-reference the DLL).
            var clutterHandsType = AccessTools.TypeByName("WHands.ClutterHandsTDef");
            if (clutterHandsType == null)
            {
                Log.Warning("[Mod Fixes Pack] WHands.ClutterHandsTDef not found in loaded types — SMYH may have changed its namespace. Skipping auto-match.");
                return;
            }
            var compTargetsType = clutterHandsType.GetNestedType("CompTargets", BindingFlags.Public);
            if (compTargetsType == null)
            {
                Log.Warning("[Mod Fixes Pack] ClutterHandsTDef.CompTargets nested type not found. Skipping.");
                return;
            }

            var weaponCompLoaderField = AccessTools.Field(clutterHandsType, "WeaponCompLoader");
            var thingTargetsField     = AccessTools.Field(compTargetsType, "ThingTargets");
            var mainHandField         = AccessTools.Field(compTargetsType, "MainHand");
            var secHandField          = AccessTools.Field(compTargetsType, "SecHand");
            var mainRotField          = AccessTools.Field(compTargetsType, "MainRotation");
            var secRotField           = AccessTools.Field(compTargetsType, "SecRotation");

            if (weaponCompLoaderField == null || thingTargetsField == null ||
                mainHandField == null || secHandField == null ||
                mainRotField == null || secRotField == null)
            {
                Log.Warning("[Mod Fixes Pack] Could not reflect on one or more ClutterHandsTDef fields. Skipping.");
                return;
            }

            // === Step 1: Build the reference library from all existing ClutterHandsTDef defs ===
            var references = new List<Reference>();
            var coveredDefNames = new HashSet<string>();

            foreach (var def in GenericEnumerate(clutterHandsType))
            {
                var loader = weaponCompLoaderField.GetValue(def) as IEnumerable;
                if (loader == null) continue;

                foreach (var entry in loader)
                {
                    var targets = thingTargetsField.GetValue(entry) as IList<string>;
                    if (targets == null || targets.Count == 0) continue;

                    var mainHand = (Vector3)mainHandField.GetValue(entry);
                    var secHand  = (Vector3)secHandField.GetValue(entry);
                    var mainRot  = (float)mainRotField.GetValue(entry);
                    var secRot   = (float)secRotField.GetValue(entry);

                    foreach (var targetDefName in targets)
                    {
                        coveredDefNames.Add(targetDefName);

                        var weaponDef = DefDatabase<ThingDef>.GetNamedSilentFail(targetDefName);
                        if (weaponDef == null) continue; // reference points at a weapon not in this modlist

                        // Filter out body-part "weapons" (SmilodonFangs, ElasmotheriumHorn,
                        // TriceratopsHorn, etc.). These aren't wielded items; they're natural
                        // animal weapons that leak into DefDatabase as ThingDefs with IsWeapon=true.
                        // Using them as references produces wildly wrong hand positions on the
                        // closest-match uncovered weapons.
                        if (weaponDef.equipmentType != EquipmentType.Primary) continue;

                        references.Add(new Reference
                        {
                            DefName     = targetDefName,
                            MainHand    = mainHand,
                            SecHand     = secHand,
                            MainRotation = mainRot,
                            SecRotation  = secRot,
                            Tokens      = ExtractTokens(weaponDef),
                            DrawSize    = GetDrawSize(weaponDef),
                            IsRanged    = IsRangedWeapon(weaponDef),
                        });
                    }
                }
            }

            if (references.Count == 0)
            {
                Log.Message("[Mod Fixes Pack] ShowMeYourHands: no reference data found (Elldar's mod or equivalent not loaded?). Skipping auto-match.");
                return;
            }

            // === Step 2: Find all weapon ThingDefs not already covered ===
            // Require equipmentType == Primary to exclude:
            //  - Body-part "weapons" (TriceratopsHorn, SmilodonFangs)
            //  - Natural attack ThingDefs (fists, claws)
            //  - Projectiles/landmines/thrown resources (NuclearLandmine, GU_RedWood)
            // These aren't held in hand — matching them would waste a reference slot.
            var uncovered = DefDatabase<ThingDef>.AllDefs
                .Where(d => d.IsWeapon && !d.destroyOnDrop
                            && d.equipmentType == EquipmentType.Primary
                            && !coveredDefNames.Contains(d.defName))
                .ToList();

            // === Step 3: For each uncovered weapon, score against every reference, keep best ===
            int matchedCount = 0;
            int unmatchedCount = 0;
            var autoMatchedEntries = new List<(Vector3 main, Vector3 sec, float mainRot, float secRot, string targetDefName)>();
            var unmatchedList = new List<string>();
            var meleeMatchLog = new List<string>(); // per-match diagnostics for melee only (ranged too spammy)

            foreach (var weapon in uncovered)
            {
                var tokens = ExtractTokens(weapon);
                var drawSize = GetDrawSize(weapon);
                var isRanged = IsRangedWeapon(weapon);

                Reference? best = null;
                int minScore = isRanged ? MinMatchScoreRanged : MinMatchScoreMelee;
                int bestScore = minScore - 1;

                foreach (var r in references)
                {
                    // Hard gate: never match a melee weapon to a ranged reference or vice versa.
                    // Cross-category matches produce the worst visual outcomes (pistol hand-grip
                    // on a sword handle, etc.) and the soft -5 penalty wasn't always enough to
                    // prevent it when a strong generic token overlap existed.
                    if (isRanged != r.IsRanged) continue;

                    int score = ScoreMatch(tokens, drawSize, isRanged, r);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = r;
                    }
                }

                if (best.HasValue)
                {
                    autoMatchedEntries.Add((best.Value.MainHand, best.Value.SecHand,
                                            best.Value.MainRotation, best.Value.SecRotation,
                                            weapon.defName));
                    matchedCount++;

                    // Log melee matches so we can spot bad references visually from the log
                    // (e.g. Kukri → BreadKnife is suspicious even at score 8).
                    if (!isRanged)
                    {
                        meleeMatchLog.Add($"{weapon.defName} → {best.Value.DefName} (score {bestScore})");
                    }
                }
                else
                {
                    unmatchedCount++;
                    unmatchedList.Add(weapon.defName);
                }
            }

            if (autoMatchedEntries.Count == 0)
            {
                Log.Message($"[Mod Fixes Pack] ShowMeYourHands: {references.Count} reference entries; all {uncovered.Count} uncovered weapons scored below threshold (melee {MinMatchScoreMelee}, ranged {MinMatchScoreRanged}). Nothing injected.");
                return;
            }

            // === Step 4: Build one ClutterHandsTDef containing all our auto-matches ===
            var newDef = Activator.CreateInstance(clutterHandsType);
            AccessTools.Field(typeof(Def), "defName").SetValue(newDef, "MFP_AutoHandPresets");
            AccessTools.Field(typeof(Def), "label").SetValue(newDef, "Auto-matched hand presets");
            var thingClassField = AccessTools.Field(typeof(ThingDef), "thingClass");
            if (thingClassField != null) thingClassField.SetValue(newDef, typeof(Thing));

            var loaderList = weaponCompLoaderField.GetValue(newDef) as IList;
            if (loaderList == null)
            {
                Log.Warning("[Mod Fixes Pack] Could not get mutable WeaponCompLoader list from new def. Skipping.");
                return;
            }

            foreach (var entry in autoMatchedEntries)
            {
                var compTargets = Activator.CreateInstance(compTargetsType);
                mainHandField.SetValue(compTargets, entry.main);
                secHandField.SetValue(compTargets, entry.sec);
                mainRotField.SetValue(compTargets, entry.mainRot);
                secRotField.SetValue(compTargets, entry.secRot);

                var targetList = thingTargetsField.GetValue(compTargets) as IList<string>;
                targetList?.Add(entry.targetDefName);

                loaderList.Add(compTargets);
            }

            // === Step 5: Inject the def so SMYH's LoadFromDefs (main-menu time) picks it up ===
            // We use the low-level DefDatabase.Add path rather than DefGenerator.AddImpliedDef
            // to avoid triggering anything that assumes the def was loaded from XML.
            var thingDef = (ThingDef)newDef;
            thingDef.shortHash = 0;
            AccessTools.Method(typeof(ShortHashGiver), "GiveShortHash")
                ?.Invoke(null, new object[] { thingDef, typeof(ThingDef), new HashSet<ushort>() });
            DefDatabase<ThingDef>.Add(thingDef);

            Log.Message(
                $"[Mod Fixes Pack] ShowMeYourHands: auto-matched {matchedCount} weapons " +
                $"from {references.Count} reference entries ({unmatchedCount} uncovered skipped as low-confidence).");

            // Log the specific defNames of skipped weapons so we can diagnose which families
            // are missing from our keyword table (or which weapons have dimensions too unusual
            // for confident matching). Capped at 20 to avoid log spam on wildly-modded lists.
            if (unmatchedList.Count > 0)
            {
                var sample = string.Join(", ", unmatchedList.Take(20));
                var suffix = unmatchedList.Count > 20 ? $" ... (+{unmatchedList.Count - 20} more)" : "";
                Log.Message($"[Mod Fixes Pack] ShowMeYourHands: skipped weapons: {sample}{suffix}");
            }

            // Dump every melee match with its chosen reference + score so we can eyeball
            // bad matches (ranged dumps would be too noisy — most guns match well).
            if (meleeMatchLog.Count > 0)
            {
                Log.Message($"[Mod Fixes Pack] ShowMeYourHands: melee matches ({meleeMatchLog.Count}):\n  " +
                            string.Join("\n  ", meleeMatchLog));
            }
        }

        /// <summary>
        /// Score a candidate reference against a target weapon.
        /// +10 per shared weapon-family token (AK matches AK, Mosin matches Mosin, etc.)
        /// +5  per exact generic token overlap not in a family table
        /// +3  if weapon category matches (both ranged or both melee)
        /// -2  per drawSize unit of difference
        /// </summary>
        private static int ScoreMatch(HashSet<string> targetTokens, float targetDrawSize, bool targetRanged, Reference reference)
        {
            int score = 0;

            // Family overlap — highest-value signal
            foreach (var family in WeaponFamilies)
            {
                bool targetInFamily    = family.Any(t => targetTokens.Contains(t));
                bool referenceInFamily = family.Any(t => reference.Tokens.Contains(t));
                if (targetInFamily && referenceInFamily) score += 10;
            }

            // Generic token overlap (catches families we didn't hard-code)
            var generic = targetTokens.Intersect(reference.Tokens).Where(t => t.Length >= 3);
            score += generic.Count() * 5;

            // Category match
            if (targetRanged == reference.IsRanged) score += 3;
            else score -= 5; // pistol-hand-onto-sword is the worst outcome; penalise heavily

            // drawSize proximity — every 0.5 of difference docks 1 point
            var sizeDiff = Mathf.Abs(targetDrawSize - reference.DrawSize);
            score -= Mathf.RoundToInt(sizeDiff * 2f);

            return score;
        }

        /// <summary>
        /// Tokenise a weapon's defName + label for matching. Splits on underscore,
        /// camelCase, spaces, hyphens; removes generic prefixes (Gun_, MeleeWeapon_, etc.);
        /// lowercases and deduplicates.
        /// </summary>
        private static HashSet<string> ExtractTokens(ThingDef def)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var raw = def.defName + " " + (def.label ?? string.Empty);
            // Insert spaces at camelCase boundaries: "DesertEagle" -> "Desert Eagle"
            var spaced = System.Text.RegularExpressions.Regex.Replace(raw, "(?<=[a-z])(?=[A-Z])", " ");

            foreach (var chunk in spaced.Split(new[] { '_', ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var normalised = chunk.ToLowerInvariant();
                if (normalised.Length < 2) continue;
                if (PrefixNoise.Contains(normalised)) continue;
                tokens.Add(normalised);
            }

            return tokens;
        }

        private static float GetDrawSize(ThingDef def)
        {
            if (def.graphicData == null) return 1f;
            var ds = def.graphicData.drawSize;
            return (ds.x + ds.y) * 0.5f; // average x/y — most weapons are drawn with equal-ish dims
        }

        private static bool IsRangedWeapon(ThingDef def)
        {
            // ThingDef.IsRangedWeapon is the canonical property in 1.5.
            return def.IsRangedWeapon;
        }

        /// <summary>
        /// Enumerate all defs of a specific ThingDef subclass (ClutterHandsTDef).
        /// DefDatabase&lt;ThingDef&gt; is the canonical home for ThingDef subclasses.
        /// </summary>
        private static IEnumerable<object> GenericEnumerate(Type defSubclass)
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (defSubclass.IsInstanceOfType(def)) yield return def;
            }
        }
    }
}
