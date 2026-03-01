using System;
using System.Collections.Generic;
using System.Linq;
using Game.Services.Commands;
using Game.Services.Events;
using Game.Services.Saves;
using UnityEngine;

namespace Game.Services
{
    public class AIService
    {
        private readonly CommanderyActionService _commanderyActionService;
        private readonly System.Random _rng = new System.Random();
        private readonly LoggingService _loggingService;
        private readonly DiplomacyService _diplomacyService;

        // Store pending diplomacy proposals keyed by a unique identifier
        private readonly Dictionary<string, DiplomacyService.DealProposal> _pendingProposals = new Dictionary<string, DiplomacyService.DealProposal>();
        private int _proposalCounter = 0;

        public AIService(CommanderyActionService commanderyActionService, LoggingService loggingService, DiplomacyService diplomacyService)
        {
            _commanderyActionService = commanderyActionService;
            _loggingService = loggingService;
            _diplomacyService = diplomacyService;
        }

        public List<GameEvent> ProcessAiTurns(SaveData saveRoot)
        {
            var diplomacyEvents = new List<GameEvent>();
            var npcCountries = saveRoot.Country.Values.Where(c => c.Id != saveRoot.Game.PlayerCountryId && c.Id != 99).ToList();
            _loggingService.LogForService("AIService", "Processing AI Turns...");

            foreach (var country in npcCountries)
            {
                bool isAtWar = IsCountryAtWar(saveRoot, country.Id);

                // 1. Diplomatic Actions
                var potentialTargets = saveRoot.Country.Values.Where(c => c.Id != country.Id).ToList();
                if (potentialTargets.Count > 0)
                {
                    if (_rng.NextDouble() < 0.3)
                    {
                        var strongestTarget = potentialTargets.OrderByDescending(t => GetMilitaryStrength(saveRoot, t.Id)).First();
                        var strongDiplomacy = PerformDiplomacy(saveRoot, country, strongestTarget);
                        if (strongDiplomacy != null)
                        {
                            diplomacyEvents.Add(strongDiplomacy);
                        }
                    }

                    var target = potentialTargets[_rng.Next(potentialTargets.Count)];
                    var diplomacyEvent = PerformDiplomacy(saveRoot, country, target);
                    if (diplomacyEvent != null)
                    {
                        diplomacyEvents.Add(diplomacyEvent);
                    }
                }

                // 2. Army Actions
                PerformArmyAction(saveRoot, country);

                // 3. Commandery Actions
                PerformCommanderyAction(saveRoot, country);

                // Extra AI cheating/buffs for flavor/difficulty
                var commanderies = saveRoot.Commandery.Values.Where(c => c.CountryId == country.Id).ToList();
                foreach (var commandery in commanderies)
                {
                    if (_rng.NextDouble() < 0.05)
                    {
                        commandery.Wealth += _rng.Next(1, 6);
                    }
                }
                if (isAtWar && country.Id == 6) // Special buff for country 6 
                {
                    country.Treasury += _rng.Next(1, 6);
                }
            }

            return diplomacyEvents;
        }

        private bool IsCountryAtWar(SaveData save, int countryId)
        {
            return save.Relation.Any(r => (r.SrcCountryId == countryId || r.DstCountryId == countryId) && r.IsAtWar);
        }

