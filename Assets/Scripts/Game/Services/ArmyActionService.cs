using System;
using System.Linq;
using Game.Services.Saves;
using UnityEngine;

namespace Game.Services
{
    public class ArmyActionService
    {
        private readonly LoggingService _logging = new();
        public string ExecuteCreateArmy(SaveData saveRoot, CountryData country, int locationId, string name, int size)
        {
            var NewArmies = saveRoot.Army.Where(a => a.CountryId == country.Id && a.Morale == 0).ToList();
            if (NewArmies.Count >= 1) return "Cannot recruit more when there's a demoralised army.";

            int maximumIncrease = Math.Max((int)((double)country.Manpower * (100 + country.Prestige) / 1000), 10000);
            int cost = (int)Math.Ceiling(size / (double)GameConstants.TroopsPerGold);

            if (size > country.Manpower) return "Not enough manpower!";
            if (size > maximumIncrease) return "Exceeded maximum recruitment size.";
            if (size <= 0) return "Invalid amount.";
            if (cost > country.Treasury) return $"Not enough gold! Cost: {cost}, Treasury: {country.Treasury}";

            var newArmy = new ArmyData
            {
                Id = saveRoot.Army.Any() ? saveRoot.Army.Max(a => a.Id) + 1 : 1,
                CountryId = country.Id,
                GameId = country.GameId,
                LocationId = locationId,
                Name = name,
                Size = size,
                Morale = 0,
                Supply = 100,
                ActionLeft = 0,
                CommanderId = null,
                History = ""
            };

            saveRoot.Army.Add(newArmy);
            country.Manpower -= size;
            country.Treasury -= cost;
            _logging.LogForService("ArmyActionService", $"Army created: {name} (Size: {size}) at location {locationId}");
            return "Success";
        }

        public string ExecuteMoveArmy(SaveData saveRoot, CountryData country, ArmyData army, int destinationId, bool ignoreBattle = false)
        {
            if (!ignoreBattle)
            {
                var activeBattle = saveRoot.Battle.FirstOrDefault(b => b.AttackerArmyIds.Contains(army.Id) || b.DefenderArmyIds.Contains(army.Id));
                if (activeBattle != null)
                {
                    if (activeBattle.Phase == "engagement" || activeBattle.Phase == "breakthrough")
                    {
                        return "BattleInProgress";
                    }
                }
            }

            if (army.ActionLeft < 2) return "Not enough actions to move (requires 2).";

            if (!saveRoot.Commandery.TryGetValue(destinationId, out var destination)) return "Invalid destination.";

            // Check valid move (Foreign land + Not Allied/AtWar)
            if (destination.CountryId != country.Id)
            {
                var rel = saveRoot.Relation.FirstOrDefault(r =>
                    (r.SrcCountryId == country.Id && r.DstCountryId == destination.CountryId) ||
                    (r.SrcCountryId == destination.CountryId && r.DstCountryId == country.Id));

                bool isAllied = rel != null && rel.IsAllied;
                bool isAtWar = rel != null && rel.IsAtWar;

                if (!isAllied && !isAtWar)
                {
                    return $"Cannot move to {destination.Name}. You must be Allied or At War to enter foreign territory.";
                }
            }

            army.LocationId = destination.Id;
            army.ActionLeft -= 2;
            _logging.LogForService("ArmyActionService", $"Army {army.Name} moved to {destination.Name}");
            return "Success";
        }

        public void ExecuteRetreat(SaveData saveRoot, ArmyData army, BattleData activeBattle)
        {
            int troopLoss = (int)(army.Size * GameConstants.RetreatTroopLoss);
            int moraleLoss = GameConstants.RetreatMoraleLoss;

            army.Size -= troopLoss;
            army.Morale = Math.Max(0, army.Morale - moraleLoss);

            if (army.Size <= 0)
            {
                saveRoot.Army.Remove(army);
            }

            activeBattle.AttackerArmyIds.Remove(army.Id);
            activeBattle.DefenderArmyIds.Remove(army.Id);
            _logging.LogForService("ArmyActionService", $"Army {army.Name} retreated (Lost {troopLoss} troops)");
        }

        public string ExecuteDecreaseArmy(SaveData saveRoot, CountryData country, ArmyData army, int amount)
        {
            var Armylocation = saveRoot.Commandery.Values.FirstOrDefault(c => c.Id == army.LocationId);
            if (Armylocation != null && Armylocation.CountryId != country.Id) return "Cannot recruit in foreign territory.";

            if (amount > army.Size) return "Cannot decrease more than current size!";
            if (amount <= 0) return "Invalid amount.";

            army.Size -= amount;
            country.Manpower += amount / 2;

            if (saveRoot.Commandery.TryGetValue(army.LocationId, out var localCommandery))
            {
                localCommandery.Population += amount / 2;
            }

            if (army.Size <= 0)
            {
                saveRoot.Army.Remove(army);
            }
            else
            {
                army.ActionLeft--;
            }

            return "Success";
        }

