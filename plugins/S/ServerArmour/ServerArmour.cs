using ConVar;
using Facepunch;
using Facepunch.Extend;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Libraries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using WebSocketSharp;
using Application = UnityEngine.Application;
using Time = Oxide.Core.Libraries.Time;

#pragma warning disable 8600
#pragma warning disable 8601

namespace Oxide.Plugins
{
    [Info("Server Armour", "Pho3niX90", "2.83.7")]
    [Description("Protect your server! Auto ban known hackers, scripters and griefer accounts, and notify server owners of threats.")]
    class ServerArmour : CovalencePlugin
    {
#if CARBON
        bool isCarbon = true;
#else
        bool isCarbon = false;
#endif
        #region Variables
        string api_hostname = "https://serverarmour.com"; // 
        Dictionary<string, ISAPlayer> _playerData = new Dictionary<string, ISAPlayer>();
        private Time _time = GetLibrary<Time>();
        private double cacheLifetime = 1; // minutes
        private SAConfig config;
        string specifier = "G";
        CultureInfo culture = CultureInfo.CreateSpecificCulture("en-US");
        //StringComparison defaultCompare = StringComparison.InvariantCultureIgnoreCase;
        const string DATE_FORMAT = "yyyy/MM/dd HH:mm";
        const string DATE_FORMAT2 = "yyyy-MM-dd HH:mm:ss";
        const string DATE_FORMAT_BAN = "yyyy-MM-ddTHH:mm:ss.fffZ";
        Regex logRegex = new Regex(@"(^assets.*prefab).*?position (.*) on");
        Regex logRegexNull = new Regex(@"(.*) changed its network group to null");

        bool debug = false;
        bool apiConnected = false;
        bool serverStarted = false;
        int serverId;

        private Dictionary<string, string> headers;
        string adminIds = "";
        Timer updateTimer;

        // related to auto updating
        Dictionary<string, byte[]> fileBackups = new Dictionary<string, byte[]>();
        List<string> ignoredPlugins = new List<string>();

        #endregion

        #region Libraries
        private readonly Game.Rust.Libraries.Player Player = Interface.Oxide.GetLibrary<Game.Rust.Libraries.Player>();
        #endregion

        #region Permissions
        const string PermissionToBan = "serverarmour.ban";
        const string PermissionToUnBan = "serverarmour.unban";

        const string PermissionAdminWebsite = "serverarmour.website.admin";

        const string PermissionWhitelistRecentVacKick = "serverarmour.whitelist.recentvac";
        const string PermissionWhitelistBadIPKick = "serverarmour.whitelist.badip";
        const string PermissionWhitelistVacCeilingKick = "serverarmour.whitelist.vacceiling";
        const string PermissionWhitelistServerCeilingKick = "serverarmour.whitelist.banceiling";
        const string PermissionWhitelistGameBanCeilingKick = "serverarmour.whitelist.gamebanceiling";
        const string PermissionWhitelistTotalBanCeiling = "serverarmour.whitelist.totalbanceiling";
        const string PermissionWhitelistSteamProfile = "serverarmour.whitelist.steamprofile";
        const string PermissionWhitelistFamilyShare = "serverarmour.whitelist.familyshare";
        const string PermissionWhitelistTwitterBan = "serverarmour.whitelist.twitterban";
        const string PermissionWhitelistAllowCountry = "serverarmour.whitelist.allowcountry";
        const string PermissionWhitelistAllowHighPing = "serverarmour.whitelist.allowhighping";

        const string DISCORD_INTRO_URL = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";
        /**
         * Plugin Name, Download Url
         */
        Dictionary<string, string> requiredPlugins = new Dictionary<string, string>
        {
            {"DiscordApi","https://serverarmour.com/api/v1/shop/product/379a27d9-d245-43c9-ad8f-332efa8a25e6/download" },
            {"CombatLogInfo","https://serverarmour.com/api/v1/shop/product/4195c37c-8eb5-47f6-8908-45aaa06df59c/download" }
        };

        // int serverId = 0;
        #endregion

        #region Plugins

#pragma warning disable 0649
        [PluginReference] Plugin DiscordApi, DiscordMessages, BetterChat, Ember, Clans, AdminToggle, CombatLogInfo, ServerArmourUpdater;
#pragma warning restore 0649
        void DiscordSend(string steamId, string name, EmbedFieldList report, int color = 39423, bool isBan = false)
        {
            string webHook;
            if (isBan)
            {
                if (config.DiscordBanWebhookURL.IsNullOrEmpty() || config.DiscordBanWebhookURL.Equals(DISCORD_INTRO_URL)) { Puts("Discord webhook not setup."); return; }
                webHook = config.DiscordBanWebhookURL;
            }
            else
            {
                if (config.DiscordWebhookURL.IsNullOrEmpty() || config.DiscordWebhookURL.Equals(DISCORD_INTRO_URL)) { Puts("Discord webhook not setup."); return; }
                webHook = config.DiscordWebhookURL;
            }

            List<EmbedFieldList> fields = new List<EmbedFieldList>();
            if (config.DiscordQuickConnect)
            {
                fields.Add(new EmbedFieldList()
                {
                    name = server.Name,
                    value = $"[steam://connect/{config.ServerIp}:{server.Port}](steam://connect/{config.ServerIp}:{server.Port})",
                    inline = true
                });
            }

            fields.Add(new EmbedFieldList()
            {
                name = "Steam Profile",
                value = $"[{name}\n{steamId}](https://steamcommunity.com/profiles/{steamId})",
                inline = !config.DiscordQuickConnect
            });

            fields.Add(new EmbedFieldList()
            {
                name = "Server Armour Profile ",
                value = $"[{name}\n{steamId}](https://serverarmour.com/profile/{steamId})",
                inline = !config.DiscordQuickConnect
            });

            fields.Add(report);
            var fieldsObject = fields.Cast<object>().ToArray();
            string json = JsonConvert.SerializeObject(fieldsObject);

            if (DiscordApi != null && DiscordApi.IsLoaded)
            {
                DiscordApi?.Call("API_SendEmbeddedMessage", webHook, "Server Armour Report: ", color, json);
            }
            else if (DiscordMessages != null && DiscordMessages.IsLoaded)
            {
                DiscordMessages?.Call("API_SendFancyMessage", webHook, "Server Armour Report: ", color, json);
            }
            else
            {
                LogWarning("No discord API plugin loaded, will not publish to hook!");
            }
        }

        void CheckPing(IPlayer player)
        {
            // fix: https://discord.com/channels/751155344532570223/751155561776808158/1160293816104914974
            if (player == null || !player.IsConnected)
                return;

            try
            {
                if (!HasPerm(player.Id, PermissionWhitelistAllowHighPing) && (config.AutoKickMaxPing > 0 && config.AutoKickMaxPing < player.Ping))
                {
                    KickPlayer(player.Id, GetMsg("Your Ping is too High", new Dictionary<string, string> { ["ping"] = player.Ping.ToString(), ["maxPing"] = config.AutoKickMaxPing.ToString() }), "C");
                }
            }
            catch (Exception ex) { }
        }

        #endregion

        #region Hooks
        void OnServerSave()
        {
            if (config.AutoKickMaxPing > 0)
                foreach (var player in players.Connected)
                    timer.Once(5f, () => CheckPing(player));
        }

        void OnServerInitialized(bool first)
        {
            LoadData();

            if (first)
            {
                //quick fix to ignore bans on server restart. (rust rebanning.)
                Unsubscribe(nameof(OnUserBanned));
                timer.Once(60, () =>
                {
                    Subscribe(nameof(OnUserBanned));
                    serverStarted = true;
                });
            }
            else
            {
                serverStarted = true;
            }

            // CheckOnlineUsers();
            // CheckLocalBans();

            string ServerGPort = ConVar.Server.port.ToString();
            string ServerQPort = ConVar.Server.queryport > 0 ? ConVar.Server.queryport.ToString() : ServerGPort;
            string ServerRPort = RCon.Port.ToString();

            Puts($"Server Ports are, Game Port: {ServerGPort} | Query Port:{ServerQPort} | RCON Port: {ServerRPort}");

            RegPerm(PermissionToBan);
            RegPerm(PermissionToUnBan);

            RegPerm(PermissionAdminWebsite);

            RegPerm(PermissionWhitelistBadIPKick);
            RegPerm(PermissionWhitelistRecentVacKick);
            RegPerm(PermissionWhitelistServerCeilingKick);
            RegPerm(PermissionWhitelistVacCeilingKick);
            RegPerm(PermissionWhitelistGameBanCeilingKick);
            RegPerm(PermissionWhitelistTotalBanCeiling);
            RegPerm(PermissionWhitelistSteamProfile);
            RegPerm(PermissionWhitelistFamilyShare);
            RegPerm(PermissionWhitelistTwitterBan);
            RegPerm(PermissionWhitelistAllowCountry);
            RegPerm(PermissionWhitelistAllowHighPing);
            RegisterTag();

            string framework = this.isCarbon ? "Carbon" : "Oxide";
            headers = new Dictionary<string, string> {
                { "server-key", config.ServerApiKey },
                { "Accept", "application/json" },
                { "Content-Type", "application/x-www-form-urlencoded" },
                { "User-Agent", $"Server Armour/{this.Version} <{framework}>"}
            };

            string[] _admins = permission.GetUsersInGroup("admin");
            if (config.OwnerSteamId != null && config.OwnerSteamId.Length > 0 && !_admins.Contains(config.OwnerSteamId))
            {
                var adminsList = new List<string>();
                adminsList.AddRange(_admins);
                adminsList.AddRange(config.OwnerSteamId.Split(','));

                var extraAdmins = permission.GetPermissionUsers(PermissionAdminWebsite);
                if (extraAdmins != null && extraAdmins.Length > 0)
                {
                    adminsList.AddRange(extraAdmins);
                }

                int e = 0;
                foreach (string extraAdminsGroups in permission.GetPermissionGroups(PermissionAdminWebsite))
                {
                    foreach (string extraAdmin in permission.GetUsersInGroup(extraAdminsGroups))
                    {
                        if (!adminsList.Contains(extraAdmin))
                        {
                            adminsList.Add(extraAdmin);
                            e++;
                        }
                    }
                }

                _admins = adminsList.Distinct().ToArray();
            }

            for (int i = 0, n = _admins.Length; i < n; i++)
            {
                adminIds += $"{_admins[i].Substring(0, 17)}" + (i < _admins.Length - 1 ? "," : string.Empty);
            }

            CheckServerConnection();
            Application.logMessageReceived += HandleLog;

            if (ServerArmourUpdater != null && ServerArmourUpdater.IsLoaded)
            {
                foreach (var item in plugins.GetAll())
                {
                    if (item.Filename == null || item.Filename.Length == 0)
                        continue;
                    if (requiredPlugins.ContainsKey(item.Name))
                        requiredPlugins.Remove(item.Name);
                }

                Puts($"Needed plugins = {requiredPlugins.Count}");
                foreach (var plugin in requiredPlugins)
                {
                    ServerArmourUpdater?.Call("QueueDownload", plugin.Key, plugin.Value, "Server Armour");
                }
            }
        }

        void CheckServerConnection()
        {
            string body = ServerGetString();
            DoRequest("check_server", body, (code, response) =>
            {
                JObject obj = null;

                try
                {
                    obj = JObject.Parse(response);
                }
                catch (Exception)
                {
                    LogWarning($"Response issue: ({code}) {response}");
                    timer.Once(15, CheckServerConnection);
                    return;
                }

                if (obj != null)
                {
                    var msg = obj["message"].ToString();
                    try
                    {
                        this.serverId = int.Parse(obj["serverId"].ToString());
                    }
                    catch (Exception) { }
                    Puts(msg);
                    if (msg.Equals("connected"))
                    {
                        apiConnected = true;
                        Puts($"Connected to SA API | Server ID = {this.serverId}");
                        ServerStatusUpdate();
                        updateTimer = timer.Every(60, ServerStatusUpdate);
                    }
                    else
                    {
                        Puts($"ServerApiKey = {config.ServerApiKey}");
                        var errMsg = "Server Armour has not initialized. Is your apikey correct? Get it from https://serverarmour.com/my-servers or join discord for support https://discord.gg/jxvRaPR";
                        LogError(errMsg);
                        timer.Once(500, () => LogError(errMsg));
                        return;
                    }
                    Puts("Server Armour has initialized.");
                }
                else
                {
                    timer.Once(5, CheckServerConnection);
                    return;
                }
            });
        }

