using Oxide.Core;
using Oxide.Core.Configuration;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Prod", "YarinQuapi", "3.0.0")]
    [Description("Building ownership and access information tool")]
    public class Prod : RustPlugin
    {
        // Unified color palette
        private const string ColorPrimary = "#91ffff";  // Section titles
        private const string ColorLabel = "#ffff8f";    // Field labels
        private const string ColorValue = "#ffffff";   // Data values
        private const string ColorMuted = "#aaaaaa";   // Dividers, IDs, empty states
        private const string ColorError = "#ff6b6b";   // Errors and denied access

        #region Fields
        private DynamicConfigFile _dataFile;
        private ProdPluginData _pluginData;

        private static bool _serverInitialized = false;

        // Configuration fields
        private string _prodCommand;
        private string _prodPermission;
        private int _prodAuth;
        private float _maxRayDistance;
        private bool _printToConsole;
        private bool _debugMode;
        private bool _showBuildingGrade;
        private bool _showBuildingStability;
        private bool _showRustTeamInfo;

        // Message fields (plain text from config; colors applied at display time)
        private string _informationAdded;
        private string _noAccess, _noTargetFound;
        private string _noCodeAccess, _noKeyLockFound, _noCodelock;
        private string _noKeyLockOwner, _noKeyAccess;
        private string _toolCupboardNoAuth, _authorization;
        private string _noContainerOwner, _noDeployableOwner, _noVehicleOwner, _noBlockOwnerFound, _noElectricalOwner;
        private string _noComputerStationAuth, _noComputerStationAuthError, _noTurretAuth;
        private string _noEntityInfo, _noGenericOwner;
        private string _stabilityGrounded;

        // Section title names (formatted via helpers)
        private string _prodTitle;
        private string _codelockTitle;
        private string _toolCupboardTitle;
        private string _buildingGradeTitle;
        private string _autoturretTitle, _sleepingBagTitle;
        private string _storageContainerTitle, _vehicleTitle, _electricalTitle, _computerStationTitle;
        private string _keylockTitle;

        // Reflection fields
        private FieldInfo _codeLockWhitelistFieldInfo;
        private FieldInfo _codeNumFieldInfo;
        private FieldInfo _keyLockKeyCodeFieldInfo;

        private readonly Dictionary<ulong, string> _playerNameCache = new Dictionary<ulong, string>();
        #endregion

        #region Data Model
        public class ProdPluginData
        {
            public Dictionary<string, Dictionary<string, object>> Categories { get; set; } = new Dictionary<string, Dictionary<string, object>>();
        }
        #endregion

        #region Oxide Hooks
        private void Loaded()
        {
            LoadConfiguration();
            InitializeReflection();
            RegisterCommands();
            InitializeDataFile();

            Puts("Prod plugin loaded.");
        }

        private void OnServerInitialized()
        {
            _serverInitialized = true;
            Puts("Prod plugin initialized successfully.");

            foreach (var iPlayer in covalence.Players.All)
            {
                CachePlayerName(ulong.Parse(iPlayer.Id), iPlayer.Name);
            }
            if (_debugMode) Puts($"Pre-populated player name cache with {covalence.Players.All.Count()} players.");
        }

        private void OnServerSave()
        {
            SaveProdData();
        }

        private void Unload()
        {
            SaveProdData();
            _playerNameCache.Clear();
            _serverInitialized = false;
            Puts("Prod plugin unloaded.");
        }

        private void OnEntityBuilt(HeldEntity heldEntity, GameObject gameObject)
        {
            if (!_serverInitialized || heldEntity == null || gameObject == null)
                return;

            var buildingBlock = gameObject.GetComponent<BuildingBlock>();
            if (buildingBlock == null)
                return;

            var player = heldEntity.GetOwnerPlayer();
            if (player == null)
                return;

            // Should not happen, ever.
            var existingData = GetProdData(buildingBlock.net.ID.ToString(), "BuildingBlocks");
            if (existingData != null)
                return;

            SetProdData(buildingBlock.net.ID.ToString(), "BuildingBlocks", new Dictionary<string, object>
            {
                ["owner"] = player.IPlayer.Id,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["position"] = $"{buildingBlock.transform.position.x:F1},{buildingBlock.transform.position.y:F1},{buildingBlock.transform.position.z:F1}"
            });

            CachePlayerName(player.userID, player.displayName);
            if (_debugMode) Puts($"Stored building block {buildingBlock.net.ID} for owner {player.displayName} ({player.userID})");
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null)
                CachePlayerName(player.userID, player.displayName);
        }

        private void OnEntityDeath(BaseCombatEntity entity)
        {
            if (!_serverInitialized || entity == null)
                return;

            string entityId = entity.net.ID.ToString();
            string category = "";

            if (entity is BuildingBlock) category = "BuildingBlocks";
            else if (entity is StorageContainer) category = "StorageContainers";
            else if (entity is BaseVehicle) category = "Vehicles";
            else if (entity is AutoTurret || entity is FlameTurret || entity is BearTrap || entity is Landmine || entity is Workbench || entity is ResearchTable || entity is RepairBench) category = "Deployables";
            else if (entity is IOEntity) category = "ElectricalComponents";
            // Add more types as needed that you want to track ownership for

            if (!string.IsNullOrEmpty(category))
            {
                if (RemoveProdData(entityId, category))
                {
                    if (_debugMode) Puts($"Removed data for {entity.ShortPrefabName} (ID: {entityId}) from {category} due to destruction.");
                }
            }
        }
        #endregion

        #region Initialization
        private void InitializeReflection()
        {
            try
            {
                var codeLockType = typeof(CodeLock);
                _codeLockWhitelistFieldInfo = codeLockType.GetField("whitelistPlayers",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic);
                _codeNumFieldInfo = codeLockType.GetField("code",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic);

                var keyLockType = typeof(KeyLock);
                _keyLockKeyCodeFieldInfo = keyLockType.GetField("keyCode",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic);

                if (_codeLockWhitelistFieldInfo == null || _codeNumFieldInfo == null || _keyLockKeyCodeFieldInfo == null)
                {
                    PrintWarning("Some reflection fields could not be found. Code/Key lock functionality may be limited.");
                }
            }
            catch (Exception ex)
            {
                PrintError($"Failed to initialize reflection fields: {ex.Message}");
            }
        }

        private void RegisterCommands()
        {
            cmd.AddChatCommand(_prodCommand, this, nameof(ChatCommandProd));
        }

        private void InitializeDataFile()
        {
            // Get the DynamicConfigFile instance
            _dataFile = Interface.GetMod().DataFileSystem.GetDatafile("Prod_BuildingData");

            // Try to read the existing data into our ProdPluginData object
            try
            {
                _pluginData = _dataFile.ReadObject<ProdPluginData>();
                if (_pluginData == null) // If the file was empty or corrupted, create a new instance
                {
                    _pluginData = new ProdPluginData();
                    if (_debugMode) Puts("Created new ProdPluginData instance as file was empty/corrupted.");
                }
            }
            catch (Exception ex)
            {
                PrintError($"Failed to read Prod_BuildingData: {ex.Message}. Creating new data instance.");
                _pluginData = new ProdPluginData(); // Fallback to a new instance on error
            }

            // Ensure Categories dictionary is initialized within the loaded/new data object
            if (_pluginData.Categories == null)
            {
                _pluginData.Categories = new Dictionary<string, Dictionary<string, object>>();
            }
        }
        #endregion

        #region Configuration
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating new configuration file");
            Config.Clear(); // Clear existing config to ensure defaults are applied
            LoadConfiguration(); // Load configuration after clearing
        }

        private void LoadConfiguration()
        {
            // Core settings
            _printToConsole = GetConfigValue("Settings", "Print to console instead of chat", false);
            _prodAuth = GetConfigValue("Settings", "Required auth level", 1);
            _prodCommand = GetConfigValue("Settings", "Chat command", "prod");
            _showBuildingGrade = GetConfigValue("Settings", "Show building grade", true);
            _showBuildingStability = GetConfigValue("Settings", "Show building stability", true);
            _maxRayDistance = GetConfigValue("Settings", "Maximum raycast distance", 10f);
            _showRustTeamInfo = GetConfigValue("Settings", "Show Rust team information", true); // Unused
            _debugMode = GetConfigValue("Settings", "Enable debug logging", false);
            _prodPermission = GetConfigValue("Settings", "Permission (Auth alternative)", "prod.admin");

            // Messages (plain text; formatting applied in display helpers)
            _informationAdded = GetConfigValue("Messages", "Information added to console", "New information was printed to your console.");
            _noAccess = GetConfigValue("Messages", "No access", "You don't have permission to use this command.");
            _noTargetFound = GetConfigValue("Messages", "No target found", "You must look at an entity or building block!");
            _toolCupboardNoAuth = GetConfigValue("Messages", "Tool Cupboard (No Auth)", "No players have access to this tool cupboard.");
            _authorization = GetConfigValue("Messages", "Authorization (TC, Turrets, etc.)", "Authorized Players ({0})");
            _noComputerStationAuth = GetConfigValue("Messages", "Computer Station (No Auth)", "No authorized players on this computer station.");
            _noComputerStationAuthError = GetConfigValue("Messages", "Computer Station (Error)", "Could not read computer station authorization list.");
            _noBlockOwnerFound = GetConfigValue("Messages", "No block owner", "No owner found for this building block.");
            _noCodeAccess = GetConfigValue("Messages", "No code access", "No players have access to this code lock.");
            _noKeyLockFound = GetConfigValue("Messages", "No KeyLock found", "No key lock found.");
            _noKeyLockOwner = GetConfigValue("Messages", "No KeyLock owner", "Lock placer unknown.");
            _noKeyAccess = GetConfigValue("Messages", "No key access", "No players on the server currently have access to this key lock.");
            _noCodelock = GetConfigValue("Messages", "No Codelock", "No code lock found.");
            _noVehicleOwner = GetConfigValue("Messages", "No vehicle owner", "No owner found for this vehicle.");
            _noContainerOwner = GetConfigValue("Messages", "No container owner", "No owner found for this container.");
            _noDeployableOwner = GetConfigValue("Messages", "No deployable owner", "No owner found for this deployable.");
            _noElectricalOwner = GetConfigValue("Messages", "No electrical component owner", "No owner found for this electrical component.");
            _noTurretAuth = GetConfigValue("Messages", "No Turret Auth", "No players are authorized on this turret.");
            _noEntityInfo = GetConfigValue("Messages", "No entity info", "Entity detected but no specific ownership information available.");
            _noGenericOwner = GetConfigValue("Messages", "No generic owner", "No owner found.");
            _stabilityGrounded = GetConfigValue("Messages", "Stability grounded", "Grounded (stability disabled)");

            // Titles (section names only; formatted via helpers)
            _prodTitle = GetConfigValue("Titles", "Prod", "Prod");
            _codelockTitle = GetConfigValue("Titles", "Codelock", "Codelock");
            _keylockTitle = GetConfigValue("Titles", "Keylock", "Key Lock");
            _toolCupboardTitle = GetConfigValue("Titles", "Toolcupboard", "Tool Cupboard");
            _buildingGradeTitle = GetConfigValue("Titles", "Building Grade", "Building Grade");
            _autoturretTitle = GetConfigValue("Titles", "Auto Turret", "Auto Turret");
            _storageContainerTitle = GetConfigValue("Titles", "Storage Container", "Storage Container");
            _electricalTitle = GetConfigValue("Titles", "Electrical Components", "Electrical / IO");
            _vehicleTitle = GetConfigValue("Titles", "Vehicles", "Vehicle");
            _computerStationTitle = GetConfigValue("Titles", "Computer Station", "Computer Station");
            _sleepingBagTitle = GetConfigValue("Titles", "Sleeping Bag", "Sleeping Bag");

            SaveConfig();
        }

        private T GetConfigValue<T>(string category, string setting, T defaultValue)
        {
            if (!(Config[category] is Dictionary<string, object> categoryData))
            {
                categoryData = new Dictionary<string, object>();
                Config[category] = categoryData;
            }

            if (!categoryData.TryGetValue(setting, out var value))
            {
                categoryData[setting] = defaultValue;
                return defaultValue;
            }

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (InvalidCastException)
            {
                PrintWarning($"Invalid config value type for {category}.{setting}, expected {typeof(T).Name}, got {value.GetType().Name}. Using default value.");
                categoryData[setting] = defaultValue;
                return defaultValue;
            }
            catch (FormatException)
            {
                PrintWarning($"Invalid config value format for {category}.{setting}. Using default value.");
                categoryData[setting] = defaultValue;
                return defaultValue;
            }
            catch (Exception ex)
            {
                PrintError($"An unexpected error occurred while reading config value {category}.{setting}: {ex.Message}. Using default value.");
                categoryData[setting] = defaultValue;
                return defaultValue;
            }
        }
        #endregion

        #region Data Management
        private void SaveProdData()
        {
            try
            {
                // Write the in-memory data object back to the file
                _dataFile.WriteObject(_pluginData);
                if (_debugMode) Puts("Prod_BuildingData saved.");
            }
            catch (Exception ex)
            {
                PrintError($"Failed to save Prod_BuildingData: {ex.Message}");
            }
        }

        private void SetProdData(string entityId, string category, Dictionary<string, object> data)
        {
            if (string.IsNullOrEmpty(entityId) || string.IsNullOrEmpty(category) || data == null)
                return;

            // Ensure the specific category dictionary exists within _pluginData.Categories
            if (!_pluginData.Categories.ContainsKey(category))
            {
                _pluginData.Categories[category] = new Dictionary<string, object>();
            }

            // Now, access the sub-dictionary for the specific category and set the entity's data
            _pluginData.Categories[category][entityId] = data;
            if (_debugMode) Puts($"Set data for {category}/{entityId}");
        }

        private Dictionary<string, object> GetProdData(string entityId, string category)
        {
            if (string.IsNullOrEmpty(entityId) || string.IsNullOrEmpty(category))
                return null;

            // Try to get the category dictionary
            if (_pluginData.Categories.TryGetValue(category, out var categoryDict))
            {
                // Try to get the specific entity data from that category dictionary
                if (categoryDict.TryGetValue(entityId, out var entityDataObj) && entityDataObj is Dictionary<string, object> entityData)
                {
                    return entityData;
                }
            }
            return null;
        }

        private bool RemoveProdData(string entityId, string category)
        {
            if (string.IsNullOrEmpty(entityId) || string.IsNullOrEmpty(category))
                return false;

            if (_pluginData.Categories.TryGetValue(category, out var categoryDict))
            {
                return categoryDict.Remove(entityId);
            }
            return false;
        }

        private string GetBuildingBlockOwnerFromData(BuildingBlock block)
        {
            var data = GetProdData(block.net.ID.ToString(), "BuildingBlocks");
            return data?["owner"] as string;
        }
        #endregion

        #region Command Handling
        private void ChatCommandProd(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;

            if (!HasAccess(player))
            {
                SendError(player, _noAccess);
                return;
            }

            if (_printToConsole)
            {
                SendNotice(player, _informationAdded);
            }

            var target = GetLookingAtEntity(player);
            if (target == null)
            {
                SendError(player, _noTargetFound);
                return;
            }

            HandleProdCommand(player, target);
        }

        private void HandleProdCommand(BasePlayer player, BaseEntity target)
        {
            var sb = new StringBuilder();

            AppendDivider(sb);
            AppendMainTitle(sb, _prodTitle);
            ShowGenericEntityInfo(player, target, sb);

            if (TryHandleSpecializedEntity(player, target, sb))
            {
                SendMessage(player, sb.ToString());
                return;
            }

            AppendMuted(sb, _noEntityInfo);
            AppendDivider(sb);
            SendMessage(player, sb.ToString());
        }

        #endregion

        #region Entity Information Display
        private void ShowGenericEntityInfo(BasePlayer player, BaseEntity target, StringBuilder sb)
        {
            AppendEntityType(sb, target.ShortPrefabName, target.net.ID);

            if (target.OwnerID != 0L)
                AppendOwner(sb, target.OwnerID);
            else
                AppendMuted(sb, _noGenericOwner);
        }

        #region Building Block
        private void ShowBuildingBlockInfo(BasePlayer player, BuildingBlock block, StringBuilder sb)
        {
            AppendSectionTitle(sb, _buildingGradeTitle);

            if (_showBuildingGrade)
            {
                var grade = block.grade.ToString();
                var currentHP = Mathf.RoundToInt(block.health);
                var maxHP = Mathf.RoundToInt(block.MaxHealth());
                AppendLabelValue(sb, "Grade", grade);
                AppendLabelValue(sb, "HP", $"{currentHP}/{maxHP}");
            }

            if (_showBuildingStability)
                AppendStability(sb, block);

            var ownerData = GetBuildingBlockOwnerFromData(block);
            if (string.IsNullOrEmpty(ownerData))
            {
                if (block.OwnerID != 0L)
                    AppendOwner(sb, block.OwnerID);
                else
                    AppendMuted(sb, _noBlockOwnerFound);
            }
            else if (ulong.TryParse(ownerData, out var ownerId))
            {
                AppendOwner(sb, ownerId);
            }
            else
            {
                AppendMuted(sb, _noBlockOwnerFound);
            }

            AppendDivider(sb);
        }

        #endregion

        #region Tool Cupboard  // TODO: Continue going through messages verifying everything and making sure the plugin actually works
        private void ShowCupboardInfo(BasePlayer player, BuildingPrivlidge cupboard, StringBuilder sb)
        {
            AppendSectionTitle(sb, _toolCupboardTitle);

            var pos = cupboard.transform.position;
            AppendLabelValue(sb, "Location", $"{pos.x:F1}, {pos.y:F1}, {pos.z:F1}");

            if (cupboard.authorizedPlayers.Count == 0)
            {
                AppendMuted(sb, _toolCupboardNoAuth);
            }
            else
            {
                AppendAuthHeader(sb, cupboard.authorizedPlayers.Count);
                foreach (var authorizedPlayer in cupboard.authorizedPlayers)
                    AppendPlayerEntry(sb, authorizedPlayer, indent: true);
            }

            if (cupboard.HasSlot(BaseEntity.Slot.Lock))
                ShowLockInfo(player, cupboard, sb);
            else
                AppendDivider(sb);
        }

        #endregion

        #region Sleeping Bags
        private void ShowSleepingBagInfo(BasePlayer player, SleepingBag bag, StringBuilder sb)
        {
            AppendSectionTitle(sb, _sleepingBagTitle);
            AppendLabelValue(sb, "Name", bag.niceName);
            AppendOwner(sb, bag.deployerUserID);
            AppendDivider(sb);
        }
        #endregion

        #region Storage
        private void ShowStorageContainerInfo(BasePlayer player, StorageContainer container, StringBuilder sb)
        {
            AppendSectionTitle(sb, _storageContainerTitle);
            AppendEntityType(sb, container.ShortPrefabName, container.net.ID);

            if (container.OwnerID != 0L)
                AppendOwner(sb, container.OwnerID);
            else
                AppendMuted(sb, _noContainerOwner);

            ShowLockInfo(player, container, sb);
            AppendDivider(sb);
        }
        #endregion

        #region AutoTurret
    
        private void ShowAutoTurretInfo(BasePlayer player, AutoTurret turret, StringBuilder sb)
        {
            AppendSectionTitle(sb, _autoturretTitle);
            AppendEntityType(sb, turret.ShortPrefabName, turret.net.ID);
            AppendLabelValue(sb, "Powered", turret.IsPowered() ? "Yes" : "No");

            if (turret.OwnerID != 0L)
                AppendOwner(sb, turret.OwnerID);
            else
                AppendMuted(sb, _noDeployableOwner);

            if (turret.authorizedPlayers.Count == 0)
            {
                AppendMuted(sb, _noTurretAuth);
                AppendDivider(sb);
                return;
            }

            AppendAuthHeader(sb, turret.authorizedPlayers.Count);
            foreach (var authorizedPlayer in turret.authorizedPlayers)
                AppendPlayerEntry(sb, authorizedPlayer);

            AppendDivider(sb);
        }
        #endregion

        #region Vehicle / Computer Station? 
        private void ShowVehicleInfo(BasePlayer player, BaseVehicle vehicle, StringBuilder sb)
        {
            AppendSectionTitle(sb, _vehicleTitle);
            AppendEntityType(sb, vehicle.ShortPrefabName, vehicle.net.ID);

            if (vehicle.OwnerID != 0L)
                AppendOwner(sb, vehicle.OwnerID);
            else
                AppendMuted(sb, _noVehicleOwner);

            ShowLockInfo(player, vehicle, sb);

            var computerStation = vehicle.GetComponent<ComputerStation>();
            if (computerStation != null)
            {
                AppendSectionTitle(sb, _computerStationTitle, indent: true);
                var bookmarks = computerStation.GenerateControlBookmarkString();
                if (!string.IsNullOrEmpty(bookmarks))
                    AppendLabelValue(sb, "Bookmarks", bookmarks, indent: true);
                AppendComputerStationAuth(sb, computerStation, indent: true);
            }

            AppendDivider(sb);
        }

        #endregion

        #region Electrical / IO Info
        private void ShowIOEntityInfo(BasePlayer player, IOEntity entity, StringBuilder sb)
        {
            AppendSectionTitle(sb, _electricalTitle);
            AppendEntityType(sb, entity.ShortPrefabName, entity.net.ID);
            AppendPoweredState(sb, entity);

            if (entity is SeismicSensor seismic)
                AppendLabelValue(sb, "Range", seismic.range.ToString("F0"));
            else if (entity is IndustrialConveyor conveyor)
            {
                AppendLabelValue(sb, "Mode", conveyor.mode.ToString());
                AppendLabelValue(sb, "Filters", conveyor.filterItems.Count.ToString());
            }
            else if (entity is ElectricalBranch branch)
                AppendLabelValue(sb, "Branch Power", branch.branchAmount.ToString("F0"));
            else if (entity is PowerCounter counter)
            {
                AppendLabelValue(sb, "Counter", counter.counterNumber.ToString());
                AppendLabelValue(sb, "Target", counter.GetTarget().ToString());
            }
            else if (entity is TimerSwitch timerSwitch)
                AppendLabelValue(sb, "Timer", $"{timerSwitch.timerLength:F0}s");

            if (entity is IRFObject rfObject)
            {
                var frequency = rfObject.GetFrequency();
                if (frequency > 0)
                    AppendLabelValue(sb, "Frequency", frequency.ToString());
            }

            if (entity.OwnerID != 0L)
                AppendOwner(sb, entity.OwnerID);
            else
                AppendMuted(sb, _noElectricalOwner);

            AppendDivider(sb);
        }
        #endregion

        #region Code Lock Info
        private void ShowCodeLockInfo(BasePlayer player, BaseEntity entity, StringBuilder sb)
        {
            var lockEntity = entity.GetSlot(BaseEntity.Slot.Lock);
            var codeLock = lockEntity?.GetComponent<CodeLock>();

            if (codeLock == null)
            {
                if (lockEntity?.GetComponent<KeyLock>() != null)
                    ShowKeyLockInfo(player, entity, sb);
                else
                {
                    AppendMuted(sb, _noCodelock);
                    AppendDivider(sb);
                }
                return;
            }

            AppendSectionTitle(sb, _codelockTitle);

            var code = GetReflectionValue<string>(codeLock, _codeNumFieldInfo);
            AppendLabelValue(sb, "Code", string.IsNullOrEmpty(code) ? "Unlocked" : code);

            var whitelist = GetReflectionValue<List<ulong>>(codeLock, _codeLockWhitelistFieldInfo);

            if (whitelist == null || whitelist.Count == 0)
            {
                AppendMuted(sb, _noCodeAccess);
                AppendDivider(sb);
                return;
            }

            AppendLabel(sb, $"Whitelist ({whitelist.Count})");
            foreach (var userId in whitelist)
                AppendPlayerEntry(sb, userId, indent: true);

            AppendDivider(sb);
        }
        #endregion

        #region KeyLock Info
        private void ShowKeyLockInfo(BasePlayer player, BaseEntity entity, StringBuilder sb)
        {
            var lockEntity = entity.GetSlot(BaseEntity.Slot.Lock);
            var keyLock = lockEntity?.GetComponent<KeyLock>();

            if (keyLock == null)
            {
                AppendMuted(sb, _noKeyLockFound);
                return;
            }

            AppendSectionTitle(sb, _keylockTitle);
            AppendEntityType(sb, "Key Lock", keyLock.net.ID);

            var placedBy = keyLock.OwnerID != 0 ? keyLock.OwnerID : entity.OwnerID;
            if (placedBy != 0)
                AppendPlacedBy(sb, placedBy);
            else
                AppendMuted(sb, _noKeyLockOwner);

            var keyCode = GetKeyLockCode(keyLock);
            AppendLabelValue(sb, "Key Code", keyCode > 0 ? keyCode.ToString() : "Unknown");
            AppendLabelValue(sb, "Locked", keyLock.IsLocked() ? "Yes" : "No");

            var accessPlayers = GetKeyLockAccessPlayers(keyLock, keyCode);
            if (accessPlayers.Count == 0)
            {
                AppendMuted(sb, _noKeyAccess);
            }
            else
            {
                AppendAuthHeader(sb, accessPlayers.Count);
                foreach (var userId in accessPlayers)
                    AppendPlayerEntry(sb, userId, indent: true);
            }

            AppendDivider(sb);
        }

        private int GetKeyLockCode(KeyLock keyLock)
        {
            if (keyLock == null)
                return 0;

            if (keyLock.keyCode > 0)
                return keyLock.keyCode;

            return GetReflectionValue<int>(keyLock, _keyLockKeyCodeFieldInfo);
        }

        private List<ulong> GetKeyLockAccessPlayers(KeyLock keyLock, int keyCode)
        {
            var accessPlayers = new List<ulong>();
            var seen = new HashSet<ulong>();

            void TryAdd(BasePlayer basePlayer)
            {
                if (basePlayer == null || basePlayer.userID == 0 || !seen.Add(basePlayer.userID))
                    return;

                if (keyLock.HasLockPermission(basePlayer) || PlayerCarriesMatchingKey(basePlayer, keyCode))
                    accessPlayers.Add(basePlayer.userID);
            }

            foreach (var basePlayer in BasePlayer.activePlayerList)
                TryAdd(basePlayer);

            foreach (var basePlayer in BasePlayer.sleepingPlayerList)
                TryAdd(basePlayer);

            return accessPlayers;
        }

        private bool PlayerCarriesMatchingKey(BasePlayer basePlayer, int keyCode)
        {
            if (basePlayer?.inventory == null || keyCode <= 0)
                return false;

            return ContainerHasMatchingKey(basePlayer.inventory.containerMain, keyCode)
                || ContainerHasMatchingKey(basePlayer.inventory.containerBelt, keyCode)
                || ContainerHasMatchingKey(basePlayer.inventory.containerWear, keyCode);
        }

        private static bool ContainerHasMatchingKey(ItemContainer container, int keyCode)
        {
            if (container?.itemList == null)
                return false;

            foreach (var item in container.itemList)
            {
                if (item?.info == null || item.info.shortname != "door.key")
                    continue;

                if (item.instanceData != null && item.instanceData.dataInt == keyCode)
                    return true;
            }

            return false;
        }
        #endregion

        #endregion

        
        private bool TryHandleSpecializedEntity(BasePlayer player, BaseEntity target, StringBuilder sb)
        {
            var buildingBlock = target.GetComponent<BuildingBlock>() ?? target.GetComponentInParent<BuildingBlock>();
            if (buildingBlock != null)
            {
                ShowBuildingBlockInfo(player, buildingBlock, sb);
                return true;
            }

            var cupboard = target.GetComponent<BuildingPrivlidge>() ?? target.GetComponentInParent<BuildingPrivlidge>();
            if (cupboard != null)
            {
                ShowCupboardInfo(player, cupboard, sb);
                return true;
            }

            var door = target.GetComponent<Door>() ?? target.GetComponentInParent<Door>();
            if (door != null)
            {
                ShowDoorInfo(player, door, sb);
                return true;
            }

            var sleepingBag = target.GetComponent<SleepingBag>() ?? target.GetComponentInParent<SleepingBag>();
            if (sleepingBag != null)
            {
                ShowSleepingBagInfo(player, sleepingBag, sb);
                return true;
            }

            var vendingMachine = target.GetComponent<VendingMachine>() ?? target.GetComponentInParent<VendingMachine>();
            if (vendingMachine != null)
            {
                ShowVendingMachineInfo(player, vendingMachine, sb);
                return true;
            }

            var samSite = target.GetComponent<SamSite>() ?? target.GetComponentInParent<SamSite>();
            if (samSite != null)
            {
                ShowSamSiteInfo(player, samSite, sb);
                return true;
            }

            var autoTurret = target.GetComponent<AutoTurret>() ?? target.GetComponentInParent<AutoTurret>();
            if (autoTurret != null)
            {
                ShowAutoTurretInfo(player, autoTurret, sb);
                return true;
            }

            var flameTurret = target.GetComponent<FlameTurret>() ?? target.GetComponentInParent<FlameTurret>();
            if (flameTurret != null)
            {
                ShowFlameTurretInfo(player, flameTurret, sb);
                return true;
            }

            var gunTrap = target.GetComponent<GunTrap>() ?? target.GetComponentInParent<GunTrap>();
            if (gunTrap != null)
            {
                ShowGunTrapInfo(player, gunTrap, sb);
                return true;
            }

            if (target is BearTrap bearTrap)
            {
                ShowTrapInfo(player, bearTrap, sb, "Bear Trap");
                return true;
            }

            if (target is Landmine landmine)
            {
                ShowTrapInfo(player, landmine, sb, "Landmine");
                return true;
            }

            var computerStation = target.GetComponent<ComputerStation>() ?? target.GetComponentInParent<ComputerStation>();
            if (computerStation != null && !(target.GetComponent<BaseVehicle>() ?? target.GetComponentInParent<BaseVehicle>()))
            {
                ShowComputerStationInfo(player, computerStation, sb);
                return true;
            }

            var cctv = target.GetComponent<CCTV_RC>() ?? target.GetComponentInParent<CCTV_RC>();
            if (cctv != null)
            {
                ShowCctvInfo(player, cctv, sb);
                return true;
            }

            var elevator = target.GetComponent<Elevator>() ?? target.GetComponentInParent<Elevator>();
            if (elevator != null)
            {
                ShowElevatorInfo(player, elevator, sb);
                return true;
            }

            var chickenCoop = target.GetComponent<ChickenCoop>() ?? target.GetComponentInParent<ChickenCoop>();
            if (chickenCoop != null)
            {
                ShowChickenCoopInfo(player, chickenCoop, sb);
                return true;
            }

            if (target is FarmableAnimal farmableAnimal)
            {
                ShowFarmableAnimalInfo(player, farmableAnimal, sb);
                return true;
            }

            var mixingTable = target.GetComponent<MixingTable>() ?? target.GetComponentInParent<MixingTable>();
            if (mixingTable != null)
            {
                ShowMixingTableInfo(player, mixingTable, sb);
                return true;
            }

            var baseOven = target.GetComponent<BaseOven>() ?? target.GetComponentInParent<BaseOven>();
            if (baseOven != null)
            {
                ShowBaseOvenInfo(player, baseOven, sb);
                return true;
            }

            var weaponRack = target.GetComponent<WeaponRack>() ?? target.GetComponentInParent<WeaponRack>();
            if (weaponRack != null)
            {
                ShowWeaponRackInfo(player, weaponRack, sb);
                return true;
            }

            if (target is ConstructableEntity constructable)
            {
                ShowConstructableInfo(player, constructable, sb);
                return true;
            }

            if (target is BaseSiegeWeapon siegeWeapon)
            {
                ShowSiegeWeaponInfo(player, siegeWeapon, sb);
                return true;
            }

            var industrialCrafter = target.GetComponent<IndustrialCrafter>() ?? target.GetComponentInParent<IndustrialCrafter>();
            if (industrialCrafter != null)
            {
                ShowIndustrialCrafterInfo(player, industrialCrafter, sb);
                return true;
            }

            var hbhfSensor = target.GetComponent<HBHFSensor>() ?? target.GetComponentInParent<HBHFSensor>();
            if (hbhfSensor != null)
            {
                ShowHbhSensorInfo(player, hbhfSensor, sb);
                return true;
            }

            var fogMachine = target.GetComponent<FogMachine>() ?? target.GetComponentInParent<FogMachine>();
            if (fogMachine != null)
            {
                ShowFogMachineInfo(player, fogMachine, sb);
                return true;
            }

            var electricBattery = target.GetComponent<ElectricBattery>() ?? target.GetComponentInParent<ElectricBattery>();
            if (electricBattery != null)
            {
                ShowElectricBatteryInfo(player, electricBattery, sb);
                return true;
            }

            var teslaCoil = target.GetComponent<TeslaCoil>() ?? target.GetComponentInParent<TeslaCoil>();
            if (teslaCoil != null)
            {
                ShowTeslaCoilInfo(player, teslaCoil, sb);
                return true;
            }

            var boomBox = target.GetComponent<BoomBox>() ?? target.GetComponentInParent<BoomBox>();
            if (boomBox != null)
            {
                ShowBoomBoxInfo(player, target, boomBox, sb);
                return true;
            }

            var phoneController = target.GetComponent<PhoneController>() ?? target.GetComponentInParent<PhoneController>();
            if (phoneController != null)
            {
                ShowPhoneInfo(player, target, phoneController, sb);
                return true;
            }

            var baseVehicle = target.GetComponent<BaseVehicle>() ?? target.GetComponentInParent<BaseVehicle>();
            if (baseVehicle != null)
            {
                ShowVehicleInfo(player, baseVehicle, sb);
                return true;
            }

            var storageContainer = target.GetComponent<StorageContainer>() ?? target.GetComponentInParent<StorageContainer>();
            if (storageContainer != null)
            {
                ShowStorageContainerInfo(player, storageContainer, sb);
                return true;
            }

            var ioEntity = target.GetComponent<IOEntity>() ?? target.GetComponentInParent<IOEntity>();
            if (ioEntity != null)
            {
                ShowIOEntityInfo(player, ioEntity, sb);
                return true;
            }

            return false;
        }

        private void ShowDoorInfo(BasePlayer player, Door door, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Door");
            AppendEntityType(sb, door.ShortPrefabName, door.net.ID);
            AppendOwnerOrMuted(sb, door.OwnerID, _noDeployableOwner);
            AppendLabelValue(sb, "Open", door.IsOpen() ? "Yes" : "No");
            ShowLockInfo(player, door, sb);
            if (!door.HasSlot(BaseEntity.Slot.Lock))
                AppendDivider(sb);
        }

        private void ShowVendingMachineInfo(BasePlayer player, VendingMachine vm, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Vending Machine");
            AppendEntityType(sb, vm.ShortPrefabName, vm.net.ID);
            AppendOwnerOrMuted(sb, vm.OwnerID, _noDeployableOwner);
            AppendPoweredState(sb, vm);

            if (!string.IsNullOrEmpty(vm.shopName))
                AppendLabelValue(sb, "Shop Name", vm.shopName);

            AppendLabelValue(sb, "Broadcasting", vm.IsBroadcasting() ? "Yes" : "No");
            AppendLabelValue(sb, "Sell Orders", vm.sellOrders.sellOrders.Count.ToString());
            AppendDivider(sb);
        }

        private void ShowSamSiteInfo(BasePlayer player, SamSite samSite, StringBuilder sb)
        {
            AppendSectionTitle(sb, "SAM Site");
            AppendEntityType(sb, samSite.ShortPrefabName, samSite.net.ID);
            AppendOwnerOrMuted(sb, samSite.OwnerID, _noDeployableOwner);
            AppendPoweredState(sb, samSite);
            AppendLabelValue(sb, "Vehicle Range", $"{samSite.vehicleScanRadius:F0}m");
            AppendLabelValue(sb, "Missile Range", $"{samSite.missileScanRadius:F0}m");

            var ammoAmount = samSite.ammoItem?.amount ?? 0;
            AppendLabelValue(sb, "Ammo", ammoAmount > 0 ? $"{ammoAmount}x {samSite.ammoType?.displayName.english ?? "missiles"}" : "Empty");
            AppendDivider(sb);
        }

        private void ShowFlameTurretInfo(BasePlayer player, FlameTurret turret, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Flame Turret");
            AppendEntityType(sb, turret.ShortPrefabName, turret.net.ID);
            AppendOwnerOrMuted(sb, turret.OwnerID, _noDeployableOwner);
            AppendInventoryFuel(sb, turret);
            AppendDivider(sb);
        }

        private void ShowGunTrapInfo(BasePlayer player, GunTrap trap, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Shotgun Trap");
            AppendEntityType(sb, trap.ShortPrefabName, trap.net.ID);
            AppendOwnerOrMuted(sb, trap.OwnerID, _noDeployableOwner);

            var ammo = trap.inventory?.GetSlot(0);
            if (ammo == null)
                AppendLabelValue(sb, "Ammo", "Empty");
            else
                AppendLabelValue(sb, "Ammo", $"{ammo.amount}x {ammo.info?.displayName.english ?? trap.ammoType?.displayName.english ?? "shells"}");

            AppendDivider(sb);
        }

        private void ShowTrapInfo(BasePlayer player, BaseEntity trap, StringBuilder sb, string title)
        {
            AppendSectionTitle(sb, title);
            AppendEntityType(sb, trap.ShortPrefabName, trap.net.ID);
            AppendOwnerOrMuted(sb, trap.OwnerID, _noDeployableOwner);
            AppendLabelValue(sb, "HP", $"{Mathf.RoundToInt(trap.Health())}/{Mathf.RoundToInt(trap.MaxHealth())}");
            AppendDivider(sb);
        }

        private void ShowComputerStationInfo(BasePlayer player, ComputerStation station, StringBuilder sb)
        {
            AppendSectionTitle(sb, _computerStationTitle);
            AppendEntityType(sb, station.ShortPrefabName, station.net.ID);
            AppendOwnerOrMuted(sb, station.OwnerID, _noDeployableOwner);

            var bookmarks = station.GenerateControlBookmarkString();
            if (!string.IsNullOrEmpty(bookmarks))
                AppendLabelValue(sb, "Bookmarks", bookmarks);

            AppendComputerStationAuth(sb, station);
            AppendDivider(sb);
        }

        private void ShowCctvInfo(BasePlayer player, CCTV_RC cctv, StringBuilder sb)
        {
            AppendSectionTitle(sb, "CCTV");
            AppendEntityType(sb, cctv.ShortPrefabName, cctv.net.ID);
            AppendOwnerOrMuted(sb, cctv.OwnerID, _noElectricalOwner);
            AppendPoweredState(sb, cctv);

            if (!string.IsNullOrEmpty(cctv.rcIdentifier))
                AppendLabelValue(sb, "Identifier", cctv.rcIdentifier);

            AppendLabelValue(sb, "Viewers", cctv.ViewerCount.ToString());
            AppendLabelValue(sb, "Static", cctv.IsStatic() ? "Yes" : "No");
            AppendLabelValue(sb, "Yaw/Pitch", $"{cctv.yawAmount:F0}/{cctv.pitchAmount:F0}");
            AppendDivider(sb);
        }

        private void ShowElevatorInfo(BasePlayer player, Elevator elevator, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Elevator");
            AppendEntityType(sb, elevator.ShortPrefabName, elevator.net.ID);
            AppendOwnerOrMuted(sb, elevator.OwnerID, _noDeployableOwner);
            AppendLabelValue(sb, "Floor", elevator.Floor.ToString());
            AppendLabelValue(sb, "Powered", elevator.HasFlag(Elevator.Flag_HasPower) ? "Yes" : "No");
            AppendLabelValue(sb, "Busy", elevator.HasFlag(BaseEntity.Flags.Busy) ? "Yes" : "No");
            AppendDivider(sb);
        }

        private void ShowChickenCoopInfo(BasePlayer player, ChickenCoop coop, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Chicken Coop");
            AppendEntityType(sb, coop.ShortPrefabName, coop.net.ID);
            AppendOwnerOrMuted(sb, coop.OwnerID, _noDeployableOwner);
            AppendLabelValue(sb, "Animals", coop.Animals.Count.ToString());
            AppendLabelValue(sb, "Incubating", coop.HasFlag(BaseEntity.Flags.Reserved1) ? "Yes" : "No");

            var hatching = coop.Animals.Count(a => a.TimeUntilHatch > 0f);
            if (hatching > 0)
                AppendLabelValue(sb, "Hatching", hatching.ToString());

            AppendDivider(sb);
        }

        private void ShowFarmableAnimalInfo(BasePlayer player, FarmableAnimal animal, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Farm Animal");
            AppendEntityType(sb, animal.ShortPrefabName, animal.net.ID);
            AppendOwnerOrMuted(sb, animal.OwnerID, _noDeployableOwner);

            if (!string.IsNullOrEmpty(animal.AnimalName))
                AppendLabelValue(sb, "Name", animal.AnimalName);

            AppendLabelValue(sb, "Hunger", $"{animal.AnimalHunger:F0}%");
            AppendLabelValue(sb, "Thirst", $"{animal.AnimalThirst:F0}%");
            AppendLabelValue(sb, "Love", $"{animal.AnimalLove:F0}%");
            AppendLabelValue(sb, "Sunlight", $"{animal.AnimalSunlight:F0}%");
            AppendDivider(sb);
        }

        private void ShowMixingTableInfo(BasePlayer player, MixingTable table, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Mixing Table");
            AppendEntityType(sb, table.ShortPrefabName, table.net.ID);
            AppendOwnerOrMuted(sb, table.OwnerID, _noContainerOwner);
            AppendLabelValue(sb, "Active", table.IsOn() ? "Yes" : "No");

            if (table.IsOn())
            {
                AppendLabelValue(sb, "Quantity", table.currentQuantity.ToString());
                AppendLabelValue(sb, "Time Left", $"{table.RemainingMixTime:F0}s");
            }

            ShowLockInfo(player, table, sb);
            if (!table.HasSlot(BaseEntity.Slot.Lock))
                AppendDivider(sb);
        }

        private void ShowBaseOvenInfo(BasePlayer player, BaseOven oven, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Oven / Furnace");
            AppendEntityType(sb, oven.ShortPrefabName, oven.net.ID);
            AppendOwnerOrMuted(sb, oven.OwnerID, _noContainerOwner);
            AppendLabelValue(sb, "Cooking", oven.IsOn() ? "Yes" : "No");
            ShowLockInfo(player, oven, sb);
            if (!oven.HasSlot(BaseEntity.Slot.Lock))
                AppendDivider(sb);
        }

        private void ShowWeaponRackInfo(BasePlayer player, WeaponRack rack, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Weapon Rack");
            AppendEntityType(sb, rack.ShortPrefabName, rack.net.ID);
            AppendOwnerOrMuted(sb, rack.OwnerID, _noContainerOwner);

            var mounted = rack.gridSlots?.Count(s => s != null && s.Used) ?? 0;
            AppendLabelValue(sb, "Mounted Weapons", mounted.ToString());
            AppendDivider(sb);
        }

        private void ShowConstructableInfo(BasePlayer player, ConstructableEntity constructable, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Siege Constructable");
            AppendEntityType(sb, constructable.ShortPrefabName, constructable.net.ID);
            AppendOwnerOrMuted(sb, constructable.OwnerID, _noDeployableOwner);
            AppendLabelValue(sb, "HP", $"{Mathf.RoundToInt(constructable.Health())}/{Mathf.RoundToInt(constructable.MaxHealth())}");
            AppendDivider(sb);
        }

        private void ShowSiegeWeaponInfo(BasePlayer player, BaseSiegeWeapon weapon, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Siege Weapon");
            AppendEntityType(sb, weapon.ShortPrefabName, weapon.net.ID);
            AppendOwnerOrMuted(sb, weapon.OwnerID, _noDeployableOwner);
            AppendLabelValue(sb, "HP", $"{Mathf.RoundToInt(weapon.Health())}/{Mathf.RoundToInt(weapon.MaxHealth())}");
            AppendDivider(sb);
        }

        private void ShowIndustrialCrafterInfo(BasePlayer player, IndustrialCrafter crafter, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Industrial Crafter");
            AppendEntityType(sb, crafter.ShortPrefabName, crafter.net.ID);
            AppendOwnerOrMuted(sb, crafter.OwnerID, _noElectricalOwner);
            AppendPoweredState(sb, crafter);
            AppendLabelValue(sb, "Crafting", crafter.HasFlag(IndustrialCrafter.Crafting) ? "Yes" : "No");
            AppendDivider(sb);
        }

        private void ShowHbhSensorInfo(BasePlayer player, HBHFSensor sensor, StringBuilder sb)
        {
            AppendSectionTitle(sb, "HBHF Sensor");
            AppendEntityType(sb, sensor.ShortPrefabName, sensor.net.ID);
            AppendOwnerOrMuted(sb, sensor.OwnerID, _noElectricalOwner);
            AppendPoweredState(sb, sensor);
            AppendLabelValue(sb, "Include Authed", sensor.HasFlag(HBHFSensor.Flag_IncludeAuthed) ? "Yes" : "No");
            AppendLabelValue(sb, "Include Others", sensor.HasFlag(HBHFSensor.Flag_IncludeOthers) ? "Yes" : "No");
            AppendLabelValue(sb, "Connected", sensor.HasConnections() ? "Yes" : "No");
            AppendDivider(sb);
        }

        private void ShowFogMachineInfo(BasePlayer player, FogMachine machine, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Fog Machine");
            AppendEntityType(sb, machine.ShortPrefabName, machine.net.ID);
            AppendOwnerOrMuted(sb, machine.OwnerID, _noDeployableOwner);
            AppendPoweredState(sb, machine);

            var fuel = machine.inventory?.GetSlot(0);
            if (fuel == null)
                AppendLabelValue(sb, "Fuel", "Empty");
            else
                AppendLabelValue(sb, "Fuel", $"{fuel.amount}x {fuel.info?.displayName.english ?? "low grade fuel"}");

            AppendDivider(sb);
        }

        private void ShowElectricBatteryInfo(BasePlayer player, ElectricBattery battery, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Electric Battery");
            AppendEntityType(sb, battery.ShortPrefabName, battery.net.ID);
            AppendOwnerOrMuted(sb, battery.OwnerID, _noElectricalOwner);
            AppendPoweredState(sb, battery);

            if (battery.maxCapactiySeconds > 0f)
            {
                var chargePercent = Mathf.Clamp(Mathf.RoundToInt((battery.rustWattSeconds / battery.maxCapactiySeconds) * 100f), 0, 100);
                AppendLabelValue(sb, "Charge", $"{chargePercent}%");
            }

            AppendDivider(sb);
        }

        private void ShowTeslaCoilInfo(BasePlayer player, TeslaCoil coil, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Tesla Coil");
            AppendEntityType(sb, coil.ShortPrefabName, coil.net.ID);
            AppendOwnerOrMuted(sb, coil.OwnerID, _noElectricalOwner);
            AppendPoweredState(sb, coil);
            AppendDivider(sb);
        }

        private void ShowBoomBoxInfo(BasePlayer player, BaseEntity entity, BoomBox boomBox, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Boom Box");
            AppendEntityType(sb, entity.ShortPrefabName, entity.net.ID);
            AppendOwnerOrMuted(sb, entity.OwnerID, _noDeployableOwner);
            AppendLabelValue(sb, "Playing", boomBox.IsOn() ? "Yes" : "No");

            if (!string.IsNullOrEmpty(boomBox.CurrentRadioIp))
                AppendLabelValue(sb, "Station", boomBox.CurrentRadioIp);

            if (boomBox.AssignedRadioBy != 0)
                AppendPlacedBy(sb, boomBox.AssignedRadioBy);

            AppendLabelValue(sb, "Cassette", boomBox.HasFlag(BoomBox.HasCassette) ? "Yes" : "No");
            AppendDivider(sb);
        }

        private void ShowPhoneInfo(BasePlayer player, BaseEntity entity, PhoneController phone, StringBuilder sb)
        {
            AppendSectionTitle(sb, "Telephone");
            AppendEntityType(sb, entity.ShortPrefabName, entity.net.ID);
            AppendOwnerOrMuted(sb, entity.OwnerID, _noDeployableOwner);
            AppendLabelValue(sb, "Voicemail", phone.savedVoicemail?.Count.ToString() ?? "0");
            AppendDivider(sb);
        }
    
        #region Message Formatting
        private static string Colored(string color, string text) => $"<color={color}>{text}</color>";

        private static string FormatLabel(string label) => Colored(ColorLabel, label);

        private static string FormatLabelValue(string label, string value) =>
            $"{FormatLabel(label)}: {Colored(ColorValue, value)}";

        private static string FormatMainTitle(string title) =>
            Colored(ColorPrimary, $"---- {title} ----");

        private static string FormatSectionTitle(string title) =>
            Colored(ColorPrimary, $"--- {title} ---");

        private static string FormatDivider() =>
            Colored(ColorMuted, "---------------------------------------------------");

        private string FormatPlayerEntry(ulong userId) =>
            $"{Colored(ColorValue, GetPlayerName(userId))} {Colored(ColorMuted, $"({userId})")}";

        private string FormatAuthHeader(int count) =>
            FormatLabel(string.Format(_authorization, count));

        private void AppendLine(StringBuilder sb, string message, bool indent = false)
        {
            sb.AppendLine(indent ? $"  {message}" : message);
        }

        private void AppendDivider(StringBuilder sb) => AppendLine(sb, FormatDivider());

        private void AppendMainTitle(StringBuilder sb, string title) =>
            AppendLine(sb, FormatMainTitle(title));

        private void AppendSectionTitle(StringBuilder sb, string title, bool indent = false) =>
            AppendLine(sb, FormatSectionTitle(title), indent);

        private void AppendLabel(StringBuilder sb, string label, bool indent = false) =>
            AppendLine(sb, FormatLabel(label), indent);

        private void AppendLabelValue(StringBuilder sb, string label, string value, bool indent = false) =>
            AppendLine(sb, FormatLabelValue(label, value), indent);

        private void AppendEntityType(StringBuilder sb, string prefabName, NetworkableId id, bool indent = false) =>
            AppendLabelValue(sb, "Type", $"{prefabName} ({id})", indent);

        private void AppendOwner(StringBuilder sb, ulong ownerId, bool indent = false) =>
            AppendLine(sb, $"{FormatLabel("Owner")}: {FormatPlayerEntry(ownerId)}", indent);

        private void AppendPlacedBy(StringBuilder sb, ulong ownerId, bool indent = false) =>
            AppendLine(sb, $"{FormatLabel("Placed by")}: {FormatPlayerEntry(ownerId)}", indent);

        private void AppendPlayerEntry(StringBuilder sb, ulong userId, bool indent = false) =>
            AppendLine(sb, FormatPlayerEntry(userId), indent);

        private void AppendAuthHeader(StringBuilder sb, int count, bool indent = false) =>
            AppendLine(sb, FormatAuthHeader(count), indent);

        private void AppendMuted(StringBuilder sb, string message, bool indent = false) =>
            AppendLine(sb, Colored(ColorMuted, message), indent);

        private void AppendPoweredState(StringBuilder sb, IOEntity entity, bool indent = false) =>
            AppendLabelValue(sb, "Powered", entity.IsPowered() ? "Yes" : "No", indent);

        private void AppendOwnerOrMuted(StringBuilder sb, ulong ownerId, string noOwnerMessage, bool indent = false)
        {
            if (ownerId != 0L)
                AppendOwner(sb, ownerId, indent);
            else
                AppendMuted(sb, noOwnerMessage, indent);
        }

        private void AppendInventoryFuel(StringBuilder sb, StorageContainer container, string label = "Fuel")
        {
            var slot = container?.inventory?.GetSlot(0);
            if (slot == null)
            {
                AppendLabelValue(sb, label, "Empty");
                return;
            }

            AppendLabelValue(sb, label, $"{slot.amount}x {slot.info?.displayName.english ?? slot.info?.shortname ?? "unknown"}");
        }

        private void ShowLockInfo(BasePlayer player, BaseEntity entity, StringBuilder sb)
        {
            if (!entity.HasSlot(BaseEntity.Slot.Lock))
                return;

            var lockEntity = entity.GetSlot(BaseEntity.Slot.Lock);
            if (lockEntity == null)
                return;

            if (lockEntity.GetComponent<CodeLock>() != null)
                ShowCodeLockInfo(player, entity, sb);
            else if (lockEntity.GetComponent<KeyLock>() != null)
                ShowKeyLockInfo(player, entity, sb);
        }

        private List<ulong> GetComputerStationAuthorized(ComputerStation station)
        {
            var authorizedPlayersField = typeof(ComputerStation).GetField("authorizedPlayers",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (authorizedPlayersField == null)
                return null;

            var authValue = authorizedPlayersField.GetValue(station);
            var authIds = authValue as IEnumerable<ulong>
                ?? (authValue as System.Collections.IEnumerable)?.Cast<object>()
                    .Select(o => o is ulong u ? u : (o?.GetType().GetField("userid")?.GetValue(o) as ulong? ?? 0UL))
                    .Where(id => id != 0UL);

            return authIds?.ToList();
        }

        private void AppendComputerStationAuth(StringBuilder sb, ComputerStation station, bool indent = false)
        {
            var authorizedPlayers = GetComputerStationAuthorized(station);
            if (authorizedPlayers == null)
            {
                AppendMuted(sb, _noComputerStationAuthError, indent);
                return;
            }

            if (authorizedPlayers.Count == 0)
            {
                AppendMuted(sb, _noComputerStationAuth, indent);
                return;
            }

            AppendAuthHeader(sb, authorizedPlayers.Count, indent);
            foreach (var authPlayer in authorizedPlayers)
                AppendPlayerEntry(sb, authPlayer, indent);
        }

        private string FormatStabilityValue(BuildingBlock block)
        {
            if (block.grounded)
                return Colored(ColorMuted, _stabilityGrounded);

            block.UpdateStability();
            var percent = Mathf.Clamp(Mathf.RoundToInt(block.cachedStability * 100f), 0, 100);
            var color = percent >= 80 ? ColorPrimary : percent >= 50 ? ColorLabel : ColorError;
            return Colored(color, $"{percent}%");
        }

        private void AppendStability(StringBuilder sb, BuildingBlock block, bool indent = false) =>
            AppendLine(sb, $"{FormatLabel("Stability")}: {FormatStabilityValue(block)}", indent);

        private void SendMessage(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message))
                return;

            if (_printToConsole)
                player.ConsoleMessage(message);
            else
                player.ChatMessage(message);
        }

        private void SendError(BasePlayer player, string message) =>
            SendMessage(player, Colored(ColorError, message));

        private void SendNotice(BasePlayer player, string message) =>
            SendMessage(player, Colored(ColorMuted, message));
        #endregion

        #region Utility Methods
        private bool HasAccess(BasePlayer player)
        {
            return player?.net?.connection?.authLevel >= _prodAuth || player.IPlayer.HasPermission(_prodPermission);
        }

        private BaseEntity GetLookingAtEntity(BasePlayer player)
        {
            if (player?.eyes == null)
                return null;

            var ray = player.eyes.HeadRay();

            if (Physics.Raycast(ray, out RaycastHit hit, _maxRayDistance))
            {
                return hit.GetEntity();
            }

            return null;
        }

        private void CachePlayerName(ulong userId, string name)
        {
            if (userId == 0 || string.IsNullOrEmpty(name))
                return;

            _playerNameCache[userId] = name;
        }

        private string GetPlayerName(ulong userId)
        {
            if (userId == 0) return "Server"; // Handle server as owner

            // Check cache first for performance
            if (_playerNameCache.TryGetValue(userId, out var cachedName))
            {
                var onlinePlayer = BasePlayer.FindByID(userId);
                if (onlinePlayer != null)
                {
                    return $"{cachedName}";
                }

                var sleepingPlayer = BasePlayer.FindSleeping(userId);
                if (sleepingPlayer != null)
                {
                    return $"{cachedName} (Sleeping)";
                }
                return $"{cachedName} (Offline)";
            }

            // Try online player first
            var playerOnline = BasePlayer.FindByID(userId);
            if (playerOnline != null)
            {
                CachePlayerName(userId, playerOnline.displayName);
                return $"{playerOnline.displayName} (Online)";
            }

            // Try sleeping player
            var playerSleeping = BasePlayer.FindSleeping(userId);
            if (playerSleeping != null)
            {
                CachePlayerName(userId, playerSleeping.displayName);
                return $"{playerSleeping.displayName} (Sleeping)";
            }

            // Try Covalence for offline players and cache
            var iPlayer = covalence.Players.FindPlayer(userId.ToString());
            if (iPlayer != null)
            {
                CachePlayerName(userId, iPlayer.Name);
                return $"{iPlayer.Name} (Offline)";
            }

            return $"Unknown Player ({userId})";
        }

        private T GetReflectionValue<T>(object instance, FieldInfo field)
        {
            if (field == null || instance == null)
            {
                if (_debugMode) PrintWarning($"Reflection: Field or instance is null. Field: {field?.Name ?? "N/A"}, Instance: {instance?.GetType().Name ?? "N/A"}");
                return default;
            }

            try
            {
                var value = field.GetValue(instance);
                if (value is T typedValue)
                {
                    return typedValue;
                }
                if (_debugMode) PrintWarning($"Reflection: Value of field '{field.Name}' is not of type {typeof(T).Name}. Actual type: {value?.GetType().Name ?? "null"}.");
                return default;
            }
            catch (Exception ex)
            {
                PrintError($"Failed to get reflection value for field '{field.Name}': {ex.Message}");
                return default;
            }
        }
        #endregion
    }
}
