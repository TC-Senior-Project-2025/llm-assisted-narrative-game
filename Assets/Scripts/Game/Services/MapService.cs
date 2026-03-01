using System.Collections.Generic;
using Game.Services.Saves;
using R3;

namespace Game.Services
{
    public class MapService
    {
        public static MapService Main { get; private set; }

        private static readonly Dictionary<int, List<int>> _mapConnections = new()
        {
            { 1, new List<int> { 2, 3, 4, 5, 6, 7, 27 } },
            { 2, new List<int> { 1, 3, 23, 27, 28, 29 } },
            { 3, new List<int> { 1, 2, 6, 10, 13, 16, 23 } },
            { 4, new List<int> { 1, 5, 7 } },
            { 5, new List<int> { 1, 4, 6 } },
            { 6, new List<int> { 1, 3, 5, 13, 12, 17 } },
            { 7, new List<int> { 1, 4, 8, 9, 27, 33 } },
            { 8, new List<int> { 7, 9 } },
            { 9, new List<int> { 7, 8 } },
            { 10, new List<int> { 3, 16, 13, 14, 15, 18, 23 } },
            { 11, new List<int> { 12, 17 } },
            { 12, new List<int> { 6, 11, 13, 14, 17 } },
            { 13, new List<int> { 3, 6, 10, 12, 14, 16 } },
            { 14, new List<int> { 10, 12, 13, 18 } },
            { 15, new List<int> { 10, 18, 23, 24 } },
            { 16, new List<int> { 3, 10, 13 } },
            { 17, new List<int> { 6, 11, 12 } },
            { 18, new List<int> { 10, 14, 15, 19 } },
            { 19, new List<int> { 18, 20 } },
            { 20, new List<int> { 19, 21 } },
            { 21, new List<int> { 20, 22 } },
            { 22, new List<int> { 21 } },
            { 23, new List<int> { 2, 3, 10, 15, 24, 26, 28 } },
            { 24, new List<int> { 15, 23, 25, 26 } },
            { 25, new List<int> { 24, 26, 35 } },
            { 26, new List<int> { 23, 24, 25, 28, 32, 35 } },
            { 27, new List<int> { 1, 2, 7, 29, 30, 33 } },
            { 28, new List<int> { 2, 23, 26, 29, 32 } },
            { 29, new List<int> { 2, 27, 28, 30, 32 } },
            { 30, new List<int> { 27, 29, 31, 32, 33, 34, 35, 36 } },
            { 31, new List<int> { 30, 34, 35 } },
            { 32, new List<int> { 26, 28, 29, 30, 35 } },
            { 33, new List<int> { 7, 27, 30, 36 } },
            { 34, new List<int> { 30, 31 } },
            { 35, new List<int> { 25, 26, 30, 31, 32 } },
            { 36, new List<int> { 30, 33 } }
        };

        public MapService()
        {
            Main = this;
        }

        public void InitializeMapNodes(SaveData saveRoot)
        {
            foreach (var commandery in saveRoot.Commandery.Values)
            {
                if (_mapConnections.TryGetValue(commandery.Id, out var neighbors))
                {
                    commandery.Neighbors = neighbors;
                }
            }
        }

        public void UpdateBorderStatus(SaveData save)
        {
            // 1. Reset Caches
            foreach (var country in save.Country.Values)
            {
                country.BorderingCountryIds.Clear();
            }
            foreach (var commandery in save.Commandery.Values)
            {
                commandery.BorderCountryIds.Clear();
            }

            // 2. Populate Caches
            foreach (var commandery in save.Commandery.Values)
            {
                if (commandery.Neighbors == null) continue;

                foreach (var neighborId in commandery.Neighbors)
                {
                    if (save.Commandery.TryGetValue(neighborId, out var neighbor))
                    {
                        if (neighbor.CountryId != commandery.CountryId)
                        {
                            // It's a border!
                            if (!commandery.BorderCountryIds.Contains(neighbor.CountryId))
                            {
                                commandery.BorderCountryIds.Add(neighbor.CountryId);
                            }

                            // Update Country Level Cache
                            if (save.Country.TryGetValue(commandery.CountryId, out var country))
                            {
                                country.BorderingCountryIds.Add(neighbor.CountryId);
                            }
                        }
                    }
                }
            }
        }
    }
}
