using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Services.Events;
using Game.Services.Llm;
using Game.Services.Saves;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Services
{
    public class BattleService
    {
        public static BattleService Main;

        private readonly LlmService _llmService;
        private readonly LoggingService _loggingService;
        private readonly System.Random _rng = new System.Random();

        private string _sysBattlePrompt;

        private readonly Dictionary<int, int> _unitToBattle = new();

        public BattleService(LlmService llmService, LoggingService loggingService)
        {
            _llmService = llmService;
            _loggingService = loggingService;
            Main = this;

            _sysBattlePrompt = ResourcesService.LoadPrompt("sys_battle");
        }

        private void MapUnitsToBattle(BattleData battle)
        {
            foreach (var armyId in battle.AttackerArmyIds)
            {
                _unitToBattle[armyId] = battle.Id;
            }

            foreach (var armyId in battle.DefenderArmyIds)
            {
                _unitToBattle[armyId] = battle.Id;
            }
        }

        private void UnmapUnitsToBattle(BattleData battle)
        {
            foreach (var armyId in battle.AttackerArmyIds)
            {
                _unitToBattle.Remove(armyId);
            }

            foreach (var armyId in battle.DefenderArmyIds)
            {
                _unitToBattle.Remove(armyId);
            }
        }

        public bool IsUnitInBattle(int unitId)
        {
            return _unitToBattle.ContainsKey(unitId);
        }

        public async Task<List<GameEvent>> ProcessBattles(SaveData save)
        {
            var events = new List<GameEvent>();

            // 1. Identify battles
            var battles = IdentifyBattles(save);
            if (battles.Count == 0) return events;

            _loggingService.LogForService("BattleService", $"Processing {battles.Count} battles/sieges.");

            foreach (var battle in battles)
            {
                var gameEvent = await ResolveBattle(save, battle);
                if (gameEvent != null)
                {
                    events.Add(gameEvent);
                    var json = JsonConvert.SerializeObject(gameEvent, Formatting.Indented);
                    _loggingService.LogForService("BattleService", $"Battle event:\n{json}");
                }
            }

            return events;
        }

        public void Initialize(SaveData save)
        {
            _unitToBattle.Clear();
            foreach (var battle in save.Battle)
            {
                MapUnitsToBattle(battle);
            }
        }

        private List<BattleContext> IdentifyBattles(SaveData save)
        {
            var battles = new List<BattleContext>();

            foreach (var loc in save.Commandery.Values)
            {
                var armiesHere = save.Army.Where(a => a.LocationId == loc.Id).ToList();
                //if (armiesHere.Count == 0) continue;

                // Armies belonging to the location owner (Defenders)
                var defenders = armiesHere.Where(a => a.CountryId == loc.CountryId).ToList();

                // Armies NOT belonging to location owner
                var foreignArmies = armiesHere.Where(a => a.CountryId != loc.CountryId).ToList();

                var attackers = new List<ArmyData>();
                foreach (var army in foreignArmies)
                {
                    // Check if At War
                    var rel = save.Relation.FirstOrDefault(r =>
                        (r.SrcCountryId == army.CountryId && r.DstCountryId == loc.CountryId) ||
                        (r.SrcCountryId == loc.CountryId && r.DstCountryId == army.CountryId));

                    if (rel != null && rel.IsAtWar)
                    {
                        attackers.Add(army);
                    }
                }

                // Check for foreign armies joining the defender
                if (attackers.Count > 0)
                {
                    var potentialAllies = foreignArmies.Except(attackers).ToList();
                    foreach (var army in potentialAllies)
                    {
                        var alliance = save.Relation.FirstOrDefault(r =>
                            (r.SrcCountryId == army.CountryId && r.DstCountryId == loc.CountryId) ||
                            (r.SrcCountryId == loc.CountryId && r.DstCountryId == army.CountryId));

                        bool isAlliedToDefender = alliance != null && alliance.IsAllied;

                        bool isAtWarWithAttackers = attackers.Any(attacker =>
                        {
                            var warRel = save.Relation.FirstOrDefault(r =>
                                (r.SrcCountryId == army.CountryId && r.DstCountryId == attacker.CountryId) ||
                                (r.SrcCountryId == attacker.CountryId && r.DstCountryId == army.CountryId));
                            return warRel != null && warRel.IsAtWar;
                        });

                        if (isAlliedToDefender && isAtWarWithAttackers)
                        {
                            defenders.Add(army);
                        }
                    }
                }

                if (attackers.Count > 0)
                {
                    var existingBattle = save.Battle.FirstOrDefault(b => b.LocationId == loc.Id);
                    if (existingBattle == null)
                    {
                        existingBattle = new BattleData
                        {
                            Id = save.Battle.Any() ? save.Battle.Max(b => b.Id) + 1 : 1,
                            Phase = "start",
                            AttackerArmyIds = attackers.Select(a => a.Id).ToList(),
                            DefenderArmyIds = defenders.Select(a => a.Id).ToList(),
                            LocationId = loc.Id,
                            StartingMonth = save.Game.CurrentMonth,
                            StartingYear = save.Game.CurrentYear,
                            AttackerLosses = 0,
                            DefenderLosses = 0
                        };
                        save.Battle.Add(existingBattle);
                        MapUnitsToBattle(existingBattle);
                    }
                    else
                    {
                        // Clean up old mapping before updating
                        UnmapUnitsToBattle(existingBattle);
                        existingBattle.AttackerArmyIds = attackers.Select(a => a.Id).ToList();
                        existingBattle.DefenderArmyIds = defenders.Select(a => a.Id).ToList();
                        MapUnitsToBattle(existingBattle);
                    }

                    battles.Add(new BattleContext
                    {
                        Location = loc,
                        Attackers = attackers,
                        Defenders = defenders,
                        OwnerId = loc.CountryId,
                        Data = existingBattle
                    });
                }
                else
                {
                    var existingBattle = save.Battle.FirstOrDefault(b => b.LocationId == loc.Id);
                    if (existingBattle != null)
                    {
                        save.Battle.Remove(existingBattle);
                        UnmapUnitsToBattle(existingBattle);
                    }
                }
            }

            return battles;
        }

        private async Task<GameEvent> ResolveBattle(SaveData save, BattleContext battle)
        {
            if (battle.Defenders.Count == 0)
            {
                if (battle.Data.Phase == "siege") return await ResolveSiege(save, battle);
                else if (battle.Data.Phase == "assault") return await ResolveAssault(save, battle);
                else return await ResolveRaid(save, battle);
            }
            else
            {
                // Morale Calculation
                long attackerMorale = 0;
                long totalAttackerSize = battle.Attackers.Sum(a => a.Size);
                if (totalAttackerSize > 0)
                    attackerMorale = battle.Attackers.Sum(a => (long)a.Morale * a.Size) / totalAttackerSize;

                long defenderMorale = 0;
                long totalDefenderSize = battle.Defenders.Sum(d => d.Size);
                if (totalDefenderSize > 0)
                    defenderMorale = battle.Defenders.Sum(d => (long)d.Morale * d.Size) / totalDefenderSize;

                int initiativeRoll = _rng.Next(1, 21);
                if (attackerMorale <= 100 - initiativeRoll || defenderMorale <= 100 - initiativeRoll)
                {
                    battle.Data.Phase = "engagement";
                    return await ResolveEngage(save, battle);
                }
                else
                {
                    battle.Data.Phase = "skirmish";
                    return await ResolveSkirmish(save, battle);
                }
            }
        }

        private async Task<GameEvent> ResolveRaid(SaveData save, BattleContext battle)
        {
            var battlefield = battle.Location;
            int totalAttackerSize = battle.Attackers.Sum(a => a.Size);
            var attackerGeneral = GetGeneral(save, battle.Attackers);

            // Attacker Morale
            long attackerMorale = totalAttackerSize > 0 ?
                battle.Attackers.Sum(a => (long)a.Morale * a.Size) / totalAttackerSize : 0;

            var defenderGeneral = battlefield.CommanderId.HasValue
                ? save.Person.FirstOrDefault(p => p.Id == battlefield.CommanderId.Value)
                : null;

            int defendSkill = defenderGeneral?.Stats?.SiegeDefense ?? 0;
            int attackSkill = attackerGeneral?.Stats?.SiegeOffense ?? 0;

            double damageMultiplier = 0;
            int attackRoll = _rng.Next(1, 21);

            if (battlefield.Defensiveness + defendSkill >= attackRoll + attackSkill + 10)
                damageMultiplier = 0;
            else if (attackRoll == 20 || attackRoll + attackSkill >= battlefield.Defensiveness + defendSkill + 10)
                damageMultiplier = 2;
            else
                damageMultiplier = 1.0 + (attackRoll + attackSkill - battlefield.Defensiveness - defendSkill) / 10.0;

            damageMultiplier = Math.Max(0, damageMultiplier);

            int popDMG = (int)(totalAttackerSize / 20.0 * damageMultiplier);
            int WealthDMG = (int)(5 * damageMultiplier);
            int SupplyGain = (int)(10 * damageMultiplier);
            int prestigeGain = (int)damageMultiplier;

            // Cap popDMG at half the population, reduce other values proportionally
            int maxPopDMG = battlefield.Population / 2;
            if (popDMG > maxPopDMG && popDMG > 0)
            {
                double ratio = (double)maxPopDMG / popDMG;
                popDMG = maxPopDMG;
                WealthDMG = (int)(WealthDMG * ratio);
                SupplyGain = (int)(SupplyGain * ratio);
            }

            battlefield.Population = Math.Max(0, battlefield.Population - popDMG);
            battlefield.Wealth = Math.Max(0, battlefield.Wealth - WealthDMG);
            foreach (var army in battle.Attackers) army.Supply += SupplyGain;

            // Effects
            var effects = new Dictionary<string, List<Dictionary<string, object>>>();
            var narrativeParts = new List<string> { $"Raid on {battlefield.Name}." };

            effects["commandery"] = new List<Dictionary<string, object>> {
                new Dictionary<string, object> {
                    { "id", battlefield.Id },
                    { "Population", $"-{popDMG}" },
                    { "Wealth", $"-{WealthDMG}" }
                }
            };
            narrativeParts.Add($"Damage: Population -{popDMG}, Wealth -{WealthDMG}.");

            ApplyPrestigeChange(save, battle.Attackers.Select(a => a.CountryId).Distinct(), prestigeGain, effects);
            ApplyPrestigeChange(save, battlefield.CountryId, -prestigeGain, effects);

            if (!effects.ContainsKey("army")) effects["army"] = new List<Dictionary<string, object>>();
            foreach (var army in battle.Attackers)
            {
                effects["army"].Add(new Dictionary<string, object> { { "id", army.Id }, { "Supply", $"+{SupplyGain}" } });
            }
            narrativeParts.Add($"Attackers seized supplies and gained {prestigeGain} prestige.");

            battle.Data.Phase = "siege";

            string summary = string.Join(" ", narrativeParts);
            string fallbackTitle = "Raid of " + battlefield.Name;
            var (eventName, eventDesc) = await GenerateFlavorText(save, battle, summary, fallbackTitle);

            return CreateEvent(eventName, eventDesc, summary, save, battle, effects);
        }

        private async Task<GameEvent> ResolveSiege(SaveData save, BattleContext battle)
        {
            var battlefield = battle.Location;
            int totalAttackerSize = battle.Attackers.Sum(a => a.Size);
            var attackerGeneral = GetGeneral(save, battle.Attackers);
            var defenderGeneral = save.Person.FirstOrDefault(p => p.Id == battlefield.CommanderId);

            int defendSkill = defenderGeneral?.Stats?.SiegeDefense ?? 0;
            int attackSkill = attackerGeneral?.Stats?.SiegeOffense ?? 0;

            int defendRoll = _rng.Next(1, 11);
            int attackRoll = _rng.Next(1, 21);
            int attackDamage = _rng.Next(0, attackSkill + 3);

            int battleDuration = (save.Game.CurrentYear * 12 + save.Game.CurrentMonth) - (battle.Data.StartingYear * 12 + battle.Data.StartingMonth);
            double baseDamage = 4 + battleDuration - (defendSkill / 4.0);
            double damageMultiplier = (8.0 + attackDamage) / (10.0 + defendSkill / 2.0);

            int garrisonLoss = (int)(baseDamage * damageMultiplier * battlefield.Garrisons / 100.0);
            int attackerLoss = 0;
            int prestigeGain = 0;

            if (defendRoll + defendSkill + battlefield.Defensiveness >= attackRoll + attackSkill + 10)
            {
                attackerLoss = garrisonLoss;
                prestigeGain -= 5;
            }
            else if (attackRoll == 20 || attackRoll + attackSkill >= defendRoll + defendSkill + battlefield.Defensiveness + 10)
            {
                garrisonLoss *= 2;
                prestigeGain += 5;
            }

            if (battleDuration > 6) garrisonLoss += _rng.Next(0, 150);

            int actualGarrisonLoss = Math.Min(garrisonLoss, battlefield.Garrisons);
            battlefield.Garrisons -= actualGarrisonLoss;

            // Apply Attacker Losses
            attackerLoss = Math.Min(attackerLoss, totalAttackerSize);
            DistributeLosses(battle.Attackers, attackerLoss);

            battle.Data.AttackerLosses += attackerLoss;
            battle.Data.DefenderLosses += actualGarrisonLoss;

            // Narrative
            var effects = new Dictionary<string, List<Dictionary<string, object>>>();
            var narrativeParts = new List<string> { $"Siege of {battlefield.Name} continues." };

            if (actualGarrisonLoss > 0)
            {
                AddEffect(effects, "commandery", new Dictionary<string, object> { { "id", battlefield.Id }, { "Garrisons", $"-{actualGarrisonLoss}" } });
                narrativeParts.Add($"Defenders lost {actualGarrisonLoss} troops.");
            }

            // Apply Morale Factor to Attacker Loss
            long attackerMorale = totalAttackerSize > 0 ?
                battle.Attackers.Sum(a => (long)a.Morale * a.Size) / totalAttackerSize : 0;
            attackerLoss = (int)(attackerLoss * 100.0 / Math.Max(2, attackerMorale));

            if (attackerLoss > 0) narrativeParts.Add($"Attackers suffered {attackerLoss} casualties.");

            ApplyPrestigeChange(save, battle.Attackers.Select(a => a.CountryId), prestigeGain, effects);

            bool captured = battlefield.Garrisons <= 0;
            if (captured) HandleCityCapture(save, battle, effects, narrativeParts);

            CleanupNullArmies(save, battle, narrativeParts);

            string summary = string.Join(" ", narrativeParts);
            string fallbackTitle = captured ? $"Fall of {battlefield.Name}" : $"Siege of {battlefield.Name}";
            var (eventName, eventDesc) = await GenerateFlavorText(save, battle, summary, fallbackTitle);

            return CreateEvent(eventName, eventDesc, summary, save, battle, effects);
        }

        private async Task<GameEvent> ResolveAssault(SaveData save, BattleContext battle)
        {
            var battlefield = battle.Location;
            int totalAttackerSize = battle.Attackers.Sum(a => a.Size);
            var attackerGeneral = GetGeneral(save, battle.Attackers);
            var defenderGeneral = save.Person.FirstOrDefault(p => p.Id == battlefield.CommanderId);

            int defendSkill = defenderGeneral?.Stats?.SiegeDefense ?? 0;
            int attackSkill = attackerGeneral?.Stats?.SiegeOffense ?? 0;

            int defendRoll = _rng.Next(1, 11);
            int attackRoll = _rng.Next(1, 21);
            int attackDamage = _rng.Next(0, attackSkill + 3);

            double baseDamage = _rng.Next(5, 16);
            double damageMultiplier = (8.0 + attackDamage) / (10.0 + defendSkill / 2.0);
            double defendRollDamage = (defendRoll + 1 + defendSkill / 4.0) / 2.0;

            int battleDuration = (save.Game.CurrentYear * 12 + save.Game.CurrentMonth) - (battle.Data.StartingYear * 12 + battle.Data.StartingMonth);
            double durationDamage = 3 + battleDuration - (defendSkill / 4.0);

            int garrisonLoss = (int)(durationDamage * damageMultiplier * battlefield.Garrisons / 100.0);

            int attackerLoss = (int)(baseDamage * totalAttackerSize / 100.0);
            garrisonLoss += (int)(attackerLoss / defendRollDamage * damageMultiplier);
            int prestigeGain = 0;

            if (defendRoll + defendSkill + battlefield.Defensiveness >= attackRoll + attackSkill + 10)
            {
                prestigeGain -= 5;
                attackerLoss *= 2;
                garrisonLoss /= 2;
            }
            else if (attackRoll == 20 || attackRoll + attackSkill >= defendRoll + defendSkill + battlefield.Defensiveness + 10)
            {
                prestigeGain += 5;
                attackerLoss = (int)(attackerLoss * 0.75);
                garrisonLoss = (int)(garrisonLoss * 1.5);
            }

            int actualGarrisonLoss = Math.Min(garrisonLoss, battlefield.Garrisons);
            battlefield.Garrisons -= actualGarrisonLoss;

            attackerLoss = Math.Min(attackerLoss, totalAttackerSize);
            DistributeLosses(battle.Attackers, attackerLoss);
            // Morale drop for attackers
            foreach (var army in battle.Attackers) army.Morale = Math.Max(2, army.Morale - (int)baseDamage);

            battle.Data.AttackerLosses += attackerLoss;
            battle.Data.DefenderLosses += actualGarrisonLoss;

            // Narrative
            var effects = new Dictionary<string, List<Dictionary<string, object>>>();
            var narrativeParts = new List<string> { $"Assault on {battlefield.Name}!" };

            if (actualGarrisonLoss > 0)
            {
                AddEffect(effects, "commandery", new Dictionary<string, object> { { "id", battlefield.Id }, { "Garrisons", $"-{actualGarrisonLoss}" } });
                narrativeParts.Add($"Defenders lost {actualGarrisonLoss} troops.");
            }

            // Apply Morale Factor to Attacker Loss
            long attackerMorale = totalAttackerSize > 0 ?
                battle.Attackers.Sum(a => (long)a.Morale * a.Size) / totalAttackerSize : 0;
            attackerLoss = (int)(attackerLoss * 100.0 / Math.Max(2, attackerMorale));

            if (attackerLoss > 0) narrativeParts.Add($"Attackers suffered {attackerLoss} casualties.");

            ApplyPrestigeChange(save, battle.Attackers.Select(a => a.CountryId), prestigeGain, effects);

            bool captured = battlefield.Garrisons <= 0;
            if (captured) HandleCityCapture(save, battle, effects, narrativeParts);
            else narrativeParts.Add("The assault failed to capture the city.");

            CleanupNullArmies(save, battle, narrativeParts);

            string summary = string.Join(" ", narrativeParts);
            string fallbackTitle = captured ? $"Fall of {battlefield.Name}" : $"Assault of {battlefield.Name}";
            var (eventName, eventDesc) = await GenerateFlavorText(save, battle, summary, fallbackTitle);

            battle.Data.Phase = "siege"; // Reset to siege
            return CreateEvent(eventName, eventDesc, summary, save, battle, effects);
        }

        private async Task<GameEvent> ResolveSkirmish(SaveData save, BattleContext battle)
        {
            // Field Battle Logic
            int attackerLoss = 0;
            int defenderLoss = 0;
            double attackerMoraleLoss = 0;
            double defenderMoraleLoss = 0;
            int attackerPrestigeGain = 0;
            int defenderPrestigeGain = 0;

            // Attacker stats
            var battlefield = battle.Location;
            int totalAttackerSize = battle.Attackers.Sum(a => a.Size);
            var attackerGeneral = GetGeneral(save, battle.Attackers);

            long attackerMorale = 0;
            if (totalAttackerSize > 0)
                attackerMorale = battle.Attackers.Sum(a => (long)a.Morale * a.Size) / totalAttackerSize;

            int attackerAttackSkill = attackerGeneral?.Stats?.FieldOffense ?? 0;
            int attackerDefenseSkill = attackerGeneral?.Stats?.FieldDefense ?? 0;

            // Defender stats
            int totalDefenderSize = battle.Defenders.Sum(d => d.Size);
            var defenderGeneral = GetGeneral(save, battle.Defenders);

            long defenderMorale = 0;
            if (totalDefenderSize > 0)
                defenderMorale = battle.Defenders.Sum(d => (long)d.Morale * d.Size) / totalDefenderSize;

            int defenderAttackSkill = defenderGeneral?.Stats?.FieldOffense ?? 0;
            int defenderDefenseSkill = defenderGeneral?.Stats?.FieldDefense ?? 0;
            defenderDefenseSkill += battlefield.Defensiveness;
            attackerDefenseSkill += 5;

            // Attacker skirmish
            int attackerSkirmishSize = (int)(0.01 * totalAttackerSize);

            int attackRoll = _rng.Next(1, 21);
            if (attackRoll == 20 || attackRoll + attackerAttackSkill >= defenderDefenseSkill + 10)
            {
                defenderLoss += attackerSkirmishSize;
                defenderMoraleLoss += 2 * GameConstants.SkirmishMoraleLoss;
                attackerPrestigeGain += 2;
                defenderPrestigeGain -= 2;
            }
            else if (attackRoll == 1 || attackRoll + attackerAttackSkill <= defenderDefenseSkill)
            {
                attackerLoss += attackerSkirmishSize;
                attackerMoraleLoss += 2 * GameConstants.SkirmishMoraleLoss;
                attackerPrestigeGain -= 2;
                defenderPrestigeGain += 2;
            }
            else
            {
                double lossRatio = (double)attackRoll / (attackRoll + defenderDefenseSkill);
                defenderLoss += (int)(attackerSkirmishSize * lossRatio);
                defenderMoraleLoss += GameConstants.SkirmishMoraleLoss;
            }

            // Defender skirmish
            int defenderSkirmishSize = (int)(0.01 * totalDefenderSize);

            attackRoll = _rng.Next(1, 21);
            if (attackRoll == 20 || attackRoll + defenderAttackSkill >= attackerDefenseSkill + 10)
            {
                attackerLoss += defenderSkirmishSize;
                attackerMoraleLoss += 2 * GameConstants.SkirmishMoraleLoss;
                defenderPrestigeGain += 2;
                attackerPrestigeGain -= 2;
            }
            else if (attackRoll == 1 || attackRoll + defenderAttackSkill <= attackerDefenseSkill)
            {
                defenderLoss += defenderSkirmishSize;
                defenderMoraleLoss += 2 * GameConstants.SkirmishMoraleLoss;
                attackerPrestigeGain += 2;
                defenderPrestigeGain -= 2;
            }
            else
            {
                double lossRatio = (double)attackRoll / (attackRoll + attackerDefenseSkill);
                attackerLoss += (int)(defenderSkirmishSize * lossRatio);
                attackerMoraleLoss += GameConstants.SkirmishMoraleLoss;
            }

            // Apply Morale Scale
            attackerLoss = (int)(attackerLoss * 100.0 / Math.Max(2, attackerMorale));
            defenderLoss = (int)(defenderLoss * 100.0 / Math.Max(2, defenderMorale));

            // Cap losses
            attackerLoss = Math.Min(attackerLoss, totalAttackerSize);
            defenderLoss = Math.Min(defenderLoss, totalDefenderSize);

            ApplyBattleLosses(battle.Attackers, attackerLoss, attackerMoraleLoss);
            ApplyBattleLosses(battle.Defenders, defenderLoss, defenderMoraleLoss);

            battle.Data.AttackerLosses += attackerLoss;
            battle.Data.DefenderLosses += defenderLoss;

            // Event Generation
            var effects = new Dictionary<string, List<Dictionary<string, object>>>();
            var narrativeParts = new List<string> { "Skirmish phase report." };
            if (attackerLoss > 0) narrativeParts.Add($"Attackers lost {attackerLoss} troops.");
            if (defenderLoss > 0) narrativeParts.Add($"Defenders lost {defenderLoss} troops.");

            ApplyPrestigeChange(save, battle.Attackers.Select(a => a.CountryId).Distinct(), attackerPrestigeGain, effects);
            ApplyPrestigeChange(save, battle.Defenders.Select(d => d.CountryId).Distinct(), defenderPrestigeGain, effects);

            CleanupNullArmies(save, battle, narrativeParts);

            string summary = string.Join(" ", narrativeParts);
            var (eventName, eventDesc) = await GenerateFlavorText(save, battle, summary, "Skirmish");

            return CreateEvent(eventName, eventDesc, summary, save, battle, effects);
        }

        private async Task<GameEvent> ResolveEngage(SaveData save, BattleContext battle)
        {
            var effects = new Dictionary<string, List<Dictionary<string, object>>>
            {
                ["country"] = new List<Dictionary<string, object>>()
            };
            var narrativeParts = new List<string>();

            // Field Battle Logic
            int attackerLoss = 0;
            int defenderLoss = 0;
            double attackerMoraleLoss = 0;
            double defenderMoraleLoss = 0;
            double attackerPrestigeGain = 0;
            double defenderPrestigeGain = 0;

            // Attacker stats
            var battlefield = battle.Location;
            int totalAttackerSize = battle.Attackers.Sum(a => a.Size);
            var attackerGeneral = GetGeneral(save, battle.Attackers);

            long attackerMorale = 0;
            if (totalAttackerSize > 0)
                attackerMorale = battle.Attackers.Sum(a => (long)a.Morale * a.Size) / totalAttackerSize;

            int attackerAttackSkill = attackerGeneral?.Stats?.FieldOffense ?? 0;
            int attackerDefenseSkill = attackerGeneral?.Stats?.FieldDefense ?? 0;

            // Defender stats
            int totalDefenderSize = battle.Defenders.Sum(d => d.Size);
            var defenderGeneral = GetGeneral(save, battle.Defenders);

            long defenderMorale = 0;
            if (totalDefenderSize > 0)
                defenderMorale = battle.Defenders.Sum(d => (long)d.Morale * d.Size) / totalDefenderSize;

            int defenderAttackSkill = defenderGeneral?.Stats?.FieldOffense ?? 0;
            int defenderDefenseSkill = defenderGeneral?.Stats?.FieldDefense ?? 0;
            defenderDefenseSkill += battlefield.Defensiveness;
            attackerDefenseSkill += 5;

            // Roll initiative
            int initiativeRoll = _rng.Next(0, (int)(attackerMorale + defenderMorale + 1));
            string initiativeSide = "None";
            if (initiativeRoll < attackerMorale)
            {
                initiativeSide = "Attacker";
                // attackers push
                int defendScore = defenderDefenseSkill + battlefield.Defensiveness;
                int attackScore = attackerAttackSkill + 5;
                int attackRoll = _rng.Next(1, 21);
                double attackPower = (attackerAttackSkill + 20.0) / 20.0 * Math.Sqrt((double)totalAttackerSize / Math.Max(1, totalDefenderSize));
                int damageRoll = _rng.Next(1, 11);

                if (attackRoll == 20 || attackRoll + attackScore >= defendScore + 10)
                {
                    // critical success
                    battle.Data.Phase = "breakthrough";
                    damageRoll = _rng.Next(5, 21);
                    attackerLoss += (int)(damageRoll * totalAttackerSize * 0.001);
                    defenderLoss += (int)(attackerLoss * attackPower * (damageRoll / 2.0));
                    defenderMoraleLoss += 3 * damageRoll;
                    attackerMoraleLoss -= 10;
                    attackerPrestigeGain += 10;
                    defenderPrestigeGain -= 10;

                    // 5% chance defender general dies
                    if (defenderGeneral != null && _rng.Next(1, 101) <= 5)
                    {
                        defenderPrestigeGain -= 10;
                        defenderMoraleLoss += 20;
                        defenderGeneral.IsAlive = false;
                        defenderGeneral.History += $" Dead since {save.Game.CurrentYear} in battle of {battlefield.Name}";
                        narrativeParts.Add($"The defender's general, {defenderGeneral.Name}, falls in the chaos!");

                        foreach (var army in battle.Defenders)
                        {
                            if (army.CommanderId == defenderGeneral.Id) army.CommanderId = null;
                        }
                    }
                }
                else if (attackRoll == 1 || attackRoll + attackScore <= defendScore)
                {
                    // critical failure
                    damageRoll = _rng.Next(5, 21);
                    defenderLoss += (int)(damageRoll * totalAttackerSize * 0.001 * attackPower);
                    attackerLoss += (int)(defenderLoss * (damageRoll / 2.0));
                    attackerMoraleLoss += 3 * damageRoll;
                    defenderMoraleLoss -= 10;
                    attackerPrestigeGain -= 10;
                    defenderPrestigeGain += 10;

                    // 5% chance attacker general dies
                    if (attackerGeneral != null && _rng.Next(1, 101) <= 5)
                    {
                        attackerPrestigeGain -= 10;
                        attackerMoraleLoss += 20;
                        attackerGeneral.IsAlive = false;
                        attackerGeneral.History += $" Dead since {save.Game.CurrentYear} in battle of {battlefield.Name}";
                        narrativeParts.Add($"The attacker's general, {attackerGeneral.Name}, falls in the chaos!");

                        foreach (var army in battle.Attackers)
                        {
                            if (army.CommanderId == attackerGeneral.Id) army.CommanderId = null;
                        }
                    }
                }
                else
                {
                    attackerLoss += (int)(totalAttackerSize * damageRoll * 0.5 * 0.001);
                    defenderLoss += (int)(attackerLoss * attackPower * (attackScore - defenderDefenseSkill) * 0.1);
                    defenderMoraleLoss += ((double)defenderLoss / Math.Max(1, totalDefenderSize)) * 200;
                    attackerMoraleLoss += ((double)attackerLoss / Math.Max(1, totalAttackerSize)) * 200;
                    attackerPrestigeGain += (defenderMoraleLoss - attackerMoraleLoss) / 4;
                    defenderPrestigeGain += (attackerMoraleLoss - defenderMoraleLoss) / 4;
                }
            }
            else
            {
                initiativeSide = "Defender";
                // defenders push
                int defendScore = attackerDefenseSkill + 5;
                int attackScore = defenderAttackSkill + 5;
                int attackRoll = _rng.Next(1, 21);
                double attackPower = (defenderAttackSkill + 20.0) / 20.0 * Math.Sqrt((double)totalDefenderSize / Math.Max(1, totalAttackerSize));
                int damageRoll = _rng.Next(1, 11);

                if (attackRoll == 20 || attackRoll + attackScore >= defendScore + 10)
                {
                    // critical success
                    battle.Data.Phase = "breakthrough";
                    damageRoll = _rng.Next(5, 21);
                    defenderLoss += (int)(damageRoll * totalDefenderSize * 0.001);
                    attackerLoss += (int)(defenderLoss * attackPower * (damageRoll / 2.0));
                    attackerMoraleLoss += 3 * damageRoll;
                    defenderMoraleLoss -= 10;
                    attackerPrestigeGain -= 10;
                    defenderPrestigeGain += 10;

                    // 5% chance attacker general dies
                    if (attackerGeneral != null && _rng.Next(1, 101) <= 5)
                    {
                        attackerPrestigeGain -= 10;
                        attackerMoraleLoss += 20;
                        attackerGeneral.IsAlive = false;
                        attackerGeneral.History = $" Dead since {save.Game.CurrentYear} in battle of {battlefield.Name}";
                        narrativeParts.Add($"The attacker's general, {attackerGeneral.Name}, falls in the chaos!");

                        foreach (var army in battle.Attackers)
                        {
                            if (army.CommanderId == attackerGeneral.Id) army.CommanderId = null;
                        }
                    }
                }
                else if (attackRoll == 1 || attackRoll + attackScore <= defendScore)
                {
                    // critical failure
                    damageRoll = _rng.Next(5, 21);
                    attackerLoss += (int)(damageRoll * totalDefenderSize * 0.001 * attackPower);
                    defenderLoss += (int)(attackerLoss * (damageRoll / 2.0));
                    attackerMoraleLoss += 3 * damageRoll;
                    defenderMoraleLoss -= 10;
                    attackerPrestigeGain -= 10;
                    defenderPrestigeGain += 10;

                    // 5% chance defender general dies
                    if (defenderGeneral != null && _rng.Next(1, 101) <= 5)
                    {
                        defenderPrestigeGain -= 10;
                        defenderMoraleLoss += 20;
                        defenderGeneral.IsAlive = false;
                        defenderGeneral.History = $" Dead since {save.Game.CurrentYear} in battle of {battlefield.Name}";
                        narrativeParts.Add($"The defender's general, {defenderGeneral.Name}, falls in the chaos!");

                        foreach (var army in battle.Defenders)
                        {
                            if (army.CommanderId == defenderGeneral.Id) army.CommanderId = null;
                        }
                    }
                }
                else
                {
                    defenderLoss += (int)(totalDefenderSize * damageRoll * 0.5 * 0.001);
                    attackerLoss += (int)(defenderLoss * attackPower * (attackScore - attackerDefenseSkill) * 0.1);
                    defenderMoraleLoss += ((double)defenderLoss / Math.Max(1, totalDefenderSize)) * 200.0;
                    attackerMoraleLoss += ((double)attackerLoss / Math.Max(1, totalAttackerSize)) * 200.0;
                    attackerPrestigeGain += (defenderMoraleLoss - attackerMoraleLoss) / 4;
                    defenderPrestigeGain += (attackerMoraleLoss - defenderMoraleLoss) / 4;
                }
            }

            // Apply Morale Scale
            attackerLoss = (int)(attackerLoss * 100.0 / Math.Max(2, attackerMorale));
            defenderLoss = (int)(defenderLoss * 100.0 / Math.Max(2, defenderMorale));

            attackerLoss = Math.Min(attackerLoss, totalAttackerSize);
            defenderLoss = Math.Min(defenderLoss, totalDefenderSize);

            ApplyBattleLosses(battle.Attackers, attackerLoss, attackerMoraleLoss);
            ApplyBattleLosses(battle.Defenders, defenderLoss, defenderMoraleLoss);

            battle.Data.AttackerLosses += attackerLoss;
            battle.Data.DefenderLosses += defenderLoss;

            // Event Generation
            narrativeParts.Insert(0, $"Engagement Phase: {battle.Data.Phase}");

            ApplyPrestigeChange(save, battle.Attackers.Select(a => a.CountryId).Distinct(), (int)attackerPrestigeGain, effects);
            ApplyPrestigeChange(save, battle.Defenders.Select(d => d.CountryId).Distinct(), (int)defenderPrestigeGain, effects);

            CleanupNullArmies(save, battle, narrativeParts);
            if (attackerLoss > 0) narrativeParts.Add($"Attackers lost {attackerLoss} troops.");
            if (defenderLoss > 0) narrativeParts.Add($"Defenders lost {defenderLoss} troops.");

            string summary = string.Join(" ", narrativeParts);
            var (eventName, eventDesc) = await GenerateFlavorText(save, battle, summary, "Engagement Report");

            return CreateEvent(eventName, eventDesc, summary, save, battle, effects);
        }

        // --- Helpers ---

        private PersonData GetGeneral(SaveData save, List<ArmyData> armies)
        {
            return armies
                .Where(a => a.CommanderId.HasValue)
                .OrderByDescending(a => a.Size)
                .Select(a => save.Person.FirstOrDefault(p => p.Id == a.CommanderId))
                .FirstOrDefault();
        }

        private void DistributeLosses(List<ArmyData> armies, int totalLoss)
        {
            int totalSize = armies.Sum(a => a.Size);
            if (totalSize <= 0) return;

            if (totalSize <= totalLoss)
            {
                foreach (var a in armies) a.Size = 0;
                return;
            }

            // Proportional distribution: each army takes losses based on its share of the total size
            int remaining = totalLoss;
            foreach (var army in armies)
            {
                if (remaining <= 0) break;
                // Calculate proportional loss: (army.Size / totalSize) * totalLoss
                int proportionalLoss = (int)Math.Round((double)army.Size / totalSize * totalLoss);
                int actualLoss = Math.Min(proportionalLoss, Math.Min(remaining, army.Size));
                army.Size -= actualLoss;
                remaining -= actualLoss;
            }

            // Handle any rounding remainder by distributing to armies that still have troops
            while (remaining > 0)
            {
                bool distributed = false;
                foreach (var army in armies)
                {
                    if (remaining <= 0) break;
                    if (army.Size > 0)
                    {
                        army.Size -= 1;
                        remaining -= 1;
                        distributed = true;
                    }
                }
                if (!distributed) break; // Safety: avoid infinite loop
            }
        }

        private void ApplyBattleLosses(List<ArmyData> armies, int sizeLoss, double moraleLoss)
        {
            DistributeLosses(armies, sizeLoss);
            foreach (var a in armies)
            {
                if (a.Size > 0) a.Morale = (int)Math.Max(2, a.Morale - moraleLoss);
                else a.Morale = 2;
            }
        }

        private void HandleCityCapture(SaveData save, BattleContext battle, Dictionary<string, List<Dictionary<string, object>>> effects, List<string> narrativeParts)
        {
            var battlefield = battle.Location;
            narrativeParts.Add($"The garrison has been depleted! {battlefield.Name} has fallen.");

            var winner = battle.Attackers.OrderByDescending(a => a.Size).FirstOrDefault();
            if (winner != null)
            {
                var winnerCountryName = save.Country.TryGetValue(winner.CountryId, out var wc) ? wc.Name : "Unknown";
                var defenderCountryName = save.Country.TryGetValue(battlefield.CountryId, out var dc) ? dc.Name : "Unknown";
                battlefield.History += $" Conquered by {winnerCountryName} from {defenderCountryName} in {save.Game.CurrentYear}.";

                // Prestige Change based on population
                int prestigeChange;
                int relationPenalty;
                if (battlefield.Population > 1000000)
                {
                    prestigeChange = 25;
                    relationPenalty = -30;
                }
                else if (battlefield.Population > 100000)
                {
                    prestigeChange = 15;
                    relationPenalty = -20;
                }
                else
                {
                    prestigeChange = 10;
                    relationPenalty = -10;
                }

                // Loser (Old Owner)
                if (save.Country.TryGetValue(battlefield.CountryId, out var oldOwner))
                {
                    oldOwner.Prestige -= prestigeChange;
                    AddEffect(effects, "country", new Dictionary<string, object>
                    {
                        { "id", oldOwner.Id },
                        { "Prestige", $"-{prestigeChange}" }
                    });
                }

                // Winner
                if (save.Country.TryGetValue(winner.CountryId, out var winnerCountry))
                {
                    winnerCountry.Prestige += prestigeChange;
                    AddEffect(effects, "country", new Dictionary<string, object>
                    {
                        { "id", winnerCountry.Id },
                        { "Prestige", $"+{prestigeChange}" }
                    });

                    // Apply Relation Penalty
                    // 1. Full penalty to old owner
                    var ownerRel = save.Relation.FirstOrDefault(r =>
                        (r.SrcCountryId == battlefield.CountryId && r.DstCountryId == winner.CountryId) ||
                        (r.SrcCountryId == winner.CountryId && r.DstCountryId == battlefield.CountryId));
                    if (ownerRel != null) ownerRel.Value += relationPenalty;

                    // 2. Half penalty to others
                    foreach (var country in save.Country.Values)
                    {
                        if (country.Id == winner.CountryId || country.Id == battlefield.CountryId) continue;

                        var rel = save.Relation.FirstOrDefault(r =>
                            (r.SrcCountryId == country.Id && r.DstCountryId == winner.CountryId) ||
                            (r.SrcCountryId == winner.CountryId && r.DstCountryId == country.Id));

                        if (rel != null) rel.Value += relationPenalty / 2;
                    }
                }

                TerritoryService.Main.AnnexCommandery(winner.CountryId, battlefield.Id);

                AddEffect(effects, "commandery", new Dictionary<string, object> {
                    { "id", battlefield.Id },
                    { "CountryId", winner.CountryId },
                    { "Unrest", 10 },
                    { "Garrisons", 1000 }
                });
            }
        }

        private void CleanupNullArmies(SaveData save, BattleContext battle, List<string> narrativeParts)
        {
            var allArmies = battle.Attackers.Concat(battle.Defenders).ToList();
            foreach (var army in allArmies)
            {
                if (army.Size <= 0)
                {
                    if (army.CommanderId.HasValue)
                    {
                        if (_rng.Next(0, 100) < 50)
                        {
                            var p = save.Person.FirstOrDefault(x => x.Id == army.CommanderId);
                            if (p != null)
                            {
                                p.IsAlive = false;
                                p.History += $" Died in battle of {battle.Location.Name}.";
                                narrativeParts.Add($"{p.Name} has fallen in battle!");
                            }
                        }
                    }
                    save.Army.Remove(army);
                    narrativeParts.Add($"Army {army.Name} destroyed.");
                }
            }
        }

        private void ApplyPrestigeChange(SaveData save, IEnumerable<int> countryIds, int amount, Dictionary<string, List<Dictionary<string, object>>> effects)
        {
            if (amount == 0) return;
            foreach (var cid in countryIds)
            {
                ApplyPrestigeChange(save, cid, amount, effects);
            }
        }

        private void ApplyPrestigeChange(SaveData save, int countryId, int amount, Dictionary<string, List<Dictionary<string, object>>> effects)
        {
            if (save.Country.TryGetValue(countryId, out var c))
            {
                c.Prestige += amount;
                AddEffect(effects, "country", new Dictionary<string, object> { { "id", c.Id }, { "Prestige", (amount > 0 ? "+" : "") + amount } });
            }
        }

        private void AddEffect(Dictionary<string, List<Dictionary<string, object>>> effects, string table, Dictionary<string, object> data)
        {
            if (!effects.ContainsKey(table)) effects[table] = new List<Dictionary<string, object>>();
            effects[table].Add(data);
        }

        private GameEvent CreateEvent(string name, string desc, string summary, SaveData save, BattleContext battle, Dictionary<string, List<Dictionary<string, object>>> effects)
        {
            return new GameEvent
            {
                EventName = name,
                EventDesc = desc,
                EventCountry = battle.Defenders.FirstOrDefault()?.CountryId ?? battle.Location.CountryId,
                RelatedCountryIds = battle.Attackers.Select(a => a.CountryId)
                   .Concat(battle.Defenders.Select(d => d.CountryId))
                   .Append(battle.Location.CountryId)
                   .Distinct()
                   .ToList(),
                Outcomes = new List<EventOutcome>
                {
                    new EventOutcome
                    {
                        OutcomeName = "Result",
                        OutcomeDesc = summary,
                        Effects = effects
                    }
                }
            };
        }

        private async Task<(string name, string desc)> GenerateFlavorText(SaveData save, BattleContext battle, string changesSummary, string fallbackName)
        {
            if (GameService.Main != null && GameService.Main.UseMockEvents)
            {
                return (fallbackName, $"[MOCK EVENT] {changesSummary}");
            }

            // OPTIMIZATION: Skip LLM for AI vs AI battles to save costs/time
            int playerCid = save.Game.PlayerCountryId;
            bool isPlayerInvolved = battle.Location.CountryId == playerCid ||
                                    battle.Attackers.Any(a => a.CountryId == playerCid) ||
                                    battle.Defenders.Any(d => d.CountryId == playerCid);

            if (!isPlayerInvolved)
            {
                return (fallbackName, changesSummary);
            }

            // Gather commander IDs for People context
            var commanders = battle.Attackers
                .Where(a => a.CommanderId.HasValue)
                .Select(a => a.CommanderId.Value)
                .ToList();
            commanders.AddRange(battle.Defenders
                .Where(d => d.CommanderId.HasValue)
                .Select(d => d.CommanderId.Value));
            if (battle.Location.CommanderId.HasValue)
            {
                commanders.Add(battle.Location.CommanderId.Value);
            }

            // Build full context for LLM
            var context = new
            {
                Game = save.Game,
                Battle = battle.Data,
                Location = battle.Location,
                AttackerCountry = save.Country.TryGetValue(battle.Attackers.FirstOrDefault()?.CountryId ?? 0, out var atkCountry) ? atkCountry : null,
                DefenderCountry = save.Country.TryGetValue(battle.Location.CountryId, out var defCountry) ? defCountry : null,
                Attackers = battle.Attackers,
                Defenders = battle.Defenders,
                People = save.Person.Where(p => commanders.Contains(p.Id)).ToList(),
                Changes = changesSummary
            };

            var jsonContext = JsonConvert.SerializeObject(context, Formatting.Indented);
            var prompt = $"Context:\n{jsonContext}\n\nTask: Write a narrative description for this event based on the context and changes.";

            try
            {
                var response = await _llmService.AskTextAsync(prompt, _sysBattlePrompt);
                var content = response.Choices[0].Message.Content;

                // Try to parse if it returns JSON with event_name and event_desc
                try
                {
                    int start = content.IndexOf('{');
                    int end = content.LastIndexOf('}');
                    if (start != -1 && end != -1)
                    {
                        var json = content.Substring(start, end - start + 1);
                        var obj = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                        if (obj != null)
                        {
                            string eventName = obj.ContainsKey("event_name") ? obj["event_name"] : fallbackName;
                            string eventDesc = obj.ContainsKey("event_desc") ? obj["event_desc"] : content;
                            return (eventName, eventDesc);
                        }
                    }
                }
                catch { }

                return (fallbackName, content);
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorForService("BattleService", "Error generating flavor text", ex);
                return (fallbackName, changesSummary);
            }
        }

        private class BattleContext
        {
            public BattleData Data { get; set; }
            public CommanderyData Location { get; set; }
            public List<ArmyData> Attackers { get; set; }
            public List<ArmyData> Defenders { get; set; }
            public int OwnerId { get; set; }
        }
    }
}