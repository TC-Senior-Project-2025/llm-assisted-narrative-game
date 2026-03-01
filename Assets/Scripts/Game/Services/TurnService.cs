using System;
using System.Collections.Generic;
using System.Linq;
using Game.Services.Saves;
using UnityEngine;

namespace Game.Services
{
    public class TurnService
    {
        private readonly System.Random _rng = new System.Random();
        private readonly LoggingService _logging = new();
        private readonly TerritoryService _territoryService;
        private readonly BattleService _battleService;

        public TurnService(TerritoryService territoryService, BattleService battleService)
        {
            _territoryService = territoryService;
            _battleService = battleService;
        }

        public TurnResult ProcessTurn(SaveData save)
        {
            _territoryService.UpdateFogOfWar(GameService.Main.PlayerCountry.Id);
            _territoryService.UpdateBorders();

            var turnLog = new List<string>();
            var deadPersons = new List<PersonData>();
            var deadKings = new List<PersonData>();
            var rebellions = new List<RebellionRequest>();

            _logging.LogForService("TurnService", $"Processing turn {save.Game.Turn} ({save.Game.CurrentYear}/{save.Game.CurrentMonth})");

            foreach (var country in save.Country.Values)
            {
                var countryCommanderies = save.Commandery.Values
                    .Where(c => c.CountryId == country.Id)
                    .ToList();

                double totalTaxBase = 0;
                double totalManpowerBase = 0;
                double totalGarrisonCost = 0;
                long totalPopulation = 0;

                foreach (var location in countryCommanderies)
                {
                    bool hasGovernor = location.CommanderId.HasValue;
                    double governorBonus = hasGovernor ? GameConstants.GovernorEffect : 1.0;

                    double unrestTaxBurden = location.Unrest > 5 ? (15 - location.Unrest) / 10.0 : 1.0;
                    double commanderyTax = (long)location.Population * location.Wealth / (double)GameConstants.TaxDivisor;
                    commanderyTax = commanderyTax * unrestTaxBurden * governorBonus;

                    double unrestManpowerBurden = (12 - location.Unrest) / 10.0;
                    double commanderyManpower = location.Population / (double)GameConstants.ManpowerDivisor * unrestManpowerBurden * governorBonus;
                    location.Population -= (int)commanderyManpower;

                    location.Population += (int)(location.Population * GameConstants.PopulationGrowth);

                    double localGarrisonCost = location.Garrisons * GameConstants.GarrisonUpkeep / governorBonus;

                    //unrest decay
                    double localStab = 100 - country.Stability;
                    if (hasGovernor) localStab -= GameConstants.GovernorUnrestReduction;
                    if (location.Unrest * 10 > localStab && _rng.Next(0, 101) > country.Efficiency - 10)
                    {
                        if (_rng.Next(0, 101) < location.Unrest * 10 - localStab) location.Unrest -= 1;
                    }
                    else if (location.Unrest * 10 < localStab && _rng.Next(0, 101) < country.Efficiency - 10)
                    {
                        if (_rng.Next(0, 101) < localStab - location.Unrest * 10) location.Unrest += 1;
                    }

                    // Cap to keep things from going negative
                    if (location.Unrest < 0) location.Unrest = 0;
                    if (location.Wealth < 20) location.Wealth = 20;
                    if (location.Population < 0) location.Population = 0;
                    if (location.Population < 1000) location.Population += 100;

                    // Rebellion Logic
                    if (hasGovernor)
                    {
                        var governor = save.Person.Find(p => p.Id == location.CommanderId.Value);
                        //total population
                        double nationalPopulation = countryCommanderies.Sum(c => c.Population);
                        double rebelChance = (60 - governor.Loyalty) * (double)location.Population / nationalPopulation;
                        if (_rng.Next(0, 101) < rebelChance)
                        {
                            rebellions.Add(new RebellionRequest
                            {
                                CountryId = country.Id,
                                CommanderyId = location.Id,
                                RebellionType = "Noble",
                                PersonId = governor.Id
                            });
                        }
                    }

                    int unrestRebelChance = location.Unrest > 5 ? (int)Math.Pow(location.Unrest - 5, 1.5) : 0;
                    unrestRebelChance -= location.Garrisons / GameConstants.GarrisonPerRebelChance;
                    if (hasGovernor) unrestRebelChance -= 1;
                    if (_rng.Next(0, 101) < unrestRebelChance)
                    {
                        rebellions.Add(new RebellionRequest
                        {
                            CountryId = country.Id,
                            CommanderyId = location.Id,
                            RebellionType = "Others"
                        });
                    }

                    totalTaxBase += commanderyTax;
                    totalManpowerBase += commanderyManpower;
                    totalGarrisonCost += localGarrisonCost;
                    totalPopulation += location.Population;
                }

                var countryPersons = save.Person
                    .Where(c => c.CountryId == country.Id && c.IsAlive)
                    .ToList();

                foreach (var person in countryPersons)
                {
                    if (country.Prestige > 80 && country.Prestige > person.Loyalty) person.Loyalty += 1;
                    else if (country.Prestige > 50)
                    {
                        if (_rng.Next(0, 101) < country.Prestige) person.Loyalty += 1;
                    }
                    else if (country.Prestige < 25)
                    {
                        if (_rng.Next(0, 101) > country.Prestige) person.Loyalty -= 1;
                    }

                    if (person.Loyalty > 100) person.Loyalty = 100;

                    if (DeathTick(person.Age, _rng))
                    {
                        person.IsAlive = false;
                        _logging.LogForService("TurnService", $"Person died: {person.Name} (Age {person.Age}, Role: {person.Role})");
                        if (string.Equals(person.Role, "King", StringComparison.OrdinalIgnoreCase))
                            deadKings.Add(person);
                        else
                            deadPersons.Add(person);
                    }
                    if (save.Game.CurrentMonth == 1) person.Age += 1;
                }

                var countryArmy = save.Army.Where(c => c.CountryId == country.Id).ToList();
                double totalArmyCost = 0;
                foreach (var army in countryArmy)
                {
                    bool inBattle = BattleService.Main.IsUnitInBattle(army.Id);
                    totalArmyCost += army.Size * GameConstants.ArmyUpkeep;
                    army.ActionLeft = 2;

                    int maxMorale = 90 + (int)(country.Prestige / 10.0);
                    if (army.CommanderId.HasValue)
                    {
                        var commander = save.Person.FirstOrDefault(c => c.Id == army.CommanderId);
                        if (commander != null)
                        {
                            maxMorale += 2 * (commander.Stats.Morale ?? 0);


                            // General Rebellion
                            double totalArmySize = countryArmy.Sum(c => c.Size);
                            double rebelChance = (60 - commander.Loyalty) * (double)army.Size / totalArmySize;
                            if (_rng.Next(0, 101) < rebelChance)
                            {
                                rebellions.Add(new RebellionRequest
                                {
                                    CountryId = country.Id,
                                    CommanderyId = army.LocationId,
                                    RebellionType = "General",
                                    PersonId = commander.Id,
                                    ArmyId = army.Id
                                });
                            }
                        }
                    }
                    if (maxMorale < 10) maxMorale = 10;

                    if (army.Morale > maxMorale) army.Morale = Math.Max(maxMorale, army.Morale - 5);

                    var location = save.Commandery.Values.FirstOrDefault(c => c.Id == army.LocationId);

                    if (location != null && location.CountryId == country.Id)
                    {
                        if (army.Supply < 100 && !inBattle) army.Supply = Math.Min(army.Supply + 10, 100);
                        if (army.Morale < maxMorale && !inBattle) army.Morale = Math.Min(army.Morale + 10 + (int)(country.Prestige / 10.0), maxMorale);
                    }
                    else
                    {
                        army.Supply -= GameConstants.SupplyDecay;
                        army.Size = (int)(army.Size * _rng.Next(100 - GameConstants.Attrition, 101) / 100.0);
                    }
                    if (army.Supply <= 0)
                    {
                        army.Morale = Math.Max(army.Morale + (int)(army.Supply / 2.0), 0);
                        army.Size = (int)(army.Size * (100 + army.Supply / 2.0) / 100.0);
                        army.Supply = 0;
                    }
                    // Feature: Teleport out of neutral lands
                    if (location != null && location.CountryId != country.Id)
                    {
                        var rel = save.Relation.FirstOrDefault(r =>
                            (r.SrcCountryId == country.Id && r.DstCountryId == location.CountryId) ||
                            (r.SrcCountryId == location.CountryId && r.DstCountryId == country.Id));

                        bool isAtWar = rel != null && rel.IsAtWar;
                        bool isAllied = rel != null && rel.IsAllied;

                        if (!isAtWar && !isAllied)
                        {
                            var home = save.Commandery.Values.FirstOrDefault(c => c.CountryId == country.Id);
                            if (home != null)
                            {
                                army.LocationId = home.Id;
                                location = home; // Update ref so they get supply bonuses immediately
                            }
                        }
                    }
                }

                var destroyedArmies = countryArmy.Where(a => a.Size <= 0).ToList();
                foreach (var army in destroyedArmies) save.Army.Remove(army);

                double efficiencyMod = country.Efficiency / 100.0;
                country.LastTurnGoldIncome = (int)(totalTaxBase * efficiencyMod);
                country.LastTurnManpowerIncome = (int)(totalManpowerBase * efficiencyMod);

                int garrisonCost = (int)(totalGarrisonCost * efficiencyMod);
                int countryArmyCost = (int)totalArmyCost;

                country.LastTurnGarrisonUpkeep = garrisonCost;
                country.LastTurnArmyUpkeep = countryArmyCost;

                country.Treasury += country.LastTurnGoldIncome;
                country.Manpower += country.LastTurnManpowerIncome;
                country.Treasury -= (garrisonCost + countryArmyCost);

                // Efficiency Decay
                if (country.Efficiency > 100) country.Efficiency -= 2;
                else if (country.Efficiency > 90) country.Efficiency -= 1;
                else if (country.Efficiency < 10) country.Efficiency += 1;
                else if (country.Efficiency < 1) country.Efficiency = 1;

                // Stability Decay
                if (country.Stability > 150) country.Stability -= 2;
                else if (country.Stability > 100) country.Stability -= 1;
                else if (country.Stability < 50) country.Stability += 1;
                else if (country.Stability < 0) country.Stability += 2;

                // Prestige Decay
                if (country.Prestige > 105 || country.Prestige < -52) country.Prestige -= country.Prestige / 20;
                else if (country.Prestige > country.Stability) country.Prestige -= 2;
                else if (country.Prestige > country.Stability / 2) country.Prestige -= 1;
                else if (country.Prestige < country.Stability / 2) country.Prestige += 1;
                else if (country.Prestige < 0) country.Prestige += 2;

                if (country.Id == save.Game.PlayerCountryId)
                {
                    turnLog.Add($"Taxation: +{country.LastTurnGoldIncome} (Pop: {totalPopulation})");
                    turnLog.Add($"Manpower: +{country.LastTurnManpowerIncome}");
                    turnLog.Add($"Army Upkeep: -{countryArmyCost}");
                    turnLog.Add($"Garrison Cost: -{garrisonCost}");
                }

                country.MissionPoint = 1;
            }

            // Relation Decay
            foreach (var relation in save.Relation)
            {
                if (relation.Value > 100) relation.Value -= relation.Value / 50;
                else if (relation.Value < -100) relation.Value += relation.Value / 50;
                else if (relation.Value > 0) relation.Value -= 1;
                else if (relation.Value < 0) relation.Value += 1;
            }

            var countriesToRemove = new List<CountryData>();
            foreach (var country in save.Country.Values)
            {
                bool hasCommandery = save.Commandery.Values.Any(c => c.CountryId == country.Id);
                bool hasArmy = save.Army.Any(a => a.CountryId == country.Id);
                if (!hasCommandery && !hasArmy) countriesToRemove.Add(country);
            }

            foreach (var country in countriesToRemove)
            {
                _logging.LogForService("TurnService", $"Country annexed: {country.Name} (ID: {country.Id})");
                save.Country.Remove(country.Id);
                save.Relation.RemoveAll(r => r.SrcCountryId == country.Id || r.DstCountryId == country.Id);
                save.Person.RemoveAll(p => p.CountryId == country.Id);
                var game = save.Game;
                game.History += $"\n{game.CurrentYear} {game.CurrentMonth}: {country.Name} was annexed.";
            }

            if (countriesToRemove.Count > 0)
            {
                World.Map.GameMap.Main.BorderRenderer.Render();
                TerritoryService.Main.UpdateFogOfWar(GameService.Main.PlayerCountry.Id);
            }

            return new TurnResult
            {
                Log = turnLog,
                DeadKings = deadKings,
                DeadPersons = deadPersons,
                Rebellions = rebellions
            };
        }

