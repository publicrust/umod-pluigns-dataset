﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Game.Rust.Libraries;
using Oxide.Plugins.DuelistExtensionMethods;
using Rust;
using Rust.Workshop;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Duelist", "nivex", "1.3.5")]
    [Description("1v1 and team deathmatch event.")]
    public class Duelist : RustPlugin
    {
        [PluginReference] Plugin Kits, ZoneManager, Economics, ServerRewards, Clans, AimTrain, LustyMap;

        private static Duelist Instance;

        public enum Team { Good = 0, Evil = 1, None = 2 }
        private const string hewwPrefab = "assets/prefabs/building/wall.external.high.wood/wall.external.high.wood.prefab";
        private const string heswPrefab = "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab";
        private const bool debugMode = false;
        private List<string> readyUiList = new List<string>();
        private List<string> spectators = new List<string>();
        private List<Rematch> rematches = new List<Rematch>();
        private Dictionary<string, AttackerInfo> tdmAttackers = new Dictionary<string, AttackerInfo>();
        private Dictionary<string, string> tdmKits = new Dictionary<string, string>();
        private HashSet<GoodVersusEvilMatch> tdmMatches = new HashSet<GoodVersusEvilMatch>();
        private List<DuelingZone> duelingZones = new List<DuelingZone>(); // where all the fun is at
        private StoredData duelsData = new StoredData();
        private Dictionary<string, string> dataDuelists = new Dictionary<string, string>(); // active duelers
        private Dictionary<string, long> dataImmunity = new Dictionary<string, long>(); // players immune to damage
        private Dictionary<string, Vector3> dataImmunitySpawns = new Dictionary<string, Vector3>(); // players spawn points
        private int blockedMask = LayerMask.GetMask("Player (Server)", "Prevent Building", "Construction", "Deployed", "Trigger"); // layers we won't be setting a zone within 50 meters of
        private int constructionMask = LayerMask.GetMask("Construction", "Deployed");
        private bool matchUpdateRequired;
        private int groundMask = LayerMask.GetMask("Terrain", "World", "Default"); // used to find dueling zone/set custom zone and create spawn points
        private int wallMask = LayerMask.GetMask("Terrain", "World", "Default", "Construction", "Deployed");
        private int waterMask = LayerMask.GetMask("Water"); // used to count water colliders when finding a random dueling zone on the map
        private int worldMask = LayerMask.GetMask("World");
        private Timer announceTimer;
        private SortedDictionary<string, string> boneTags = new SortedDictionary<string, string> { ["r_"] = "Right ", ["l_"] = "Left ", [".prefab"] = string.Empty, ["1"] = string.Empty, ["2"] = string.Empty, ["3"] = string.Empty, ["4"] = string.Empty, ["END"] = string.Empty, ["_"] = " ", ["."] = " " };
        private Dictionary<string, string> announcements = new Dictionary<string, string>(); // users id and announcement
        private Dictionary<string, long> dataDeath = new Dictionary<string, long>(); // users id and timestamp of when they're to be executed
        private Dictionary<string, string> dataRequests = new Dictionary<string, string>(); // users requesting a duel and to whom
        private Dictionary<string, bool> deployables = new Dictionary<string, bool>();
        private Dictionary<ulong, List<BaseEntity>> duelEntities = new Dictionary<ulong, List<BaseEntity>>();
        private DynamicConfigFile duelsFile;
        private Timer eventTimer; // timer to check for immunity and auto death time of duelers
        private Timer matchTimer; // timer to check for updates to the match ui
        private SpawnFilter filter = new SpawnFilter(); // RandomDropPosition()
        private Dictionary<Vector3, float> managedZones = new Dictionary<Vector3, float>(); // blocked zones from zonemanager plugin
        private List<Vector3> monuments = new List<Vector3>(); // positions of monuments on the server
        private Dictionary<string, string> prefabs = new Dictionary<string, string>();
        private bool resetDuelists; // if wipe is detected then assign awards and wipe VictoriesSeed / LossesSeed
        private Dictionary<string, List<ulong>> skinsCache = new Dictionary<string, List<ulong>>(); // used to randomize custom kit skins which skin id values are 0
        private Dictionary<string, string> tdmRequests = new Dictionary<string, string>(); // users requesting a deathmatch and to whom
        private Dictionary<string, List<ulong>> workshopskinsCache = new Dictionary<string, List<ulong>>();
        private List<string> dcsBlock = new List<string>(); // users blocked from 1v1 for 60 seconds after suiciding or disconnecting
        private Dictionary<string, string> playerZones = new Dictionary<string, string>(); // id, set zone name

        public class StoredData
        {
            public List<string> Allowed = new List<string>(); // list of users that allow duel requests
            public Dictionary<string, List<string>> AutoGeneratedSpawns = new Dictionary<string, List<string>>();
            public Dictionary<string, string> Bans = new Dictionary<string, string>(); // users banned from dueling
            public Dictionary<string, BetInfo> Bets = new Dictionary<string, BetInfo>(); // active bets users have placed
            public Dictionary<string, List<string>> BlockedUsers = new Dictionary<string, List<string>>(); // users and the list of players they blocked from requesting duels with
            public List<string> Chat = new List<string>(); // user ids of those who opted out of seeing duel death messages
            public List<string> ChatEx = new List<string>(); // user ids of those who opted to see duel death messages when the config blocks them for all players
            public Dictionary<string, List<BetInfo>> ClaimBets = new Dictionary<string, List<BetInfo>>(); // active bets users need to claim after winning a bet
            public Dictionary<string, string> CustomKits = new Dictionary<string, string>(); // userid and custom kit
            public bool DuelsEnabled; // enable/disable dueling for all players (not admins)
            public Dictionary<string, string> Homes = new Dictionary<string, string>(); // user id and location of where they teleported from
            public Dictionary<string, string> Kits = new Dictionary<string, string>(); // userid and kit. give kit when they wake up inside of the dueling zone
            public Dictionary<string, int> Losses = new Dictionary<string, int>(); // user id / losses for lifetime
            public Dictionary<string, int> LossesSeed = new Dictionary<string, int>(); // user id / losses for seed
            public SortedDictionary<long, string> Queued = new SortedDictionary<long, string>(); // queued duelers sorted by timestamp and user id. first come first serve
            public List<string> Restricted = new List<string>(); // list of users blocked from requesting a duel for 60 seconds
            public List<string> Spawns = new List<string>(); // custom spawn points
            public List<string> AutoReady = new List<string>();
            public Dictionary<string, int> MatchVictories = new Dictionary<string, int>(); // player name & total wins
            public Dictionary<string, int> MatchVictoriesSeed = new Dictionary<string, int>(); // player name & wins for current seed
            public Dictionary<string, int> MatchLosses = new Dictionary<string, int>(); // player name & total losses
            public Dictionary<string, int> MatchLossesSeed = new Dictionary<string, int>(); // player name & losses for current seed
            public Dictionary<string, int> MatchDeaths = new Dictionary<string, int>(); // player name & total deaths
            public Dictionary<string, int> MatchDeathsSeed = new Dictionary<string, int>(); // player name & deaths for the seed
            public Dictionary<string, int> MatchKills = new Dictionary<string, int>(); // player name & total kills
            public Dictionary<string, int> MatchKillsSeed = new Dictionary<string, int>(); // player name & kills for current seed
            public Dictionary<string, Dictionary<string, int>> MatchSizesVictories = new Dictionary<string, Dictionary<string, int>>(); // size, id, wins
            public Dictionary<string, Dictionary<string, int>> MatchSizesVictoriesSeed = new Dictionary<string, Dictionary<string, int>>(); // size, id, wins seed
            public Dictionary<string, Dictionary<string, int>> MatchSizesLosses = new Dictionary<string, Dictionary<string, int>>(); // size, id, losses
            public Dictionary<string, Dictionary<string, int>> MatchSizesLossesSeed = new Dictionary<string, Dictionary<string, int>>(); // size, id, losses seed
            public int TotalDuels; // the total amount of duels ever played on the server
            public Dictionary<string, int> Victories = new Dictionary<string, int>(); // user id / wins for lifetime
            public Dictionary<string, int> VictoriesSeed = new Dictionary<string, int>(); // user id / wins for seed
            public List<string> ZoneIds = new List<string>(); // the locations of each dueling zone
            public Dictionary<string, string> DuelZones = new Dictionary<string, string>(); // location, name
        }

        private class Tracker : FacepunchBehaviour
        {
            public BasePlayer player;
            private Duelist _;

            private void Awake()
            {
                player = GetComponent<BasePlayer>();
            }

            public void Init(Duelist _)
            {
                this._ = _;
                InvokeRepeating(Track, 0f, 0.5f);
            }

            private void Track()
            {
                if (player == null || player.transform == null)
                {
                    Destroy(this);
                    return;
                }

                if (_.dataDuelists.ContainsKey(player.UserIDString) || _.tdmMatches.Exists(team => team.GetTeam(player) != Team.None))
                {
                    if (!_.DuelTerritory(player.transform.position))
                    {
                        player.inventory.Strip();
                        Destroy(this);
                    }
                }
            }

            private void OnDestroy()
            {
                try { CancelInvoke(); } catch { }
                Destroy(this);
            }
        }

        public class Rematch
        {
            public Rematch(Duelist _)
            {
                this._ = _;
            }
            private Duelist _;
            public List<BasePlayer> Duelists = new List<BasePlayer>();
            public List<BasePlayer> Ready = new List<BasePlayer>();
            private List<BasePlayer> Evil = new List<BasePlayer>();
            private List<BasePlayer> Good = new List<BasePlayer>();
            public GoodVersusEvilMatch match;
            private Timer _notify;

            public List<BasePlayer> Players
            {
                get
                {
                    Duelists.RemoveAll(IsNotConnected);
                    Good.RemoveAll(IsNotConnected);
                    Evil.RemoveAll(IsNotConnected);
                    Ready.RemoveAll(IsNotConnected);

                    if (match == null)
                        return Duelists;

                    var _list = new List<BasePlayer>();

                    _list.AddRange(Good);
                    _list.AddRange(Evil);

                    return _list;
                }
            }

            public bool HasPlayer(BasePlayer player)
            {
                return Players.Contains(player);
            }

            public bool AddRange(List<BasePlayer> players, Team team)
            {
                foreach (var player in players)
                {
                    if (IsNotConnected(player) || _.InEvent(player) || Good.Contains(player) || Evil.Contains(player))
                        break;

                    if (team == Team.Evil)
                        Evil.Add(player);
                    else
                        Good.Add(player);
                }

                return (team == Team.Evil ? Evil.Count : Good.Count) == players.Count;
            }

            public bool IsReady(BasePlayer player)
            {
                if (_.InEvent(player) || !_.IsNewman(player) || _.duelsData.Bans.ContainsKey(player.UserIDString))
                    return false;

                return true;
            }

            public bool IsReady()
            {
                if (Players.Exists(player => !IsReady(player)))
                {
                    Reset("RematchFailed2");
                    return false;
                }

                return Ready.Count == (match == null ? 2 : match.TeamSize * 2);
            }

            private void Reset(string key)
            {
                _.tdmMatches.Remove(match);
                MessageAll(key);
                Duelists.Clear();
                Good.Clear();
                Evil.Clear();
                Ready.Clear();
                _notify?.Destroy();
                _.rematches.Remove(this);
                _.matchUpdateRequired = true;
            }

            public void MessageAll(string key, params object[] args)
            {
                foreach (var player in Players)
                {
                    Message(player, _.msg(key, player.UserIDString, args));
                }
            }

            public void Notify()
            {
                MessageAll("RematchNotify", 60f, match == null ? _.szDuelChatCommand : _.szMatchChatCommand);

                foreach (var player in Players)
                    if (_.duelsData.AutoReady.Contains(player.UserIDString))
                        Ready.Add(player);

                if (IsReady())
                {
                    Start();
                    _.rematches.Remove(this);
                }
                else if (match == null || !match.IsPublic)
                    _notify = _.timer.Once(60f, Cancel);
            }

            private void Cancel()
            {
                if (match != null && match.IsPublic)
                    return;

                if (_.rematches.Contains(this))
                {
                    if (_.sendHomeSpectatorWhenRematchTimesOut)
                    {
                        foreach (var player in Players)
                        {
                            if (_.IsSpectator(player))
                            {
                                _.EndSpectate(player);
                                _.SendHome(player);
                            }
                        }
                    }

                    if (match != null)
                    {
                        match.Reuse();
                    }

                    _.tdmMatches.Remove(match);
                    Reset("RematchTimedOut");
                }
            }

            public void Start()
            {
                if (match == null)
                {
                    var player = Ready[0];
                    var target = Ready[1];

                    if (!_.SelectZone(player, target))
                    {
                        Message(player, _.msg("AllZonesFull", player.UserIDString, _.duelingZones.Count, _.playersPerZone));
                        Message(target, _.msg("AllZonesFull", target.UserIDString, _.duelingZones.Count, _.playersPerZone));
                    }
                }
                else
                {
                    match.Reuse();
                    _.tdmMatches.Add(match);

                    if (!AddMatchPlayers(Good, Team.Good) || !AddMatchPlayers(Evil, Team.Evil))
                    {
                        Reset("RematchFailed");
                        match.Reuse();
                    }
                }

                _notify?.Destroy();
                _.rematches.Remove(this);
            }

            private bool AddMatchPlayers(List<BasePlayer> players, Team team)
            {
                foreach (var player in players)
                    if (!match.AddMatchPlayer(player, team))
                        return false;

                return true;
            }
        }

        public class AttackerInfo
        {
            public string AttackerName = "";
            public string AttackerId = "";
            public string BoneName = "";
            public string Distance = "";
            public string Weapon = "";
        }

        public class GoodVersusEvilMatch
        {
            public GoodVersusEvilMatch(Duelist _)
            {
                this._ = _;
            }
            private Duelist _;
            private HashSet<ulong> _banned = new HashSet<ulong>();
            private HashSet<BasePlayer> _evil = new HashSet<BasePlayer>();
            private HashSet<ulong> _evilKIA = new HashSet<ulong>();
            private List<BasePlayer> _evilRematch = new List<BasePlayer>();
            private HashSet<BasePlayer> _good = new HashSet<BasePlayer>();
            private HashSet<ulong> _goodKIA = new HashSet<ulong>();
            private List<BasePlayer> _goodRematch = new List<BasePlayer>();
            private string _goodHostName = "";
            private string _evilHostName = "";
            private string _goodHostId = "";
            private string _evilHostId = "";
            private string _goodCode = "";
            private string _evilCode = "";
            private int _teamSize = 2;
            private bool _started;
            private bool _ended;
            private string _kit = "";
            private DuelingZone _zone;
            private Timer _queueTimer;
            private bool _enteredQueue;
            private bool _public;
            public bool CanRematch = true;

            public string Id
            {
                get
                {
                    return _goodHostId + _evilHostId;
                }
            }

            public string Versus
            {
                get
                {
                    return string.Format("{0} / {1} {2}v{2}", _goodHostName, _evilHostName, _teamSize);
                }
            }

            public bool IsPublic
            {
                get
                {
                    return _public;
                }
                set
                {
                    _public = value;
                    _.matchUpdateRequired = true;
                    MessageAll(_public ? "MatchPublic" : "MatchPrivate");
                }
            }

            public int TeamSize
            {
                get
                {
                    return _teamSize;
                }
                set
                {
                    if (IsStarted)
                        return;

                    _teamSize = value;
                    _.matchUpdateRequired = true;
                    MessageAll("MatchSizeChanged", _teamSize);
                }
            }

            public DuelingZone Zone
            {
                get
                {
                    return _zone;
                }
            }

            public bool EitherEmpty
            {
                get
                {
                    return _good.Count == 0 || _evil.Count == 0;
                }
            }

            public bool IsStarted
            {
                get
                {
                    return _started;
                }
                set
                {
                    _started = value;
                    _.matchUpdateRequired = true;
                }
            }

            public bool IsOver
            {
                get
                {
                    return _ended;
                }
                set
                {
                    _ended = value;
                    _.matchUpdateRequired = true;
                }
            }

            public string Kit
            {
                get
                {
                    return _kit;
                }
                set
                {
                    _kit = value;

                    if (!EitherEmpty)
                    {
                        _good.RemoveWhere(IsNotConnected);
                        _evil.RemoveWhere(IsNotConnected);

                        _good.ToList().ForEach(player => _.duelsData.Kits[player.UserIDString] = _kit);
                        _evil.ToList().ForEach(player => _.duelsData.Kits[player.UserIDString] = _kit);

                        MessageAll("MatchKitSet", _kit);
                    }
                }
            }

            public void Reuse()
            {
                if (_zone != null)
                    _zone.IsLocked = false;

                _evilRematch.Clear();
                _goodRematch.Clear();
                _good.Clear();
                _evil.Clear();
                _goodKIA.Clear();
                _evilKIA.Clear();
                _started = false;
                _ended = false;
                _enteredQueue = false;
                _kit = _.GetRandomKit();
                _goodHostId = BasePlayer.activePlayerList.FirstOrDefault(x => x.displayName == _goodHostName)?.UserIDString ?? _goodHostId;
                _evilHostId = BasePlayer.activePlayerList.FirstOrDefault(x => x.displayName == _evilHostName)?.UserIDString ?? _evilHostId;
                _.matchUpdateRequired = true;
            }

            public void Setup(BasePlayer player, BasePlayer target)
            {
                _.tdmMatches.Add(this);
                _goodHostName = player.displayName;
                _goodHostId = player.UserIDString;
                _evilHostName = target.displayName;
                _evilHostId = target.UserIDString;
                _goodCode = Random.Range(10000, 99999).ToString();
                _evilCode = Random.Range(10000, 99999).ToString();

                if (_teamSize < _.minDeathmatchSize)
                    _teamSize = _.minDeathmatchSize;

                AddMatchPlayer(player, Team.Good);
                AddMatchPlayer(target, Team.Evil);

                if (_.tdmKits.ContainsKey(player.UserIDString))
                {
                    Kit = _.tdmKits[player.UserIDString];
                    _.tdmKits.Remove(player.UserIDString);
                }
                else if (_.tdmKits.ContainsKey(target.UserIDString))
                {
                    Kit = _.tdmKits[target.UserIDString];
                    _.tdmKits.Remove(target.UserIDString);
                }
                else
                    Kit = _.GetRandomKit();

                if (TeamSize > 1)
                {
                    Message(player, _.msg("MatchOpened", player.UserIDString, _.szMatchChatCommand, _goodCode));
                    Message(target, _.msg("MatchOpened", target.UserIDString, _.szMatchChatCommand, _evilCode));
                }

                _.matchUpdateRequired = true;
            }

            public bool IsFull()
            {
                return _good.Count == _teamSize && _evil.Count == _teamSize;
            }

            public bool IsFull(Team team)
            {
                return team == Team.Good ? _good.Count == _teamSize : _evil.Count == _teamSize;
            }

            public void MessageAll(string key, params object[] args)
            {
                _.Message(_good, key, args);
                _.Message(_evil, key, args);
            }

            public Team GetTeam(BasePlayer player)
            {
                return _good.Contains(player) ? Team.Good : _evil.Contains(player) ? Team.Evil : Team.None;
            }

            public bool IsHost(BasePlayer player)
            {
                return player.UserIDString == _goodHostId || player.UserIDString == _evilHostId;
            }

            public void SetCode(BasePlayer player, string code)
            {
                if (GetTeam(player) == Team.Evil)
                    _evilCode = code;
                else if (GetTeam(player) == Team.Good)
                    _goodCode = code;
            }

            public string Code(Team team)
            {
                return team == Team.Good ? _goodCode : _evilCode;
            }

            public bool AlliedTo(BasePlayer player, Team team)
            {
                return _.IsAllied(player.UserIDString, team == Team.Good ? _goodHostId : _evilHostId);
            }

            public bool IsBanned(ulong targetId)
            {
                return _banned.Contains(targetId);
            }

            public bool Ban(BasePlayer target)
            {
                if (target.UserIDString == _goodHostId || target.UserIDString == _evilHostId || IsBanned(target.userID))
                    return false;

                _banned.Add(target.userID);
                RemoveMatchPlayer(target);
                return true;
            }

            public bool Equals(GoodVersusEvilMatch match)
            {
                return match._good.Equals(_good) && match._evil.Equals(_evil);
            }

            public string GetNames(Team team)
            {
                return string.Join(", ", team == Team.Good ? _good.ToList().Select(player => player.displayName) : _evil.ToList().Select(player => player.displayName));
            }

            public void GiveShirt(BasePlayer player)
            {
                Item item = ItemManager.CreateByName(_.teamShirt, 1, GetTeam(player) == Team.Evil ? _.teamEvilShirt : _.teamGoodShirt);

                if (item == null)
                    return;

                if (item.info.category != ItemCategory.Attire)
                {
                    item.Remove(0.01f);
                    return;
                }

                foreach (Item wear in player.inventory.containerWear.itemList)
                {
                    if (wear.info.shortname.Contains("shirt"))
                    {
                        wear.RemoveFromContainer();
                        wear.Remove(0.01f);
                        break;
                    }
                }

                item.MoveToContainer(player.inventory.containerWear, -1, false);

                if (!player.inventory.containerWear.HasFlag(ItemContainer.Flag.IsLocked))
                    player.inventory.containerWear.SetFlag(ItemContainer.Flag.IsLocked, true);
            }

            public bool AddMatchPlayer(BasePlayer player, Team team)
            {
                if (_started)
                {
                    Message(player, _.msg("MatchStartedAlready", player.UserIDString));
                    return false;
                }

                _good.RemoveWhere(IsNotConnected);
                _evil.RemoveWhere(IsNotConnected);

                if (_banned.Contains(player.userID))
                    return false;

                if (!_.IsNewman(player))
                {
                    Message(player, _.msg("MustBeNaked", player.UserIDString));
                    return false;
                }

                switch (team)
                {
                    case Team.Good:
                        if (_good.Count == _teamSize)
                        {
                            Message(player, _.msg("MatchTeamFull", player.UserIDString, _teamSize));
                            return false;
                        }

                        _good.Add(player);
                        MessageAll("MatchJoinedTeam", player.displayName, _goodHostName, _good.Count, _teamSize, _evilHostName, _evil.Count);
                        break;
                    case Team.Evil:
                        if (_evil.Count == _teamSize)
                        {
                            Message(player, _.msg("MatchTeamFull", player.UserIDString, _teamSize));
                            return false;
                        }

                        _evil.Add(player);
                        MessageAll("MatchJoinedTeam", player.displayName, _evilHostName, _evil.Count, _teamSize, _goodHostName, _good.Count);
                        break;
                }

                if (_good.Count == _teamSize && _evil.Count == _teamSize)
                    Queue();

                return true;
            }

            public bool RemoveMatchPlayer(BasePlayer player)
            {
                if (player == null)
                    return false;

                if (player.inventory.containerWear.HasFlag(ItemContainer.Flag.IsLocked))
                    player.inventory.containerWear.SetFlag(ItemContainer.Flag.IsLocked, false);

                _.Metabolize(player, false);
                _.Track(player, false);
                _.RemoveEntities(player.userID);
                Interface.Oxide.CallHook("DisableBypass", player.userID);

                if (_.DuelTerritory(player.transform.position))
                {
                    if (_.sendDefeatedHome)
                        _.SendHome(player);
                    else
                        _.StartSpectate(player);
                }

                if (IsOver)
                {
                    _good.Remove(player);
                    _evil.Remove(player);
                    return true;
                }

                if (_good.Remove(player))
                {
                    if (_good.Count == 0)
                    {
                        if (_started)
                        {
                            _goodKIA.Add(player.userID);
                            _goodRematch.Add(player);
                        }
                        else
                            MessageAll("MatchNoPlayersLeft");

                        EndMatch(Team.Evil);
                        return true;
                    }
                    if (_started)
                    {
                        _goodKIA.Add(player.userID);
                        _goodRematch.Add(player);
                    }

                    if (player.UserIDString == _goodHostId)
                        AssignGoodHostId();

                    return true;
                }

                if (_evil.Remove(player))
                {
                    if (_evil.Count == 0)
                    {
                        if (_started)
                        {
                            _evilKIA.Add(player.userID);
                            _evilRematch.Add(player);
                        }
                        else
                            MessageAll("MatchNoPlayersLeft");

                        EndMatch(Team.Good);
                        return true;
                    }
                    if (_started)
                    {
                        _evilKIA.Add(player.userID);
                        _evilRematch.Add(player);
                    }

                    if (player.UserIDString == _evilHostId)
                        AssignEvilHostId();

                    return true;
                }

                return false;
            }

            private void AssignGoodHostId()
            {
                _good.RemoveWhere(IsNotConnected);

                if (_good.Count > 0)
                {
                    _goodHostId = _good.FirstOrDefault().UserIDString;
                    _.matchUpdateRequired = true;
                }
                else
                    EndMatch(Team.Evil);
            }

            private void AssignEvilHostId()
            {
                _evil.RemoveWhere(IsNotConnected);

                if (_evil.Count > 0)
                {
                    _evilHostId = _evil.FirstOrDefault().UserIDString;
                    _.matchUpdateRequired = true;
                }
                else
                    EndMatch(Team.Good);
            }

            private void Finalize(Team team)
            {
                Interface.CallHook("OnDuelistFinalized", team == Team.Good ? _goodKIA : _evilKIA);

                switch (team)
                {
                    case Team.Evil:
                        {
                            foreach (ulong playerId in _goodKIA)
                            {
                                _.UpdateMatchStats(playerId.ToString(), false, true, false, false);
                                _.UpdateMatchSizeStats(playerId.ToString(), true, false, _teamSize);
                            }

                            foreach (ulong playerId in _evilKIA)
                            {
                                _.AwardPlayer(playerId, _.teamEconomicsMoney, _.teamServerRewardsPoints);
                                _.UpdateMatchStats(playerId.ToString(), true, false, false, false);
                                _.UpdateMatchSizeStats(playerId.ToString(), false, true, _teamSize);
                            }

                            break;
                        }
                    case Team.Good:
                        {
                            foreach (ulong playerId in _evilKIA)
                            {
                                _.UpdateMatchStats(playerId.ToString(), false, true, false, false);
                                _.UpdateMatchSizeStats(playerId.ToString(), true, false, _teamSize);
                            }

                            foreach (ulong playerId in _goodKIA)
                            {
                                _.AwardPlayer(playerId, _.teamEconomicsMoney, _.teamServerRewardsPoints);
                                _.UpdateMatchStats(playerId.ToString(), true, false, false, false);
                                _.UpdateMatchSizeStats(playerId.ToString(), false, true, _teamSize);
                            }

                            break;
                        }
                }

                _goodKIA.Clear();
                _evilKIA.Clear();
            }

            private bool SetupRematch()
            {
                if (!CanRematch || _goodRematch.Exists(IsNotConnected) || _evilRematch.Exists(IsNotConnected))
                    return false;

                var rematch = new Rematch(_);

                if (rematch.AddRange(_evilRematch, Team.Evil) && rematch.AddRange(_goodRematch, Team.Good))
                {
                    rematch.match = this;
                    rematch.Notify();
                    _.rematches.Add(rematch);
                    return true;
                }

                return false;
            }

            private void EndMatch(Team team)
            {
                if (!_ended && _started)
                {
                    Finalize(team);
                    _.Puts(_.msg("MatchDefeat", null, team == Team.Evil ? _evilHostName : _goodHostName, team == Team.Evil ? _goodHostName : _evilHostName, _teamSize));
                    IsOver = true;
                    IsStarted = false;

                    foreach (var player in _evil.ToList())
                    {
                        RemoveMatchPlayer(player);

                        if (player != null && player.IsConnected && !_evilRematch.Contains(player))
                            _evilRematch.Add(player);
                    }

                    foreach (var player in _good.ToList())
                    {
                        RemoveMatchPlayer(player);

                        if (player != null && player.IsConnected && !_goodRematch.Contains(player))
                            _goodRematch.Add(player);
                    }

                    foreach (var target in BasePlayer.activePlayerList.Where(p => p?.displayName != null))
                    {
                        if (_.guiAnnounceUITime > 0f && (_goodKIA.Contains(target.userID) || _evilKIA.Contains(target.userID)))
                            _.CreateAnnouncementUI(target, _.msg("MatchDefeat", target.UserIDString, team == Team.Evil ? _evilHostName : _goodHostName, team == Team.Evil ? _goodHostName : _evilHostName, _teamSize));

                        if (_.duelsData.Chat.Contains(target.UserIDString) && !_goodKIA.Contains(target.userID) && !_evilKIA.Contains(target.userID))
                            continue;

                        Message(target, _.msg("MatchDefeat", target.UserIDString, team == Team.Evil ? _evilHostName : _goodHostName, team == Team.Evil ? _goodHostName : _evilHostName, _teamSize));
                    }

                    if (!SetupRematch())
                    {
                        var _list = new List<BasePlayer>();

                        _list.AddRange(_evilRematch);
                        _list.AddRange(_goodRematch);

                        foreach (var player in _list)
                        {
                            if (IsNotConnected(player)) continue;
                            Message(player, _.msg("RematchFailed2", player.UserIDString));
                        }

                        Reuse();
                    }
                }

                End();
            }

            public void End(bool forced = false)
            {
                if (_zone != null)
                    _zone.IsLocked = false;

                _queueTimer?.Destroy();
                _good.RemoveWhere(IsNull);
                _evil.RemoveWhere(IsNull);

                var _list = new List<BasePlayer>();

                _list.AddRange(_good);
                _list.AddRange(_evil);

                foreach (var player in _list)
                {
                    if (player.inventory.containerWear.HasFlag(ItemContainer.Flag.IsLocked))
                        player.inventory.containerWear.SetFlag(ItemContainer.Flag.IsLocked, false);

                    if (IsStarted || IsOver)
                    {
                        if (_.DuelTerritory(player.transform.position))
                        {
                            player.inventory.Strip();
                            _.SendHome(player);
                        }

                        _.Metabolize(player, false);
                        _.Track(player, false);
                    }
                }

                _good.Clear();
                _evil.Clear();
                _.tdmMatches.Remove(this);
                _.matchUpdateRequired = true;

                if (_.dataDuelists.Count == 0 && _.tdmMatches.Count == 0)
                    _.Unsubscribe(nameof(OnPlayerHealthChange));
            }

            private void Queue()
            {
                DuelingZone zone = null;

                var _list = new List<BasePlayer>();

                _list.AddRange(_good);
                _list.AddRange(_evil);

                foreach (var player in _list)
                {
                    if (player == null)
                        continue;

                    if (!_.IsNewman(player))
                    {
                        Message(player, _.msg("MustBeNaked", player.UserIDString));
                        MessageAll("MatchIsNotNaked", player.displayName);
                        _queueTimer = _.timer.Once(30f, Queue);
                        return;
                    }

                    if (zone == null)
                        zone = _.GetPlayerZone(player, TeamSize);
                }

                var zones = _.duelingZones.Where(x => x.TotalPlayers == 0 && !x.IsLocked && x.Spawns.Count >= (_.requireTeamSize ? TeamSize * 2 : 2)).ToList();

                if (zones == null || zones.Count == 0)
                {
                    if (!_enteredQueue)
                    {
                        MessageAll("MatchQueued");
                        _enteredQueue = true;
                    }

                    _queueTimer = _.timer.Once(2f, Queue);
                    return;
                }

                _zone = zone ?? LastZone ?? zones.GetRandom();
                _queueTimer?.Destroy();
                Start();
            }

            public DuelingZone LastZone
            {
                get
                {
                    DuelingZone zone = null;

                    if (_good.Exists(player => _.DuelTerritory(player.transform.position)))
                        zone = _.GetDuelZone(_good.FirstOrDefault(player => _.DuelTerritory(player.transform.position)).transform.position);

                    if (_evil.Exists(player => _.DuelTerritory(player.transform.position)))
                        zone = _.GetDuelZone(_evil.FirstOrDefault(player => _.DuelTerritory(player.transform.position)).transform.position);

                    return zone == null || zone.TotalPlayers > 0 || zone.IsLocked ? null : zone;
                }
            }

            private void Start()
            {
                _.SubscribeHooks(true);

                var goodSpawn = _zone.Spawns.GetRandom();
                var evilSpawn = goodSpawn;
                float dist = -100f;

                foreach (var spawn in _zone.Spawns) // get the furthest spawn point away from the good team and assign it to the evil team
                {
                    float distance = Vector3.Distance(spawn, goodSpawn);

                    if (distance > dist)
                    {
                        dist = distance;
                        evilSpawn = spawn;
                    }
                }

                _.Message(_good, "MatchStarted", GetNames(Team.Evil));
                _.Message(_evil, "MatchStarted", GetNames(Team.Good));
                _zone.IsLocked = true;
                IsStarted = true;

                Spawn(_good, goodSpawn);
                Spawn(_evil, evilSpawn);
            }

            private void Spawn(HashSet<BasePlayer> players, Vector3 spawn)
            {
                foreach (var player in players)
                {
                    _.duelsData.Kits[player.UserIDString] = _kit;

                    if (!_.DuelTerritory(player.transform.position) || !_.duelsData.Homes.ContainsKey(player.UserIDString))
                    {
                        var ppos = player.transform.position;
                        if (_.IsOnConstruction(ppos)) ppos.y += 1; // prevent player from becoming stuck or dying when teleported home
                        _.duelsData.Homes[player.UserIDString] = ppos.ToString();
                    }

                    _.RemoveFromQueue(player.UserIDString);
                    _.Teleport(player, spawn);

                    if (_.immunityTime >= 1)
                    {
                        _.dataImmunity[player.UserIDString] = TimeStamp() + _.immunityTime;
                        _.dataImmunitySpawns[player.UserIDString] = spawn;
                    }
                }
            }
        }

        public class DuelKitItem
        {
            public string ammo;
            public int amount;
            public string container;
            public List<string> mods;
            public string shortname;
            public ulong skin;
            public int slot;
        }

        public class BetInfo
        {
            public string trigger; // the trigger used to request this as a bet
            public int amount; // amount the player bet
            public int itemid; // the unique identifier of the item
            public int max; // the maximum amount allowed to bet on this item

            public bool Equals(BetInfo bet)
            {
                return bet.amount == amount && bet.itemid == itemid;
            }
        }

        public class DuelingZone : FacepunchBehaviour // Thanks @Jake_Rich for helping me get this started!
        {
            private Duelist _ => Instance;
            private HashSet<BasePlayer> _players = new HashSet<BasePlayer>();
            private HashSet<BasePlayer> _waiting = new HashSet<BasePlayer>();
            private Vector3 _zonePos;
            private List<Vector3> _duelSpawns = new List<Vector3>(); // spawn points generated on the fly
            private List<SphereEntity> spheres = new List<SphereEntity>();
            public bool IsLocked;
            public int Kills;

            public int TotalPlayers
            {
                get
                {
                    return _players.Count;
                }
            }

            public List<BasePlayer> Players
            {
                get
                {
                    return _players.ToList();
                }
            }

            public List<Vector3> Spawns
            {
                get
                {
                    var spawns = _.GetSpawnPoints(this); // get custom spawn points if any exist

                    return spawns == null || spawns.Count < 2 ? _duelSpawns : spawns;
                }
            }

            public bool IsFull
            {
                get
                {
                    return TotalPlayers + _waiting.Count + 2 > _.playersPerZone || IsLocked;
                }
            }

            public Vector3 Position
            {
                get
                {
                    return _zonePos;
                }
            }

            private void OnDestroy()
            {
                Destroy(gameObject);
                Destroy(this);
            }

            private void OnTriggerEnter(Collider col)
            {
                if (col == null) return;
                var entity = col.ToBaseEntity();

                if (entity is BaseMountable)
                {
                    var m = entity as BaseMountable;

                    if (m.GetParentEntity() is TrainCar)
                    {
                        return;
                    }

                    RemoveMountable(m, GetMountedPlayers(m));
                }
                else if (entity is BasePlayer)
                {
                    var p = entity as BasePlayer;

                    if (_.InEvent(p) || _.IsSpectator(p))
                    {
                        return;
                    }

                    RemovePlayer(p);
                }
            }


            public Vector3 GetEjectLocation(Vector3 a, float distance)
            {
                var position = ((a.XZ3D() - _zonePos.XZ3D()).normalized * (_.zoneRadius + distance)) + _zonePos; // credits ZoneManager
                float y = TerrainMeta.HighestPoint.y + 250f;

                RaycastHit hit;
                if (Physics.Raycast(position + new Vector3(0f, y, 0f), Vector3.down, out hit, Mathf.Infinity, targetLayer, QueryTriggerInteraction.Ignore))
                {
                    position.y = hit.point.y + 0.75f;
                }
                else position.y = Mathf.Max(TerrainMeta.HeightMap.GetHeight(position), TerrainMeta.WaterMap.GetHeight(position)) + 0.75f;

                return position;
            }

            public bool RemovePlayer(BasePlayer player)
            {
                if (!player.IsHuman() || player.IsAdmin)
                {
                    return false;
                }

                var m = player.GetMounted();

                if (m.IsValid())
                {
                    var players = GetMountedPlayers(m);

                    players.RemoveAll(x => x == null || !x.IsHuman());

                    if (RemoveMountable(m, players))
                    {
                        return true;
                    }
                }

                var position = GetEjectLocation(player.transform.position, 10f);

                if (player.IsFlying)
                {
                    position.y = player.transform.position.y;
                }

                player.Teleport(position);
                player.SendNetworkUpdateImmediate();

                return true;
            }

            public void DismountAllPlayers(BaseMountable m)
            {
                foreach (var target in GetMountedPlayers(m))
                {
                    if (target == null) continue;

                    m.DismountPlayer(target, false);

                    target.EnsureDismounted();
                }
            }

            private List<BasePlayer> GetMountedPlayers(BaseMountable m)
            {
                BaseVehicle vehicle = m.HasParent() ? m.VehicleParent() : m as BaseVehicle;

                if (vehicle.IsValid())
                {
                    return GetMountedPlayers(vehicle);
                }

                List<BasePlayer> players = new List<BasePlayer>();

                var player = m.GetMounted();

                if (player.IsValid() && player.IsHuman())
                {
                    players.Add(player);
                }

                return players;
            }

            private List<BasePlayer> GetMountedPlayers(BaseVehicle vehicle)
            {
                List<BasePlayer> players = new List<BasePlayer>();

                if (!vehicle.HasMountPoints())
                {
                    var player = vehicle.GetMounted();

                    if (player.IsValid() && player.IsHuman())
                    {
                        players.Add(player);
                    }

                    return players;
                }

                for (int i = 0; i < vehicle.mountPoints.Count; i++)
                {
                    var mountPoint = vehicle.mountPoints[i];

                    if (mountPoint.mountable == null)
                    {
                        continue;
                    }

                    var player = mountPoint.mountable.GetMounted();

                    if (player.IsValid() && player.IsHuman())
                    {
                        players.Add(player);
                    }
                }

                return players;
            }

            private bool RemoveMountable(BaseMountable m, List<BasePlayer> players)
            {
                if (players.Count == 0)
                {
                    return EjectMountable(m, 10f, players);
                }

                BaseVehicle vehicle = m.HasParent() ? m.VehicleParent() : m as BaseVehicle;

                if (!vehicle.IsKilled())
                {
                    var e = vehicle.transform.eulerAngles; // credits k1lly0u

                    vehicle.transform.rotation = Quaternion.Euler(e.x, e.y - 180f, e.z);

                    if (vehicle.rigidBody != null)
                    {
                        vehicle.rigidBody.velocity *= -1f;
                    }

                    return true;
                }

                return EjectMountable(m, 2f, players);
            }

            private bool IsFlying(BasePlayer player)
            {
                return player?.modelState?.onground == false && TerrainMeta.HeightMap.GetHeight(player.transform.position) < player.transform.position.y - 1f;
            }

            private bool EjectMountable(BaseMountable m, float distance, List<BasePlayer> players)
            {
                if (m is BaseVehicle)
                {
                    var vehicle = m as BaseVehicle;

                    foreach (var mp in vehicle.mountPoints)
                    {
                        if (mp.mountable.IsValid() && mp.mountable.GetMounted().IsValid())
                        {
                            if (mp.mountable.GetMounted().IsAdmin)
                            {
                                return false;
                            }
                        }
                    }
                }
                else if (m.GetMounted().IsValid())
                {
                    if (m.GetMounted().IsAdmin)
                    {
                        return false;
                    }
                }

                var j = TerrainMeta.HeightMap.GetHeight(m.transform.position) - m.transform.position.y;

                if (j > 5f)
                {
                    distance += j;
                }

                var position = ((m.transform.position.XZ3D() - _zonePos.XZ3D()).normalized * (_.zoneRadius + distance)) + _zonePos;
                var e = m.transform.eulerAngles;

                if (m is Minicopter || m is CH47Helicopter || players.Exists(player => IsFlying(player)))
                {
                    position.y = Mathf.Max(m.transform.position.y + 5f, GetSpawnHeight(position) + 1f);
                }
                else
                {
                    position.y = GetSpawnHeight(position) + 1f;
                }

                m.transform.rotation = Quaternion.Euler(e.x, e.y - 180f, e.z);

                Rigidbody rigidbody;
                if (m.TryGetComponent(out rigidbody))
                {
                    rigidbody.velocity *= -1f;
                }

                if (m.mountAnchor != null && m.mountAnchor.transform != null)
                {
                    m.transform.position = m.mountAnchor.transform.position = position;
                    m.mountAnchor.Rotate(m.transform.eulerAngles);
                }
                else m.transform.position = position;

                m.TransformChanged();

                return true;
            }

            private int targetLayer = ~(Layers.Mask.Invisible | Layers.Mask.Trigger | Layers.Mask.Prevent_Movement | Layers.Mask.Prevent_Building); // credits ZoneManager

            private static float GetSpawnHeight(Vector3 target)
            {
                float y = TerrainMeta.HeightMap.GetHeight(target);
                float w = TerrainMeta.WaterMap.GetHeight(target);
                float p = TerrainMeta.HighestPoint.y + 250f;
                RaycastHit hit;

                if (Physics.Raycast(new Vector3(target.x, w, target.z), Vector3.up, out hit, p, Layers.Mask.World))
                {
                    y = Mathf.Max(y, hit.point.y);

                    if (Physics.Raycast(new Vector3(target.x, hit.point.y + 0.5f, target.z), Vector3.up, out hit, p, Layers.Mask.World))
                    {
                        y = Mathf.Max(y, hit.point.y);
                    }
                }

                return Mathf.Max(y, w);
            }

            public void Setup(Vector3 position)
            {
                transform.position = _zonePos = position;
                _duelSpawns = _.GetAutoSpawns(this);

                if (_.removePlayers)
                {
                    var collider = gameObject.GetComponent<SphereCollider>() ?? gameObject.AddComponent<SphereCollider>();
                    collider.radius = _.zoneRadius + 1.5f;
                    collider.isTrigger = true;
                    collider.center = Vector3.zero;
                    gameObject.layer = (int)Layer.Trigger;
                }

                if (_.autoOvens || _.autoFlames || _.autoTurrets)
                {
                    var entities = new List<BaseCombatEntity>();
                    Vis.Entities(_zonePos, _.zoneRadius, entities);

                    foreach (var e in entities)
                    {
                        _.SetupPower(e);
                    }
                }

                CreateSpheres();
            }

            private void RemoveSpheres()
            {
                spheres.ForEach(sphere => sphere.SafelyKill());
            }

            private void CreateSpheres()
            {
                if (_.sphereAmount <= 0)
                {
                    return;
                }

                var prefab = StringPool.Get(3211242734);

                for (int i = 0; i < _.sphereAmount; i++)
                {
                    var sphere = GameManager.server.CreateEntity(prefab, _zonePos) as SphereEntity;

                    if (sphere == null)
                    {
                        break;
                    }

                    sphere.currentRadius = 1f;
                    sphere.Spawn();
                    sphere.LerpRadiusTo(_.zoneRadius * 2f, _.zoneRadius * 0.75f);
                    spheres.Add(sphere);
                }
            }

            public float Distance(Vector3 position)
            {
                position.y = 0f;
                return Vector3.Distance(new Vector3(_zonePos.x, 0f, _zonePos.z), position);
            }

            public bool? AddWaiting(BasePlayer player, BasePlayer target)
            {
                if (IsFull)
                    return false;

                double requiredDuelMoney = _.requiredDuelMoney;

                if (requiredDuelMoney > 0.0 && _.Economics.CanCall())
                {
                    double playerMoney = Convert.ToDouble(_.Economics.Call("Balance", player.userID));
                    double targetMoney = Convert.ToDouble(_.Economics.Call("Balance", target.userID));

                    if (playerMoney < _.requiredDuelMoney || targetMoney < requiredDuelMoney)
                    {
                        _.RemoveFromQueue(player.UserIDString);
                        _.RemoveFromQueue(target.UserIDString);
                        Message(player, _.msg("MoneyRequired", player.UserIDString, requiredDuelMoney));
                        Message(target, _.msg("MoneyRequired", target.UserIDString, requiredDuelMoney));
                        return null;
                    }

                    bool playerWithdrawn = Convert.ToBoolean(_.Economics.Call("Withdraw", player.userID, requiredDuelMoney));
                    bool targetWithdrawn = Convert.ToBoolean(_.Economics.Call("Withdraw", target.userID, requiredDuelMoney));

                    if (!playerWithdrawn || !targetWithdrawn)
                    {
                        _.RemoveFromQueue(player.UserIDString);
                        _.RemoveFromQueue(target.UserIDString);
                        return null;
                    }
                }

                _waiting.Add(player);
                _waiting.Add(target);

                return true;
            }

            public bool IsWaiting(BasePlayer player)
            {
                return _waiting.Contains(player);
            }

            public void AddPlayer(BasePlayer player)
            {
                _waiting.Remove(player);
                _players.Add(player);
            }

            public void RemovePlayer(string playerId)
            {
                _players.RemoveWhere(IsNull);

                foreach (var player in _players)
                {
                    if (player.UserIDString == playerId)
                    {
                        _players.Remove(player);
                        _waiting.Remove(player);
                        break;
                    }
                }
            }

            public bool HasPlayer(string playerId)
            {
                return _players.Exists(player => player.UserIDString == playerId);
            }

            public void Kill()
            {
                foreach (var player in _players.ToList())
                    _.EjectPlayer(player);

                foreach (var player in BasePlayer.allPlayerList)
                {
                    if (Distance(player.transform.position) <= _.zoneRadius)
                    {
                        _.EndSpectate(player);
                        _.SendHome(player);
                    }
                }

                _.duelingZones.Remove(this);
                _players.Clear();
                RemoveSpheres();
                Destroy(gameObject);
                Destroy(this);
            }
        }

        private object OnDangerousOpen(Vector3 treasurePos)
        {
            return DuelTerritory(treasurePos) ? (object)false : null;
        }

        private object OnPlayerDeathMessage(BasePlayer victim, HitInfo info) // private plugin hook
        {
            return DuelTerritory(victim.transform.position) ? (object)false : null;
        }

        private void Init()
        {
            Instance = this;
            SubscribeHooks(false); // turn off all hooks immediately
        }

        private void OnServerInitialized()
        {
            LoadVariables();
            SetupDefinitions();

            monuments = UnityEngine.Object.FindObjectsOfType<MonumentInfo>().Select(monument => monument.transform.position).ToList();
            duelsFile = Interface.Oxide.DataFileSystem.GetFile(Name);

            try
            {
                duelsData = duelsFile.ReadObject<StoredData>();
            }
            catch { }

            if (duelsData == null)
                duelsData = new StoredData();

            foreach (var bet in duelingBets.ToList()) // 0.1.5 fix - check itemList after server has initialized
            {
                if (ItemManager.itemList.Find(def => def.itemid == bet.itemid) == null)
                {
                    Puts("Bet itemid {0} is invalid.", bet.itemid);
                    duelingBets.Remove(bet);
                }
            }

            if (useAnnouncement && announceTime > 0f)
                announceTimer = timer.Repeat(announceTime, 0, () => DuelAnnouncement(false));

            eventTimer = timer.Once(0.5f, CheckDuelistMortality); // kill players who haven't finished their duel in time. remove temporary immunity for duelers when it expires

            if (!resetDuelists && BuildingManager.server.buildingDictionary.Count == 0)
            {
                if (duelsData.VictoriesSeed.Count > 0 && duelsData.VictoriesSeed.Values.Exists(x => x > 0))
                {
                    resetDuelists = true;
                }
            }

            if (resetDuelists) // map wipe detected - award duelers and reset the data for the seed only
            {
                ResetDuelists();
                resetDuelists = false;
            }

            if (BasePlayer.activePlayerList.Count == 0)
            {
                RemoveZeroStats();
                ResetTemporaryData();
            }

            if (ZoneManager.CanCall())
                SetupZoneManager();

            SetupZones();

            if (duelingZones.Count > 0 && autoEnable)
                duelsData.DuelsEnabled = true;

            UpdateStability();
            CheckZoneHooks(true);

            if (guiAutoEnable)
            {
                Subscribe(nameof(OnPlayerConnected));

                foreach (var player in BasePlayer.activePlayerList)
                    OnPlayerConnected(player);
            }

            if (useWorkshopSkins)
                webrequest.Enqueue("http://s3.amazonaws.com/s3.playrust.com/icons/inventory/rust/schema.json", null, GetWorkshopIDs, this, Core.Libraries.RequestMethod.GET);
        }

        private void OnServerSave()
        {
            timer.Once(5f, SaveData);
        }

        private void OnNewSave(string filename)
        {
            resetDuelists = true;
        }

        public void SaveData()
        {
            if (duelsFile != null && duelsData != null)
            {
                duelsFile.WriteObject(duelsData);
            }
        }

        private void DestroyAll()
        {
            foreach (var zone in duelingZones.ToList())
            {
                UnityEngine.Object.Destroy(zone.gameObject);
            }
        }

        private void Unload()
        {
            var objects = UnityEngine.Object.FindObjectsOfType(typeof(Tracker));

            if (objects != null)
                foreach (var gameObj in objects)
                    UnityEngine.Object.Destroy(gameObj);

            DestroyAll();
            announceTimer?.Destroy();
            eventTimer?.Destroy();

            foreach (var match in tdmMatches.ToList())
                match.End();

            foreach (var zone in duelingZones.ToList())
            {
                RemoveEntities(zone);
                zone.Kill();
            }

            tdmMatches.Clear();
            duelingZones.Clear();
            ResetTemporaryData();
            DestroyAllUI();
            Instance = null;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            var match = GetMatch(player);

            if (match != null && !match.IsStarted && match.EitherEmpty)
                match.End();

            if (dataDuelists.ContainsKey(player.UserIDString))
            {
                string uid = player.UserIDString;

                if (!dcsBlock.Contains(uid))
                {
                    dcsBlock.Add(uid);
                    timer.Once(60f, () => dcsBlock.Remove(uid));
                }

                duelsData.AutoReady.Remove(player.UserIDString);
                OnDuelistLost(player, true);
                RemoveDuelist(player.UserIDString);
                ResetDuelist(player.UserIDString, false);
            }
            else if (match != null && match.IsStarted && !match.IsOver)
            {
                string uid = player.UserIDString;

                if (!dcsBlock.Contains(uid))
                {
                    dcsBlock.Add(uid);
                    timer.Once(60f, () => dcsBlock.Remove(uid));
                }

                duelsData.AutoReady.Remove(player.UserIDString);
                player.inventory.Strip();
                DefeatMessage(player, match);
                match.CanRematch = false;
                match.RemoveMatchPlayer(player);
            }
            else if (IsSpectator(player))
            {
                EndSpectate(player);
                SendHome(player);
            }

            if (dataDuelists.Count == 0 && tdmMatches.Count == 0 && spectators.Count == 0)
                Unsubscribe(nameof(OnPlayerDisconnected)); // nothing else to do right now, unsubscribe the hook
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!player.IsValid())
                return;

            if (!player.CanInteract())
            {
                timer.Once(1f, () => OnPlayerConnected(player));
                return;
            }

            createUI.Remove(player.UserIDString);
            cmdDUI(player, szUIChatCommand, new string[0]);
        }

        private List<ulong> executes = new List<ulong>();

        private void OnPlayerSleepEnded(BasePlayer player) // setup the player
        {
            if (IsDueling(player))
            {
                if (IsExploiting(player, true))
                {
                    return;
                }

                foreach (var zone in duelingZones)
                {
                    if (zone.IsWaiting(player))
                    {
                        if (deathTime > 0)
                        {
                            if (!executes.Contains(player.userID))
                            {
                                Message(player, msg("ExecutionTime", player.UserIDString, deathTime));
                                executes.Add(player.userID);
                            }

                            dataDeath[player.UserIDString] = TimeStamp() + deathTime * 60;
                        }

                        EndSpectate(player);
                        GivePlayerKit(player);
                        Track(player, true);
                        Metabolize(player, true);

                        if (DestroyUI(player) && !createUI.Contains(player.UserIDString))
                            createUI.Add(player.UserIDString);

                        CheckAutoReady(player);
                        zone.AddPlayer(player);
                        Interface.Oxide.CallHook("EnableBypass", player.userID);
                    }
                }

                return;
            }
            else if (InDeathmatch(player))
            {
                if (IsExploiting(player, false))
                {
                    return;
                }

                var match = GetMatch(player);

                if (deathTime > 0)
                {
                    Message(player, msg("ExecutionTime", player.UserIDString, deathTime));
                    dataDeath[player.UserIDString] = TimeStamp() + deathTime * 60;
                }

                if (DestroyUI(player) && !createUI.Contains(player.UserIDString))
                {
                    createUI.Add(player.UserIDString);
                }

                EndSpectate(player);
                GivePlayerKit(player);
                Track(player, true);
                Metabolize(player, true);
                match.GiveShirt(player);
                CheckAutoReady(player);
                Interface.Oxide.CallHook("EnableBypass", player.userID);
                return;
            }
            else SetPlayerTime(player, false);

            if (announcements.ContainsKey(player.UserIDString))
            {
                CreateAnnouncementUI(player, announcements[player.UserIDString]);
                announcements.Remove(player.UserIDString);
            }

            if (dataDuelists.Count == 0 && tdmMatches.Count == 0 && announcements.Count == 0)
            {
                // nothing else to do right now, unsubscribe the hook
                Unsubscribe(nameof(OnPlayerSleepEnded));
            }
        }

        private bool IsExploiting(BasePlayer player, bool duel)
        {
            if (AimTrain.CanCall() && Convert.ToBoolean(AimTrain?.Call("IsAimTraining", player.userID)))
            {
                AimTrain?.Call("LeaveAimTrain", player);
                duelsData.AutoReady.Remove(player.UserIDString);
                DestroyUI(player);

                if (duel)
                {
                    OnDuelistLost(player, true);
                    RemoveDuelist(player.UserIDString);
                    ResetDuelist(player.UserIDString, false);
                    EndSpectate(player);
                    SendHome(player);
                }
                else
                {
                    var match = GetMatch(player);

                    if (match != null && !match.IsStarted && match.EitherEmpty)
                    {
                        match.End();
                    }

                    if (match != null && match.IsStarted && !match.IsOver)
                    {
                        DefeatMessage(player, match);
                        match.CanRematch = false;
                        match.RemoveMatchPlayer(player);
                    }
                }

                return true;
            }

            return false;
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (DuelTerritory(player.transform.position) && !InEvent(player) && !spectators.Contains(player.UserIDString))
            {
                var spawnPoint = ServerMgr.FindSpawnPoint();
                int retries = 25;

                while (DuelTerritory(spawnPoint.pos) && --retries > 0)
                    spawnPoint = ServerMgr.FindSpawnPoint();

                Teleport(player, spawnPoint.pos);
            }
        }

        private void OnEntityKill(SimpleBuildingBlock e)
        {
            if (respawnWalls)
            {
                if (e?.transform != null && e.ShortPrefabName.Contains("wall.external.high"))
                {
                    RecreateZoneWall(e.PrefabName, e.transform.position, e.transform.rotation, e.OwnerID);
                }
            }
        }

        public void SetupPower(BaseEntity e)
        {
            if (autoOvens && e is BaseOven)
            {
                e.SetFlag(BaseEntity.Flags.On, true);
            }
            else if (autoFlames && e is FlameTurret)
            {
                var ft = e as FlameTurret;

                if (!ft.HasFuel())
                {
                    ft.inventory.AddItem(ItemManager.FindItemDefinition("lowgradefuel"), 5);
                }

                ft.fuelPerSec = 0f;
            }
            else if (autoTurrets && e is AutoTurret)
            {
                var at = e as AutoTurret;

                at.InitiateStartup();
                at.SetPeacekeepermode(false);
            }
        }

        public void Track(BasePlayer player, bool enable)
        {
            Tracker tracker;
            if (enable && !player.TryGetComponent(out tracker))
            {
                tracker = player.gameObject.AddComponent<Tracker>();
                tracker.Init(this);
            }
            else if (!enable && player.TryGetComponent(out tracker))
            {
                UnityEngine.Object.Destroy(tracker);
            }
        }

        public void RecreateZoneWall(string prefab, Vector3 pos, Quaternion rot, ulong ownerId)
        {
            if (DuelTerritory(pos) && duelsData.DuelZones.Exists(entry => GetOwnerId(entry.Key) == ownerId))
                CreateZoneWall(prefab, pos, rot, ownerId);
        }

        public BaseEntity CreateZoneWall(string prefab, Vector3 pos, Quaternion rot, ulong ownerId)
        {
            var e = GameManager.server.CreateEntity(prefab, pos, rot, false);

            if (e != null)
            {
                e.OwnerID = ownerId;
                e.Spawn();
                e.gameObject.SetActive(true);
                return e;
            }

            return null;
        }

        private void OnEntityDeath(BaseEntity entity, HitInfo hitInfo) // 0.1.16 fix for player suiciding
        {
            if (entity == null)
                return;

            if (respawnWalls && entity.transform != null && entity.ShortPrefabName.Contains("wall.external.high"))
            {
                RecreateZoneWall(entity.PrefabName, entity.transform.position, entity.transform.rotation, entity.OwnerID);
                return;
            }

            var victim = entity as BasePlayer;

            if (victim == null)
                return;

            if (spectators.Contains(victim.UserIDString))
                EndSpectate(victim);

            if (IsDueling(victim))
            {
                victim.inventory.Strip();
                OnDuelistLost(victim, true);
            }
            else if (InDeathmatch(victim))
            {
                victim.inventory.Strip();
                var match = GetMatch(victim);

                DefeatMessage(victim, match);
                match.RemoveMatchPlayer(victim);
            }
        }

        private void OnPlayerHealthChange(BasePlayer player, float oldValue, float newValue)
        {
            if (newValue < 6f && player.IsHuman())
            {
                if (IsDueling(player))
                {
                    player.health = 6f;
                    player.inventory.Strip();
                    OnDuelistLost(player, false);
                }
                else if (InDeathmatch(player))
                {
                    player.health = 6f;
                    player.inventory.Strip();

                    var match = GetMatch(player);
                    DefeatMessage(player, match);
                    match.RemoveMatchPlayer(player);
                }
            }
        }

        private void DefeatMessage(BasePlayer victim, GoodVersusEvilMatch match)
        {
            if (tdmAttackers.ContainsKey(victim.UserIDString))
            {
                var info = tdmAttackers[victim.UserIDString];

                if (tdmServerDeaths || duelsData.ChatEx.Count > 0)
                {
                    foreach (var target in BasePlayer.activePlayerList.Where(p => p?.displayName != null))
                    {
                        if (duelsData.Chat.Contains(target.UserIDString) && target != victim)
                            continue;

                        Message(target, msg("MatchPlayerDefeated", target.UserIDString, victim.displayName, info.AttackerName, info.Weapon, info.BoneName, info.Distance));
                    }
                }
                else if (tdmMatchDeaths)
                    match.MessageAll("MatchPlayerDefeated", victim.displayName, info.AttackerName, info.Weapon, info.BoneName, info.Distance);

                if (guiAnnounceUITime > 0f)
                {
                    if (sendDefeatedHome)
                        announcements[victim.UserIDString] = msg("MatchPlayerDefeated", victim.UserIDString, victim.displayName, info.AttackerName, info.Weapon, info.BoneName, info.Distance);
                    else
                        CreateAnnouncementUI(victim, msg("MatchPlayerDefeated", victim.UserIDString, victim.displayName, info.AttackerName, info.Weapon, info.BoneName, info.Distance));
                }

                tdmAttackers.Remove(victim.UserIDString);
                UpdateMatchStats(victim.UserIDString, false, false, true, false);
                UpdateMatchStats(info.AttackerId, false, false, false, true);
            }
        }

        private void OnDuelistLost(BasePlayer victim, bool sendHome)
        {
            RemoveEntities(victim.userID);

            if (!dataDuelists.ContainsKey(victim.UserIDString))
            {
                NextTick(() => SendHome(victim));
                return;
            }

            string attackerId = dataDuelists[victim.UserIDString];
            var attacker = BasePlayer.Find(attackerId);
            string attackerName = attacker?.displayName ?? GetDisplayName(attackerId); // get the attackers name. null check for self inflicted

            dataDeath.Remove(victim.UserIDString); // remove them from automatic deaths
            dataDeath.Remove(attackerId);
            dataDuelists.Remove(victim.UserIDString); // unset their status as duelers
            dataDuelists.Remove(attackerId);
            victim.inventory.Strip();
            Metabolize(victim, false);
            Track(victim, false);

            if (!duelsData.LossesSeed.ContainsKey(victim.UserIDString)) duelsData.LossesSeed.Add(victim.UserIDString, 1);
            else duelsData.LossesSeed[victim.UserIDString]++;
            if (!duelsData.Losses.ContainsKey(victim.UserIDString)) duelsData.Losses.Add(victim.UserIDString, 1);
            else duelsData.Losses[victim.UserIDString]++;
            if (!duelsData.VictoriesSeed.ContainsKey(attackerId)) duelsData.VictoriesSeed.Add(attackerId, 1);
            else duelsData.VictoriesSeed[attackerId]++;
            if (!duelsData.Victories.ContainsKey(attackerId)) duelsData.Victories.Add(attackerId, 1);
            else duelsData.Victories[attackerId]++;
            duelsData.TotalDuels++;

            int victimLossesSeed = duelsData.LossesSeed[victim.UserIDString];
            int victimVictoriesSeed = duelsData.VictoriesSeed.ContainsKey(victim.UserIDString) ? duelsData.VictoriesSeed[victim.UserIDString] : 0;
            int attackerLossesSeed = duelsData.LossesSeed.ContainsKey(attackerId) ? duelsData.LossesSeed[attackerId] : 0;
            int attackerVictoriesSeed = duelsData.VictoriesSeed[attackerId];
            var bet = duelsData.Bets.ContainsKey(attackerId) && duelsData.Bets.ContainsKey(victim.UserIDString) && duelsData.Bets[attackerId].Equals(duelsData.Bets[victim.UserIDString]) && !IsAllied(victim, attacker) ? duelsData.Bets[attackerId] : null; // victim bet his attacker and lost, use later to add a claim for the attacker

            Puts(RemoveFormatting(msg("DuelDeathMessage", null, attackerName, attackerVictoriesSeed, attackerLossesSeed, victim.displayName, victimVictoriesSeed, victimLossesSeed, Math.Round(attacker?.health ?? 0f, 2), bet != null ? msg("BetWon", null, bet.trigger, bet.amount) : ""))); // send message to console
            Interface.CallHook("OnDuelistDefeated", attacker, victim);

            if (guiAnnounceUITime > 0f)
            {
                if (sendDefeatedHome)
                {
                    announcements[victim.UserIDString] = msg("DuelDeathMessage", victim.UserIDString, attackerName, attackerVictoriesSeed, attackerLossesSeed, victim.displayName, victimVictoriesSeed, victimLossesSeed, Math.Round(attacker?.health ?? 0f, 2), bet != null ? msg("BetWon", null, bet.trigger, bet.amount) : "");
                    announcements[attackerId] = msg("DuelDeathMessage", attackerId, attackerName, attackerVictoriesSeed, attackerLossesSeed, victim.displayName, victimVictoriesSeed, victimLossesSeed, Math.Round(attacker?.health ?? 0f, 2), bet != null ? msg("BetWon", null, bet.trigger, bet.amount) : "");
                }
                else
                {
                    CreateAnnouncementUI(victim, msg("DuelDeathMessage", victim.UserIDString, attackerName, attackerVictoriesSeed, attackerLossesSeed, victim.displayName, victimVictoriesSeed, victimLossesSeed, Math.Round(attacker?.health ?? 0f, 2), bet != null ? msg("BetWon", null, bet.trigger, bet.amount) : ""));
                    CreateAnnouncementUI(attacker, msg("DuelDeathMessage", attackerId, attackerName, attackerVictoriesSeed, attackerLossesSeed, victim.displayName, victimVictoriesSeed, victimLossesSeed, Math.Round(attacker?.health ?? 0f, 2), bet != null ? msg("BetWon", null, bet.trigger, bet.amount) : ""));
                }
            }

            foreach (var target in BasePlayer.activePlayerList.Where(p => p?.displayName != null))
            {
                if (duelsData.Chat.Contains(target.UserIDString) && target != victim && target != attacker)
                    continue;

                if (!broadcastDefeat && !duelsData.ChatEx.Contains(target.UserIDString) && target != victim && target != attacker)
                    continue;

                string betWon = bet != null ? msg("BetWon", target.UserIDString, bet.trigger, bet.amount) : "";
                Message(target, msg("DuelDeathMessage", target.UserIDString, attackerName, attackerVictoriesSeed, attackerLossesSeed, victim.displayName, victimVictoriesSeed, victimLossesSeed, Math.Round(attacker?.health ?? 0f, 2), betWon));
            }

            if (bet != null && attacker != null) // award the bet to the attacker
            {
                var claimBet = new BetInfo
                {
                    itemid = bet.itemid,
                    amount = bet.amount * 2,
                    trigger = bet.trigger
                };

                if (!duelsData.ClaimBets.ContainsKey(attackerId))
                    duelsData.ClaimBets.Add(attackerId, new List<BetInfo>());

                duelsData.ClaimBets[attackerId].Add(claimBet);
                duelsData.Bets.Remove(attackerId);
                duelsData.Bets.Remove(victim.UserIDString);
                Puts(msg("ConsoleBetWon", null, attacker.displayName, attacker.UserIDString, victim.displayName, victim.UserIDString));
                Message(attacker, msg("NotifyBetWon", attacker.UserIDString, szDuelChatCommand));
            }

            ulong attackeruId = Convert.ToUInt64(attackerId);

            RemoveDuelist(attackerId);
            RemoveEntities(attackeruId);
            AwardPlayer(attackeruId, economicsMoney, serverRewardsPoints);

            Interface.Oxide.CallHook("DisableBypass", victim.userID);
            Interface.Oxide.CallHook("DisableBypass", attackeruId);

            if (attacker != null)
            {
                attacker.inventory.Strip();
                Metabolize(attacker, false);
                Track(attacker, false);
            }

            var zone = RemoveDuelist(victim.UserIDString);

            if (zoneCounter > 0 && zone != null) // if new zones are set to spawn every X duels then increment by 1
            {
                if (++zone.Kills >= zoneCounter && zone.TotalPlayers == 0)
                {
                    RemoveDuelZone(zone);
                    SetupDuelZone(null, GetZoneName()); // x amount of duels completed. time to relocate and start all over! changing the dueling zones location keeps things mixed up and entertaining for everyone. especially when there's issues with terrain
                    SaveData();
                }
            }

            if (dataDuelists.Count == 0 && tdmMatches.Count == 0)
                Unsubscribe(nameof(OnPlayerHealthChange));

            if (sendHome || dcsBlock.Contains(victim.UserIDString))
            {
                NextTick(() =>
                {
                    SendHome(attacker);
                    SendHome(victim);
                });

                if (dcsBlock.Contains(victim.UserIDString))
                    return;
            }

            if (attacker != null)
            {
                if (victim.IsConnected && attacker.IsConnected)
                {
                    var rematch = new Rematch(this);
                    rematches.Add(rematch);
                    rematch.Duelists.Add(attacker);
                    rematch.Duelists.Add(victim);
                    rematch.Notify();
                }

                if (!InEvent(attacker) && !InEvent(victim) && !sendHome)
                {
                    StartSpectate(attacker);
                    StartSpectate(victim);
                }
            }
        }

        public string GetZoneName()
        {
            return (duelsData.DuelZones.Count + 1).ToString();
        }

        public void SendDuelistsHome()
        {
            foreach (var entry in dataDuelists.ToList())
            {
                if (duelsData.Homes.ContainsKey(entry.Key))
                {
                    var target = BasePlayer.Find(entry.Key);

                    if (target != null && DuelTerritory(target.transform.position))
                    {
                        target.inventory.Strip();
                        SendHome(target);
                    }
                }

                ResetDuelist(entry.Key);
            }
        }

        public void SendSpectatorsHome()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (IsSpectator(player))
                {
                    EndSpectate(player);
                }
            }
        }

        private void StartSpectate(BasePlayer player)
        {
            if (IsNotConnected(player))
                return;

            if (GetDuelZone(player.transform.position) == null)
            {
                SendHome(player);
                return;
            }

            if (!player.CanInteract())
            {
                if (player.IsDead())
                    player.RespawnAt(player.transform.position, default(Quaternion));

                timer.Once(1f, () => StartSpectate(player));
                return;
            }

            spectators.Add(player.UserIDString);
            Message(player, msg("BeginSpectating", player.UserIDString));
            player.inventory.Strip();
            player.health = 100f;
            player.metabolism.bleeding.value = 0f;
            player.StopWounded();
            CreateDefeatUI(player);
        }

        private void EndSpectate(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "DuelistUI_Defeat");

            if (spectators.Contains(player.UserIDString))
            {
                if (playerHealth > 0f && player.IsAlive())
                    player.health = playerHealth;

                spectators.Remove(player.UserIDString);
                player.SendNetworkUpdate();
                Message(player, msg("EndSpectating", player.UserIDString));
            }
        }

        private static bool IsNull(BaseNetworkable a) => a == null || a.IsDestroyed;

        private static bool IsNotConnected(BasePlayer a) => a == null || a.IsDestroyed || !a.IsConnected;

        public void HealDamage(BaseCombatEntity entity)
        {
            timer.Once(1f, () =>
            {
                if (!entity.IsKilled() && entity.health < entity.MaxHealth())
                {
                    entity.health = entity.MaxHealth();
                    entity.SendNetworkUpdate();
                }
            });
        }

        public void CancelDamage(HitInfo hitInfo)
        {
            if (hitInfo != null)
            {
                hitInfo.damageTypes = new DamageTypeList();
                hitInfo.DidHit = false;
                hitInfo.HitEntity = null;
                hitInfo.Initiator = null;
                hitInfo.DoHitEffects = false;
                hitInfo.HitMaterial = 0;
            }
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity.IsKilled() || entity.net == null || entity is BaseNpc)
                return null;

            if (DuelTerritory(entity.transform.position, 1f))
            {
                if (entity is BuildingBlock || entity.name.Contains("deploy") || entity.name.Contains("wall.external.high") || entity.name.Contains("building"))
                {
                    CancelDamage(hitInfo);
                    HealDamage(entity);
                    return true;
                }
            }

            if (hitInfo == null || hitInfo.Initiator.IsKilled() || !hitInfo.hasDamage)
                return null;

            var victim = entity as BasePlayer;
            var attacker = hitInfo.Initiator as BasePlayer;
            var pointStart = hitInfo.Initiator?.transform?.position ?? hitInfo.PointStart; // 0.1.6 border fix
            var pointEnd = entity.transform.position;
            bool adt = DuelTerritory(pointStart);
            bool vdt = DuelTerritory(pointEnd);

            if (adt && entity is PatrolHelicopter)
                return true;

            if (vdt && victim?.transform != null && hitInfo.Initiator != null && hitInfo.Initiator.ShortPrefabName.Contains("wall.external.high")) // 1.0.2 - exploit fix
                return true;

            if (victim != null && victim.transform != null && attacker != null && victim == attacker) // allow player to suicide and self inflict
            {
                if (hitInfo.damageTypes.Has(DamageType.Suicide) && InEvent(victim))
                {
                    string uid = victim.UserIDString;

                    if (!dcsBlock.Contains(uid))
                    {
                        dcsBlock.Add(uid);
                        timer.Once(60f, () => dcsBlock.Remove(uid));
                    }
                }

                return null;
            }

            if (victim != null && hitInfo.damageTypes.GetMajorityDamageType() == DamageType.Fall && !dataImmunity.ContainsKey(victim.UserIDString))
                return null;

            if (attacker?.transform != null && spectators.Contains(attacker.UserIDString)) // 0.1.27: someone will find a way to abuse spectate mode so we'll prevent that now
            {
                if (!adt)
                {
                    EndSpectate(attacker);
                    SendHome(attacker);
                }

                CancelDamage(hitInfo);
                return true;
            }

            if ((adt || vdt) && !hitInfo.Initiator.IsKilled() && hitInfo.Initiator.IsNpc) // 1.2.0
            {
                if (hitInfo.Initiator is BaseNpc)
                {
                    var npc = hitInfo.Initiator as BaseNpc;

                    if (npc != null)
                    {
                        if (putToSleep)
                        {
                            npc.SetAiFlag(BaseNpc.AiFlags.Sleeping, true);
                            npc.CurrentBehaviour = BaseNpc.Behaviour.Sleep;
                        }
                        else if (killNpc)
                        {
                            npc.Kill();
                        }

                        return true;
                    }
                }
                else if (hitInfo.Initiator is BasePlayer)
                {
                    var npc = hitInfo.Initiator as BasePlayer;

                    if (npc != null)
                    {
                        if (npc is global::HumanNPC)
                        {
                            (npc as global::HumanNPC).LootSpawnSlots = new LootContainer.LootSpawnSlot[0];
                        }

                        npc.Kill();
                        CancelDamage(hitInfo);
                        return true;
                    }
                }
            }

            if (dataDuelists.Count > 0)
            {
                if (attacker?.transform != null && IsDueling(attacker) && victim != null && dataDuelists[attacker.UserIDString] != victim.UserIDString) // 0.1.8 check attacker then victim
                    return true; // prevent attacker from doing damage to others

                if (victim?.transform != null && IsDueling(victim)) // 1.2.0 NRE get_transform
                {
                    if (victim.health == 6f)
                        return true;

                    if (dataImmunity.ContainsKey(victim.UserIDString))
                        return true; // immunity timer

                    if (hitInfo.Initiator is PatrolHelicopter)
                        return true; // protect duelers from helicopters

                    if (attacker?.transform != null && dataDuelists[victim.UserIDString] != attacker.UserIDString)
                        return true; // prevent attacker from doing damage to others

                    hitInfo.damageTypes.ScaleAll(damageScaleAmount);
                    return null;
                }
            }

            if (tdmMatches.Count > 0)
            {
                if (victim != null && attacker?.transform != null && InDeathmatch(attacker))
                {
                    var match = GetMatch(attacker);

                    if (match.GetTeam(victim) == Team.None)
                        return true;

                    if (!dmFF && match.GetTeam(victim) == match.GetTeam(attacker))
                        return true; // FF
                }

                if (victim?.transform != null && InDeathmatch(victim))
                {
                    if (dataImmunity.ContainsKey(victim.UserIDString))
                        return true;

                    if (hitInfo.Initiator is PatrolHelicopter)
                        return true;

                    if (attacker?.transform != null)
                    {
                        if (GetMatch(attacker) == null)
                            return true;

                        if (victim.health == 6f)
                            return true;

                        if (tdmAttackers.ContainsKey(victim.UserIDString))
                            tdmAttackers.Remove(victim.UserIDString);

                        string weapon = attacker.GetActiveItem()?.info?.displayName?.english ?? hitInfo?.WeaponPrefab?.ShortPrefabName ?? "??";

                        if (weapon.EndsWith(".entity"))
                        {
                            var def = ItemManager.FindItemDefinition(weapon.Replace(".entity", "").Replace("_", "."));
                            weapon = def?.displayName.translated ?? weapon.Replace(".entity", "").Replace("_", "").SentenceCase();
                        }

                        tdmAttackers.Add(victim.UserIDString, new AttackerInfo());
                        tdmAttackers[victim.UserIDString].AttackerName = attacker.displayName;
                        tdmAttackers[victim.UserIDString].AttackerId = attacker.UserIDString;
                        tdmAttackers[victim.UserIDString].Distance = Math.Round(Vector3.Distance(attacker.transform.position, victim.transform.position), 2).ToString();
                        tdmAttackers[victim.UserIDString].BoneName = FormatBone(hitInfo.boneName).TrimEnd(); //StringPool.Get(hitInfo.boneName)
                        tdmAttackers[victim.UserIDString].Weapon = weapon;
                    }

                    hitInfo.damageTypes.ScaleAll(damageScaleAmount);
                    return null;
                }
            }

            if (victim?.transform != null && attacker?.transform != null) // 1.1.1 - fix for players standing on the edge of a zone for protection
            {
                if (vdt && !InEvent(victim))
                    return null;

                if (adt && !InEvent(attacker))
                    return null;
            }

            if (adt && !vdt)
                return true; // block all damage to the outside

            if (!adt && vdt)
                return true; // block all damage to the inside

            return null;
        }

        private object OnRestoreUponDeath(BasePlayer player)
        {
            if (DuelTerritory(player.transform.position))
            {
                return InEvent(player) ? true : (object)null;
            }
            return null;
        }

        class Explosives : FacepunchBehaviour
        {
            WorldItem worldItem;
            float minExplosionRadius = 1f;
            float explosionRadius = 5f;
            int layers = 141568;

            void Awake()
            {
                worldItem = GetComponent<WorldItem>();
            }

            void OnDestroy()
            {
                var damageTypes = new List<DamageTypeEntry>
                {
                    new DamageTypeEntry()
                    {
                        amount = 25f,
                        type = DamageType.Explosion
                    }
                };

                Effect.server.Run("some resource", worldItem.PivotPoint(), worldItem.transform.forward, null, true);
                DamageUtil.RadiusDamage(worldItem, worldItem.LookupPrefab(), worldItem.CenterPoint(), minExplosionRadius, explosionRadius, damageTypes, layers, true);
                Destroy(this);
            }
        }

        void OnEntitySpawned(WorldItem worldItem)
        {
            NextTick(() =>
            {
                if (!worldItem.IsKilled() && worldItem.item != null && worldItem.item.info.shortname == "arrow.bone")
                {
                    worldItem.gameObject.AddComponent<Explosives>();
                }
            });
        }

        private void OnEntitySpawned(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed || !DuelTerritory(entity.transform.position))
                return;

            if (entity.IsNpc)
            {
                NextTick(entity.SafelyKill);
                return;
            }

            if (autoOvens && entity is BaseOven || autoTurrets && entity is AutoTurret || autoFlames && entity is FlameTurret)
            {
                SetupPower(entity);
                return;
            }

            if (noStability && entity is BuildingBlock block)
            {
                if (block.OwnerID == 0 || permission.UserHasGroup(block.OwnerID.ToString(), "admin"))
                {
                    block.grounded = true;
                    return;
                }
            }

            if (dataDuelists.Count == 0 && tdmMatches.Count == 0)
                return;

            if (prefabs.ContainsKey(entity.PrefabName))
            {
                if (entity.name.Contains("barricade."))
                {
                    if (morphBarricadesStoneWalls || morphBarricadesWoodenWalls)
                    {
                        var wall = CreateZoneWall(morphBarricadesStoneWalls ? heswPrefab : hewwPrefab, entity.transform.position, entity.transform.rotation, entity.OwnerID);

                        if (wall != null)
                        {
                            entity.SafelyKill();

                            if (!duelEntities.TryGetValue(wall.OwnerID, out var entities))
                                duelEntities.Add(wall.OwnerID, entities = new());

                            entities.Add(wall);

                            return;
                        }
                    }
                }

                if (!duelEntities.TryGetValue(entity.OwnerID, out var entities2))
                    duelEntities.Add(entity.OwnerID, entities2 = new());
                
                entities2.Add(entity);
            }

            if (entity is PlayerCorpse || entity.name.Contains("item_drop_backpack"))
            {
                NextTick(entity.SafelyKill);
            }
            else if (entity is WorldItem)
            {
                if (duelsData.Homes.Count > 0)
                {
                    NextTick(() => // prevent rpc kick by using NextTick since we're also hooking OnItemDropped
                    {
                        if (!entity.IsKilled()) // we must check this or you will still be rpc kicked
                        {
                            var worldItem = entity as WorldItem; // allow thrown weapons / destroy items which are dropped by players and on death

                            if (worldItem != null && worldItem.item != null && !IsThrownWeapon(worldItem.item))
                                entity.Kill();
                        }
                    });

                    if (!entity.IsKilled())
                    {
                        timer.Repeat(0.1f, 20, () => // track the item to make sure it wasn't thrown out of the dueling zone
                        {
                            if (!entity.IsKilled() && !DuelTerritory(entity.transform.position))
                                entity.Kill(); // destroy items which are dropped from inside to outside of the zone
                        });
                    }
                }
            }
        }

        object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = planner.GetOwnerPlayer();

            if (player.IsAdmin)
                return null;

            var position = player.transform.position;
            var buildPos = position + player.eyes.BodyForward() * 4f; // get the estimated position of where the player is trying to build at
            var up = buildPos + Vector3.up + new Vector3(0f, 0.6f, 0f);

            buildPos.y = Mathf.Max(position.y, up.y); // adjust the cursor position to our best estimate

            if (DuelTerritory(buildPos, buildingBlockExtensionRadius)) // extend the distance slightly
            {
                if (deployables.Count > 0)
                {
                    var kvp = prefabs.FirstOrDefault(x => x.Key == prefab.fullName);

                    if (!string.IsNullOrEmpty(kvp.Value) && deployables.ContainsKey(kvp.Value) && deployables[kvp.Value])
                    {
                        if (dataDuelists.ContainsKey(player.UserIDString) || InMatch(player))
                        {
                            return null;
                        }
                    }
                }

                Message(player, msg("Building is blocked!", player.UserIDString));
                return false;
            }

            return null;
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity) // stop all players from looting anything inside of dueling zones. this allows server owners to setup duels anywhere without worry.
        {
            if (player != null && (IsDueling(player) || InDeathmatch(player) || IsSpectator(player)))
                timer.Once(0.01f, player.EndLooting);

            if (dataDuelists.Count == 0 && tdmMatches.Count == 0 && spectators.Count == 0)
                Unsubscribe(nameof(OnLootEntity));
        }

        private object OnCreateWorldProjectile(HitInfo info, Item item) // prevents thrown items from becoming stuck in players when they respawn and requiring them to relog to remove them
        {
            if (info == null)
                return null;

            if (dataDuelists.Count == 0 && tdmMatches.Count == 0)
            {
                Unsubscribe(nameof(OnCreateWorldProjectile));
                return null;
            }

            var victim = info.HitEntity as BasePlayer;
            var attacker = info.Initiator as BasePlayer;

            if (victim != null && (IsDueling(victim) || InDeathmatch(victim)))
                return false; // block it

            if (attacker != null && (IsDueling(attacker) || InDeathmatch(attacker)))
                return false;

            return null;
        }

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (dataDuelists.Count == 0 && tdmMatches.Count == 0) // nothing left to do here, unsubscribe the hook
            {
                Unsubscribe(nameof(OnItemDropped));
                return;
            }

            if (item.GetOwnerPlayer() == null)
                return;

            var player = item.GetOwnerPlayer();

            if (!IsThrownWeapon(item) && (IsDueling(player) || InDeathmatch(player)))
                item.Remove(0.01f); // do NOT allow players to drop items
        }

        private object IsPrisoner(BasePlayer player) // Random Warps
        {
            return IsDueling(player) || InDeathmatch(player) ? (object)true : null;
        }

        private object CanEventJoin(BasePlayer player) // EventManager
        {
            return IsDueling(player) || InDeathmatch(player) ? msg("CannotEventJoin", player.UserIDString) : null;
        }

        private object canRemove(BasePlayer player) // RemoverTool
        {
            return DuelTerritory(player.transform.position) ? (object)false : null;
        }

        private object CanTrade(BasePlayer player) // Trade
        {
            return DuelTerritory(player.transform.position) ? (object)false : null;
        }

        private object CanBank(BasePlayer player)
        {
            return DuelTerritory(player.transform.position) ? msg("CannotBank", player.UserIDString) : null;
        }

        private object CanOpenBackpack(BasePlayer player)
        {
            return DuelTerritory(player.transform.position) ? msg("CommandNotAllowed", player.UserIDString) : null;
        }

        private object canShop(BasePlayer player) // Shop and ServerRewards
        {
            return DuelTerritory(player.transform.position) ? msg("CannotShop", player.UserIDString) : null;
        }

        private object CanShop(BasePlayer player)
        {
            return DuelTerritory(player.transform.position) ? msg("CannotShop", player.UserIDString) : null;
        }

        private object CanBePenalized(BasePlayer player) // ZLevels Remastered
        {
            return DuelTerritory(player.transform.position) || dataDuelists.ContainsKey(player.UserIDString) ? (object)false : null;
        }

        private object canTeleport(BasePlayer player) // 0.1.2: block teleport from NTeleportation plugin
        {
            return DuelTerritory(player.transform.position) ? msg("CannotTeleport", player.UserIDString) : null;
        }

        private object CanTeleport(BasePlayer player) // 0.1.2: block teleport from MagicTeleportation plugin
        {
            return DuelTerritory(player.transform.position) ? msg("CannotTeleport", player.UserIDString) : null;
        }

        private object CanJoinTDMEvent(BasePlayer player)
        {
            return DuelTerritory(player.transform.position) ? (object)false : null;
        }

        private object CanEntityTakeDamage(BaseEntity entity, HitInfo hitinfo) // TruePVE!!!! <3 @ignignokt84
        {
            return entity is BasePlayer && DuelTerritory(entity.transform.position) ? (object)true : null;
        }

        private object CanJoinAimTrain(BasePlayer player)
        {
            if (InQueue(player.UserIDString) || InEvent(player) || DuelTerritory(player.transform.position))
            {
                return false;
            }

            return null;
        }

        private bool InQueue(string userid)
        {
            foreach (var element in duelsData.Queued)
            {
                if (element.Value == userid)
                {
                    return true;
                }
            }

            return false;
        }

        object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            if (!player.IsValid() || player.transform == null || InQueue(player.UserIDString))
            {
                return null;
            }

            if (DuelTerritory(player.transform.position))
            {
                if (useBlacklistCommands && blacklistCommands.Exists(entry => entry.Replace("/", "").Equals(command, StringComparison.OrdinalIgnoreCase)))
                {
                    Message(player, msg("CommandNotAllowed", player.UserIDString));
                    return true;
                }

                if (useWhitelistCommands && !whitelistCommands.Exists(entry => entry.Replace("/", "").Equals(command, StringComparison.OrdinalIgnoreCase)))
                {
                    Message(player, msg("CommandNotAllowed", player.UserIDString));
                    return true;
                }
            }

            return null;
        }

        object OnServerCommand(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (!player.IsValid() || InQueue(player.UserIDString))
            {
                return null;
            }

            if (DuelTerritory(player.transform.position))
            {
                string command = arg.cmd.FullName;

                if (useBlacklistCommands && blacklistCommands.Exists(entry => entry.Replace("/", "").Equals(command, StringComparison.OrdinalIgnoreCase)))
                {
                    Message(player, msg("CommandNotAllowed", player.UserIDString));
                    return true;
                }

                if (useWhitelistCommands && !whitelistCommands.Exists(entry => entry.Replace("/", "").Equals(command, StringComparison.OrdinalIgnoreCase)))
                {
                    Message(player, msg("CommandNotAllowed", player.UserIDString));
                    return true;
                }
            }

            return null;
        }

        public void CheckAutoReady(BasePlayer player)
        {
            if (duelsData.AutoReady.Contains(player.UserIDString))
            {
                if (!readyUiList.Contains(player.UserIDString))
                {
                    ToggleReadyUI(player);
                }
            }
            else if (readyUiList.Contains(player.UserIDString))
            {
                CuiHelper.DestroyUi(player, "DuelistUI_Ready");
                readyUiList.Remove(player.UserIDString);
            }
        }

        public void ToggleAutoReady(BasePlayer player)
        {
            if (duelsData.AutoReady.Contains(player.UserIDString))
                duelsData.AutoReady.Remove(player.UserIDString);
            else
                duelsData.AutoReady.Add(player.UserIDString);

            Message(player, msg(duelsData.AutoReady.Contains(player.UserIDString) ? "RematchAutoOn" : "RematchAutoOff", player.UserIDString));

            if (DuelTerritory(player.transform.position))
                CreateDefeatUI(player);

            if (duelistUI.Contains(player.UserIDString))
                RefreshUI(player);
        }

        public void ReadyUp(BasePlayer player)
        {
            var rematch = rematches.FirstOrDefault(x => x.HasPlayer(player));

            if (rematch == null)
            {
                ToggleAutoReady(player);
                Message(player, msg("RematchNone", player.UserIDString));
                return;
            }

            if (rematch.Ready.Contains(player))
            {
                Message(player, msg("RematchAcceptedAlready", player.UserIDString));
                ToggleAutoReady(player);
            }
            else
            {
                Message(player, msg("RematchAccepted", player.UserIDString));
                rematch.Ready.Add(player);
            }

            if (rematch.IsReady())
            {
                rematch.Start();
                rematches.Remove(rematch);
            }
        }

        public void cmdTDM(BasePlayer player, string command, string[] args)
        {
            if (player.IsAdmin && args.Length == 1 && args[0] == "showall" && tdmMatches.Count > 0)
            {
                foreach (var match in tdmMatches)
                {
                    Message(player, msg("InMatchListGood", player.UserIDString, match.GetNames(Team.Good)));
                    Message(player, msg("InMatchListEvil", player.UserIDString, match.GetNames(Team.Evil)));
                }

                return;
            }

            if (!autoAllowAll && !duelsData.Allowed.Contains(player.UserIDString))
            {
                Message(player, msg("MustAllowDuels", player.UserIDString, szDuelChatCommand));
                return;
            }

            if (IsDueling(player))
            {
                Message(player, msg("AlreadyInADuel", player.UserIDString));
                return;
            }

            var deathmatch = tdmMatches.FirstOrDefault(x => x.GetTeam(player) != Team.None);

            if (deathmatch != null && deathmatch.IsStarted)
            {
                Message(player, msg("MatchStartedAlready", player.UserIDString));
                return;
            }

            if (args.Length == 0)
            {
                if (deathmatch == null)
                {
                    if (!autoAllowAll)
                        Message(player, msg("HelpAllow", player.UserIDString, szDuelChatCommand));

                    Message(player, msg("MatchChallenge0", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchChallenge2", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchChallenge3", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchAccept", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchCancel", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchLeave", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchSize", player.UserIDString, szMatchChatCommand, minDeathmatchSize));
                    Message(player, msg("MatchKickBan", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchSetCode", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchTogglePublic", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchKit", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("UI_Help", player.UserIDString, szUIChatCommand));
                }
                else
                {
                    Message(player, msg("MatchLeave", player.UserIDString, szMatchChatCommand));

                    if (!deathmatch.IsHost(player))
                        return;

                    Message(player, msg("MatchCancel", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchSize", player.UserIDString, szMatchChatCommand, minDeathmatchSize));
                    Message(player, msg("MatchKickBan", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchSetCode", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchTogglePublic", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("MatchKit", player.UserIDString, szMatchChatCommand));
                    Message(player, msg("InMatchListGood", player.UserIDString, deathmatch.GetNames(Team.Good)));
                    Message(player, msg("InMatchListEvil", player.UserIDString, deathmatch.GetNames(Team.Evil)));
                }

                return;
            }

            RemoveRequests(player);

            switch (args[0].ToLower())
            {
                case "autoready":
                    {
                        ToggleAutoReady(player);
                        return;
                    }
                case "rematch":
                case "ready":
                    {
                        ReadyUp(player);
                        return;
                    }
                case "kit":
                    {
                        if (deathmatch != null)
                        {
                            if (!deathmatch.IsHost(player))
                            {
                                Message(player, msg("MatchKitSet", player.UserIDString, deathmatch.Kit));
                                return;
                            }

                            if (args.Length == 2)
                            {
                                string kit = GetVerifiedKit(args[1]);

                                if (string.IsNullOrEmpty(kit))
                                {
                                    Message(player, msg("MatchChallenge0", player.UserIDString, szMatchChatCommand));
                                    Message(player, msg("KitDoesntExist", player.UserIDString, args[1]));

                                    string kits = string.Join(", ", VerifiedKits.ToArray());

                                    if (!string.IsNullOrEmpty(kits))
                                        Message(player, "Kits: " + kits);
                                }
                                else
                                    deathmatch.Kit = kit;
                            }
                            else
                                Message(player, msg("MatchKit", player.UserIDString));
                        }
                        else
                            Message(player, msg("MatchDoesntExist", player.UserIDString, szMatchChatCommand));

                        return;
                    }
                case "kickban":
                    {
                        if (deathmatch != null)
                        {
                            if (!deathmatch.IsHost(player))
                            {
                                Message(player, msg("MatchNotAHost", player.UserIDString));
                                return;
                            }

                            if (args.Length == 2)
                            {
                                var target = BasePlayer.Find(args[1]);

                                if (target != null)
                                {
                                    if (deathmatch.GetTeam(target) == deathmatch.GetTeam(player))
                                    {
                                        if (deathmatch.Ban(target))
                                            Message(player, msg("MatchBannedUser", player.UserIDString, target.displayName));
                                        else
                                            Message(player, msg("MatchCannotBan", player.UserIDString));
                                    }
                                    else
                                        Message(player, msg("MatchPlayerNotFound", player.UserIDString, target.displayName));
                                }
                                else
                                    Message(player, msg("PlayerNotFound", player.UserIDString, args[1]));
                            }
                            else
                                Message(player, msg("MatchKickBan", player.UserIDString));
                        }
                        else
                            Message(player, msg("MatchDoesntExist", player.UserIDString, szMatchChatCommand));

                        break;
                    }
                case "setcode":
                    {
                        if (deathmatch != null)
                        {
                            if (deathmatch.IsHost(player))
                            {
                                if (args.Length == 2)
                                    deathmatch.SetCode(player, args[1]);

                                Message(player, msg("MatchCodeIs", player.UserIDString, deathmatch.GetTeam(player) == Team.Evil ? deathmatch.Code(Team.Evil) : deathmatch.Code(Team.Good)));
                            }
                            else
                                Message(player, msg("MatchNotAHost", player.UserIDString));
                        }
                        else
                            Message(player, msg("MatchDoesntExist", player.UserIDString, szMatchChatCommand));

                        break;
                    }
                case "cancel":
                case "decline":
                    {
                        if (deathmatch != null)
                        {
                            if (deathmatch.IsHost(player))
                            {
                                deathmatch.MessageAll("MatchCancelled", player.displayName);
                                deathmatch.End();

                                if (tdmMatches.Contains(deathmatch))
                                {
                                    tdmMatches.Remove(deathmatch);
                                    matchUpdateRequired = true;
                                }
                            }
                            else
                                Message(player, msg("MatchNotAHost", player.UserIDString));
                        }
                        else // also handle cancelling a match request
                        {
                            if (tdmRequests.ContainsValue(player.UserIDString))
                            {
                                var entry = tdmRequests.FirstOrDefault(kvp => kvp.Value == player.UserIDString);
                                var target = BasePlayer.Find(entry.Key);

                                if (target != null)
                                    Message(target, msg("MatchCancelled", target.UserIDString, player.displayName));

                                Message(player, msg("MatchCancelled", player.UserIDString, player.displayName));
                                tdmRequests.Remove(entry.Key);
                                return;
                            }

                            if (tdmRequests.ContainsKey(player.UserIDString))
                            {
                                var target = BasePlayer.Find(tdmRequests[player.UserIDString]);

                                if (target != null)
                                    Message(target, msg("MatchCancelled", player.UserIDString, player.displayName));

                                Message(player, msg("MatchCancelled", player.UserIDString, player.displayName));
                                tdmRequests.Remove(player.UserIDString);
                                return;
                            }

                            Message(player, msg("MatchDoesntExist", player.UserIDString, szMatchChatCommand));
                        }

                        break;
                    }
                case "size":
                    {
                        if (deathmatch != null)
                        {
                            if (args.Length == 2)
                            {
                                if (args[1].All(char.IsDigit))
                                {
                                    if (deathmatch.IsHost(player))
                                    {
                                        int size = Convert.ToInt32(args[1]);

                                        if (size < minDeathmatchSize)
                                            size = deathmatch.TeamSize;

                                        if (size > maxDeathmatchSize)
                                            size = maxDeathmatchSize;

                                        if (deathmatch.TeamSize != size)
                                            deathmatch.TeamSize = size; // sends message to all players in the match
                                    }
                                    else
                                        Message(player, msg("MatchNotAHost", player.UserIDString));
                                }
                                else
                                    Message(player, msg("InvalidNumber", player.UserIDString, args[1]));
                            }
                            else
                                Message(player, msg("MatchSizeSyntax", player.UserIDString, szMatchChatCommand));
                        }
                        else
                            Message(player, msg("MatchDoesntExist", player.UserIDString, szMatchChatCommand));

                        break;
                    }
                case "accept":
                    {
                        if (InEvent(player))
                        {
                            Message(player, msg("AlreadyDueling", player.UserIDString));
                            return;
                        }

                        if (!tdmRequests.ContainsValue(player.UserIDString))
                        {
                            Message(player, msg("MatchNoneRequested", player.UserIDString));
                            return;
                        }

                        var kvp = tdmRequests.FirstOrDefault(entry => entry.Value == player.UserIDString);
                        var target = BasePlayer.Find(kvp.Key);

                        tdmRequests.Remove(kvp.Key);

                        if (IsNotConnected(target))
                        {
                            Message(player, msg("MatchPlayerOffline", player.UserIDString));
                            break;
                        }

                        SetupTeams(player, target);
                        break;
                    }
                case "leave":
                    {
                        if (deathmatch != null)
                        {
                            deathmatch.RemoveMatchPlayer(player);
                            Message(player, msg("MatchPlayerLeft", player.UserIDString));
                        }
                        else
                            Message(player, msg("MatchDoesntExist", player.UserIDString, szMatchChatCommand));

                        break;
                    }
                case "any":
                    {
                        if (tdmMatches.Count == 0)
                        {
                            Message(player, msg("MatchNoMatchesExist", player.UserIDString, szMatchChatCommand));
                            return;
                        }

                        if (deathmatch != null)
                        {
                            deathmatch.RemoveMatchPlayer(player);
                            Message(player, msg("MatchPlayerLeft", player.UserIDString));
                        }

                        foreach (var match in tdmMatches)
                        {
                            if (match.IsBanned(player.userID) || match.IsFull())
                                continue;

                            if (!match.IsFull(Team.Good) && match.AlliedTo(player, Team.Good))
                            {
                                match.AddMatchPlayer(player, Team.Good);
                                return;
                            }

                            if (!match.IsFull(Team.Evil) && match.AlliedTo(player, Team.Evil))
                            {
                                match.AddMatchPlayer(player, Team.Evil);
                                return;
                            }

                            if (match.IsPublic)
                            {
                                if (!match.IsFull(Team.Good))
                                {
                                    match.AddMatchPlayer(player, Team.Good);
                                    return;
                                }

                                if (!match.IsFull(Team.Evil))
                                {
                                    match.AddMatchPlayer(player, Team.Evil);
                                    return;
                                }
                            }
                        }

                        Message(player, msg("MatchNoTeamFoundAny", player.UserIDString, args[0]));
                        break;
                    }
                case "public":
                    {
                        if (deathmatch != null)
                        {
                            if (!deathmatch.IsHost(player))
                            {
                                Message(player, msg("MatchNotAHost", player.UserIDString));
                                return;
                            }

                            deathmatch.IsPublic = !deathmatch.IsPublic;
                        }
                        else
                            Message(player, msg("MatchDoesntExist", player.UserIDString, szMatchChatCommand));

                        break;
                    }
                default:
                    {
                        if (args.Length > 1)
                        {
                            SetPlayerZone(player, args);

                            foreach (string arg in args)
                            {
                                string kit = GetVerifiedKit(arg);

                                if (!string.IsNullOrEmpty(kit))
                                {
                                    tdmKits[player.UserIDString] = kit;
                                    break;
                                }
                            }
                        }

                        var target = BasePlayer.Find(args[0]);

                        if (target != null)
                        {
                            if (target == player)
                            {
                                Message(player, msg("PlayerNotFound", player.UserIDString, args[0]));
                                return;
                            }

                            if (deathmatch != null)
                            {
                                Message(player, msg("MatchCannotChallengeAgain", player.UserIDString));
                                return;
                            }

                            if (InMatch(target) || tdmRequests.ContainsValue(target.UserIDString))
                            {
                                Message(player, msg("MatchCannotChallenge", player.UserIDString, target.displayName));
                                return;
                            }

                            if (!IsNewman(player))
                            {
                                Message(player, msg("MustBeNaked", player.UserIDString));
                                return;
                            }

                            if (!IsNewman(target))
                            {
                                Message(player, msg("TargetMustBeNaked", player.UserIDString));
                                return;
                            }

                            Message(player, msg("MatchRequestSent", player.UserIDString, target.displayName));
                            Message(target, msg("MatchRequested", target.UserIDString, player.displayName, szMatchChatCommand));

                            string uid = player.UserIDString;
                            tdmRequests.Remove(uid);
                            tdmRequests.Add(uid, target.UserIDString);
                            timer.Once(60f, () => tdmRequests.Remove(uid));
                            return;
                        }

                        if (tdmMatches.Count == 0)
                        {
                            Message(player, msg("MatchNoMatchesExist", player.UserIDString, szMatchChatCommand));
                            return;
                        }

                        if (deathmatch != null)
                        {
                            deathmatch.RemoveMatchPlayer(player);
                            Message(player, msg("MatchPlayerLeft", player.UserIDString));
                        }

                        foreach (var match in tdmMatches)
                        {
                            if (match.IsBanned(player.userID))
                                continue;

                            if (match.Code(Team.Good).Equals(args[0], StringComparison.OrdinalIgnoreCase))
                            {
                                match.AddMatchPlayer(player, Team.Good);
                                return;
                            }
                            else if (match.Code(Team.Evil).Equals(args[0], StringComparison.OrdinalIgnoreCase))
                            {
                                match.AddMatchPlayer(player, Team.Evil);
                                return;
                            }
                        }

                        Message(player, msg("MatchNoTeamFoundCode", player.UserIDString, args[0]));

                    }
                    break;
            }
        }

        public void cmdQueue(BasePlayer player, string command, string[] args)
        {
            if (IsNotConnected(player) || AimTrain.CanCall() && Convert.ToBoolean(AimTrain?.Call("IsAimTraining", player.userID)))
                return;

            if (!player.CanInteract())
            {
                timer.Once(1f, () => cmdQueue(player, command, args));
                return;
            }

            if (!autoAllowAll && !duelsData.Allowed.Contains(player.UserIDString))
            {
                Message(player, msg("MustAllowDuels", player.UserIDString, szDuelChatCommand));
                return;
            }

            if (InMatch(player))
            {
                Message(player, msg("MatchTeamed", player.UserIDString));
                return;
            }

            if (IsDueling(player))
            {
                Message(player, msg("AlreadyInADuel", player.UserIDString));
                return;
            }

            RemoveRequests(player);

            if (!IsNewman(player))
            {
                Message(player, msg("MustBeNaked", player.UserIDString));
                return;
            }

            if (player.IsAdmin)
            {
                Message(player, msg("InQueueList", player.UserIDString));
                Message(player, string.Join(", ", duelsData.Queued.ToList().Select(kvp => GetDisplayName(kvp.Value))));
            }

            if (!InQueue(player.UserIDString))
            {
                long stamp = TimeStamp();

                while (duelsData.Queued.ContainsKey(stamp))
                    stamp++;

                duelsData.Queued.Add(stamp, player.UserIDString);
                Message(player, msg("InQueueSuccess", player.UserIDString));
                CheckQueue();
                return;
            }

            if (RemoveFromQueue(player.UserIDString))
                Message(player, msg("NoLongerQueued", player.UserIDString));
        }

        private void cmdLadder(BasePlayer player, string command, string[] args)
        {
            bool onLadder = false;
            bool life = args.Exists(arg => arg.ToLower().Contains("life"));
            var sorted = life ? duelsData.Victories.ToList() : duelsData.VictoriesSeed.ToList();
            sorted.Sort((x, y) => y.Value.CompareTo(x.Value));

            Message(player, msg(life ? "TopAll" : "Top", player.UserIDString, sorted.Count));

            for (int i = 0; i < 10; i++)
            {
                if (i >= sorted.Count)
                    break;

                if (sorted[i].Key == player.UserIDString)
                    onLadder = true; // 0.1.2: fix for ranks showing user on ladder twice

                string name = GetDisplayName(sorted[i].Key);
                int losses = 0;

                if (life)
                    losses = duelsData.Losses.ContainsKey(sorted[i].Key) ? duelsData.Losses[sorted[i].Key] : 0;
                else
                    losses = duelsData.LossesSeed.ContainsKey(sorted[i].Key) ? duelsData.LossesSeed[sorted[i].Key] : 0;

                double ratio = losses > 0 ? Math.Round(sorted[i].Value / (double)losses, 2) : sorted[i].Value;
                string message = msg("TopFormat", player.UserIDString, (i + 1).ToString(), name, sorted[i].Value, losses, ratio);
                Player.Message(player, message, Convert.ToUInt64(sorted[i].Key));
            }

            if (!onLadder && !life && duelsData.VictoriesSeed.ContainsKey(player.UserIDString))
            {
                int index = sorted.FindIndex(kvp => kvp.Key == player.UserIDString);
                int losses = duelsData.LossesSeed.ContainsKey(player.UserIDString) ? duelsData.LossesSeed[player.UserIDString] : 0;
                double ratio = losses > 0 ? Math.Round(duelsData.VictoriesSeed[player.UserIDString] / (double)losses, 2) : duelsData.VictoriesSeed[player.UserIDString];
                string message = msg("TopFormat", player.UserIDString, index, player.displayName, duelsData.VictoriesSeed[player.UserIDString], losses, ratio);
                Player.Message(player, message, player.userID);
            }

            if (!onLadder && life && duelsData.Victories.ContainsKey(player.UserIDString))
            {
                int index = sorted.FindIndex(kvp => kvp.Key == player.UserIDString);
                int losses = duelsData.Losses.ContainsKey(player.UserIDString) ? duelsData.Losses[player.UserIDString] : 0;
                double ratio = losses > 0 ? Math.Round(duelsData.Victories[player.UserIDString] / (double)losses, 2) : duelsData.Victories[player.UserIDString];
                string message = msg("TopFormat", player.UserIDString, index, player.displayName, duelsData.Victories[player.UserIDString], losses, ratio);
                Player.Message(player, message, player.userID);
            }

            if (!life) Message(player, msg("LadderLife", player.UserIDString, szDuelChatCommand));
            sorted.Clear();
            sorted = null;
        }

        private void ccmdDuel(ConsoleSystem.Arg arg)
        {
            if (!arg.IsAdmin)
                return;

            string id = arg.Player()?.UserIDString ?? null;

            if (arg.HasArgs(1))
            {
                switch (arg.Args[0].ToLower())
                {
                    case "resetseed":
                        {
                            duelsData.VictoriesSeed.Clear();
                            duelsData.LossesSeed.Clear();
                            duelsData.MatchKillsSeed.Clear();
                            duelsData.MatchDeathsSeed.Clear();
                            duelsData.MatchLossesSeed.Clear();
                            duelsData.MatchVictoriesSeed.Clear();
                            duelsData.MatchSizesVictoriesSeed.Clear();
                            duelsData.MatchSizesVictoriesSeed.Clear();
                            arg.ReplyWith(msg("ResetSeed", arg.Player()?.UserIDString));
                            break;
                        }
                    case "removeall":
                        {
                            if (duelingZones.Count > 0)
                            {
                                foreach (var zone in duelingZones.ToList())
                                {
                                    EjectPlayers(zone);
                                    arg.ReplyWith(msg("RemovedZoneAt", id, zone.Position));
                                    RemoveDuelZone(zone);
                                }

                                duelsData.DuelZones.Clear();
                                SaveData();
                            }
                            else
                                arg.ReplyWith(msg("NoZoneExists", id));

                            break;
                        }
                    case "1":
                    case "enable":
                    case "on":
                        {
                            if (duelsData.DuelsEnabled)
                            {
                                arg.ReplyWith(msg("DuelsEnabledAlready", id));
                                return;
                            }

                            duelsData.DuelsEnabled = true;
                            arg.ReplyWith(msg("DuelsNowEnabled", id));
                            DuelAnnouncement(false);
                            SaveData();
                            return;
                        }
                    case "0":
                    case "disable":
                    case "off":
                        {
                            if (!duelsData.DuelsEnabled)
                            {
                                arg.ReplyWith(msg("DuelsDisabledAlready", id));
                                return;
                            }

                            duelsData.DuelsEnabled = false;
                            arg.ReplyWith(msg(dataDuelists.Count > 0 ? "DuelsNowDisabled" : "DuelsNowDisabledEmpty", id));
                            SendDuelistsHome();
                            SendSpectatorsHome();
                            SaveData();
                            return;
                        }
                    case "new":
                        {
                            if (duelsData.DuelZones.Count >= zoneAmount)
                            {
                                arg.ReplyWith(msg("ZoneLimit", id, zoneAmount));
                                return;
                            }

                            string zoneName = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : GetZoneName();

                            if (SetupDuelZone(null, zoneName) != Vector3.zero)
                                arg.ReplyWith(msg("ZoneCreated", id));

                            return;
                        }
                    default:
                        {
                            arg.ReplyWith(string.Format("{0} on|off|new|removeall|resetseed", szDuelChatCommand));
                            break;
                        }
                }
            }
            else
                arg.ReplyWith(string.Format("{0} on|off|new|removeall|resetseed", szDuelChatCommand));
        }

        private void CommandDuelist(IPlayer player, string command, string[] args)
        {
            switch (args[0])
            {
                case "kit":
                    {
                        string kits = string.Join(", ", VerifiedKits.ToArray());

                        player.Reply(string.IsNullOrEmpty(kits) ? msg("KitsNotConfigured", player.Id) : "Kits: " + kits);
                        return;
                    }
            }
        }

        private void cmdDuel(BasePlayer player, string command, string[] args)
        {
            if (AimTrain.CanCall() && Convert.ToBoolean(AimTrain?.Call("IsAimTraining", player.userID)))
                return;

            if (IsEventBanned(player.UserIDString))
            {
                Message(player, msg("Banned", player.UserIDString));
                return;
            }

            if (dcsBlock.Contains(player.UserIDString))
            {
                Message(player, msg("SuicideBlock", player.UserIDString));
                return;
            }

            if (args.Length >= 1 && args[0] == "ladder")
            {
                cmdLadder(player, command, args);
                return;
            }

            if (!duelsData.DuelsEnabled)
            {
                if (!args.Exists(arg => arg.ToLower() == "on"))
                    Message(player, msg("DuelsDisabled", player.UserIDString));

                if (!player.IsAdmin)
                    return;
            }

            bool noZone = duelsData.DuelZones.Count == 0 || duelingZones.Count == 0;

            if (noZone)
            {
                if (!args.Exists(arg => arg.ToLower() == "new") && !args.Exists(arg => arg.ToLower() == "removeall") && !args.Exists(arg => arg.ToLower() == "custom"))
                    Message(player, msg("NoZoneExists", player.UserIDString));

                if (!player.IsAdmin)
                    return;
            }

            if (!noZone && !duelsData.DuelsEnabled && !args.Exists(arg => arg.ToLower() == "on"))
                Message(player, msg("DuelsMustBeEnabled", player.UserIDString, szDuelChatCommand));

            if (IsDueling(player) && !player.IsAdmin)
                return;

            if (args.Length == 0)
            {
                Message(player, msg("HelpDuels", player.UserIDString, duelsData.TotalDuels.ToString("N0")));
                Message(player, msg("ZoneNames", player.UserIDString, duelsData.DuelZones.Count, string.Join(" ", duelsData.DuelZones.Values.ToList().Take(10).ToArray())));

                if (!autoAllowAll)
                    Message(player, msg("HelpAllow", player.UserIDString, szDuelChatCommand));

                Message(player, msg("HelpBlock", player.UserIDString, szDuelChatCommand));
                Message(player, msg("HelpChallenge", player.UserIDString, szDuelChatCommand));
                Message(player, msg("HelpAccept", player.UserIDString, szDuelChatCommand));
                Message(player, msg("HelpCancel", player.UserIDString, szDuelChatCommand));
                Message(player, msg("HelpChat", player.UserIDString, szDuelChatCommand));
                Message(player, msg("HelpQueue", player.UserIDString, szQueueChatCommand));
                Message(player, msg("HelpLadder", player.UserIDString, szDuelChatCommand));
                Message(player, msg("HelpKit", player.UserIDString, szDuelChatCommand));

                if (allowBets)
                    Message(player, msg("HelpBet", player.UserIDString, szDuelChatCommand));

                if (tdmEnabled)
                    Message(player, msg("HelpTDM", player.UserIDString, szMatchChatCommand));

                Message(player, msg("UI_Help", player.UserIDString, szUIChatCommand));

                if (player.IsAdmin)
                {
                    Message(player, msg("HelpDuelAdmin", player.UserIDString, szDuelChatCommand));
                    Message(player, msg("HelpDuelAdminRefundAll", player.UserIDString, szDuelChatCommand));
                }

                return;
            }

            switch (args[0].ToLower())
            {
                case "autoready":
                    {
                        ToggleAutoReady(player);
                        return;
                    }
                case "rematch":
                case "ready":
                    {
                        ReadyUp(player);
                        return;
                    }
                case "resetseed":
                    {
                        if (player.IsAdmin)
                        {
                            duelsData.VictoriesSeed.Clear();
                            duelsData.LossesSeed.Clear();
                            duelsData.MatchKillsSeed.Clear();
                            duelsData.MatchDeathsSeed.Clear();
                            duelsData.MatchLossesSeed.Clear();
                            duelsData.MatchVictoriesSeed.Clear();
                            duelsData.MatchSizesVictoriesSeed.Clear();
                            duelsData.MatchSizesVictoriesSeed.Clear();
                            Message(player, msg("ResetSeed", player.UserIDString));
                            Puts("{0] ({1}): {2}", player.displayName, player.UserIDString, msg("ResetSeed", player.UserIDString));
                        }
                        break;
                    }
                case "remove_all_walls":
                    {
                        if (player.IsAdmin)
                        {
                            int removed = 0;

                            if (respawnWalls)
                            {
                                Unsubscribe(nameof(OnEntityKill));
                            }

                            foreach (var entity in GetWallEntities().ToList())
                            {
                                if (entity.OwnerID > 0 && !entity.OwnerID.IsSteamId())
                                {
                                    entity.Kill();
                                    removed++;
                                }
                            }

                            if (respawnWalls)
                            {
                                Subscribe(nameof(OnEntityKill));
                            }

                            Message(player, msg("RemovedXWalls", player.UserIDString, removed));
                        }
                        break;
                    }
                case "remove_all":
                    {
                        if (player.IsAdmin && args.Length == 2 && args[1].All(char.IsDigit))
                        {
                            ulong ownerId = ulong.Parse(args[1]);
                            int removed = 0;

                            if (respawnWalls)
                            {
                                Unsubscribe(nameof(OnEntityKill));
                            }

                            foreach (var entity in GetWallEntities().ToList())
                            {
                                if (entity.OwnerID == ownerId)
                                {
                                    entity.Kill();
                                    removed++;
                                }
                            }

                            if (respawnWalls)
                            {
                                Subscribe(nameof(OnEntityKill));
                            }

                            Message(player, msg("RemovedXWalls", player.UserIDString, removed));
                        }
                        break;
                    }
                case "0":
                case "disable":
                case "off":
                    {
                        if (player.IsAdmin)
                        {
                            if (!duelsData.DuelsEnabled)
                            {
                                Message(player, msg("DuelsDisabledAlready", player.UserIDString));
                                return;
                            }

                            duelsData.DuelsEnabled = false;
                            Message(player, msg(dataDuelists.Count > 0 ? "DuelsNowDisabled" : "DuelsNowDisabledEmpty", player.UserIDString));
                            SendDuelistsHome();
                            SendSpectatorsHome();
                            SaveData();
                        }
                        break;
                    }
                case "1":
                case "enable":
                case "on":
                    {
                        if (player.IsAdmin)
                        {
                            if (duelsData.DuelsEnabled)
                            {
                                Message(player, msg("DuelsEnabledAlready", player.UserIDString));
                                return;
                            }

                            duelsData.DuelsEnabled = true;
                            Message(player, msg("DuelsNowEnabled", player.UserIDString));
                            DuelAnnouncement(false);
                            SaveData();
                        }
                        break;
                    }
                case "custom":
                case "me":
                    {
                        if (player.IsAdmin)
                        {
                            if (duelsData.DuelZones.Count >= zoneAmount)
                            {
                                Message(player, msg("ZoneLimit", player.UserIDString, zoneAmount));
                                return;
                            }

                            RaycastHit hit;
                            if (Physics.Raycast(player.eyes.HeadRay(), out hit, Mathf.Infinity, wallMask))
                            {
                                if (DuelTerritory(hit.point, 5f))
                                {
                                    Message(player, msg("ZoneExists", player.UserIDString));
                                    return;
                                }

                                string zoneName = args.Length > 1 ? string.Join(" ", args.Skip(1)) : GetZoneName();
                                var zone = SetupDuelZone(hit.point, null, zoneName);
                                int i = 0;

                                foreach (var spawn in zone.Spawns)
                                    player.SendConsoleCommand("ddraw.text", 30f, Color.yellow, spawn, ++i);

                                UpdateStability();

                                if (zoneCounter > 0) Message(player, $"Zone will reset after {zoneCounter} duels. Disable by setting `Create New Zone Every X Duels [0 = disabled]` to `0` in the config.");
                            }
                            else
                                Message(player, msg("FailedRaycast", player.UserIDString));
                        }
                        break;
                    }
                case "remove":
                    {
                        if (player.IsAdmin)
                        {
                            if (duelingZones.Count > 0)
                            {
                                var zone = GetDuelZone(player.transform.position);

                                if (zone == null)
                                {
                                    Message(player, msg("NoZoneFound", player.UserIDString));
                                    return;
                                }

                                EjectPlayers(zone);
                                RemoveDuelZone(zone);
                                Message(player, msg("RemovedZone", player.UserIDString));
                            }

                        }
                        break;
                    }
                case "removeall":
                    {
                        if (player.IsAdmin)
                        {
                            if (duelingZones.Count > 0)
                            {
                                foreach (var zone in duelingZones.ToList())
                                {
                                    EjectPlayers(zone);
                                    Message(player, msg("RemovedZoneAt", player.UserIDString, zone.Position));
                                    RemoveDuelZone(zone);
                                }

                                duelsData.DuelZones.Clear();
                                SaveData();
                            }
                            else
                                Message(player, msg("NoZoneExists", player.UserIDString));
                        }
                        break;
                    }
                case "spawns":
                    {
                        if (player.IsAdmin)
                        {
                            if (args.Length >= 2)
                            {
                                switch (args[1].ToLower())
                                {
                                    case "add":
                                        AddSpawnPoint(player, true);
                                        break;
                                    case "here":
                                        AddSpawnPoint(player, false);
                                        break;
                                    case "remove":
                                        RemoveSpawnPoint(player);
                                        break;
                                    case "removeall":
                                        RemoveSpawnPoints(player);
                                        break;
                                    case "wipe":
                                        WipeSpawnPoints(player);
                                        break;
                                    default:
                                        SendSpawnHelp(player);
                                        break;
                                }

                                return;
                            }

                            SendSpawnHelp(player);

                            int i = 0;
                            float dist = float.MaxValue;
                            DuelingZone destZone = null;

                            foreach (var zone in duelingZones)
                            {
                                if (zone.Distance(player.transform.position) > zoneRadius + 200f)
                                    continue;

                                float distance = zone.Distance(player.transform.position);

                                if (distance < dist)
                                {
                                    dist = distance;
                                    destZone = zone;
                                }
                            }

                            if (destZone != null)
                                foreach (var spawn in destZone.Spawns)
                                    player.SendConsoleCommand("ddraw.text", 30f, Color.yellow, spawn, ++i);
                        }
                        break;
                    }
                case "rename":
                    {
                        if (player.IsAdmin)
                        {
                            if (args.Length > 1)
                            {
                                var zone = GetDuelZone(player.transform.position);

                                if (zone == null)
                                {
                                    Message(player, msg("NoZoneFound", player.UserIDString));
                                    return;
                                }

                                string zoneName = string.Join(" ", args.Skip(1));
                                duelsData.DuelZones[zone.Position.ToString()] = zoneName;
                                Message(player, msg("ZoneRenamed", player.UserIDString, zoneName));
                            }
                            else
                                Message(player, msg("ZoneRename", player.UserIDString, szDuelChatCommand));

                            return;
                        }
                        break;
                    }
                case "new":
                    {
                        if (player.IsAdmin)
                        {
                            if (duelsData.DuelZones.Count >= zoneAmount)
                            {
                                Message(player, msg("ZoneLimit", player.UserIDString, zoneAmount));
                                return;
                            }

                            string zoneName = GetZoneName();
                            var _nameArgs = args.Where(arg => arg.ToLower() != "tp");

                            if (_nameArgs.ToList().Count > 0)
                                zoneName = string.Join(" ", _nameArgs);

                            var zonePos = SetupDuelZone(null, zoneName);

                            if (zonePos != Vector3.zero)
                            {
                                Message(player, msg("ZoneCreated", player.UserIDString));

                                if (args.Exists(arg => arg.ToLower() == "tp"))
                                {
                                    Player.Teleport(player, zonePos);
                                }
                            }

                        }
                        break;
                    }
                case "tpm":
                    {
                        if (player.IsAdmin)
                        {
                            float dist = float.MaxValue;
                            var dest = Vector3.zero;
                            var matches = tdmMatches.Exists(m => m.IsStarted) ? tdmMatches.Where(m => m.IsStarted).ToList() : tdmMatches.ToList(); // 0.1.17 if multiple zones then choose from active ones if any exist

                            foreach (var match in matches)
                            {
                                float distance = match.Zone.Distance(player.transform.position);

                                if (matches.Count > 1 && distance < zoneRadius * 4f) // move admin to the next nearest zone
                                    continue;

                                if (distance < dist)
                                {
                                    dist = distance;
                                    dest = match.Zone.Position;
                                }
                            }

                            if (dest != Vector3.zero)
                                Player.Teleport(player, dest);
                        }
                        break;
                    }
                case "tp":
                    {
                        if (player.IsAdmin)
                        {
                            float dist = float.MaxValue;
                            var dest = Vector3.zero;
                            var zones = duelingZones.Count > 3 && duelingZones.Exists(zone => zone.TotalPlayers > 0) ? duelingZones.Where(zone => zone.TotalPlayers > 0).ToList() : duelingZones; // 0.1.17 if multiple zones then choose from active ones if any exist

                            foreach (var zone in zones)
                            {
                                float distance = zone.Distance(player.transform.position);

                                if (zones.Count > 1 && distance < zoneRadius * 4f) // move admin to the next nearest zone
                                    continue;

                                if (distance < dist)
                                {
                                    dist = distance;
                                    dest = zone.Position;
                                }
                            }

                            if (dest != Vector3.zero)
                                Player.Teleport(player, dest);
                        }
                        break;
                    }
                case "save":
                    {
                        if (player.IsAdmin)
                        {
                            SaveData();
                            Message(player, msg("DataSaved", player.UserIDString));
                        }
                    }
                    break;
                case "ban":
                    {
                        if (player.IsAdmin && args.Length >= 2)
                        {
                            string targetId = args[1].IsSteamId() ? args[1] : BasePlayer.Find(args[1])?.UserIDString ?? null;

                            if (string.IsNullOrEmpty(targetId))
                            {
                                Message(player, msg("PlayerNotFound", player.UserIDString, args[1]));
                                return;
                            }

                            if (!duelsData.Bans.ContainsKey(targetId))
                            {
                                duelsData.Bans.Add(targetId, player.UserIDString);
                                Message(player, msg("AddedBan", player.UserIDString, targetId));
                            }
                            else
                            {
                                duelsData.Bans.Remove(targetId);
                                Message(player, msg("RemovedBan", player.UserIDString, targetId));
                            }

                            SaveData();
                        }
                        break;
                    }
                case "announce":
                    {
                        if (player.IsAdmin)
                            DuelAnnouncement(true);

                        break;
                    }
                case "claim":
                    {
                        if (!duelsData.ClaimBets.ContainsKey(player.UserIDString))
                        {
                            Message(player, msg("NoBetsToClaim", player.UserIDString));
                            return;
                        }

                        foreach (var bet in duelsData.ClaimBets[player.UserIDString].ToList())
                        {
                            var item = ItemManager.CreateByItemID(bet.itemid, bet.amount);

                            if (item == null)
                            {
                                continue;
                            }

                            if (!item.MoveToContainer(player.inventory.containerMain, -1))
                            {
                                var position = player.transform.position;
                                item.Drop(position + new Vector3(0f, 1f, 0f) + position / 2f, (position + new Vector3(0f, 0.2f, 0f)) * 8f); // Credit: Slack comment by @visagalis
                            }

                            string message = msg("PlayerClaimedBet", player.UserIDString, item.info.displayName.translated, item.amount);

                            Message(player, message);
                            Puts("{0} ({1}) - {2}", player.displayName, player.UserIDString, message);
                            duelsData.ClaimBets[player.UserIDString].Remove(bet);

                            if (duelsData.ClaimBets[player.UserIDString].Count == 0)
                            {
                                duelsData.ClaimBets.Remove(player.UserIDString);
                                Message(player, msg("AllBetsClaimed", player.UserIDString));
                            }
                        }
                        return;
                    }
                case "queue":
                case "que":
                case "q":
                    {
                        if (!string.IsNullOrEmpty(szQueueChatCommand))
                            cmdQueue(player, command, args);
                        return;
                    }
                case "chat":
                    {
                        if (broadcastDefeat)
                        {
                            if (!duelsData.Chat.Contains(player.UserIDString))
                                duelsData.Chat.Add(player.UserIDString);
                            else
                                duelsData.Chat.Remove(player.UserIDString);

                            Message(player, msg(duelsData.Chat.Contains(player.UserIDString) ? "DuelChatOff" : "DuelChatOn", player.UserIDString));
                        }
                        else
                        {
                            if (!duelsData.ChatEx.Contains(player.UserIDString))
                                duelsData.ChatEx.Add(player.UserIDString);
                            else
                                duelsData.ChatEx.Remove(player.UserIDString);

                            Message(player, msg(duelsData.ChatEx.Contains(player.UserIDString) ? "DuelChatOn" : "DuelChatOff", player.UserIDString));
                        }
                        return;
                    }
                case "kit":
                    {
                        string kits = string.Join(", ", VerifiedKits.ToArray());

                        if (args.Length == 2 && !string.IsNullOrEmpty(kits))
                        {
                            string kit = GetVerifiedKit(args[1]);

                            if (!string.IsNullOrEmpty(kit))
                            {
                                duelsData.CustomKits[player.UserIDString] = kit;
                                Message(player, msg("KitSet", player.UserIDString, kit));
                            }
                            else
                                Message(player, msg("KitDoesntExist", player.UserIDString, args[1]));

                            return;
                        }

                        if (duelsData.CustomKits.ContainsKey(player.UserIDString))
                        {
                            duelsData.CustomKits.Remove(player.UserIDString);
                            Message(player, msg("ResetKit", player.UserIDString));
                        }

                        Message(player, string.IsNullOrEmpty(kits) ? msg("KitsNotConfigured", player.UserIDString) : "Kits: " + kits);
                        return;
                    }
                case "allow":
                    {
                        if (!duelsData.Allowed.Contains(player.UserIDString))
                        {
                            duelsData.Allowed.Add(player.UserIDString);
                            Message(player, msg("PlayerRequestsOn", player.UserIDString));
                            return;
                        }

                        duelsData.Allowed.Remove(player.UserIDString);
                        Message(player, msg("PlayerRequestsOff", player.UserIDString));
                        RemoveRequests(player);
                        return;
                    }
                case "block":
                    {
                        if (args.Length >= 2)
                        {
                            var target = BasePlayer.Find(args[1]);

                            if (IsNull(target))
                            {
                                Message(player, msg("PlayerNotFound", player.UserIDString, args[1]));
                                return;
                            }

                            if (!duelsData.BlockedUsers.ContainsKey(player.UserIDString))
                            {
                                duelsData.BlockedUsers.Add(player.UserIDString, new List<string>());
                            }
                            else if (duelsData.BlockedUsers[player.UserIDString].Contains(target.UserIDString))
                            {
                                duelsData.BlockedUsers[player.UserIDString].Remove(target.UserIDString);

                                if (duelsData.BlockedUsers[player.UserIDString].Count == 0)
                                    duelsData.BlockedUsers.Remove(player.UserIDString);

                                Message(player, msg("UnblockedRequestsFrom", player.UserIDString, target.displayName));
                                return;
                            }

                            duelsData.BlockedUsers[player.UserIDString].Add(target.UserIDString);
                            Message(player, msg("BlockedRequestsFrom", player.UserIDString, target.displayName));
                            return;
                        }

                        if (duelsData.Allowed.Contains(player.UserIDString))
                        {
                            duelsData.Allowed.Remove(player.UserIDString);
                            Message(player, msg("PlayerRequestsOff", player.UserIDString));
                            RemoveRequests(player);
                            return;
                        }

                        Message(player, msg("AlreadyBlocked", player.UserIDString));
                        return;
                    }
                case "bet":
                    {
                        if (!allowBets)
                        {
                            Message(player, "Betting is disabled.");
                            break;
                        }

                        if (duelingBets.Count == 0)
                        {
                            Message(player, msg("NoBetsConfigured", player.UserIDString));
                            return;
                        }

                        if (args.Length == 2)
                        {
                            switch (args[1].ToLower())
                            {
                                case "refundall":
                                    {
                                        if (player.IsAdmin)
                                        {
                                            if (duelsData.Bets.Count == 0)
                                            {
                                                Message(player, msg("NoBetsToRefund", player.UserIDString));
                                                return;
                                            }

                                            foreach (var kvp in duelsData.Bets.ToList())
                                            {
                                                var target = BasePlayer.Find(kvp.Key);
                                                if (target == null) continue;

                                                Item item = ItemManager.CreateByItemID(kvp.Value.itemid, kvp.Value.amount);

                                                if (item == null)
                                                    continue;

                                                if (!item.MoveToContainer(target.inventory.containerMain, -1, true) && !item.MoveToContainer(target.inventory.containerBelt, -1, true))
                                                {
                                                    item.Remove(0.01f);
                                                    continue;
                                                }

                                                Message(target, msg("RefundAllPlayerNotice", target.UserIDString, item.info.displayName.translated, item.amount));
                                                Message(player, msg("RefundAllAdminNotice", player.UserIDString, target.displayName, target.UserIDString, item.info.displayName.english, item.amount));
                                                duelsData.Bets.Remove(kvp.Key);
                                            }

                                            if (duelsData.Bets.Count > 0) Message(player, msg("BetsRemaining", player.UserIDString, duelsData.Bets.Count));
                                            else Message(player, msg("AllBetsRefunded", player.UserIDString));
                                            SaveData();
                                            return;
                                        }

                                        break;
                                    }
                                case "forfeit":
                                    {
                                        if (allowBetRefund) // prevent operator error ;)
                                        {
                                            cmdDuel(player, command, new[] { "bet", "refund" });
                                            return;
                                        }

                                        if (!allowBetForfeit)
                                        {
                                            Message(player, msg("CannotForfeit", player.UserIDString));
                                            return;
                                        }

                                        if (duelsData.Bets.ContainsKey(player.UserIDString))
                                        {
                                            if (dataRequests.ContainsKey(player.UserIDString) || dataRequests.ContainsValue(player.UserIDString))
                                            {
                                                Message(player, msg("CannotForfeitRequestDuel", player.UserIDString));
                                                return;
                                            }

                                            if (dataDuelists.ContainsKey(player.UserIDString))
                                            {
                                                Message(player, msg("CannotForfeitInDuel", player.UserIDString));
                                                return;
                                            }

                                            duelsData.Bets.Remove(player.UserIDString);
                                            Message(player, msg("BetForfeit", player.UserIDString));
                                            SaveData();
                                        }
                                        else
                                            Message(player, msg("NoBetToForfeit", player.UserIDString));

                                        return;
                                    }
                                case "cancel":
                                case "refund":
                                    {
                                        if (!allowBetRefund && !player.IsAdmin)
                                        {
                                            Message(player, msg("CannotRefund", player.UserIDString));
                                            return;
                                        }

                                        if (duelsData.Bets.ContainsKey(player.UserIDString))
                                        {
                                            if (dataRequests.ContainsKey(player.UserIDString) || dataRequests.ContainsValue(player.UserIDString))
                                            {
                                                Message(player, msg("CannotRefundRequestDuel", player.UserIDString));
                                                return;
                                            }

                                            if (dataDuelists.ContainsKey(player.UserIDString))
                                            {
                                                Message(player, msg("CannotRefundInDuel", player.UserIDString));
                                                return;
                                            }

                                            var bet = duelsData.Bets[player.UserIDString];

                                            Item item = ItemManager.CreateByItemID(bet.itemid, bet.amount);

                                            if (!item.MoveToContainer(player.inventory.containerMain, -1, true))
                                            {
                                                if (!item.MoveToContainer(player.inventory.containerBelt, -1, true))
                                                {
                                                    var position = player.transform.position;
                                                    item.Drop(position + new Vector3(0f, 1f, 0f) + position / 2f, (position + new Vector3(0f, 0.2f, 0f)) * 8f); // Credit: Slack comment by @visagalis
                                                }
                                            }

                                            duelsData.Bets.Remove(player.UserIDString);
                                            Message(player, msg("BetRefunded", player.UserIDString));
                                            SaveData();
                                        }
                                        else
                                            Message(player, msg("NoBetToRefund", player.UserIDString));

                                        return;
                                    }
                                default:
                                    break;
                            }
                        }

                        if (duelsData.Bets.ContainsKey(player.UserIDString))
                        {
                            var bet = duelsData.Bets[player.UserIDString];

                            Message(player, msg("AlreadyBetting", player.UserIDString, bet.trigger, bet.amount));

                            if (allowBetRefund)
                                Message(player, msg("ToRefundUse", player.UserIDString, szDuelChatCommand));
                            else if (allowBetForfeit)
                                Message(player, msg("ToForfeitUse", player.UserIDString, szDuelChatCommand));

                            return;
                        }

                        if (args.Length < 3)
                        {
                            Message(player, msg("AvailableBets", player.UserIDString));

                            foreach (var betInfo in duelingBets)
                                Message(player, string.Format("{0} (max: {1})", betInfo.trigger, betInfo.max));

                            Message(player, msg("BetSyntax", player.UserIDString, szDuelChatCommand));
                            return;
                        }

                        int betAmount;
                        if (!int.TryParse(args[2], out betAmount))
                        {
                            Message(player, msg("InvalidNumber", player.UserIDString, args[2]));
                            return;
                        }

                        if (betAmount > 500 && betAmount % 500 != 0)
                        {
                            Message(player, msg("MultiplesOnly", player.UserIDString));
                            return;
                        }

                        foreach (var betInfo in duelingBets)
                        {
                            if (betInfo.trigger.ToLower() == args[1].ToLower())
                            {
                                CreateBet(player, betAmount, betInfo);
                                return;
                            }
                        }

                        Message(player, msg("InvalidBet", player.UserIDString, args[1]));
                        return;
                    }
                case "accept":
                case "a":
                case "y":
                case "yes":
                    {
                        if (!autoAllowAll && !duelsData.Allowed.Contains(player.UserIDString))
                        {
                            Message(player, msg("MustAllowDuels", player.UserIDString, szDuelChatCommand));
                            return;
                        }

                        if (InEvent(player))
                        {
                            Message(player, msg("AlreadyDueling", player.UserIDString));
                            return;
                        }

                        if (!dataRequests.ContainsValue(player.UserIDString))
                        {
                            Message(player, msg("NoRequestsReceived", player.UserIDString));
                            return;
                        }

                        if (!IsNewman(player))
                        {
                            Message(player, msg("MustBeNaked", player.UserIDString));
                            return;
                        }

                        BasePlayer target = null;

                        foreach (var kvp in dataRequests)
                        {
                            if (kvp.Value == player.UserIDString)
                            {
                                target = BasePlayer.Find(kvp.Key);

                                if (target == null || !target.IsConnected)
                                {
                                    Message(player, string.Format("DuelCancelledFor", player.UserIDString, GetDisplayName(kvp.Key)));
                                    dataRequests.Remove(kvp.Key);
                                    return;
                                }

                                break;
                            }
                        }

                        if (!IsNewman(target))
                        {
                            Message(player, msg("TargetMustBeNaked", player.UserIDString));
                            Message(target, msg("MustBeNaked", target.UserIDString));
                            return;
                        }

                        duelsData.Restricted.Remove(player.UserIDString);
                        duelsData.Restricted.Remove(target.UserIDString);

                        if (!SelectZone(player, target))
                        {
                            Message(player, msg("AllZonesFull", player.UserIDString, duelingZones.Count, playersPerZone));
                            Message(target, msg("AllZonesFull", target.UserIDString, duelingZones.Count, playersPerZone));
                        }

                        return;
                    }
                case "cancel":
                case "decline":
                    {
                        if (IsDueling(player))
                            return;

                        if (!autoAllowAll && !duelsData.Allowed.Contains(player.UserIDString))
                        {
                            Message(player, msg("MustAllowDuels", player.UserIDString, szDuelChatCommand));
                            return;
                        }

                        var entry = dataRequests.FirstOrDefault(kvp => dataRequests.ContainsKey(player.UserIDString) ? kvp.Key == player.UserIDString : kvp.Value == player.UserIDString);

                        if (!string.IsNullOrEmpty(entry.Key))
                        {
                            var target = BasePlayer.Find(entry.Key) ?? BasePlayer.Find(entry.Value);

                            if (target != null)
                                Message(target, msg("DuelCancelledWith", target.UserIDString, player.displayName));

                            Message(player, msg("DuelCancelComplete", player.UserIDString));
                            dataRequests.Remove(entry.Key);
                            return;
                        }

                        Message(player, msg("NoPendingRequests", player.UserIDString));
                        return;
                    }
                default:
                    {
                        if (!autoAllowAll && !duelsData.Allowed.Contains(player.UserIDString))
                        {
                            Message(player, msg("MustAllowDuels", player.UserIDString, szDuelChatCommand));
                            return;
                        }

                        if (IsDueling(player))
                        {
                            Message(player, msg("AlreadyDueling", player.UserIDString));
                            return;
                        }

                        if (duelsData.Restricted.Contains(player.UserIDString) && !player.IsAdmin)
                        {
                            Message(player, msg("MustWaitToRequestAgain", player.UserIDString, 1));
                            return;
                        }

                        if (!IsNewman(player))
                        {
                            Message(player, msg("MustBeNaked", player.UserIDString));
                            return;
                        }

                        var target = BasePlayer.Find(args[0]);

                        if (target == null || target == player) //if (target == null || (target == player && target.userID != 76561198212544308))
                        {
                            Message(player, msg("PlayerNotFound", player.UserIDString, args[0]));
                            return;
                        }

                        if (duelsData.BlockedUsers.ContainsKey(target.UserIDString) && duelsData.BlockedUsers[target.UserIDString].Contains(player.UserIDString))
                        {
                            Message(player, msg("CannotRequestThisPlayer", player.UserIDString));
                            return;
                        }

                        if (IsDueling(target))
                        {
                            Message(player, msg("TargetAlreadyDueling", player.UserIDString, target.displayName));
                            return;
                        }

                        if (!autoAllowAll && !duelsData.Allowed.Contains(target.UserIDString))
                        {
                            Message(player, msg("NotAllowedYet", player.UserIDString, target.displayName, szDuelChatCommand));
                            return;
                        }

                        if (dataRequests.ContainsKey(player.UserIDString))
                        {
                            Message(player, msg("MustWaitForAccept", player.UserIDString, GetDisplayName(dataRequests[player.UserIDString])));
                            return;
                        }

                        if (dataRequests.ContainsValue(target.UserIDString))
                        {
                            Message(player, msg("PendingRequestAlready", player.UserIDString));
                            return;
                        }

                        if (duelsData.Bets.ContainsKey(player.UserIDString) && !duelsData.Bets.ContainsKey(target.UserIDString))
                        {
                            var bet = duelsData.Bets[player.UserIDString];

                            Message(player, msg("TargetHasNoBet", player.UserIDString, target.displayName));
                            Message(player, msg("YourBet", player.UserIDString, bet.trigger, bet.amount));
                            return;
                        }

                        if (duelsData.Bets.ContainsKey(target.UserIDString) && !duelsData.Bets.ContainsKey(player.UserIDString))
                        {
                            var targetBet = duelsData.Bets[target.UserIDString];
                            Message(player, msg("MustHaveSameBet", player.UserIDString, target.displayName, targetBet.trigger, targetBet.amount));
                            return;
                        }

                        if (duelsData.Bets.ContainsKey(player.UserIDString) && duelsData.Bets.ContainsKey(target.UserIDString))
                        {
                            var playerBet = duelsData.Bets[player.UserIDString];
                            var targetBet = duelsData.Bets[target.UserIDString];

                            if (!playerBet.Equals(targetBet))
                            {
                                Message(player, msg("BetsDoNotMatch", player.UserIDString, playerBet.trigger, playerBet.amount, targetBet.trigger, targetBet.amount));
                                return;
                            }
                        }

                        if (args.Length > 1)
                            SetPlayerZone(player, args.Skip(1));

                        dataRequests.Add(player.UserIDString, target.UserIDString);
                        Message(target, msg("DuelRequestReceived", target.UserIDString, player.displayName, szDuelChatCommand));
                        Message(player, msg("DuelRequestSent", player.UserIDString, target.displayName, szDuelChatCommand));

                        if (RemoveFromQueue(player.UserIDString))
                            Message(player, msg("RemovedFromQueueRequest", player.UserIDString));

                        string targetName = target.displayName;
                        string playerId = player.UserIDString;

                        if (!duelsData.Restricted.Contains(playerId))
                            duelsData.Restricted.Add(playerId);

                        timer.In(60f, () =>
                        {
                            duelsData.Restricted.Remove(playerId);

                            if (dataRequests.ContainsKey(playerId))
                            {
                                if (player != null && !IsDueling(player))
                                    Message(player, msg("RequestTimedOut", playerId, targetName));

                                dataRequests.Remove(playerId);
                            }
                        });

                        break;
                    }
            } // end switch
        }

        public DuelingZone GetPlayerZone(BasePlayer player, int size)
        {
            if (playerZones.ContainsKey(player.UserIDString))
            {
                var kvp = duelsData.DuelZones.FirstOrDefault(entry => entry.Value.Equals(playerZones[player.UserIDString], StringComparison.OrdinalIgnoreCase));

                playerZones.Remove(player.UserIDString);

                if (!string.IsNullOrEmpty(kvp.Key))
                {
                    var zone = duelingZones.FirstOrDefault(x => x.Position.ToString() == kvp.Key);

                    if (size > 2)
                        return zone == null || zone.IsLocked || zone.Spawns.Count < (requireTeamSize ? size * 2 : 2) ? null : zone;

                    return zone == null || zone.IsLocked || zone.Spawns.Count < requiredMinSpawns || zone.Spawns.Count > requiredMaxSpawns ? null : zone;
                }
            }

            return null;
        }

        private List<ulong> _times = new List<ulong>();

        public void SetPlayerTime(BasePlayer player, bool set)
        {
            if (!setPlayerTime || !set && !_times.Remove(player.userID))
            {
                return;
            }

            var time = set ? "12" : "-1";

            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), $"setenv {player.UserIDString} time {time}");

            if (set) _times.Add(player.userID);
        }

        public bool SetPlayerZone(BasePlayer player, string[] args)
        {
            foreach (string arg in args)
            {
                if (duelsData.DuelZones.Values.Exists(zoneName => zoneName.Equals(arg, StringComparison.OrdinalIgnoreCase)))
                {
                    string zoneName = duelsData.DuelZones.Values.FirstOrDefault(x => x.Equals(arg, StringComparison.OrdinalIgnoreCase));
                    Message(player, msg("ZoneSet", player.UserIDString, zoneName));
                    playerZones[player.UserIDString] = zoneName;
                    return true;
                }
            }

            return false;
        }

        public void SetupTeams(BasePlayer player, BasePlayer target)
        {
            if (!IsNewman(player))
            {
                Message(player, msg("MustBeNaked", player.UserIDString));
                Message(target, msg("DuelMustBeNaked", target.UserIDString, player.displayName));
                return;
            }

            if (!IsNewman(target))
            {
                Message(target, msg("MustBeNaked", target.UserIDString));
                Message(player, msg("DuelMustBeNaked", player.UserIDString, target.displayName));
                return;
            }

            RemoveFromQueue(player.UserIDString);
            RemoveFromQueue(target.UserIDString);

            var match = new GoodVersusEvilMatch(this);
            match.Setup(player, target);

            SubscribeHooks(true);
        }

        private void ResetTemporaryData() // keep our datafile cleaned up by removing entries which are temporary
        {
            if (duelsData == null)
                duelsData = new StoredData();

            dataDuelists.Clear();
            dataRequests.Clear();
            dataImmunity.Clear();
            dataImmunitySpawns.Clear();
            duelsData.Restricted.Clear();
            dataDeath.Clear();
            duelsData.Queued.Clear();
            duelsData.Homes.Clear();
            duelsData.Kits.Clear();
            SaveData();
        }

        public DuelingZone RemoveDuelist(string playerId)
        {
            foreach (var zone in duelingZones)
            {
                if (zone.HasPlayer(playerId))
                {
                    zone.RemovePlayer(playerId);
                    return zone;
                }
            }

            return null;
        }

        public void ResetDuelist(string targetId, bool removeHome = true) // remove a dueler from the datafile
        {
            duelsData.Kits.Remove(targetId);
            duelsData.Restricted.Remove(targetId);
            dataImmunity.Remove(targetId);
            dataImmunitySpawns.Remove(targetId);
            dataDuelists.Remove(targetId);
            dataRequests.Remove(targetId);
            dataDeath.Remove(targetId);

            if (removeHome)
                duelsData.Homes.Remove(targetId);

            if (duelingZones.Count > 0)
                RemoveDuelist(targetId);

            RemoveFromQueue(targetId);
        }

        private void RemoveZeroStats() // someone enabled duels but never joined one. remove them to keep the datafile cleaned up
        {
            foreach (string targetId in duelsData.Allowed.ToList())
            {
                if (!duelsData.Losses.ContainsKey(targetId) && !duelsData.Victories.ContainsKey(targetId)) // no permanent stats
                {
                    ResetDuelist(targetId);
                    duelsData.Allowed.Remove(targetId);
                }
            }
        }

        public void SetupZoneManager()
        {
            var zoneIds = ZoneManager?.Call("GetZoneIDs");

            if (zoneIds != null && zoneIds is string[])
            {
                foreach (var zoneId in (string[])zoneIds)
                {
                    var zoneLoc = ZoneManager?.Call("GetZoneLocation", zoneId);

                    if (zoneLoc is Vector3 && (Vector3)zoneLoc != Vector3.zero)
                    {
                        var position = (Vector3)zoneLoc;
                        var radius = ZoneManager?.Call("GetZoneRadius", zoneId);
                        float distance = 0f;

                        if (radius is float && (float)radius > 0f)
                        {
                            distance = (float)radius;
                        }
                        else
                        {
                            var zoneSize = ZoneManager?.Call("GetZoneSize", zoneId);

                            if (zoneSize is Vector3 && (Vector3)zoneSize != Vector3.zero)
                            {
                                var size = (Vector3)zoneSize;
                                distance = Mathf.Max(size.x, size.y);
                            }
                        }

                        if (distance > 0f)
                        {
                            distance += zoneRadius + 5f;
                            managedZones[position] = distance;
                        }
                    }
                }
            }
        }

        public void SetupZones()
        {
            if (duelsData.ZoneIds.Count > 0)
            {
                foreach (string id in duelsData.ZoneIds)
                {
                    duelsData.DuelZones[id] = GetZoneName();
                }

                duelsData.ZoneIds.Clear();
                SaveData();
            }

            if (duelsData.DuelZones.Count > zoneAmount) // zoneAmount was changed in the config file so remove existing zones until we're at the new cap
            {
                int removed = 0;

                do
                {
                    string zoneId = duelsData.DuelZones.FirstOrDefault().Key;
                    var zonePos = zoneId.ToVector3();

                    if (spAutoRemove && duelsData.Spawns.Count > 0)
                        foreach (string spawn in duelsData.Spawns.ToList())
                            if (Vector3.Distance(spawn.ToVector3(), zonePos) <= zoneRadius)
                                duelsData.Spawns.Remove(spawn);

                    duelsData.AutoGeneratedSpawns.Remove(zoneId);
                    duelsData.DuelZones.Remove(zoneId);
                    removed += RemoveZoneWalls(GetOwnerId(zoneId));
                } while (duelsData.DuelZones.Count > zoneAmount);

                if (removed > 0)
                    Puts(msg("RemovedXWallsCustom", null, removed));
            }

            var entities = autoSetup && (duelsData.DuelZones.Count < zoneAmount || duelsData.DuelZones.Count > 0) ? GetWallEntities() : null; // don't cache if we don't need to

            foreach (var entry in duelsData.DuelZones) // create all zones that don't already exist
                SetupDuelZone(entry.Key.ToVector3(), entities, entry.Value);

            if (autoSetup && duelsData.DuelZones.Count < zoneAmount) // create each dueling zone that is missing. if this fails then console will be notified
            {
                int attempts = Math.Max(zoneAmount, 5); // 0.1.10 fix - infinite loop fix for when zone radius is too large to fit on the map
                int created = 0;

                do
                {
                    if (SetupDuelZone(entities, GetZoneName()) != Vector3.zero)
                        created++;
                } while (duelsData.DuelZones.Count < zoneAmount && --attempts > 0);

                if (attempts <= 0)
                {
                    if (created > 0)
                        Puts(msg("SupportCreated", null, created));
                    else
                        Puts(msg("SupportInvalidConfig"));
                }
            }

            if (duelingZones.Count > 0)
                Puts(msg("ZonesSetup", null, duelingZones.Count));
        }

        public Vector3 SetupDuelZone(List<BaseEntity> entities, string zoneName) // starts the process of creating a new or existing zone and then setting up it's own spawn points around the circumference of the zone
        {
            var zonePos = FindDuelingZone(); // a complex process to search the map for a suitable area

            if (zonePos == Vector3.zero) // unfortunately we weren't able to find a location. this is likely due to an extremely high entity count. just try again.
                return Vector3.zero;

            SetupDuelZone(zonePos, entities, zoneName);
            return zonePos;
        }

        public DuelingZone SetupDuelZone(Vector3 zonePos, List<BaseEntity> entities, string zoneName)
        {
            if (!duelsData.DuelZones.ContainsKey(zonePos.ToString()))
                duelsData.DuelZones.Add(zonePos.ToString(), zoneName);

            var newZone = new GameObject().AddComponent<DuelingZone>();

            newZone.Setup(zonePos);
            duelingZones.Add(newZone);

            if (duelingZones.Count == 1)
            {
                if (blockSpawning) Subscribe(nameof(OnPlayerRespawned));
                Subscribe(nameof(OnEntityTakeDamage));
                Subscribe(nameof(OnEntitySpawned));
                Subscribe(nameof(CanBuild));
            }

            CreateZoneWalls(newZone.Position, zoneRadius, zoneUseWoodenWalls ? hewwPrefab : heswPrefab, entities);
            return newZone;
        }

        public List<BaseEntity> GetWallEntities()
        {
            var entities = new List<BaseEntity>();

            foreach (var e in BaseNetworkable.serverEntities)
            {
                if (e != null && e.ShortPrefabName.Contains("wall.external.high"))
                {
                    var entity = e as BaseEntity;

                    if (entity != null)
                    {
                        entities.Add(entity);
                    }
                }
            }

            return entities;
        }

        public int RemoveZoneWalls(ulong ownerId)
        {
            int removed = 0;

            foreach (var entity in GetWallEntities().ToList())
            {
                if (entity.OwnerID == ownerId)
                {
                    entity.Kill();
                    removed++;
                }
            }

            return removed;
        }

        public bool ZoneWallsExist(ulong ownerId, List<BaseEntity> entities)
        {
            if (entities == null || entities.Count < 3)
                entities = GetWallEntities();

            return entities.Exists(entity => entity.OwnerID == ownerId);
        }

        public void CreateZoneWalls(Vector3 center, float zoneRadius, string prefab, List<BaseEntity> entities, BasePlayer player = null)
        {
            if (!useZoneWalls)
                return;

            var tick = DateTime.Now;
            ulong ownerId = GetOwnerId(center.ToString());

            if (ZoneWallsExist(ownerId, entities))
                return;

            float maxHeight = -200f;
            float minHeight = 200f;
            int spawned = 0;
            int raycasts = Mathf.CeilToInt(360 / zoneRadius * 0.1375f);

            foreach (var position in GetCircumferencePositions(center, zoneRadius, raycasts, 0f)) // get our positions and perform the calculations for the highest and lowest points of terrain
            {
                RaycastHit hit;
                if (Physics.Raycast(new Vector3(position.x, position.y + 200f, position.z), Vector3.down, out hit, Mathf.Infinity, wallMask))
                {
                    maxHeight = Mathf.Max(hit.point.y, maxHeight); // calculate the highest point of terrain
                    minHeight = Mathf.Min(hit.point.y, minHeight); // calculate the lowest point of terrain
                    center.y = minHeight; // adjust the spawn point of our walls to that of the lowest point of terrain
                }
            }

            float gap = prefab == heswPrefab ? 0.3f : 0.5f; // the distance used so that each wall fits closer to the other so players cannot throw items between the walls
            int stacks = Mathf.CeilToInt((maxHeight - minHeight) / 6f) + extraWallStacks; // get the amount of walls to stack onto each other to go above the highest point
            float next = 360 / zoneRadius - gap; // the distance apart each wall will be from the other

            for (int i = 0; i < stacks; i++) // create our loop to spawn each stack
            {
                foreach (var position in GetCircumferencePositions(center, zoneRadius, next, center.y)) // get a list positions where each positions difference is the width of a high external stone wall. specify the height since we've already calculated what's required
                {
                    float groundHeight = TerrainMeta.HeightMap.GetHeight(new Vector3(position.x, position.y + 6f, position.z));

                    if (groundHeight > position.y + 9f) // 0.1.13 improved distance check underground
                        continue;

                    if (useLeastAmount && position.y - groundHeight > 6f + extraWallStacks * 6f)
                        continue;

                    var entity = GameManager.server.CreateEntity(prefab, position, default(Quaternion), false);

                    if (entity != null)
                    {
                        entity.OwnerID = ownerId; // set a unique identifier so the walls can be easily removed later
                        entity.transform.LookAt(center, Vector3.up); // have each wall look at the center of the zone
                        entity.Spawn(); // spawn into the game
                        entity.gameObject.SetActive(true); // 0.1.16: fix for animals and explosives passing through walls. set it active after it spawns otherwise AntiHack will throw ProjectileHack: Line of sight warnings each time the entity is hit
                        spawned++; // our counter
                    }
                    else
                        return; // invalid prefab, return or cause massive server lag

                    if (stacks == i - 1)
                    {
                        RaycastHit hit;
                        if (Physics.Raycast(new Vector3(position.x, position.y + 6f, position.z), Vector3.down, out hit, 12f, worldMask))
                            stacks++; // 0.1.16 fix where rocks could allow a boost in or out of the top of a zone
                    }
                }

                center.y += 6f; // increase the positions height by one high external stone wall's height
            }

            if (player == null)
                Puts(msg("GeneratedWalls", null, spawned, stacks, FormatPosition(center), (DateTime.Now - tick).TotalSeconds));
            else
                Message(player, msg("GeneratedWalls", player.UserIDString, spawned, stacks, FormatPosition(center), (DateTime.Now - tick).TotalSeconds));

            Subscribe(nameof(OnEntityTakeDamage));
        }

        public void EjectPlayers(DuelingZone zone)
        {
            foreach (var player in zone.Players)
            {
                EjectPlayer(player);
            }
        }

        public void EjectPlayer(BasePlayer player)
        {
            if (player == null)
                return;

            player.inventory.Strip();
            ResetDuelist(player.UserIDString, false);
            SendHome(player);
        }

        public void RemoveDuelZone(DuelingZone zone)
        {
            string uid = zone.Position.ToString();

            foreach (string playerId in spectators.ToList())
            {
                var player = BasePlayer.Find(playerId);

                if (player == null)
                {
                    spectators.Remove(playerId);
                    continue;
                }

                if (zone.Distance(player.transform.position) <= zoneRadius)
                {
                    EndSpectate(player);
                    SendHome(player);
                }
            }

            var match = tdmMatches.FirstOrDefault(x => x.Zone != null && x.Zone == zone);

            if (match != null)
                match.End();

            if (spAutoRemove && duelsData.Spawns.Count > 0)
                foreach (var spawn in zone.Spawns)
                    duelsData.Spawns.Remove(spawn.ToString());

            duelsData.DuelZones.Remove(uid);
            duelsData.AutoGeneratedSpawns.Remove(uid);
            RemoveEntities(zone);
            RemoveZoneWalls(GetOwnerId(uid));
            zone.Kill();

            if (duelingZones.Count == 0 && tdmMatches.Count == 0)
            {
                SubscribeHooks(false);
                CheckZoneHooks();
            }
        }

        public void RemoveEntities(ulong playerId)
        {
            if (duelEntities.ContainsKey(playerId))
            {
                foreach (var e in duelEntities[playerId].ToList())
                    e.SafelyKill();

                duelEntities.Remove(playerId);
            }
        }

        public void RemoveEntities(DuelingZone zone)
        {
            foreach (var entry in duelEntities.ToList())
            {
                foreach (var entity in entry.Value.ToList())
                {
                    if (entity.IsKilled())
                    {
                        duelEntities[entry.Key].Remove(entity);
                        continue;
                    }

                    if (zone.Distance(entity.transform.position) <= zoneRadius + 1f)
                    {
                        duelEntities[entry.Key].Remove(entity);
                        entity.Kill();
                    }
                }
            }
        }

        public DuelingZone GetDuelZone(Vector3 startPos, float offset = 1f)
        {
            return duelingZones.FirstOrDefault(zone => zone.Distance(startPos) <= zoneRadius + offset);
        }

        public void SendHome(BasePlayer player) // send a player home to the saved location that they joined from
        {
            if (player != null && duelsData.Homes.ContainsKey(player.UserIDString))
            {
                if (player.IsDead() && !player.IsConnected && !respawnDeadDisconnect)
                {
                    duelsData.Homes.Remove(player.UserIDString);
                    return;
                }

                if (player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
                {
                    timer.Once(2f, () => SendHome(player));
                    return;
                }

                RemoveEntities(player.userID);
                var homePos = duelsData.Homes[player.UserIDString].ToVector3();

                if (DuelTerritory(homePos) && !player.IsAdmin)
                {
                    var bags = SleepingBag.FindForPlayer(player.userID, true).ToList();

                    if (bags.Count > 0)
                    {
                        bags.Sort((x, y) => x.net.ID.Value.CompareTo(y.net.ID.Value));
                        homePos = bags[0].transform.position;
                        homePos.y += 0.25f;
                    }
                    else
                    {
                        homePos = ServerMgr.FindSpawnPoint().pos;
                    }
                }

                if (player.IsDead())
                {
                    if (sendDeadHome)
                        player.RespawnAt(homePos, default(Quaternion));
                    else player.Respawn();
                }
                else
                {
                    if (!sendDeadHome)
                    {
                        player.LifeStoryEnd();
                        player.Respawn();
                    }
                    else Teleport(player, homePos);
                }

                if (playerHealth > 0f)
                    player.health = playerHealth;

                GiveRespawnLoot(player);
                duelsData.Homes.Remove(player.UserIDString);

                if (guiAutoEnable || createUI.Contains(player.UserIDString))
                    OnPlayerConnected(player);

                if (readyUiList.Contains(player.UserIDString))
                    ToggleReadyUI(player);
            }
        }

        public void GiveRespawnLoot(BasePlayer player)
        {
            if (respawnLoot.Count > 0)
            {
                player.inventory.Strip();

                foreach (var entry in respawnLoot)
                {
                    Item item = ItemManager.CreateByName(entry.shortname, entry.amount, entry.skin);

                    if (item == null)
                        continue;

                    var container = entry.container == "wear" ? player.inventory.containerWear : entry.container == "belt" ? player.inventory.containerBelt : player.inventory.containerMain;

                    if (!item.MoveToContainer(container, entry.slot))
                    {
                        item.Remove(0f);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(autoKitName) && Kits.CanCall() && IsKit(autoKitName))
            {
                player.inventory.Strip();
                Kits.Call("GiveKit", player, autoKitName);
            }
            else plugins.Find("Loadoutless")?.Call("OnPlayerRespawned", player);
        }

        private void UpdateStability()
        {
            if (noStability)
            {
                Subscribe(nameof(OnEntitySpawned));

                foreach (BuildingBlock block in BaseNetworkable.serverEntities.OfType<BuildingBlock>().Where(e => DuelTerritory(e.transform.position)).ToList())
                {
                    if (block.grounded)
                        continue;

                    if (block.OwnerID == 0 || permission.UserHasGroup(block.OwnerID.ToString(), "admin"))
                        block.grounded = true;
                }
            }
        }

        private void CheckZoneHooks(bool message = false)
        {
            if (respawnWalls && duelingZones.Count > 0)
            {
                Subscribe(nameof(OnEntityDeath));
                Subscribe(nameof(OnEntityKill));
            }
        }

        private void CheckDuelistMortality()
        {
            eventTimer = timer.Once(0.5f, CheckDuelistMortality);

            if (dataImmunity.Count > 0) // each player that spawns into a dueling zone is given immunity for X seconds. here we'll keep track of this and remove their immunities
            {
                var timeStamp = TimeStamp();

                foreach (var kvp in dataImmunity.ToList())
                {
                    var player = BasePlayer.Find(kvp.Key);
                    long time = kvp.Value - timeStamp;

                    if (time <= 0)
                    {
                        dataImmunity.Remove(kvp.Key);
                        dataImmunitySpawns.Remove(kvp.Key);

                        if (IsNotConnected(player))
                            continue;

                        CuiHelper.DestroyUi(player, "DuelistUI_Countdown");
                        Message(player, msg("ImmunityFaded", player.UserIDString));
                    }
                    else if (player != null && player.IsConnected)
                    {
                        if (noMovement && dataImmunitySpawns.ContainsKey(player.UserIDString))
                        {
                            var dest = dataImmunitySpawns[player.UserIDString];
                            player.Teleport(dest);
                        }

                        CreateCountdownUI(player, time.ToString());
                    }
                }
            }

            if (dataDeath.Count > 0) // keep track of how long the match has been going on for, and if it's been too long then kill the player off.
            {
                var timeStamp = TimeStamp();

                foreach (var kvp in dataDeath.ToList())
                {
                    if (kvp.Value - timeStamp <= 0)
                    {
                        var target = BasePlayer.Find(kvp.Key);
                        dataDeath.Remove(kvp.Key);

                        if (IsNotConnected(target) || (!IsDueling(target) && !InDeathmatch(target)))
                            continue;

                        target.inventory.Strip();
                        OnEntityDeath(target, null);
                    }
                }
            }

            UpdateMatchUI();
        }

        public void SubscribeHooks(bool flag) // we're using lots of temporary and permanent hooks so we'll turn off the temporary hooks when the plugin is loaded, and unsubscribe to others inside of their hooks when they're no longer in use
        {
            if (!flag)
            {
                Unsubscribe(nameof(OnPlayerDisconnected));
                //Unsubscribe(nameof(CanNetworkTo));
                Unsubscribe(nameof(OnItemDropped));
                Unsubscribe(nameof(OnPlayerSleepEnded));
                Unsubscribe(nameof(OnCreateWorldProjectile));
                Unsubscribe(nameof(OnLootEntity));
                Unsubscribe(nameof(OnPlayerRespawned));
                Unsubscribe(nameof(OnEntityTakeDamage));
                Unsubscribe(nameof(OnEntitySpawned));
                Unsubscribe(nameof(CanBuild));
                Unsubscribe(nameof(OnPlayerHealthChange));
                Unsubscribe(nameof(OnEntityDeath));
                Unsubscribe(nameof(OnEntityKill));
                //Unsubscribe(nameof(OnPlayerCommand));
                Unsubscribe(nameof(OnPlayerConnected));
                Unsubscribe(nameof(OnRestoreUponDeath));
                return;
            }

            Subscribe(nameof(OnPlayerDisconnected));
            Subscribe(nameof(OnItemDropped));
            Subscribe(nameof(OnPlayerSleepEnded));
            Subscribe(nameof(OnCreateWorldProjectile));
            Subscribe(nameof(OnLootEntity));
            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(OnRestoreUponDeath));

            if (!allowPlayerDeaths)
                Subscribe(nameof(OnPlayerHealthChange));

            Subscribe(nameof(OnEntityTakeDamage));
            Subscribe(nameof(OnEntityDeath));

            if (useBlacklistCommands || useWhitelistCommands)
            {
                Subscribe(nameof(OnPlayerCommand));
            }

            if (respawnWalls)
                Subscribe(nameof(OnEntityKill));
        }

        // Helper methods which are essential for the plugin to function. Do not modify these.

        [HookMethod("DuelistTerritory")]
        public bool DuelistTerritory(Vector3 position) // API
        {
            return DuelTerritory(position);
        }

        public bool DuelTerritory(Vector3 position, float offset = 5f) // 0.1.21: arena can be inside of the zone at any height
        {
            return duelingZones.Exists(zone => Vector3Ex.Distance2D(zone.Position, position) <= zoneRadius + offset);
        }

        public ulong GetOwnerId(string uid)
        {
            return Convert.ToUInt64(Math.Abs(uid.GetHashCode()));
        }

        [HookMethod("inEvent")]
        public bool inEvent(BasePlayer player)
        {
            return InEvent(player);
        }

        public bool InEvent(BasePlayer player)
        {
            return player != null && (dataDuelists.ContainsKey(player.UserIDString) || tdmMatches.Exists(match => match.GetTeam(player) != Team.None));
        }

        public bool IsDueling(BasePlayer player)
        {
            return player != null && duelsData != null && duelingZones.Count > 0 && player != null && dataDuelists.ContainsKey(player.UserIDString) && DuelTerritory(player.transform.position);
        }

        public bool InDeathmatch(BasePlayer player)
        {
            return player != null && tdmMatches.Exists(team => team.GetTeam(player) != Team.None) && DuelTerritory(player.transform.position);
        }

        public bool IsSpectator(BasePlayer player)
        {
            return player != null && spectators.Contains(player.UserIDString) && DuelTerritory(player.transform.position);
        }

        public bool IsEventBanned(string targetId)
        {
            return duelsData.Bans.ContainsKey(targetId);
        }

        public static long TimeStamp() => (DateTime.Now.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks) / 10000000;

        public string GetDisplayName(string targetId)
        {
            return covalence.Players.FindPlayerById(targetId)?.Name ?? targetId;
        }

        public void Log(string file, string message, bool timestamp = false)
        {
            LogToFile(file, $"[{DateTime.Now}] {message}", this, timestamp);
        }

        public GoodVersusEvilMatch GetMatch(BasePlayer player)
        {
            return tdmMatches.FirstOrDefault(team => team.GetTeam(player) != Team.None);
        }

        public bool InMatch(BasePlayer target)
        {
            return tdmMatches.Exists(team => team.GetTeam(target) != Team.None);
        }

        public bool IsOnConstruction(Vector3 position)
        {
            position.y += 1f;
            RaycastHit hit;

            return Physics.Raycast(position, Vector3.down, out hit, 1.5f, constructionMask) && hit.GetEntity() != null;
        }

        public bool Teleport(BasePlayer player, Vector3 destination)
        {
            if (player == null || destination == Vector3.zero) // don't send a player to their death. this should never happen
                return false;

            player.Invoke(player.EndLooting, 0.01f);

            if (DuelTerritory(destination))
            {
                var rematch = rematches.FirstOrDefault(x => x.HasPlayer(player));

                if (rematch != null)
                    rematches.Remove(rematch);

                SetPlayerTime(player, true);
            }

            if (player.IsWounded())
                player.StopWounded();

            player.metabolism.bleeding.value = 0;

            if (playerHealth > 0f && player.health < playerHealth)
                player.health = playerHealth;

            if (player.IsConnected)
                player.StartSleeping();

            player.Teleport(destination);

            if (player.IsConnected && (Vector3.Distance(player.transform.position, destination) > 50f || !DuelTerritory(destination))) // 1.1.2 reduced from 150 to 100
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                player.ClientRPCPlayer(null, player, "StartLoading");
                player.UpdateNetworkGroup();
                player.SendEntityUpdate();
            }

            player.SendNetworkUpdateImmediate(false);

            if (LustyMap.CanCall())
            {
                LustyMap?.Call(DuelTerritory(destination) ? "DisableMaps" : "EnableMaps", player);
            }

            return true;
        }

        public bool IsThrownWeapon(Item item)
        {
            if (item == null)
                return false;

            if (item.info.category == ItemCategory.Weapon || item.info.category == ItemCategory.Tool)
            {
                if (item.info.stackable > 1)
                    return false;

                var weapon = item?.GetHeldEntity() as BaseProjectile;

                if (weapon == null)
                    return true;

                if (weapon.primaryMagazine.capacity > 0)
                    return false;
            }

            return false;
        }

        public Vector3 RandomDropPosition() // CargoPlane.RandomDropPosition()
        {
            Vector3 vector;
            float num = 100f, x = TerrainMeta.Size.x / 3f;
            do
            {
                vector = Vector3Ex.Range(-x, x);
            } while (filter.GetFactor(vector) == 0f && --num > 0f);
            vector.y = 0f;
            return vector;
        }

        public Vector3 FindDuelingZone()
        {
            Vector3 position;
            DateTime tick = DateTime.Now; // create a timestamp to see how long this process takes
            int maxRetries = 500; // 0.1.9: increased due to rock collider detection. 0.1.10 rock collider detection removed but amount not changed
            int retries = maxRetries; // servers with high entity counts will require this

            if (managedZones.Count == 0 && ZoneManager.CanCall())
                SetupZoneManager();

            do
            {
                position = RandomDropPosition();

                foreach (var monument in monuments)
                {
                    if (Vector3.Distance(position, monument) < 150f) // don't put the dueling zone inside of a monument. players will throw a shit fit
                    {
                        position = Vector3.zero;
                        break;
                    }
                }

                if (position == Vector3.zero)
                    continue;

                if (managedZones.Count > 0)
                {
                    foreach (var zone in managedZones)
                    {
                        if (Vector3.Distance(zone.Key, position) <= zone.Value)
                        {
                            position = Vector3.zero; // blocked by zone manager
                            break;
                        }
                    }
                }

                if (position == Vector3.zero)
                    continue;

                position.y = TerrainMeta.HeightMap.GetHeight(position) + 100f; // setup the hit

                RaycastHit hit;
                if (Physics.Raycast(position, Vector3.down, out hit, position.y, groundMask))
                {
                    position.y = Mathf.Max(hit.point.y, TerrainMeta.HeightMap.GetHeight(position)); // get the max height

                    var colliders = Pool.Get<List<Collider>>();
                    Vis.Colliders(position, zoneRadius + 15f, colliders, blockedMask, QueryTriggerInteraction.Collide); // get all colliders using the provided layermask

                    if (colliders.Count > 0) // if any colliders were found from the blockedMask then we don't want this as our dueling zone. retry.
                        position = Vector3.zero;

                    Pool.FreeUnmanaged(ref colliders);

                    if (position != Vector3.zero) // so far so good, let's measure the highest and lowest points of the terrain, and count the amount of water colliders
                    {
                        var positions = GetCircumferencePositions(position, zoneRadius - 15f, 1f, 0f); // gather positions around the purposed zone
                        float min = 200f;
                        float max = -200f;
                        int water = 0;

                        foreach (var pos in positions)
                        {
                            if (Physics.Raycast(new Vector3(pos.x, pos.y + 100f, pos.z), Vector3.down, 100.5f, waterMask)) //look for water
                                water++; // count the amount of water colliders

                            min = Mathf.Min(pos.y, min); // set the lowest and highest points of the terrain
                            max = Mathf.Max(pos.y, max);
                        }

                        if (max - min > maxIncline || position.y - min > maxIncline) // the incline is too steep to be suitable for a dueling zone, retry.
                            position = Vector3.zero;

                        if (water > positions.Count / 4) // too many water colliders, retry.
                            position = Vector3.zero;

                        positions.Clear();
                    }
                }
                else
                    position = Vector3.zero; // found water instead of land

                if (position == Vector3.zero)
                    continue;

                if (DuelTerritory(position, zoneRadius + 15f)) // check if position overlaps an existing zone
                    position = Vector3.zero; // overlaps, retry.
            } while (position == Vector3.zero && --retries > 0); // prevent infinite loops

            if (position != Vector3.zero)
                Puts(msg("FoundZone", null, maxRetries - retries, (DateTime.Now - tick).TotalMilliseconds)); // we found a dueling zone! return the position to be assigned, spawn the zone and the spawn points!

            return position;
        }

        public List<Vector3> GetCircumferencePositions(Vector3 center, float radius, float next, float y) // as the name implies
        {
            var positions = new List<Vector3>();
            float degree = 0f;

            while (degree < 360)
            {
                float angle = (float)(2 * Math.PI / 360) * degree;
                float x = center.x + radius * (float)Math.Cos(angle);
                float z = center.z + radius * (float)Math.Sin(angle);
                var position = new Vector3(x, 0f, z);

                position.y = y == 0f ? TerrainMeta.HeightMap.GetHeight(position) : y;
                positions.Add(position);

                degree += next;
            }

            return positions;
        }

        public List<Vector3> GetAutoSpawns(DuelingZone zone)
        {
            var spawns = new List<Vector3>();
            string key = zone.Position.ToString();

            if (duelsData.AutoGeneratedSpawns.ContainsKey(key) && duelsData.AutoGeneratedSpawns[key].Count > 0)
                spawns.AddRange(duelsData.AutoGeneratedSpawns[key].Select(spawn => spawn.ToVector3())); // use cached spawn points

            if (!duelsData.AutoGeneratedSpawns.ContainsKey(key))
                duelsData.AutoGeneratedSpawns.Add(key, new List<string>());

            if (spawns.Count < 2)
                spawns = CreateSpawnPoints(zone.Position); // create spawn points on the fly

            duelsData.AutoGeneratedSpawns[key] = spawns.Select(spawn => spawn.ToString()).ToList();
            return spawns;
        }

        public List<Vector3> CreateSpawnPoints(Vector3 center)
        {
            var positions = new List<Vector3>(); // 0.1.1 bugfix: spawn point height (y) wasn't being modified when indexing the below foreach list. instead, create a copy of each position and return a new list (cause: can't modify members of value types without changing the collection and invalidating the enumerator. bug: index the value type and change the value. result: list did not propagate)

            // create spawn points slightly inside of the dueling zone so they don't spawn inside of walls
            foreach (var position in GetCircumferencePositions(center, zoneRadius - 15f, 10f, 0f))
            {
                var hits = Physics.RaycastAll(new Vector3(position.x, TerrainMeta.HighestPoint.y + 200f, position.z), Vector3.down, Mathf.Infinity);

                if (hits.Length > 0) // low failure rate
                {
                    float y = TerrainMeta.HeightMap.GetHeight(position);

                    if (avoidWaterSpawns && TerrainMeta.WaterMap.GetHeight(position) - y > 0.8f)
                        continue; // 0.1.16: better method to check water level

                    foreach (var hit in hits)
                    {
                        switch (LayerMask.LayerToName(hit.collider.gameObject.layer))
                        {
                            case "Construction":
                            case "Deployed":
                                if (!hit.GetEntity()) // 0.1.2 bugfix: spawn points floating when finding a collider with no entity
                                    continue;

                                y = Mathf.Max(hit.point.y, y);
                                break;
                            case "World":
                            case "Terrain":
                                y = Mathf.Max(hit.point.y, y);
                                break;
                        }
                    }

                    positions.Add(new Vector3(position.x, y, position.z));
                }
            }

            return positions;
        }

        public bool ResetDuelists() // reset all data for the wipe after assigning awards
        {
            if (AssignDuelists())
            {
                if (resetSeed)
                {
                    duelsData.VictoriesSeed.Clear();
                    duelsData.LossesSeed.Clear();
                    duelsData.MatchKillsSeed.Clear();
                    duelsData.MatchDeathsSeed.Clear();
                    duelsData.MatchLossesSeed.Clear();
                    duelsData.MatchVictoriesSeed.Clear();
                    duelsData.MatchSizesVictoriesSeed.Clear();
                    duelsData.MatchSizesVictoriesSeed.Clear();
                }

                if (wipeDuelZones)
                {
                    duelsData.DuelZones.Clear();
                    duelsData.Spawns.Clear();
                    duelsData.AutoGeneratedSpawns.Clear();
                }

                duelsData.Bets.Clear();
                duelsData.ClaimBets.Clear();
                ResetTemporaryData();
            }

            return true;
        }

        public bool AssignDuelists()
        {
            if (!recordStats || duelsData.VictoriesSeed.Count == 0)
                return true; // nothing to do here, return

            foreach (var target in covalence.Players.All) // remove player awards from previous wipe
            {
                if (permission.UserHasPermission(target.Id, duelistPerm))
                    permission.RevokeUserPermission(target.Id, duelistPerm);

                if (permission.UserHasGroup(target.Id, duelistGroup))
                    permission.RemoveUserGroup(target.Id, duelistGroup);
            }

            if (permsToGive <= 0) // check now incase the user disabled awards later on
                return true;

            var duelists = duelsData.VictoriesSeed.ToList(); // sort the data
            duelists.Sort((x, y) => y.Value.CompareTo(x.Value));

            int added = 0;

            for (int i = 0; i < duelists.Count; i++) // now assign it
            {
                var target = covalence.Players.FindPlayerById(duelists[i].Key);

                if (target == null || target.IsBanned || target.IsAdmin)
                    continue;

                permission.GrantUserPermission(target.Id, duelistPerm.ToLower(), this);
                permission.AddUserGroup(target.Id, duelistGroup.ToLower());

                Log("awards", msg("Awards", null, target.Name, target.Id, duelists[i].Value), true);
                Puts(msg("Granted", null, target.Name, target.Id, duelistPerm, duelistGroup));

                if (++added >= permsToGive)
                    break;
            }

            if (added > 0)
                Puts(msg("Logged", null, string.Format("{0}{1}{2}_{3}-{4}.txt", Interface.Oxide.LogDirectory, Path.DirectorySeparatorChar, Name.Replace(" ", "").ToLower(), "awards", DateTime.Now.ToString("yyyy-MM-dd"))));

            return true;
        }

        public bool IsNewman(BasePlayer player) // count the players items. exclude rocks and torchs
        {
            if (bypassNewmans || saveRestoreEnabled)
                return true;

            List<Item> itemList = Pool.Get<List<Item>>();
            int count = player.inventory.GetAllItems(itemList);
            Pool.FreeUnmanaged(ref itemList);

            if (permission.UserHasPermission(player.UserIDString, "permamap.use") && player.inventory.containerBelt.GetSlot(6) != null)
                count -= 1;

            count -= respawnLoot.Sum(entry => GetAmount(player, entry.shortname));

            return count == 0;
        }

        public int GetAmount(BasePlayer player, string shortname)
        {
            List<Item> itemList = Pool.Get<List<Item>>();
            player.inventory.GetAllItems(itemList);
            int count = itemList.Where(x => x.info.shortname.Equals(shortname, StringComparison.CurrentCultureIgnoreCase)).Sum(item => item.amount);
            Pool.FreeUnmanaged(ref itemList);
            return count;
        }

        public bool RemoveFromQueue(string targetId)
        {
            foreach (var kvp in duelsData.Queued)
            {
                if (kvp.Value == targetId)
                {
                    duelsData.Queued.Remove(kvp.Key);
                    return true;
                }
            }

            return false;
        }

        public void CheckQueue()
        {
            if (duelsData.Queued.Count < 2 || !duelsData.DuelsEnabled)
                return;

            string playerId = duelsData.Queued.Values.ElementAt(0);
            string targetId = duelsData.Queued.Values.ElementAt(1);
            var player = BasePlayer.Find(playerId);
            var target = BasePlayer.Find(targetId);

            if (player == null || !player.CanInteract() || InMatch(player))
            {
                if (RemoveFromQueue(playerId))
                    CheckQueue();

                return;
            }

            if (target == null || !player.CanInteract() || InMatch(player))
            {
                if (RemoveFromQueue(targetId))
                    CheckQueue();

                return;
            }

            if (!IsNewman(player))
            {
                if (RemoveFromQueue(player.UserIDString))
                    Message(player, msg("MustBeNaked", player.UserIDString));
                return;
            }

            if (!IsNewman(target))
            {
                if (RemoveFromQueue(target.UserIDString))
                    Message(target, msg("MustBeNaked", target.UserIDString));
                return;
            }

            SelectZone(player, target);
        }

        public bool SelectZone(BasePlayer player, BasePlayer target)
        {
            var lastZone = GetPlayerZone(player, 2) ?? GetPlayerZone(target, 2) ?? GetDuelZone(player.transform.position) ?? GetDuelZone(target.transform.position);

            if (lastZone != null)
            {
                var success = lastZone.AddWaiting(player, target);

                if (success != null && success is bool && (bool)success)
                {
                    Initiate(player, target, false, lastZone);
                    return true;
                }
            }

            var zones = duelingZones.Where(zone => !zone.IsFull && !zone.IsLocked && zone.Spawns.Count >= requiredMinSpawns && zone.Spawns.Count <= requiredMaxSpawns).ToList();

            while (zones.Count > 0)
            {
                var zone = zones.GetRandom();
                var success = zone.AddWaiting(player, target);

                if (success == null) // user must pay the duel entry fee first
                    return true;

                if (success is bool && (bool)success)
                {
                    Initiate(player, target, false, zone);
                    return true;
                }

                zones.Remove(zone);
            };

            return false;
        }

        public string GetKit(BasePlayer player, BasePlayer target)
        {
            string kit = GetRandomKit();

            if (duelsData.CustomKits.ContainsKey(player.UserIDString) && duelsData.CustomKits.ContainsKey(target.UserIDString))
            {
                string playerKit = duelsData.CustomKits[player.UserIDString];
                string targetKit = duelsData.CustomKits[target.UserIDString];

                if (playerKit.Equals(targetKit, StringComparison.CurrentCultureIgnoreCase))
                {
                    return GetVerifiedKit(playerKit) ?? kit;
                }
            }

            return kit;
        }

        public void VerifyKits()
        {
            if (Kits.CanCall())
            {
                foreach (string kit in lpDuelingKits.ToList())
                    if (!IsKit(kit))
                        lpDuelingKits.Remove(kit);

                foreach (string kit in hpDuelingKits.ToList())
                    if (!IsKit(kit))
                        hpDuelingKits.Remove(kit);
            }
        }

        public string GetRandomKit()
        {
            VerifyKits();

            if (Random.value < lesserKitChance && lpDuelingKits.Count > 0)
            {
                return lpDuelingKits.GetRandom();
            }

            if (hpDuelingKits.Count > 0)
            {
                return hpDuelingKits.GetRandom();
            }

            if (customKits.Count > 0)
            {
                return customKits.ElementAt(Random.Range(0, customKits.Count)).Key;
            }

            return null;
        }

        public void Initiate(BasePlayer player, BasePlayer target, bool checkInventory, DuelingZone destZone)
        {
            try
            {
                if (player == null || target == null || destZone == null)
                    return;

                dataRequests.Remove(player.UserIDString);
                dataRequests.Remove(target.UserIDString);

                if (checkInventory)
                {
                    if (!IsNewman(player))
                    {
                        Message(player, msg("MustBeNaked", player.UserIDString));
                        Message(target, msg("DuelMustBeNaked", target.UserIDString, player.displayName));
                        return;
                    }

                    if (!IsNewman(target))
                    {
                        Message(target, msg("MustBeNaked", player.UserIDString));
                        Message(player, msg("DuelMustBeNaked", player.UserIDString, target.displayName));
                        return;
                    }
                }

                if (!DuelTerritory(player.transform.position) || !duelsData.Homes.ContainsKey(player.UserIDString))
                {
                    var ppos = player.transform.position;
                    if (IsOnConstruction(ppos)) ppos.y += 1; // prevent player from becoming stuck or dying when teleported home
                    duelsData.Homes[player.UserIDString] = ppos.ToString();
                }

                if (!DuelTerritory(target.transform.position) || !duelsData.Homes.ContainsKey(target.UserIDString))
                {
                    var tpos = target.transform.position;
                    if (IsOnConstruction(tpos)) tpos.y += 1;
                    duelsData.Homes[target.UserIDString] = tpos.ToString();
                }

                var playerSpawn = destZone.Spawns.GetRandom();
                var targetSpawn = playerSpawn;
                float dist = -100f;

                foreach (var spawn in destZone.Spawns) // get the furthest spawn point away from the player and assign it to target
                {
                    float distance = Vector3.Distance(spawn, playerSpawn);

                    if (distance > dist)
                    {
                        dist = distance;
                        targetSpawn = spawn;
                    }
                }

                string kit = GetKit(player, target);
                duelsData.Kits[player.UserIDString] = kit;
                duelsData.Kits[target.UserIDString] = kit;

                Teleport(player, playerSpawn);
                Teleport(target, targetSpawn);

                if (debugMode)
                    Puts($"{player.displayName} and {target.displayName} have entered a duel.");

                RemoveFromQueue(player.UserIDString);
                RemoveFromQueue(target.UserIDString);

                if (immunityTime >= 1)
                {
                    dataImmunity[player.UserIDString] = TimeStamp() + immunityTime;
                    dataImmunity[target.UserIDString] = TimeStamp() + immunityTime;
                    dataImmunitySpawns[player.UserIDString] = playerSpawn;
                    dataImmunitySpawns[target.UserIDString] = targetSpawn;
                }

                dataDuelists[player.UserIDString] = target.UserIDString;
                dataDuelists[target.UserIDString] = player.UserIDString;
                SubscribeHooks(true);

                Message(player, msg("NowDueling", player.UserIDString, target.displayName));
                Message(target, msg("NowDueling", target.UserIDString, player.displayName));

            }
            catch (Exception ex)
            {
                SubscribeHooks(false);
                duelsData.DuelsEnabled = false;
                SaveData();

                Puts("---");
                Puts("Plugin disabled: {0} --- {1}", ex.Message, ex.StackTrace);
                Puts("---");

                ResetDuelist(player.UserIDString);
                ResetDuelist(target.UserIDString);
            }
        }

        // manually check as players may not be in a clan or on a friends list

        public bool IsAllied(string playerId, string targetId)
        {
            var player = BasePlayer.Find(playerId);
            var target = BasePlayer.Find(targetId);

            return player != null && target != null && IsAllied(player, target);
        }

        public bool IsAllied(BasePlayer player, BasePlayer target)
        {
            if (player.IsAdmin && target.IsAdmin)
                return false;

            return IsOnSameTeam(player, target) || IsInSameClan(player, target) || IsAuthorizing(player, target) || IsBunked(player, target) || IsCodeAuthed(player, target) || IsInSameBase(player, target);
        }

        public bool IsOnSameTeam(BasePlayer player, BasePlayer target)
        {
            return player.currentTeam != 0 && player.Team.members.Contains(target.userID);
        }

        public bool IsInSameClan(BasePlayer player, BasePlayer target) // 1st method.
        {
            return Clans.CanCall() && Convert.ToBoolean(Clans?.Call("IsMemberOrAlly", player.UserIDString, target.UserIDString));
        }

        private bool IsAuthorizing(BasePlayer player, BasePlayer target) // 2nd method.
        {
            var privs = BaseNetworkable.serverEntities.OfType<BuildingPrivlidge>();

            return privs.Exists(priv => priv.IsAuthed(player) && priv.IsAuthed(target));
        }

        private bool IsBunked(BasePlayer player, BasePlayer target) // 3rd method. thanks @i_love_code for helping with this too
        {
            var bags = SleepingBag.FindForPlayer(target.userID, true);

            if (bags.Length > 0)
            {
                foreach (var a in SleepingBag.FindForPlayer(player.userID, true))
                {
                    if (bags.Exists(b => a.buildingID == b.buildingID))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsCodeAuthed(BasePlayer player, BasePlayer target) // 4th method
        {
            foreach (CodeLock codelock in BaseNetworkable.serverEntities.OfType<CodeLock>())
            {
                if (codelock.whitelistPlayers.Contains(player.userID) && codelock.whitelistPlayers.Contains(target.userID))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsInSameBase(BasePlayer player, BasePlayer target) // 5th method
        {
            var privs = BaseNetworkable.serverEntities.OfType<BuildingPrivlidge>();

            foreach (var priv in privs.Where(p => p.IsAuthed(player)))
            {
                if (priv.GetBuilding()?.decayEntities?.Exists(entity => entity.OwnerID == target.userID) == true)
                {
                    return true;
                }
            }

            foreach (var priv in privs.Where(p => p.IsAuthed(target)))
            {
                if (priv.GetBuilding()?.decayEntities?.Exists(entity => entity.OwnerID == player.userID) == true)
                {
                    return true;
                }
            }

            return false;
        }

        public void Metabolize(BasePlayer player, bool set) // we don't want the elements to harm players since the zone can spawn anywhere on the map!
        {
            if (player == null)
                return;

            if (set)
            {
                player.health = 100f;
                player.metabolism.temperature.min = 32; // immune to cold
                player.metabolism.temperature.max = 32;
                player.metabolism.temperature.value = 32;
                player.metabolism.oxygen.min = 1; // immune to drowning
                player.metabolism.oxygen.value = 1;
                player.metabolism.poison.value = 0; // if they ate raw meat
                player.metabolism.calories.value = player.metabolism.calories.max;
                player.metabolism.hydration.value = player.metabolism.hydration.max;
                player.metabolism.wetness.max = 0;
                player.metabolism.wetness.value = 0;
                player.metabolism.radiation_level.max = 0;
                player.metabolism.radiation_poison.max = 0;
            }
            else
            {
                player.metabolism.oxygen.min = 0;
                player.metabolism.oxygen.max = 1;
                player.metabolism.temperature.min = -100;
                player.metabolism.temperature.max = 100;
                player.metabolism.wetness.min = 0;
                player.metabolism.wetness.max = 1;
                player.metabolism.radiation_level.Reset();
                player.metabolism.radiation_poison.Reset();
            }

            player.metabolism.SendChangesToClient();
        }

        public bool IsKit(string kit)
        {
            return Convert.ToBoolean(Kits?.Call("isKit", kit));
        }

        public void AwardPlayer(ulong playerId, double money, int points)
        {
            if (money == 0.0 && points == 0)
                return;

            var player = BasePlayer.FindByID(playerId);

            if (money > 0.0)
            {
                if (Economics.CanCall())
                {
                    Economics?.Call("Deposit", playerId, money);

                    if (player != null)
                        Message(player, msg("EconomicsDeposit", player.UserIDString, money));
                }
            }

            if (points > 0)
            {
                if (ServerRewards.CanCall())
                {
                    var success = ServerRewards?.Call("AddPoints", playerId, points);

                    if (player != null && success != null && success is bool && (bool)success)
                        Message(player, msg("ServerRewardPoints", player.UserIDString, points));
                }
            }
        }

        public void GivePlayerKit(BasePlayer player)
        {
            if (player == null)
                return;

            string kit = duelsData.Kits.ContainsKey(player.UserIDString) ? duelsData.Kits[player.UserIDString] : string.Empty;

            duelsData.Kits.Remove(player.UserIDString);
            player.inventory.Strip();

            if (!string.IsNullOrEmpty(kit))
            {
                if (Kits.CanCall() && IsKit(kit))
                {
                    object success = Kits.Call("GiveKit", player, kit);

                    if (success is bool && (bool)success)
                    {
                        return;
                    }
                }

                if (string.IsNullOrEmpty(kit))
                {
                    kit = duelsData.CustomKits.ContainsKey(player.UserIDString) ? duelsData.CustomKits[player.UserIDString] : null;
                }

                if (GiveCustomKit(player, kit))
                {
                    return;
                }
            }

            if (Kits.CanCall())
            {
                kit = GetRandomKit();

                if (!string.IsNullOrEmpty(kit))
                {
                    object success = Kits.Call("GiveKit", player, kit);

                    if (success is bool && (bool)success)
                    {
                        return;
                    }
                }
            }

            // give a basic kit when no kit is provided, or the provided kit is invalid
            player.inventory.GiveItem(ItemManager.CreateByItemID(1443579727, 1, 0)); // bow
            player.inventory.GiveItem(ItemManager.CreateByItemID(-1234735557, 50, 0)); // arrows
            player.inventory.GiveItem(ItemManager.CreateByItemID(1602646136, 1, 0)); // stone spear
            player.inventory.GiveItem(ItemManager.CreateByItemID(-2072273936, 5, 0)); // bandage
            player.inventory.GiveItem(ItemManager.CreateByItemID(254522515, 3, 0)); // medkit
            player.inventory.GiveItem(ItemManager.CreateByItemID(1079279582, 4, 0)); // syringe
        }

        public bool GiveCustomKit(BasePlayer player, string kit)
        {
            if (string.IsNullOrEmpty(kit) || customKits.Count == 0 || !customKits.ContainsKey(kit))
                return false;

            bool success = false;

            foreach (var dki in customKits[kit])
            {
                Item item = ItemManager.CreateByName(dki.shortname, dki.amount, dki.skin);

                if (item == null)
                {
                    Puts("Invalid shortname {0}", dki.shortname);
                    continue;
                }

                if (item.skin == 0 && useRandomSkins)
                {
                    var skins = GetItemSkins(item.info);

                    if (skins.Count > 0)
                        item.skin = skins.GetRandom();
                }

                if (dki.mods != null)
                {
                    foreach (string shortname in dki.mods)
                    {
                        Item mod = ItemManager.CreateByName(shortname, 1);

                        if (mod != null)
                            item.contents.AddItem(mod.info, 1);
                    }
                }

                var heldEntity = item.GetHeldEntity();

                if (heldEntity != null)
                {
                    if (item.skin != 0)
                        heldEntity.skinID = item.skin;

                    var weapon = heldEntity as BaseProjectile;

                    if (weapon != null)
                    {
                        if (!string.IsNullOrEmpty(dki.ammo))
                        {
                            var def = ItemManager.FindItemDefinition(dki.ammo);

                            if (def != null)
                                weapon.primaryMagazine.ammoType = def;
                        }

                        weapon.primaryMagazine.contents = 0; // unload the old ammo
                        weapon.SendNetworkUpdateImmediate(false); // update
                        weapon.primaryMagazine.contents = weapon.primaryMagazine.capacity; // load new ammo
                    }
                }

                var container = dki.container == "belt" ? player.inventory.containerBelt : dki.container == "wear" ? player.inventory.containerWear : player.inventory.containerMain;

                item.MarkDirty();
                if (item.MoveToContainer(container, dki.slot < 0 || dki.slot > container.capacity - 1 ? -1 : dki.slot, true))
                {
                    success = true;
                }
                else
                {
                    item.Remove(0f);
                }
            }

            return success;
        }

        private void DuelAnnouncement(bool bypass)
        {
            if (!bypass && (!duelsData.DuelsEnabled || !useAnnouncement))
                return;

            if (BasePlayer.activePlayerList.Count < 3)
                return;

            string console = msg("DuelAnnouncement");
            string disabled = msg("Disabled");

            console = console.Replace("{duelChatCommand}", !string.IsNullOrEmpty(szDuelChatCommand) ? szDuelChatCommand : disabled);
            console = console.Replace("{ladderCommand}", !string.IsNullOrEmpty(szDuelChatCommand) ? string.Format("{0} ladder", szDuelChatCommand) : disabled);
            console = console.Replace("{queueCommand}", !string.IsNullOrEmpty(szQueueChatCommand) ? szQueueChatCommand : disabled);

            if (allowBets)
                console += msg("DuelAnnouncementBetsSuffix", null, szDuelChatCommand);

            Puts(RemoveFormatting(console));

            foreach (var player in BasePlayer.activePlayerList.Where(p => p?.displayName != null))
            {
                string message = msg("DuelAnnouncement", player.UserIDString);

                message = message.Replace("{duelChatCommand}", !string.IsNullOrEmpty(szDuelChatCommand) ? szDuelChatCommand : disabled);
                message = message.Replace("{ladderCommand}", !string.IsNullOrEmpty(szDuelChatCommand) ? string.Format("{0} ladder", szDuelChatCommand) : disabled);
                message = message.Replace("{queueCommand}", !string.IsNullOrEmpty(szQueueChatCommand) ? szQueueChatCommand : disabled);

                if (allowBets)
                    message += msg("DuelAnnouncementBetsSuffix", player.UserIDString, szDuelChatCommand);

                Message(player, string.Format("{0} <color=#C0C0C0>{1}</color>", lang.GetMessage("Prefix", this, player.UserIDString), message));
            }
        }

        public bool CreateBet(BasePlayer player, int betAmount, BetInfo betInfo)
        {
            if (betAmount > betInfo.max) // adjust the bet to the maximum since they clearly want to do this
                betAmount = betInfo.max;

            int amount = player.inventory.GetAmount(betInfo.itemid);

            if (amount == 0)
            {
                Message(player, msg("BetZero", player.UserIDString));
                return false;
            }

            if (amount < betAmount) // obviously they're just trying to see how this works. we won't adjust it here.
            {
                Message(player, msg("BetNotEnough", player.UserIDString));
                return false;
            }

            var takenItems = new List<Item>();
            int takenAmount = player.inventory.Take(takenItems, betInfo.itemid, betAmount);

            if (takenAmount == betAmount)
            {
                var bet = new BetInfo
                {
                    itemid = betInfo.itemid,
                    amount = betAmount,
                    trigger = betInfo.trigger
                };

                duelsData.Bets.Add(player.UserIDString, bet);

                string message = msg("BetPlaced", player.UserIDString, betInfo.trigger, betAmount);

                if (allowBetRefund)
                    message += msg("BetRefundSuffix", player.UserIDString, szDuelChatCommand);
                else if (allowBetForfeit)
                    message += msg("BetForfeitSuffix", player.UserIDString, szDuelChatCommand);

                Message(player, message);
                Puts("{0} bet {1} ({2})", player.displayName, betInfo.trigger, betAmount);

                foreach (Item item in takenItems.ToList())
                    item.Remove(0.1f);

                return true;
            }

            if (takenItems.Count > 0)
            {
                foreach (Item item in takenItems.ToList())
                    player.GiveItem(item, BaseEntity.GiveItemReason.Generic);

                takenItems.Clear();
            }

            return false;
        }

        private void GetWorkshopIDs(int code, string response)
        {
            if (!string.IsNullOrEmpty(response) && code == 200)
            {
                var items = JsonConvert.DeserializeObject<ItemSchema>(response).items;

                foreach (var item in items)
                {
                    if (string.IsNullOrEmpty(item.itemshortname) || string.IsNullOrEmpty(item.workshopdownload))
                        continue;

                    if (!workshopskinsCache.ContainsKey(item.itemshortname))
                        workshopskinsCache.Add(item.itemshortname, new List<ulong>());

                    workshopskinsCache[item.itemshortname].Add(Convert.ToUInt64(item.workshopdownload));
                }
            }
        }

        public List<ulong> GetItemSkins(ItemDefinition def)
        {
            if (!skinsCache.ContainsKey(def.shortname))
            {
                var skins = new List<ulong>();

                skins.AddRange(def.skins.Select(skin => Convert.ToUInt64(skin.id)));

                if (useWorkshopSkins && workshopskinsCache.ContainsKey(def.shortname))
                {
                    skins.AddRange(workshopskinsCache[def.shortname]);
                    workshopskinsCache.Remove(def.shortname);
                }

                if (skins.Contains(0uL))
                    skins.Remove(0uL);

                skinsCache.Add(def.shortname, skins);
            }

            return skinsCache[def.shortname];
        }

        private void RemoveRequests(BasePlayer player)
        {
            foreach (var entry in dataRequests.ToList())
            {
                if (entry.Key == player.UserIDString || entry.Value == player.UserIDString)
                {
                    dataRequests.Remove(entry.Key);
                }
            }
        }

        private void UpdateMatchSizeStats(string playerId, bool winner, bool loser, int teamSize)
        {
            string key = teamSize.ToString();

            if (winner)
            {
                if (!duelsData.MatchSizesVictoriesSeed.ContainsKey(key)) duelsData.MatchSizesVictoriesSeed.Add(key, new Dictionary<string, int>());
                if (!duelsData.MatchSizesVictories.ContainsKey(key)) duelsData.MatchSizesVictories.Add(key, new Dictionary<string, int>());
                if (!duelsData.MatchSizesVictoriesSeed[key].ContainsKey(playerId)) duelsData.MatchSizesVictoriesSeed[key].Add(playerId, 1);
                else duelsData.MatchSizesVictoriesSeed[key][playerId]++;
                if (!duelsData.MatchSizesVictories[key].ContainsKey(playerId)) duelsData.MatchSizesVictories[key].Add(playerId, 1);
                else duelsData.MatchSizesVictories[key][playerId]++;
            }
            if (loser)
            {
                if (!duelsData.MatchSizesLossesSeed.ContainsKey(key)) duelsData.MatchSizesLossesSeed.Add(key, new Dictionary<string, int>());
                if (!duelsData.MatchSizesLosses.ContainsKey(key)) duelsData.MatchSizesLosses.Add(key, new Dictionary<string, int>());
                if (!duelsData.MatchSizesLossesSeed[key].ContainsKey(playerId)) duelsData.MatchSizesLossesSeed[key].Add(playerId, 1);
                else duelsData.MatchSizesLossesSeed[key][playerId]++;
                if (!duelsData.MatchSizesLosses[key].ContainsKey(playerId)) duelsData.MatchSizesLosses[key].Add(playerId, 1);
                else duelsData.MatchSizesLosses[key][playerId]++;
            }
        }

        private void UpdateMatchStats(string playerId, bool winner, bool loser, bool death, bool kill)
        {
            if (winner)
            {
                if (!duelsData.MatchVictories.ContainsKey(playerId)) duelsData.MatchVictories.Add(playerId, 1);
                else duelsData.MatchVictories[playerId]++;
                if (!duelsData.MatchVictoriesSeed.ContainsKey(playerId)) duelsData.MatchVictoriesSeed.Add(playerId, 1);
                else duelsData.MatchVictoriesSeed[playerId]++;
            }
            if (loser)
            {
                if (!duelsData.MatchLosses.ContainsKey(playerId)) duelsData.MatchLosses.Add(playerId, 1);
                else duelsData.MatchLosses[playerId]++;
                if (!duelsData.MatchLossesSeed.ContainsKey(playerId)) duelsData.MatchLossesSeed.Add(playerId, 1);
                else duelsData.MatchLossesSeed[playerId]++;
            }
            if (death)
            {
                if (!duelsData.MatchDeaths.ContainsKey(playerId)) duelsData.MatchDeaths.Add(playerId, 1);
                else duelsData.MatchDeaths[playerId]++;
                if (!duelsData.MatchDeathsSeed.ContainsKey(playerId)) duelsData.MatchDeathsSeed.Add(playerId, 1);
                else duelsData.MatchDeathsSeed[playerId]++;
            }
            if (kill)
            {
                if (!duelsData.MatchKills.ContainsKey(playerId)) duelsData.MatchKills.Add(playerId, 1);
                else duelsData.MatchKills[playerId]++;
                if (!duelsData.MatchKillsSeed.ContainsKey(playerId)) duelsData.MatchKillsSeed.Add(playerId, 1);
                else duelsData.MatchKillsSeed[playerId]++;
            }
        }

        #region SpawnPoints

        public void SendSpawnHelp(BasePlayer player)
        {
            Message(player, msg("SpawnCount", player.UserIDString, duelsData.Spawns.Count));
            Message(player, msg("SpawnAdd", player.UserIDString, szDuelChatCommand));
            Message(player, msg("SpawnHere", player.UserIDString, szDuelChatCommand));
            Message(player, msg("SpawnRemove", player.UserIDString, szDuelChatCommand, spRemoveOneMaxDistance));
            Message(player, msg("SpawnRemoveAll", player.UserIDString, szDuelChatCommand, spRemoveAllMaxDistance));
            Message(player, msg("SpawnWipe", player.UserIDString, szDuelChatCommand));
        }

        public void AddSpawnPoint(BasePlayer player, bool useHit)
        {
            var spawn = player.transform.position;

            if (useHit)
            {
                RaycastHit hit;
                if (!Physics.Raycast(player.eyes.HeadRay(), out hit, Mathf.Infinity, wallMask))
                {
                    Message(player, msg("FailedRaycast", player.UserIDString));
                    return;
                }

                spawn = hit.point;
            }

            if (duelsData.Spawns.Contains(spawn.ToString()))
            {
                Message(player, msg("SpawnExists", player.UserIDString));
                return;
            }

            duelsData.Spawns.Add(spawn.ToString());
            player.SendConsoleCommand("ddraw.text", spDrawTime, Color.green, spawn, "+S");
            Message(player, msg("SpawnAdded", player.UserIDString, FormatPosition(spawn)));
        }

        public void RemoveSpawnPoint(BasePlayer player)
        {
            float radius = spRemoveOneMaxDistance;
            var spawn = Vector3.zero;
            float dist = radius;

            foreach (var entry in duelsData.Spawns.ToList())
            {
                var _spawn = entry.ToVector3();
                float distance = Vector3.Distance(player.transform.position, _spawn);

                if (distance < dist)
                {
                    dist = distance;
                    spawn = _spawn;
                }
            }

            if (spawn != Vector3.zero)
            {
                duelsData.Spawns.Remove(spawn.ToString());
                player.SendConsoleCommand("ddraw.text", spDrawTime, Color.red, spawn, "-S");
                Message(player, msg("SpawnRemoved", player.UserIDString, 1));
            }
            else
                Message(player, msg("SpawnNoneFound", player.UserIDString, radius));
        }

        public void RemoveSpawnPoints(BasePlayer player)
        {
            int count = 0;

            foreach (var entry in duelsData.Spawns.ToList())
            {
                var spawn = entry.ToVector3();

                if (Vector3.Distance(player.transform.position, spawn) <= spRemoveAllMaxDistance)
                {
                    count++;
                    duelsData.Spawns.Remove(entry);
                    player.SendConsoleCommand("ddraw.text", spDrawTime, Color.red, spawn, "-S");
                }
            }

            if (count == 0)
                Message(player, msg("SpawnNoneFound", player.UserIDString, spRemoveAllMaxDistance));
            else
                Message(player, msg("SpawnRemoved", player.UserIDString, count));
        }

        public void WipeSpawnPoints(BasePlayer player)
        {
            if (duelsData.Spawns.Count == 0)
            {
                Message(player, msg("SpawnNoneExist", player.UserIDString));
                return;
            }

            var spawns = duelsData.Spawns.Select(spawn => spawn.ToVector3()).ToList();

            foreach (var spawn in spawns)
                player.SendConsoleCommand("ddraw.text", 30f, Color.red, spawn, "-S");

            int amount = duelsData.Spawns.Count;
            duelsData.Spawns.Clear();
            spawns.Clear();
            Message(player, msg("SpawnWiped", player.UserIDString, amount));
        }

        public List<Vector3> GetSpawnPoints(DuelingZone zone)
        {
            return duelsData.Spawns.Select(entry => entry.ToVector3()).Where(spawn => zone.Distance(spawn) < zoneRadius).ToList();
        }

        public string FormatBone(string source)
        {
            if (string.IsNullOrEmpty(source))
                return "Chest";

            foreach (var entry in boneTags)
                source = source.Replace(entry.Key, entry.Value);

            return string.Join(" ", source.Split(' ').Select(str => str.SentenceCase()));
        }

        public string FormatPosition(Vector3 position)
        {
            string x = position.x.ToString("N2");
            string y = position.y.ToString("N2");
            string z = position.z.ToString("N2");

            return $"{x} {y} {z}";
        }

        #endregion

        #region UI Creation 

        private readonly List<string> createUI = new List<string>();
        private readonly List<string> duelistUI = new List<string>();
        private readonly List<string> kitsUI = new List<string>();
        private readonly List<string> matchesUI = new List<string>();

        [ConsoleCommand("UI_DuelistCommand")]
        private void ccmdDuelistUI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (IsNull(player) || !arg.HasArgs())
                return;

            switch (arg.Args[0].ToLower())
            {
                case "accept":
                    {
                        if (dataRequests.ContainsValue(player.UserIDString))
                        {
                            cmdDuel(player, szDuelChatCommand, new[] { "accept" });
                            break;
                        }
                        if (tdmRequests.ContainsValue(player.UserIDString))
                        {
                            cmdTDM(player, szMatchChatCommand, new[] { "accept" });
                            break;
                        }

                        Message(player, msg("NoPendingRequests2", player.UserIDString));
                        break;
                    }
                case "decline":
                    {
                        if (dataRequests.ContainsKey(player.UserIDString) || dataRequests.ContainsValue(player.UserIDString))
                        {
                            cmdDuel(player, szDuelChatCommand, new[] { "decline" });
                            break;
                        }

                        var deathmatch = tdmMatches.FirstOrDefault(x => x.GetTeam(player) != Team.None);

                        if (deathmatch != null || tdmRequests.ContainsValue(player.UserIDString) || tdmRequests.ContainsKey(player.UserIDString))
                        {
                            cmdTDM(player, szMatchChatCommand, new[] { "decline" });
                            break;
                        }

                        Message(player, msg("NoPendingRequests", player.UserIDString));
                        break;
                    }
                case "closeui":
                    {
                        DestroyUI(player);
                        return;
                    }
                case "kits":
                    {
                        ToggleKitUI(player);
                        break;
                    }
                case "public":
                    {
                        cmdTDM(player, szMatchChatCommand, new[] { "public" });
                        break;
                    }
                case "requeue":
                    {
                        if (IsDueling(player) || InDeathmatch(player))
                            return;

                        if (sendHomeRequeue)
                        {
                            CuiHelper.DestroyUi(player, "DuelistUI_Defeat");
                            SendHome(player);
                        }
                        else CreateDefeatUI(player);
                        cmdQueue(player, szQueueChatCommand, new string[0]);
                        return;
                    }
                case "queue":
                    {
                        if (IsDueling(player) || InDeathmatch(player))
                            break;

                        cmdQueue(player, szQueueChatCommand, new string[0]);
                        break;
                    }
                case "respawn":
                    {
                        CuiHelper.DestroyUi(player, "DuelistUI_Defeat");

                        if (!InEvent(player) && DuelTerritory(player.transform.position))
                            SendHome(player);

                        return;
                    }
                case "ready":
                case "readyon":
                case "readyoff":
                    {
                        ReadyUp(player);

                        if (DuelTerritory(player.transform.position))
                        {
                            CreateDefeatUI(player);
                            return;
                        }

                        break;
                    }
                case "tdm":
                    {
                        ToggleMatchUI(player);
                        break;
                    }
                case "kit":
                    {
                        if (arg.Args.Length != 2)
                            return;

                        var match = GetMatch(player);

                        if (match != null && match.IsHost(player))
                        {
                            if (!match.IsStarted)
                                match.Kit = GetVerifiedKit(arg.Args[1]);

                            break;
                        }

                        if (duelsData.CustomKits.ContainsKey(player.UserIDString) && duelsData.CustomKits[player.UserIDString] == arg.Args[1])
                        {
                            duelsData.CustomKits.Remove(player.UserIDString);
                            Message(player, msg("ResetKit", player.UserIDString));
                            break;
                        }

                        string kit = GetVerifiedKit(arg.Args[1]);

                        if (string.IsNullOrEmpty(kit))
                            break;

                        duelsData.CustomKits[player.UserIDString] = kit;
                        Message(player, msg("KitSet", player.UserIDString, kit));
                        break;
                    }
                case "joinmatch":
                    {
                        if (arg.Args.Length != 2)
                            return;

                        if (IsDueling(player))
                            break;

                        var match = GetMatch(player);

                        if (match != null)
                        {
                            if (match.IsStarted)
                                break;

                            match.RemoveMatchPlayer(player);
                        }

                        var newMatch = tdmMatches.FirstOrDefault(x => x.Id == arg.Args[1] && x.IsPublic);

                        if (newMatch == null || newMatch.IsFull() || newMatch.IsStarted || newMatch.IsOver)
                        {
                            Message(player, msg("MatchNoLongerValid", player.UserIDString));
                            break;
                        }

                        if (newMatch.GetTeam(player) != Team.None)
                            break;

                        newMatch.AddMatchPlayer(player, !newMatch.IsFull(Team.Good) ? Team.Good : Team.Evil);

                        if (matchesUI.Contains(player.UserIDString))
                        {
                            CuiHelper.DestroyUi(player, "DuelistUI_Matches");
                            matchesUI.Remove(player.UserIDString);
                        }

                        break;
                    }
                case "size":
                    {
                        if (arg.Args.Length != 2 || !arg.Args[1].All(char.IsDigit))
                            break;

                        cmdTDM(player, szMatchChatCommand, new[] { "size", arg.Args[1] });
                        break;
                    }
                case "any":
                    {
                        cmdTDM(player, szMatchChatCommand, new[] { "any" });
                        break;
                    }
            }

            RefreshUI(player);
        }

        public void DestroyAllUI()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
            }
        }

        public bool DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "DuelistUI_Options");
            CuiHelper.DestroyUi(player, "DuelistUI_Kits");
            CuiHelper.DestroyUi(player, "DuelistUI_Matches");
            CuiHelper.DestroyUi(player, "DuelistUI_Announcement");
            CuiHelper.DestroyUi(player, "DuelistUI_Defeat");
            CuiHelper.DestroyUi(player, "DuelistUI_Countdown");

            if (readyUiList.Contains(player.UserIDString))
            {
                CuiHelper.DestroyUi(player, "DuelistUI_Ready");
                readyUiList.Remove(player.UserIDString);
            }

            if (duelistUI.Contains(player.UserIDString))
            {
                duelistUI.Remove(player.UserIDString);
                return true;
            }

            return false;
        }

        public void ccmdDUI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (IsNull(player))
                return;

            if (arg.HasArgs(1))
            {
                switch (arg.Args[0].ToLower())
                {
                    case "on":
                        {
                            cmdDUI(player, szUIChatCommand, new string[0]);
                            return;
                        }
                    case "off":
                        {
                            DestroyUI(player);
                            return;
                        }
                }
            }

            if (duelistUI.Contains(player.UserIDString))
                DestroyUI(player);
            else
                cmdDUI(player, szUIChatCommand, new string[0]);
        }

        public void cmdDUI(BasePlayer player, string command, string[] args)
        {
            DestroyUI(player);
            var buttons = new List<string>
            {
                "UI_Accept",
                "UI_Decline",
                "UI_Kits",
                "UI_Public",
                "UI_Queue",
                "UI_TDM",
                "UI_Any",
                duelsData.AutoReady.Contains(player.UserIDString) ? "UI_ReadyOn" : "UI_ReadyOff",
            };
            var element = UI.CreateElementContainer("DuelistUI_Options", "0 0 0 0.5", "0.915 0.148", "0.981 0.441", guiUseCursor);

            if (guiUseCloseButton)
                UI.CreateButton(ref element, "DuelistUI_Options", "0.29 0.49 0.69 0.5", "X", 14, "0.7 0.9", "0.961 0.98", "UI_DuelistCommand closeui");

            for (int number = 0; number < buttons.Count; number++)
            {
                var pos = UI.CalcButtonPos(number + 1, 2.075f);
                string uicommand = buttons[number].Replace("UI_", "").ToLower();
                string text = msg(buttons[number], player.UserIDString);
                UI.CreateButton(ref element, "DuelistUI_Options", "0.29 0.49 0.69 0.5", text, 14, $"{pos[0]} {pos[1]}", $"{pos[2]} {pos[3]}", $"UI_DuelistCommand {uicommand}");
            }

            if (!duelistUI.Contains(player.UserIDString))
                duelistUI.Add(player.UserIDString);

            CuiHelper.AddUi(player, element);
        }

        public void RefreshUI(BasePlayer player)
        {
            cmdDUI(player, szUIChatCommand, new string[0]);

            if (kitsUI.Contains(player.UserIDString))
            {
                kitsUI.Remove(player.UserIDString);
                ToggleKitUI(player);
            }
            if (matchesUI.Contains(player.UserIDString))
            {
                matchesUI.Remove(player.UserIDString);
                ToggleMatchUI(player);
            }
        }

        public void ToggleMatchUI(BasePlayer player)
        {
            if (matchesUI.Contains(player.UserIDString))
            {
                CuiHelper.DestroyUi(player, "DuelistUI_Matches");
                matchesUI.Remove(player.UserIDString);
                return;
            }

            if (kitsUI.Contains(player.UserIDString))
            {
                CuiHelper.DestroyUi(player, "DuelistUI_Kits");
                kitsUI.Remove(player.UserIDString);
            }

            var element = UI.CreateElementContainer("DuelistUI_Matches", "0 0 0 0.5", "0.669 0.148", "0.903 0.541");
            var matches = tdmMatches.Where(x => x.IsPublic && !x.IsStarted && !x.IsFull()).ToList();

            for (int number = 0; number < matches.Count; number++)
            {
                var pos = UI.CalcButtonPos(number);
                UI.CreateButton(ref element, "DuelistUI_Matches", "0.29 0.49 0.69 0.5", matches[number].Versus, 12, $"{pos[0]} {pos[1]}", $"{pos[2]} {pos[3]}", $"UI_DuelistCommand joinmatch {matches[number].Id}");
            }

            var match = GetMatch(player);
            string teamSize = msg("UI_TeamSize", player.UserIDString);

            for (int size = Math.Max(2, minDeathmatchSize); size < maxDeathmatchSize + 1; size++)
            {
                var pos = UI.CalcButtonPos(size + matches.Count);
                string color = match != null && match.TeamSize == size || size == minDeathmatchSize ? "0.69 0.49 0.29 0.5" : "0.29 0.49 0.69 0.5";
                UI.CreateButton(ref element, "DuelistUI_Matches", color, teamSize + size, 12, $"{pos[0]} {pos[1]}", $"{pos[2]} {pos[3]}", $"UI_DuelistCommand size {size}");
            }

            if (matches.Count == 0)
                UI.CreateLabel(ref element, "DuelistUI_Matches", "1 1 1 1", msg("NoMatchesExistYet", player.UserIDString), 14, "0.047 0.73", "1 0.89");

            CuiHelper.AddUi(player, element);
            matchesUI.Add(player.UserIDString);
        }

        public void ToggleKitUI(BasePlayer player)
        {
            if (kitsUI.Contains(player.UserIDString))
            {
                CuiHelper.DestroyUi(player, "DuelistUI_Kits");
                kitsUI.Remove(player.UserIDString);
                return;
            }

            if (matchesUI.Contains(player.UserIDString))
            {
                CuiHelper.DestroyUi(player, "DuelistUI_Matches");
                matchesUI.Remove(player.UserIDString);
            }

            var element = UI.CreateElementContainer("DuelistUI_Kits", "0 0 0 0.5", "0.669 0.148", "0.903 0.541");
            var kits = VerifiedKits;
            string kit = duelsData.CustomKits.ContainsKey(player.UserIDString) ? duelsData.CustomKits[player.UserIDString] : null;

            for (int number = 0; number < kits.Count; number++)
            {
                var pos = UI.CalcButtonPos(number);
                UI.CreateButton(ref element, "DuelistUI_Kits", kits[number] == kit ? "0.69 0.49 0.29 0.5" : "0.29 0.49 0.69 0.5", kits[number], 12, $"{pos[0]} {pos[1]}", $"{pos[2]} {pos[3]}", $"UI_DuelistCommand kit {kits[number]}");
            }

            CuiHelper.AddUi(player, element);
            kitsUI.Add(player.UserIDString);
        }

        public void CreateAnnouncementUI(BasePlayer player, string text)
        {
            if (guiAnnounceUITime <= 0f || IsNotConnected(player))
                return;

            var element = UI.CreateElementContainer("DuelistUI_Announcement", "0 0 0 0.5", "-0.027 0.92", "1.026 0.9643", false, "Hud");

            UI.CreateLabel(ref element, "DuelistUI_Announcement", "", text, 18, "0 0", "1 1");
            CuiHelper.DestroyUi(player, "DuelistUI_Announcement");
            CuiHelper.AddUi(player, element);

            timer.Once(guiAnnounceUITime, () => CuiHelper.DestroyUi(player, "DuelistUI_Announcement"));
        }

        public void CreateCountdownUI(BasePlayer player, string text)
        {
            var element = UI.CreateElementContainer("DuelistUI_Countdown", "0 0 0 0.5", "0.484 0.92", "0.527 0.9643", false, "Hud");

            UI.CreateLabel(ref element, "DuelistUI_Countdown", "1 0.1 0.1 1", text, 20, "0 0", "1 1");
            CuiHelper.DestroyUi(player, "DuelistUI_Countdown");
            CuiHelper.AddUi(player, element);
        }

        public void ToggleReadyUI(BasePlayer player)
        {
            if (IsNotConnected(player))
                return;

            if (readyUiList.Contains(player.UserIDString))
            {
                CuiHelper.DestroyUi(player, "DuelistUI_Ready");
                readyUiList.Remove(player.UserIDString);
                return;
            }

            var element = UI.CreateElementContainer("DuelistUI_Ready", "0 0 0 0.5", "0.475 0.158", "0.573 0.21");
            UI.CreateButton(ref element, "DuelistUI_Ready", "0.29 0.49 0.69 0.5", msg(duelsData.AutoReady.Contains(player.UserIDString) ? "UI_ReadyOn" : "UI_ReadyOff", player.UserIDString), 18, "0.016 0.081", "0.984 0.919", "UI_DuelistCommand ready");
            CuiHelper.AddUi(player, element);
            readyUiList.Add(player.UserIDString);
        }

        public void CreateDefeatUI(BasePlayer player)
        {
            if (IsNotConnected(player))
                return;

            var element = UI.CreateElementContainer("DuelistUI_Defeat", "0 0 0 0.5", "0.436 0.133", "0.534 0.307", guiUseCursor);

            UI.CreateButton(ref element, "DuelistUI_Defeat", "0.29 0.49 0.69 0.5", msg("UI_Respawn", player.UserIDString), 18, "0.016 0.679", "0.984 0.976", "UI_DuelistCommand respawn");
            UI.CreateButton(ref element, "DuelistUI_Defeat", "0.29 0.49 0.69 0.5", msg("UI_Requeue", player.UserIDString), 18, "0.016 0.357", "0.984 0.655", "UI_DuelistCommand requeue");
            UI.CreateButton(ref element, "DuelistUI_Defeat", "0.29 0.49 0.69 0.5", msg(duelsData.AutoReady.Contains(player.UserIDString) ? "UI_ReadyOn" : "UI_ReadyOff", player.UserIDString), 18, "0.016 0.024", "0.984 0.333", "UI_DuelistCommand ready");
            CuiHelper.DestroyUi(player, "DuelistUI_Defeat");
            CuiHelper.AddUi(player, element);

            if (readyUiList.Contains(player.UserIDString))
            {
                CuiHelper.DestroyUi(player, "DuelistUI_Ready");
                readyUiList.Remove(player.UserIDString);
            }
        }

        private void UpdateMatchUI()
        {
            if (!matchUpdateRequired)
                return;

            matchUpdateRequired = false;

            foreach (string userId in matchesUI.ToList())
            {
                matchesUI.Remove(userId);
                var player = BasePlayer.Find(userId);

                if (player != null && player.IsConnected)
                {
                    CuiHelper.DestroyUi(player, "DuelistUI_Matches");
                    ToggleMatchUI(player);
                }
            }
        }

        public class UI // Credit: Absolut
        {
            public static CuiElementContainer CreateElementContainer(string panelName, string color, string aMin, string aMax, bool cursor = false, string parent = "Overlay")
            {
                var NewElement = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Color = color
                            },
                            RectTransform =
                            {
                                AnchorMin = aMin,
                                AnchorMax = aMax
                            },
                            CursorEnabled = cursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
                return NewElement;
            }

            public static void CreateLabel(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text =
                    {
                        Color = color,
                        FontSize = size,
                        Align = align,
                        FadeIn = 1.0f,
                        Text = text
                    },
                    RectTransform =
                    {
                        AnchorMin = aMin,
                        AnchorMax = aMax
                    }
                },
                panel);
            }

            public static void CreateButton(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, string command, TextAnchor align = TextAnchor.MiddleCenter, string labelColor = "")
            {
                container.Add(new CuiButton
                {
                    Button =
                        {
                            Color = color,
                            Command = command,
                            FadeIn = 1.0f
                        },
                    RectTransform =
                        {
                            AnchorMin = aMin,
                            AnchorMax = aMax
                        },
                    Text =
                        {
                            Text = text,
                            FontSize = size,
                            Align = align,
                            Color = labelColor
                        }
                },
                    panel);
            }

            public static float[] CalcButtonPos(int number, float dMinOffset = 1f)
            {
                Vector2 position = new Vector2(0.03f, 0.889f);
                Vector2 dimensions = new Vector2(0.45f * dMinOffset, 0.1f);
                float offsetY = 0;
                float offsetX = 0;
                if (number >= 0 && number < 9)
                {
                    offsetY = (-0.01f - dimensions.y) * number;
                }
                else if (number > 8 && number < 19)
                {
                    offsetY = (-0.01f - dimensions.y) * (number - 9);
                    offsetX = (0.04f + dimensions.x) * 1;
                }
                else if (number > 18 && number < 29)
                {
                    offsetY = (-0.01f - dimensions.y) * (number - 19);
                    offsetX = (0.08f + dimensions.x) * 1;
                }
                Vector2 offset = new Vector2(offsetX, offsetY);
                Vector2 posMin = position + offset;
                Vector2 posMax = posMin + dimensions;
                return new[] { posMin.x, posMin.y, posMax.x, posMax.y };
            }
        }

        #endregion

        #region Config

        private bool Changed;
        private string szMatchChatCommand;
        private string szDuelChatCommand;
        private string szQueueChatCommand;
        private const string duelistPerm = "duelist.dd";
        private const string duelistGroup = "duelist";
        private float zoneRadius;
        private int deathTime;
        private int immunityTime;
        private int zoneCounter;
        private List<string> _hpDuelingKits = new List<string>();
        private List<string> _lpDuelingKits = new List<string>();
        private List<string> hpDuelingKits = new List<string>();
        private List<string> lpDuelingKits = new List<string>();
        private List<BetInfo> duelingBets = new List<BetInfo>();
        private bool recordStats = true;
        private int permsToGive = 3;
        private float maxIncline;
        private bool allowBetForfeit;
        private bool allowBetRefund;
        private bool allowBets;
        private bool putToSleep;
        private bool blockSpawning;
        private bool killNpc;
        private float announceTime;
        private bool removePlayers;
        private bool useAnnouncement;
        private bool autoSetup;
        private bool broadcastDefeat;
        private double economicsMoney;
        private double requiredDuelMoney;
        private int serverRewardsPoints;
        private float damageScaleAmount;
        private int zoneAmount;
        private int playersPerZone;
        private bool visibleToAdmins;
        private float spDrawTime;
        private float spRemoveOneMaxDistance;
        private float spRemoveAllMaxDistance;
        private bool spAutoRemove;
        private bool avoidWaterSpawns;
        private int extraWallStacks;
        private bool useZoneWalls;
        private bool zoneUseWoodenWalls;
        private float buildingBlockExtensionRadius;
        private bool autoAllowAll;
        private bool useRandomSkins;
        private float playerHealth;
        private bool dmFF;
        private int minDeathmatchSize;
        private int maxDeathmatchSize;
        private bool autoEnable;
        private ulong teamGoodShirt;
        private ulong teamEvilShirt;
        private string teamShirt;
        private double teamEconomicsMoney;
        private int teamServerRewardsPoints;
        private float lesserKitChance;
        private bool tdmEnabled;
        private bool useLeastAmount;
        private bool tdmServerDeaths;
        private bool tdmMatchDeaths;
        private List<string> whitelistCommands = new List<string>();
        private bool useWhitelistCommands;
        private List<string> blacklistCommands = new List<string>();
        private bool useBlacklistCommands;
        private bool bypassNewmans;
        private bool saveRestoreEnabled;
        private List<DuelKitItem> respawnLoot = new List<DuelKitItem>();
        private bool respawnDeadDisconnect;
        private bool sendDeadHome;
        private bool resetSeed;
        private bool noStability;
        private bool noMovement;
        private bool requireTeamSize;
        private int requiredMinSpawns;
        private int requiredMaxSpawns;
        private bool guiAutoEnable;
        private bool guiUseCursor;
        private string szUIChatCommand;
        private bool useWorkshopSkins;
        private bool respawnWalls;
        private bool allowPlayerDeaths;
        private bool morphBarricadesStoneWalls;
        private bool morphBarricadesWoodenWalls;
        private bool guiUseCloseButton;
        private string autoKitName;
        private float guiAnnounceUITime;
        private bool sendDefeatedHome;
        private bool sendHomeRequeue;
        private bool sendHomeSpectatorWhenRematchTimesOut;
        private bool autoFlames;
        private bool autoOvens;
        private bool autoTurrets;
        private int sphereAmount;
        private bool wipeDuelZones;
        private bool setPlayerTime;
        private ulong chatSteamID;

        private List<object> RespawnLoot
        {
            get
            {
                return new List<object>
                {
                    new DuelKitItem
                    {
                        shortname = "rock",
                        amount = 1,
                        skin = 0,
                        container = "belt",
                        slot = -1
                    },
                    new DuelKitItem
                    {
                        shortname = "torch",
                        amount = 1,
                        skin = 0,
                        container = "belt",
                        slot = -1
                    }
                };
            }
        }

        private List<object> BlacklistedCommands
        {
            get
            {
                return new List<object>
                {
                    "/tp",
                    "/remove",
                    "/bank",
                    "/shop",
                    "/event",
                    "/rw",
                    "/home",
                    "/trade"
                };
            }
        }

        private List<object> WhitelistedCommands
        {
            get
            {
                return new List<object>
                {
                    "/report",
                    "/pm",
                    "/r",
                    "/help"
                };
            }
        }

        private List<object> DefaultBets
        {
            get
            {
                return new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["trigger"] = "stone",
                        ["max"] = 50000,
                        ["itemid"] = -2099697608
                    },
                    new Dictionary<string, object>
                    {
                        ["trigger"] = "sulfur",
                        ["max"] = 50000,
                        ["itemid"] = -1581843485
                    },
                    new Dictionary<string, object>
                    {
                        ["trigger"] = "fragment",
                        ["max"] = 50000,
                        ["itemid"] = 69511070
                    },
                    new Dictionary<string, object>
                    {
                        ["trigger"] = "charcoal",
                        ["max"] = 50000,
                        ["itemid"] = -1938052175
                    },
                    new Dictionary<string, object>
                    {
                        ["trigger"] = "gp",
                        ["max"] = 25000,
                        ["itemid"] = -265876753
                    },
                    new Dictionary<string, object>
                    {
                        ["trigger"] = "hqm",
                        ["max"] = 1000,
                        ["itemid"] = 317398316
                    },
                    new Dictionary<string, object>
                    {
                        ["trigger"] = "c4",
                        ["max"] = 10,
                        ["itemid"] = 1248356124
                    },
                    new Dictionary<string, object>
                    {
                        ["trigger"] = "rocket",
                        ["max"] = 6,
                        ["itemid"] = -742865266
                    }
                };
            }
        }

        private List<object> DefaultLesserKits
        {
            get
            {
                return new List<object>
                {
                    "kit_4",
                    "kit_5",
                    "kit_6"
                };
            }
        }

        private List<object> DefaultKits
        {
            get
            {
                return new List<object>
                {
                    "kit_1",
                    "kit_2",
                    "kit_3"
                };
            }
        }

        private static Dictionary<string, List<DuelKitItem>> customKits = new Dictionary<string, List<DuelKitItem>>();

        private Dictionary<string, object> DefaultCustomKits
        {
            get
            {
                return new Dictionary<string, object>
                {
                    ["Hunting Bow"] = new List<object>
                    {
                        new DuelKitItem
                        {
                            shortname = "bow.hunting",
                            amount = 1,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "arrow.wooden",
                            amount = 50,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "spear.stone",
                            amount = 1,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "bandage",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "syringe.medical",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "largemedkit",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "burlap.gloves",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "burlap.headwrap",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "burlap.shirt",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "burlap.shoes",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "burlap.trousers",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        }
                    },
                    ["Assault Rifle and Bolt Action Rifle"] = new List<object>
                    {
                        new DuelKitItem
                        {
                            shortname = "rifle.ak",
                            amount = 1,
                            skin = 0,
                            container = "belt",
                            slot = -1,
                            ammo = "ammo.rifle",
                            mods = new List<string>
                            {
                                "weapon.mod.lasersight"
                            }
                        },
                        new DuelKitItem
                        {
                            shortname = "rifle.bolt",
                            amount = 1,
                            skin = 0,
                            container = "belt",
                            slot = -1,
                            ammo = "ammo.rifle",
                            mods = new List<string>
                            {
                                "weapon.mod.lasersight",
                                "weapon.mod.small.scope"
                            }
                        },
                        new DuelKitItem
                        {
                            shortname = "largemedkit",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "bandage",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "syringe.medical",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "bearmeat.cooked",
                            amount = 10,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "hoodie",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "metal.facemask",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "metal.plate.torso",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "pants",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "burlap.gloves",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "shoes.boots",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "ammo.rifle",
                            amount = 200,
                            skin = 0,
                            container = "main",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "weapon.mod.flashlight",
                            amount = 1,
                            skin = 0,
                            container = "main",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "weapon.mod.small.scope",
                            amount = 1,
                            skin = 0,
                            container = "main",
                            slot = -1
                        }
                    },
                    ["Semi-Automatic Pistol"] = new List<object>
                    {
                        new DuelKitItem
                        {
                            shortname = "pistol.semiauto",
                            amount = 1,
                            skin = 0,
                            container = "belt",
                            slot = -1,
                            ammo = "ammo.pistol",
                            mods = new List<string>
                            {
                                "weapon.mod.lasersight"
                            }
                        },
                        new DuelKitItem
                        {
                            shortname = "largemedkit",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "bandage",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "syringe.medical",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "bearmeat.cooked",
                            amount = 10,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "hoodie",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "metal.facemask",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "metal.plate.torso",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "pants",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "burlap.gloves",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "shoes.boots",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "ammo.pistol",
                            amount = 200,
                            skin = 0,
                            container = "main",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "weapon.mod.flashlight",
                            amount = 1,
                            skin = 0,
                            container = "main",
                            slot = -1
                        }
                    },
                    ["Pump Shotgun"] = new List<object>
                    {
                        new DuelKitItem
                        {
                            shortname = "shotgun.pump",
                            amount = 1,
                            skin = 0,
                            container = "belt",
                            slot = -1,
                            ammo = "ammo.shotgun.slug",
                            mods = new List<string>
                            {
                                "weapon.mod.lasersight"
                            }
                        },
                        new DuelKitItem
                        {
                            shortname = "largemedkit",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "bandage",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "syringe.medical",
                            amount = 5,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "bearmeat.cooked",
                            amount = 10,
                            skin = 0,
                            container = "belt",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "hoodie",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "metal.facemask",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "metal.plate.torso",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "pants",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "burlap.gloves",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "shoes.boots",
                            amount = 1,
                            skin = 0,
                            container = "wear",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "ammo.shotgun.slug",
                            amount = 200,
                            skin = 0,
                            container = "main",
                            slot = -1
                        },
                        new DuelKitItem
                        {
                            shortname = "weapon.mod.flashlight",
                            amount = 1,
                            skin = 0,
                            container = "main",
                            slot = -1
                        }
                    }
                };
            }
        }

        protected override void LoadDefaultMessages() // holy shit this took forever.
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Awards"] = "{0} ({1}) duels won {2}",
                ["Granted"] = "Granted {0} ({1}) permission {2} for group {3}",
                ["Logged"] = "Duelists have been logged to: {0}",
                ["Indestructible"] = "This object belongs to the server and is indestructible!",
                ["Building is blocked!"] = "<color=red>Building is blocked inside of dueling zones!</color>",
                ["TopAll"] = "[ <color=#ffff00>Top Duelists Of All Time ({0})</color> ]:",
                ["Top"] = "[ <color=#ffff00>Top Duelists ({0})</color> ]:",
                ["NoLongerQueued"] = "You are no longer in queue for a duel.",
                ["InQueueSuccess"] = "You are now in queue for a duel. You will teleport instantly when a match is available.",
                ["MustBeNaked"] = "<color=red>You must be naked before you can duel.</color>",
                ["AlreadyInADuel"] = "You cannot queue for a duel while already in a duel!",
                ["MustAllowDuels"] = "You must allow duels first! Type: <color=orange>/{0} allow</color>",
                ["DuelsDisabled"] = "Duels are disabled.",
                ["NoZoneExists"] = "No dueling zone exists.",
                ["Banned"] = "You are banned from duels.",
                ["FoundZone"] = "Took {0} tries ({1}ms) to get a dueling zone.",
                ["ImmunityFaded"] = "Your immunity has faded.",
                ["NotifyBetWon"] = "You have won your bet! To claim type <color=orange>/{0} claim</color>.",
                ["ConsoleBetWon"] = "{0} ({1}) won his bet against {2} ({3})!",
                ["DuelDeathMessage"] = "<color=silver><color=lime>{0}</color> (<color=lime>W</color>: <color=orange>{1}</color> / <color=red>L</color>: <color=orange>{2}</color>) has defeated <color=lime>{3}</color> (<color=lime>W</color>: <color=orange>{4}</color> / <color=red>L</color>: <color=orange>{5}</color>) in a duel with <color=green>{6}</color> health left.{7}</color>",
                ["BetWon"] = " Bet won: <color=lime>{0}</color> (<color=lime>{1}</color>)",
                ["ExecutionTime"] = "You have <color=red>{0} minutes</color> to win the duel before you are executed.",
                ["FailedZone"] = "Failed to create a dueling zone, please try again.",
                ["FailedSetup"] = "Failed to setup the zone, please try again.",
                ["FailedRaycast"] = "Look towards the ground, and try again.",
                ["BetPlaced"] = "Your bet {0} ({1}) has been placed.",
                ["BetForfeitSuffix"] = " Type <color=orange>/{0} bet forfeit</color> to forfeit your bet.",
                ["BetRefundSuffix"] = " Type <color=orange>/{0} bet refund</color> to refund your bet.",
                ["BetNotEnough"] = "Bet cancelled. You do not have enough to bet this amount!",
                ["BetZero"] = "Bet cancelled. You do not have this item in your inventory.",
                ["DuelAnnouncement"] = "Type <color=orange>/{duelChatCommand}</color> for information on the dueling system. See your standing on the leaderboard by using <color=orange>/{ladderCommand}</color>. Type <color=orange>/{queueCommand}</color> to enter the dueling queue now!",
                ["DuelAnnouncementBetsSuffix"] = " Feeling lucky? Use <color=orange>/{0} bet</color> to create a bet!",
                ["ZoneCreated"] = "Dueling zone created successfully.",
                ["RemovedZone"] = "Removed dueling zone.",
                ["RemovedBan"] = "Unbanned {0}",
                ["AddedBan"] = "Banned {0}",
                ["PlayerNotFound"] = "{0} not found. Try being more specific or use a steam id.",
                ["RequestTimedOut"] = "Request timed out to duel <color=lime>{0}</color>",
                ["RemovedFromQueueRequest"] = "You have been removed from the dueling queue since you have requested to duel another player.",
                ["RemovedFromDuel"] = "You have been removed from your duel.",
                ["BetsDoNotMatch"] = "Your bet {0} ({1}) does not match {2} ({3})",
                ["InvalidBet"] = "Invalid bet '{0}'",
                ["BetSyntax"] = "Syntax: /{0} bet <item> <amount> - resources must be refined",
                ["AvailableBets"] = "Available Bets:",
                ["MustHaveSameBet"] = "{0} is betting: {1} ({2}). You must have the same bet to duel this player.",
                ["NoBetsToRefund"] = "There are no bets to refund.",
                ["Disabled"] = "Disabled",
                ["HelpDuelBet"] = "<color=silver><color=orange>/{0} bet</color> - place a bet towards your next duel.</color>",
                ["HelpDuelAdmin"] = "<color=orange>Admin: /{0} on|off</color> - enable/disable duels",
                ["HelpDuelAdminRefundAll"] = "<color=orange>Admin: /{0} bet refundall</color> - refund all bets for all players",
                ["DuelsDisabledAlready"] = "Duels are already disabled!",
                ["DuelsNowDisabled"] = "Duels disabled. Sending duelers home.",
                ["DuelsEnabledAlready"] = "Duels are already enabled!",
                ["DuelsNowEnabled"] = "Duels enabled",
                ["NoBetsToClaim"] = "You have no bets to claim.",
                ["PlayerClaimedBet"] = "Claimed bet {0} ({1})",
                ["AllBetsClaimed"] = "You have claimed all of your bets.",
                ["DuelChatOff"] = "You will no longer see duel death messages.",
                ["DuelChatOn"] = "You will now see duel death messages.",
                ["PlayerRequestsOn"] = "Players may now request to duel you. You will be removed from this list if you do not duel.",
                ["PlayerRequestsOff"] = "Players may no longer request to duel you.",
                ["BlockedRequestsFrom"] = "Blocked duel requests from: <color=lime>{0}</color>",
                ["UnblockedRequestsFrom"] = "Removed block on duel requests from: <color=lime>{0}</color>",
                ["AlreadyBlocked"] = "You have already blocked players from requesting duels.",
                ["NoBetsConfigured"] = "No bets are configured.",
                ["RefundAllPlayerNotice"] = "Server administrator has refunded your bet: {0} ({1})",
                ["RefundAllAdminNotice"] = "Refunded {0} ({1}): {2} ({3})",
                ["BetsRemaining"] = "Bet items remaining in database: {0}",
                ["AllBetsRefunded"] = "All dueling bets refunded",
                ["CannotForfeit"] = "You cannot forfeit bets on this server.",
                ["CannotForfeitRequestDuel"] = "You cannot forfeit a bet while requesting a duel!",
                ["CannotForfeitInDuel"] = "You cannot forfeit a bet while dueling!",
                ["CannotRefundRequestDuel"] = "You cannot refund a bet while requesting a duel!",
                ["CannotRefundInDuel"] = "You cannot refund a bet while dueling!",
                ["BetForfeit"] = "You forfeit your bet!",
                ["NoBetToForfeit"] = "You do not have an active bet to forfeit.",
                ["NoBetToRefund"] = "You do not have an active bet to refund.",
                ["CannotRefund"] = "You cannot refund bets on this server.",
                ["BetRefunded"] = "You have refunded your bet.",
                ["AlreadyBetting"] = "You are already betting! Your bet: {0} ({1})",
                ["ToRefundUse"] = "To refund your bet, type: <color=orange>/{0} bet refund</color>",
                ["ToForfeitUse"] = "To forfeit your bet, type: <color=orange>/{0} bet forfeit</color>. Refunds are not allowed.",
                ["InvalidNumber"] = "Invalid number: {0}",
                ["MultiplesOnly"] = "Number must be a multiple of 500. ie: 500, 1000, 2000, 5000, 10000, 15000",
                ["NoRequestsReceived"] = "No players have requested a duel with you.",
                ["DuelCancelledFor"] = "<color=lime>{0}</color> has cancelled the duel!",
                ["NoPendingRequests"] = "You have no pending request to cancel.",
                ["DuelCancelledWith"] = "<color=lime>{0}</color> has cancelled the duel request.",
                ["DuelCancelComplete"] = "Duel request cancelled.",
                ["MustWaitToRequestAgain"] = "You must wait <color=red>{0} minute(s)</color> from the last time you requested a duel to request another.",
                ["AlreadyDueling"] = "You are already dueling another player!",
                ["CannotRequestThisPlayer"] = "You are not allowed to request duels with this player.",
                ["TargetAlreadyDueling"] = "<color=lime>{0}</color> is already dueling another player!",
                ["NotAllowedYet"] = "<color=lime>{0}</color> has not enabled duel requests yet. They must type <color=orange>/{1} allow</color>",
                ["MustWaitForAccept"] = "You have requested a duel with <color=lime>{0}</color> already. You must wait for this player to accept the duel.",
                ["PendingRequestAlready"] = "This player has a duel request pending already.",
                ["TargetHasNoBet"] = "You have an active bet going. <color=lime>{0}</color> must have the same bet to duel you.",
                ["YourBet"] = "Your bet: {0} ({1})",
                ["WoundedQueue"] = "You cannot duel while either player is wounded.",
                ["DuelMustBeNaked"] = "Duel cancelled: <color=lime>{0}</color> inventory is not empty.",
                ["LadderLife"] = "<color=#5A625B>Use <color=yellow>/{0} ladder life</color> to see all time stats</color>",
                ["EconomicsDeposit"] = "You have received <color=yellow>${0}</color>!",
                ["ServerRewardPoints"] = "You have received <color=yellow>{0} RP</color>!",
                ["DuelsMustBeEnabled"] = "Use '/{0} on' to enable dueling on the server.",
                ["DataSaved"] = "Data has been saved.",
                ["DuelsNowDisabledEmpty"] = "Duels disabled.",
                ["CannotTeleport"] = "You are not allowed to teleport from a dueling zone.",
                ["AllZonesFull"] = "All zones are currently full. Zones: {0}. Limit Per Zone: {1}",
                ["NoZoneFound"] = "No zone found. You must stand inside of the zone to remove it.",
                ["RemovedZoneAt"] = "Removed zone at {0}",
                ["CannotDuel"] = "You are not allowed to duel at the moment.",
                ["LeftZone"] = "<color=red>You were found outside of the dueling zone while dueling. Your items have been removed.</color>",
                ["SpawnAdd"] = "<color=orange>/{0} spawns add</color> - add a spawn point at the position you are looking at.",
                ["SpawnHere"] = "<color=orange>/{0} spawns here</color> - add a spawn point at your position.",
                ["SpawnRemove"] = "<color=orange>/{0} spawns remove</color> - removes the nearest spawn point within <color=orange>{1}m</color>.",
                ["SpawnRemoveAll"] = "<color=orange>/{0} spawns removeall</color> - remove all spawn points within <color=orange>{1}m</color>.",
                ["SpawnWipe"] = "<color=orange>/{0} spawns wipe</color> - wipe all spawn points.",
                ["SpawnWiped"] = "<color=red>{0}</color> spawns points wiped.",
                ["SpawnCount"] = "<color=green>{0}</color> spawn points in database.",
                ["SpawnNoneFound"] = "No custom spawn points found within <color=orange>{0}m</color>.",
                ["SpawnAdded"] = "Spawn point added at {0}",
                ["SpawnRemoved"] = "Removed <color=red>{0}</color> spawn(s)",
                ["SpawnExists"] = "This spawn point exists already.",
                ["SpawnNoneExist"] = "No spawn points exist.",
                ["ZoneExists"] = "A dueling zone already exists here.",
                ["ZoneLimit"] = "Zone limit reached ({0}). You must manually remove an existing zone before creating a new one.",
                ["CannotEventJoin"] = "You are not allowed to join this event while dueling.",
                ["KitDoesntExist"] = "This kit doesn't exist: {0}",
                ["KitSet"] = "Custom kit set to {0}. This kit will be used when both players have the same custom kit.",
                ["KitsNotConfigured"] = "No kits have been configured for dueling.",
                ["RemovedXWalls"] = "Removed {0} walls.",
                ["SupportCreated"] = "{0} new dueling zones were created, however the total amount was not met. Please lower the radius, increase Maximum Incline On Hills, or reload the plugin to try again.",
                ["SupportInvalidConfig"] = "Invalid zone radius detected in the configuration file for this map size. Please lower the radius, increase Maximum Incline On Hills, or reload the plugin to try again.",
                ["WallSyntax"] = "Use <color=orange>/{0} walls [radius] <wood|stone></color>, or stand inside of an existing area with walls and use <color=orange>/{0} walls</color> to remove them.",
                ["GeneratedWalls"] = "Generated {0} arena walls {1} high at {2} in {3}ms",
                ["ResetKit"] = "You are no longer using a custom kit.",
                ["HelpDuels"] = "<color=#183a0e><size=18>DUELIST ({0})</size></color><color=#5A625B>\nDuel other players.</color>",
                ["HelpAllow"] = "<color=#5A397A>/{0} allow</color><color=#5A625B> • Toggle requests for duels</color>",
                ["HelpBlock"] = "<color=#5A397A>/{0} block <name></color><color=#5A625B> • Toggle block requests for a player</color>",
                ["HelpChallenge"] = "<color=#5A397A>/{0} <name></color><color=#5A625B> • Challenge another player</color>",
                ["HelpAccept"] = "<color=#5A397A>/{0} accept</color><color=#5A625B> • Accept a challenge</color>",
                ["HelpCancel"] = "<color=#5A397A>/{0} cancel</color><color=#5A625B> • Cancel your duel request</color>",
                ["HelpQueue"] = "<color=#5A397A>/{0}</color><color=#5A625B> • Join duel queue</color>",
                ["HelpChat"] = "<color=#5A397A>/{0} chat</color><color=#5A625B> • Toggle duel death messages</color>",
                ["HelpLadder"] = "<color=#5A397A>/{0} ladder</color><color=#5A625B> • Show top 10 duelists</color>",
                ["HelpBet"] = "<color=#5A397A>/{0} bet</color><color=#5A625B> • Place a bet towards a duel</color>",
                ["TopFormat"] = "<color=#666666><color=#5A625B>{0}.</color> <color=#00FF00>{1}</color> (<color=#008000>W:{2}</color> • <color=#ff0000>L:{3} </color> • <color=#4c0000>WLR:{4}</color>)</color>",
                ["NowDueling"] = "<color=#ff0000>You are now dueling <color=#00FF00>{0}</color>!</color>",
                ["MoneyRequired"] = "Both players must be able to pay an entry fee of <color=#008000>${0}</color> to duel.",
                ["CannotShop"] = "You are not allowed to shop while dueling.",
                ["DuelRequestSent"] = "Sent request to duel <color=lime>{0}</color>. Request expires in 1 minute. Use <color=orange>/{1} cancel</color> to cancel this request.",
                ["DuelRequestReceived"] = "<color=lime>{0}</color> has requested a duel. You have 1 minute to type <color=orange>/{1} accept</color> to accept the duel, or use <color=orange>/{1} decline</color> to decline immediately.",
                ["MatchQueued"] = "You have entered the deathmatch queue. The match will start when a dueling zone becomes available.",
                ["MatchTeamed"] = "You are not allowed to do this while on a deathmatch team.",
                ["MatchNoMatchesExist"] = "No matches exist. Challenge a player by using <color=orange>/{0} name</color>",
                ["MatchStarted"] = "Your match is starting versus: <color=yellow>{0}</color>",
                ["MatchStartedAlready"] = "Your match has already started. You must wait for it to end.",
                ["MatchPlayerLeft"] = "You have removed yourself from your deathmatch team.",
                ["MatchCannotChallenge"] = "{0} is already in a match.",
                ["MatchCannotChallengeAgain"] = "You can only challenge one player at a time.",
                ["MatchRequested"] = "<color=lime>{0}</color> has requested a deathmatch. Use <color=orange>/{1} accept</color> to accept this challenge.",
                ["MatchRequestSent"] = "Match request sent to <color=lime>{0}</color>.",
                ["MatchNoneRequested"] = "No one has challenged you to a deathmatch yet.",
                ["MatchPlayerOffline"] = "The player challenging you is no longer online.",
                ["MatchSizeChanged"] = "Deathmatch changed to <color=yellow>{0}v{0}</color>.",
                ["MatchOpened"] = "Your deathmatch is now open for private invitation. Friends may use <color=orange>/{0} any</color>, and players may use <color=orange>/{0} {1}</color> to join your team. Use <color=orange>/{0} public</color> to toggle invitations as public or private.",
                ["MatchCancelled"] = "{0} has cancelled the deathmatch.",
                ["MatchNotAHost"] = "You must be a host of a deathmatch to use this command.",
                ["MatchDoesntExist"] = "You are not in a deathmatch. Challenge a player by using <color=orange>/{0} name</color>.",
                ["MatchSizeSyntax"] = "Invalid syntax, use /{0} size #",
                ["MatchTeamFull"] = "Team is full ({0} players)",
                ["MatchJoinedTeam"] = "{0} joined {1} ({2}/{3}). {4} ({5}/{3})",
                ["MatchNoPlayersLeft"] = "No players are left on the opposing team. Match cancelled.",
                ["MatchChallenge2"] = "<color=#5A397A>/{0} any</color><color=#5A625B> • Join any match where a friend is the host</color>",
                ["MatchChallenge3"] = "<color=#5A397A>/{0} <code></color><color=#5A625B> • Join a match with the provided code</color>",
                ["MatchAccept"] = "<color=#5A397A>/{0} accept</color><color=#5A625B> • Accept a challenge</color>",
                ["MatchCancel"] = "<color=#5A397A>/{0} cancel</color><color=#5A625B> • Cancel your match request</color>",
                ["MatchLeave"] = "<color=#5A397A>/{0} cancel</color><color=#5A625B> • Leave your match</color>",
                ["MatchSize"] = "<color=#5A397A>/{0} size #</color><color=#5A625B> • Set your match size ({1}v{1}) [Hosts Only]</color>",
                ["MatchKickBan"] = "<color=#5A397A>/{0} kickban id/name</color><color=#5A625B> • Kickban a player from the match [Host Only]</color>",
                ["MatchSetCode"] = "<color=#5A397A>/{0} setcode [code]</color><color=#5A625B> • Change or see your code [Host Only]</color>",
                ["MatchTogglePublic"] = "<color=#5A397A>/{0} public</color><color=#5A625B> • Toggle match as public or private invitation [Host Only]</color>",
                ["MatchDefeat"] = "<color=silver><color=lime>{0}</color> has defeated <color=lime>{1}</color> in a <color=yellow>{2}v{2}</color> deathmatch!</color>",
                ["MatchIsNotNaked"] = "Match cannot start because <color=lime>{0}</color> is not naked. Next queue check in 30 seconds.",
                ["MatchCannotBan"] = "You cannot ban this player, or this player is already banned.",
                ["MatchBannedUser"] = "You have banned <color=lime>{0}</color> from your team.",
                ["MatchPlayerNotFound"] = "<color=lime>{0}</color> is not on your team.",
                ["MatchCodeIs"] = "Your code is: {0}",
                ["InQueueList"] = "Players in the queue:",
                ["HelpTDM"] = "<color=#5A397A>/{0}</color><color=#5A625B> • Create a team deathmatch</color>",
                ["InMatchListGood"] = "Good Team: {0}",
                ["InMatchListEvil"] = "Evil Team: {0}",
                ["MatchNoTeamFoundCode"] = "No team could be found for you with the provided code: {0}",
                ["MatchNoTeamFoundAny"] = "No team could be found with a friend as the host. Use a code instead.",
                ["MatchPublic"] = "Your match is now open to the public.",
                ["MatchPrivate"] = "Your match is now private and requires a code, or to be a friend to join.",
                ["CannotBank"] = "You are not allowed to bank while dueling.",
                ["TargetMustBeNaked"] = "<color=red>The person you are challenging must be naked before you can challenge them.</color>",
                ["MatchKit"] = "<color=#5A397A>/{0} kit <name></color><color=#5A625B> • Changes the kit used [Host Only]</color>",
                ["MatchKitSet"] = "Kit set to: <color=yellow>{0}</color>",
                ["MatchChallenge0"] = "<color=#5A397A>/{0} <name> [kitname]</color><color=#5A625B> • Challenge another player and set the kit if specified</color>",
                ["MatchPlayerDefeated"] = "<color=silver><color=lime>{0}</color> was killed by <color=lime>{1}</color> using <color=red>{2}</color> (<color=red>{3}: {4}m</color>)</color>",
                ["CommandNotAllowed"] = "You are not allowed to use this command right now.",
                ["HelpKit"] = "<color=#5A397A>/{0} kit</color><color=#5A625B> • Pick a kit</color>",
                ["RemovedXWallsCustom"] = "Removed {0} walls due to the deletion of zones which exceed the Max Zone cap.",
                ["ZonesSetup"] = "Initialized {0} existing dueling zones.",
                ["ArenasSetup"] = "{0} existing arenas are now protected.",
                ["NoPendingRequests2"] = "You have no pending request to accept.",
                ["MatchNoLongerValid"] = "You cannot join this match anymore.",
                ["NoMatchesExistYet"] = "No matches exist yet.",
                ["UI_Accept"] = "Accept",
                ["UI_Decline"] = "Decline",
                ["UI_Kits"] = "Kits",
                ["UI_Public"] = "Public",
                ["UI_Queue"] = "Queue",
                ["UI_TDM"] = "TDM",
                ["UI_TeamSize"] = "Set Team Size: ",
                ["UI_Any"] = "Exists",
                ["UI_Help"] = "<color=#5A397A>/{0}</color><color=#5A625B> • Show Duelist User Interface</color>",
                ["ResetSeed"] = "Stats for this seed have been reset.",
                ["RematchNone"] = "No rematches are available for you.",
                ["RematchNotify"] = "A rematch is available for {0} seconds. Click Ready to join, or type /{1} ready",
                ["UI_Ready"] = "Ready",
                ["RematchAccepted"] = "You have accepted the rematch.",
                ["RematchAcceptedAlready"] = "You have accepted the rematch already!",
                ["RematchTimedOut"] = "Your rematch timed out.",
                ["RematchFailed"] = "The rematch failed to start. Not all players were ready.",
                ["RematchFailed2"] = "The rematch failed to open. Not all players are available.",
                ["RematchAutoOn"] = "You will now automatically ready up for rematches.",
                ["RematchAutoOff"] = "You will no longer automatically ready up for rematches.",
                ["UI_Respawn"] = "Respawn",
                ["UI_Requeue"] = "Requeue",
                ["BeginSpectating"] = "You are now spectating.",
                ["EndSpectating"] = "You are no longer a spectator.",
                ["UI_ReadyOn"] = "<color=red>Ready On</color>",
                ["UI_ReadyOff"] = "Ready Off",
                ["SuicideBlock"] = "<color=red>You have suicided or disconnected in a duel and must wait up to 60 seconds to duel again.</color>",
                ["ZoneRenamed"] = "Zone renamed to {0}",
                ["ZoneNames"] = "<color=#183a0e>Zone Names ({0}):</color> {1}",
                ["ZoneRename"] = "/{0} rename <name>",
                ["ZoneSet"] = "Zone set to: {0}",
                ["Prefix"] = "[ <color=#406B35>Duelist</color> ]: ",
            }, this);
        }

        public List<string> VerifiedKits
        {
            get
            {
                VerifyKits();

                var list = new List<string>();

                if (hpDuelingKits.Count > 0)
                    list.AddRange(hpDuelingKits);

                if (lpDuelingKits.Count > 0)
                    list.AddRange(lpDuelingKits);

                if (list.Count == 0 && customKits.Count > 0)
                {
                    list.AddRange(customKits.ToList().Select(kvp => kvp.Key));
                }

                list.Sort();
                return list;
            }
        }

        public string GetVerifiedKit(string kit)
        {
            string kits = string.Join(", ", VerifiedKits.ToArray());

            if (!string.IsNullOrEmpty(kits))
            {
                if (customKits.Exists(entry => entry.Key.Equals(kit, StringComparison.CurrentCultureIgnoreCase)))
                {
                    return customKits.FirstOrDefault(entry => entry.Key.Equals(kit, StringComparison.CurrentCultureIgnoreCase)).Key;
                }
                if (hpDuelingKits.Exists(entry => entry.Equals(kit, StringComparison.CurrentCultureIgnoreCase)))
                {
                    return hpDuelingKits.FirstOrDefault(entry => entry.Equals(kit, StringComparison.CurrentCultureIgnoreCase));
                }
                if (lpDuelingKits.Exists(entry => entry.Equals(kit, StringComparison.CurrentCultureIgnoreCase)))
                {
                    return lpDuelingKits.FirstOrDefault(entry => entry.Equals(kit, StringComparison.CurrentCultureIgnoreCase));
                }
            }

            return null;
        }

        private void LoadVariables()
        {
            LoadAnimalSettings();
            LoadNormalSettings();
            LoadDeviceSettings();
            LoadBetSettings();
            LoadZoneSettings();
            LoadDeployableSettings();
            LoadRankedSettings();
            LoadKitSettings();
            LoadRespawnSettings();
            LoadRewardSettings();
            LoadSpawnSettings();
            LoadDeathmatchSettings();
            LoadAdvancedSettings();
            LoadUserInterfaceSettings();
            LoadSpectatorSettings();
            RegisterCommands();
            EnsureLimits();

            canSaveConfig = true;

            if (Changed)
            {
                SaveConfig();
                Changed = false;
            }
        }

        private bool canSaveConfig;

        protected override void SaveConfig()
        {
            if (canSaveConfig)
            {
                base.SaveConfig();
            }
        }

        private void LoadAnimalSettings()
        {
            putToSleep = Convert.ToBoolean(GetConfig("Animals", "Put To Sleep", true));
            killNpc = Convert.ToBoolean(GetConfig("Animals", "Die Instantly", false));
        }

        private void LoadNormalSettings()
        {
            chatSteamID = Convert.ToUInt64(GetConfig("Settings", "Chat SteamID", 0uL));
            setPlayerTime = Convert.ToBoolean(GetConfig("Settings", "Set Preferred Environment Plugin Time", false));
            removePlayers = Convert.ToBoolean(GetConfig("Settings", "Prevent Players Not Dueling From Entering The Zone", false));
            announceTime = Convert.ToSingle(GetConfig("Settings", "Announce Duel Information Every X Seconds", 1800f));
            wipeDuelZones = Convert.ToBoolean(GetConfig("Settings", "Auto Wipe Dueling Zones On Map Wipe", true));
            autoSetup = Convert.ToBoolean(GetConfig("Settings", "Auto Create Dueling Zone If Zone Does Not Exist", false));
            immunityTime = Convert.ToInt32(GetConfig("Settings", "Immunity Time", 10));
            deathTime = Convert.ToInt32(GetConfig("Settings", "Time To Duel In Minutes Before Death", 10));
            szDuelChatCommand = Convert.ToString(GetConfig("Settings", "Duel Command Name", "duel"));
            szQueueChatCommand = Convert.ToString(GetConfig("Settings", "Queue Command Name", "queue"));
            useAnnouncement = Convert.ToBoolean(GetConfig("Settings", "Allow Announcement", true));
            broadcastDefeat = Convert.ToBoolean(GetConfig("Settings", "Broadcast Defeat To All Players", true));
            damageScaleAmount = Convert.ToSingle(GetConfig("Settings", "Scale Damage Percent", 1f));
            buildingBlockExtensionRadius = Convert.ToSingle(GetConfig("Settings", "Building Block Extension Radius", 30f));
            autoAllowAll = Convert.ToBoolean(GetConfig("Settings", "Disable Requirement To Allow Duels", false));
            useRandomSkins = Convert.ToBoolean(GetConfig("Settings", "Use Random Skins", true));
            playerHealth = Convert.ToSingle(GetConfig("Settings", "Player Health After Duel [0 = disabled]", 100f));
            autoEnable = Convert.ToBoolean(GetConfig("Settings", "Auto Enable Dueling If Zone(s) Exist", false));
            bypassNewmans = Convert.ToBoolean(GetConfig("Settings", "Bypass Naked Check And Strip Items Anyway", false));
            respawnDeadDisconnect = Convert.ToBoolean(GetConfig("Settings", "Respawn Dead Players On Disconnect", true));
            resetSeed = Convert.ToBoolean(GetConfig("Settings", "Reset Temporary Ladder Each Wipe", true));
            noStability = Convert.ToBoolean(GetConfig("Settings", "No Stability On Structures", true));
            noMovement = Convert.ToBoolean(GetConfig("Settings", "No Movement During Immunity", false));
            respawnWalls = Convert.ToBoolean(GetConfig("Settings", "Respawn Zone Walls On Death", false));
            sphereAmount = Convert.ToInt32(GetConfig("Settings", "Create Dome Around Event Using Spheres (0 = disabled, recommended = 5)", 0));
            blockSpawning = Convert.ToBoolean(GetConfig("Settings", "Prevent Players From Spawning In Zone", true));
            useBlacklistCommands = Convert.ToBoolean(GetConfig("Settings", "Blacklist Commands", false));
            blacklistCommands = (GetConfig("Settings", "Blacklisted Chat Commands", BlacklistedCommands) as List<object>).Where(o => o != null && o.ToString().Length > 0).ToList().Select(o => o.ToString().ToLower()).ToList();
            useWhitelistCommands = Convert.ToBoolean(GetConfig("Settings", "Whitelist Commands", false));
            whitelistCommands = (GetConfig("Settings", "Whitelisted Chat Commands", WhitelistedCommands) as List<object>).Where(o => o != null && o.ToString().Length > 0).ToList().Select(o => o.ToString().ToLower()).ToList();
        }

        private void LoadDeviceSettings()
        {
            autoOvens = Convert.ToBoolean(GetConfig("Automatically Power On Devices", "Ovens", false));
            autoFlames = Convert.ToBoolean(GetConfig("Automatically Power On Devices", "FlameTurrets", false));
            autoTurrets = Convert.ToBoolean(GetConfig("Automatically Power On Devices", "AutoTurrets", false));
        }

        private void LoadBetSettings()
        {
            allowBetForfeit = Convert.ToBoolean(GetConfig("Betting", "Allow Bets To Be Forfeit", true));
            allowBetRefund = Convert.ToBoolean(GetConfig("Betting", "Allow Bets To Be Refunded", false));
            allowBets = Convert.ToBoolean(GetConfig("Betting", "Enabled", false));

            SetupBets();
        }

        private void LoadZoneSettings()
        {
            zoneRadius = Convert.ToSingle(GetConfig("Zone", "Zone Radius (Min: 50, Max: 300)", 50f));
            zoneCounter = Convert.ToInt32(GetConfig("Zone", "Create New Zone Every X Duels [0 = disabled]", 10));
            maxIncline = Convert.ToSingle(GetConfig("Zone", "Maximum Incline On Hills", 40f));
            zoneAmount = Convert.ToInt32(GetConfig("Zone", "Max Zones [Min 1]", 1));
            playersPerZone = Convert.ToInt32(GetConfig("Zone", "Players Per Zone [Multiple Of 2]", 10));
            visibleToAdmins = Convert.ToBoolean(GetConfig("Zone", "Players Visible To Admins", true));
            avoidWaterSpawns = Convert.ToBoolean(GetConfig("Zone", "Avoid Creating Automatic Spawn Points In Water", true));
            extraWallStacks = Convert.ToInt32(GetConfig("Zone", "Extra High External Wall Stacks", 2));
            useZoneWalls = Convert.ToBoolean(GetConfig("Zone", "Use Arena Wall Generation", true));
            zoneUseWoodenWalls = Convert.ToBoolean(GetConfig("Zone", "Use Wooden Walls", false));
            useLeastAmount = Convert.ToBoolean(GetConfig("Zone", "Create Least Amount Of Walls", false));
        }

        private void LoadDeployableSettings()
        {
            morphBarricadesStoneWalls = Convert.ToBoolean(GetConfig("Deployables", "Morph Barricades Into High External Stone Walls", false));
            morphBarricadesWoodenWalls = Convert.ToBoolean(GetConfig("Deployables", "Morph Barricades Into High External Wooden Walls", false));
        }

        private void LoadRankedSettings()
        {
            recordStats = Convert.ToBoolean(GetConfig("Ranked Ladder", "Enabled", true));
            permsToGive = Convert.ToInt32(GetConfig("Ranked Ladder", "Award Top X Players On Wipe", 3));
        }

        private void LoadRespawnSettings()
        {
            autoKitName = Convert.ToString(GetConfig("Respawn", "Give Kit If Respawn Items Are Empty", "autokit"));
            var defaultRespawn = GetConfig("Respawn", "Items", RespawnLoot) as List<object>;

            SetupRespawnItems(defaultRespawn, ref respawnLoot);
        }

        private void LoadRewardSettings()
        {
            economicsMoney = Convert.ToDouble(GetConfig("Rewards", "Economics Money [0 = disabled]", 0.0));
            serverRewardsPoints = Convert.ToInt32(GetConfig("Rewards", "ServerRewards Points [0 = disabled]", 0));
            requiredDuelMoney = Convert.ToDouble(GetConfig("Rewards", "Required Money To Duel", 0.0));
        }

        private void LoadSpawnSettings()
        {
            spDrawTime = Convert.ToSingle(GetConfig("Spawns", "Draw Time", 30f));
            spRemoveOneMaxDistance = Convert.ToSingle(GetConfig("Spawns", "Remove Distance", 10f));
            spRemoveAllMaxDistance = Convert.ToSingle(GetConfig("Spawns", "Remove All Distance", zoneRadius));
            //spRemoveInRange = Convert.ToBoolean(GetConfig("Spawns", "Remove In Duel Zone Only", false));
            spAutoRemove = Convert.ToBoolean(GetConfig("Spawns", "Auto Remove On Zone Removal", false));
        }

        private void LoadDeathmatchSettings()
        {
            dmFF = Convert.ToBoolean(GetConfig("Deathmatch", "Friendly Fire", true));
            minDeathmatchSize = Convert.ToInt32(GetConfig("Deathmatch", "Min Team Size", 2));
            maxDeathmatchSize = Convert.ToInt32(GetConfig("Deathmatch", "Max Team Size", 5));
            teamEvilShirt = Convert.ToUInt64(GetConfig("Deathmatch", "Evil Shirt Skin", 14177));
            teamGoodShirt = Convert.ToUInt64(GetConfig("Deathmatch", "Good Shirt Skin", 101));
            teamShirt = Convert.ToString(GetConfig("Deathmatch", "Shirt Shortname", "tshirt"));
            teamEconomicsMoney = Convert.ToDouble(GetConfig("Deathmatch", "Economics Money [0 = disabled]", 0.0));
            teamServerRewardsPoints = Convert.ToInt32(GetConfig("Deathmatch", "ServerRewards Points [0 = disabled]", 0));
            tdmEnabled = Convert.ToBoolean(GetConfig("Deathmatch", "Enabled", true));
            szMatchChatCommand = Convert.ToString(GetConfig("Deathmatch", "Chat Command", "tdm"));
            tdmServerDeaths = Convert.ToBoolean(GetConfig("Deathmatch", "Announce Deaths To Server", false));
            tdmMatchDeaths = Convert.ToBoolean(GetConfig("Deathmatch", "Announce Deaths To Match", true));
        }

        private void LoadAdvancedSettings()
        {
            requireTeamSize = Convert.ToBoolean(GetConfig("Advanced Options", "Require TDM Minimum Spawn Points To Be Equal Or Greater To The Number Of Players Joining", false));
            requiredMinSpawns = Convert.ToInt32(GetConfig("Advanced Options", "Require 1v1 Minimum Spawn Points To Be Equal Or Greater Than X", 2));
            requiredMaxSpawns = Convert.ToInt32(GetConfig("Advanced Options", "Require 1v1 Maximum Spawn Points To Be Less Than Or Equal To X", 200));
            allowPlayerDeaths = Convert.ToBoolean(GetConfig("Advanced Options", "Let Players Die Normally", false));
            sendDeadHome = Convert.ToBoolean(GetConfig("Advanced Options", "Send Dead Players Back Home", true));
            sendDefeatedHome = Convert.ToBoolean(GetConfig("Advanced Options", "Send Defeated Players Back Home", false));
        }

        private void LoadUserInterfaceSettings()
        {
            guiAutoEnable = Convert.ToBoolean(GetConfig("User Interface", "Auto Enable GUI For Players", false));
            szUIChatCommand = Convert.ToString(GetConfig("User Interface", "Chat Command", "dui"));
            guiUseCursor = Convert.ToBoolean(GetConfig("User Interface", "Use Cursor", false));
            guiUseCloseButton = Convert.ToBoolean(GetConfig("User Interface", "Show Close Button (X)", true));
            guiAnnounceUITime = Convert.ToSingle(GetConfig("User Interface", "Show Defeat Message UI For X Seconds", 7.5f));
            sendHomeRequeue = Convert.ToBoolean(GetConfig("User Interface", "Send Spectators Home FirstOrDefault When Clicking Requeue", false));
        }

        private void LoadSpectatorSettings()
        {
            sendHomeSpectatorWhenRematchTimesOut = Convert.ToBoolean(GetConfig("Spectators", "Send Home If Rematch Times Out", false));
        }

        private void RegisterCommands()
        {
            if (!permission.PermissionExists(duelistPerm)) // prevent warning
                permission.RegisterPermission(duelistPerm, this);

            permission.CreateGroup(duelistGroup, duelistGroup, 0);
            permission.GrantGroupPermission(duelistGroup, duelistPerm, this);

            AddCovalenceCommand("duelist", nameof(CommandDuelist));

            if (!string.IsNullOrEmpty(szDuelChatCommand))
            {
                cmd.AddChatCommand(szDuelChatCommand, this, cmdDuel);
                cmd.AddConsoleCommand(szDuelChatCommand, this, nameof(ccmdDuel));
                whitelistCommands.Add(szDuelChatCommand.ToLower());
            }

            if (!string.IsNullOrEmpty(szQueueChatCommand))
                cmd.AddChatCommand(szQueueChatCommand, this, cmdQueue);

            if (tdmEnabled && !string.IsNullOrEmpty(szMatchChatCommand))
            {
                cmd.AddChatCommand(szMatchChatCommand, this, cmdTDM);
                whitelistCommands.Add(szMatchChatCommand.ToLower());
            }

            if (!string.IsNullOrEmpty(szUIChatCommand))
            {
                cmd.AddChatCommand(szUIChatCommand, this, cmdDUI);
                cmd.AddConsoleCommand(szUIChatCommand, this, nameof(ccmdDUI));
            }
        }

        private void SetupBets()
        {
            var bets = GetConfig("Betting", "Bets", DefaultBets) as List<object>;

            foreach (var bet in bets)
            {
                duelingBets.Add(JsonConvert.DeserializeObject<BetInfo>(JsonConvert.SerializeObject(bet)));
            }
        }

        private void LoadKitSettings()
        {
            var kits = GetConfig("Settings", "Kits", DefaultKits) as List<object>;

            if (kits != null && kits.Count > 0)
            {
                foreach (object obj in kits)
                {
                    var kit = Convert.ToString(obj);

                    if (!string.IsNullOrEmpty(kit) && !hpDuelingKits.Contains(kit))
                    {
                        hpDuelingKits.Add(kit); // 0.1.14 fix
                        _hpDuelingKits.Add(kit); // 0.1.17 clone for Least Used Chance compatibility
                    }
                }
            }

            lesserKitChance = Convert.ToSingle(GetConfig("Settings", "Kits Least Used Chance", 0.25f));

            var lesserKits = GetConfig("Settings", "Kits Least Used", DefaultLesserKits) as List<object>;

            foreach (object obj in lesserKits)
            {
                var kit = Convert.ToString(obj);

                if (!string.IsNullOrEmpty(kit) && !lpDuelingKits.Contains(kit))
                {
                    lpDuelingKits.Add(kit); // 0.1.16
                    _lpDuelingKits.Add(kit); // 0.1.17 clone for Least Used Chance compatibility
                }
            }

            useWorkshopSkins = Convert.ToBoolean(GetConfig("Custom Kits", "Use Workshop Skins", true));

            var defaultKits = GetConfig("Custom Kits", "Kits", DefaultCustomKits) as Dictionary<string, object>;

            SetupCustomKits(defaultKits, ref customKits);
        }

        private void EnsureLimits()
        {
            if (buildingBlockExtensionRadius < 20f)
                buildingBlockExtensionRadius = 20f;

            if (zoneAmount < 1)
                zoneAmount = 1;

            if (playersPerZone < 2)
                playersPerZone = 2;
            else if (playersPerZone % 2 != 0)
                playersPerZone++;

            if (immunityTime < 0)
                immunityTime = 0;

            if (zoneRadius < 50f)
                zoneRadius = 50f;
            else if (zoneRadius > 300f)
                zoneRadius = 300f;

            if (requiredMinSpawns < 2)
                requiredMinSpawns = 2;

            if (requiredMaxSpawns < 2)
                requiredMaxSpawns = 2;

            if (guiAnnounceUITime < 1f)
                guiAnnounceUITime = 1f;
        }

        private void SetupDefinitions()
        {
            foreach (var itemDef in ItemManager.GetItemDefinitions())
            {
                var mod = itemDef.GetComponent<ItemModDeployable>();

                if (mod == null)
                {
                    continue;
                }

                bool externalWall = mod.entityPrefab.resourcePath.Contains("external") && mod.entityPrefab.resourcePath.Contains("wall");
                bool barricade = mod.entityPrefab.resourcePath.Contains("barricade");
                bool isLadder = mod.entityPrefab.resourcePath.Contains("ladder.wooden.wall");

                if (externalWall || barricade || isLadder)
                {
                    bool value = Convert.ToBoolean(GetConfig("Deployables", string.Format("Allow {0}", itemDef.displayName.translated), false));

                    if (!value)
                        continue;

                    deployables[itemDef.displayName.translated] = value;
                    prefabs[mod.entityPrefab.resourcePath] = itemDef.displayName.translated;
                }
            }
            if (Changed)
            {
                SaveConfig();
            }
        }

        private void SetupRespawnItems(List<object> list, ref List<DuelKitItem> source)
        {
            foreach (var entry in list)
            {
                source.Add(JsonConvert.DeserializeObject<DuelKitItem>(JsonConvert.SerializeObject(entry)));
            }
        }

        private void SetupCustomKits(Dictionary<string, object> dict, ref Dictionary<string, List<DuelKitItem>> source)
        {
            foreach (var entry in dict)
            {
                source[entry.Key] = JsonConvert.DeserializeObject<List<DuelKitItem>>(JsonConvert.SerializeObject(entry.Value));
            }
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            Config.Clear();
            LoadVariables();
        }

        private object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                Changed = true;
            }
            object value;
            if (!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                Changed = true;
            }
            return value;
        }

        private Dictionary<string, string> hexColors = new Dictionary<string, string>
        {
            ["<color=blue>"] = "<color=#0000FF>",
            ["<color=red>"] = "<color=#FF0000>",
            ["<color=yellow>"] = "<color=#FFFF00>",
            ["<color=lightblue>"] = "<color=#ADD8E6>",
            ["<color=orange>"] = "<color=#FFA500>",
            ["<color=silver>"] = "<color=#C0C0C0>",
            ["<color=magenta>"] = "<color=#FF00FF>",
            ["<color=green>"] = "<color=#008000>",
            ["<color=lime>"] = "<color=#00FF00>",
        };

        private string msg(string key, string id = null, params object[] args)
        {
            var sb = new System.Text.StringBuilder(id == null ? RemoveFormatting(lang.GetMessage(key, this, id)) : lang.GetMessage(key, this, id));
            var str = sb.ToString();

            foreach (var entry in hexColors)
            {
                if (str.Contains(entry.Key))
                {
                    sb.Replace(entry.Key, entry.Value);
                }
            }

            return args.Length > 0 ? string.Format(sb.ToString(), args) : sb.ToString();
        }

        public string RemoveFormatting(string source)
        {
            return source.Contains(">") ? Regex.Replace(source, "<.*?>", string.Empty) : source;
        }

        public static void Message(BasePlayer player, string message)
        {
            Instance.Player.Message(player, message, Instance.chatSteamID);
        }

        public void Message(HashSet<BasePlayer> players, string key, params object[] args)
        {
            foreach (var player in players)
            {
                if (IsNotConnected(player)) continue;
                Message(player, msg(key, player.UserIDString, args ?? new string[0]));
            }
        }

        #endregion
    }
}