        void Unload()
        {
            _playerData?.Clear();
            _playerData = null;
            Application.logMessageReceived -= HandleLog;
            if (updateTimer != null && !updateTimer.Destroyed)
                updateTimer.Destroy();
        }

        void OnUserConnected(IPlayer iPlayer)
        {
            this.DoConnectionChecks(iPlayer);
        }

        void DoConnectionChecks(IPlayer player, string type = "C")
        {
            if (!apiConnected)
            {
                LogError("User not checked. Server armour is not loaded.");
            }
            else
            {
                //lets check the userid first.
                if (config.AutoKick_KickWeirdSteam64 && !player.Id.IsSteamId())
                {
                    KickPlayer(player.Id, GetMsg("Strange Steam64ID"), type);
                    return;
                }

                GetPlayerBans(player);

                try
                {
                    var connectedSeconds = GetConnectedSeconds(player);
                    if (connectedSeconds < 600)
                        timer.Once(120, () =>
                        {
                            DoConnectionChecks(player);
                        });

                    if (config.ShowProtectedMsg && connectedSeconds < 60) SendReplyWithIcon(player, GetMsg("Protected MSG"));
                }
                catch (Exception) { }
            }
        }

        int GetConnectedSeconds(IPlayer player)
        {
            return BasePlayer.FindByID(ulong.Parse(player.Id))?.secondsConnected ?? int.MaxValue;
        }

        int GetConnectedSeconds(ISAPlayer player)
        {
            return BasePlayer.FindByID(ulong.Parse(player.steamid))?.secondsConnected ?? int.MaxValue;
        }

        void OnUserDisconnected(IPlayer player)
        {
            if (apiConnected)
            {
                try
                {
                    _playerData = _playerData?.Where(pair => MinutesAgo((uint)pair.Value.cacheTimestamp) < cacheLifetime)
                                     ?.ToDictionary(pair => pair.Key, pair => pair.Value);
                }
                catch (Exception) { }
            }
        }

        void OnPluginLoaded(Plugin plugin)
        {
            if (plugin.Title == "BetterChat") RegisterTag();
        }
        /**
         * To detect publisher bans
         */
        void OnPlayerKicked(BasePlayer player, string reason)
        {
            if (reason.Contains("PublisherBanned"))
            {
                DoRequest($"player/eacban", $"reason={reason}&steamId={player.UserIDString}&username={player.displayName}&serverId={this.serverId}",
                    (code, response) => { }, RequestMethod.POST);
            }
        }


        void OnUserUnbanned(string name, string id, string ipAddress)
        {
            DoPardon(id);
        }

        void OnUserBanned(string name, string id, string ipAddress, string reason)
        {
            //this is to make sure that if an app like battlemetrics for example, bans a player, we catch it.
            if (config.IgnoreCheatDetected && reason.StartsWith("Cheat Detected"))
            {
                return;
            }
            try
            {
                if (AdminToggle != null && AdminToggle.Call<bool>("IsAdmin", id))
                {
                    return;
                }
            }
            catch (Exception) { }
            timer.Once(10f, () =>
            {
                //lets make sure first it wasn't us. 
                if (!IsPlayerCached(id) && (IsPlayerCached(id) && !ContainsMyBan(id)))
                {
                    // LogDebug($"Player wasn't banned via Server Armour, now adding to DB with a default lengh ban of 100yrs {name} ({id}) at {ipAddress} was banned: {reason}");
                    IPlayer bPlayer = players.FindPlayerById(id);
                    if (bPlayer != null)
                    {
                        if ((bPlayer.IsAdmin && config.IgnoreAdmins)) return;
                        // LogDebug("Before AddBan");
                        AddBan(new ISABan
                        {
                            steamid = ulong.Parse(bPlayer.Id),
                            serverName = server.Name,
                            serverIp = config.ServerIp,
                            reason = reason,
                            created = DateTime.Now.ToString(DATE_FORMAT),
                            banUntil = DateTime.Now.AddYears(100).ToString(DATE_FORMAT)
                        }, false);
                    }
                }
            });
        }

        void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string subject, string message, string type)
        {
            if (!serverStarted)
                return;
            string messageClean = Uri.EscapeDataString(message);
            string subjectClean = Uri.EscapeDataString(subject);
            DoRequest(
                $"player/{reporter.UserIDString}/addf7",
                $"target={targetId}&subject={subjectClean}&message={messageClean}", (c, s) => { });
        }
        #endregion


        #region Checks
        void GetPlayerRiskScore(ulong steamId)
        {
            GetJson($"player/{steamId}/risk", (c, s) =>
            {
                if (c < 300)
                {
                    double riskscore = 0;
                    double.TryParse(s.GetValue("score").ToString(), out riskscore);
                    // TODO check the risk
                }
            });
        }
        #endregion

        #region API_Hooks

        #endregion

        #region WebRequests

        void GetPlayerBans(IPlayer player)
        {
            if (player == null || player.Id == null) return;
            KickIfBanned(GetPlayerCache(player?.Id));
            WebCheckPlayer(player.Id, player.Address, player.IsConnected);
            timer.Once(10f, () => CheckPing(player));
        }

        void GetPlayerBans(string playerId, string playerName)
        {
            KickIfBanned(GetPlayerCache(playerId));
            WebCheckPlayer(playerId, "0.0.0.0", true);
        }

        void WebCheckPlayer(string id, string address, bool connected)
        {
            if (!serverStarted)
                return;

            DoRequest($"player/{id}?bans=true&linked={config.AutoKick_ActiveBans.ToString().ToLowerInvariant()}", $"ipAddress={address}",
                (code, response) =>
                {
                    // LogDebug("Getting player from API");
                    ISAPlayer isaPlayer = null;

                    try
                    {
                        isaPlayer = JsonConvert.DeserializeObject<ISAPlayer>(response);
                    }
                    catch (Exception)
                    {
                        timer.Once(30, () => WebCheckPlayer(id, address, connected));
                        return;
                    }

                    if (isaPlayer == null)
                    {
                        return;
                    }

                    isaPlayer.cacheTimestamp = _time.GetUnixTimestamp();
                    isaPlayer.lastConnected = _time.GetUnixTimestamp();


                    // add cache for player
                    // LogDebug("Checking cache");
                    if (!IsPlayerCached(isaPlayer.steamid))
                    {
                        AddPlayerCached(isaPlayer);
                    }
                    else
                    {
                        UpdatePlayerData(isaPlayer);
                    }

                    // lets check bans first
                    try
                    {
                        KickIfBanned(isaPlayer);
                    }
                    catch (Exception ane)
                    {
                        Puts("An ArgumentNullException occured. Please notify the developer along with the below information: ");
                        Puts(response);
                        Puts(ane.StackTrace);
                    }

                    //script vars
                    string pSteamId = isaPlayer.steamid ?? id;
                    //string lSteamId = GetFamilyShare(isaPlayer.steamid);
                    //

                    LogDebug("Check for a twitter/eac game ban");
                    if (config.AutoKick_KickTwitterGameBanned && !HasPerm(pSteamId, PermissionWhitelistTwitterBan) && isaPlayer.eacBans.Count > 0)
                    {
                        KickPlayer(isaPlayer?.steamid, $"https://twitter.com/rusthackreport/status/{isaPlayer.eacBans[isaPlayer.eacBans.Count - 1].id}", "C");
                    }

                    LogDebug($"Check for a recent vac: DissallowVacBanDays = {config?.DissallowVacBanDays}, steamDaysSinceLastBan = {isaPlayer?.steamDaysSinceLastBan}, steamNumberOfVACBans = {isaPlayer?.steamNumberOfVACBans}, steamDaysSinceLastBan = {isaPlayer?.steamDaysSinceLastBan}, whitelisted = {HasPerm(pSteamId, PermissionWhitelistRecentVacKick)}");
                    bool pRecentVac = (isaPlayer.steamNumberOfVACBans > 0 || isaPlayer.steamDaysSinceLastBan > 0)
                    && isaPlayer.steamDaysSinceLastBan < config.DissallowVacBanDays; //check main player

                    //bool lRecentVac = (isaPlayer.lender?.steamNumberOfVACBans > 0 || isaPlayer.lender?.steamDaysSinceLastBan > 0)
                    //&& isaPlayer.lender?.steamDaysSinceLastBan < config.DissallowVacBanDays; //check the lender player

                    if (config.AutoKickOn && !HasPerm(pSteamId, PermissionWhitelistRecentVacKick) && pRecentVac)
                    {
                        int vacLast = isaPlayer.steamDaysSinceLastBan;
                        int until = config.DissallowVacBanDays - vacLast;

                        Interface.CallHook("OnSARecentVacKick", vacLast, until);

                        string msg = GetMsg(pRecentVac ? "Reason: VAC Ban Too Fresh" : "Reason: VAC Ban Too Fresh - Lender", new Dictionary<string, string> { ["daysago"] = vacLast.ToString(), ["daysto"] = until.ToString() });

                        KickPlayer(isaPlayer?.steamid, msg, "C");
                    }

                    LogDebug("Check for too many vac bans");
                    if (!HasPerm(pSteamId, PermissionWhitelistVacCeilingKick) && HasReachedVacCeiling(isaPlayer))
                    {
                        Interface.CallHook("OnSATooManyVacKick", pSteamId, isaPlayer?.steamNumberOfVACBans);
                        KickPlayer(isaPlayer?.steamid, GetMsg("VAC Ceiling Kick"), "C");
                    }

                    LogDebug("Check for too many game bans");
                    if (!HasPerm(pSteamId, PermissionWhitelistGameBanCeilingKick) && HasReachedGameBanCeiling(isaPlayer))
                    {
                        Interface.CallHook("OnSATooManyGameBansKick", pSteamId, isaPlayer.steamNumberOfGameBans);
                        KickPlayer(isaPlayer?.steamid, GetMsg("Too Many Previous Game Bans"), "C");
                    }

                    // LogDebug("Check for players with too many server bans");
                    //if (!HasPerm(pSteamId, PermissionWhitelistServerCeilingKick) && HasReachedServerCeiling(isaPlayer))
                    //{
                    //    Interface.CallHook("OnSATooManyServerBans", pSteamId, config.AutoKickCeiling, ServerBanCount(isaPlayer) + ServerBanCount(isaPlayer.lender));
                    //    KickPlayer(isaPlayer?.steamid, GetMsg("Too Many Previous Bans"), "C");
                    //}

                    //if (!HasPerm(PermissionWhitelistTotalBanCeiling, pSteamId) && HasReachedTotalBanCeiling(isaPlayer))
                    //{
                    //    Interface.CallHook("OnSATooManyBans", pSteamId, config.AutoKickCeiling, TotalBans(isaPlayer.lender));
                    //    KickPlayer(isaPlayer?.steamid, GetMsg("Too Many Previous Bans"), "C");
                    //}

                    LogDebug("Check for players with a private profile");
                    if (!HasPerm(pSteamId, PermissionWhitelistSteamProfile) && config.AutoKick_KickPrivateProfile && isaPlayer.communityvisibilitystate != 3)
                    {
                        Interface.CallHook("OnSAProfilePrivate", pSteamId, isaPlayer.communityvisibilitystate);
                        KickPlayer(isaPlayer?.steamid, GetMsg("Profile Private"), "C");
                    }

                    LogDebug("Check for a hidden steam level");
                    if (!HasPerm(pSteamId, PermissionWhitelistSteamProfile) && isaPlayer.steamlevel == -1 && config.AutoKick_KickHiddenLevel)
                    {
                        Interface.CallHook("OnSASteamLevelHidden", pSteamId);
                        KickPlayer(isaPlayer?.steamid, GetMsg("Steam Level Hidden"), "C");
                    }

                    LogDebug($"Player {isaPlayer.steamid} is at steam level {isaPlayer.steamlevel}");
                    if (!HasPerm(pSteamId, PermissionWhitelistSteamProfile) && isaPlayer.steamlevel < config.AutoKick_MinSteamProfileLevel && isaPlayer.steamlevel >= 0)
                    {
                        Interface.CallHook("OnSAProfileLevelLow", pSteamId, config.AutoKick_MinSteamProfileLevel, isaPlayer.steamlevel);
                        KickPlayer(isaPlayer?.steamid, GetMsg("Profile Low Level", new Dictionary<string, string> { ["level"] = config.AutoKick_MinSteamProfileLevel.ToString() }), "C");
                    }

                    LogDebug($"IP/CACHE| ID:{id} ADD:{address} COUNTRY: {isaPlayer.ipInfo.isocode} RATING:{isaPlayer?.ipInfo?.rating} AGE: {isaPlayer.ipInfo?.lastcheck}");
                    if (config.AutoKickOn && config.AutoKick_BadIp && !HasPerm(id, PermissionWhitelistBadIPKick) && config.AutoKick_BadIp)
                    {
                        if (IsBadIp(isaPlayer))
                        {
                            if (isaPlayer.ipInfo?.proxy == "yes")
                                KickPlayer(id, GetMsg("Reason: Proxy IP"), "C");
                            else
                                KickPlayer(id, GetMsg("Reason: Bad IP"), "C");

                            Interface.CallHook("OnSAVPNKick", id, isaPlayer?.ipInfo?.rating);
                        }
                    }

                    if (!HasPerm(id, PermissionWhitelistAllowCountry) && !config.AutoKickLimitCountry.IsNullOrEmpty())
                    {
                        var playerIso = isaPlayer.ipInfo?.isocode?.Trim();
                        var countriesAllowed = config.AutoKickLimitCountry.Split(',');
                        if (playerIso != null && playerIso != "" && !countriesAllowed.Contains(playerIso))
                            KickPlayer(id, GetMsg("Country Not Allowed", new Dictionary<string, string> { ["country"] = playerIso, ["country2"] = config.AutoKickLimitCountry.ToString() }), "C");
                    }

                    GetPlayerReport(isaPlayer, connected);

                }, RequestMethod.POST, 5);
        }

