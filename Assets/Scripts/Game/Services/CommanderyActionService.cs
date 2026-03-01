using System;
using System.Linq;
using Game.Services.Saves;
using UnityEngine;

namespace Game.Services
{
    public class CommanderyActionService
    {
        private readonly LoggingService _logging = new();
        public string ExecuteIncreaseGarrison(SaveData saveRoot, CountryData country, CommanderyData commandery, int amount)
        {
            if (saveRoot.Battle.Any(b => b.LocationId == commandery.Id)) return "Cannot increase garrison while commandery is under attack!";

            int maxByManpower = country.Manpower;
            int maxByGold = country.Treasury * (int)GameConstants.GarrisonPerGold;
            int maxPossible = Math.Min(maxByManpower, maxByGold);

            if (amount <= 0) return "Invalid amount.";
            if (amount > maxPossible) return "Amount exceeds resource limits!";

            int mpCost = amount;
            int goldCost = (int)Math.Ceiling(amount / GameConstants.GarrisonPerGold);

            commandery.Garrisons += amount;
            country.Manpower -= mpCost;
            country.Treasury -= goldCost;
            _logging.LogForService("CommanderyActionService", $"Garrison increased at {commandery.Name}: +{amount}");

            return "Success";
        }

        public string ExecuteDecreaseGarrison(SaveData saveRoot, CountryData country, CommanderyData commandery, int amount)
        {
            if (saveRoot.Battle.Any(b => b.LocationId == commandery.Id)) return "Cannot decrease garrison while commandery is under attack!";

            if (commandery.CountryId != country.Id) return "Country does not own this commandery.";

            if (amount <= 0) return "Invalid amount.";
            if (amount > commandery.Garrisons) return "Cannot decrease more than current garrisons.";

            commandery.Garrisons -= amount;
            _logging.LogForService("CommanderyActionService", $"Garrison decreased at {commandery.Name}: -{amount}");

            return "Success";
        }

        public void ExecuteChangeGovernor(SaveData saveRoot, int commanderyId, int newGovernorId)
        {
            if (!saveRoot.Commandery.TryGetValue(commanderyId, out var commandery)) return;

            if (newGovernorId == -1)
            {
                commandery.CommanderId = null;
                return;
            }

            var person = saveRoot.Person.Find(p => p.Id == newGovernorId);
            if (person == null) return;

            commandery.CommanderId = person.Id;
            _logging.LogForService("CommanderyActionService", $"Governor assigned at {commandery.Name}: {person.Name}");
        }
    }
}
