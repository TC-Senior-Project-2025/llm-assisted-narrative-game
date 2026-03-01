using System;
using System.Collections.Generic;
using System.Linq;
using Extensions;
using Game.Services.Saves;
using R3;
using UnityEngine;

namespace Game.Services.Commands
{
    public static class ArmyCommands
    {
        private static ReactiveProperty<SaveData> State => GameService.Main.State;
        private static readonly ArmyActionService _service = new ArmyActionService();

        public static void MoveArmy(int armyId, int locationId, bool ignoreBattle = false)
        {
            var save = State.CurrentValue;
            var army = save.Army.Find(a => a.Id == armyId);
            if (army == null) { Debug.LogError($"Army not found: {armyId}"); return; }

            var country = save.Country.GetValueOrDefault(army.CountryId);
            if (country == null) { Debug.LogError($"Country not found: {army.CountryId}"); return; }

            // GUI Check: Neighbors
            var isNeighbor = World.Map.GameMap.Main.Connections.IsNeighborOf(army.LocationId, locationId);
            if (!isNeighbor) { Debug.LogError("Not neighbor"); return; }

            string result = _service.ExecuteMoveArmy(save, country, army, locationId, ignoreBattle);
            if (result == "Success")
            {
                State.ApplyInnerMutations();
            }
            else
            {
                Fail(result);
            }
        }

        public static string CreateArmy(int countryId, int locationId, string name, int size)
        {
            var save = State.CurrentValue;
            var country = save.Country.GetValueOrDefault(countryId);
            if (country == null) return Fail("Invalid country.");

            // GUI Check: Location ownership logic is implicit in service (service checks MANPOWER, but maybe not ownership of location for spawn?)
            // Service checks: none about location ownership? Let's keep the GUI check.
            if (!save.Commandery.TryGetValue(locationId, out var location)) return Fail("Invalid location.");
            if (location.CountryId != countryId) return Fail($"Cannot recruit in {location.Name}: not controlled by your country.");

            string result = _service.ExecuteCreateArmy(save, country, locationId, name, size);
            if (result == "Success")
            {
                State.ApplyInnerMutations();
                return "Success";
            }
            return Fail(result);
        }

        public static string MergeArmies(int sourceArmyId, int targetArmyId)
        {
            if (sourceArmyId == targetArmyId) return Fail("Cannot merge an army into itself.");
            var save = State.CurrentValue;
            var src = save.Army.Find(a => a.Id == sourceArmyId);
            var dst = save.Army.Find(a => a.Id == targetArmyId);

            if (src == null) return Fail($"Source army not found: {sourceArmyId}");
            if (dst == null) return Fail($"Target army not found: {targetArmyId}");

            string result = _service.ExecuteMergeArmies(save, src, dst);
            if (result == "Success")
            {
                State.ApplyInnerMutations();
                return "Success";
            }
            return Fail(result);
        }

        public static string SplitArmy(int armyId, int newSize)
        {
            var save = State.CurrentValue;
            var army = save.Army.Find(a => a.Id == armyId);
            if (army == null) return Fail($"Army not found: {armyId}");

            string result = _service.ExecuteSplitArmy(save, army, newSize);
            if (result == "Success")
            {
                State.ApplyInnerMutations();
                return "Success";
            }
            return Fail(result);
        }

        public static string ResupplyArmy(int armyId, int amount)
        {
            var save = State.CurrentValue;
            var army = save.Army.Find(a => a.Id == armyId);
            if (army == null) return Fail($"Army not found: {armyId}");
            var country = save.Country.GetValueOrDefault(army.CountryId);
            if (country == null) return Fail("Invalid country.");

            string result = _service.ExecuteResupply(save, country, army, amount);
            if (result == "Success")
            {
                State.ApplyInnerMutations();
                return "Success";
            }
            return Fail(result);
        }

        public static void ChangeCommander(int armyId, int commanderId)
        {
            var save = State.CurrentValue;
            var army = save.Army.Find(a => a.Id == armyId);
            if (army == null) { Fail($"Army not found: {armyId}"); return; }

            string result = _service.ExecuteChangeCommander(save, army, commanderId);
            if (result == "Success")
            {
                State.ApplyInnerMutations();
            }
            else
            {
                Fail(result);
            }
        }

        public static void Retreat(int armyId, int battleId)
        {
            var save = State.CurrentValue;
            var army = save.Army.Find(a => a.Id == armyId);
            if (army == null) { Fail($"Army not found: {armyId}"); return; }

            var activeBattle = save.Battle.Find(b => b.Id == battleId);
            if (activeBattle == null) { Fail($"Battle not found: {battleId}"); return; }

            _service.ExecuteRetreat(save, army, activeBattle); // void return
            State.ApplyInnerMutations();
        }

        public static void DecreaseArmy(int armyId, int amount)
        {
            var save = State.CurrentValue;
            var army = save.Army.Find(a => a.Id == armyId);
            if (army == null) { Fail($"Army not found: {armyId}"); return; }
            var country = save.Country.GetValueOrDefault(army.CountryId);
            if (country == null) { Fail($"Country not found: {army.CountryId}"); return; }

            string result = _service.ExecuteDecreaseArmy(save, country, army, amount);
            if (result == "Success")
            {
                State.ApplyInnerMutations();
            }
            else
            {
                Fail(result);
            }
        }

        public static void IncreaseArmy(int armyId, int amount)
        {
            var save = State.CurrentValue;
            var army = save.Army.Find(a => a.Id == armyId);
            if (army == null) { Fail($"Army not found: {armyId}"); return; }
            var country = save.Country.GetValueOrDefault(army.CountryId);
            if (country == null) { Fail($"Country not found: {army.CountryId}"); return; }

            // Double check battle status if GUI originally had it
            // ArmyActionService doesn't explicitly check "IsAtWar && InBattle" for Increase, 
            // but GUI did: "In battle! Cannot increase army".
            // Let's keep that check here or rely on service? 
            // The service DOES NOT have the "In battle" check for IncreaseArmy in the snippet I saw.
            // I should verify if I need to add it to Service or keep it here.
            // For now, I'll keep the specific GUI check here to be safe and identical to before.
            bool isAtWar = save.Relation.Any(r => (r.SrcCountryId == country.Id || r.DstCountryId == country.Id) && r.IsAtWar);
            if (isAtWar && save.Battle.Any(b => b.AttackerArmyIds.Contains(army.Id) || b.DefenderArmyIds.Contains(army.Id)))
            {
                Fail("In battle! Cannot increase army");
                return;
            }

            string result = _service.ExecuteIncreaseArmy(save, country, army, amount);
            if (result == "Success")
            {
                State.ApplyInnerMutations();
            }
            else
            {
                Fail(result);
            }
        }

        private static string Fail(string msg)
        {
            Debug.LogError(msg);
            return msg;
        }
    }
}
