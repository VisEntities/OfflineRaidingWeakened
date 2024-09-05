/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
using Newtonsoft.Json;
using Rust;
using System.Collections.Generic;
using static BuildingManager;

namespace Oxide.Plugins
{
    [Info("Offline Raiding Weakened", "VisEntities", "1.0.1")]
    [Description("Lowers the damage inflicted on buildings when owners are offline.")]
    public class OfflineRaidingWeakened : RustPlugin
    {
        #region Fields

        private static OfflineRaidingWeakened _plugin;
        private static Configuration _config;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Damage Reduction Percentage")]
            public int DamageReductionPercentage { get; set; }
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
                DamageReductionPercentage = 50
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

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity == null || hitInfo == null)
                return;

            if (!hitInfo.damageTypes.Has(DamageType.Explosion))
                return;

            BasePlayer attacker = hitInfo.InitiatorPlayer;
            if (attacker == null)
                return;

            if (PermissionUtil.HasPermission(attacker, PermissionUtil.IGNORE))
                return;

            BasePlayer entityOwner = PlayerUtil.FindById(entity.OwnerID);
            if (entityOwner == null)
                return;

            if (entityOwner == attacker)
                return;

            if (PlayerUtil.AreTeammates(entityOwner.userID, attacker.userID))
                return;

            Building building = BuildingUtil.TryGetBuildingForEntity(entity, minimumBuildingBlocks: 1, mustHaveBuildingPrivilege: true);
            if (building == null)
                return;

            List<ulong> authedPlayers = Pool.Get<List<ulong>>();
            foreach (var privilege in building.buildingPrivileges)
            {
                foreach (var authedPlayer in privilege.authorizedPlayers)
                    authedPlayers.Add(authedPlayer.userid);
            }

            bool allOffline = true;
            foreach (var playerId in authedPlayers)
            {
                if (!PlayerUtil.Offline(playerId))
                {
                    allOffline = false;
                    break;
                }

                var team = PlayerUtil.GetTeam(playerId);
                if (team != null)
                {
                    foreach (var memberId in team.members)
                    {
                        if (memberId == entityOwner.userID || !PlayerUtil.Offline(memberId))
                        {
                            allOffline = false;
                            break;
                        }
                    }
                }

                if (!allOffline)
                    break;
            }

            Pool.FreeUnmanaged(ref authedPlayers);

            if (allOffline)
            {
                float reduction = _config.DamageReductionPercentage / 100f;
                hitInfo.damageTypes.ScaleAll(1 - reduction);
                SendMessage(attacker, Lang.DamageReduced, _config.DamageReductionPercentage);
            }
        }

        #endregion Oxide Hooks

        #region Helper Classes

        private static class PlayerUtil
        {
            public static BasePlayer FindById(ulong playerId)
            {
                return RelationshipManager.FindByID(playerId);
            }

            public static bool AreTeammates(ulong firstPlayerId, ulong secondPlayerId)
            {
                RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindPlayersTeam(firstPlayerId);
                if (team != null && team.members.Contains(secondPlayerId))
                    return true;

                return false;
            }

            public static RelationshipManager.PlayerTeam GetTeam(ulong playerId)
            {
                if (RelationshipManager.ServerInstance == null)
                    return null;

                return RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            }

            public static bool AuthedInBuilding(BasePlayer player, Building building)
            {
                foreach (var privilege in building.buildingPrivileges)
                {
                    if (privilege.IsAuthed(player))
                        return true;
                }

                return false;
            }

            public static bool Offline(ulong playerId)
            {
                BasePlayer player = FindById(playerId);
                return player == null || !player.IsConnected;
            }
        }

        private static class BuildingUtil
        {
            public static Building TryGetBuildingForEntity(BaseEntity entity, int minimumBuildingBlocks, bool mustHaveBuildingPrivilege = true)
            {
                BuildingBlock buildingBlock = entity as BuildingBlock;
                DecayEntity decayEntity = entity as DecayEntity;

                uint buildingId = 0;
                if (buildingBlock != null)
                {
                    buildingId = buildingBlock.buildingID;
                }
                else if (decayEntity != null)
                {
                    buildingId = decayEntity.buildingID;
                }

                Building building = server.GetBuilding(buildingId);
                if (building != null &&
                    building.buildingBlocks.Count >= minimumBuildingBlocks &&
                    (!mustHaveBuildingPrivilege || building.HasBuildingPrivileges()))
                {
                    return building;
                }

                return null;
            }
        }

        #endregion Helper Classes

        #region Permissions

        private static class PermissionUtil
        {
            public const string IGNORE = "offlineraidingweakened.ignore";
            private static readonly List<string> _permissions = new List<string>
            {
                IGNORE,
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

        #endregion Permissions

        #region Localization

        private class Lang
        {
            public const string DamageReduced = "DamageReduced";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.DamageReduced] = "Your damage has been reduced by {0}% because the base owners are offline.",
            }, this, "en");
        }

        private void SendMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = lang.GetMessage(messageKey, this, player.UserIDString);
            if (args.Length > 0)
                message = string.Format(message, args);

            SendReply(player, message);
        }

        #endregion Localization
    }
}