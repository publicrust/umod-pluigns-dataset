﻿using System;
using System.ComponentModel;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Magic Rain Of Fire Panel", "MJSU", "1.0.2")]
    [Description("Displays if the rain of fire event is active")]
    public class MagicRainOfFirePanel : RustPlugin
    {
        #region Class Fields
        [PluginReference] private readonly Plugin MagicPanel;

        private PluginConfig _pluginConfig; //Plugin Config
        private bool _isRainOfFireActive;
        private Timer _endTimer;

        private enum UpdateEnum { All = 1, Panel = 2, Image = 3, Text = 4 }
        #endregion

        #region Setup & Loading
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Loading Default Config");
        }

        protected override void LoadConfig()
        {
            string path = $"{Manager.ConfigPath}/MagicPanel/{Name}.json";
            DynamicConfigFile newConfig = new DynamicConfigFile(path);
            if (!newConfig.Exists())
            {
                LoadDefaultConfig();
                newConfig.Save();
            }
            try
            {
                newConfig.Load();
            }
            catch (Exception ex)
            {
                RaiseError("Failed to load config file (is the config file corrupt?) (" + ex.Message + ")");
                return;
            }
            
            newConfig.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = AdditionalConfig(newConfig.ReadObject<PluginConfig>());
            newConfig.WriteObject(_pluginConfig);
        }

        private PluginConfig AdditionalConfig(PluginConfig config)
        {
            config.Panel = new Panel
            {
                Image = new PanelImage
                {
                    Enabled = config.Panel?.Image?.Enabled ?? true,
                    Color = config.Panel?.Image?.Color ?? "#FFFFFFFF",
                    Order = config.Panel?.Image?.Order ?? 6,
                    Width = config.Panel?.Image?.Width ?? 1f,
                    Url = config.Panel?.Image?.Url ?? "https://i.postimg.cc/fT5P4K4Y/P01HCi3.png",
                    Padding = config.Panel?.Image?.Padding ?? new TypePadding(0.05f, 0.05f, 0.05f, 0.05f)
                }
            };
            config.PanelSettings = new PanelRegistration
            {
                BackgroundColor = config.PanelSettings?.BackgroundColor ?? "#FFF2DF08",
                Dock = config.PanelSettings?.Dock ?? "center",
                Order = config.PanelSettings?.Order ?? 14,
                Width = config.PanelSettings?.Width ?? 0.02f
            };
            return config;
        }

        private void OnServerInitialized()
        {
            MagicPanelRegisterPanels();
        }

        private void MagicPanelRegisterPanels()
        {
            if (MagicPanel == null)
            {
                PrintError("Missing plugin dependency MagicPanel: https://umod.org/plugins/magic-panel");
                return;
            }
        
            MagicPanel?.Call("RegisterGlobalPanel", this, Name, JsonConvert.SerializeObject(_pluginConfig.PanelSettings), nameof(GetPanel));
        }
        #endregion

        #region uMod Hooks

        private void OnEntitySpawned(TimedExplosive explosive)
        {
            NextTick(() =>
            {
                if (!CanShowPanel(explosive))
                {
                    return;
                }

                if (!_isRainOfFireActive)
                {
                    MagicPanel?.Call("UpdatePanel", Name, (int)UpdateEnum.Image);
                }
                
                _isRainOfFireActive = true;
                _endTimer?.Destroy();
                _endTimer = timer.In(_pluginConfig.EventEnd, () =>
                {
                    _isRainOfFireActive = false;
                    MagicPanel?.Call("UpdatePanel", Name, (int) UpdateEnum.Image);
                });
            });
        }
        #endregion

        #region MagicPanel Hook
        private Hash<string, object> GetPanel()
        {
            Panel panel = _pluginConfig.Panel;
            PanelImage image = panel.Image;
            if (image != null)
            {
                image.Color = _isRainOfFireActive ? _pluginConfig.ActiveColor : _pluginConfig.InactiveColor;
            }

            return panel.ToHash();
        }
        #endregion
        
        #region Helper Methods

        private bool CanShowPanel(TimedExplosive explosive)
        {
            if (!(explosive?.IsValid() ?? false))
            {
                return false;
            }
            
            ServerProjectile proj = explosive.GetComponent<ServerProjectile>();
            if (proj == null)
            {
                return false;
            }

            //Rain of Fire checks
            if (proj.gravityModifier != 0f 
                || proj.speed != 25f 
                || explosive.timerAmountMin != 300 
                || explosive.timerAmountMax != 300)
            {
                return false;
            }
            
            object result = Interface.Call("MagicPanelCanShow", Name, explosive);
            if (result is bool)
            {
                return (bool) result;
            }

            return true;
        }
        #endregion

        #region Classes

        private class PluginConfig
        {
            [DefaultValue("#00FF00FF")]
            [JsonProperty(PropertyName = "Active Color")]
            public string ActiveColor { get; set; }

            [DefaultValue("#FFFFFF1A")]
            [JsonProperty(PropertyName = "Inactive Color")]
            public string InactiveColor { get; set; }

            [DefaultValue(5f)]
            [JsonProperty(PropertyName = "Event ends after last rocket (Seconds)")]
            public float EventEnd { get; set; }

            [JsonProperty(PropertyName = "Panel Settings")]
            public PanelRegistration PanelSettings { get; set; }

            [JsonProperty(PropertyName = "Panel Layout")]
            public Panel Panel { get; set; }
        }

        private class PanelRegistration
        {
            public string Dock { get; set; }
            public float Width { get; set; }
            public int Order { get; set; }
            public string BackgroundColor { get; set; }
        }

        private class Panel
        {
            public PanelImage Image { get; set; }
            
            public Hash<string, object> ToHash()
            {
                return new Hash<string, object>
                {
                    [nameof(Image)] = Image.ToHash(),
                };
            }
        }

        private abstract class PanelType
        {
            public bool Enabled { get; set; }
            public string Color { get; set; }
            public int Order { get; set; }
            public float Width { get; set; }
            public TypePadding Padding { get; set; }
            
            public virtual Hash<string, object> ToHash()
            {
                return new Hash<string, object>
                {
                    [nameof(Enabled)] = Enabled,
                    [nameof(Color)] = Color,
                    [nameof(Order)] = Order,
                    [nameof(Width)] = Width,
                    [nameof(Padding)] = Padding.ToHash(),
                };
            }
        }

        private class PanelImage : PanelType
        {
            public string Url { get; set; }
            
            public override Hash<string, object> ToHash()
            {
                Hash<string, object> hash = base.ToHash();
                hash[nameof(Url)] = Url;
                return hash;
            }
        }

        private class TypePadding
        {
            public float Left { get; set; }
            public float Right { get; set; }
            public float Top { get; set; }
            public float Bottom { get; set; }

            public TypePadding(float left, float right, float top, float bottom)
            {
                Left = left;
                Right = right;
                Top = top;
                Bottom = bottom;
            }
            
            public Hash<string, object> ToHash()
            {
                return new Hash<string, object>
                {
                    [nameof(Left)] = Left,
                    [nameof(Right)] = Right,
                    [nameof(Top)] = Top,
                    [nameof(Bottom)] = Bottom
                };
            }
        }
        #endregion
    }
}
