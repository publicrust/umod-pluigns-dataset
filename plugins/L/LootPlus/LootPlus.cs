using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ConVar;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Rust;
using UnityEngine;
using Physics = UnityEngine.Physics;
using Pool = Facepunch.Pool;
using Random = System.Random;

namespace Oxide.Plugins
{
    [Info("Loot Plus", "Iv Misticos", "2.2.6")]
    [Description("Modify loot on your server.")]
    public class LootPlus : RustPlugin
    {
        #region Variables

        private static LootPlus _ins;

        private Random _random = new Random();

        private bool _initialized = false;

        private const string PermissionLootSave = "lootplus.lootsave";

        private const string PermissionLootRefill = "lootplus.lootrefill";

        private const string PermissionLoadConfig = "lootplus.loadconfig";

        #endregion

        #region Configuration

        private static Configuration _config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Refill Loot On Plugin Load")]
            public bool RefillOnLoad = true;

            [JsonProperty(PropertyName = "Process Corpses", NullValueHandling = NullValueHandling.Ignore)]
            public bool? ProcessCorpses = null;

            [JsonProperty(PropertyName = "Process Loot Containers", NullValueHandling = NullValueHandling.Ignore)]
            public bool? ProcessLootContainers = null;

            [JsonProperty(PropertyName = "Container Loot Save Command")]
            public string LootSaveCommand = "lootsave";

            [JsonProperty(PropertyName = "Container Refill Command")]
            public string LootRefillCommand = "lootrefill";

            [JsonProperty(PropertyName = "Load Config Command")]
            public string LoadConfigCommand = "lootconfig";