namespace Oxide.Plugins.DuelistExtensionMethods
{
    public static class ExtensionMethods
    {
        public static bool All<T>(this IEnumerable<T> a, Func<T, bool> b) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (!b(c.Current)) { return false; } } } return true; }
        public static T ElementAt<T>(this IEnumerable<T> a, int b) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (b == 0) { return c.Current; } b--; } } return default(T); }
        public static bool Exists<T>(this IEnumerable<T> a, Func<T, bool> b = null) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (b == null || b(c.Current)) { return true; } } } return false; }
        public static T FirstOrDefault<T>(this IEnumerable<T> a, Func<T, bool> b = null) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (b == null || b(c.Current)) { return c.Current; } } } return default(T); }
        public static IEnumerable<T> Select<Y, T>(this IList<Y> a, Func<Y, T> b) { var c = new List<T>(); for (int i = 0; i < a.Count; i++) { c.Add(b(a[i])); } return c; }
        public static string[] Skip(this string[] a, int b) { if (a.Length == 0) { return Array.Empty<string>(); } string[] c = new string[a.Length - b]; int n = 0; for (int i = 0; i < a.Length; i++) { if (i < b) continue; c[n] = a[i]; n++; } return c; }
        public static List<T> Take<T>(this IList<T> a, int b) { var c = new List<T>(); for (int i = 0; i < a.Count; i++) { if (c.Count == b) { break; } c.Add(a[i]); } return c; }
        public static List<T> ToList<T>(this IEnumerable<T> a) { var b = new List<T>(); using (var c = a.GetEnumerator()) { while (c.MoveNext()) { b.Add(c.Current); } } return b; }
        public static IEnumerable<T> Where<T>(this IEnumerable<T> a, Func<T, bool> b) { var c = new List<T>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { if (b(d.Current)) { c.Add(d.Current); } } } return c; }
        public static bool IsHuman(this BasePlayer a) => a != null && a.userID.IsSteamId();
        public static List<T> OfType<T>(this IEnumerable<BaseNetworkable> a) where T : BaseNetworkable { var b = new List<T>(); using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (c.Current is T) { b.Add(c.Current as T); } } } return b; }
        public static int Sum<T>(this IEnumerable<T> a, Func<T, int> b) { int c = 0; foreach (T d in a) { c += b(d); } return c; }
        public static bool IsKilled(this BaseNetworkable a) => a == null || a.IsDestroyed || !a.IsFullySpawned();
        public static void SafelyKill(this BaseNetworkable a) { if (a == null || a.IsDestroyed) { return; } a.Kill(BaseNetworkable.DestroyMode.None); }
        public static bool CanCall(this Plugin a) { return a != null && a.IsLoaded; }
    }
}