        void KickIfBanned(ISAPlayer isaPlayer)
        {
            if (isaPlayer == null) return;
            IPlayer iPlayer = covalence.Players.FindPlayer(isaPlayer.steamid);
            if (iPlayer != null && iPlayer.IsAdmin && config.IgnoreAdmins) return;
            try
            {
                if (AdminToggle != null && AdminToggle.Call<bool>("IsAdmin", iPlayer.Id) && config.IgnoreAdmins) return;
            }
            catch (Exception) { }

            ISABan ban = IsBanned(isaPlayer?.steamid);
            if (ban != null)
            {
                if (!ban.steamid.ToString().Equals(isaPlayer.steamid))
                {
                    KickPlayer(isaPlayer?.steamid, $"Linked: {ban.steamid}, {ban.reason}", "U");
                }
                else
                {
                    KickPlayer(isaPlayer?.steamid, ban.reason, "U");
                }
            }
        }

        void AddBan(ISABan thisBan, bool doNative = true)
        {
            if (!serverStarted)
                return;
            if (thisBan == null)
            {
                // LogDebug($"This ban is null");
                return;
            }

            string reason = Uri.EscapeDataString(thisBan.reason);

            try
            {
                DoRequest($"player/{thisBan.steamid}/addban", $"reason={reason}&dateTime={thisBan.created}&banUntil={thisBan.banUntil}&createdBySteamId={thisBan.bannedBy}", (code, response) =>
                {
                    // ISABan thisBan = new ISABan { serverName = server.Name, date = dateTime, reason = banreason, serverIp = thisServerIp, banUntil = dateBanUntil };
                    // LogDebug("Check cache");
                    if (IsPlayerCached(thisBan.steamid.ToString()))
                    {
                        // LogDebug($"{thisBan.steamid} has ban cached, now updating.");
                        AddPlayerData(thisBan.steamid.ToString(), thisBan);
                    }
                    else
                    {
                        // LogDebug($"{thisBan.steamid} had no ban data cached, now creating.");
                        ISAPlayer newPlayer = new ISAPlayer((ulong)thisBan.steamid);
                        // LogDebug("Player cache object created");
                        newPlayer.bans.Add(thisBan);
                        // LogDebug("After adding");
                        AddPlayerCached(newPlayer);
                    }
                }, RequestMethod.POST, 60);
            }
            catch (Exception)
            {
                return;
            }
        }

        #endregion

        #region Commands
        [Command("sa.apply_key")]
        void ApplyKey(IPlayer player, string command, string[] args)
        {
            var key = args[0].ToString();

            if (!player.IsServer)
            {
                return;
            }

            this.config.ServerApiKey = key;
            SaveConfig();
        }

        [Command("sa.clb", "getreport")]
        void SCmdCheckLocalBans(IPlayer player, string command, string[] args)
        {
            CheckLocalBans();
        }

        [Command("unban", "playerunban", "sa.unban"), Permission(PermissionToUnBan)]
        void SCmdUnban(IPlayer player, string command, string[] args)
        {
            if (args == null || (args.Length > 2 || args.Length < 1))
            {
                SendReplyWithIcon(player, GetMsg("UnBan Syntax"));
                return;
            }

            var reason = args.Length > 1 ? args[1] : "";
            SaUnban(args[0], player, reason);
        }

        void SilentBan(ulong steamId, TimeSpan timeSpan, string reason, IPlayer enforcer = null)
        {
            Unsubscribe(nameof(OnUserBanned));
            var username = covalence.Players.FindPlayerById(steamId.ToString())?.Name ?? "";
            NativeBan(steamId, reason, Convert.ToInt64(timeSpan.TotalSeconds), username, enforcer);
            Subscribe(nameof(OnUserBanned));
        }
        void SilentBan(ulong steamId, int timeSpan, string reason, IPlayer enforcer = null)
        {
            Unsubscribe(nameof(OnUserBanned));
            var username = covalence.Players.FindPlayerById(steamId.ToString())?.Name ?? "";
            NativeBan(steamId, reason, -1, username, enforcer);
            Subscribe(nameof(OnUserBanned));
        }

        void SilentUnban(string playerId, IPlayer admin)
        {
            Unsubscribe(nameof(OnUserUnbanned));
            NativeUnban(playerId, admin);
            Subscribe(nameof(OnUserUnbanned));
        }

        void NativeBan(ulong playerId, string reason, long duration = -1, string playerUsername = "unnamed", IPlayer player = null)
        {

            if (!playerId.IsSteamId())
            {
                if (serverStarted)
                    player?.Reply(string.Concat("This doesn't appear to be a 64bit steamid: ", playerId));
                return;
            }

            ServerUsers.User user = ServerUsers.Get(playerId);

            if (user != null && user.@group == ServerUsers.UserGroup.Banned)
            {
                if (serverStarted)
                    player?.Reply(string.Concat("User ", playerId, " is already banned"));
                return;
            }

            string str3 = "";
            BasePlayer basePlayer = BasePlayer.FindByID(playerId);
            string durationSuffix = (duration > (long)0 ? string.Concat(" for ", duration.FormatSecondsLong()) : "");
            if (basePlayer != null && basePlayer.IsConnected)
            {
                playerUsername = basePlayer.displayName;
                if (basePlayer.IsConnected && basePlayer.net.connection.ownerid != 0 && basePlayer.net.connection.ownerid != basePlayer.net.connection.userid)
                {
                    str3 = string.Concat(str3, string.Format(" and also banned ownerid {0}", basePlayer.net.connection.ownerid));
                    ServerUsers.Set(basePlayer.net.connection.ownerid, ServerUsers.UserGroup.Banned, basePlayer.displayName, reason, duration);
                }
                Chat.Broadcast(string.Concat(new string[] { "Kickbanning ", basePlayer.displayName, durationSuffix, " (", reason, ")" }), "SERVER", "#eee", (ulong)0);
                //Net.sv.Kick(basePlayer.net.connection, string.Concat("Banned", str, ": ", str2), false);
            }

            ServerUsers.Set(playerId, ServerUsers.UserGroup.Banned, playerUsername, reason, duration == -1 ? -1 : (long)(DateTime.Now.AddSeconds(duration) - Epoch).TotalSeconds);
            if (serverStarted)
                player?.Reply(string.Format("Banned User{0}: {1} - \"{2}\" for \"{3}\"{4}", new object[] { durationSuffix, playerId, playerUsername, reason, str3 }));
        }

        bool NativeUnban(string playerId, IPlayer admin = null)
        {
            ulong playerIdLong = 0;

            if (!ulong.TryParse(playerId, out playerIdLong))
            {
                var msg = string.Format("This doesn't appear to be a 64bit steamid: {0}", playerId);

                if (admin == null)
                {
                    Puts(msg);
                }
                else
                {
                    admin.Reply(msg);
                }
                return false;
            }

            ServerUsers.User user = ServerUsers.Get(playerIdLong);
            if (user == null || user.@group != ServerUsers.UserGroup.Banned)
            {
                admin?.Reply(string.Format("User {0} isn't banned", playerId));
                return false;
            }
            ServerUsers.Remove(playerIdLong);
            admin?.Reply($"Unbanned User: {playerId}");
            return true;
        }

        void SaUnban(string playerId, IPlayer admin = null, string reason = "")
        {
            IPlayer iPlayer = players.FindPlayer(playerId);
            SilentUnban(playerId, admin);

            ulong playerIdLong = 0;
            ulong.TryParse(playerId, out playerIdLong);

            if (playerIdLong == 0 && iPlayer == null)
            {
                GetMsg("Player Not Found", new Dictionary<string, string> { ["player"] = playerId }); return;
            }

            Puts($"Player {iPlayer?.Name} ({playerId}) was unbanned by {admin?.Name} ({admin?.Id})");

            // Add ember support.
            if (Ember != null)
            {
                Ember?.Call("Unban", playerId, Player.FindById(admin?.Id));
            }
            //

            if (serverStarted)
            {
                DoPardon(playerId, iPlayer, admin);
            }

            string msgClean;
            if (reason.IsNullOrEmpty())
            {
                msgClean = GetMsg("Player Now Unbanned Clean - NoReason", new Dictionary<string, string> { ["player"] = iPlayer?.Name ?? playerId });
            }
            else
            {
                msgClean = GetMsg("Player Now Unbanned Clean - Reason", new Dictionary<string, string> { ["player"] = iPlayer?.Name ?? playerId, ["reason"] = reason });
            }

            if (config.DiscordBanReport)
            {
                DiscordSend(playerId, iPlayer?.Name ?? playerId, new EmbedFieldList()
                {
                    name = "Player Unbanned",
                    value = msgClean,
                    inline = true
                }, 3066993, true);
            }
        }

