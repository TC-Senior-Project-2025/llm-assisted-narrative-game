using System;
using System.Collections.Generic;
using System.Linq;
using Game.Services.Saves;
using Game.World.Map;
using R3;

namespace Game.Services
{
    public class DiplomacyService
    {
        public static DiplomacyService Main { get; private set; }

        private readonly ReactiveProperty<SaveData> _state;
        private readonly TerritoryService _territoryService;
        private readonly MapService _mapService;
        private readonly LoggingService _logging = new();

        public DiplomacyService(ReactiveProperty<SaveData> state, TerritoryService territoryService, MapService mapService)
        {
            _state = state;
            _territoryService = territoryService;
            _mapService = mapService;
            Main = this;
        }

        public List<CountryData> GetEnemyCountries(int countryId)
        {
            var save = _state.CurrentValue;

            var enemyIds = save.Relation
                .Where(r => r.SrcCountryId == countryId && r.IsAtWar)
                .Select(r => r.DstCountryId)
                .Distinct()
                .ToList();

            return enemyIds
                .Where(id => save.Country.ContainsKey(id))
                .Select(id => save.Country[id])
                .ToList();
        }

        public int EvaluateDeal(DealProposal deal, out string reason)
        {
            var save = _state.CurrentValue;

            // Score > 0 means accept.
            int score = 0;
            reason = "";

            var rel = GetRelation(save, deal.Target.Id, deal.Player.Id);
            int relationVal = rel?.Value ?? 0;
            bool HaveOffer = false, HaveRequest = false;

            // 1. Calculate Offer Value (Positive score)
            foreach (var item in deal.Offers)
            {
                int prescore = score;
                score += GetItemValue(save, item, deal.Player, deal.Target);
                HaveOffer = (score != prescore);
            }

            // 2. Calculate Request Cost (Negative score)
            foreach (var item in deal.Requests)
            {
                int prescore = score;
                score -= GetItemCost(save, item, deal.Target, deal.Player);
                HaveRequest = (score != prescore);
            }

            // If asking for something significant (Alliance/CallToArms), apply reluctance
            bool significantRequest = deal.Requests.Any(r => r.Type == DealItemType.Alliance || r.Type == DealItemType.CallToArms);

            // 3. Base Reluctance / Relation and prestige modifier
            if ((HaveOffer && HaveRequest) || significantRequest)
            {
                score += relationVal + (deal.Player.Prestige - deal.Target.Prestige) / 2;
            }

            if (significantRequest)
            {
                int reluctance = 0;
                if (relationVal < 0) reluctance += Math.Abs(relationVal); // If relations are bad, more reluctance

                score -= reluctance;
                reason += $"Reluctance: -{reluctance} ";
            }

            // Peace treaty doesn't care about relations
            if (deal.Requests.Any(r => r.Type == DealItemType.Peace))
            {
                score -= relationVal;
            }

            // Debug reason
            reason += $"(Rel: {relationVal})";

            return score;
        }

        /// <summary>
        /// Evaluates the deal from the Proposer's perspective (how the proposer would feel if they received this deal).
        /// </summary>
        public int EvaluateDealForProposer(DealProposal deal, out string reason)
        {
            // Create a mirrored deal: swap Player/Target, swap Offers/Requests
            var mirroredDeal = new DealProposal(deal.Target, deal.Player);
            foreach (var item in deal.Offers)
                mirroredDeal.AddItem(false, item.Type, item.Value, item.Name); // Offers become Requests
            foreach (var item in deal.Requests)
                mirroredDeal.AddItem(true, item.Type, item.Value, item.Name); // Requests become Offers

            return EvaluateDeal(mirroredDeal, out reason);
        }

        public bool TryAcceptDeal(DealProposal deal, out string rejectionReason)
        {
            int score = EvaluateDeal(deal, out string reason);
            if (score > 0)
            {
                rejectionReason = null;
                ExecuteDeal(deal);
                return true;
            }
            else
            {
                rejectionReason = "The other party refused this deal. (Score: " + score + ", Reason: " + reason + ")";
                return false;
            }
        }

