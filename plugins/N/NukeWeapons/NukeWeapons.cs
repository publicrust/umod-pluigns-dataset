﻿using Facepunch;
using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using System.IO;
using Rust;

namespace Oxide.Plugins
{
    [Info("NukeWeapons", "k1lly0u", "0.1.69")]
    [Description("Create nuclear ammo for a bunch of different ammo types, full GUI crafting menu and ammo gauge")]
    class NukeWeapons : RustPlugin
    {
        #region Fields
        [PluginReference] Plugin LustyMap, ImageLibrary;

        private NukeData _nukeData;
        private ItemNames _itemNames;
        private DynamicConfigFile _data;        
        private DynamicConfigFile item_names;
                       
        private string dataDirectory = "file://" + Interface.Oxide.DataDirectory + Path.DirectorySeparatorChar + "NukeWeapons" + Path.DirectorySeparatorChar + "Icons" + Path.DirectorySeparatorChar;

        private readonly List<ZoneList> _radiationZones = new List<ZoneList>();

        private readonly Hash<ulong, NukeType> _activeUsers = new Hash<ulong, NukeType>();
        private Hash<ulong, Hash<NukeType, int>> _cachedAmmo = new Hash<ulong, Hash<NukeType, int>>();
        private readonly Hash<ulong, Hash<NukeType, double>> _craftingTimers = new Hash<ulong, Hash<NukeType, double>>();

        private readonly List<Timer> _timers = new List<Timer>();

        private Dictionary<string, ItemDefinition> _itemDefs;
        private Hash<string, string> _displayNames = new Hash<string, string>();

        private const int PlayerMask = 131072;

        #endregion

        #region Oxide Hooks
        private void Loaded()
        {
            _data = Interface.Oxide.DataFileSystem.GetFile("NukeWeapons/nukeweapon_data");
            item_names = Interface.Oxide.DataFileSystem.GetFile("NukeWeapons/itemnames");
            Interface.Oxide.DataFileSystem.SaveDatafile("NukeWeapons/Icons/foldercreator");
            lang.RegisterMessages(Messages, this);           
            InitializePlugin();
        }

        private void OnServerInitialized()
        {
            LoadData();
            
            _itemDefs = ItemManager.itemList.ToDictionary(i => i.shortname);
            
            if (_itemNames.displayNames == null || _itemNames.displayNames.Count < 1)
            {
                foreach (KeyValuePair<string, ItemDefinition> item in _itemDefs.Where(item => !_displayNames.ContainsKey(item.Key)))
                    _displayNames.Add(item.Key, item.Value.displayName.translated);

                SaveDisplayNames();
            }
            else _displayNames = _itemNames.displayNames;
            
            FindAllMines();
        }

        private void Unload()
        {
            for (int i = 0; i < _radiationZones.Count; i++)
            {
                _radiationZones[i].time.Destroy();
                UnityEngine.Object.Destroy(_radiationZones[i].zone.gameObject);
            }

            _radiationZones.Clear();

            foreach(BasePlayer player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UIMain);
                CuiHelper.DestroyUi(player, UIPanel);
                DestroyIconUI(player);
                DestroyCraftUI(player);
            }

            for (int i = 0; i < _timers.Count; i++)            
                _timers[i].Destroy();            

            SaveData();         
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            _activeUsers.Remove(player.userID);

            CuiHelper.DestroyUi(player, UIMain);
            CuiHelper.DestroyUi(player, UIPanel);
            
            DestroyIconUI(player);
            DestroyCraftUI(player);
        }

        private void OnEntityDeath(BasePlayer player, HitInfo hitInfo)
        {
            if (!player)
                return;

            _activeUsers.Remove(player.userID);
            
            CuiHelper.DestroyUi(player, UIMain);
            CuiHelper.DestroyUi(player, UIPanel);

            DestroyIconUI(player);
        }

        private void OnRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            if (!_activeUsers.ContainsKey(player.userID) || _activeUsers[player.userID] != NukeType.Rocket) 
                return;
            