        private GameEvent PerformDiplomacy(SaveData save, CountryData country, CountryData target)
        {
            bool isBorder = country.BorderingCountryIds.Contains(target.Id);

            var rel = save.Relation.FirstOrDefault(r => (r.SrcCountryId == country.Id && r.DstCountryId == target.Id));
            bool isAtWar = rel != null && rel.IsAtWar;
            bool isAllied = rel != null && rel.IsAllied;
            int relValue = rel?.Value ?? 0;

            bool hasAllies = save.Relation.Any(r => (r.SrcCountryId == country.Id || r.DstCountryId == country.Id) && r.IsAllied);

            bool proposerAtWarWithOthers = IsCountryAtWar(save, country.Id) && !isAtWar;
            var deal = new DiplomacyService.DealProposal(country, target);

            // 1. Unilateral Actions (Execute Immediately)
            if (isAllied)
            {
                if (_rng.NextDouble() < (relValue <= 0 ? 0.2 : 0.05))
                {
                    deal.AddItem(true, DiplomacyService.DealItemType.BreakAlliance, 1);
                    return ProposeToTarget(save, deal, target);
                }
            }
            else if (!isAtWar && !proposerAtWarWithOthers)
            {
                // Declare War Check
                double myStrength = GetMilitaryStrength(save, country.Id);
                double targetStrength = GetMilitaryStrength(save, target.Id);

                // Add allies strength to target
                var targetAllies = save.Relation
                    .Where(r => r.SrcCountryId == target.Id && r.IsAllied)
                    .Select(r => r.DstCountryId)
                    .ToList();

                foreach (var allyId in targetAllies)
                {
                    targetStrength += GetMilitaryStrength(save, allyId);
                }

                bool targetIsAtWar = IsCountryAtWar(save, target.Id);
                double warChance = 0;
                warChance -= relValue * 0.005;
                if (myStrength > targetStrength) warChance += 0.3;
                if (targetIsAtWar) warChance += 0.5;

                // Gradual reduction based on manpower ratio (full chance at 12x income, zero at 0)
                double manpowerRatio = country.LastTurnManpowerIncome > 0
                    ? Math.Min(1.0, (double)country.Manpower / (country.LastTurnManpowerIncome * 120))
                    : 0;
                warChance *= manpowerRatio;

                if (_rng.NextDouble() < warChance)
                {
                    deal.AddItem(true, DiplomacyService.DealItemType.DeclareWar, 1);
                    return ProposeToTarget(save, deal, target);
                }
            }

            // 2. Bilateral Actions (Accumulate)

            if (isAtWar)
            {
                // Peace chance scales from 0.2 (at full manpower) to 1.0 (at 0 manpower)
                double peaceManpowerRatio = country.LastTurnManpowerIncome > 0
                    ? Math.Min(1.0, (double)country.Manpower / (country.LastTurnManpowerIncome * 120))
                    : 0;
                double peaceChance = 0.2 + (1.0 - peaceManpowerRatio) * 0.8;

                if (_rng.NextDouble() < peaceChance)
                {
                    deal.AddItem(true, DiplomacyService.DealItemType.Peace, 1);
                }
            }
            else if (proposerAtWarWithOthers)
            {
                // At war with someone else, ask for manpower from neutral/allied countries
                int requestAmount = country.LastTurnManpowerIncome * 3; // Request 3 months of manpower
                deal.AddItem(false, DiplomacyService.DealItemType.Manpower, requestAmount);
            }
            else if (!isAllied)
            {
                double allyChance = (hasAllies) ? 0.0 : 0.2;
                allyChance += relValue * 0.01;
                if (_rng.NextDouble() < allyChance)
                {
                    deal.AddItem(true, DiplomacyService.DealItemType.Alliance, 1);
                }
            }

            // 3. Evaluate and Propose if valid
            if (deal.Offers.Count > 0 || deal.Requests.Count > 0)
            {
                // Quick check - if already acceptable, propose immediately
                if (_diplomacyService.EvaluateDeal(deal, out _) > 0)
                {
                    return ProposeToTarget(save, deal, target);
                }

                // Add prestige to sweeten (capped at 10)
                deal.AddItem(true, DiplomacyService.DealItemType.Prestige, 10);
                if (_diplomacyService.EvaluateDeal(deal, out _) > 0)
                {
                    return ProposeToTarget(save, deal, target);
                }

                // Early exit if proposer already unhappy
                if (_diplomacyService.EvaluateDealForProposer(deal, out _) < -10)
                {
                    return null;
                }

                // Keep adding Gold until sweet spot is found
                int goldStep = country.LastTurnGoldIncome; // Add 1 month's income per step
                int maxGoldToOffer = country.Treasury / 4;  // Don't offer more than 25% of treasury
                int totalGoldOffered = 0;

                while (totalGoldOffered < maxGoldToOffer)
                {
                    deal.AddItem(true, DiplomacyService.DealItemType.Gold, goldStep);
                    totalGoldOffered += goldStep;

                    int proposerScore = _diplomacyService.EvaluateDealForProposer(deal, out _);
                    int targetScore = _diplomacyService.EvaluateDeal(deal, out _);

                    // Found sweet spot
                    if (proposerScore >= -10 && targetScore > 0)
                    {
                        return ProposeToTarget(save, deal, target);
                    }

                    // Proposer would lose too much, abort
                    if (proposerScore < -10)
                    {
                        return null;
                    }
                }

                // If gold wasn't enough, try offering manpower (only if we're not requesting it)
                bool alreadyRequestingManpower = deal.Requests.Any(r => r.Type == DiplomacyService.DealItemType.Manpower);
                if (!alreadyRequestingManpower && country.Manpower > country.LastTurnManpowerIncome * 12)
                {
                    int manpowerStep = country.LastTurnManpowerIncome; // Offer 1 month's income per step
                    int maxManpowerToOffer = country.Manpower / 4;     // Don't offer more than 25% of manpower
                    int totalManpowerOffered = 0;

                    while (totalManpowerOffered < maxManpowerToOffer)
                    {
                        deal.AddItem(true, DiplomacyService.DealItemType.Manpower, manpowerStep);
                        totalManpowerOffered += manpowerStep;

                        int proposerScore = _diplomacyService.EvaluateDealForProposer(deal, out _);
                        int targetScore = _diplomacyService.EvaluateDeal(deal, out _);

                        // Found sweet spot
                        if (proposerScore >= -10 && targetScore > 0)
                        {
                            return ProposeToTarget(save, deal, target);
                        }

                        // Proposer would lose too much, abort
                        if (proposerScore < -10)
                        {
                            return null;
                        }
                    }
                }
            }

            return null;
        }

