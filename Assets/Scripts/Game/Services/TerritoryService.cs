using System.Collections.Generic;
using System.Linq;
using Extensions;
using Game.Services.Saves;
using R3;

namespace Game.Services
{
    public class TerritoryService
    {
        public static TerritoryService Main { get; private set; }

        private readonly ReactiveProperty<SaveData> _state;
        private readonly World.Map.GameMap _gameMap;
        private readonly MapService _mapService;
        private readonly LoggingService _logging = new();

        public TerritoryService(ReactiveProperty<SaveData> state, World.Map.GameMap gameMap, MapService mapService)
        {
            _state = state;
            _gameMap = gameMap;
            _mapService = mapService;
            Main = this;
        }

        public void AnnexCommandery(int countryId, int commanderyId)
        {
            var commandery = _state.Value.Commandery[commanderyId];
            var countryName = _state.Value.Country.TryGetValue(countryId, out var c) ? c.Name : countryId.ToString();
            _logging.LogForService("TerritoryService", $"Commandery annexed: {commandery.Name} by {countryName}");

            commandery.CountryId = countryId;
            commandery.Unrest = 10;
            commandery.Garrisons = 1000;

            UpdateFogOfWar(GameService.Main.PlayerCountry.Id);
            UpdateBorders();
            _mapService.UpdateBorderStatus(_state.Value); // Update border caches
            _state.ApplyInnerMutations();
        }

        public void UpdateFogOfWar(int countryId)
        {
            var allyCountries = new HashSet<int>(_state.CurrentValue.Relation
                .Where(r => r.IsAllied && r.DstCountryId == countryId)
                .Select(r => r.SrcCountryId));

            var allyProvinces = _state.CurrentValue.Commandery.Values
                .Where(c => allyCountries.Contains(c.CountryId))
                .Select(c => c.Id);

            var occupiedProvinces = _state.CurrentValue.Commandery.Values
                .Where(c => c.CountryId == countryId)
                .Select(c => c.Id);

            var combined = allyProvinces.Concat(occupiedProvinces);

            _gameMap.FogOfWar.occupiedProvinces = combined.ToList();
            _gameMap.FogOfWar.BuildFogOfWarTexture();
            _gameMap.FogOfWar.ApplyTexture();
        }

        public void UpdateBorders()
        {
            _gameMap.BorderRenderer.Render();
        }
    }
}