        void DoPardon(string playerId, IPlayer iPlayer = null, IPlayer admin = null)
        {
            RemoveBans(playerId);
            DoRequest($"player/ban/pardon/{playerId}", $"pardonedBy={admin?.Id ?? admin.Name}", (code, response) =>
            {
                if (config.RconBroadcast)
                    RCon.Broadcast(RCon.LogType.Chat, new Chat.ChatEntry
                    {
                        Message = $"Player was unbanned by {admin.Name} ({admin?.Id})",
                        UserId = playerId,
                        Username = iPlayer?.Name ?? "",
                        Time = Facepunch.Math.Epoch.Current
                    });


                if (admin != null && config.RconBroadcast)
                {
                    RCon.Broadcast(RCon.LogType.Chat, new Chat.ChatEntry
                    {
                        Message = $"Player {iPlayer?.Name} ({playerId}) was unbanned",
                        UserId = admin?.Id,
                        Username = admin?.Name ?? "",
                        Time = Facepunch.Math.Epoch.Current
                    });
                }
                if (admin != null && !admin.IsServer)
                {
                    SendReplyWithIcon(admin, $"Player {iPlayer?.Name} ({playerId}) was unbanned");
                }
            }, RequestMethod.POST, 30, 5);
        }

        //banid 76561198007433923 "Pho3niX90" "Some reason" 1953394942
        [Command("banid"), Permission(PermissionToBan)]
        void SCmdBanId(IPlayer player, string command, string[] args)
        {
            var argString = string.Join(" ", args);
            var matches = new Regex(@"([7]\d{16}).*?(\d{9,12}|-1)$").Match(argString);
            var mG = matches.Groups;

            // [0] steam64id [1] username [2] reason [3] length
            if (args.Length > 0)
            {
                var playerId = args[0];
                var reason = "No reason provided.";
                var lengthInt = 0;
                var length = "-1";
                if (mG.Count == 3)
                {
                    if (int.TryParse(mG[2].Value, out lengthInt))
                        length = lengthInt == -1 || lengthInt > 1848030000 ? length : lengthInt.ToString();
                }

                if (args.Length >= 3)
                {
                    reason = args[2].ToString() == length ? args[1] : args[2];
                }


                API_BanPlayer(player, playerId, reason, length, true);
            }
        }


        [Command("ban", "playerban", "sa.ban"), Permission(PermissionToBan)]
        void SCmdBan(IPlayer player, string command, string[] args)
        {

            int argsLength = args == null ? 0 : args.Length;
            if (argsLength >= 0 && argsLength < 2)
            {
                SendReplyWithIcon(player, GetMsg("Ban Syntax"));
                return;
            }
            var playerId = args[0]?.ToString()?.Trim();
            var fPlayer = players.FindPlayer(playerId);
            if (playerId.Length != 17 && !playerId.StartsWith("7656"))
            {
                // LogDebug("Length = " + playerId.Length + ", StartsWith = " + playerId.StartsWith("7656"));
                if (fPlayer == null)
                {
                    //Puts("Player not found, or not in servers cache.");
                    SendReplyWithIcon(player, "Player not found, or not in servers cache.");
                    return;
                }
                playerId = fPlayer.Id;
            }

            /***
             * Length 2: player, reason
             * Length 3: player, reason, time
             * Length 4: playerSteamId, reason, time, ignoreSearch
             ***/

            if (args == null || (argsLength > 4))
            {
                SendReplyWithIcon(player, GetMsg("Ban Syntax"));
                return;
            }

            var reason = argsLength < 2 ? "No reason provided." : args[1];
            var length = args.Length > 2 ? args[2].ToUpper() : "100Y";
            var ignoreSearch = false;

            if (args.Length > 3)
                bool.TryParse(args[3], out ignoreSearch);

            try
            {
                API_BanPlayer(player, playerId, reason, length, ignoreSearch);
            }
            catch (Exception e)
            {
                Puts(e.Message);
                SendReplyWithIcon(player, GetMsg("Ban Syntax"));
            }
        }

        [Command("clanban"), Permission(PermissionToBan)]
        void SCmdClanBan(IPlayer player, string command, string[] args)
        {
            int argsLength = args == null ? 0 : args.Length;

            /***
             * Length 2: player, reason
             * Length 3: player, reason, time
             * Length 4: playerSteamId, reason, time, ignoreSearch
             ***/
            if (args == null || (argsLength < 2 || argsLength > 4))
            {
                SendReplyWithIcon(player, GetMsg("Ban Syntax"));
                return;
            }

            var playerId = args[0];
            var reason = args[1];
            var length = args.Length > 2 ? args[2] : "100y";
            var ignoreSearch = false;


            var errMsg = "";
            IPlayer iPlayer = null;
            IEnumerable<IPlayer> playersFound = players.FindPlayers(playerId);
            int playersFoundCount = playersFound.Count();
            switch (playersFoundCount)
            {
                case 0:
                    errMsg = GetMsg("Player Not Found", new Dictionary<string, string> { ["player"] = playerId });
                    break;
                case 1:
                    iPlayer = players.FindPlayer(playerId);
                    break;
                default:
                    List<string> playersFoundNames = new List<string>();
                    for (int i = 0; i < playersFoundCount; i++) playersFoundNames.Add(playersFound.ElementAt(i).Name);
                    string playersFoundNamesString = String.Join(", ", playersFoundNames.ToArray());
                    errMsg = GetMsg("Multiple Players Found", new Dictionary<string, string> { ["players"] = playersFoundNamesString });
                    break;
            }
            if ((!ignoreSearch && iPlayer == null) || !errMsg.Equals("")) { SendReplyWithIcon(player, errMsg); return; }

            ulong playerIdU = ulong.Parse(iPlayer.Id);

            List<ulong> teamMembers = new List<ulong>();

            if (config.ClanBanTeams)
            {
                teamMembers = GetTeamMembers(playerIdU);
            }

            if (Clans != null && Clans.IsLoaded)
            {
                var clanMembers = GetClan(playerId);
                if (clanMembers != null && clanMembers.Count() > 0)
                {
                    teamMembers.AddRange(clanMembers);
                }
            }

            teamMembers = teamMembers.Distinct().ToList();
            teamMembers?.Remove(ulong.Parse(playerId));

            bool.TryParse(args[3], out ignoreSearch);

            API_BanPlayer(player, playerId, reason, length, ignoreSearch);

            if (teamMembers.Count() > 0)
            {
                var clanBanReason = config.ClanBanPrefix.Replace("{reason}", reason).Replace("{playerId}", playerId);

                foreach (var member in teamMembers)
                {
                    API_BanPlayer(player, member.ToString(), clanBanReason, length, ignoreSearch);
                }
            }
        }

        [Command("sa.cp")]
        void SCmdCheckPlayer(IPlayer player, string command, string[] args)
        {
            string playerArg = (args.Length == 0) ? player.Id : args[0];

            IPlayer playerToCheck = players.FindPlayer(playerArg.Trim());
            if (playerToCheck == null)
            {
                SendReplyWithIcon(player, GetMsg("Player Not Found", new Dictionary<string, string> { ["player"] = playerArg }));
                return;
            }

            GetPlayerReport(playerToCheck, player);
        }
        #endregion

        #region VPN/Proxy
        #endregion

        #region Ban System

        string BanMinutes(DateTime ban)
        {
            return ((int)Math.Round((ban - DateTime.UtcNow).TotalMinutes)).ToString();
        }
        DateTime BanUntil(string banLength)
        {
            int digit = 10;
            string del = "y";

            if (!banLength.ToLower().Equals("permanent"))
            {
                int.TryParse(new string(banLength.Where(char.IsDigit).ToArray()), out digit);
                del = new string(banLength.Where(char.IsLetter).ToArray());
            }

            if (digit <= 0)
            {
                digit = 100;
            }

            DateTime now = DateTime.UtcNow;

            /*
             * Fix for bans.cfg going blank.
             * Issue: When server restarts, the bans.cfg is empty. The reason is that facepunch stores the bans in this file, verbatim, and rebans everyone when the server is restart (bans are kept in memory). Once the server restarted, SA couldn't convert the unix timestamp to a length, and errored out, causing the file to go blank on next save (since there are no bans in memory).
             */
            DateTime dateBanUntil;
            if (banLength.Length >= 10)
            {
                //already epoch?
                long lBanLength;
                if (long.TryParse(banLength, out lBanLength))
                {
                    return ConvertUnixToDateTime(lBanLength);
                }
            }

            switch (del.ToUpper())
            {
                case "S":
                    dateBanUntil = now.AddSeconds(digit);
                    break;
                case "MI":
                    dateBanUntil = now.AddMinutes(digit);
                    break;
                case "H":
                    dateBanUntil = now.AddHours(digit);
                    break;
                case "D":
                    dateBanUntil = now.AddDays(digit);
                    break;
                case "W":
                    dateBanUntil = now.AddDays(digit * 7);
                    break;
                case "M":
                    dateBanUntil = now.AddMonths(digit);
                    break;
                case "Y":
                    dateBanUntil = now.AddYears(Math.Min(2030 - DateTime.Now.Year, digit));
                    break;
                default:
                    dateBanUntil = now.AddDays(digit);
                    break;
            }
            return dateBanUntil;
        }

        string BanFor(string banLength)
        {
            int digit = int.Parse(new string(banLength.Where(char.IsDigit).ToArray()));
            string del = new string(banLength.Where(char.IsLetter).ToArray());


            // DateTime now = DateTime.Now;
            //string dateTime = now.ToString(DATE_FORMAT);
            string dateBanUntil;

            switch (del.ToUpper())
            {
                case "MI":
                    dateBanUntil = digit + " Minutes";
                    break;
                case "H":
                    dateBanUntil = digit + " Hours";
                    break;
                case "D":
                    dateBanUntil = digit + " Days";
                    break;
                case "M":
                    dateBanUntil = digit + " Months";
                    break;
                case "Y":
                    dateBanUntil = digit + " Years";
                    break;
                default:
                    dateBanUntil = digit + " Days";
                    break;
            }
            return dateBanUntil;
        }

        void RemoveBans(string id)
        {
            if (_playerData.ContainsKey(id) && ServerBanCount(_playerData[id]) > 0)
            {
                _playerData[id].bans.RemoveAll(x => x.serverIp == config.ServerIp || x.adminSteamId.Equals(config.OwnerSteamId));
            }
        }

        bool BanPlayer(ISABan ban)
        {
            AddBan(ban);
            KickPlayer(ban.steamid.ToString(), ban.reason, "U");
            return true;
        }
        #endregion

        #region IEnumerators

        void CheckOnlineUsers()
        {
            // todo, improve by changing to single call.
            IEnumerable<IPlayer> allPlayers = players.Connected;

            int allPlayersCount = allPlayers.Count();

            List<string> playerCalls = new List<string>();

            int page = 0;
            for (int i = 0; i < allPlayersCount; i++)
            {
                if (i % 50 == 0)
                {
                    page++;
                }
                playerCalls[page] += allPlayers.ElementAt(i);
            }

            int allPlayersCounter = 0;
            float waitTime = 5f;
            if (allPlayersCount > 0)
            {
                // LogDebug("Will now inspect all online users, time etimation: " + (allPlayersCount * waitTime) + " seconds");
                timer.Repeat(waitTime, allPlayersCount, () =>
                {
                    // LogDebug($"Inpecting online user {allPlayersCounter + 1} of {allPlayersCount} for infractions");
                    try
                    {
                        IPlayer player = allPlayers.ElementAt(allPlayersCounter);
                        if (player != null) GetPlayerBans(player);
                        if (allPlayersCounter >= allPlayersCount) LogDebug("Inspection completed.");
                        allPlayersCounter++;
                    }
                    catch (Exception)
                    {
                        allPlayersCounter++;
                    }
                });
            }
        }