        public void ExecuteDeal(DealProposal deal, List<CountryData> targetAllies = null)
        {
            var save = _state.CurrentValue;

            // 1. Unilateral Actions
            if (deal.ContainsWarDeclaration)
            {
                SetWar(save, deal.Player.Id, deal.Target.Id);

                if (targetAllies != null && targetAllies.Any())
                {
                    foreach (var ally in targetAllies)
                        SetWar(save, deal.Player.Id, ally.Id);
                }

                return;
            }

            if (deal.ContainsBreakAlliance)
            {
                var r = GetOrCreateRelation(save, deal.Player.Id, deal.Target.Id);
                var r2 = GetOrCreateRelation(save, deal.Target.Id, deal.Player.Id);
                r.IsAllied = false;
                r2.IsAllied = false;
                r.History += $" Alliance broken in {save.Game.CurrentYear}.";
                r2.History += $" Alliance broken in {save.Game.CurrentYear}.";
                r2.Value += GameConstants.BreakAllianceRelationPenalty;

                var fromCommanderies = new HashSet<int>(
                    save.Commandery.Values
                        .Where(c => c.CountryId == deal.Player.Id)
                        .Select(c => c.Id)
                );

                var toCommanderies = new HashSet<int>(
                    save.Commandery.Values
                        .Where(c => c.CountryId == deal.Target.Id)
                        .Select(c => c.Id)
                );

                var fromArmiesInForeignLand = save.Army.Where(a => a.CountryId == deal.Player.Id && toCommanderies.Contains(a.LocationId));
                var fromRandomCommandery = save.Commandery.Values.FirstOrDefault(c => c.CountryId == deal.Player.Id);

                var toArmiesInForeignLand = save.Army.Where(a => a.CountryId == deal.Target.Id && fromCommanderies.Contains(a.LocationId));
                var toRandomCommandery = save.Commandery.Values.FirstOrDefault(c => c.CountryId == deal.Target.Id);

                foreach (var army in fromArmiesInForeignLand)
                {
                    army.LocationId = fromRandomCommandery.Id;
                }

                foreach (var army in toArmiesInForeignLand)
                {
                    army.LocationId = toRandomCommandery.Id;
                }

                if (deal.Player.Id == GameService.Main.PlayerCountry.Id || deal.Target.Id == GameService.Main.PlayerCountry.Id)
                {
                    _territoryService.UpdateFogOfWar(GameService.Main.PlayerCountry.Id);
                }

                return;
            }

            // Apply Relation Impact (Generosity)
            int offerValue = 0;
            foreach (var item in deal.Offers) offerValue += GetItemValue(save, item, deal.Player, deal.Target);

            int requestCost = 0;
            foreach (var item in deal.Requests) requestCost += GetItemCost(save, item, deal.Target, deal.Player);

            int relationImpact = offerValue - requestCost;

            var rel = GetOrCreateRelation(save, deal.Target.Id, deal.Player.Id);
            rel.Value += Math.Min(relationImpact, 20);

            // 2. Transfer Offers (Player -> Target)
            foreach (var item in deal.Offers) ApplyTransfer(save, item, deal.Player, deal.Target);

            // 3. Transfer Requests (Target -> Player)
            foreach (var item in deal.Requests) ApplyTransfer(save, item, deal.Target, deal.Player);

            // 4. Apply Pacts
            bool alliance = deal.HasItem(DealItemType.Alliance);
            bool peace = deal.HasItem(DealItemType.Peace);

            if (alliance) SetAlliance(save, deal.Player.Id, deal.Target.Id);
            if (peace) SetPeace(save, deal.Player.Id, deal.Target.Id);

            // Call to Arms
            var cta = deal.Requests.FirstOrDefault(r => r.Type == DealItemType.CallToArms);
            if (cta != null)
                SetWar(save, deal.Target.Id, cta.Value);

            var ctaOffer = deal.Offers.FirstOrDefault(r => r.Type == DealItemType.CallToArms);
            if (ctaOffer != null)
                SetWar(save, deal.Player.Id, ctaOffer.Value);
        }

        public (RelationData, RelationData) GetRelations(int srcCountryId, int dstCountryId)
        {
            var incoming = _state.CurrentValue.Relation.Single(r => r.DstCountryId == srcCountryId && r.SrcCountryId == dstCountryId);
            var outgoing = _state.CurrentValue.Relation.Single(r => r.SrcCountryId == srcCountryId && r.DstCountryId == dstCountryId);
            return (incoming, outgoing);
        }

