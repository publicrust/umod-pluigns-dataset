using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oxide.Plugins
{
    [Info("Limited Admin", "misticos", "1.0.8")]
    [Description("Prevents admin abuse by blocking actions and commands")]
    class LimitedAdmin : RustPlugin
    {
        #region Configuration

        private Configuration _config = new Configuration();

        private class Configuration
        {
            [JsonProperty(PropertyName = "Limited Admins (SteamID)",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> Admins = new List<ulong>
            {
                76000000000000
            };

            [JsonProperty(PropertyName = "Limited Auth Levels",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<uint> LimitedAuthLevels = new List<uint>
            {
                1, 2
            };

            [JsonProperty(PropertyName = "Whitelisted Admins (SteamID)",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> ExcludedAdmins = new List<ulong>
            {
                76000000000000
            };

            [JsonProperty(PropertyName = "Limit All Admins Exclude Whitelisted")]
            public bool LimitAll = true;

            [JsonProperty(PropertyName = "Blocked Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Blacklist = new List<string>
            {
                "chat.say"
            };

            [JsonProperty(PropertyName = "Can Loot Entity")]
            public bool CanLootEntity = false;

            [JsonProperty(PropertyName = "Can Loot Player")]
            public bool CanLootPlayer = false;

            [JsonProperty(PropertyName = "Can Pickup Entity")]
            public bool CanPickupEntity = false;

            [JsonProperty(PropertyName = "Can Rename Bed")]
            public bool CanRenameBed = false;

            [JsonProperty(PropertyName = "Can Use Locked Entity")]
            public bool CanUseLockedEntity = false;

            [JsonProperty(PropertyName = "Can Interact With Lock")]
            public bool CanInteractLock = false;

            [JsonProperty(PropertyName = "Can Use Voice Chat")]
            public bool CanUseVoiceChat = false;

            [JsonProperty(PropertyName = "Can Be Targeted")]
            public bool CanBeTargeted = false;

            [JsonProperty(PropertyName = "Can Build")]
            public bool CanBuild = false;

            [JsonProperty(PropertyName = "Can Interact With Structure")]
            public bool CanInteractStructure = false;

            [JsonProperty(PropertyName = "Can Hack CH47 Crate")]
            public bool CanHackCrate = false;

            [JsonProperty(PropertyName = "Can Interact With Item")]
            public bool CanInteractItem = false;

            [JsonProperty(PropertyName = "Can Pickup Item")]
            public bool CanItemPickup = false;

            [JsonProperty(PropertyName = "Can Damage")]
            public bool CanDamage = false;

            [JsonProperty(PropertyName = "Can Use Lift")]
            public bool CanUseLift = false;

            [JsonProperty(PropertyName = "Can Toggle Oven")]
            public bool CanToggleOven = false;

            [JsonProperty(PropertyName = "Can Toggle Recycler")]
            public bool CanToggleRecycler = false;

            [JsonProperty(PropertyName = "Can Interact With Turret")]
            public bool CanInteractTurret = false;

            [JsonProperty(PropertyName = "Can Gather")]
            public bool CanGather = false;

            [JsonProperty(PropertyName = "Can Update Sign")]
            public bool CanUpdateSign = false;

            [JsonProperty(PropertyName = "Can Interact With Cupboard")]
            public bool CanInteractCupboard = false;

            [JsonProperty(PropertyName = "Can Interact With Vending Machine")]
            public bool CanInteractVending = false;

            [JsonProperty(PropertyName = "Can Interact With Weapons")]
            public bool CanInteractWeapons = false;

            [JsonProperty(PropertyName = "Debug")]
            public bool Debug = false;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Hooks

        private void OnServerInitialized()
        {
            if (_config.CanLootEntity)
                Unsubscribe(nameof(CanLootEntity));

            if (_config.CanLootPlayer)
                Unsubscribe(nameof(CanLootPlayer));

            if (_config.CanPickupEntity)
                Unsubscribe(nameof(CanPickupEntity));

            if (_config.CanRenameBed)
                Unsubscribe(nameof(CanRenameBed));

            if (_config.CanUseLockedEntity)
                Unsubscribe(nameof(CanUseLockedEntity));

            if (_config.CanUseVoiceChat)
                Unsubscribe(nameof(OnPlayerVoice));

            if (_config.CanBeTargeted)
            {
                Unsubscribe(nameof(CanBeTargeted));
                Unsubscribe(nameof(CanBradleyApcTarget));
                Unsubscribe(nameof(CanHelicopterStrafeTarget));
                Unsubscribe(nameof(CanHelicopterTarget));
                Unsubscribe(nameof(OnNpcTarget));
                Unsubscribe(nameof(OnTurretTarget));
            }

            if (_config.CanBuild)
                Unsubscribe(nameof(CanBuild));

            if (_config.CanInteractStructure)
            {
                Unsubscribe(nameof(OnStructureDemolish));
                Unsubscribe(nameof(OnStructureRepair));
                Unsubscribe(nameof(OnStructureUpgrade));
                Unsubscribe(nameof(OnStructureRotate));
            }

            if (_config.CanHackCrate)
                Unsubscribe(nameof(CanHackCrate));

            if (_config.CanInteractItem)
                Unsubscribe(nameof(OnItemAction));

            if (_config.CanItemPickup)
                Unsubscribe(nameof(OnItemPickup));

            if (_config.CanDamage)
                Unsubscribe(nameof(OnEntityTakeDamage));

            if (_config.CanUseLift)
                Unsubscribe(nameof(OnLiftUse));

            if (_config.CanToggleOven)
                Unsubscribe(nameof(OnOvenToggle));

            if (_config.CanToggleRecycler)
                Unsubscribe(nameof(OnRecyclerToggle));

            if (_config.CanInteractTurret)
            {
                Unsubscribe(nameof(OnTurretAuthorize));
                Unsubscribe(nameof(OnTurretDeauthorize));
                Unsubscribe(nameof(OnTurretClearList));
            }

            if (_config.CanGather)
            {
                Unsubscribe(nameof(OnCollectiblePickup));
                Unsubscribe(nameof(OnDispenserBonus));
                Unsubscribe(nameof(OnDispenserGather));
                Unsubscribe(nameof(OnGrowableGather));
            }

            if (_config.CanUpdateSign)
                Unsubscribe(nameof(CanUpdateSign));

            if (_config.CanInteractLock)
            {
                Unsubscribe(nameof(CanLock));
                Unsubscribe(nameof(CanUnlock));
                Unsubscribe(nameof(OnCodeEntered));
                Unsubscribe(nameof(CanChangeCode));
            }

            if (_config.CanInteractCupboard)
            {
                Unsubscribe(nameof(OnCupboardAuthorize));
                Unsubscribe(nameof(OnCupboardClearList));
                Unsubscribe(nameof(OnCupboardDeauthorize));
            }

            if (_config.CanInteractVending)
            {
                Unsubscribe(nameof(CanAdministerVending));
                Unsubscribe(nameof(CanUseVending));
                Unsubscribe(nameof(OnRotateVendingMachine));
            }

            if (_config.CanInteractWeapons)
            {
                Unsubscribe(nameof(CanCreateWorldProjectile));
                Unsubscribe(nameof(OnReloadMagazine));
                Unsubscribe(nameof(OnReloadWeapon));
                Unsubscribe(nameof(OnSwitchAmmo));
            }
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !IsLimited(player))
                return null;

            var command = arg.cmd.FullName;
            return _config.Blacklist.Contains(command) ? false : (object) null;
        }

        #region Can Loot Entity

        private object CanLootEntity(BasePlayer player, Object container) =>
            IsLimited(player) ? false : (object) null;

        #endregion

        private object CanLootPlayer(BasePlayer target, BasePlayer looter) => IsLimited(looter) ? false : (object) null;

        private object CanPickupEntity(BasePlayer player, BaseCombatEntity entity) =>
            IsLimited(player) ? false : (object) null;

        private object CanRenameBed(BasePlayer player, SleepingBag bed, string bedName) =>
            IsLimited(player) ? false : (object) null;

        private object CanUseLockedEntity(BasePlayer player, BaseLock baseLock) =>
            IsLimited(player) ? false : (object) null;

        private object OnPlayerVoice(BasePlayer player, byte[] data) => IsLimited(player) ? false : (object) null;

        #region Can Be Targeted

        private object CanBeTargeted(BaseCombatEntity entity, MonoBehaviour behaviour)
        {
            var player = entity as BasePlayer;
            return player == null || player.IsNpc || player.net?.connection == null || !IsLimited(player)
                ? (object) null
                : false;
        }

        private object CanBradleyApcTarget(BradleyAPC apc, BaseEntity entity)
        {
            var player = entity as BasePlayer;
            return player == null || player.IsNpc || player.net?.connection == null || !IsLimited(player)
                ? (object) null
                : false;
        }

        private object CanHelicopterStrafeTarget(PatrolHelicopterAI entity, BasePlayer target) =>
            IsLimited(target) ? false : (object) null;

        private object CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        private object OnNpcTarget(BaseNpc npc, BaseEntity entity)
        {
            var player = entity as BasePlayer;
            return player == null || player.IsNpc || player.net?.connection == null || !IsLimited(player)
                ? (object) null
                : false;
        }

        private object OnTurretTarget(AutoTurret turret, BaseCombatEntity entity)
        {
            var player = entity as BasePlayer;
            return player == null || player.IsNpc || player.net?.connection == null || !IsLimited(player)
                ? (object) null
                : false;
        }

        #endregion

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target) =>
            IsLimited(planner.GetOwnerPlayer()) ? false : (object) null;

        #region Can Interact With Structure

        private object OnStructureDemolish(BaseCombatEntity entity, BasePlayer player, bool immediate) =>
            IsLimited(player) ? false : (object) null;

        private object OnStructureRepair(BaseCombatEntity entity, BasePlayer player, bool immediate) =>
            IsLimited(player) ? false : (object) null;

        private object OnStructureRotate(BaseCombatEntity entity, BasePlayer player, bool immediate) =>
            IsLimited(player) ? false : (object) null;

        private object OnStructureUpgrade(BaseCombatEntity entity, BasePlayer player, bool immediate) =>
            IsLimited(player) ? false : (object) null;

        #endregion

        private object CanHackCrate(BasePlayer player, HackableLockedCrate crate) =>
            IsLimited(player) ? false : (object) null;

        private object OnItemAction(Item item, string action, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        private object OnItemPickup(Item item, BasePlayer player) => IsLimited(player) ? false : (object) null;

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var player = info.InitiatorPlayer;
            return player == null || player.IsNpc || player.net?.connection == null || !IsLimited(player)
                ? (object) null
                : false;
        }

        #region Can Use Lift

        private object OnLiftUse(Object lift, BasePlayer player) => IsLimited(player) ? false : (object) null;

        #endregion

        private object OnOvenToggle(BaseOven oven, BasePlayer player) => IsLimited(player) ? false : (object) null;

        private object OnRecyclerToggle(Recycler recycler, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        #region Can Interact With Turret

        private object OnTurretAuthorize(AutoTurret turret, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        private object OnTurretClearList(AutoTurret turret, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        private object OnTurretDeauthorize(AutoTurret turret, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        #endregion

        #region Can Gather

        private object OnCollectiblePickup(Item item, BasePlayer player) => IsLimited(player) ? false : (object) null;

        private object OnGrowableGather(GrowableEntity plant, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        private object OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item) =>
            IsLimited(player) ? false : (object) null;

        private object OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            var player = entity as BasePlayer;
            return player == null || player.IsNpc || player.net?.connection == null || !IsLimited(player)
                ? (object) null
                : false;
        }

        #endregion

        private object CanUpdateSign(BasePlayer player, Signage sign) => IsLimited(player) ? (object) false : null;

        #region Can Interact With Lock

        private object CanLock(BasePlayer player, BaseLock baseLock) => IsLimited(player) ? false : (object) null; 

        private object CanUnlock(BasePlayer player, BaseLock baseLock) => IsLimited(player) ? false : (object) null;

        private object OnCodeEntered(CodeLock codeLock, BasePlayer player, string code) =>
            IsLimited(player) ? false : (object) null;

        private object CanChangeCode(CodeLock codeLock, BasePlayer player, string newCode, bool isGuestCode) =>
            IsLimited(player) ? false : (object) null;

        private object CanPickupLock(BasePlayer player, BaseLock baseLock) =>
            IsLimited(player) ? false : (object) null;
        
        #endregion
        
        #region Can Interact With Cupboard

        private object OnCupboardAuthorize(BuildingPrivlidge privilege, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        private object OnCupboardClearList(BuildingPrivlidge privilege, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        private object OnCupboardDeauthorize(BuildingPrivlidge privilege, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;
        
        #endregion
        
        #region Can Interact With Vending Machine

        private object CanAdministerVending(BasePlayer player, VendingMachine machine) =>
            IsLimited(player) ? false : (object) null;

        private object CanUseVending(VendingMachine machine, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;

        private object OnRotateVendingMachine(VendingMachine machine, BasePlayer player) =>
            IsLimited(player) ? false : (object) null;
        
        #endregion
        
        #region Can Interact With Weapons

        private object CanCreateWorldProjectile(HitInfo info, ItemDefinition itemDef)
        {
            var player = info.InitiatorPlayer;
            return player == null || player.IsNpc || player.net?.connection == null || !IsLimited(player)
                ? (object) null
                : false;
        }

        private object OnReloadMagazine(BasePlayer player, BaseProjectile projectile) =>
            IsLimited(player) ? false : (object) null;

        private object OnReloadWeapon(BasePlayer player, BaseProjectile projectile) =>
            IsLimited(player) ? false : (object) null;

        private object OnSwitchAmmo(BasePlayer player, BaseProjectile projectile) =>
            IsLimited(player) ? false : (object) null;
        
        #endregion
        
        #endregion
        
        #region Helpers

        private bool IsLimited(BasePlayer player)
        {
            if (player == null || player.net?.connection == null)
                return false;

            if (_config.Admins.Contains(player.userID))
                return true;

            if (_config.LimitAll && _config.ExcludedAdmins.Contains(player.userID))
                return false;

            if (_config.LimitAll && _config.LimitedAuthLevels.Contains(player.net.connection.authLevel))
                return true;

            return false;
        }

        private void PrintDebug(string message)
        {
            if (_config.Debug)
                Interface.Oxide.LogDebug(message);
        }

        #endregion
    }
}