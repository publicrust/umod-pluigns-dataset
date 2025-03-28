using System;
using System.Collections.Generic;
using System.Text;
using ConVar;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Entity Reducer", "Arainrr", "2.1.4")]
    [Description("Controls all spawn populations on the server")]
    public class EntityReducer : RustPlugin
    {
        #region Oxide Hooks

        private void OnServerInitialized()
        {
            if (SpawnHandler.Instance == null || SpawnHandler.Instance.AllSpawnPopulations == null)
            {
                PrintError("The SpawnHandler is missing on your server, the plugin cannot be used");
                Interface.Oxide.UnloadPlugin(Name);
                return;
            }
            UpdateConfig();
            if (configData.pluginEnabled)
            {
                ApplySpawnHandler();
            }
        }

        #endregion Oxide Hooks

        #region Methods

        private void UpdateConfig()
        {
            var newPopulationSettings = new Dictionary<string, PopulationSetting>();
            for (int i = 0; i < SpawnHandler.Instance.AllSpawnPopulations.Length; i++)
            {
                var spawnPopulation = SpawnHandler.Instance.AllSpawnPopulations[i];
                if (spawnPopulation == null) continue;
                var spawnDistribution = SpawnHandler.Instance.SpawnDistributions[i];
                if (spawnDistribution == null) continue;
                int targetCount = spawnPopulation.GetTargetCount(spawnDistribution);
                PopulationSetting populationSetting;
                if (configData.populationSettings.TryGetValue(spawnPopulation.name, out populationSetting))
                {
                    if (!populationSetting.enabled)
                    {
                        populationSetting.targetCount = targetCount;
                    }
                    newPopulationSettings.Add(spawnPopulation.name, populationSetting);
                }
                else
                {
                    newPopulationSettings.Add(spawnPopulation.name, new PopulationSetting { targetCount = targetCount });
                }
            }
            configData.populationSettings = newPopulationSettings;
            SaveConfig();
        }

        private void ApplySpawnHandler()
        {
            foreach (var spawnPopulationBase in SpawnHandler.Instance.AllSpawnPopulations)
            {
                var spawnPopulation = spawnPopulationBase as DensitySpawnPopulation;
                if (spawnPopulation == null) continue;
                PopulationSetting populationSetting;
                if (configData.populationSettings.TryGetValue(spawnPopulation.name, out populationSetting) && populationSetting.enabled)
                {
                    float num = TerrainMeta.Size.x * TerrainMeta.Size.z;
                    if (!spawnPopulation.ScaleWithLargeMaps)
                    {
                        num = Mathf.Min(num, 1.6E+07f);
                    }
                    var densityToMaxPopulation = num * 1E-06f * Spawn.max_density;
                    spawnPopulation.ScaleWithSpawnFilter = false;
                    spawnPopulation.ScaleWithServerPopulation = false;
                    spawnPopulation.EnforcePopulationLimits = true;
                    spawnPopulation.ScaleWithLargeMaps = true;
                    var targetDensity = populationSetting.targetCount / densityToMaxPopulation;
                    var convarControlledSpawnPopulation = spawnPopulation as ConvarControlledSpawnPopulation;
                    if (convarControlledSpawnPopulation != null)
                    {
                        ConsoleSystem.Command command = ConsoleSystem.Index.Server.Find(convarControlledSpawnPopulation.PopulationConvar);
                        command?.Set(targetDensity);
                    }
                    else spawnPopulation._targetDensity = targetDensity;
                }
            }
            SpawnHandler.Instance.EnforceLimits(true);
        }

        public string GetReport()
        {
            SpawnPopulationBase[] allSpawnPopulations = SpawnHandler.Instance.AllSpawnPopulations;
            SpawnDistribution[] spawnDistributions = SpawnHandler.Instance.SpawnDistributions;
            StringBuilder stringBuilder = new StringBuilder();
            if (allSpawnPopulations == null) stringBuilder.AppendLine("Spawn population array is null.");
            if (spawnDistributions == null) stringBuilder.AppendLine("Spawn distribution array is null.");
            if (allSpawnPopulations != null && spawnDistributions != null)
            {
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("SpawnPopulationName".PadRight(40) + "CurrentPopulation".PadRight(25) + "MaximumPopulation");
                for (int i = 0; i < allSpawnPopulations.Length; i++)
                {
                    var spawnPopulation = allSpawnPopulations[i];
                    if (spawnPopulation == null) continue;
                    var spawnDistribution = spawnDistributions[i];
                    if (spawnDistribution == null) continue;
                    int currentCount = spawnDistribution.Count;
                    int targetCount = spawnPopulation.GetTargetCount(spawnDistribution);
                    stringBuilder.AppendLine(spawnPopulation.name.PadRight(40) + currentCount.ToString().PadRight(25) + targetCount);
                }
            }
            return stringBuilder.ToString();
        }

        #endregion Methods

        #region Commands

        [ConsoleCommand("er.fillpopulations")]
        private void CmdFillPopulations(ConsoleSystem.Arg arg)
        {
            SpawnHandler.Instance.FillPopulations();
            SendReply(arg, "Successfully filled all populations");
        }

        [ConsoleCommand("er.getreport")]
        private void CmdGetReport(ConsoleSystem.Arg arg) => SendReply(arg, GetReport());

        [ConsoleCommand("er.enforcelimits")]
        private void CmdEnforceLimits(ConsoleSystem.Arg arg)
        {
            SpawnHandler.Instance.EnforceLimits(true);
            SendReply(arg, "Successfully enforced all population limits");
        }

        #endregion Commands

        #region Configuration

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Enabled Plugin")]
            public bool pluginEnabled = false;

            [JsonProperty(PropertyName = "Population Settings")]
            public Dictionary<string, PopulationSetting> populationSettings = new Dictionary<string, PopulationSetting>();
        }

        private class PopulationSetting
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool enabled = true;

            [JsonProperty(PropertyName = "Target Count")]
            public int targetCount;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                if (configData == null)
                    LoadDefaultConfig();
            }
            catch (Exception ex)
            {
                PrintError($"The configuration file is corrupted. \n{ex}");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            configData = new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(configData);

        #endregion Configuration
    }
}