        void CheckLocalBans()
        {
            IEnumerable<ServerUsers.User> bannedUsers = ServerUsers.GetAll(ServerUsers.UserGroup.Banned);
            int BannedUsersCount = bannedUsers.Count();
            int BannedUsersCounter = 0;
            float waitTime = 1f;

            if (BannedUsersCount > 0)
                timer.Repeat(waitTime, BannedUsersCount, () =>
                {
                    ServerUsers.User usr = bannedUsers.ElementAt(BannedUsersCounter);
                    LogDebug($"Checking local user ban {BannedUsersCounter + 1} of {BannedUsersCount}");
                    if (IsBanned(usr.steamid.ToString(specifier, culture)) == null && !usr.IsExpired)
                    {
                        try
                        {
                            IPlayer player = covalence.Players.FindPlayer(usr.steamid.ToString(specifier, culture));
                            DateTime expireDate = ConvertUnixToDateTime(usr.expiry);
                            if (expireDate.Year <= 1970)
                            {
                                expireDate = expireDate.AddYears(100);
                            }
                            // LogDebug($"Adding ban for {((player == null) ? usr.steamid.ToString() : player.Name)} with reason `{usr.notes}`, and expiry {expireDate.ToString(DATE_FORMAT)} to server armour.");

                            AddBan(new ISABan
                            {
                                steamid = (ulong)usr.steamid,
                                serverName = server.Name,
                                serverIp = config.ServerIp,
                                reason = usr.notes,
                                created = DateTime.Now.ToString(DATE_FORMAT),
                                banUntil = expireDate.ToString(DATE_FORMAT)
                            });
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            BannedUsersCounter++;
                        }
                    }

                    BannedUsersCounter++;
                });

        }
        #endregion