        private void ApplyTransfer(SaveData save, DealItem item, CountryData from, CountryData to)
        {
            if (item.Type == DealItemType.Gold)
            {
                from.Treasury -= item.Value;
                to.Treasury += item.Value;
            }
            else if (item.Type == DealItemType.Manpower)
            {
                from.Manpower -= item.Value;
                to.Manpower += item.Value;
            }
            else if (item.Type == DealItemType.Prestige)
            {
                from.Prestige -= item.Value;
                to.Prestige += item.Value;
            }
            else if (item.Type == DealItemType.Commandery)
            {
                if (save.Commandery.TryGetValue(item.Value, out var com) && com.CountryId == from.Id)
                {
                    com.History += $" Traded to {to.Name} by {from.Name} in {save.Game.CurrentYear}.";
                    com.CountryId = to.Id;

                    var armiesInCommandery = save.Army.Where(a => a.LocationId == com.Id);
                    var randomCommandery = save.Commandery.Values.FirstOrDefault(c => c.CountryId == from.Id);

                    foreach (var army in armiesInCommandery)
                    {
                        army.LocationId = randomCommandery.Id;
                    }

                    GameMap.Main.BorderRenderer.Render();
                    _territoryService.UpdateFogOfWar(GameService.Main.PlayerCountry.Id);
                    _mapService.UpdateBorderStatus(save); // Update border caches
                }
            }
        }

        private static bool IsAtWar(SaveData save, int c1, int c2)
        {
            var r = GetRelation(save, c1, c2);
            return r != null && r.IsAtWar;
        }

        private void SetWar(SaveData save, int c1, int c2)
        {
            var r = GetOrCreateRelation(save, c1, c2);
            var r2 = GetOrCreateRelation(save, c2, c1);
            r.IsAtWar = true; r.IsAllied = false;
            r2.IsAtWar = true; r2.IsAllied = false;
            if (!r.History.Contains("War declared")) r.History += $" War declared in {save.Game.CurrentYear}.";
            if (!r2.History.Contains("War declared")) r2.History += $" War declared in {save.Game.CurrentYear}.";
            r.Value += GameConstants.WarRelationPenalty;
            r2.Value += GameConstants.WarRelationPenalty;

            var country1 = save.Country.TryGetValue(c1, out var c1Data) ? c1Data.Name : c1.ToString();
            var country2 = save.Country.TryGetValue(c2, out var c2Data) ? c2Data.Name : c2.ToString();
            _logging.LogForService("DiplomacyService", $"War declared: {country1} vs {country2}");
        }

        private void SetPeace(SaveData save, int c1, int c2)
        {
            var r = GetOrCreateRelation(save, c1, c2);
            var r2 = GetOrCreateRelation(save, c2, c1);
            r.IsAtWar = false;
            r2.IsAtWar = false;
            r.History += $" Peace signed in {save.Game.CurrentYear}.";
            r2.History += $" Peace signed in {save.Game.CurrentYear}..";
            r.Value += GameConstants.PeaceRelationBonus;
            r2.Value += GameConstants.PeaceRelationBonus;

            var country1 = save.Country.TryGetValue(c1, out var c1Data) ? c1Data.Name : c1.ToString();
            var country2 = save.Country.TryGetValue(c2, out var c2Data) ? c2Data.Name : c2.ToString();
            _logging.LogForService("DiplomacyService", $"Peace signed: {country1} and {country2}");
        }

        private void SetAlliance(SaveData save, int c1, int c2)
        {
            var r = GetOrCreateRelation(save, c1, c2);
            var r2 = GetOrCreateRelation(save, c2, c1);
            r.IsAllied = true; r.IsAtWar = false;
            r2.IsAllied = true; r2.IsAtWar = false;
            r.History += $" Alliance formed in {save.Game.CurrentYear}.";
            r2.History += $" Alliance formed in {save.Game.CurrentYear}.";
            r.Value += GameConstants.AllianceRelationBonus;
            r2.Value += GameConstants.AllianceRelationBonus;

            var country1 = save.Country.TryGetValue(c1, out var c1Data) ? c1Data.Name : c1.ToString();
            var country2 = save.Country.TryGetValue(c2, out var c2Data) ? c2Data.Name : c2.ToString();
            _logging.LogForService("DiplomacyService", $"Alliance formed: {country1} and {country2}");

            if (c1 == GameService.Main.PlayerCountry.Id || c2 == GameService.Main.PlayerCountry.Id)
            {
                _territoryService.UpdateFogOfWar(GameService.Main.PlayerCountry.Id);
            }
        }

