using Newtonsoft.Json;
using System.Collections.Generic;

/*
 * Rewritten from scratch and maintained to present by VisEntities
 * Originally created by Orange, up to version 1.1.1
 */

namespace Oxide.Plugins
{
    [Info("No Pickup Penalty", "VisEntities", "2.0.0")]
    [Description("Disables condition loss for deployables when picked up.")]
    public class NoPickupPenalty : RustPlugin
    {
        #region Fields

        private static NoPickupPenalty _plugin;
        private static Configuration _config;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Unbreakable Entities")]
            public List<string> UnbreakableEntities { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                UnbreakableEntities = new List<string>()
                {
                    "furnace",
                    "composter",
                    "box.wooden.large",
                    "workbench1.deployed",
                    "workbench2.deployed",
                    "workbench3.deployed",
                    "woodbox_deployed"
                },
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
        }

        private void Unload()
        {
            _config = null;
            _plugin = null;
        }

        private object CanPickupEntity(BasePlayer player, BaseCombatEntity entity)
        {
            if (player != null && entity != null && PermissionUtil.HasPermission(player, PermissionUtil.USE))
            {
                if (_config.UnbreakableEntities.Contains(entity.ShortPrefabName))
                    entity.pickup.subtractCondition = 0f;
            }

            return null;
        }

        #endregion Oxide Hooks

        #region Permission

        private static class PermissionUtil
        {
            public const string USE = "nopickuppenalty.use";
            private static readonly List<string> _permissions = new List<string>
            {
                USE,
            };

            public static void RegisterPermissions()
            {
                foreach (var permission in _permissions)
                {
                    _plugin.permission.RegisterPermission(permission, _plugin);
                }
            }

            public static bool HasPermission(BasePlayer player, string permissionName)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permission
    }
}