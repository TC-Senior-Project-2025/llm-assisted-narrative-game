using UnityEngine;
using Game.Services;
using Game.Services.Saves;
using System.Collections.Generic;

public class MigrationVerification : MonoBehaviour
{
    private void Start()
    {
        Debug.Log("Starting Migration Verification...");

        var gameService = GameService.Main;
        if (gameService == null)
        {
            Debug.LogError("GameService.Main is null! Service integration failed.");
            return;
        }

        Debug.Log("GameService found.");

        // Create Mock SaveData
        var mockSave = new SaveData
        {
            Game = new GameData { Id = 1, PlayerCountryId = 1, CurrentYear = 190, CurrentMonth = 1 },
            Country = new Dictionary<int, CountryData>
            {
                { 1, new CountryData { Id = 1, Name = "Player", Treasury = 1000, Manpower = 5000, Stability = 100, Efficiency = 100 } },
                { 2, new CountryData { Id = 2, Name = "Enemy", Treasury = 1000, Manpower = 5000, Stability = 100, Efficiency = 100 } }
            },
            Commandery = new Dictionary<int, CommanderyData>
            {
                { 1, new CommanderyData { Id = 1, CountryId = 1, Name = "Home", Population = 10000, Wealth = 100, Garrisons = 1000, Neighbors = new List<int>() } },
                { 2, new CommanderyData { Id = 2, CountryId = 2, Name = "Away", Population = 10000, Wealth = 100, Garrisons = 1000, Neighbors = new List<int>{ 1 } } }
            },
            Army = new List<ArmyData>(),
            Relation = new List<RelationData>(),
            Person = new List<PersonData>(),
            Battle = new List<BattleData>()
        };

        // Neighbor setup
        mockSave.Commandery[1].Neighbors.Add(2);

        Debug.Log("Starting Game Service with mock save...");
        try
        {
            gameService.StartFromSave(mockSave);
            Debug.Log("StartFromSave executed successfully.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"StartFromSave failed: {ex}");
        }

        Debug.Log("Migration Verification Complete. Check logs for errors.");
    }
}