        public string ExecuteIncreaseArmy(SaveData saveRoot, CountryData country, ArmyData army, int amount)
        {
            var Armylocation = saveRoot.Commandery.Values.FirstOrDefault(c => c.Id == army.LocationId);
            if (Armylocation != null && Armylocation.CountryId != country.Id) return "Cannot recruit in foreign territory.";

            int maximumIncrease = Math.Max((int)((double)country.Manpower * (100 + country.Prestige) / 1000), 10000);
            int cost = (int)Math.Ceiling(amount / (double)GameConstants.TroopsPerGold);

            if (amount > country.Manpower) return "Not enough manpower!";
            if (amount > maximumIncrease) return "Exceeded maximum increase.";
            if (amount <= 0) return "Invalid amount.";
            if (cost > country.Treasury) return $"Not enough gold! Cost: {cost}, Treasury: {country.Treasury}";

            army.Size += amount;
            country.Manpower -= amount;
            country.Treasury -= cost;

            army.Morale -= (int)(100.0 * amount / army.Size);
            if (army.Morale < 0) army.Morale = 0;

            army.ActionLeft--;

            return "Success";
        }

        public string ExecuteResupply(SaveData saveRoot, CountryData country, ArmyData army, int amount)
        {
            int maxNeeded = 100 - army.Supply;
            if (army.Supply >= 100) return "Army is already fully supplied.";
            if (amount <= 0) return "Invalid amount.";
            if (amount > maxNeeded) return $"You only need {maxNeeded} supply.";

            double costPerSupply = (double)army.Size * GameConstants.SupplyPrice;
            int totalCost = (int)Math.Ceiling(costPerSupply * amount);

            if (totalCost > country.Treasury) return $"Not enough gold! Cost: {totalCost}, Treasury: {country.Treasury}";

            country.Treasury -= totalCost;
            army.Supply += amount;
            army.Morale += amount / 5;

            return "Success";
        }

        public string ExecuteSplitArmy(SaveData saveRoot, ArmyData army, int newSize)
        {
            if (army.ActionLeft <= 0) return "No more actions.";
            if (IsArmyBusyInBattle(saveRoot, army.Id)) return "Cannot split while a battle is in progress.";
            if (army.Size <= 1) return "Army size must be greater than 1 to split.";

            if (newSize >= army.Size) return "New army size must be smaller than original army.";
            if (newSize <= 0) return "New army size must be positive.";

            int remainingSize = army.Size - newSize;

            var newArmy = new ArmyData
            {
                Id = saveRoot.Army.Any() ? saveRoot.Army.Max(a => a.Id) + 1 : 1,
                GameId = army.GameId,
                CountryId = army.CountryId,
                CommanderId = null,
                LocationId = army.LocationId,
                Name = $"{army.Name} (Detachment)",
                Size = newSize,
                Morale = army.Morale,
                Supply = army.Supply,
                ActionLeft = 0,
                History = ""
            };

            army.Size = remainingSize;
            army.ActionLeft--;
            saveRoot.Army.Add(newArmy);
            _logging.LogForService("ArmyActionService", $"Army {army.Name} split - new detachment of {newSize} troops");

            return "Success";
        }

        public string ExecuteMergeArmies(SaveData saveRoot, ArmyData src, ArmyData dst)
        {
            if (src.CountryId != dst.CountryId) return "Armies must belong to the same country.";
            if (src.LocationId != dst.LocationId) return "Armies must be in the same location to merge.";
            if (src.ActionLeft <= 0 || dst.ActionLeft <= 0) return "One or both armies have no actions left.";

            if (IsArmyBusyInBattle(saveRoot, src.Id) || IsArmyBusyInBattle(saveRoot, dst.Id))
                return "Cannot merge while a battle is in progress.";

            int srcSize = Math.Max(0, src.Size);
            int dstSize = Math.Max(0, dst.Size);
            int total = srcSize + dstSize;

            if (srcSize <= 0) return "Source army has no troops.";
            if (dstSize <= 0) return "Target army has no troops.";

            dst.Morale = WeightedAverage(dst.Morale, dstSize, src.Morale, srcSize);
            dst.Supply = WeightedAverage(dst.Supply, dstSize, src.Supply, srcSize);
            dst.Size = total;

            if (dst.CommanderId == null) dst.CommanderId = src.CommanderId;

            src.ActionLeft--;
            dst.ActionLeft--;
            saveRoot.Army.Remove(src);
            _logging.LogForService("ArmyActionService", $"Armies merged: {src.Name} into {dst.Name} (Total: {total} troops)");

            return "Success";
        }

        public string ExecuteChangeCommander(SaveData saveRoot, ArmyData army, int commanderId)
        {
            if (army.ActionLeft <= 0) return "No more actions.";
            army.CommanderId = commanderId;
            army.ActionLeft--;
            return "Success";
        }

        private bool IsArmyBusyInBattle(SaveData save, int armyId)
        {
            var battle = save.Battle.FirstOrDefault(b =>
                (b.AttackerArmyIds?.Contains(armyId) ?? false) ||
                (b.DefenderArmyIds?.Contains(armyId) ?? false));

            if (battle == null) return false;
            return battle.Phase == "engagement" || battle.Phase == "breakthrough";
        }

        private int WeightedAverage(int a, int aW, int b, int bW)
        {
            int denom = aW + bW;
            if (denom <= 0) return 0;
            return (int)Math.Round((a * (double)aW + b * (double)bW) / denom);
        }
    }
}
