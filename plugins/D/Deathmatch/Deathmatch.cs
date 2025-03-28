﻿// Requires: EventManager
using Newtonsoft.Json;
using Oxide.Plugins.EventManagerEx;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Deathmatch", "k1lly0u", "3.0.1"), Description("Deathmatch event mode for EventManager")]
    class Deathmatch : RustPlugin, IEventPlugin
    {
        #region Oxide Hooks
        private void OnServerInitialized()
        {
            EventManager.RegisterEvent(Title, this);

            GetMessage = Message;
        }

        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);
        
        private void Unload()
        {
            if (!EventManager.IsUnloading)
                EventManager.UnregisterEvent(Title);

            Configuration = null;
        }
        #endregion

        #region Event Checks
        public bool InitializeEvent(EventManager.EventConfig config) => EventManager.InitializeEvent<DeathmatchEvent>(this, config);
        
        public bool CanUseClassSelector => true;

        public bool RequireTimeLimit => true;

        public bool RequireScoreLimit => false;

        public bool UseScoreLimit => true;

        public bool UseTimeLimit => true;

        public bool IsTeamEvent => false;

        public void FormatScoreEntry(EventManager.ScoreEntry scoreEntry, ulong langUserId, out string score1, out string score2)
        {
            score1 = string.Format(Message("Score.Kills", langUserId), scoreEntry.value1);
            score2 = string.Format(Message("Score.Deaths", langUserId), scoreEntry.value2);
        }

        public List<EventManager.EventParameter> AdditionalParameters { get; } = new List<EventManager.EventParameter>
        {
            new EventManager.EventParameter
            {
                DataType = "bool",
                Field = "closeOnStart",
                Input = EventManager.EventParameter.InputType.Toggle,
                IsRequired = false,
                Name = "Close Event On Start",
                DefaultValue = false
            }
        };

        public string ParameterIsValid(string fieldName, object value) => null;
        #endregion

        #region Event Classes
        public class DeathmatchEvent : EventManager.BaseEventGame
        {
            public EventManager.BaseEventPlayer winner;

            internal override void PrestartEvent()
            {
                if (Config.GetParameter<bool>("closeEventOnStart"))
                    CloseEvent();

                base.PrestartEvent();
            }

            internal override void OnEventPlayerDeath(EventManager.BaseEventPlayer victim, EventManager.BaseEventPlayer attacker = null, HitInfo info = null)
            {
                if (victim == null)
                    return;

                victim.OnPlayerDeath(attacker, Configuration.RespawnTime);

                if (attacker != null && victim != attacker)
                {
                    attacker.OnKilledPlayer(info);

                    if (Config.ScoreLimit > 0 && attacker.Kills >= Config.ScoreLimit)
                    {
                        winner = attacker;
                        InvokeHandler.Invoke(this, EndEvent, 0.1f);
                        return;
                    }
                }

                UpdateScoreboard();
                base.OnEventPlayerDeath(victim, attacker);
            }

            protected override void GetWinningPlayers(ref List<EventManager.BaseEventPlayer> winners)
            {
                if (winner == null)
                {
                    if (eventPlayers.Count > 0)
                    {
                        int kills = 0;
                        int deaths = 0;

                        for (int i = 0; i < eventPlayers.Count; i++)
                        {
                            EventManager.BaseEventPlayer eventPlayer = eventPlayers[i];
                            if (eventPlayer == null)
                                continue;

                            if (eventPlayer.Kills > kills)
                            {
                                winner = eventPlayer;
                                kills = eventPlayer.Kills;
                                deaths = eventPlayer.Deaths;
                            }
                            else if (eventPlayer.Kills == kills)
                            {
                                if (eventPlayer.Deaths < deaths)
                                {
                                    winner = eventPlayer;
                                    kills = eventPlayer.Kills;
                                    deaths = eventPlayer.Deaths;
                                }
                            }
                        }
                    }
                }

                if (winner != null)
                    winners.Add(winner);
            }

            #region Scoreboards
            protected override void BuildScoreboard()
            {
                scoreContainer = EMInterface.CreateScoreboardBase(this);

                int index = -1;

                if (Config.ScoreLimit > 0)                
                    EMInterface.CreatePanelEntry(scoreContainer, string.Format(GetMessage("Score.Limit", 0UL), Config.ScoreLimit), index += 1);
                
                EMInterface.CreateScoreEntry(scoreContainer, string.Empty, "K", "D", index += 1);

                for (int i = 0; i < Mathf.Min(scoreData.Count, 15); i++)
                {
                    EventManager.ScoreEntry score = scoreData[i];
                    EMInterface.CreateScoreEntry(scoreContainer, score.displayName, ((int)score.value1).ToString(), ((int)score.value2).ToString(), i + index + 1);
                }
            }

            protected override float GetFirstScoreValue(EventManager.BaseEventPlayer eventPlayer) => eventPlayer.Kills;

            protected override float GetSecondScoreValue(EventManager.BaseEventPlayer eventPlayer) => eventPlayer.Deaths;

            protected override void SortScores(ref List<EventManager.ScoreEntry> list)
            {
                list.Sort(delegate (EventManager.ScoreEntry a, EventManager.ScoreEntry b)
                {
                    int primaryScore = a.value1.CompareTo(b.value1);

                    if (primaryScore == 0)
                        return a.value2.CompareTo(b.value2) * -1;

                    return primaryScore;
                });
            }
            #endregion
        }
        #endregion

        #region Config        
        private static ConfigData Configuration;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Respawn time (seconds)")]
            public int RespawnTime { get; set; }

            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Configuration = Config.ReadObject<ConfigData>();

            if (Configuration.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(Configuration, true);
        }

        protected override void LoadDefaultConfig() => Configuration = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                RespawnTime = 5,
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(Configuration, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            Configuration.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion

        #region Localization
        public string Message(string key, ulong playerId = 0U) => lang.GetMessage(key, this, playerId != 0U ? playerId.ToString() : null);
        
        private static Func<string, ulong, string> GetMessage;

        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Score.Kills"] = "Kills: {0}",
            ["Score.Deaths"] = "Deaths: {0}",
            ["Score.Name"] = "Kills",
            ["Score.Limit"] = "Score Limit : {0}"
        };
        #endregion
    }
}
