using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Cupboard for Friends", "LaserHydra", "2.0.0", ResourceId = 1578)]
    [Description("Only allow friends of already authorized people to authorize themselves.")]
    // New manteiner eKiSDe aka j0se
    class CupboardForFriends : RustPlugin
    {
        bool debug = false;
        bool blockDamage = false;

        bool adminByPass = true;

        [PluginReference]
        Plugin Clans, Friends;

        ////////////////////////////////////////
        ///     On Plugin Loaded
        ////////////////////////////////////////

        void Loaded()
        {
            LoadMessages();
            LoadConfig();

            if (Friends == null)
                PrintWarning($"Friends could not be found! Friends is an OPTIONAL addition for '{this.Title}'' to work! Get it here: http://oxidemod.org/plugins/686/");

            if (Clans == null)
                PrintWarning($"Clans could not be found! Clans is an OPTIONAL addition for '{this.Title}'! Get it here: http://oxidemod.org/plugins/842/");

            if (Config["Settings", "Block Damage"] != null)
                blockDamage = (bool)Config["Settings", "Block Damage"];

            if (Config["Settings", "Admin ByPass"] != null)
                adminByPass = (bool)Config["Settings", "Admin ByPass"];
        }

        ////////////////////////////////////////
        ///     Config & Message Loading
        ////////////////////////////////////////

        void LoadMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"Blocked Authorization", "You can not authorize yourself or clear the list of this cupboard as none of the authorized players has you on his friendlist! You can be added as friend by typing /friend add <your name>"},
                {"Blocked Damage", "You can not damage this cupboard as none of the authorized players has you on his friendlist! You can be added as friend by typing /friend add <your name>"}
            }, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"Blocked Authorization", "Tu no te puedes autorizar o borrar la lista porque no eres amigo o miembro del clan de los jugadores autorizados."},
                {"Blocked Damage", "Tu no te dañar el armario porque no eres amigo o miembro del clan de los jugadores autorizados."}
            }, this, "es");
        }

        new void LoadConfig()
        {
            SetConfig("Settings", "Block Damage", true);
            SetConfig("Settings", "Admin ByPass", true);
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Generating new config file...");
        }

        ////////////////////////////////////////
        ///    Subject Related
        ////////////////////////////////////////

        void OnEntityTakeDamage(BaseCombatEntity vic, HitInfo info)
        {
            if (!blockDamage)
                return;

            if (vic != null && vic is BuildingPrivlidge && info != null && info?.Initiator != null && info?.Initiator?.ToPlayer() != null)
            {
                object blocked = TestForFriends((BuildingPrivlidge)vic, info.Initiator.ToPlayer(), true);
                info.damageTypes.Scale(Rust.DamageType.Heat, 0f);

                if (blocked != null)
                {
                    info?.damageTypes?.ScaleAll(0f);
                    info.HitBone = 0;
                    info.HitEntity = null;
                    info.material = new UnityEngine.PhysicMaterial();
                    info.DidHit = false;
                    info.DoHitEffects = false;

                    SendChatMessage(info.Initiator.ToPlayer(), GetMsg("Blocked Damage", info.Initiator.ToPlayer().UserIDString));
                }
            }
        }

        object OnCupboardClearList(BuildingPrivlidge priviledge, BasePlayer player) => TestForFriends(priviledge, player);

        object OnCupboardAuthorize(BuildingPrivlidge priviledge, BasePlayer player) => TestForFriends(priviledge, player);

        object TestForFriends(BuildingPrivlidge priviledge, BasePlayer player, bool isDamage = false)
        {
            DevMsg($"-------------------------------------------");
            DevMsg($"Any authed: {priviledge.AnyAuthed()}");

            if (adminByPass && player.IsAdmin)
                return null;

            if (priviledge.AnyAuthed())
            {
                bool isFriend = false;

                List<string> ids = (from id in priviledge.authorizedPlayers
                                    select id.userid.ToString()).ToList();

                foreach (string uid in ids)
                {
                    DevMsg($"------------------------");
                    DevMsg($"Current uid: {uid}");

                    if (IsFriend(player, uid))
                    {
                        DevMsg($"Found Friend: {uid}");
                        isFriend = true;
                    }
                    else if (Clans != null && IsClanMember(player, uid))
                    {
                        DevMsg($"Found Clanmember: {uid}");
                        isFriend = true;
                    }
                    else if (IsTeamMember(player, uid)) // comprobar si es del mismo team
                    {
                        DevMsg($"Found Teammember: {uid}");
                        isFriend = true;
                    }
                }

                DevMsg($"isFriend: {isFriend}");

                if (!isFriend)
                {
                    if (!isDamage)
                        SendChatMessage(player, GetMsg("Blocked Authorization", player.UserIDString));

                    return false;
                }
            }

            return null;
        }

        ////////////////////////////////////////
        ///     Friends API
        ////////////////////////////////////////

        bool IsFriend(BasePlayer player, string friendID)
        {
            if (Friends == null)
                return false;



            bool isFriend = (bool)(Friends.Call("HasFriendS", friendID, player.UserIDString) ?? false);

            DevMsg($"IsFriend({player}, {friendID})");
            DevMsg($"IsFriend: returning {isFriend}");

            return isFriend;
        }

        bool IsClanMember(BasePlayer player, string targetID)
        {
            if (Clans == null)
                return false;

            string playerClan = (string)Clans.Call("GetClanOf", player.UserIDString) ?? "";
            string targetClan = (string)Clans.Call("GetClanOf", targetID) ?? "";

            DevMsg($"{player.displayName}: '{playerClan}' (Length: {playerClan.Length}), {targetID}: '{targetClan}' (Length: {targetClan.Length})");

            bool isClanMember = (playerClan == "" || targetClan == "") ? false : playerClan == targetClan;

            DevMsg($"IsClanMember({player}, {targetID})");
            DevMsg($"IsClanMember: returning {isClanMember}");

            return isClanMember;
        }


        bool IsTeamMember(BasePlayer player, string targetID)
        {
            if (player.currentTeam != (long)0)
            {
                RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (playerTeam == null) return false;
                if (playerTeam.members.Contains(ulong.Parse(targetID))) return true; // long.Parse ??¿¿

            }
            return false;

        }

        ////////////////////////////////////////
        ///     Converting
        ////////////////////////////////////////

        string ListToString(List<string> list, int first, string seperator) => string.Join(seperator, list.Skip(first).ToArray());

        ////////////////////////////////////////
        ///     Config & Message Related
        ////////////////////////////////////////

        void SetConfig(params object[] args)
        {
            List<string> stringArgs = (from arg in args select arg.ToString()).ToList<string>();
            stringArgs.RemoveAt(args.Length - 1);

            if (Config.Get(stringArgs.ToArray()) == null) Config.Set(args);
        }

        string GetMsg(string key, string userID = null) => lang.GetMessage(key, this, userID);

        ////////////////////////////////////////
        ///     Chat Handling
        ////////////////////////////////////////

        void DevMsg(string prefix, string msg = null)
        {
            if (debug && BasePlayer.FindByID(76561198008644030) != null)
                BasePlayer.FindByID(76561198008644030).ConsoleMessage(msg == null ? prefix : "<color=#00FF8D>" + prefix + "</color>: " + msg);
        }

        void SendChatMessage(BasePlayer player, string prefix, string msg = null) => SendReply(player, msg == null ? prefix : "<color=#00FF8D>" + prefix + "</color>: " + msg);
    }
}