        private double GetMilitaryStrength(SaveData save, int countryId)
        {
            if (!save.Country.TryGetValue(countryId, out var country)) return 0;

            double strength = country.Manpower / 2;
            var armies = save.Army.Where(a => a.CountryId == countryId).ToList();
            foreach (var army in armies)
            {
                strength += army.Size;
            }
            return strength;
        }

        private GameEvent ProposeToTarget(SaveData save, DiplomacyService.DealProposal deal, CountryData target)
        {
            if (target.Id == save.Game.PlayerCountryId)
            {
                // Create a diplomacy event for the player to decide
                return CreateDiplomacyEvent(save, deal);
            }
            else
            {
                // AI receiver evaluates the deal
                int score = _diplomacyService.EvaluateDeal(deal, out string reason);
                if (score > 0) // Receiver only accepts if deal is fair/favorable
                {
                    _diplomacyService.ExecuteDeal(deal);
                    _loggingService.LogForService("AIService", $"AI {target.Name} accepted deal from {deal.Player.Name} (score: {score})");
                }
                else
                {
                    _loggingService.LogForService("AIService", $"AI {target.Name} rejected deal from {deal.Player.Name} (score: {score}, {reason})");
                }
                return null;
            }
        }

        private GameEvent CreateDiplomacyEvent(SaveData save, DiplomacyService.DealProposal deal)
        {
            // Generate a unique proposal ID
            string proposalId = $"diplomacy_{_proposalCounter++}_{deal.Player.Id}_{deal.Target.Id}";

            // Determine the type of proposal
            string proposalType = "";
            string proposalDescription = "";
            bool isUnilateral = false;

            if (deal.ContainsWarDeclaration)
            {
                proposalType = "War Declaration";
                proposalDescription = $"{deal.Player.Name} has declared war on our nation!";
                isUnilateral = true;
            }
            else if (deal.ContainsBreakAlliance)
            {
                proposalType = "Alliance Broken";
                proposalDescription = $"{deal.Player.Name} has broken their alliance with us.";
                isUnilateral = true;
            }
            else if (deal.HasItem(DiplomacyService.DealItemType.Peace))
            {
                proposalType = "Peace Treaty";
                proposalDescription = $"{deal.Player.Name} proposes a peace treaty to end the war between our nations.";
            }
            else if (deal.HasItem(DiplomacyService.DealItemType.Alliance))
            {
                proposalType = "Alliance Proposal";
                proposalDescription = $"{deal.Player.Name} proposes to form an alliance with our nation.";
            }
            else
            {
                // Generic deal
                var offerTypes = string.Join(", ", deal.Offers.Select(o => o.Type.ToString()));
                var requestTypes = string.Join(", ", deal.Requests.Select(r => r.Type.ToString()));
                proposalType = "Diplomatic Proposal";
                proposalDescription = $"{deal.Player.Name} proposes a deal. They offer: {(string.IsNullOrEmpty(offerTypes) ? "Nothing" : offerTypes)}. They request: {(string.IsNullOrEmpty(requestTypes) ? "Nothing" : requestTypes)}.";
            }

            _loggingService.LogForService("AIService", $"Creating diplomacy event: {proposalType} from {deal.Player.Name} to {deal.Target.Name}");

            // For unilateral actions, execute the deal immediately - the event is just a notification
            if (isUnilateral)
            {
                _diplomacyService.ExecuteDeal(deal);
                _loggingService.LogForService("AIService", $"Unilateral action executed: {proposalType} from {deal.Player.Name} to {deal.Target.Name}");
            }
            else
            {
                // Only store pending proposal for bilateral deals that need player decision
                _pendingProposals[proposalId] = deal;
            }

            var choices = new List<EventChoice>();

            if (isUnilateral)
            {
                // For unilateral actions, the "Choice" is just acknowledgement - deal already executed above
                choices.Add(new EventChoice
                {
                    ChoiceName = "Acknowledge",
                    ChoiceDesc = $"Acknowledge the {proposalType.ToLower()}.",
                    Effects = new Dictionary<string, List<Dictionary<string, object>>>() // No effects needed, already executed
                });
            }
            else
            {
                choices.Add(new EventChoice
                {
                    ChoiceName = "Accept",
                    ChoiceDesc = $"Accept the {proposalType.ToLower()} from {deal.Player.Name}.",
                    Effects = CreateAcceptEffects(save, deal, proposalId)
                });
                choices.Add(new EventChoice
                {
                    ChoiceName = "Reject",
                    ChoiceDesc = $"Reject the {proposalType.ToLower()} from {deal.Player.Name}.",
                    Effects = CreateRejectEffects(save, deal, proposalId)
                });
            }

            // Create the event
            var gameEvent = new GameEvent
            {
                EventName = $"{proposalType} from {deal.Player.Name}",
                EventDesc = proposalDescription,
                EventCountry = deal.Target.Id, // Player's country
                RelatedCountryIds = new List<int> { deal.Player.Id, deal.Target.Id },
                Choices = choices
            };

            return gameEvent;
        }