            [JsonProperty(PropertyName = "Containers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ContainerData> Containers = new List<ContainerData> {new ContainerData()};

            [JsonProperty(PropertyName = "Debug")]
            public bool Debug = false;
        }

        private class ContainerData
        {
            [JsonProperty(PropertyName = "Entity Shortnames", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ShortnameData> Shortnames = new List<ShortnameData>
            {
                new ShortnameData()
            };

            [JsonProperty(PropertyName = "Process Corpses")]
            public bool ProcessCorpses = true;

            [JsonProperty(PropertyName = "Process Loot Containers")]
            public bool ProcessLootContainers = true;

            [JsonProperty(PropertyName = "Monument Prefabs", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Monuments = new List<string> {string.Empty};

            [JsonProperty(PropertyName = "Shuffle Items")]
            public bool ShuffleItems = true;

            [JsonProperty(PropertyName = "Allow Duplicate Items")]
            public bool AllowDuplicateItems = false;

            [JsonProperty(PropertyName = "Allow Duplicate Items With Different Skins")]
            public bool AllowDuplicateItemsDifferentSkins = true;

            [JsonProperty(PropertyName = "Remove Container")]
            public bool RemoveContainer = false;

            [JsonProperty(PropertyName = "Item Container Indexes",
                ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<int> ContainerIndexes = new List<int> {0};

            [JsonProperty(PropertyName = "Replace Items")]
            public bool ReplaceItems = true;

            [JsonProperty(PropertyName = "Add Items")]
            public bool AddItems = false;

            [JsonProperty(PropertyName = "Modify Items")]
            public bool ModifyItems = false;

            [JsonProperty(PropertyName = "Modify Ignores Blueprint State")]
            public bool ModifyIgnoreBlueprint = false;

            [JsonProperty(PropertyName = "Fill With Default Items")]
            public bool DefaultLoot = false;

            [JsonProperty(PropertyName = "Online Condition")]
            public OnlineData Online = new OnlineData();

            [JsonProperty(PropertyName = "Maximal Failures To Add An Item")]
            public int MaxRetries = 5;

            [JsonProperty(PropertyName = "Capacity", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<CapacityData> Capacity = new List<CapacityData> {new CapacityData()};

            [JsonProperty(PropertyName = "Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ItemData> Items = new List<ItemData> {new ItemData()};

            public bool ShortnameFits(string shortname, bool api)
            {
                var fits = false;

                for (var i = 0; i < Shortnames.Count; i++)
                {
                    var shortnameData = Shortnames[i];
                    if (shortnameData.API != api)
                        continue;

                    var fitsExpression = shortnameData.FitsExpression(shortname);
                    if (!fitsExpression)
                        continue;

                    if (shortnameData.Exclude)
                        return false;

                    fits = true;
                }

                return fits;
            }

            public class ShortnameData
            {
                [JsonProperty(PropertyName = "Shortname")]
                public string Shortname = "entity.shortname";

                [JsonProperty(PropertyName = "Enable Regex")]
                public bool Regex = false;

                [JsonProperty(PropertyName = "Exclude")]
                public bool Exclude = false;

                [JsonProperty(PropertyName = "API Shortname")]
                public bool API = false;

                [JsonIgnore]
                public Regex ParsedRegex;

                public bool FitsExpression(string shortname)
                {
                    if (Regex)
                    {
                        return ParsedRegex.IsMatch(shortname);
                    }

                    return Shortname == "global" || Shortname == shortname;
                }
            }
        }

        private class ItemData : ChanceData
        {
            [JsonProperty(PropertyName = "Item Shortname", NullValueHandling = NullValueHandling.Ignore)]
            public string Shortname = null;

            [JsonProperty(PropertyName = "Item Shortnames", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ShortnameData> Shortnames = new List<ShortnameData>
            {
                new ShortnameData()
            };

            [JsonProperty(PropertyName = "Item Name (Empty To Ignore)")]
            public string Name = "";

            [JsonProperty(PropertyName = "Is Blueprint")]
            public bool IsBlueprint = false;

            [JsonProperty(PropertyName = "Allow Stacking")]
            public bool AllowStacking = true;

            [JsonProperty(PropertyName = "Ignore Max Stack")]
            public bool IgnoreStack = true;

            [JsonProperty(PropertyName = "Ignore Max Condition")]
            public bool IgnoreCondition = true;

            [JsonProperty(PropertyName = "Remove Item")]
            public bool RemoveItem = false;

            [JsonProperty(PropertyName = "Replace Item With Default Loot")]
            public bool ReplaceDefaultLoot = false;

            [JsonProperty(PropertyName = "Conditions", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ConditionData> Conditions = new List<ConditionData> {new ConditionData()};

            [JsonProperty(PropertyName = "Skins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<SkinData> Skins = new List<SkinData> {new SkinData()};

            [JsonProperty(PropertyName = "Amount", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<AmountData> Amount = new List<AmountData> {new AmountData()};

            public bool ShortnameFits(Rarity rarity, ItemCategory category, string shortname)
            {
                var fits = false;

                for (var i = 0; i < Shortnames.Count; i++)
                {
                    var shortnameData = Shortnames[i];
                    var fitsExpression = shortnameData.FitsExpression(rarity, category, shortname);
                    if (!fitsExpression)
                        continue;

                    if (shortnameData.Exclude)
                        return false;

                    fits = true;
                }

                return fits;
            }

            public bool ShortnameFits(Item item, ContainerData container)
            {
                if (!container.ModifyIgnoreBlueprint && item.IsBlueprint() != IsBlueprint)
                    return false;

                return ShortnameFits(item.IsBlueprint() ? item.blueprintTargetDef.rarity : item.info.rarity,
                    item.IsBlueprint() ? item.blueprintTargetDef.category : item.info.category,
                    item.IsBlueprint() ? item.blueprintTargetDef.shortname : item.info.shortname);
            }

            public class ShortnameData
            {
                [JsonProperty(PropertyName = "Shortname")]
                public string Shortname = "item.shortname";
                
                [JsonProperty(PropertyName = "Rarity")]
                public string Rarity = "global";
                
                [JsonProperty(PropertyName = "Category")]
                public string Category = "global";

                [JsonProperty(PropertyName = "Enable Regex")]
                public bool Regex = false;

                [JsonProperty(PropertyName = "Exclude")]
                public bool Exclude = false;

                [JsonIgnore]
                public Regex ParsedRegex;

                public bool FitsExpression(Rarity rarity, ItemCategory category, string shortname)
                {
                    if (Rarity != "global" && Rarity != rarity.ToString())
                        return false;

                    if (Category != "global" && Category != category.ToString())
                        return false;
                    
                    if (Regex)
                    {
                        return ParsedRegex.IsMatch(shortname);
                    }

                    return Shortname == "global" || Shortname == shortname;
                }
            }
        }

        #region Additional

        private class ConditionData : ChanceData
        {
            [JsonProperty(PropertyName = "Condition", NullValueHandling = NullValueHandling.Ignore)]
            public float? Condition = null;

            [JsonProperty(PropertyName = "Minimal Condition")]
            public float MinCondition = 75f;

            [JsonProperty(PropertyName = "Maximal Condition")]
            public float MaxCondition = 100f;

            [JsonProperty(PropertyName = "Max Condition Of Item")]
            public float MaxItemCondition = -1f;

            [JsonProperty(PropertyName = "Condition Rate")]
            public float ConditionRate = -1f;

            [JsonProperty(PropertyName = "Max Condition Rate")]
            public float MaxItemConditionRate = -1f;

            public void Modify(ref float condition, ref float maxCondition)
            {
                if (MaxItemCondition > 0f)
                    maxCondition = MaxItemCondition;

                if (MaxItemConditionRate > 0f)
                    maxCondition *= MaxItemConditionRate;

                if (MinCondition > 0 && MaxCondition > 0)
                    condition = (float) (_ins._random.NextDouble() * (MaxCondition - MinCondition) + MinCondition);

                if (ConditionRate > 0f)
                    condition *= ConditionRate;
            }
        }

        private class SkinData : ChanceData
        {
            [JsonProperty(PropertyName = "Skin")]
            // ReSharper disable once RedundantDefaultMemberInitializer
            public ulong Skin = 0;
        }

        private class AmountData : ChanceData
        {
            [JsonProperty(PropertyName = "Amount", NullValueHandling = NullValueHandling.Ignore)]
            public int? Amount = null;

            [JsonProperty(PropertyName = "Minimal Amount")]
            public int MinAmount = 3;

            [JsonProperty(PropertyName = "Maximal Amount")]
            public int MaxAmount = 3;

            [JsonProperty(PropertyName = "Rate")]
            public float Rate = -1f;

            public void Modify(ref int amount)
            {
                if (MinAmount > 0 && MaxAmount > 0)
                    amount = _ins._random.Next(MinAmount, MaxAmount + 1);

                if (Rate > 0)
                    amount = (int) (amount * Rate);
            }
        }

        private class CapacityData : ChanceData
        {
            [JsonProperty(PropertyName = "Capacity")]
            public int Capacity = 3;
        }

        private class OnlineData
        {
            // sorry, dont want anything in config to be "private" :)
            // ReSharper disable MemberCanBePrivate.Local
            [JsonProperty(PropertyName = "Minimal Online")]
            public int MinOnline = -1;

            [JsonProperty(PropertyName = "Maximal Online")]
            public int MaxOnline = -1;
            // ReSharper restore MemberCanBePrivate.Local

            public bool IsOkay()
            {
                var online = BasePlayer.activePlayerList.Count;
                return MinOnline == -1 && MaxOnline == -1 || online > MinOnline && online < MaxOnline;
            }
        }

        public class ChanceData
        {
            [JsonProperty(PropertyName = "Chance")]
            // ReSharper disable once MemberCanBePrivate.Global
            public int Chance = 1;

            public static T Select<T>(IReadOnlyList<T> data) where T : ChanceData
            {
                // xD

                if (data == null)
                {
                    return null;
                }

                if (data.Count == 0)
                {
                    return null;
                }

                var sum1 = 0;
                for (var i = 0; i < data.Count; i++)
                {
                    var entry = data[i];
                    sum1 += entry?.Chance ?? 0;
                }

                if (sum1 < 1)
                {
                    return null;
                }

                var random = _ins._random.Next(1, sum1 + 1); // include the sum1 number itself and exclude the 0

                var sum2 = 0;
                for (var i = 0; i < data.Count; i++)
                {
                    var entry = data[i];
                    sum2 += entry?.Chance ?? 0;
                    if (random <= sum2)
                        return entry;
                }

                return null;
            }
        }

        #endregion

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
            
                for (var i = 0; i < _config.Containers.Count; i++)
                {
                    var container = _config.Containers[i];

                    if (_config.ProcessCorpses.HasValue)
                    {
                        container.ProcessCorpses = _config.ProcessCorpses.Value;
                    }

                    if (_config.ProcessLootContainers.HasValue)
                    {
                        container.ProcessLootContainers = _config.ProcessLootContainers.Value;
                    }

                    for (var j = 0; j < container.Items.Count; j++)
                    {
                        var item = container.Items[j];
                        if (item.Shortname != null)
                        {
                            if (!string.IsNullOrEmpty(item.Shortname))
                            {
                                item.Shortnames.Add(new ItemData.ShortnameData
                                {
                                    Shortname = item.Shortname,
                                    Exclude = false,
                                    Regex = false
                                });
                            }

                            item.Shortname = null;
                        }

                        for (var k = 0; k < item.Amount.Count; k++)
                        {
                            var amount = item.Amount[k];
                            if (!amount.Amount.HasValue)
                                continue;

                            amount.MinAmount = amount.Amount.Value;
                            amount.MaxAmount = amount.Amount.Value;
                            amount.Amount = null;
                        }

                        for (var k = 0; k < item.Conditions.Count; k++)
                        {
                            var condition = item.Conditions[k];
                            if (!condition.Condition.HasValue)
                                continue;

                            condition.MinCondition = condition.Condition.Value;
                            condition.MaxCondition = condition.Condition.Value;
                            condition.Condition = null;
                        }

                        for (var k = 0; k < item.Shortnames.Count; k++)
                        {
                            var shortname = item.Shortnames[k];
                            if (!shortname.Regex)
                                continue;

                            shortname.ParsedRegex = new Regex(shortname.Shortname);
                        }
                    }

                    for (var j = 0; j < container.Shortnames.Count; j++)
                    {
                        var shortname = container.Shortnames[j];
                        if (!shortname.Regex)
                            continue;

                        shortname.ParsedRegex = new Regex(shortname.Shortname);
                    }
                }

                if (_config.ProcessCorpses.HasValue)
                {
                    _config.ProcessCorpses = null;
                }

                if (_config.ProcessLootContainers.HasValue)
                {
                    _config.ProcessLootContainers = null;
                }
            
                SaveConfig();
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

        #region Commands

        private void CommandLootSave(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if (player == null)
            {
                iplayer.Reply(GetMsg("In-Game Only", iplayer.Id));
                return;
            }

            if (!iplayer.HasPermission(PermissionLootSave))
            {
                iplayer.Reply(GetMsg("No Permission", iplayer.Id));
                return;
            }

            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 10f) || hit.GetEntity() == null ||
                !(hit.GetEntity() is LootContainer))
            {
                iplayer.Reply(GetMsg("No Loot Container", iplayer.Id));
                return;
            }

            var container = hit.GetEntity() as LootContainer;
            if (container == null || container.inventory == null)
            {
                // Shouldn't really happen
                return;
            }

            var inventory = player.inventory.containerMain;

            var containerData = new ContainerData
            {
                ModifyItems = false,
                AddItems = false,
                ReplaceItems = true,
                Shortnames = new List<ContainerData.ShortnameData>
                {
                    new ContainerData.ShortnameData
                    {
                        Shortname = container.ShortPrefabName,
                        Exclude = false,
                        Regex = false,
                        API = false
                    }
                },
                Capacity = new List<CapacityData>
                {
                    new CapacityData
                    {
                        Capacity = inventory.itemList.Count
                    }
                },
                Items = new List<ItemData>(),
                MaxRetries = inventory.itemList.Count * 10
            };

            for (var i = 0; i < inventory.itemList.Count; i++)
            {
                var item = inventory.itemList[i];
                var isBlueprint = item.IsBlueprint();
                var itemData = new ItemData
                {
                    Amount = new List<AmountData>
                    {
                        new AmountData
                        {
                            MinAmount = item.amount,
                            MaxAmount = item.amount
                        }
                    },
                    Conditions = new List<ConditionData>(),
                    Skins = new List<SkinData>
                    {
                        new SkinData
                        {
                            Skin = item.skin
                        }
                    },
                    Shortnames = new List<ItemData.ShortnameData>
                    {
                        new ItemData.ShortnameData
                        {
                            Shortname = isBlueprint ? item.blueprintTargetDef.shortname : item.info.shortname
                        }
                    },
                    Name = item.name ?? string.Empty, // yes but it doesnt work :( lol?
                    AllowStacking = true,
                    IsBlueprint = isBlueprint
                };

                if (!isBlueprint)
                {
                    if (item.hasCondition)
                        itemData.Conditions.Add(new ConditionData
                        {
                            MinCondition = item.condition,
                            MaxCondition = item.condition,
                            MaxItemCondition = item.maxCondition
                        });
                }

                containerData.Items.Add(itemData);
            }

            _config.Containers.Add(containerData);
            SaveConfig();

            iplayer.Reply(GetMsg("Loot Container Saved", iplayer.Id));
        }

        private void CommandLootRefill(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PermissionLootRefill))
            {
                player.Reply(GetMsg("No Permission", player.Id));
                return;
            }

            player.Reply(GetMsg("Loot Refill Started", player.Id));
            LootRefill();
        }

        private void CommandLoadConfig(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PermissionLoadConfig))
            {
                player.Reply(GetMsg("No Permission", player.Id));
                return;
            }

            LoadConfig(); // What if something has changed there? :o

            player.Reply(GetMsg("Config Loaded", player.Id));
        }

        #endregion

        #region Hooks

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"In-Game Only", "Please, use this only while you're in the game"},
                {"No Permission", "You don't have enough permissions"},
                {"No Loot Container", "Please, look at the loot container in 10m"},
                {"Loot Container Saved", "You have saved this loot container data to configuration"},
                {"Loot Refill Started", "Loot refill process just started"},
                {"Config Loaded", "Your configuration was loaded"}
            }, this);
        }

        private void Init()
        {
            _ins = this;
            
            permission.RegisterPermission(PermissionLootSave, this);
            permission.RegisterPermission(PermissionLootRefill, this);
            permission.RegisterPermission(PermissionLoadConfig, this);

            AddCovalenceCommand(_config.LootSaveCommand, nameof(CommandLootSave));
            AddCovalenceCommand(_config.LootRefillCommand, nameof(CommandLootRefill));
            AddCovalenceCommand(_config.LoadConfigCommand, nameof(CommandLoadConfig));
        }

        private void OnServerInitialized()
        {
            _ins = this;
            _initialized = true;
            
            if (!_config.RefillOnLoad)
            {
                PrintWarning("Loot refill on plugin load is disabled in configuration");
                return;
            }

            NextFrame(LootRefill);
        }

        private void Unload()
        {
            _initialized = false;
            
            // LOOT IS BACK
            using (var entitiesEnumerator = BaseNetworkable.serverEntities.GetEnumerator())
            {
                while (entitiesEnumerator.MoveNext())
                {
                    var entity = entitiesEnumerator.Current;
                    var lootContainer = entity as LootContainer;
                    if (lootContainer == null)
                        continue;

                    if (lootContainer is Stocking && !XMas.enabled)
                    {
                        PrintDebug("Stocking entity but XMas is disabled");
                        continue;
                    }

                    if (lootContainer.shouldRefreshContents && !lootContainer.IsInvoking(lootContainer.SpawnLoot))
                    {
                        PrintDebug("Entity should refresh content but does NOT.");
                        continue;
                    }

                    PrintDebug(
                        $"Restoring loot for {lootContainer.ShortPrefabName}. SRC: {lootContainer.shouldRefreshContents} / II: {lootContainer.IsInvoking(lootContainer.SpawnLoot)}");
                    
                    // Creating an inventory
                    if (lootContainer.inventory == null)
                    {
                        lootContainer.CreateInventory(true);
                    }
                    else
                    {
                        lootContainer.inventory.Clear();
                        ItemManager.DoRemoves();
                    }

                    // Spawning loot
                    lootContainer.SpawnLoot();

                    // Changing the capacity
                    lootContainer.inventory.capacity = lootContainer.inventory.itemList.Count;

                    // no i wont do anything with npc and other containers >:(
                }
            }

            _ins = null;
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (!_initialized)
                return;
            
            if (entity is LootContainer)
            {
                PrintDebug("Entity is a LootContainer. Waiting for OnLootSpawn");
                return; // So this entity's content is modified in OnLootSpawn
            }
            
            OnLootSpawn(entity);
        }

        private void OnLootSpawn(BaseNetworkable entity)
        {
            if (!_initialized || entity == null)
                return;

            var lootContainer = entity as LootContainer;
            if (lootContainer != null)
            {
                RunLootHandler(entity, lootContainer.inventory, -1);
                return;
            }
            
            var corpse = entity as LootableCorpse;
            if (corpse != null)
            {
                if (corpse.containers == null || corpse.containers.Length == 0)
                {
                    PrintDebug("Entity has no containers");
                    return;
                }
                
                for (var i = 0; i < corpse.containers.Length; i++)
                {
                    var inventory = corpse.containers[i];
                    RunLootHandler(entity, inventory, i);
                }
            }
        }

        #endregion
        
        #region API

        private void FillContainer(ItemContainer container, string apiShortname, int containerIndex = -1)
        {
            HandleAPIFill(container, apiShortname, containerIndex);
        }
        
        #endregion
        
        #region Helpers

        private void RunLootHandler(BaseNetworkable networkable, ItemContainer inventory, int containerIndex)
        {
            NextFrame(() => ServerMgr.Instance.StartCoroutine(LootHandler(networkable, inventory, containerIndex)));
        }

        private IEnumerator LootHandler(BaseNetworkable networkable, ItemContainer inventory, int containerIndex, string apiShortname = null)
        {
            if (inventory == null || networkable == null && string.IsNullOrEmpty(apiShortname) ||
                networkable.ShortPrefabName == "player_corpse")
                yield break;
            
            var isApi = networkable == null;


            var blacklisted = new List<ItemData>();
            var usable = new List<ContainerData>();
            for (var i = 0; i < _config.Containers.Count; i++)
            {
                var container = _config.Containers[i];
                
                if (!container.ProcessCorpses && networkable is LootableCorpse)
                    continue;

                if (!container.ProcessLootContainers && networkable is LootContainer)
                    continue;

                if (!container.ShortnameFits(isApi ? apiShortname : networkable.ShortPrefabName, isApi))
                    continue;

                if (containerIndex != -1 && !container.ContainerIndexes.Contains(containerIndex))
                    continue;

                if (networkable != null && container.Monuments.Count > 0)
                {
                    if (!container.Monuments.Contains(string.Empty))
                    {
                        if (!container.Monuments.Contains(GetMonumentName(networkable.transform.position)))
                            continue;
                    }
                }
                
                usable.Add(container);

                for (var j = 0; j < container.Items.Count; j++)
                {
                    var item = container.Items[j];
                    if (item.ReplaceDefaultLoot || item.RemoveItem)
                        blacklisted.Add(item);
                }
            }

            for (var i = 0; i < usable.Count; i++)
            {
                yield return HandleInventory(networkable, inventory, usable[i], blacklisted);
            }
        }

        private IEnumerator HandleInventory(BaseNetworkable networkable, ItemContainer inventory, ContainerData container, List<ItemData> blacklisted)
        {
            if (container.RemoveContainer)
            {
                PrintDebug("Removing container in next frame");
                NextFrame(() =>
                {
                    if (networkable != null && !networkable.IsDestroyed)
                        networkable.Kill();
                });
                
                yield break;
            }
            
            if (!container.Online.IsOkay())
            {
                PrintDebug("Online check failed");
                yield break;
            }

            if (container.ShuffleItems && !container.ModifyItems && container.Items != null) // No need to shuffle for items modification
                Shuffle(container.Items);

            inventory.capacity = inventory.itemList.Count;

            if (container.ReplaceItems || container.AddItems)
            {
                var dataCapacity = ChanceData.Select(container.Capacity);
                if (dataCapacity == null)
                {
                    PrintDebug("Could not select a correct capacity");
                    yield break;
                }

                if (container.ReplaceItems)
                {
                    inventory.Clear();
                    ItemManager.DoRemoves();
                    inventory.capacity = dataCapacity.Capacity;
                }

                if (container.AddItems)
                {
                    inventory.capacity += dataCapacity.Capacity;
                }
                
                if (container.Items?.Count > 0)
                    yield return HandleInventoryAddReplace(inventory, container);
            }

            if (container.ModifyItems && container.Items?.Count > 0)
            {
                yield return HandleInventoryModify(inventory, container, blacklisted);
            }

            if (!container.DefaultLoot)
                yield break;
            
            PrintDebug("Filling with default loot..");

            var lootContainer = networkable as LootContainer;
            if (lootContainer == null)
            {
                PrintDebug("Tried to set vanilla loot, but it's not a loot container");
                yield break;
            }

            var failures = 0;
            while (inventory.itemList.Count < inventory.capacity)
            {
                Item addedItem;
                do
                {
                    addedItem = GenerateDefaultItem(lootContainer, blacklisted, container, inventory.itemList);
                } while (addedItem == null & ++failures <= container.MaxRetries);

                if (failures > container.MaxRetries)
                {
                    PrintDebug($"Skipping item, a lot of failures ({failures})");

                    addedItem?.Remove();
                    break;
                }

                if (addedItem != null)
                {
                    PrintDebug($"Inserting default loot {addedItem.info.shortname} ({addedItem.blueprintTargetDef?.shortname ?? "nobp"})");
                    addedItem.position = inventory.itemList.Count - 1;
                    if (!inventory.Insert(addedItem))
                        PrintDebug("Unable to insert an item!");
                }

                yield return null;
            }
            
            ItemManager.DoRemoves();
        }

        private void HandleAPIFill(ItemContainer container, string apiShortname, int containerIndex)
        {
            PrintDebug($"Handling API fill with API Shortname: {apiShortname}");
            // ReSharper disable once IteratorMethodResultIsIgnored
            LootHandler(null, container, containerIndex, apiShortname);
        }

        private static IEnumerator HandleInventoryAddReplace(ItemContainer inventory, ContainerData container)
        {
            PrintDebug($"Add or Replace for {inventory.entityOwner?.ShortPrefabName ?? "Unknown"}");
            
            var failures = 0;
            while (inventory.itemList.Count < inventory.capacity)
            {
                yield return null;

                var dataItem = ChanceData.Select(container.Items);
                if (dataItem == null)
                {
                    PrintDebug("Could not select a correct item");
                
                    if (++failures > container.MaxRetries)
                    {
                        PrintDebug("Stopping because of failures");
                        break;
                    }
                    
                    continue;
                }

                for (var i = 0; i < dataItem.Shortnames.Count; i++)
                {
                    var shortnameData = dataItem.Shortnames[i];
                    PrintDebug(
                        $"Handling item {shortnameData.Shortname} (Blueprint: {dataItem.IsBlueprint} / Stacking: {dataItem.AllowStacking})");

                    var skin = ChanceData.Select(dataItem.Skins)?.Skin ?? 0UL;

                    if (!container.AllowDuplicateItems) // Duplicate items are not allowed
                    {
                        if (IsDuplicate(inventory.itemList, container, dataItem, shortnameData.Shortname, skin))
                        {
                            if (++failures > container.MaxRetries)
                            {
                                PrintDebug("Stopping because of duplicates");
                                break;
                            }

                            continue;
                        }

                        PrintDebug("No duplicates");
                    }

                    var dataAmount = ChanceData.Select(dataItem.Amount);
                    if (dataAmount == null)
                    {
                        PrintDebug("Could not select a correct amount");
                        continue;
                    }

                    var amount = 1;
                    dataAmount.Modify(ref amount);

                    var definition =
                        ItemManager.FindItemDefinition(dataItem.IsBlueprint
                            ? "blueprintbase"
                            : shortnameData.Shortname);
                    if (definition == null)
                    {
                        PrintDebug("Could not find an item definition");
                        continue;
                    }

                    var stack = dataItem.IgnoreStack ? amount : Math.Min(amount, definition.stackable);
                    PrintDebug($"Creating with amount: {stack} ({amount})");

                    var createdItem = ItemManager.Create(definition, stack, skin);
                    if (createdItem == null)
                    {
                        PrintDebug("Could not create an item");
                        continue;
                    }

                    if (dataItem.IsBlueprint)
                    {
                        createdItem.blueprintTarget = ItemManager.FindItemDefinition(shortnameData.Shortname).itemid;
                    }
                    else
                    {
                        var dataCondition = ChanceData.Select(dataItem.Conditions);
                        if (createdItem.hasCondition)
                        {
                            if (dataCondition != null)
                            {
                                dataCondition.Modify(ref createdItem._condition, ref createdItem._maxCondition);

                                if (!dataItem.IgnoreCondition)
                                    createdItem._condition =
                                        Math.Min(createdItem._condition, createdItem._maxCondition);
                            }
                        }
                        else if (dataCondition != null)
                        {
                            PrintDebug("Configurated item has a condition but item doesn't have condition");
                        }
                    }

                    if (!string.IsNullOrEmpty(dataItem.Name))
                        createdItem.name = dataItem.Name;

                    var moved = createdItem.MoveToContainer(inventory, allowStack: dataItem.AllowStacking);
                    if (moved) continue;

                    PrintDebug("Could not move item to a container");
                }
            }
        }

        private static IEnumerator HandleInventoryModify(ItemContainer inventory, ContainerData container, List<ItemData> blacklisted)
        {
            PrintDebug($"Modify for {inventory.entityOwner?.ShortPrefabName ?? "Unknown"}");
            
            // Reversed, because an item can be removed.
            for (var i = inventory.itemList.Count - 1; i >= 0; i--)
            {
                var item = inventory.itemList[i];
                for (var j = 0; j < container.Items.Count; j++)
                {
                    yield return null;

                    var dataItem = container.Items[j];
                    if (!dataItem.ShortnameFits(item, container))
                        continue;

                    PrintDebug(
                        $"Handling item {item.info.shortname} (Blueprint: {dataItem.IsBlueprint} / Stacking: {dataItem.AllowStacking})");

                    if (dataItem.RemoveItem)
                    {
                        PrintDebug("Removing item");

                        for (var k = 0; k < item.info.itemMods.Length; k++)
                        {
                            var itemMod = item.info.itemMods[k];
                            itemMod.OnRemove(item);
                        }

                        item.DoRemove();
                        break;
                    }

                    if (dataItem.ReplaceDefaultLoot)
                    {
                        PrintDebug(
                            $"Replacing {item.info.shortname} ({item.blueprintTargetDef?.shortname ?? "nobp"}) with default loot");

                        for (var k = 0; k < item.info.itemMods.Length; k++)
                        {
                            var itemMod = item.info.itemMods[k];
                            itemMod.OnRemove(item);
                        }

                        item.DoRemove();

                        var lootContainer = inventory.entityOwner as LootContainer;
                        if (lootContainer == null)
                        {
                            PrintDebug(
                                "Tried to change an item to another vanilla loot item, but it's not a loot container");

                            break;
                        }

                        var failures = 0;
                        
                        Item addedItem;
                        do
                        {
                            addedItem = GenerateDefaultItem(lootContainer, blacklisted, container, inventory.itemList);
                        } while (addedItem == null & ++failures <= container.MaxRetries);

                        if (failures > container.MaxRetries)
                        {
                            PrintDebug($"Skipping item, a lot of failures ({failures})");

                            addedItem?.Remove();
                            break;
                        }

                        if (addedItem != null)
                        {
                            PrintDebug(
                                $"Inserting replace with default loot {addedItem.info.shortname} ({addedItem.blueprintTargetDef?.shortname ?? "nobp"})");
                            addedItem.position = inventory.itemList.Count - 1;
                            if (!inventory.Insert(addedItem))
                                PrintDebug("Unable to insert an item!");
                        }

                        ItemManager.DoRemoves();
                        break;
                    }

                    item.skin = ChanceData.Select(dataItem.Skins)?.Skin ?? item.skin;

                    var dataAmount = ChanceData.Select(dataItem.Amount);
                    if (dataAmount == null)
                    {
                        PrintDebug("Could not select a correct amount");
                        continue;
                    }

                    var amount = item.amount;
                    dataAmount.Modify(ref amount);

                    var stack = dataItem.IgnoreStack ? amount : Math.Min(amount, item.info.stackable);

                    item.amount = stack;
                    var dataCondition = ChanceData.Select(dataItem.Conditions);
                    if (item.hasCondition)
                    {
                        if (dataCondition != null)
                        {
                            dataCondition.Modify(ref item._condition, ref item._maxCondition);

                            if (!dataItem.IgnoreCondition)
                                item._condition = Math.Min(item._condition, item._maxCondition);
                        }
                    }
                    else if (dataCondition != null)
                    {
                        PrintDebug("Configurated item has a condition but item doesn't have condition");
                    }

                    if (!string.IsNullOrEmpty(dataItem.Name))
                        item.name = dataItem.Name;
                }
            }
        }

        private static Item GenerateDefaultItem(LootContainer lootContainer, IReadOnlyList<ItemData> blacklisted,
            ContainerData containerData, IList<Item> containerItems)
        {
            var items = Pool.GetList<Item>();
            if (lootContainer.LootSpawnSlots.Length != 0)
            {
                for (var i = 0; i < lootContainer.LootSpawnSlots.Length; i++)
                {
                    var lootSpawnSlot = lootContainer.LootSpawnSlots[i];
                    for (var j = 0; j < lootSpawnSlot.numberToSpawn; ++j)
                    {
                        if (UnityEngine.Random.Range(0.0f, 1f) <= (double) lootSpawnSlot.probability)
                        {
                            GetLootSpawnItem(lootSpawnSlot.definition, blacklisted, containerData, containerItems,
                                items);
                        }
                    }
                }
            }
            else if (lootContainer.lootDefinition != null)
            {
                for (var i = 0; i < lootContainer.maxDefinitionsToSpawn; ++i)
                {
                    GetLootSpawnItem(lootContainer.lootDefinition, blacklisted, containerData, containerItems, items);
                }
            }

            Item addedItem = null;
            if (items.Count == 0)
                goto free;

            var index = _ins._random.Next(0, items.Count);
            addedItem = items[index];
            
            items.RemoveAt(index);

            for (var i = 0; i < items.Count; i++)
            {
                items[i].Remove();
            }

            if (lootContainer.SpawnType == LootContainer.spawnType.ROADSIDE ||
                lootContainer.SpawnType == LootContainer.spawnType.TOWN)
            {
                if (addedItem.hasCondition)
                    addedItem.condition =
                        UnityEngine.Random.Range(addedItem.info.condition.foundCondition.fractionMin,
                            addedItem.info.condition.foundCondition.fractionMax) *
                        addedItem.info.condition.max;
            }

            free:
            Pool.FreeList(ref items);
            return addedItem;
        }

        private static void GetLootSpawnItem(LootSpawn loot, IReadOnlyList<ItemData> blacklisted,
            ContainerData containerData, IList<Item> containerItems, ICollection<Item> items)
        {
            if (loot.subSpawn != null && loot.subSpawn.Length != 0)
            {
                var totalWeight = loot.subSpawn.Sum(x => x.weight);
                var randomNumber = UnityEngine.Random.Range(0, totalWeight);
                for (var i = 0; i < loot.subSpawn.Length; i++)
                {
                    if (loot.subSpawn[i].category == null)
                        continue;

                    totalWeight -= loot.subSpawn[i].weight;
                    if (randomNumber >= totalWeight)
                    {
                        GetLootSpawnItem(loot.subSpawn[i].category, blacklisted, containerData, containerItems, items);
                        return;
                    }
                }

                return;
            }

            if (loot.items == null)
                return;

            for (var i = 0; i < loot.items.Length; i++)
            {
                var itemAmountRanged = loot.items[i];
                if (itemAmountRanged == null)
                    continue;

                Item item;
                if (itemAmountRanged.itemDef.spawnAsBlueprint)
                {
                    var blueprintBaseDef = loot.GetBlueprintBaseDef();
                    if (blueprintBaseDef == null)
                        continue;

                    item = ItemManager.Create(blueprintBaseDef);
                    item.blueprintTarget = itemAmountRanged.itemDef.itemid;
                }
                else
                {
                    item = ItemManager.CreateByItemID(itemAmountRanged.itemid, (int) itemAmountRanged.GetAmount());
                }

                if (IsItemBlacklisted(item, blacklisted, containerData) ||
                    IsItemDuplicate(containerItems, item, containerData))
                {
                    item.Remove();
                    continue;
                }

                items.Add(item);
            }
        }

        private static bool IsItemDuplicate(IEnumerable<Item> items, Item origin, ContainerData container)
        {
            // In case duplicate items are allowed or item is null
            if (container.AllowDuplicateItems || origin == null)
                return false;

            var originShortname = origin.IsBlueprint() ? origin.blueprintTargetDef.shortname : origin.info.shortname;
            foreach (var item in items)
            {
                if ((!container.ModifyItems || !container.ModifyIgnoreBlueprint) &&
                    item.IsBlueprint() != origin.IsBlueprint())
                    continue;

                var itemShortname = item.IsBlueprint() ? item.blueprintTargetDef.shortname : item.info.shortname;
                if (originShortname != itemShortname)
                    continue;

                if (container.AllowDuplicateItemsDifferentSkins && origin.skin != item.skin)
                    continue;

                return true;
            }

            return false;
        }

        private static bool IsItemBlacklisted(Item item, IReadOnlyList<ItemData> blacklisted, ContainerData container)
        {
            if (item == null)
                return false;

            for (var i = 0; i < blacklisted.Count; i++)
            {
                var blacklistedItem = blacklisted[i];
                if (!blacklistedItem.ShortnameFits(item, container))
                    continue;

                return true;
            }

            return false;
        }

        // Not used in modify
        private static bool IsDuplicate(IReadOnlyList<Item> list, ContainerData container, ItemData dataItem, string shortname, ulong skin)
        {
            for (var j = 0; j < list.Count; j++)
            {
                var item = list[j];
                if (dataItem.IsBlueprint)
                {
                    if (!item.IsBlueprint() || item.blueprintTargetDef.shortname != shortname) continue;

                    PrintDebug("Found a duplicate blueprint");
                    return true;
                }

                if (item.IsBlueprint() || item.info.shortname != shortname) continue;
                if (container.AllowDuplicateItemsDifferentSkins && item.skin != skin)
                    continue;

                PrintDebug("Found a duplicate item");
                return true;
            }

            return false;
        }

        private string GetMsg(string key, string userId) => lang.GetMessage(key, this, userId);

        private static void PrintDebug(string message)
        {
            if (_config.Debug)
                Interface.Oxide.LogDebug(message);
        }

        private void LootRefill()
        {
            using (var enumerator = BaseNetworkable.serverEntities.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    OnLootSpawn(enumerator.Current);
                }
            }
        }

        private string GetMonumentName(Vector3 position)
        {
            var monuments = TerrainMeta.Path.Monuments;
            for (var i = 0; i < monuments.Count; i++)
            {
                var monument = monuments[i];
                var obb = new OBB(monument.transform.position, Quaternion.identity, monument.Bounds);
                if (obb.Contains(position))
                    return monument.name;
            }

            return string.Empty;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            var count = list.Count;
            while (count > 1)
            {
                count--;
                var index = _ins._random.Next(count + 1);
                var value = list[index];
                list[index] = list[count];
                list[count] = value;
            }
        }
        
        #endregion
    }
}