        public class TurnResult
        {
            public List<string> Log { get; set; } = new List<string>();
            public List<PersonData> DeadKings { get; set; } = new List<PersonData>();
            public List<PersonData> DeadPersons { get; set; } = new List<PersonData>();
            public List<RebellionRequest> Rebellions { get; set; } = new List<RebellionRequest>();
        }

        public class RebellionRequest
        {
            public int CountryId { get; set; }
            public int CommanderyId { get; set; }
            public string RebellionType { get; set; }
            public int? PersonId { get; set; }
            public int? ArmyId { get; set; }
        }

        private bool DeathTick(int age, System.Random rng)
        {
            double probability = 0.0;
            if (age < 20) probability = 0.0001;
            else if (age < 25) probability = 0.001418;
            else if (age < 30) probability = 0.000375;
            else if (age < 35) probability = 0.002;
            else if (age < 40) probability = 0.000875;
            else if (age < 45) probability = 0.0019;
            else if (age < 50) probability = 0.004615;
            else if (age < 55) probability = 0.001389;
            else if (age < 60) probability = 0.002325;
            else if (age < 65) probability = 0.005925;
            else if (age < 70) probability = 0.004;
            else if (age < 80) probability = 0.005;
            else probability = 0.03;

            return rng.NextDouble() < probability;
        }

        public void CalculateIncome(SaveData save)
        {
            // Utility for UI to predict income?
            foreach (var country in save.Country.Values)
            {
                var countryCommanderies = save.Commandery.Values.Where(c => c.CountryId == country.Id).ToList();
                double totalTaxBase = 0;
                double totalManpowerBase = 0;

                foreach (var location in countryCommanderies)
                {
                    double unrestTaxBurden = location.Unrest > 5 ? (15 - location.Unrest) / 10.0 : 1.0;
                    double commanderyTax = (long)location.Population * location.Wealth / (double)GameConstants.TaxDivisor * unrestTaxBurden;
                    double unrestManpowerBurden = (12 - location.Unrest) / 10.0;
                    double commanderyManpower = location.Population / (double)GameConstants.ManpowerDivisor * unrestManpowerBurden;
                    totalTaxBase += commanderyTax;
                    totalManpowerBase += commanderyManpower;
                }
                double efficiencyMod = country.Efficiency / 100.0;
                country.LastTurnGoldIncome = (int)(totalTaxBase * efficiencyMod);
                country.LastTurnManpowerIncome = (int)(totalManpowerBase * efficiencyMod);
            }
        }
    }
}