        private Dictionary<string, List<Dictionary<string, object>>> CreateAcceptEffects(SaveData save, DiplomacyService.DealProposal deal, string proposalId)
        {
            // Effects are empty - the actual deal execution is handled by HandleDiplomacyChoice
            // which calls DiplomacyService.ExecuteDeal to properly update relations, alliances, etc.
            return new Dictionary<string, List<Dictionary<string, object>>>();
        }

        private Dictionary<string, List<Dictionary<string, object>>> CreateRejectEffects(SaveData save, DiplomacyService.DealProposal deal, string proposalId)
        {
            // Effects are empty - the relation penalty for rejection is handled by HandleDiplomacyChoice
            return new Dictionary<string, List<Dictionary<string, object>>>();
        }

        /// <summary>
        /// Handles the player's choice for a diplomacy event.
        /// This should be called after the player makes a choice to execute the actual deal.
        /// </summary>
        public void HandleDiplomacyChoice(SaveData save, GameEvent gameEvent, EventChoice choice)
        {
            // Find the proposal by matching the event name pattern
            var matchingProposal = _pendingProposals.FirstOrDefault(kvp =>
                gameEvent.EventName.Contains(kvp.Value.Player.Name));

            if (matchingProposal.Value != null)
            {
                var deal = matchingProposal.Value;

                if (choice.ChoiceName == "Accept")
                {
                    // Execute the deal
                    _diplomacyService.ExecuteDeal(deal);
                    _loggingService.LogForService("AIService", $"Player accepted deal from {deal.Player.Name}");
                }
                else
                {
                    // Apply relation penalty for rejection
                    var relation = save.Relation.FirstOrDefault(r =>
                        (r.SrcCountryId == deal.Player.Id && r.DstCountryId == deal.Target.Id));

                    if (relation != null)
                    {
                        relation.Value -= 5;
                    }

                    _loggingService.LogForService("AIService", $"Player rejected deal from {deal.Player.Name}");
                }

                // Remove the pending proposal
                _pendingProposals.Remove(matchingProposal.Key);
            }
        }