        private static RelationData GetRelation(SaveData save, int src, int dst)
        {
            return save.Relation.FirstOrDefault(r => r.SrcCountryId == src && r.DstCountryId == dst);
        }

        private static RelationData GetOrCreateRelation(SaveData save, int src, int dst)
        {
            var rel = GetRelation(save, src, dst);
            if (rel == null)
            {
                rel = new RelationData { SrcCountryId = src, DstCountryId = dst, Value = 0, History = "" };
                save.Relation.Add(rel);
            }
            return rel;
        }

        private static int GetItemValue(SaveData save, DealItem item, CountryData giver, CountryData receiver)
        {
            switch (item.Type)
            {
                case DealItemType.Gold:
                    double goldValue;
                    if (receiver.Treasury >= 12 * receiver.LastTurnGoldIncome)
                        goldValue = 2 * item.Value / Math.Max(1, receiver.LastTurnGoldIncome);
                    else if (item.Value > (12 * receiver.LastTurnGoldIncome - receiver.Treasury))
                    {
                        goldValue = 4 * (12 * receiver.LastTurnGoldIncome - receiver.Treasury) / Math.Max(1, receiver.LastTurnGoldIncome);
                        goldValue += 2 * (item.Value - (12 * receiver.LastTurnGoldIncome - receiver.Treasury)) / Math.Max(1, receiver.LastTurnGoldIncome);
                    }
                    else
                    {
                        goldValue = 4 * item.Value / Math.Max(1, receiver.LastTurnGoldIncome);
                    }

                    return (int)goldValue; // monthly income = 4 Point // but more than yearly income = 2 Point
                case DealItemType.Manpower:
                    double manValue;
                    if (receiver.Manpower >= 12 * receiver.LastTurnManpowerIncome)
                        manValue = 2 * item.Value / Math.Max(1, receiver.LastTurnManpowerIncome);
                    else if (item.Value > (12 * receiver.LastTurnManpowerIncome - receiver.Manpower))
                    {
                        manValue = 4 * (12 * receiver.LastTurnManpowerIncome - receiver.Manpower) / Math.Max(1, receiver.LastTurnManpowerIncome);
                        manValue += 2 * (item.Value - (12 * receiver.LastTurnManpowerIncome - receiver.Manpower)) / Math.Max(1, receiver.LastTurnManpowerIncome);
                    }
                    else
                    {
                        manValue = 4 * item.Value / Math.Max(1, receiver.LastTurnManpowerIncome);
                    }
                    return (int)manValue; // monthly income = 4 Point // but more than yearly income = 2 Point
                case DealItemType.Prestige:
                    int prestigevalue;
                    if (receiver.Prestige > 75)
                    {
                        prestigevalue = 1;
                    }
                    else if (receiver.Prestige > 50)
                    {
                        prestigevalue = 2;
                    }
                    else if (receiver.Prestige > 25)
                    {
                        prestigevalue = 4;
                    }
                    else
                    {
                        prestigevalue = 5;
                    }
                    return item.Value * prestigevalue; // Depends on receiver prestige
                case DealItemType.Commandery:
                    var commanderyItem = save.Commandery.Values.FirstOrDefault(c => c.Id == item.Value);
                    if (commanderyItem == null) return 0;
                    double commanderyValue = (long)commanderyItem.Population * commanderyItem.Wealth / 10000;
                    return (int)(commanderyValue); // base 5 years income
                case DealItemType.Alliance:
                    return GetPactDesire(save, receiver, giver, DealItemType.Alliance);
                case DealItemType.Peace:
                    return GetPactDesire(save, receiver, giver, DealItemType.Peace);
                case DealItemType.CallToArms:
                    var enemy = save.Country.Values.FirstOrDefault(c => c.Id == item.Value);
                    if (enemy == null) return 0;
                    double enemyStrength = enemy.Manpower / 2;
                    double giverStrength = giver.Manpower / 2;
                    double receiverStrength = receiver.Manpower / 2;
                    var enemyArmies = save.Army
                        .Where(c => c.CountryId == enemy.Id)
                        .ToList();
                    foreach (var army in enemyArmies)
                    {
                        enemyStrength += army.Size;
                    }
                    var giverArmies = save.Army
                        .Where(c => c.CountryId == giver.Id)
                        .ToList();
                    foreach (var army in giverArmies)
                    {
                        giverStrength += army.Size;
                    }
                    var receiverArmies = save.Army
                        .Where(c => c.CountryId == receiver.Id)
                        .ToList();
                    foreach (var army in receiverArmies)
                    {
                        receiverStrength += army.Size;
                    }
                    if (enemyStrength == 0 && receiverStrength == 0)
                    {
                        return 100;
                    }
                    else
                    {
                        return (int)(100 * enemyStrength * giverStrength / ((receiverStrength + enemyStrength) * (receiverStrength + giverStrength)));
                    }
                default: return 0;
            }
        }