        #region Data Handling
        bool IsPlayerDirty(string steamid)
        {
            try
            {
                if (steamid.IsNullOrEmpty()) return false;
                ISAPlayer isaPlayer = GetPlayerCache(steamid);
                return isaPlayer != null && IsPlayerCached(steamid) && (ServerBanCount(isaPlayer) > 0 || isaPlayer?.steamCommunityBanned > 0 || isaPlayer?.steamNumberOfGameBans > 0 || isaPlayer?.steamVACBanned > 0);
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        bool IsPlayerCached(string steamid) { return _playerData != null && _playerData.Count() > 0 && _playerData.ContainsKey(steamid); }
        void AddPlayerCached(ISAPlayer isaplayer) => _playerData.Add(isaplayer.steamid, isaplayer);
        ISAPlayer GetPlayerCache(string steamid)
        {
            return !steamid.IsNullOrEmpty() && IsPlayerCached(steamid) ? _playerData[steamid] : null;
        }

        int GetPlayerBanDataCount(string steamid) => ServerBanCount(_playerData[steamid]);
        void UpdatePlayerData(ISAPlayer isaplayer) => _playerData[isaplayer.steamid] = isaplayer;
        void AddPlayerData(string id, ISABan isaban) => _playerData[id].bans.Add(isaban);

        static bool DateIsPast(DateTime to)
        {
            return DateTime.UtcNow > to && to.Year > 2010;
        }

        double MinutesAgo(uint to)
        {
            return Math.Round((_time.GetUnixTimestamp() - to) / 60.0);
        }

        void GetPlayerReport(IPlayer player, IPlayer cmdplayer)
        {
            ISAPlayer isaPlayer = GetPlayerCache(player.Id);
            if (isaPlayer != null) GetPlayerReport(isaPlayer, player.IsConnected, true, cmdplayer);
        }

        void GetPlayerReport(ISAPlayer isaPlayer, bool isConnected = true, bool isCommand = false, IPlayer cmdPlayer = null)
        {
            try
            {
                if (isaPlayer == null || isaPlayer.steamid == null) return;
                Dictionary<string, string> data =
                           new Dictionary<string, string>
                           {
                               ["status"] = IsPlayerDirty(isaPlayer.steamid) ? "dirty" : "clean",
                               ["steamid"] = isaPlayer.steamid,
                               ["username"] = isaPlayer.personaname ?? "N/A",
                               ["serverBanCount"] = ServerBanCount(isaPlayer).ToString(),
                               ["NumberOfGameBans"] = isaPlayer.steamNumberOfGameBans.ToString(),
                               ["NumberOfVACBans"] = isaPlayer.steamNumberOfVACBans.ToString() + (isaPlayer.steamNumberOfVACBans > 0 ? $" Last({isaPlayer.steamDaysSinceLastBan}) days ago" : ""),
                               ["EconomyBan"] = (!isaPlayer.steamEconomyBan?.Equals("none")).ToString()
                           };

                LogDebug("Checking player status");
                if (IsPlayerDirty(isaPlayer.steamid) || isCommand)
                {
                    LogDebug("Getting report");
                    string report = GetMsg("User Dirty MSG", data);
                    LogDebug("Checking if should broadcast");
                    if (config.BroadcastPlayerBanReport && isConnected && isCommand && !(config.BroadcastPlayerBanReportVacDays > isaPlayer.steamDaysSinceLastBan))
                    {
                        LogDebug("Broadcasting");
                        BroadcastWithIcon(report.Replace(isaPlayer.steamid + ":", string.Empty).Replace(isaPlayer.steamid, string.Empty));
                    }
                    if (isCommand)
                    {
                        LogDebug("Replying to command");
                        SendReplyWithIcon(cmdPlayer, report.Replace(isaPlayer.steamid + ":", string.Empty).Replace(isaPlayer.steamid, string.Empty));
                    }
                }

                if (config.DiscordJoinReports && ((config.DiscordOnlySendDirtyReports && IsPlayerDirty(isaPlayer.steamid)) || !config.DiscordOnlySendDirtyReports))
                {
                    if (GetConnectedSeconds(isaPlayer) > 30)
                        return;

                    IPlayer iPlayer = players.FindPlayer(isaPlayer.steamid);

                    LogDebug($"Sending to discord.");
                    DiscordSend(iPlayer.Id, iPlayer.Name, new EmbedFieldList()
                    {
                        name = "Report",
                        value = GetMsg("User Dirty DISCORD MSG", data),
                        inline = true
                    });

                    LogDebug($"Broadcasting via RCON {config.RconBroadcast}");
                    if (config.RconBroadcast)
                        RCon.Broadcast(RCon.LogType.Chat, new Chat.ChatEntry
                        {
                            Message = GetMsg("User Dirty DISCORD MSG", data),
                            UserId = isaPlayer.steamid,
                            Username = isaPlayer.personaname,
                            Time = Facepunch.Math.Epoch.Current
                        });

                }
            }
            catch (Exception) { }

        }

        private void LogDebug(string txt)
        {
            if (config.Debug || debug) Puts($"DEBUG: {txt}");
        }

        void LoadData()
        {
            Dictionary<string, ISAPlayer> playerData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, ISAPlayer>>($"ServerArmour/playerData");
            if (playerData != null)
            {
                _playerData = playerData;
            }
        }

        /***
         * WARNING: Modifying any data below to falsify information (online players, fps) will get your push rights revoked permanently, without notice. And possibly blacklisted from the servers directory.  
         */
        string ServerGetString()
        {
            // Define an array of parameter names
            string[] parameterNames = {
                "sip", "an", "ae", "ownerid", "gport", "qport", "rport", "sname", "sipcov",
                "sk", "fps", "fpsa", "mp", "cp", "qp", "v"
            };

            // Define an array of parameter values
            string[] parameterValues = {
                !config.ServerIp.IsNullOrEmpty() && !config.ServerIp.Equals("0.0.0.0") ? config.ServerIp : covalence.Server.Address.ToString(),
                Uri.EscapeDataString(config.ServerAdminName),
                Uri.EscapeDataString(config.ServerAdminEmail),
                Uri.EscapeDataString(config.OwnerSteamId),
                ConVar.Server.port.ToString(),
                ConVar.Server.queryport.ToString(),
                RCon.Port.ToString(),
                Uri.EscapeDataString(server.Name),
                covalence.Server.Address.ToString(),
                Uri.EscapeDataString(config.SteamApiKey),
                Uri.EscapeDataString(Performance.report.frameRate.ToString()),
                Uri.EscapeDataString(Performance.report.frameRateAverage.ToString()),
                Uri.EscapeDataString(Admin.ServerInfo().MaxPlayers.ToString()),
                Uri.EscapeDataString(Admin.ServerInfo().Players.ToString()),
                Uri.EscapeDataString(Admin.ServerInfo().Queued.ToString()),
                Version.ToString(),
            };

            // Use StringBuilder to efficiently build the serverString
            StringBuilder serverString = new StringBuilder();

            // Loop through the parameters and add non-empty ones to the serverString
            for (int i = 0; i < parameterNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(parameterValues[i]))
                {
                    if (serverString.Length > 0)
                    {
                        serverString.Append("&");
                    }
                    serverString.Append($"{parameterNames[i]}={parameterValues[i]}");
                }
            }
            return serverString.ToString();
        }
        #endregion

        #region Server Directory Methods
        private void ServerStatusUpdate()
        {
            DoRequest("status", ServerGetString(), (c, r) => { });
        }
        #endregion

        #region Kicking 

        void KickPlayer(string steamid, string reason, string type)
        {
            if (steamid == null)
                return;
            IPlayer player = players.FindPlayerById(steamid);
            if (player == null || (player.IsAdmin && config.IgnoreAdmins)) return;

            if (player.IsConnected)
            {
                player?.Kick(reason);
                Puts($"Player {player?.Name} was kicked for `{reason}`");

                if (config.DiscordKickReport)
                {
                    DiscordSend(player.Id, player.Name, new EmbedFieldList()
                    {
                        name = "Player Kicked",
                        value = reason,
                        inline = true
                    }, 13459797);
                }

                if (type.Equals("C") && config.BroadcastKicks)
                {
                    BroadcastWithIcon(GetMsg("Player Kicked", new Dictionary<string, string> { ["player"] = player.Name, ["reason"] = reason }));
                }
            }
        }

        bool IsBadIp(ISAPlayer isaPlayer)
        {
            if (isaPlayer?.ipInfo == null) return false;
            try
            {
                return (config.AutoKick_BadIp
                    && isaPlayer.ipInfo.type?.ToLower() == "vpn" || isaPlayer.ipInfo.type?.ToLower() == "proxy" || isaPlayer.ipInfo?.proxy == "yes")
                    && !(config.AutoKick_IgnoreNvidia && isaPlayer.ipInfo.isCloudComputing);
            }
            catch (Exception)
            {
                Puts($"An error occured with the proxy check. Please report this to the developer. with the previous trailing logs", isaPlayer.ipInfo.ToString());
                return false;
            }
        }

        bool ContainsMyBan(string steamid)
        {
            return IsBanned(steamid) != null;
        }

        ISABan IsBanned(string steamid)
        {
            if (steamid == null || steamid.Equals("0")) return null;

            if (!IsPlayerCached(steamid)) return null;

            try
            {
                ISAPlayer isaPlayer = GetPlayerCache(steamid);

                if (isaPlayer?.bans?.Count() == 0)
                {
                    return null;
                }

                LogDebug("Check ban!");
                foreach (ISABan ban in isaPlayer?.bans)
                {
                    var isServerIP = (config.AutoKick_SameServerIp && ban.serverIp.Equals(config.ServerIp))
                        || (config.AutoKick_SameServerIp && ban.serverIp.Equals(covalence.Server.Address.ToString()));
                    var shouldNetworkCheck = (config.AutoKick_NetworkBan && !config.OwnerSteamId.IsNullOrEmpty() && ban.adminSteamId != null && ban.adminSteamId.Contains(config.OwnerSteamId));
                    var isServerId = this.serverId == ban.serverId;
                    if (isServerId || isServerIP || shouldNetworkCheck)
                    {
                        try
                        {
                            if (ban.IsBanned())
                                return ban;
                        }
                        catch (FormatException ex) { }
                    }
                }

                return null;
            }
            catch (InvalidOperationException ioe)
            {
                if (_playerData.ContainsKey(steamid))
                {
                    _playerData.Remove(steamid);
                }
                Puts(ioe.Message);
                return null;
            }
        }

        int ServerBanCount(ISAPlayer player)
        {
            try
            {
                if (player == null || player.bans == null) return 0;
                return player.bans.Count();
            }
            catch (NullReferenceException)
            {
                return 0;
            }
        }

        int TotalBans(ISAPlayer isaPlayer)
        {
            int banCounts = 0;
            banCounts += isaPlayer.steamNumberOfVACBans;
            banCounts += isaPlayer.steamNumberOfGameBans;
            banCounts += ServerBanCount(isaPlayer);
            return banCounts;
        }

        bool HasReachedVacCeiling(ISAPlayer isaPlayer)
        {
            if (isaPlayer == null) return false;
            return config.AutoKickOn && config.AutoVacBanCeiling >= 0 && config.AutoVacBanCeiling < (isaPlayer.steamNumberOfVACBans);
        }

        bool HasReachedGameBanCeiling(ISAPlayer isaPlayer)
        {
            if (isaPlayer == null) return false;
            return config.AutoKickOn && config.AutoGameBanCeiling >= 0 && config.AutoGameBanCeiling < isaPlayer.steamNumberOfGameBans;
        }

        bool HasReachedTotalBanCeiling(ISAPlayer isaPlayer)
        {
            if (isaPlayer == null) return false;
            int banCounts = TotalBans(isaPlayer);

            bool kick = config.AutoKickOn && config.EnableTotalBanKick && config.AutoTotalBanCeiling >= 0 && banCounts > config.AutoTotalBanCeiling;
            if (kick)
            {
                Puts($"Player ${isaPlayer.steamid} had {banCounts} total bans, it's more than the limit set of {config.AutoTotalBanCeiling}. Kicking player");
            }
            return kick;
        }

        bool HasReachedServerCeiling(ISAPlayer isaPlayer)
        {
            if (isaPlayer == null) return false;
            return config.AutoKickOn && config.AutoKickCeiling < (ServerBanCount(isaPlayer));
        }

        bool IsProfilePrivate(string steamid)
        {
            ISAPlayer player = GetPlayerCache(steamid);
            return player.communityvisibilitystate == 1;
        }

        int GetProfileLevel(string steamid)
        {
            ISAPlayer player = GetPlayerCache(steamid);
            return (int)player.steamlevel;
        }

        #endregion

        #region API Hooks
        private int API_GetServerBanCount(string steamid) => IsPlayerCached(steamid) ? GetPlayerBanDataCount(steamid) : 0;
        private bool API_GetIsVacBanned(string steamid) => IsPlayerCached(steamid) ? GetPlayerCache(steamid).steamVACBanned == 1 : false;
        private bool API_GetIsCommunityBanned(string steamid) => IsPlayerCached(steamid) ? GetPlayerCache(steamid).steamCommunityBanned == 1 : false;
        private int API_GetVacBanCount(string steamid) => IsPlayerCached(steamid) ? GetPlayerCache(steamid).steamNumberOfVACBans : 0;
        private int API_GetDaysSinceLastVacBan(string steamid) => IsPlayerCached(steamid) ? GetPlayerCache(steamid).steamDaysSinceLastBan : 0;
        private int API_GetGameBanCount(string steamid) => IsPlayerCached(steamid) ? GetPlayerCache(steamid).steamNumberOfGameBans : 0;
        private string API_GetEconomyBanStatus(string steamid) => IsPlayerCached(steamid) ? GetPlayerCache(steamid).steamEconomyBan : "none";
        private bool API_GetIsPlayerDirty(string steamid) => IsPlayerDirty(steamid);
        private bool API_GetIsProfilePrivate(string steamid) => IsProfilePrivate(steamid);
        private int API_GetProfileLevel(string steamid) => GetProfileLevel(steamid);

        private void API_BanPlayer(IPlayer player, string playerNameId, string reason, string length = "-1", bool ignoreSearch = false)
        {
            /***
             * Length 2: player, reason
             * Length 3: player, reason, time
             * Length 4: playerSteamId, reason, time, ignoreSearch
             ***/
            string banPlayer = playerNameId; //0
            string banReason = reason; // 1
            ulong banSteamId = 0;
            if (config.IgnoreCheatDetected && reason.StartsWith("Cheat Detected"))
            {
                return;
            }

            DateTime now = DateTime.Now;
            string dateTime = now.ToString(DATE_FORMAT);
            /***
             * If time not specified, default to 100 years
             ***/
            string lengthOfBan = !length.IsNullOrEmpty() && !length.Equals("-1") && !length.Equals("100Y") ? length : "-1";

            string dateBanUntil = lengthOfBan != "-1" ? BanUntil(lengthOfBan).ToString(DATE_FORMAT) : "-1";

            ulong.TryParse(banPlayer, out banSteamId);

            string errMsg = "";


            string playerName = banSteamId.ToString();

            IPlayer iPlayer = null;

            IEnumerable<IPlayer> playersFound = players.FindPlayers(banPlayer);
            int playersFoundCount = playersFound.Count();

            switch (playersFoundCount)
            {
                case 0:
                    if (banSteamId == 0)
                    {
                        errMsg = GetMsg("Player Not Found", new Dictionary<string, string> { ["player"] = banPlayer });
                    }
                    break;
                case 1:
                    iPlayer = players.FindPlayer(banPlayer);
                    break;
                default:
                    List<string> playersFoundNames = new List<string>();
                    for (int i = 0; i < playersFoundCount; i++) playersFoundNames.Add(playersFound.ElementAt(i).Name);
                    string playersFoundNamesString = String.Join(", ", playersFoundNames.ToArray());
                    errMsg = GetMsg("Multiple Players Found", new Dictionary<string, string> { ["players"] = playersFoundNamesString });
                    break;
            }

            if (iPlayer != null && iPlayer.IsAdmin)
            {
                Puts($"You cannot ban a admin! Issued by {player?.Id ?? player?.Name}");
                return;
            }
            try
            {
                if (AdminToggle != null && AdminToggle.Call<bool>("IsAdmin", iPlayer.Id))
                {
                    return;
                }
            }
            catch (Exception) { }

            playerName = iPlayer?.Name;

            if (!ignoreSearch && !errMsg.Equals("")) { SendReplyWithIcon(player, errMsg); return; }

            ISAPlayer isaPlayer;

            ulong adminId = 0;
            ulong.TryParse(player?.Id, out adminId);
            if (BanPlayer(new ISABan
            {
                steamid = banSteamId,
                bannedBy = adminId,
                serverName = server.Name,
                serverIp = config.ServerIp,
                reason = banReason,
                created = dateTime,
                banUntil = dateBanUntil
            }))
            {
                string msg;
                string banLengthText = lengthOfBan.Equals("-1") ? GetMsg("Permanent") : BanFor(lengthOfBan);
                // LogDebug($"banLengthText {banLengthText}");

                msg = GetMsg("Player Now Banned Perma", new Dictionary<string, string> { ["player"] = playerName, ["reason"] = reason, ["length"] = banLengthText });
                string msgClean = GetMsg("Player Now Banned Clean", new Dictionary<string, string> { ["player"] = playerName, ["reason"] = reason, ["length"] = banLengthText });


                // fix to ignore init rebans from rust
                if (serverStarted && apiConnected)
                {
                    // Add ember support.
                    if (Ember != null)
                        Ember?.Call("Ban", banSteamId, BanMinutes(BanUntil(lengthOfBan)), banReason, true, config.OwnerSteamId, Player.FindById(banSteamId));

                    if (config.RconBroadcast)
                        RCon.Broadcast(RCon.LogType.Chat, new Chat.ChatEntry
                        {
                            Message = msgClean,
                            UserId = banSteamId.ToString(),
                            Username = playerName,
                            Time = Facepunch.Math.Epoch.Current
                        });

                    if (config.BroadcastNewBans)
                    {
                        BroadcastWithIcon(msg);
                    }
                    else
                    {
                        SendReplyWithIcon(player, msg);
                    }

                    if (config.DiscordBanReport)
                    {
                        DiscordSend(banSteamId.ToString(), playerName, new EmbedFieldList()
                        {
                            name = "Player Banned",
                            value = msgClean,
                            inline = true
                        }, 13459797, true);
                    }
                }

                try
                {
                    if (lengthOfBan == "-1")
                        SilentBan(banSteamId, -1, banReason, player);
                    else
                        SilentBan(banSteamId, TimeSpan.FromMinutes((BanUntil(lengthOfBan) - DateTime.Now).TotalMinutes), banReason, player);
                }
                catch (Exception e)
                {
                    Puts("Silent Ban Failed.");
                    Puts(e.Message);
                }
                finally
                {
                    Subscribe(nameof(OnUserBanned));
                }
            }
        }
        #endregion

        #region Localization
        string GetMsg(string msg, Dictionary<string, string> rpls = null)
        {
            string message = lang.GetMessage(msg, this);
            if (rpls != null) foreach (var rpl in rpls) message = message.Replace($"{{{rpl.Key}}}", rpl.Value);
            return message;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Protected MSG"] = "Server protected by [#008080ff]ServerArmour[/#]",
                ["User Dirty MSG"] = "[#008080ff]Server Armour Report:\n {steamid}:{username}[/#] is {status}.\n [#ff0000ff]Server Bans:[/#] {serverBanCount}\n [#ff0000ff]Game Bans:[/#] {NumberOfGameBans}\n [#ff0000ff]Vac Bans:[/#] {NumberOfVACBans}\n [#ff0000ff]Economy Banned:[/#] {EconomyBan}\n [#ff0000ff]Family Share:[/#] {FamShare}",
                ["User Dirty DISCORD MSG"] = "**Server Bans:** {serverBanCount}\n **Game Bans:** {NumberOfGameBans}\n **Vac Bans:** {NumberOfVACBans}\n **Economy Banned:** {EconomyBan}\n **Family Share:** {FamShare}",
                ["Command sa.cp Error"] = "Wrong format, example: /sa.cp usernameORsteamid trueORfalse",
                ["Arkan No Recoil Violation"] = "[#ff0000]{player}[/#] received an Arkan no recoil violation.\n[#ff0000]Violation[/#] #{violationNr}, [#ff0000]Weapon:[/#] {weapon}, [#ff0000]Ammo:[/#] {ammo}, [#ff0000]Shots count:[/#] {shots}\n Admins will investigate ASAP, please have handcams ready.\n This might be a false-positive, but all violations need to be investigated.",
                ["Arkan Aimbot Violation"] = "[#ff0000]{player}[/#] received an Arkan aimbot violation.\n[#ff0000]Violation[/#]  #{violationNr}, [#ff0000]Weapon:[/#] {weapon}, [#ff0000]Ammo:[/#] {ammo}\n Admins will investigate ASAP, please have handcams ready.\n This might be a false-positive, but all violations need to be investigated.",
                ["Arkan In Rock Violation"] = "[#ff0000]{player}[/#] received an Arkan in rock violation.\n[#ff0000]Violation[/#]  #{violationNr}, [#ff0000]Weapon:[/#] {weapon}, [#ff0000]Ammo:[/#] {ammo}\n Admins will investigate ASAP, please have handcams ready.\n This might be a false-positive, but all violations need to be investigated.",
                ["Player Now Banned Perma"] = "[#ff0000]{player}[/#] has been banned\n[#ff0000]Reason:[/#] {reason}\n[#ff0000]Length:[/#] {length}",
                ["Player Now Banned Clean"] = "{player} has been banned\nReason: {reason}\nLength: {length}",
                ["Player Now Unbanned Clean - Reason"] = "{player} has been unbanned\nReason: {reason}",
                ["Player Now Unbanned Clean - NoReason"] = "{player} has been unbanned",
                ["Reason: Bad IP"] = "Bad IP Detected, either due to a VPN/Proxy",
                ["Reason: Proxy IP"] = "VPN & Proxy's not allowed.",
                ["Player Not Found"] = "Player wasn't found",
                ["Multiple Players Found"] = "Multiple players found with that name ({players}), please try something more unique like a steamid",
                ["Ban Syntax"] = "[#ff0000]ban <playerNameOrID> \"the reason\" <length>[/#]\nexample: ban \"some user\" \"cheating\" 1y\n length examples: 1h for 1 hour, 1m for 1 month etc",
                ["UnBan Syntax"] = "sa.unban <playerNameOrID> <reason>",
                ["No Response From API"] = "Couldn't get an answer from ServerArmour.com! Error: {code} {response}",
                ["Player Not Banned"] = "Player not banned",
                ["Broadcast Player Banned"] = "{tag} {username} wasn't allowed to connect\nReason: {reason}",
                ["Reason: VAC Ban Too Fresh"] = "VAC ban received {daysago} days ago, wait another {daysto} days",
                ["Reason: VAC Ban Too Fresh - Lender"] = "VAC ban received {daysago} days ago on lender account, wait another {daysto} days",
                ["Lender Banned"] = "The lender account contained a ban",
                ["Keyword Kick"] = "Due to your past behaviour on other servers, you aren't allowed in.",
                ["Family Share Kick"] = "Family share accounts are not allowed on this server.",
                ["Too Many Previous Bans"] = "You have too many previous bans (other servers included). Appeal in discord",
                ["Too Many Previous Game Bans"] = "You have too many previous game bans. Appeal in discord",
                ["VAC Ceiling Kick"] = "You have too many VAC bans. Appeal in discord",
                ["Player Kicked"] = "[#ff0000]{player} Kicked[/#] - Reason\n{reason}",
                ["Profile Private"] = "Your Steam Profile is not allowed to be private on this server.",
                ["Profile Low Level"] = "You need a level {level} steam profile for this server.",
                ["Steam Level Hidden"] = "You are not allowed to hide your steam level on this server.",
                ["Strange Steam64ID"] = "Your steam id does not conform to steam standards.",
                ["Country Not Allowed"] = "Your country {country} is not allowed, only from {country2}",
                ["Permanent"] = "Permanent"
            }, this, "en");
        }