        private void PerformCommanderyAction(SaveData save, CountryData country)
        {
            var commanderies = save.Commandery.Values.Where(c => c.CountryId == country.Id).ToList();

            if (country.LastTurnGarrisonUpkeep > country.LastTurnGoldIncome / 4.0)
            {
                foreach (var location in commanderies)
                {
                    bool isBorder = location.Neighbors
                        .Select(nid => save.Commandery.ContainsKey(nid) ? save.Commandery[nid] : null)
                        .Any(n => n != null && n.CountryId != country.Id);
                    if (!isBorder && _rng.NextDouble() < 0.5)
                    {
                        _commanderyActionService.ExecuteDecreaseGarrison(save, country, location, 1000);
                    }
                    else if (location.Garrisons * commanderies.Count * GameConstants.GarrisonUpkeep > country.LastTurnGarrisonUpkeep && _rng.NextDouble() < 0.5)
                    {
                        _commanderyActionService.ExecuteDecreaseGarrison(save, country, location, 1000);
                    }
                }
            }

            if (country.Treasury > country.LastTurnGoldIncome * 12 && country.Manpower > 5000)
            {
                foreach (var location in commanderies)
                {
                    bool isBorder = location.Neighbors
                        .Select(nid => save.Commandery.ContainsKey(nid) ? save.Commandery[nid] : null)
                        .Any(n => n != null && n.CountryId != country.Id);
                    if (isBorder && _rng.NextDouble() < (location.Defensiveness / 10.0))
                    {
                        _commanderyActionService.ExecuteIncreaseGarrison(save, country, location, 1000);
                    }
                }
            }
            var lowGarrisons = commanderies.Where(c => c.Garrisons < 10000).ToList();
            if (lowGarrisons.Count > 0)
            {
                var location = lowGarrisons[_rng.Next(lowGarrisons.Count)];
                _commanderyActionService.ExecuteIncreaseGarrison(save, country, location, 1000);
            }
        }

