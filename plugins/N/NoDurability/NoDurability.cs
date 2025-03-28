﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("No Durability", "Wulf/lukespragg/Arainrr", "2.2.5", ResourceId = 1061)]
    public class NoDurability : RustPlugin
    {
        #region Fields

        [PluginReference] private readonly Plugin ZoneManager, DynamicPVP;
        private const string PERMISSION_USE = "nodurability.allowed";

        #endregion Fields

        #region Oxide Hooks

        private void Init() => permission.RegisterPermission(PERMISSION_USE, this);

        private void OnLoseCondition(Item item, ref float amount)
        {
            if (item == null) return;
            if (configData.itemListIsBlackList
                ? configData.itemList.Contains(item.info.shortname)
                : !configData.itemList.Contains(item.info.shortname))
            {
                return;
            }
            var player = item.GetOwnerPlayer() ?? item.GetRootContainer()?.GetOwnerPlayer();
            if (player == null || !permission.UserHasPermission(player.UserIDString, PERMISSION_USE)) return;
            if (configData.useZoneManager && ZoneManager != null)
            {
                var zoneIDs = GetPlayerZoneIDs(player);
                if (zoneIDs != null && zoneIDs.Length > 0)
                {
                    if (configData.excludeAllZone)
                    {
                        return;
                    }
                    if (configData.excludeDynPVPZone && DynamicPVP != null)
                    {
                        foreach (var zoneId in zoneIDs)
                        {
                            if (IsPlayerInZone(zoneId, player) && IsDynamicPVPZone(zoneId))
                            {
                                return;
                            }
                        }
                        return;
                    }

                    foreach (var zoneId in configData.zoneList)
                    {
                        if (IsPlayerInZone(zoneId, player))
                        {
                            return;
                        }
                    }
                }
            }

            amount = 0;
            if (configData.keepMaxDurability)
            {
                item.condition = item.maxCondition;
            }
        }

        #endregion Oxide Hooks

        #region Methods

        private bool IsDynamicPVPZone(string zoneID) => (bool)DynamicPVP.Call("IsDynamicPVPZone", zoneID);

        private bool IsPlayerInZone(string zoneID, BasePlayer player) => (bool)ZoneManager.Call("IsPlayerInZone", zoneID, player);

        private string[] GetPlayerZoneIDs(BasePlayer player) => (string[])ZoneManager.Call("GetPlayerZoneIDs", player);

        #endregion Methods

        #region ConfigurationFile

        private ConfigData configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Use ZoneManager")]
            public bool useZoneManager;

            [JsonProperty(PropertyName = "Keep Max Durability")]
            public bool keepMaxDurability = true;

            [JsonProperty(PropertyName = "Exclude all zone")]
            public bool excludeAllZone;

            [JsonProperty(PropertyName = "Exclude dynamic pvp zone")]
            public bool excludeDynPVPZone;

            [JsonProperty(PropertyName = "Zone exclude list (Zone ID)")]
            public HashSet<string> zoneList = new HashSet<string>();

            [JsonProperty(PropertyName = "Item list (Item short name)")]
            public HashSet<string> itemList = new HashSet<string>();

            [JsonProperty(PropertyName = "Item list is a blacklist? (If false, it's is a whitelist)")]
            public bool itemListIsBlackList = true;
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

        #endregion ConfigurationFile
    }
}