        private static int GetItemCost(SaveData save, DealItem item, CountryData giver, CountryData receiver)
        {
            switch (item.Type)
            {
                case DealItemType.Gold:
                    double goldValue = 0;
                    if (giver.LastTurnGoldIncome == 0) return 0; // Avoid divide by zero
                    int income = Math.Max(1, giver.LastTurnGoldIncome);
                    int i = 1;
                    // Calculate cost for full years
                    for (i = 1; (long)i * income * 12 <= item.Value; i++)
                    {
                        goldValue += 12 * Math.Pow(2, i);
                    }
                    // Calculate cost for remaining months (using double for precision)
                    goldValue += (double)(item.Value - (i - 1) * income * 12) / income * Math.Pow(2, i);

                    return (int)goldValue; // base 2 point per monthly gold, double every yearly gold
                case DealItemType.Manpower:
                    double manValue = 0;
                    if (giver.LastTurnManpowerIncome == 0) return 0;
                    int recruit = Math.Max(1, giver.LastTurnManpowerIncome);
                    int j = 1;
                    // Calculate cost for full years
                    for (j = 1; (long)j * recruit * 12 <= item.Value; j++)
                    {
                        manValue += 12 * Math.Pow(2, j);
                    }
                    // Calculate cost for remaining months (using double for precision)
                    manValue += (double)(item.Value - (j - 1) * recruit * 12) / recruit * Math.Pow(2, j);
                    return (int)manValue; // base 2 point per monthly manpower, double every yearly manpower
                case DealItemType.Prestige:
                    int prestigevalue;
                    if (giver.Prestige > 100)
                    {
                        prestigevalue = 2;
                    }
                    else if (giver.Prestige > 75)
                    {
                        prestigevalue = 4;
                    }
                    else if (giver.Prestige > 50)
                    {
                        prestigevalue = 8;
                    }
                    else
                    {
                        prestigevalue = 16;
                    }
                    return item.Value * prestigevalue; // Depends on Giver prestige but more expensive to give than receive
                case DealItemType.Commandery:
                    var commanderyItem = save.Commandery.Values.FirstOrDefault(c => c.Id == item.Value);
                    if (commanderyItem == null) return 0;
                    double commanderyValue = (long)commanderyItem.Population * commanderyItem.Wealth / 10000;
                    double countryValue = 0;
                    var countryCommanderies = save.Commandery.Values
                        .Where(c => c.CountryId == giver.Id)
                        .ToList();
                    foreach (var location in countryCommanderies)
                    {
                        countryValue += (long)location.Population * location.Wealth / 10000;
                    }
                    if (countryValue == 0) countryValue = 1; // Avoid divide by zero
                    double multiplier = 1 + (commanderyValue / countryValue);
                    return (int)(commanderyValue * multiplier); // base 5 years income multiplies by 1 + (commanderyValue / countryValue)
                case DealItemType.CallToArms:
                    var enemy = save.Country.Values.FirstOrDefault(c => c.Id == item.Value);
                    if (enemy == null) return 0;
                    double enemyStrength = enemy.Manpower / 2;
                    double giverStrength = giver.Manpower / 2;
                    double receiverStrength = receiver.Manpower / 2;
                    var enemyArmies = save.Army
                        .Where(c => c.CountryId == enemy.Id)
                        .ToList();
                    foreach (var army in enemyArmies)
                    {
                        enemyStrength += army.Size;
                    }
                    var giverArmies = save.Army
                        .Where(c => c.CountryId == giver.Id)
                        .ToList();
                    foreach (var army in giverArmies)
                    {
                        giverStrength += army.Size;
                    }
                    var receiverArmies = save.Army
                        .Where(c => c.CountryId == receiver.Id)
                        .ToList();
                    foreach (var army in receiverArmies)
                    {
                        receiverStrength += army.Size;
                    }
                    double wanttojoin = (giverStrength - enemyStrength) / (giverStrength + enemyStrength) + (receiverStrength + giverStrength - enemyStrength) / (receiverStrength + giverStrength + enemyStrength);
                    if (wanttojoin > 0) wanttojoin *= -10;
                    else wanttojoin *= -100;
                    return (int)wanttojoin; // If we are winning, they quite want to join. If we are losing, they don't want to join, Very expensive to drag into war
                default:
                    return 0;
            }
        }