            if (HasUnlimitedAmmo(player) || HasAmmo(player.userID, NukeType.Rocket))
            {
                if (!HasUnlimitedAmmo(player))
                {
                    string itemname = "ammo.rocket.basic";
                    switch (entity.ShortPrefabName)
                    {
                        case "calledrocket_hv":
                            itemname = "ammo.rocket.hv";
                            break;
                        case "calledrocket_fire":
                            itemname = "ammo.rocket.fire";
                            break;
                        default:
                            break;
                    }
                    player.inventory.containerMain.AddItem(_itemDefs[itemname], 1);
                    _cachedAmmo[player.userID][NukeType.Rocket]--;
                }
                entity.gameObject.AddComponent<Nuke>().InitializeComponent(this, NukeType.Rocket, configData.Rockets.RadiationProperties);
            }
            else
            {
                _activeUsers.Remove(player.userID);
                SendMSG(player, $"{MSG("OOA", player.UserIDString)} {MSG("Rockets", player.UserIDString)}");
            }
            CreateAmmoIcons(player);
        }

        private void OnEntitySpawned(Landmine landmine)
        {
            if (!_activeUsers.ContainsKey(landmine.OwnerID) || _activeUsers[landmine.OwnerID] != NukeType.Mine) 
                return;
            
            BasePlayer player = BasePlayer.FindByID(landmine.OwnerID);
            if (!player) 
                return;
            
            if (HasUnlimitedAmmo(player) || HasAmmo(player.userID, NukeType.Mine))
            {
                if (!HasUnlimitedAmmo(player))
                {
                    player.inventory.containerMain.AddItem(_itemDefs["trap.landmine"], 1);
                    _cachedAmmo[player.userID][NukeType.Mine]--;
                }

                landmine.gameObject.AddComponent<Nuke>().InitializeComponent(this, NukeType.Mine, configData.Mines.RadiationProperties);
                _nukeData.Mines.Add(landmine.net.ID.Value);
            }
            else
            {
                _activeUsers.Remove(player.userID);
                SendMSG(player, $"{MSG("OOA", player.UserIDString)} {MSG("Mines", player.UserIDString)}");
            }

            CreateAmmoIcons(player);
        }

        private void OnEntityDeath(Landmine landmine, HitInfo info)
        {
            if (landmine && _nukeData.Mines.Contains(landmine.net.ID.Value))
                _nukeData.Mines.Remove(landmine.net.ID.Value);
        }

        private void OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            if (!_activeUsers.ContainsKey(attacker.userID) || _activeUsers[attacker.userID] != NukeType.Bullet) 
                return;

            BaseEntity weapon = info?.Weapon ? info.Weapon.GetEntity() : null;
            if (!weapon) 
                return;
            
            BaseProjectile projectile = weapon.GetComponent<BaseProjectile>();
            if (!projectile) 
                return;
            
            ItemDefinition ammoType = projectile.primaryMagazine.ammoType;
            if (!ammoType)
                return;

            if (string.IsNullOrEmpty(ammoType.shortname)) 
                return;

            if (!ammoType.shortname.Contains("ammo.rifle")) 
                return;
            
            Vector3 hitPos = info.HitPositionWorld;
            
            ConfigData.NWType.RadiationStats radVar = configData.Bullets.RadiationProperties;
            if (HasUnlimitedAmmo(attacker) || HasAmmo(attacker.userID, NukeType.Bullet))
            {
                if (!HasUnlimitedAmmo(attacker))
                {
                    attacker.inventory.containerMain.AddItem(_itemDefs[ammoType.shortname], 1);
                    _cachedAmmo[attacker.userID][NukeType.Bullet]--;
                }
                InitializeZone(hitPos, radVar.Intensity, radVar.Duration, radVar.Radius, false);
            }
            else
            {
                _activeUsers.Remove(attacker.userID);
                SendMSG(attacker, $"{MSG("OOA", attacker.UserIDString)} {MSG("Bullets", attacker.UserIDString)}");
            }
            CreateAmmoIcons(attacker);
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            if (entity.ShortPrefabName.Contains("explosive.timed"))
            {
                if (!_activeUsers.ContainsKey(player.userID) || _activeUsers[player.userID] != NukeType.Explosive)
                    return;
                
                if (HasUnlimitedAmmo(player) || HasAmmo(player.userID, NukeType.Explosive))
                {
                    if (!HasUnlimitedAmmo(player))
                    {
                        player.inventory.containerMain.AddItem(_itemDefs["explosive.timed"], 1);
                        _cachedAmmo[player.userID][NukeType.Explosive]--;
                    }

                    entity.gameObject.AddComponent<Nuke>().InitializeComponent(this, NukeType.Explosive, configData.Explosives.RadiationProperties);
                }
                else
                {
                    _activeUsers.Remove(player.userID);
                    SendMSG(player, $"{MSG("OOA", player.UserIDString)} {MSG("Explosives", player.UserIDString)}");
                }

                CreateAmmoIcons(player);
            }
            
            if (entity.ShortPrefabName.Contains("grenade.f1"))
            {
                if (!_activeUsers.ContainsKey(player.userID) || _activeUsers[player.userID] != NukeType.Grenade) 
                    return;
                
                if (HasUnlimitedAmmo(player) || HasAmmo(player.userID, NukeType.Grenade))
                {
                    if (!HasUnlimitedAmmo(player))
                    {
                        player.inventory.containerMain.AddItem(_itemDefs["grenade.f1"], 1);
                        _cachedAmmo[player.userID][NukeType.Grenade]--;
                    }
                    entity.gameObject.AddComponent<Nuke>().InitializeComponent(this, NukeType.Explosive, configData.Grenades.RadiationProperties);                        
                }
                else
                {
                    _activeUsers.Remove(player.userID);
                    SendMSG(player, $"{MSG("OOA", player.UserIDString)} {MSG("Grenades", player.UserIDString)}");
                }
                CreateAmmoIcons(player);
            }
        }
        #endregion

        #region Helpers
        private bool HasEnoughResources(BasePlayer player, int itemid, int amount) => player.inventory.GetAmount(itemid) >= amount;

        private void TakeResources(BasePlayer player, int itemid, int amount) => player.inventory.Take(null, itemid, amount);
        
        private double CurrentTime() => DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;

        private bool IsSelectedType(BasePlayer player, NukeType type) => _activeUsers.ContainsKey(player.userID) && _activeUsers[player.userID] == type;

        private T ParseType<T>(string type)
        {
            try
            {
                return (T)Enum.Parse(typeof(T), type, true);
            }
            catch
            {
                PrintError($"INVALID OPTION! The value \"{type}\" is an incorrect selection.\nAvailable options are: {Enum.GetNames(typeof(T)).ToSentence()}");
                return default(T);
            }
        }
        #endregion

        #region Functions
        private void FindAllMines()
        {
            List<BaseNetworkable> list = Pool.Get<List<BaseNetworkable>>();
            list.AddRange(BaseNetworkable.serverEntities.Where(x => x is Landmine));

            foreach (BaseNetworkable baseNetworkable in list)
            {
                if (_nukeData.Mines.Any(x=> x == baseNetworkable.net.ID.Value))
                    baseNetworkable.gameObject.AddComponent<Nuke>().InitializeComponent(this, NukeType.Mine, configData.Mines.RadiationProperties);
            }
            
            Pool.FreeUnmanaged(ref list);  
        }

        private bool CanCraftWeapon(BasePlayer player, NukeType type)
        {
            Dictionary<string, int> ingredients = GetCraftingComponents(type);

            foreach (KeyValuePair<string, int> item in ingredients)
            {
                if (HasEnoughResources(player, _itemDefs[item.Key].itemid, item.Value))
                    continue;
                
                return false;
            }
            
            return true;
        }

        private bool IsCrafting(BasePlayer player, NukeType type)
        {
            if (!_craftingTimers.TryGetValue(player.userID, out Hash<NukeType, double> craftingTimer)) 
                return false;

            if (!craftingTimer.TryGetValue(type, out double time)) 
                return false;
            
            return time > CurrentTime();
        }

        private string CraftTimeClock(BasePlayer player, double time)
        {
            if (!player)
                return "";

            TimeSpan dateDifference = TimeSpan.FromSeconds(time - CurrentTime());            
            int mins = dateDifference.Minutes;
            int secs = dateDifference.Seconds;
            return $"{mins:00}:{secs:00}";
        }

        private void StartCrafting(BasePlayer player, NukeType type)
        {
            ConfigData.NWType config = GetConfigFromType(type);

            Dictionary<string, int> ingredients = GetCraftingComponents(type);

            foreach (KeyValuePair<string, int> ing in ingredients)            
                TakeResources(player, _itemDefs[ing.Key].itemid, ing.Value);

            bool finished = FinishedCrafting(player);

            _craftingTimers[player.userID][type] = CurrentTime() + config.CraftTime;            
            CraftingElement(player, type);

            if (finished)
                CreateCraftTimer(player);
        }

        private void FinishCraftingItems(BasePlayer player, NukeType type)
        {
            ConfigData.NWType config = GetConfigFromType(type);  
                     
            _cachedAmmo[player.userID][type] += config.CraftAmount;

            if (_activeUsers.ContainsKey(player.userID))
                CreateAmmoIcons(player);
        }

        private bool FinishedCrafting(BasePlayer player)
        {
            if (!_craftingTimers.ContainsKey(player.userID))
            {
                CheckPlayerEntry(player);
                return true;
            }

            bool finished = true;

            foreach (KeyValuePair<NukeType, double> craft in _craftingTimers[player.userID])
            {
                if (craft.Value != -1)
                {
                    finished = false;
                    break;
                }
            }
            return finished;
        }
        #endregion

        #region External Calls        
        private void CloseMap(BasePlayer player)
        {
            if (LustyMap)
            {
                LustyMap.Call("DisableMaps", player);
            }
        }

        private void OpenMap(BasePlayer player)
        {
            if (LustyMap)
            {
                LustyMap.Call("EnableMaps", player);
            }
        }
        #endregion

        #region UI Creation
        private class NWUI
        {
            public static CuiElementContainer CreateElementContainer(string panelName, string color, string aMin, string aMax, bool cursor = false, string parent = "Overlay")
            {
                CuiElementContainer NewElement = new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = {Color = color},
                            RectTransform = {AnchorMin = aMin, AnchorMax = aMax},
                            CursorEnabled = cursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
                return NewElement;
            }
            public static void CreatePanel(ref CuiElementContainer container, string panel, string color, string aMin, string aMax, bool cursor = false)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax },
                    CursorEnabled = cursor
                },
                panel);
            }
            public static void CreateLabel(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, TextAnchor align = TextAnchor.MiddleCenter, float fadein = 0f)
            {
                
                container.Add(new CuiLabel
                {
                    Text = { Color = color, FontSize = size, Align = align, FadeIn = fadein, Text = text },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax }
                },
                panel);

            }
            public static void CreateButton(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, string command, TextAnchor align = TextAnchor.MiddleCenter, float fadein = 0f)
            {
               
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = fadein },
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax },
                    Text = { Text = text, FontSize = size, Align = align }
                },
                panel);
            }
            public static void LoadImage(ref CuiElementContainer container, string panel, string png, string aMin, string aMax)
            {
                container.Add(new CuiElement
                {
                    Parent = panel,
                    Components =
                    {
                        new CuiRawImageComponent {Png = png, Sprite = "assets/content/textures/generic/fulltransparent.tga" },
                        new CuiRectTransformComponent {AnchorMin = aMin, AnchorMax = aMax }
                    }
                });
            }            
            public static string CreateTextOverlay(ref CuiElementContainer container, string panelName, string textcolor, string text, int size, string distance, string olcolor, string aMin, string aMax, TextAnchor align = TextAnchor.MiddleCenter)
            {
                string name = CuiHelper.GetGuid();
                container.Add(new CuiElement
                {
                    Name = name,
                    Parent = panelName,
                    Components =
                        {
                            new CuiTextComponent { Color = textcolor, Text = text, FontSize = size, Align = align},
                            new CuiOutlineComponent { Distance = distance, Color = olcolor },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = aMin,
                                AnchorMax = aMax
                            }
                        }
                });
                return name;
            }
        }

        #region Colors
        private readonly Dictionary<string, string> _uiColors = new Dictionary<string, string>
        {
            {"dark", "0.1 0.1 0.1 0.98" },
            {"light", "0.7 0.7 0.7 0.3" },
            {"grey1", "0.6 0.6 0.6 1.0" },
            {"buttonbg", "0.2 0.2 0.2 0.7" },
            {"buttonopen", "0.2 0.8 0.2 0.9" },
            {"buttoncompleted", "0 0.5 0.1 0.9" },
            {"buttonred", "0.85 0 0.35 0.9" },
            {"buttongrey", "0.8 0.8 0.8 0.9" },
            {"grey8", "0.8 0.8 0.8 1.0" }
        };
        #endregion
        #endregion

        #region NW UI
        private const string UIMain = "NWUIMain";

        private const string UIPanel = "NWUIPanel";

        private const string UIEntry = "NWUIEntry";

        private const string UIIcon = "NWUIIcon";
        
        private void OpenCraftingMenu(BasePlayer player)
        {
            CloseMap(player);

            CuiElementContainer container = NWUI.CreateElementContainer(UIMain, _uiColors["dark"], "0 0.92", "1 1");

            NWUI.CreatePanel(ref container, UIMain, _uiColors["light"], "0.01 0.05", "0.99 0.95", true);
            NWUI.CreateLabel(ref container, UIMain, "", $"{configData.Options.MSG_MainColor}{Title}</color>", 30, "0.05 0", "0.2 1");

            int number = 0;
            if (configData.Bullets.Enabled && HasPermission(player, NukeType.Bullet))
            {
                CreateMenuButton(ref container, UIMain, MSG("Bullets", player.UserIDString), "NWUI_ChangeElement bullet", number);
                number++;
            }

            if (configData.Explosives.Enabled && HasPermission(player, NukeType.Explosive))
            {
                CreateMenuButton(ref container, UIMain, MSG("Explosives", player.UserIDString), "NWUI_ChangeElement explosive", number);
                number++;
            }

            if (configData.Grenades.Enabled && HasPermission(player, NukeType.Grenade))
            {
                CreateMenuButton(ref container, UIMain, MSG("Grenades", player.UserIDString), "NWUI_ChangeElement grenade", number);
                number++;
            }

            if (configData.Mines.Enabled && HasPermission(player, NukeType.Mine))
            {
                CreateMenuButton(ref container, UIMain, MSG("Mines", player.UserIDString), "NWUI_ChangeElement mine", number);
                number++;
            }

            if (configData.Rockets.Enabled && HasPermission(player, NukeType.Rocket))
            {
                CreateMenuButton(ref container, UIMain, MSG("Rockets", player.UserIDString), "NWUI_ChangeElement rocket", number);
                number++;
            }

            CreateMenuButton(ref container, UIMain, MSG("Close", player.UserIDString), "NWUI_DestroyAll", number);

            CuiHelper.AddUi(player, container);
        }

        private void CraftingElement(BasePlayer player, NukeType type)
        {            
            CuiElementContainer container = NWUI.CreateElementContainer(UIPanel, _uiColors["dark"], "0 0", "1 0.92");

            NWUI.CreatePanel(ref container, UIPanel, _uiColors["light"], "0.01 0.02", "0.99 0.98", true);
            NWUI.LoadImage(ref container, UIPanel, GetImage("Background"), "0.01 0.02", "0.99 0.98");         

            NWUI.CreateLabel(ref container, UIPanel, "", $"{configData.Options.MSG_MainColor}{MSG("Required Ingredients", player.UserIDString)}</color>", 20, "0.1 0.85", "0.55 0.95");
            NWUI.CreateLabel(ref container, UIPanel, "", MSG("Item Name", player.UserIDString), 16, "0.1 0.75", "0.3 0.85", TextAnchor.MiddleLeft);
            NWUI.CreateLabel(ref container, UIPanel, "", MSG("Required Amount", player.UserIDString), 16, "0.3 0.75", "0.42 0.85");
            NWUI.CreateLabel(ref container, UIPanel, "", MSG("Your Supply", player.UserIDString), 16, "0.42 0.75", "0.54 0.85");
                        
            Dictionary<string, int> ingredients = GetCraftingComponents(type);

            int i = 0;
            foreach(KeyValuePair<string, int> item in ingredients)
            {
                ItemDefinition itemInfo = _itemDefs[item.Key];
                int plyrAmount = player.inventory.GetAmount(itemInfo.itemid);                
                CreateIngredientEntry(ref container, UIPanel, _displayNames[itemInfo.shortname], item.Value, plyrAmount, i);
                i++;
            }

            ConfigData.NWType config = GetConfigFromType(type);

            string command = null;            
            string text = $"{MSG("Craft", player.UserIDString)} {config.CraftAmount}x";

            if (CanCraftWeapon(player, type))
                command = $"NWUI_Craft {type.ToString()}";

            if (_cachedAmmo[player.userID][type] >= config.MaxAllowed)
            {
                text = MSG("Limit Reached", player.UserIDString);
                command = null;
            }
            if (IsCrafting(player, type))
            {
                text = MSG("Crafting...", player.UserIDString);
                command = null;                
            }
            if (HasUnlimitedAmmo(player))
            {
                text = MSG("Unlimited", player.UserIDString);
                command = null;
            }

            NWUI.CreateLabel(ref container, UIPanel, "", $"{configData.Options.MSG_MainColor}{MSG("Inventory Amount", player.UserIDString)}</color>", 20, "0.6 0.85", "0.9 0.95");

            if (HasUnlimitedAmmo(player))
                NWUI.CreateLabel(ref container, UIPanel, "", $"~ / {config.MaxAllowed}", 16, "0.6 0.75", "0.9 0.85");
            else NWUI.CreateLabel(ref container, UIPanel, "", $"{_cachedAmmo[player.userID][type]} / {config.MaxAllowed}", 16, "0.6 0.75", "0.9 0.85");

            NWUI.CreateButton(ref container, UIPanel, _uiColors["buttonbg"], text, 16, $"0.6 0.65", $"0.74 0.72", command);

            if (_cachedAmmo[player.userID][type] > 0 || HasUnlimitedAmmo(player))
            {
                if (IsSelectedType(player, type))
                    NWUI.CreateButton(ref container, UIPanel, _uiColors["buttonbg"], MSG("Disarm", player.UserIDString), 16, $"0.76 0.65", $"0.9 0.72", $"NWUI_DeactivateMenu {type.ToString()}");
                else NWUI.CreateButton(ref container, UIPanel, _uiColors["buttonbg"], MSG("Arm", player.UserIDString), 16, $"0.76 0.65", $"0.9 0.72", $"NWUI_Activate {type.ToString()}");
            }

            CuiHelper.DestroyUi(player, UIPanel);
            CuiHelper.AddUi(player, container);
        }

        private void CreateCraftTimer(BasePlayer player)
        {               
            CuiElementContainer container = NWUI.CreateElementContainer(UIEntry, "0 0 0 0", "0.2 0.11", "0.8 0.15");

            string message = "";

            List<NukeType> completedTypes = new List<NukeType>();

            foreach(KeyValuePair<NukeType, double> craft in _craftingTimers[player.userID])
            {
                if (craft.Value == -1)
                    continue;   
                
                if (craft.Value <= CurrentTime())                
                    completedTypes.Add(craft.Key); 
                else message += $"{craft.Key.ToString()}: {configData.Options.MSG_MainColor}{CraftTimeClock(player, craft.Value)}</color>     ";                              
            }

            foreach(NukeType type in completedTypes)
            {
                _craftingTimers[player.userID][type] = -1;
                FinishCraftingItems(player, type);
            }

            if (string.IsNullOrEmpty(message))
            {
                DestroyCraftUI(player);
                return;
            }
            
            message = $"{configData.Options.MSG_MainColor}{MSG("Crafting", player.UserIDString)} ::: </color> " + message;

            NWUI.CreateLabel(ref container, UIEntry, "", message, 16, $"0 0", $"1 1", TextAnchor.MiddleRight, 0f);

            CuiHelper.DestroyUi(player, UIEntry);
            CuiHelper.AddUi(player, container);

            timer.Once(1, () => CreateCraftTimer(player));
        }

        private void CreateIngredientEntry(ref CuiElementContainer container, string panel, string name, int amountreq, int plyrhas, int number)
        {
            Vector2 position = new Vector2(0.1f, 0.68f);
            Vector2 dimensions = new Vector2(0.4f, 0.06f);
            float offsetY = (0.004f + dimensions.y) * number;
            Vector2 offset = new Vector2(0, offsetY);
            Vector2 posMin = position - offset;
            Vector2 posMax = posMin + dimensions;
            string color;

            if (amountreq > plyrhas)
                color = "<color=red>";
            else color = configData.Options.MSG_MainColor;

            NWUI.CreateLabel(ref container, panel, "", $"{configData.Options.MSG_MainColor}{name}</color>", 16, $"{posMin.x} {posMin.y}", $"{posMin.x + 0.2f} {posMax.y}", TextAnchor.MiddleLeft);
            NWUI.CreateLabel(ref container, panel, "", $"{amountreq}", 16, $"{posMin.x + 0.2f} {posMin.y}", $"{posMin.x + 0.32f} {posMax.y}");
            NWUI.CreateLabel(ref container, panel, "", $"{color}{plyrhas}</color>", 16, $"{posMin.x + 0.32f} {posMin.y}", $"{posMin.x + 0.44f} {posMax.y}");                       
        }

        private void CreateAmmoIcons(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIMain);
            CuiHelper.DestroyUi(player, UIPanel);

            if (_cachedAmmo.ContainsKey(player.userID))
            {
                DestroyIconUI(player);
                int i = 0;
                if (HasPermission(player, NukeType.Bullet))
                {
                    if (_cachedAmmo[player.userID][NukeType.Bullet] > 0 || HasUnlimitedAmmo(player))
                    {
                        AmmoIcon(player, NukeType.Bullet, i); i++;
                    }
                }
                if (HasPermission(player, NukeType.Explosive))
                {
                    if (_cachedAmmo[player.userID][NukeType.Explosive] > 0 || HasUnlimitedAmmo(player))
                    {
                        AmmoIcon(player, NukeType.Explosive, i); i++;
                    }
                }
                if (HasPermission(player, NukeType.Grenade))
                {
                    if (_cachedAmmo[player.userID][NukeType.Grenade] > 0 || HasUnlimitedAmmo(player))
                    {
                        AmmoIcon(player, NukeType.Grenade, i); i++;
                    }
                }
                if (HasPermission(player, NukeType.Mine))
                {
                    if (_cachedAmmo[player.userID][NukeType.Mine] > 0 || HasUnlimitedAmmo(player))
                    {
                        AmmoIcon(player, NukeType.Mine, i); i++;
                    }
                }
                if (HasPermission(player, NukeType.Rocket))
                {
                    if (_cachedAmmo[player.userID][NukeType.Rocket] > 0 || HasUnlimitedAmmo(player))
                    {
                        AmmoIcon(player, NukeType.Rocket, i); i++;
                    }
                }
                AddButtons(player, i);
            }
        }

        private void AmmoIcon(BasePlayer player, NukeType type, int number)
        {      
            Vector2 position = new Vector2(0.92f, 0.2f);
            Vector2 dimensions = new Vector2(0.07f, 0.12f);
            Vector2 offset = new Vector2(0, (0.01f + dimensions.y) * number);
            Vector2 posMin = position + offset;
            Vector2 posMax = posMin + dimensions;

            string panelName = UIIcon + type.ToString();
            
            CuiElementContainer container = NWUI.CreateElementContainer(panelName, "0 0 0 0", $"{posMin.x} {posMin.y}", $"{posMax.x} {posMax.y}", false, "Hud");

            string image = GetImage(type.ToString());
            if (IsSelectedType(player, type))
                image = GetImage($"{type.ToString()}Active");
            NWUI.LoadImage(ref container, panelName, image, "0 0", "1 1");

            string amount;
            if (HasUnlimitedAmmo(player))
                amount = "~";
            else amount = _cachedAmmo[player.userID][type].ToString();
            NWUI.CreateTextOverlay(ref container, panelName, "", $"{amount}", 30, "2 2", "0 0 0 1", "0 0", "1 1", TextAnchor.LowerCenter);

            if (IsSelectedType(player, type))
                NWUI.CreateButton(ref container, panelName, "0 0 0 0", "", 20, "0 0", "1 1", "NWUI_DeactivateButton");
            else NWUI.CreateButton(ref container, panelName, "0 0 0 0", "", 20, "0 0", "1 1", $"NWUI_Activate {type.ToString()}");
            
            CuiHelper.AddUi(player, container);
        }  
        
        private void AddButtons(BasePlayer player, int number)
        {
            Vector2 position = new Vector2(0.92f, 0.2f);
            Vector2 dimensions = new Vector2(0.07f, 0.12f);
            Vector2 offset = new Vector2(0, (0.01f + dimensions.y) * number);
            Vector2 posMin = position + offset;
            Vector2 posMax = posMin + dimensions;

            CuiElementContainer container = NWUI.CreateElementContainer(UIIcon, "0 0 0 0", $"{posMin.x} {posMin.y}", $"{posMax.x} {posMin.y + 0.1}", false, "Hud");
            NWUI.CreateButton(ref container, UIIcon, _uiColors["buttonbg"], MSG("Menu",player.UserIDString), 16, "0 0.55", "1 1", "NWUI_OpenMenu");
            NWUI.CreateButton(ref container, UIIcon, _uiColors["buttonbg"], MSG("Deactivate", player.UserIDString), 16, "0 0", "1 0.45", "NWUI_DeactivateIcons");

            CuiHelper.DestroyUi(player, UIIcon);
            CuiHelper.AddUi(player, container);
        }   
        
        #region UI Functions
        private void CreateMenuButton(ref CuiElementContainer container, string panelName, string buttonname, string command, int number)
        {
            Vector2 dimensions = new Vector2(0.1f, 0.6f);
            Vector2 origin = new Vector2(0.25f, 0.2f);
            Vector2 offset = new Vector2((0.01f + dimensions.x) * number, 0);

            Vector2 posMin = origin + offset;
            Vector2 posMax = posMin + dimensions;

            NWUI.CreateButton(ref container, panelName, _uiColors["buttonbg"], buttonname, 16, $"{posMin.x} {posMin.y}", $"{posMax.x} {posMax.y}", command);
        }                
        #endregion
        
        #region UI Commands
        [ConsoleCommand("NWUI_Craft")]
        private void cmdNWCraft(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (!player)
                return;

            NukeType nukeType = ParseType<NukeType>(arg.GetString(0));
            StartCrafting(player, nukeType);            
        }

        [ConsoleCommand("NWUI_DeactivateMenu")]
        private void cmdNWDeActivate(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (!player)
                return;

            _activeUsers.Remove(player.userID);

            NukeType nukeType = ParseType<NukeType>(arg.GetString(0));
            CraftingElement(player, nukeType);            
        }

        [ConsoleCommand("NWUI_DeactivateButton")]
        private void cmdNWDeActivateButton(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (!player)
                return;

            _activeUsers.Remove(player.userID);
            CreateAmmoIcons(player);       
        }

        [ConsoleCommand("NWUI_DeactivateIcons")]
        private void cmdNWDeactivateIcons(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (!player)
                return;

            _activeUsers.Remove(player.userID);
            DestroyIconUI(player);
        }

        [ConsoleCommand("NWUI_OpenMenu")]
        private void cmdNWOpenMenu(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (!player)
                return;

            if (HasPermission(player, NukeType.Bullet) || HasPermission(player, NukeType.Explosive) || HasPermission(player, NukeType.Grenade) || HasPermission(player, NukeType.Mine) || HasPermission(player, NukeType.Rocket))
            {
                CloseMap(player);
                CheckPlayerEntry(player);
                OpenCraftingMenu(player);
            }
        }

        [ConsoleCommand("NWUI_Activate")]
        private void cmdNWActivate(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (!player)
                return;

            NukeType nukeType = ParseType<NukeType>(arg.GetString(0));

            if (!_activeUsers.ContainsKey(player.userID))
                _activeUsers.Add(player.userID, nukeType);
            else _activeUsers[player.userID] = nukeType;

            SendMSG(player, MSG("activated", player.UserIDString).Replace("<type>", nukeType.ToString()));

            CreateAmmoIcons(player);
        }

        [ConsoleCommand("NWUI_ChangeElement")]
        private void cmdNWChangeElement(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (!player)
                return;

            NukeType nukeType = ParseType<NukeType>(arg.GetString(0));
            CraftingElement(player, nukeType);            
        }

        [ConsoleCommand("NWUI_DestroyAll")]
        private void cmdNWDestroyAll(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection.player as BasePlayer;
            if (!player)
                return;

            OpenMap(player);
            CuiHelper.DestroyUi(player, UIMain);
            CuiHelper.DestroyUi(player, UIPanel);            
        }

        private void DestroyCraftUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIEntry);            
        }

        private void DestroyIconUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIIcon + "Bullet");
            CuiHelper.DestroyUi(player, UIIcon + "Rocket");
            CuiHelper.DestroyUi(player, UIIcon + "Explosive");
            CuiHelper.DestroyUi(player, UIIcon + "Grenade");
            CuiHelper.DestroyUi(player, UIIcon + "Mine");
            CuiHelper.DestroyUi(player, UIIcon);
        }
        #endregion
        #endregion

        #region Functions
        private void InitializePlugin()
        {
            lang.RegisterMessages(Messages, this);
            permission.RegisterPermission("nukeweapons.rocket", this);
            permission.RegisterPermission("nukeweapons.bullet", this);
            permission.RegisterPermission("nukeweapons.mine", this);
            permission.RegisterPermission("nukeweapons.explosive", this);
            permission.RegisterPermission("nukeweapons.grenade", this);
            permission.RegisterPermission("nukeweapons.all", this);
            permission.RegisterPermission("nukeweapons.unlimited", this);
        }

        private bool HasAmmo(ulong player, NukeType type) 
            => _cachedAmmo.TryGetValue(player, out Hash<NukeType, int> value) && value[type] > 0;

        private Dictionary<string, int> GetCraftingComponents(NukeType type)
        {
            return type switch
            {
                NukeType.Mine => configData.Mines.CraftingCosts,
                NukeType.Rocket => configData.Rockets.CraftingCosts,
                NukeType.Bullet => configData.Bullets.CraftingCosts,
                NukeType.Explosive => configData.Explosives.CraftingCosts,
                NukeType.Grenade => configData.Grenades.CraftingCosts,
                _ => null
            };
        }

        private ConfigData.NWType GetConfigFromType(NukeType type)
        {
            return type switch
            {
                NukeType.Mine => configData.Mines,
                NukeType.Rocket => configData.Rockets,
                NukeType.Bullet => configData.Bullets,
                NukeType.Explosive => configData.Explosives,
                NukeType.Grenade => configData.Grenades,
                _ => null
            };
        }

        private void CheckPlayerEntry(BasePlayer player)
        {
            if (!_cachedAmmo.ContainsKey(player.userID))
            {
                _cachedAmmo.Add(player.userID, new Hash<NukeType, int>
                {
                    {NukeType.Bullet, 0 },
                    {NukeType.Explosive, 0 },
                    {NukeType.Grenade, 0 },
                    {NukeType.Mine, 0 },
                    {NukeType.Rocket, 0 },
                });
            }
            if (!_craftingTimers.ContainsKey(player.userID))
            {
                _craftingTimers.Add(player.userID, new Hash<NukeType, double>
                {
                    {NukeType.Bullet, -1 },
                    {NukeType.Explosive, -1 },
                    {NukeType.Grenade, -1 },
                    {NukeType.Mine, -1 },
                    {NukeType.Rocket, -1 },
                });
            }
        }
        #endregion

        #region Radiation Control
        private void InitializeZone(Vector3 location, float intensity, float duration, float radius, bool explosionType = false)
        {
            if (!ConVar.Server.radiation)
                ConVar.Server.radiation = true;

            if (explosionType)
                Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", location);
            else Effect.server.Run("assets/prefabs/npc/patrol helicopter/effects/rocket_explosion.prefab", location);

            RadiationZone radiationZone = new GameObject().AddComponent<RadiationZone>();
            radiationZone.Activate(location, radius, intensity);

            ZoneList listEntry = new ZoneList { zone = radiationZone };
            listEntry.time = timer.Once(duration, () => DestroyZone(listEntry));

            _radiationZones.Add(listEntry);
        }

        private void DestroyZone(ZoneList zone)
        {
            if (!_radiationZones.Contains(zone)) 
                return;
            
            int index = _radiationZones.FindIndex(a => a.zone == zone.zone);
            _radiationZones[index].time.Destroy();
            UnityEngine.Object.Destroy(_radiationZones[index].zone.gameObject);
            _radiationZones.Remove(zone);
        }

        private class Nuke : MonoBehaviour
        {
            private NukeWeapons instance;
            public NukeType type;
            public ConfigData.NWType.RadiationStats stats;

            private void OnDestroy()
            {
                bool useExplosion = false;
                switch (type)
                {
                    case NukeType.Mine:
                        useExplosion = true;
                        break;
                    case NukeType.Rocket:
                        break;
                    case NukeType.Bullet:
                        break;
                    case NukeType.Explosive:
                        useExplosion = true;
                        break;
                    case NukeType.Grenade:
                        break;
                    default:
                        break;
                }
                instance.InitializeZone(transform.position, 30, 10, 20, useExplosion);
            }

            public void InitializeComponent(NukeWeapons ins, NukeType typ, ConfigData.NWType.RadiationStats sta)
            {
                instance = ins;
                type = typ;
                stats = sta;
            }
        }

        public class ZoneList
        {
            public RadiationZone zone;
            public Timer time;
        }

        internal class RadiationZone : MonoBehaviour
        {            
            private void Awake()
            {
                gameObject.layer = (int)Layer.Reserved1;
                gameObject.name = "radiation_zone";

                Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
            }

            public void Activate(Vector3 pos, float radius, float amount)
            {
                transform.position = pos;

                SphereCollider sphereCollider = gameObject.GetComponent<SphereCollider>() ?? gameObject.AddComponent<SphereCollider>();
                sphereCollider.isTrigger = true;
                sphereCollider.radius = radius;

                TriggerRadiation triggerRadiation = gameObject.GetComponent<TriggerRadiation>() ?? gameObject.AddComponent<TriggerRadiation>();
                triggerRadiation.RadiationAmountOverride = amount;
                triggerRadiation.interestLayers = PlayerMask;
                triggerRadiation.enabled = true;

                gameObject.SetActive(true);
                enabled = true;
            }
        }

        #endregion
       
        #region Chat Commands
        [ChatCommand("nw")]
        private void cmdNukes(BasePlayer player, string command, string[] args)
        {
            if (HasPermission(player, NukeType.Bullet) || HasPermission(player, NukeType.Explosive) || HasPermission(player, NukeType.Grenade) || HasPermission(player, NukeType.Mine) || HasPermission(player, NukeType.Rocket))
            {
                CheckPlayerEntry(player);
                OpenCraftingMenu(player);
            }       
        }
        #endregion

        #region Permissions
        private bool HasPermission(BasePlayer player, NukeType type)
        {
            string perm = string.Empty;
            switch (type)
            {
                case NukeType.Mine:
                    perm = "nukeweapons.mine";
                    break;
                case NukeType.Rocket:
                    perm = "nukeweapons.rocket";
                    break;
                case NukeType.Bullet:
                    perm = "nukeweapons.bullet";
                    break;
                case NukeType.Explosive:
                    perm = "nukeweapons.explosive";
                    break;
                case NukeType.Grenade:
                    perm = "nukeweapons.grenade";
                    break;
                default:
                    break;
            }
            return permission.UserHasPermission(player.UserIDString, perm) || permission.UserHasPermission(player.UserIDString, "nukeweapons.all") || player.IsAdmin;
        } 
        
        private bool HasUnlimitedAmmo(BasePlayer player) => permission.UserHasPermission(player.UserIDString, "nukeweapons.unlimited");
        #endregion

        #region Config        
        private ConfigData configData;

        private class ConfigData
        {            
            public NWType Mines { get; set; }
            public NWType Rockets { get; set; }
            public NWType Bullets { get; set; }
            public NWType Grenades { get; set; }
            public NWType Explosives { get; set; }
            public Option Options { get; set; }
            public Dictionary<string, string> URL_IconList { get; set; }

            public class NWType
            {
                public bool Enabled { get; set; }
                public int MaxAllowed { get; set; }
                public int CraftTime { get; set; }
                public int CraftAmount { get; set; }
                public Dictionary<string, int> CraftingCosts { get; set; }
                public RadiationStats RadiationProperties { get; set; }

                public class RadiationStats
                {
                    public float Intensity { get; set; }
                    public float Duration { get; set; }
                    public float Radius { get; set; }
                }
            }

            public class Option
            {
                public string MSG_MainColor { get; set; }
                public string MSG_SecondaryColor { get; set; }
            }

            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(configData, true);
        }

        protected override void LoadDefaultConfig() => configData = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                Bullets = new ConfigData.NWType
                {
                    CraftAmount = 5,
                    CraftTime = 30,
                    CraftingCosts = new Dictionary<string, int>
                    {
                        {"ammo.rifle.explosive", 5 },
                        {"sulfur", 10 },
                        {"lowgradefuel", 10 }
                    },
                    Enabled = true,
                    MaxAllowed = 100,
                    RadiationProperties = new ConfigData.NWType.RadiationStats
                    {
                        Intensity = 15,
                        Duration = 3,
                        Radius = 5
                    }
                },
                Explosives = new ConfigData.NWType
                {
                    CraftAmount = 1,
                    CraftTime = 90,
                    CraftingCosts = new Dictionary<string, int>
                    {
                        {"explosive.timed", 1 },
                        {"sulfur", 150 },
                        {"lowgradefuel", 200 }
                    },
                    Enabled = true,
                    MaxAllowed = 3,
                    RadiationProperties = new ConfigData.NWType.RadiationStats
                    {
                        Intensity = 60,
                        Duration = 30,
                        Radius = 25
                    }
                },
                Grenades = new ConfigData.NWType
                {
                    CraftAmount = 1,
                    CraftTime = 45,
                    CraftingCosts = new Dictionary<string, int>
                    {
                        {"grenade.f1", 1 },
                        {"sulfur", 100 },
                        {"lowgradefuel", 100 }
                    },
                    Enabled = true,
                    MaxAllowed = 3,
                    RadiationProperties = new ConfigData.NWType.RadiationStats
                    {
                        Intensity = 35,
                        Duration = 15,
                        Radius = 15
                    }
                },
                Mines = new ConfigData.NWType
                {
                    CraftAmount = 1,
                    CraftTime = 60,
                    CraftingCosts = new Dictionary<string, int>
                    {
                        {"trap.landmine", 1 },
                        {"sulfur", 100 },
                        {"lowgradefuel", 150 }
                    },
                    Enabled = true,
                    MaxAllowed = 5,
                    RadiationProperties = new ConfigData.NWType.RadiationStats
                    {
                        Intensity = 70,
                        Duration = 25,
                        Radius = 20
                    }
                },
                Rockets = new ConfigData.NWType
                {
                    CraftAmount = 1,
                    CraftTime = 60,
                    CraftingCosts = new Dictionary<string, int>
                    {
                        {"ammo.rocket.basic", 1 },
                        {"sulfur", 150 },
                        {"lowgradefuel", 150 }
                    },
                    Enabled = true,
                    MaxAllowed = 3,
                    RadiationProperties = new ConfigData.NWType.RadiationStats
                    {
                        Intensity = 45,
                        Duration = 15,
                        Radius = 10
                    }
                },
                Options = new ConfigData.Option
                {
                    MSG_MainColor = "<color=#00CC00>",
                    MSG_SecondaryColor = "<color=#939393>"                    
                },
                URL_IconList = new Dictionary<string, string>
                {
                    {"BulletActive", "bulletactive.png" },
                    {"ExplosiveActive", "explosiveactive.png" },
                    {"GrenadeActive", "grenadeactive.png" },
                    {"MineActive", "landmineactive.png" },
                    {"RocketActive", "rocketactive.png" },
                    {"Bullet", "bullet.png" },
                    {"Explosive", "explosive.png" },
                    {"Grenade", "grenade.png" },
                    {"Mine", "landmine.png" },
                    {"Rocket", "rocket.png" },
                    {"Background", "background.png" }
                },
                Version = Version
            };
        }
        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            configData.Version = Version;
            PrintWarning("Config update completed!");
        }
        #endregion

        #region Data Management
        private void SaveData()
        {
            _nukeData.ammo = _cachedAmmo;
            _data.WriteObject(_nukeData);
        }

        private void SaveDisplayNames()
        {
            _itemNames.displayNames = _displayNames;
            item_names.WriteObject(_itemNames);
        }

        private void LoadData()
        {
            try
            {
                _nukeData = _data.ReadObject<NukeData>();
                _cachedAmmo = _nukeData.ammo;
            }
            catch
            {
                _nukeData = new NukeData();
            }
            try
            {
                _itemNames = item_names.ReadObject<ItemNames>();
            }
            catch
            {
                Puts("Couldn't load item display name data, creating new datafile");
                _itemNames = new ItemNames();
            }
        }

        private class NukeData
        {
            public Hash<ulong, Hash<NukeType, int>> ammo = new Hash<ulong, Hash<NukeType, int>>();
            public List<ulong> Mines = new List<ulong>();
        }

        private class ItemNames
        {
            public Hash<string, string> displayNames = new Hash<string, string>();
        }

        private class PlayerAmmo
        {
            public int Rockets;
            public int Mines;
            public int Bullets;
            public int Explosives;
            public int Grenades;
        }

        private enum NukeType
        {
            Mine,
            Rocket,
            Bullet,
            Explosive,
            Grenade
        }
        #endregion

        #region Image Storage      
        [ConsoleCommand("nukeicons")]
        private void cmdNukeIcons(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null) 
                return;
            
            PrintWarning("Storing icons to file storage...");     
            foreach (KeyValuePair<string, string> image in configData.URL_IconList)
                AddImage(image.Key, image.Value);
        }

        public void AddImage(string imageName, string fileName) => ImageLibrary.Call("AddImage", fileName.StartsWith("www") || fileName.StartsWith("http") ? fileName : dataDirectory + fileName, imageName);

        private string GetImage(string name) => (string)ImageLibrary.Call("GetImage", name);
        #endregion

        #region Messaging
        private void SendMSG(BasePlayer player, string message, string message2 = "") => SendReply(player, $"{configData.Options.MSG_MainColor}{message}</color>{configData.Options.MSG_SecondaryColor}{message2}</color>");

        private string MSG(string key, string playerid = null) => lang.GetMessage(key, this, playerid);

        private Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            {"Bullet", "Bullet" },
            {"Explosive", "Explosive" },
            {"Grenade", "Grenade" },
            {"Rocket", "Rocket" },
            {"Mine", "Mine" },
            {"Bullets", "Bullets" },
            {"Explosives", "Explosives" },
            {"Grenades", "Grenades" },
            {"Rockets", "Rockets" },
            {"Mines", "Mines" },
            {"activated", "You have activated Nuke <type>s" },
            {"Menu", "Menu" },
            {"Deactivate", "Deactivate" },
            {"Disarm", "Disarm" },
            {"Arm", "Arm" },
            {"Inventory Amount", "Inventory Amount" },
            {"Unlimited", "Unlimited" },
            {"Crafting...", "Crafting..." },
            {"Limit Reached", "Limit Reached" },
            {"Craft", "Craft" },
            {"Item Name", "Item Name" },
            {"Required Amount", "Required Amount" },
            {"Your Supply", "Your Supply" },
            {"Required Ingredients", "Required Ingredients" },
            {"Close", "Close" },
            {"OOA", "You have run out of Nuke" },
            {"Crafting", "Crafting" }
        };
        #endregion
    }
}