        private void PerformArmyAction(SaveData save, CountryData country)
        {
            // Find foreign armies in our territory
            var foreignArmiesInTerritory = save.Army
                .Where(a => a.CountryId != country.Id &&
                            save.Commandery.TryGetValue(a.LocationId, out var loc) &&
                            loc.CountryId == country.Id)
                .ToList();

            var biggestForeignArmy = foreignArmiesInTerritory.OrderByDescending(a => a.Size).FirstOrDefault();

            // 0. Assign Commanders
            var armiesWithoutCommanders = save.Army.Where(a => a.CountryId == country.Id && a.CommanderId == null).ToList();
            if (armiesWithoutCommanders.Any())
            {
                var existingCommanderIds = save.Army.Where(a => a.CommanderId.HasValue).Select(a => a.CommanderId.Value).ToHashSet();
                var commanderyCommanderIds = save.Commandery.Values.Where(c => c.CommanderId.HasValue).Select(c => c.CommanderId.Value).ToHashSet();
                var eligiblePeople = save.Person
                    .Where(p => p.CountryId == country.Id && p.IsAlive && !existingCommanderIds.Contains(p.Id) && !commanderyCommanderIds.Contains(p.Id))
                    .OrderByDescending(p => (p.Stats.Morale ?? 0) + (p.Stats.FieldOffense ?? 0) +
                                            (p.Stats.FieldDefense ?? 0) + (p.Stats.SiegeOffense ?? 0) +
                                            (p.Stats.SiegeDefense ?? 0))
                    .ToList();

                int pIndex = 0;
                foreach (var army in armiesWithoutCommanders)
                {
                    if (pIndex >= eligiblePeople.Count) break;
                    army.CommanderId = eligiblePeople[pIndex].Id;
                    pIndex++;
                }
            }

            // 1.1 Recruit
            var armies = save.Army.Where(a => a.CountryId == country.Id).ToList();
            if (armies.Count == 0 && country.Manpower > 10000 && country.Treasury > 10000 / GameConstants.TroopsPerGold)
            {
                var commanderies = save.Commandery.Values.Where(c => c.CountryId == country.Id).ToList();
                if (commanderies.Any())
                {
                    var loc = commanderies[_rng.Next(commanderies.Count)];
                    int size = Math.Min(country.Manpower / 2, 10000);
                    ArmyCommands.CreateArmy(country.Id, loc.Id, $"{country.Name} Army", size);
                }
                return;
            }

            // 1.2 Resupply
            foreach (var army in armies)
            {
                if (army.Size <= 0) continue;
                if (army.ActionLeft <= 0) continue;
                if (army.Supply <= 90)
                {
                    ArmyCommands.ResupplyArmy(army.Id, 100 - army.Supply);
                }
            }

            // 1.3 Resize
            if (!IsCountryAtWar(save, country.Id) && country.LastTurnArmyUpkeep > country.LastTurnGoldIncome / 2.0)
            {
                foreach (var army in armies)
                {
                    if (army.Size <= 0 || army.ActionLeft <= 0) continue;
                    if (army.Size > 10000)
                    {
                        int size = Math.Max(army.Size / 4, 10000);
                        ArmyCommands.DecreaseArmy(army.Id, size);
                    }
                }
            }
            else if (!IsCountryAtWar(save, country.Id) && country.LastTurnArmyUpkeep < country.LastTurnGoldIncome / 4.0 && country.Treasury > country.LastTurnGoldIncome * 12)
            {
                foreach (var army in armies)
                {
                    if (army.Size <= 0 || army.ActionLeft <= 0) continue;
                    int manIncrease = Math.Min(country.Manpower, 10000);
                    ArmyCommands.IncreaseArmy(army.Id, manIncrease);
                }
            }
            else if (IsCountryAtWar(save, country.Id))
            {
                if (country.LastTurnArmyUpkeep + country.LastTurnGarrisonUpkeep < country.LastTurnGoldIncome * 0.9 && country.Treasury > country.LastTurnGoldIncome * 12)
                {
                    bool increased = false;
                    foreach (var army in armies)
                    {
                        if (army.ActionLeft <= 0) continue;
                        if (biggestForeignArmy != null && army.Size >= biggestForeignArmy.Size) continue;
                        int maximumIncrease = Math.Max((int)((double)country.Manpower * (100 + country.Prestige) / 1000), 10000);
                        ArmyCommands.IncreaseArmy(army.Id, maximumIncrease);
                        increased = true;
                    }
                    if (increased == false)
                    {
                        var commanderies = save.Commandery.Values.Where(c => c.CountryId == country.Id).ToList();
                        if (commanderies.Any())
                        {
                            var loc = commanderies[_rng.Next(commanderies.Count)];
                            int size = Math.Min(country.Manpower / 2, 10000);
                            ArmyCommands.CreateArmy(country.Id, loc.Id, $"{country.Name} Army", size);
                            increased = true;
                        }
                    }
                }
            }

            // 2. Move Armies
            foreach (var army in armies)
            {
                if (army.Size <= 0 || army.ActionLeft <= 0) continue;

                int maxMorale = 90 + (int)(country.Prestige / 10.0);
                if (army.CommanderId.HasValue)
                {
                    var commander = save.Person.FirstOrDefault(c => c.Id == army.CommanderId);
                    if (commander != null)
                    {
                        maxMorale += 2 * (commander.Stats.Morale ?? 0);
                    }
                }
                if (maxMorale < 10) maxMorale = 10;

                bool inBattle = save.Battle.Any(b => b.AttackerArmyIds.Contains(army.Id) || b.DefenderArmyIds.Contains(army.Id));

                if (inBattle && army.Morale < 30)
                {
                    // Retreat logic
                    if (save.Commandery.TryGetValue(army.LocationId, out var currentCol) && currentCol.Neighbors != null)
                    {
                        var neighbors = currentCol.Neighbors.Select(nid => save.Commandery.ContainsKey(nid) ? save.Commandery[nid] : null).Where(c => c != null).ToList();
                        var safeLoc = neighbors.FirstOrDefault(n =>
                            n.CountryId == country.Id &&
                            !save.Battle.Any(b => b.LocationId == n.Id));

                        if (safeLoc != null)
                        {
                            var battle = save.Battle.FirstOrDefault(b => b.LocationId == army.LocationId);
                            ArmyCommands.Retreat(army.Id, battle.Id);
                            ArmyCommands.MoveArmy(army.Id, safeLoc.Id, ignoreBattle: true);
                            continue;
                        }
                    }
                }

                if (inBattle) continue;

                if (army.Morale < maxMorale) continue;

                var enemyIds = save.Relation
                   .Where(r => (r.SrcCountryId == country.Id || r.DstCountryId == country.Id) && r.IsAtWar)
                   .Select(r => r.SrcCountryId == country.Id ? r.DstCountryId : r.SrcCountryId)
                   .ToList();

                if (save.Commandery.TryGetValue(army.LocationId, out var currentLocation) && currentLocation.Neighbors != null)
                {
                    var allNeighbors = currentLocation.Neighbors.Select(nid => save.Commandery.ContainsKey(nid) ? save.Commandery[nid] : null).Where(c => c != null).ToList();

                    if (enemyIds.Any())
                    {
                        var enemyNeighbor = allNeighbors.FirstOrDefault(n => enemyIds.Contains(n.CountryId));
                        if (enemyNeighbor != null)
                        {
                            ArmyCommands.MoveArmy(army.Id, enemyNeighbor.Id);
                        }
                        else
                        {
                            var randomNeighbor = allNeighbors[_rng.Next(allNeighbors.Count)];
                            ArmyCommands.MoveArmy(army.Id, randomNeighbor.Id);
                        }
                    }
                    else
                    {
                        var friendlyNeighbors = allNeighbors.Where(n => n.CountryId == country.Id).ToList();
                        if (friendlyNeighbors.Any())
                        {
                            var target = friendlyNeighbors[_rng.Next(friendlyNeighbors.Count)];
                            ArmyCommands.MoveArmy(army.Id, target.Id);
                        }
                    }
                }
            }
        }
    }
}