        #endregion

        #region Plugins methods
        string GetChatTag() => "[#008080ff][Server Armour]:[/#] ";
        void RegisterTag()
        {
            if (BetterChat != null && config.BetterChatDirtyPlayerTag != null && config.BetterChatDirtyPlayerTag.Length > 0)
                BetterChat?.Call("API_RegisterThirdPartyTitle", new object[] { this, new Func<IPlayer, string>(GetTag) });
        }

        string GetTag(IPlayer player)
        {
            if (BetterChat != null && IsPlayerDirty(player.Id) && config.BetterChatDirtyPlayerTag.Length > 0)
            {
                return $"[#FFA500][{config.BetterChatDirtyPlayerTag}][/#]";
            }
            else
            {
                return string.Empty;
            }
        }
        #endregion

        #region Log Helpers
        private void HandleLog(string message, string stackTrace, LogType type)
        {
            if (!type.Equals(LogType.Warning))
                return;

            var meshLog = logRegex.Match(message);
            if (!meshLog.Success || meshLog.Groups.Count < 3) return;

            var offendingPrefab = meshLog.Groups[1].ToString();
            var offendingPrefabPos = meshLog.Groups[2].ToString().ToVector3();

            var entities = new List<BaseEntity>();
            Vis.Entities(offendingPrefabPos, 5f, entities);

            if (entities.Count < 1) return;

            foreach (var entity in entities)
            {
                if (entity.PrefabName == offendingPrefab && !entity.IsDestroyed)
                {
                    entity.Kill();
                }
            }
        }
        #endregion

