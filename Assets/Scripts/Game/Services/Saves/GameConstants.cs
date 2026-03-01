namespace Game.Services.Saves
{
    public static class GameConstants
    {
        public const double TroopsPerGold = 1000;
        public const double GarrisonPerGold = 10.0;
        public const double SupplyPrice = 0.00005;
        public const double GarrisonUpkeep = 0.0005;
        public const double ArmyUpkeep = 0.00025;
        public const long TaxDivisor = 12 * 100000;
        public const int ManpowerDivisor = 12 * 100;
        public const int SupplyDecay = 30;
        public const double PopulationGrowth = 0.2 / 100 / 12;
        public const int AllianceRelationBonus = 50;
        public const int WarRelationPenalty = -50;
        public const int BreakAllianceRelationPenalty = -50;
        public const int PeaceRelationBonus = 25;
        public const int Attrition = 5;
        public const double GovernorEffect = 1.1;
        public const double RetreatTroopLoss = 0.2;
        public const int RetreatMoraleLoss = 15;
        public const int SkirmishMoraleLoss = 3;
        public const int GovernorUnrestReduction = 10;
        public const int GarrisonPerRebelChance = 5000;
    }
}