        private static int GetPactDesire(SaveData save, CountryData approver, CountryData requester, DealItemType type)
        {
            if (type == DealItemType.Alliance)
            {
                double approverStrength = approver.Manpower / 2;
                double requesterStrength = requester.Manpower / 2;

                var approverArmies = save.Army
                .Where(c => c.CountryId == approver.Id)
                .ToList();
                foreach (var army in approverArmies)
                {
                    approverStrength += army.Size;
                }
                if (approverStrength == 0) return 15;

                var requesterArmies = save.Army
                    .Where(c => c.CountryId == requester.Id)
                    .ToList();
                foreach (var army in requesterArmies)
                {
                    requesterStrength += army.Size;
                }

                // Base desire: -10 (Reluctant)
                int AllianceDesire = -10;
                AllianceDesire += (int)(25 * (requesterStrength - approverStrength) / (requesterStrength + approverStrength));
                return AllianceDesire; // not more than 15
            }
            if (type == DealItemType.Peace)
            {
                // Base desire: -2 per monthly manpower left
                int peaceDesire = 0;
                // Strength
                double approverStrength = approver.Manpower / 4;
                double requesterStrength = requester.Manpower / 4;
                var approverArmies = save.Army
                .Where(c => c.CountryId == approver.Id)
                .ToList();
                foreach (var army in approverArmies)
                {
                    approverStrength += army.Size;
                }
                var requesterArmies = save.Army
                    .Where(c => c.CountryId == requester.Id)
                    .ToList();
                foreach (var army in requesterArmies)
                {
                    requesterStrength += army.Size;
                }


                if (approverStrength > approver.Manpower / 3)
                {
                    peaceDesire -= 2 * approver.Manpower / Math.Max(1, approver.LastTurnManpowerIncome);
                }
                peaceDesire += (int)(20 * Math.Pow(requesterStrength / approverStrength - approverStrength / requesterStrength, 3));
                return peaceDesire;
            }
            return 0;
        }

        // Inner Classes
        public class DealProposal
        {
            public CountryData Player { get; }
            public CountryData Target { get; }
            public List<DealItem> Offers { get; } = new List<DealItem>();
            public List<DealItem> Requests { get; } = new List<DealItem>();

            public DealProposal(CountryData player, CountryData target)
            {
                Player = player;
                Target = target;
            }

            public void AddItem(bool isOffer, DealItemType type, int value, string name = "")
            {
                var list = isOffer ? Offers : Requests;
                if (type == DealItemType.Alliance || type == DealItemType.Peace || type == DealItemType.DeclareWar || type == DealItemType.BreakAlliance)
                    list.RemoveAll(i => i.Type == type);

                list.Add(new DealItem { Type = type, Value = value, Name = name });
            }

            public bool HasItem(DealItemType type)
            {
                return Offers.Any(i => i.Type == type) || Requests.Any(i => i.Type == type);
            }

            public bool ContainsWarDeclaration => HasItem(DealItemType.DeclareWar);
            public bool ContainsBreakAlliance => HasItem(DealItemType.BreakAlliance);
        }

        public class DealItem
        {
            public DealItemType Type { get; set; }
            public int Value { get; set; }
            public string Name { get; set; } = "";
        }

        public enum DealItemType
        {
            Gold,
            Manpower,
            Prestige,
            Commandery,
            Alliance,
            Peace,
            DeclareWar,
            CallToArms,
            BreakAlliance
        }
    }

    // Example shape expected by this refactor.
    // Remove if you already have your own State type.
    public class State
    {
        public SaveData Save { get; set; }
    }
}