        #region Helpers 
        void SendReplyWithIcon(IPlayer player, string format, params object[] args)
        {
            int cnt = 0;
            string msg = GetMsg(format);
            foreach (var arg in args)
            {
                msg = msg.Replace("{" + cnt + "}", arg.ToString());
                cnt++;
            }

            if (!player.IsServer && player.IsConnected)
            {
                BasePlayer bPlayer = player.Object as BasePlayer;
                bPlayer?.SendConsoleCommand("chat.add", 2, config.IconSteamId, FixColors(msg));
            }
            else
            {
                player?.Reply(msg);
            }
        }
        void BroadcastWithIcon(string format, params object[] args)
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                SendReplyWithIcon(player.IPlayer, format, args);
            }
        }

        string FixColors(string msg) => msg.Replace("[/#]", "</color>").Replace("[", "<color=").Replace("]", ">");

        void AssignGroup(string id, string group) => permission.AddUserGroup(id, group);
        bool HasGroup(string id, string group) => permission.UserHasGroup(id, group);
        void RegPerm(string perm)
        {
            if (!permission.PermissionExists(perm)) permission.RegisterPermission(perm, this);
        }

        bool HasPerm(string id, string perm) => permission.UserHasPermission(id, perm);
        void GrantPerm(string id, string perm) => permission.GrantUserPermission(id, perm, this);

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static uint ConvertToTimestamp(string value)
        {
            return ConvertToTimestamp(ConverToDateTime(value));
        }

        private static uint ConvertToTimestamp(DateTime value)
        {
            TimeSpan elapsedTime = value - Epoch;
            return (uint)elapsedTime.TotalSeconds;
        }

        private static DateTime ConverToDateTime(string stringDate)
        {
            DateTime time;
            if (!DateTime.TryParseExact(stringDate, DATE_FORMAT, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                if (!DateTime.TryParseExact(stringDate, DATE_FORMAT2, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                {
                    if (!DateTime.TryParseExact(stringDate, DATE_FORMAT_BAN, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                    {
                        return DateTime.MaxValue;
                    }
                }
            }
            return time;
        }

        private static DateTime ConvertUnixToDateTime(long unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dtDateTime = Epoch;
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        #endregion

        #region Classes 
        public class EmbedFieldList
        {
            public string name { get; set; }
            public string value { get; set; }
            public bool inline { get; set; }
        }

        public class ISAPlayer
        {
            public int id { get; set; }
            public string steamid { get; set; }
            public int? steamlevel { get; set; }

            public int steamCommunityBanned { get; set; }
            public int steamVACBanned { get; set; }
            public int steamNumberOfVACBans { get; set; }
            public int steamDaysSinceLastBan { get; set; }
            public int steamNumberOfGameBans { get; set; }
            public string steamEconomyBan { get; set; }
            public int communityvisibilitystate { get; set; }
            public string personaname { get; set; }
            //public long? twitterBanId { get; set; }
            public List<EacBan> eacBans { get; set; }

            public uint? cacheTimestamp { get; set; }

            public uint? lastConnected { get; set; }
            public List<ISABan> bans { get; set; }
            //public ISAPlayer lender { get; set; }

            public IPInfo ipInfo { get; set; }

            public ISAPlayer(ulong steamId)
            {
                steamid = steamId.ToString();
                personaname = "";
                this.bans = new List<ISABan>();
            }
        }

        public class IPInfo
        {
#pragma warning disable 0649
            public string ip;
            public string lastcheck;
            public long longIp;
            public string asn;
            public string provider;
            public string continent;
            public string country;
            public string isocode;
            public string region;
            public string regioncode;
            public string city;
            public string latitude;
            public string longitude;
            public string proxy;
            public string type;
            public float rating;
            public bool isCloudComputing;
#pragma warning restore 0649
        }

        public class EacBan
        {
#pragma warning disable 0649
            public string id;
            public string steamid;
            public string createdAt;
            public string lastChecked;
            public string text;
            public string steamProfile;
            public bool isCron;
            public bool isTemp;
#pragma warning restore 0649
        }

        public class ISABan
        {
#pragma warning disable 0649
            public int id;
            public string adminSteamId;
            public int serverId;
            public ulong steamid;
            public ulong bannedBy;
            public string reason;
            public string banLength;
            public string serverName;
            public string serverIp;
            public string dateTime;
            public string created;
            public int gameId;
            public string? banUntil;
#pragma warning restore 0649

            public uint GetUnixBanUntill()
            {
                return ConvertToTimestamp(banUntil);
            }

            /// <summary>This method throws an exception when the date cannot be parsed, parmanent bans do not have a banUntil date.</summary>
            /// <exception cref="FormatException">This exception is thrown if it's a pemanent ban</exception>
            public DateTime BanUntillDateTime()
            {
                return ConverToDateTime(banUntil);
            }

            public bool IsBanned()
            {
                return banUntil == null || !DateIsPast(BanUntillDateTime());
            }
        }

        #endregion

        #region ESP Detection
        private void API_EspDetected(string jString)
        {
            JObject aObject = JObject.Parse(jString);
            DoRequest($"player/{aObject.GetValue("steamId")}/addesp", $"radarUrl={aObject.GetValue("radarUrl")}&violations={aObject.GetValue("violations")}");
        }
        #endregion

        #region Stash Warning System
        private void API_StashFoundTrigger(string jString)
        {
            JObject aObject = JObject.Parse(jString);
            DoRequest($"player/{aObject.GetValue("steamId")}/addstashtrigger",
                $"isFalsePositive={aObject.GetValue("isFalsePositive")}&" +
                $"isClanMember={aObject.GetValue("isClanMember")}&" +
                $"location={aObject.GetValue("location")}&" +
                $"position={aObject.GetValue("position")}&" +
                $"stashOwnerSteamId={aObject.GetValue("stashOwnerSteamId")}");
        }
        #endregion

        #region Arkan

        private void API_ArkanOnNoRecoilViolation(BasePlayer player, int NRViolationsNum, string jString)
        {
            if (!serverStarted || !config.SubmitArkanData)
                return;

            if (jString != null)
            {
                JObject aObject = JObject.Parse(jString);

                string shotsCnt = aObject.GetValue("ShotsCnt").ToString();
                string violationProbability = aObject.GetValue("violationProbability").ToString();
                string ammoShortName = aObject.GetValue("ammoShortName").ToString();
                string weaponShortName = aObject.GetValue("weaponShortName").ToString();
                string attachments = String.Join(", ", aObject.GetValue("attachments").Select(jv => (string)jv).ToArray());
                string suspiciousNoRecoilShots = aObject.GetValue("suspiciousNoRecoilShots").ToString();

                DoRequest($"player/{player.UserIDString}/addarkan/nr",
                    $"vp={violationProbability}&sc={shotsCnt}&ammo={ammoShortName}&weapon={weaponShortName}&attach={attachments}&snrs={suspiciousNoRecoilShots}", (c, r) => { });
            }
        }

        private void API_ArkanOnAimbotViolation(BasePlayer player, int AIMViolationsNum, string jString)
        {
            if (!config.SubmitArkanData)
                return;

            if (jString != null)
            {
                JObject aObject = JObject.Parse(jString);
                string attachments = String.Join(", ", aObject.GetValue("attachments").Select(jv => (string)jv).ToArray());
                string ammoShortName = aObject.GetValue("ammoShortName").ToString();
                string weaponShortName = aObject.GetValue("weaponShortName").ToString();
                string damage = aObject.GetValue("damage").ToString();
                string bodypart = aObject.GetValue("bodyPart").ToString();
                string hitsData = aObject.GetValue("hitsData").ToString();
                string hitInfoProjectileDistance = aObject.GetValue("hitInfoProjectileDistance").ToString();

                DoRequest($"player/{player.UserIDString}/addarkan/aim",
                    $"attach={attachments}&ammo={ammoShortName}&weapon={weaponShortName}&dmg={damage}&bp={bodypart}&distance={hitInfoProjectileDistance}&hits={hitsData}", (c, r) => { });
            }
        }

        #endregion

        #region webrequest
        private void TranslateCode(int statusCode)
        {
            switch (statusCode)
            {
                case 429:
                    PrintWarning("Rate limited. Upgrade package on https://serverarmour.com");
                    break;
                case 502:
                    break;
                case 500:
                    break;
            }
        }

        private void GetJson(string url, Action<int, JObject> callback)
        {
            DoRequest(url, null, (c, s) =>
            {
                JObject json = new JObject();
                if (c < 300)
                {
                    json = JObject.Parse(s);
                }
                callback(c, json);
            }, RequestMethod.GET);
        }

        private void DoRequest(string url, string body = null, Action<int, string> callback = null, RequestMethod requestType = RequestMethod.POST, int retryInSeconds = 0, int retryCounter = 0)
        {
            try
            {
                webrequest.Enqueue($"{api_hostname}/api/v1/plugin/{url}", body, (code, response) =>
                {
                    var status = code < 299 ? "OK" : "NOK";
                    LogDebug($"API ({url}) Response = {code} {status}: \n{response}");
                    if (code < 299)
                    {
                        if (callback != null)
                            callback(code, response);
                        return;
                    }

                    TranslateCode(code);

                    if (retryInSeconds > 0 && retryCounter < 5)
                        timer.Once(retryInSeconds, () => DoRequest(url, body, callback, requestType, retryInSeconds, retryCounter++));

                }, this, requestType, headers);
            }
            catch (Exception e) { }
        }

        private void CalcElo(string steamIdKiller, string steamIdVictim, string killInfo, Action<int, string> callback = null, int retryInSeconds = 0)
        {
            webrequest.Enqueue($"{api_hostname}/api/v1/elo/{steamIdKiller}/{steamIdVictim}", killInfo, (code, response) =>
            {
                if (code < 400)
                {
                    Interface.CallHook("OnEloChange", JObject.Parse(response));
                    if (callback != null)
                        callback(code, response);
                    return;
                }

                TranslateCode(code);

                if (retryInSeconds > 0)
                    timer.Once(retryInSeconds, () => CalcElo(steamIdKiller, steamIdVictim, killInfo, callback));

            }, this, RequestMethod.POST, headers);
        }

        private void FetchElo(string steamId) => FetchEloUpdate(steamId, (c, r) => { });
        private void FetchEloUpdate(string steamId, Action<int, string> callback, int retryInSeconds = 0)
        {
            webrequest.Enqueue($"{api_hostname}/api/v1/elo/{steamId}", null, (code, response) =>
            {
                if (code < 299)
                {
                    Interface.CallHook("OnEloUpdate", JObject.Parse(response));
                    callback(code, response);
                    return;
                }

                TranslateCode(code);
                if (retryInSeconds > 0)
                    timer.Once(retryInSeconds, () => FetchEloUpdate(steamId, callback));

            }, this, RequestMethod.GET, headers);
        }
        #endregion

        #region Combat
        private void UploadCombatEntries(string logEntries)
        {
            // LogDebug("Combat Logs upload requested");
            DoRequest("combat_log", $"entries={logEntries}", (c, r) =>
            {
                Interface.CallHook("OnEntriesUploaded", c, r);
            });
        }
        #endregion

        #region Clan/Team Helpers
        List<ulong> GetTeamMembers(ulong userid) => RelationshipManager.ServerInstance.FindPlayersTeam(userid)?.members;
        string GetClanTag(string userid) => Clans?.Call<string>("GetClanOf", userid);
        List<ulong> GetClan(string userid) => Clans?.Call<JObject>("GetClan", GetClanTag(userid))?.GetValue("members").ToObject<List<ulong>>();
        #endregion

        #region Configuration
        private class SAConfig
        {
            // Config default vars
            public string ServerIp = "";
            public bool Debug = false;
            public bool ShowProtectedMsg = true;
            public bool AutoKickOn = true;
            public bool EnableTotalBanKick = false;

            public int AutoKickCeiling = 3;
            public int AutoVacBanCeiling = 1;
            public int AutoGameBanCeiling = 2;
            public int AutoTotalBanCeiling = 30;

            public int DissallowVacBanDays = 90;
            public int BroadcastPlayerBanReportVacDays = 120;

            public bool AutoKickFamilyShare = false;
            public bool AutoKickFamilyShareIfDirty = false;
            public string BetterChatDirtyPlayerTag = string.Empty;
            public bool BroadcastPlayerBanReport = true;
            public bool BroadcastNewBans = true;
            public bool BroadcastKicks = false;
            public bool ServerAdminShareDetails = true;
            public string ServerAdminName = string.Empty;
            public string ServerAdminEmail = string.Empty;
            public string ServerApiKey = string.Empty;
            public string SteamApiKey = string.Empty;

            public bool AutoKick_KickHiddenLevel = false;
            public int AutoKick_MinSteamProfileLevel = -1;
            public bool AutoKick_KickPrivateProfile = false;
            public bool AutoKick_KickWeirdSteam64 = true;

            public bool AutoKick_KickTwitterGameBanned = false;
            public bool AutoKick_BadIp = true;
            public bool AutoKick_BadIp_IgnoreComputing = true;

            public string DiscordWebhookURL = DISCORD_INTRO_URL;
            public string DiscordBanWebhookURL = DISCORD_INTRO_URL;
            public bool DiscordQuickConnect = true;
            public bool DiscordOnlySendDirtyReports = true;
            public bool DiscordJoinReports = true;
            public bool DiscordKickReport = true;
            public bool DiscordBanReport = true;
            public bool DiscordNotifyGameBan = true;
            public bool SubmitArkanData = true;
            public bool RconBroadcast = false;
            public bool AutoKick_IgnoreNvidia = true;
            public bool AutoKick_NetworkBan = true;
            public bool AutoKick_ActiveBans = false; 
            public bool AutoKick_SameServerIp = false;

            public string OwnerSteamId = "";
            public string ClanBanPrefix = "Assoc Ban -> {playerId}: {reason}";
            public bool ClanBanTeams = true;
            public bool IgnoreAdmins = true;
            public bool UseEloSystem = true;

            public int AutoKickMaxPing = 250;
            public string AutoKickLimitCountry = "";

            public string IconSteamId = "76561199044451528";
            public bool IgnoreCheatDetected = true;

            // Plugin reference
            private ServerArmour _plugin;
            public SAConfig(ServerArmour plugin)
            {
                this._plugin = plugin;
                /**
                 * Load all saved config values
                 * */
                GetConfig(ref ServerIp, "Server Info", "Your Server IP");

                GetConfig(ref IgnoreAdmins, "General", "Ignore Admins");
                GetConfig(ref Debug, "General", "Debug: Show additional debug console logs");
                GetConfig(ref IconSteamId, "General", "SteamID for message icon");


                GetConfig(ref ShowProtectedMsg, "Show Protected MSG");
                GetConfig(ref BetterChatDirtyPlayerTag, "Better Chat: Tag for dirty users");

                GetConfig(ref BroadcastPlayerBanReport, "Broadcast", "Player Reports");
                GetConfig(ref BroadcastPlayerBanReportVacDays, "Broadcast", "When VAC is younger than");
                GetConfig(ref BroadcastNewBans, "Broadcast", "New bans");
                GetConfig(ref BroadcastKicks, "Broadcast", "Kicks");
                GetConfig(ref RconBroadcast, "Broadcast", "RCON");

                GetConfig(ref ServerAdminShareDetails, "io.serverarmour.com", "Share details with other server owners");
                GetConfig(ref ServerApiKey, "io.serverarmour.com", "Server Key");
                GetConfig(ref SteamApiKey, "io.serverarmour.com", "Steam API Key");
                GetConfig(ref ServerAdminName, "io.serverarmour.com", "Owner Real Name");
                GetConfig(ref ServerAdminEmail, "io.serverarmour.com", "Owner Email");
                GetConfig(ref OwnerSteamId, "io.serverarmour.com", "Owner Steam64 ID");
                GetConfig(ref SubmitArkanData, "io.serverarmour.com", "Submit Arkan Data");

                GetConfig(ref AutoKickOn, "Auto Kick", "Enabled");

                GetConfig(ref AutoKick_NetworkBan, "Auto Kick", "Bans on your network");
                GetConfig(ref AutoKick_SameServerIp, "Auto Kick", "Bans from your server ip");
                GetConfig(ref AutoKick_ActiveBans, "Auto Kick", "Alts that have active bans on your servers");

                GetConfig(ref AutoKickCeiling, "Auto Kick", "Max allowed previous bans");
                GetConfig(ref AutoTotalBanCeiling, "Auto Kick", "Max allowed total bans (server + game + vac)");
                GetConfig(ref EnableTotalBanKick, "Auto Kick", "Enable Total bans (server + game + vac) kick");

                GetConfig(ref AutoKick_BadIp, "Auto Kick", "VPN", "Enabled");
                GetConfig(ref AutoKick_IgnoreNvidia, "Auto Kick", "VPN", "Ignore nVidia Cloud Gaming");

                GetConfig(ref AutoKick_KickTwitterGameBanned, "Auto Kick", "Users that have been banned on rusthackreport");

                GetConfig(ref AutoKick_KickPrivateProfile, "Auto Kick", "Steam", "Private Steam Profiles");
                GetConfig(ref AutoKick_KickHiddenLevel, "Auto Kick", "Steam", "When Steam Level Hidden");
                GetConfig(ref AutoKick_MinSteamProfileLevel, "Auto Kick", "Steam", "Min Allowed Steam Level (-1 disables)");
                GetConfig(ref AutoKick_KickWeirdSteam64, "Auto Kick", "Steam", "Profiles that do no conform to the Steam64 IDs (Highly recommended)");
                GetConfig(ref AutoVacBanCeiling, "Auto Kick", "Steam", "Max allowed VAC bans");
                GetConfig(ref AutoGameBanCeiling, "Auto Kick", "Steam", "Max allowed Game bans");
                GetConfig(ref DissallowVacBanDays, "Auto Kick", "Steam", "Min age of VAC ban allowed");
                GetConfig(ref AutoKickFamilyShare, "Auto Kick", "Steam", "Family share accounts");
                GetConfig(ref AutoKickFamilyShareIfDirty, "Auto Kick", "Steam", "Family share accounts that are dirty");

                GetConfig(ref AutoKickMaxPing, "Auto Kick", "Ping", "Max Ping Allowed");
                GetConfig(ref AutoKickLimitCountry, "Auto Kick", "Ping", "Limit players ONLY to this country ISO code, kick rest");

                GetConfig(ref DiscordWebhookURL, "Discord", "Webhook URL");
                GetConfig(ref DiscordBanWebhookURL, "Discord", "Bans Webhook URL");
                GetConfig(ref DiscordQuickConnect, "Discord", "Show Quick Connect On report");
                GetConfig(ref DiscordJoinReports, "Discord", "Send Join Player Reports");
                GetConfig(ref DiscordOnlySendDirtyReports, "Discord", "Send Only Dirty Player Reports");
                GetConfig(ref DiscordNotifyGameBan, "Discord", "Notify when a player has received a game ban");
                GetConfig(ref DiscordKickReport, "Discord", "Send Kick Report");
                GetConfig(ref DiscordBanReport, "Discord", "Send Ban Report");

                GetConfig(ref ClanBanPrefix, "Clan Ban", "Reason Prefix");
                GetConfig(ref ClanBanTeams, "Clan Ban", "Ban Native Team Members");

                GetConfig(ref IgnoreCheatDetected, "Anti Hack", "Ignore Cheat Detected");

                GetConfig(ref UseEloSystem, "Plugin", "Use ELO system?");

                plugin.SaveConfig();
            }

            private void GetConfig<T>(ref T variable, params string[] path)
            {
                if (path.Length == 0) return;

                if (_plugin.Config.Get(path) == null)
                {
                    SetConfig(ref variable, path);
                    _plugin.PrintWarning($"Added new field to config: {string.Join("/", path)}");
                }

                variable = (T)Convert.ChangeType(_plugin.Config.Get(path), typeof(T));
            }

            public void SetConfig<T>(ref T variable, params string[] path) => _plugin.Config.Set(path.Concat(new object[] { variable }).ToArray());
        }

        protected override void LoadDefaultConfig() => PrintWarning("Generating new configuration file.");
        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = new SAConfig(this);
            }
            catch (InvalidCastException)
            {
                PrintError("Your config seems to be corrupted.");
                Interface.Oxide.UnloadPlugin(Name);
            }
        }
        #endregion
    }
}