using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace VenneChecker
{
    public class ModInfo
    {
        public string Name;
        public bool IsCheat;
    }

    public class PlayerInfo
    {
        public string PlayerName = "Unknown";
        public string Platform = "Unknown";
        public float FPS = -1f;
        public string JoinTime = "N/A";
        public bool IsLocal;
        public int ActorNumber;
        public List<ModInfo> DetectedMods = new List<ModInfo>();
        public List<string> BehaviorFlags = new List<string>();
    }

    public partial class PlayerScanner : MonoBehaviour
    {
        public static PlayerScanner Instance { get; private set; }
        public PlayerInfo LastScanResult { get; private set; }

        // Cache account creation dates so we don't re-query PlayFab
        private static readonly Dictionary<string, DateTime> _accountDates = new Dictionary<string, DateTime>();
        private static readonly HashSet<string> _pendingQueries = new HashSet<string>();

        private float _scanCooldown;

        private void Awake()
        {
            Instance = this;
            Log.Info("PlayerScanner.Awake() - Instance set");
        }

        private void Update()
        {
            if (_scanCooldown > 0f)
                _scanCooldown -= Time.deltaTime;
        }

        public PlayerInfo ScanPlayer(VRRig rig)
        {
            if (rig == null || _scanCooldown > 0f)
                return null;

            _scanCooldown = 1f;

            var info = new PlayerInfo();

            try
            {
                GetPlayerDataFromRig(rig, info);
            }
            catch (Exception ex)
            {
                Log.Error($"GetPlayerDataFromRig failed: {ex}");
                info.PlayerName = rig.gameObject.name;
            }

            // FPS — VRRig has a public int fps field
            try
            {
                GetFPS(rig, info);
            }
            catch { }

            // Mods — detected from Photon CustomProperties (network-synced)
            try
            {
                info.DetectedMods = DetectRemoteMods(rig, info);
            }
            catch (Exception ex)
            {
                Log.Warn($"Mod detection failed: {ex.Message}");
                // Fallback to local scan
                try { info.DetectedMods = ScanLocalMods(); } catch { }
            }

            // Cosmetics — show what they're wearing
            try
            {
                List<string> cosmetics = CosmeticChecker.GetEquippedCosmetics(rig);
                foreach (string c in cosmetics)
                    info.DetectedMods.Add(new ModInfo { Name = c, IsCheat = false });
            }
            catch (Exception ex) { Log.Warn($"Cosmetic check failed: {ex.Message}"); }

            // Harmony / assembly / BepInEx level detections (global, not per-player)
            try
            {
                List<string> harmonyResults = HarmonyDetector.Scan();
                foreach (string h in harmonyResults)
                    info.DetectedMods.Add(new ModInfo { Name = h, IsCheat = true });
            }
            catch (Exception ex) { Log.Warn($"Harmony detection failed: {ex.Message}"); }

            // Behavior flags — movement, RPC, low FPS, tag distance, color, scale, etc.
            try
            {
                GatherBehaviorFlags(rig, info);
            }
            catch (Exception ex) { Log.Warn($"Behavior flags failed: {ex.Message}"); }

            LastScanResult = info;

            try { SoundManager.Instance?.PlayScanComplete(); } catch { }

            Log.Info($"Scan: {info.PlayerName} | {info.Platform} | FPS:{info.FPS:F0} | Join:{info.JoinTime} | Mods:{info.DetectedMods.Count} | Flags:{info.BehaviorFlags.Count}");

            return info;
        }

        private void GetPlayerDataFromRig(VRRig rig, PlayerInfo info)
        {
            Type rigType = rig.GetType();

            // --- Get Creator (NetPlayer) ---
            object creator = GetCreator(rig, rigType);

            string userId = null;

            if (creator != null)
            {
                Type ct = creator.GetType();

                // NickName
                try
                {
                    var p = ct.GetProperty("NickName");
                    if (p != null) info.PlayerName = p.GetValue(creator, null)?.ToString() ?? "Unknown";
                }
                catch { }

                // IsLocal
                try
                {
                    var p = ct.GetProperty("IsLocal");
                    if (p != null && p.GetValue(creator, null) is bool b) info.IsLocal = b;
                }
                catch { }

                // ActorNumber
                try
                {
                    var p = ct.GetProperty("ActorNumber");
                    if (p != null && p.GetValue(creator, null) is int n) info.ActorNumber = n;
                }
                catch { }

                // UserId
                try
                {
                    var p = ct.GetProperty("UserId");
                    if (p != null) userId = p.GetValue(creator, null)?.ToString();
                }
                catch { }

                // --- JoinedTime (Time.time when they joined the room) ---
                try
                {
                    var p = ct.GetProperty("JoinedTime");
                    if (p != null && p.GetValue(creator, null) is float jt && jt > 0f)
                    {
                        float ago = Time.time - jt;
                        if (ago < 60f) info.JoinTime = $"{ago:F0}s ago";
                        else if (ago < 3600f) info.JoinTime = $"{ago / 60f:F0}m ago";
                        else info.JoinTime = $"{ago / 3600f:F1}h ago";
                    }
                }
                catch { }
            }

            // --- Fallback name ---
            if (info.PlayerName == "Unknown")
            {
                try
                {
                    var f = rigType.GetField("playerNameVisible",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        string n = f.GetValue(rig)?.ToString();
                        if (!string.IsNullOrEmpty(n)) info.PlayerName = n;
                    }
                }
                catch { }
            }

            // --- Account Creation Date (PlayFab) ---
            if (!string.IsNullOrEmpty(userId))
            {
                try
                {
                    QueryAccountCreationDate(userId, info);
                }
                catch (Exception ex)
                {
                    Log.Warn($"PlayFab query failed: {ex.Message}");
                }
            }

            // --- Per-player platform detection (Bingus approach) ---
            try { GetPlayerPlatform(rig, info); } catch { }

            if (info.PlayerName == "Unknown")
                info.PlayerName = rig.gameObject.name;
        }

        /// <summary>
        /// Tries multiple approaches to extract the Photon.Realtime.Player from a NetPlayer/PunNetPlayer.
        /// Logs all fields/properties/methods found for diagnostics.
        /// </summary>
        private object GetPhotonPlayer(object creator)
        {
            Type ct = creator.GetType();

            // Log all available members for diagnostics (first time only)
            if (!_loggedCreatorType)
            {
                _loggedCreatorType = true;
                Log.Info($"Creator type: {ct.FullName}, Base: {ct.BaseType?.FullName}");

                foreach (PropertyInfo p in ct.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    Log.Info($"  Property: {p.Name} ({p.PropertyType.Name})");

                foreach (FieldInfo f in ct.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    Log.Info($"  Field: {f.Name} ({f.FieldType.Name})");

                foreach (MethodInfo m in ct.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    Log.Info($"  Method: {m.Name}({string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name))}) -> {m.ReturnType.Name}");
            }

            // Strategy 1: PlayerRef property (PunNetPlayer confirmed to have this)
            try
            {
                PropertyInfo playerRefProp = ct.GetProperty("PlayerRef",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (playerRefProp != null)
                {
                    object result = playerRefProp.GetValue(creator, null);
                    if (result != null)
                    {
                        Log.Info($"Found Photon player via PlayerRef property (type: {result.GetType().FullName})");
                        return result;
                    }
                }
            }
            catch { }

            // Strategy 1b: GetPlayerRef() method (legacy)
            try
            {
                MethodInfo getRef = ct.GetMethod("GetPlayerRef",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getRef != null)
                {
                    object result = getRef.Invoke(creator, null);
                    if (result != null) return result;
                }
            }
            catch { }

            // Strategy 2: Look for a field of type Player (Photon.Realtime.Player)
            try
            {
                foreach (FieldInfo f in ct.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    string typeName = f.FieldType.FullName ?? f.FieldType.Name;
                    if (typeName.Contains("Player") && !typeName.Contains("NetPlayer"))
                    {
                        object result = f.GetValue(creator);
                        if (result != null)
                        {
                            // Verify it has CustomProperties
                            if (result.GetType().GetProperty("CustomProperties") != null)
                            {
                                Log.Info($"Found Photon player via field: {f.Name} ({typeName})");
                                return result;
                            }
                        }
                    }
                }
            }
            catch { }

            // Strategy 3: Look for a property of type Player (Photon.Realtime.Player)
            try
            {
                foreach (PropertyInfo p in ct.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    string typeName = p.PropertyType.FullName ?? p.PropertyType.Name;
                    if (typeName.Contains("Player") && !typeName.Contains("NetPlayer"))
                    {
                        object result = p.GetValue(creator, null);
                        if (result != null)
                        {
                            if (result.GetType().GetProperty("CustomProperties") != null)
                            {
                                Log.Info($"Found Photon player via property: {p.Name} ({typeName})");
                                return result;
                            }
                        }
                    }
                }
            }
            catch { }

            // Strategy 4: Check base type too
            try
            {
                Type baseType = ct.BaseType;
                if (baseType != null)
                {
                    foreach (FieldInfo f in baseType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        string typeName = f.FieldType.FullName ?? f.FieldType.Name;
                        if (typeName.Contains("Player") && !typeName.Contains("NetPlayer"))
                        {
                            object result = f.GetValue(creator);
                            if (result != null && result.GetType().GetProperty("CustomProperties") != null)
                            {
                                Log.Info($"Found Photon player via base field: {f.Name}");
                                return result;
                            }
                        }
                    }
                }
            }
            catch { }

            // Strategy 5: Try common method/property names
            string[] names = { "GetPlayer", "Player", "player", "PhotonPlayer", "photonPlayer", "PlayerRef", "playerRef" };
            foreach (string name in names)
            {
                try
                {
                    MethodInfo m = ct.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (m != null && m.GetParameters().Length == 0)
                    {
                        object result = m.Invoke(creator, null);
                        if (result != null) { Log.Info($"Found Photon player via method: {name}"); return result; }
                    }
                }
                catch { }

                try
                {
                    PropertyInfo p = ct.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null)
                    {
                        object result = p.GetValue(creator, null);
                        if (result != null) { Log.Info($"Found Photon player via property: {name}"); return result; }
                    }
                }
                catch { }
            }

            // Strategy 6: Try VRRig.netView -> PhotonView.Owner
            // This is a completely different path that bypasses Creator
            Log.Warn("All strategies to get Photon player from Creator failed");
            return null;
        }

        private static bool _loggedCreatorType = false;

        /// <summary>
        /// Fallback: gets Photon.Realtime.Player from VRRig's network view.
        /// Path: VRRig.netView -> NetworkView -> GetView -> PhotonView -> Owner
        /// </summary>
        private object GetPhotonPlayerFromRig(VRRig rig)
        {
            Type rigType = rig.GetType();

            // Try netView field -> GetView() -> Owner
            string[] viewFieldNames = { "netView", "photonView", "NetworkView", "networkView", "view" };

            foreach (string fieldName in viewFieldNames)
            {
                try
                {
                    // Try as field
                    FieldInfo f = rigType.GetField(fieldName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    object viewObj = null;
                    if (f != null) viewObj = f.GetValue(rig);

                    // Try as property
                    if (viewObj == null)
                    {
                        PropertyInfo p = rigType.GetProperty(fieldName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (p != null) viewObj = p.GetValue(rig, null);
                    }

                    if (viewObj == null) continue;

                    // If this is a NetworkView wrapper, try GetView() to get PhotonView
                    object photonView = viewObj;
                    try
                    {
                        MethodInfo getView = viewObj.GetType().GetMethod("GetView",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (getView != null)
                        {
                            object pv = getView.Invoke(viewObj, null);
                            if (pv != null) photonView = pv;
                        }
                    }
                    catch { }

                    // Get Owner from PhotonView
                    PropertyInfo ownerProp = photonView.GetType().GetProperty("Owner",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (ownerProp != null)
                    {
                        object owner = ownerProp.GetValue(photonView, null);
                        if (owner != null)
                        {
                            Log.Info($"Got Photon player via rig.{fieldName}.Owner");
                            return owner;
                        }
                    }

                    // Try Controller property too
                    PropertyInfo ctrlProp = photonView.GetType().GetProperty("Controller",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (ctrlProp != null)
                    {
                        object ctrl = ctrlProp.GetValue(photonView, null);
                        if (ctrl != null)
                        {
                            Log.Info($"Got Photon player via rig.{fieldName}.Controller");
                            return ctrl;
                        }
                    }
                }
                catch { }
            }

            // Try GetComponent<PhotonView> as last resort
            try
            {
                Component[] components = rig.GetComponents<Component>();
                foreach (Component c in components)
                {
                    if (c == null) continue;
                    Type ct = c.GetType();
                    if (ct.Name.Contains("PhotonView") || ct.Name.Contains("NetworkView"))
                    {
                        PropertyInfo ownerProp = ct.GetProperty("Owner",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (ownerProp != null)
                        {
                            object owner = ownerProp.GetValue(c, null);
                            if (owner != null)
                            {
                                Log.Info($"Got Photon player via component: {ct.Name}.Owner");
                                return owner;
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private object GetCreator(VRRig rig, Type rigType)
        {
            try
            {
                var prop = rigType.GetProperty("Creator", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    object c = prop.GetValue(rig, null);
                    if (c != null) return c;
                }
            }
            catch { }

            try
            {
                var field = rigType.GetField("creator", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(rig);
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets FPS from VRRig.fps (public int field, networked for all players).
        /// </summary>
        private void GetFPS(VRRig rig, PlayerInfo info)
        {
            Type rigType = rig.GetType();
            FieldInfo fpsField = rigType.GetField("fps",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fpsField != null)
            {
                object val = fpsField.GetValue(rig);
                if (val is int fpsInt && fpsInt > 0)
                {
                    info.FPS = fpsInt;
                    return;
                }
            }

            // Fallback: local player only
            if (info.IsLocal)
                info.FPS = 1f / Time.deltaTime;
        }

        /// <summary>
        /// Gathers all behavior flags for a player: movement anomalies, RPC abuse, low FPS.
        /// </summary>
        private void GatherBehaviorFlags(VRRig rig, PlayerInfo info)
        {
            // Movement flags from MovementTracker
            if (MovementTracker.Instance != null && info.ActorNumber > 0)
            {
                List<string> moveFlags = MovementTracker.Instance.GetFlags(info.ActorNumber);
                info.BehaviorFlags.AddRange(moveFlags);
            }

            // RPC/event flags from NetworkEventDetector
            if (info.ActorNumber > 0)
            {
                List<string> rpcFlags = NetworkEventDetector.GetFlags(info.ActorNumber);
                foreach (string flag in rpcFlags)
                {
                    if (!info.BehaviorFlags.Contains(flag))
                        info.BehaviorFlags.Add(flag);
                }
            }

            // Per-player behavioral anomaly checks (tag distance, color, scale, arm length, etc.)
            try
            {
                List<string> behaviorFlags = BehaviorDetector.Analyze(rig);
                foreach (string flag in behaviorFlags)
                {
                    if (!info.BehaviorFlags.Contains(flag))
                        info.BehaviorFlags.Add(flag);
                }
            }
            catch (Exception ex) { Log.Warn($"BehaviorDetector failed: {ex.Message}"); }

            // Low FPS check — under 60 Hz is suspicious
            if (info.FPS > 0 && info.FPS < 60)
            {
                info.BehaviorFlags.Add($"Low FPS: {info.FPS:F0} Hz");
            }
        }

        /// <summary>
        /// Detects mods on remote players by reading their Photon CustomProperties.
        /// Also checks for behavioral flags from NetworkEventDetector and
        /// suspiciously empty properties (Seralyth signature).
        /// Falls back to local mod + assembly scan for the local player.
        /// </summary>
        private List<ModInfo> DetectRemoteMods(VRRig rig, PlayerInfo info)
        {
            var mods = new List<ModInfo>();

            if (info.IsLocal)
            {
                // For local player, scan BepInEx/plugins AND loaded assemblies
                mods = ScanLocalMods();
                var assemblyMods = ScanLoadedAssemblies();
                foreach (var m in assemblyMods)
                {
                    // Avoid duplicates
                    bool exists = false;
                    foreach (var existing in mods)
                    {
                        if (existing.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))
                        { exists = true; break; }
                    }
                    if (!exists) mods.Add(m);
                }
                return mods;
            }

            // For remote players, check their Photon CustomProperties
            try
            {
                object creator = GetCreator(rig, rig.GetType());
                if (creator == null)
                {
                    Log.Warn("Creator is null, can't read CustomProperties");
                    return mods;
                }

                // Get the Photon.Realtime.Player from the NetPlayer/PunNetPlayer
                object photonPlayer = GetPhotonPlayer(creator);

                // Fallback: try VRRig -> netView -> PhotonView -> Owner
                if (photonPlayer == null)
                {
                    photonPlayer = GetPhotonPlayerFromRig(rig);
                }

                if (photonPlayer == null)
                {
                    Log.Warn($"Could not get Photon player from creator ({creator.GetType().FullName}) or rig");
                    return mods;
                }

                // Get CustomProperties
                Type pt = photonPlayer.GetType();
                PropertyInfo propsProp = pt.GetProperty("CustomProperties");
                if (propsProp == null)
                {
                    Log.Warn($"No CustomProperties property on type: {pt.FullName}");
                    return mods;
                }

                object propsObj = propsProp.GetValue(photonPlayer, null);
                if (propsObj == null)
                {
                    Log.Warn($"CustomProperties returned null for {info.PlayerName}");
                }
                else
                {
                    // Photon uses ExitGames.Client.Photon.Hashtable, not System.Collections.Hashtable
                    // Use reflection to iterate it since it implements IDictionary
                    var dict = propsObj as System.Collections.IDictionary;
                    if (dict == null)
                    {
                        Log.Warn($"CustomProperties is not IDictionary, type: {propsObj.GetType().FullName}");
                    }
                    else
                    {
                        Log.Info($"Player {info.PlayerName} CustomProperties ({dict.Count} keys):");
                        foreach (object key in dict.Keys)
                        {
                            string keyStr = key?.ToString();
                            if (string.IsNullOrEmpty(keyStr)) continue;

                            // Skip known game properties that are NOT mods
                            if (keyStr == "didTutorial") continue;

                            try { Log.Info($"  '{keyStr}' = '{dict[key]}'"); } catch { }

                            // Only flag if it matches a known cheat in the database
                            if (CheatDatabase.IsCheat(keyStr))
                            {
                                string displayName = CheatDatabase.GetDisplayName(keyStr);
                                mods.Add(new ModInfo { Name = displayName, IsCheat = true });
                            }
                            else
                            {
                                // Unknown property — show as non-cheat mod indicator
                                mods.Add(new ModInfo { Name = keyStr, IsCheat = false });
                            }
                        }

                        // Note: Most normal players only have "didTutorial" (1 key),
                        // so empty props is NOT suspicious by itself.
                        // Seralyth detection relies on behavioral flags (RPC/events) instead.
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Remote mod detection failed: {ex.Message}");
            }

            // Check behavioral flags from NetworkEventDetector
            if (info.ActorNumber > 0)
            {
                try
                {
                    List<string> flags = NetworkEventDetector.GetFlags(info.ActorNumber);
                    foreach (string flag in flags)
                    {
                        mods.Add(new ModInfo { Name = flag, IsCheat = true });
                    }
                }
                catch { }
            }

            return mods;
        }

        /// <summary>
        /// Scans all loaded assemblies for known cheat mod namespaces/types.
        /// Catches mods like ForeverCosmetx and Seralyth that don't set CustomProperties.
        /// </summary>
        private List<ModInfo> ScanLoadedAssemblies()
        {
            var mods = new List<ModInfo>();
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cheatNamespaces = CheatDatabase.GetCheatNamespaces();
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (Assembly asm in assemblies)
                {
                    try
                    {
                        // Check assembly name first
                        string asmName = asm.GetName().Name;

                        // Skip system/Unity/Photon assemblies
                        if (asmName.StartsWith("System") || asmName.StartsWith("Unity") ||
                            asmName.StartsWith("Photon") || asmName.StartsWith("mscorlib") ||
                            asmName.StartsWith("Mono") || asmName.StartsWith("BepInEx") ||
                            asmName.StartsWith("0Harmony") || asmName == "VenneChecker")
                            continue;

                        // Check types in the assembly for known cheat namespaces
                        Type[] types;
                        try { types = asm.GetTypes(); }
                        catch { continue; }

                        foreach (Type type in types)
                        {
                            string ns = type.Namespace;
                            if (string.IsNullOrEmpty(ns)) continue;

                            // Check exact namespace match
                            if (cheatNamespaces.TryGetValue(ns, out string displayName))
                            {
                                if (found.Add(displayName))
                                {
                                    mods.Add(new ModInfo { Name = displayName, IsCheat = true });
                                    Log.Warn($"[ASSEMBLY] Detected cheat namespace: {ns} ({displayName})");
                                }
                            }

                            // Also check top-level namespace (e.g., "Seralyth" from "Seralyth.Patches.Menu")
                            string topNs = ns.Contains(".") ? ns.Substring(0, ns.IndexOf('.')) : ns;
                            if (cheatNamespaces.TryGetValue(topNs, out string topDisplayName))
                            {
                                if (found.Add(topDisplayName))
                                {
                                    mods.Add(new ModInfo { Name = topDisplayName, IsCheat = true });
                                    Log.Warn($"[ASSEMBLY] Detected cheat namespace: {topNs} ({topDisplayName})");
                                }
                            }
                        }
                    }
                    catch { continue; }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Assembly scan failed: {ex.Message}");
            }

            return mods;
        }

        /// <summary>
        /// Queries PlayFab for the account creation date using the player's UserId.
        /// UserId in GT doubles as the PlayFab ID.
        /// </summary>
        private void QueryAccountCreationDate(string userId, PlayerInfo info)
        {
            // Check cache first
            if (_accountDates.TryGetValue(userId, out DateTime cached))
            {
                info.JoinTime = cached.ToString("yyyy-MM-dd");
                return;
            }

            // Don't re-query if already pending
            if (_pendingQueries.Contains(userId))
                return;

            _pendingQueries.Add(userId);

            // Use reflection to call PlayFabClientAPI.GetAccountInfo
            // to avoid compile-time dependency issues
            Type pfType = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                pfType = asm.GetType("PlayFab.PlayFabClientAPI");
                if (pfType != null) break;
            }

            if (pfType == null)
            {
                Log.Warn("PlayFabClientAPI type not found");
                return;
            }

            // Build the request object
            Type reqType = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                reqType = asm.GetType("PlayFab.ClientModels.GetAccountInfoRequest");
                if (reqType != null) break;
            }

            if (reqType == null)
            {
                Log.Warn("GetAccountInfoRequest type not found");
                return;
            }

            object request = Activator.CreateInstance(reqType);
            // Set PlayFabId field
            FieldInfo pfIdField = reqType.GetField("PlayFabId");
            if (pfIdField != null)
                pfIdField.SetValue(request, userId);

            // Find GetAccountInfo method
            // Signature: GetAccountInfo(GetAccountInfoRequest, Action<GetAccountInfoResult>, Action<PlayFabError>, object, Dictionary<string,string>)
            Type resultType = null;
            Type errorType = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (resultType == null)
                    resultType = asm.GetType("PlayFab.ClientModels.GetAccountInfoResult");
                if (errorType == null)
                    errorType = asm.GetType("PlayFab.PlayFabError");
                if (resultType != null && errorType != null) break;
            }

            if (resultType == null || errorType == null)
            {
                Log.Warn("PlayFab result/error types not found");
                return;
            }

            // Create success callback
            Type successDelegateType = typeof(Action<>).MakeGenericType(resultType);
            Type errorDelegateType = typeof(Action<>).MakeGenericType(errorType);

            // We need to create callback delegates that handle the result
            // Use a helper to avoid generic type issues
            var callbackHelper = new PlayFabCallbackHelper(userId, info);

            // Create the success delegate via reflection
            MethodInfo onSuccessMethod = typeof(PlayFabCallbackHelper).GetMethod("OnSuccess");
            Delegate successDelegate = Delegate.CreateDelegate(successDelegateType, callbackHelper, onSuccessMethod);

            MethodInfo onErrorMethod = typeof(PlayFabCallbackHelper).GetMethod("OnError");
            Delegate errorDelegate = Delegate.CreateDelegate(errorDelegateType, callbackHelper, onErrorMethod);

            // Call PlayFabClientAPI.GetAccountInfo
            MethodInfo[] methods = pfType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (MethodInfo m in methods)
            {
                if (m.Name != "GetAccountInfo") continue;
                var parms = m.GetParameters();
                if (parms.Length >= 2)
                {
                    try
                    {
                        if (parms.Length == 5)
                            m.Invoke(null, new object[] { request, successDelegate, errorDelegate, null, null });
                        else if (parms.Length == 4)
                            m.Invoke(null, new object[] { request, successDelegate, errorDelegate, null });
                        else if (parms.Length == 3)
                            m.Invoke(null, new object[] { request, successDelegate, errorDelegate });
                        else if (parms.Length == 2)
                            m.Invoke(null, new object[] { request, successDelegate });

                        Log.Info($"PlayFab GetAccountInfo called for {userId}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"GetAccountInfo invoke failed ({parms.Length} params): {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Per-player platform detection using VRRig fields (Bingus nametag approach).
        /// Checks currentRankedSubTierPC/Quest and cosmetic set strings.
        /// </summary>
        private void GetPlayerPlatform(VRRig rig, PlayerInfo info)
        {
            Type rigType = rig.GetType();

            // Strategy 1: Check currentRankedSubTierPC > 0 → Steam/PC
            try
            {
                FieldInfo pcField = rigType.GetField("currentRankedSubTierPC",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pcField != null)
                {
                    object val = pcField.GetValue(rig);
                    if (val is int pcTier && pcTier > 0)
                    {
                        info.Platform = "Steam / PCVR";
                        return;
                    }
                }
            }
            catch { }

            // Strategy 2: Check currentRankedSubTierQuest > 0 → Meta Quest
            try
            {
                FieldInfo questField = rigType.GetField("currentRankedSubTierQuest",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (questField != null)
                {
                    object val = questField.GetValue(rig);
                    if (val is int questTier && questTier > 0)
                    {
                        info.Platform = "Meta Quest";
                        return;
                    }
                }
            }
            catch { }

            // Strategy 3: Check cosmetic set string for platform hints
            try
            {
                // Try concatStringOfCosmeticsAllowed or similar cosmetic set fields
                string[] cosmeticFieldNames = {
                    "concatStringOfCosmeticsAllowed",
                    "cosmeticsAllowed",
                    "cosmeticSet"
                };

                foreach (string fieldName in cosmeticFieldNames)
                {
                    FieldInfo f = rigType.GetField(fieldName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f == null) continue;

                    string cosmeticStr = f.GetValue(rig)?.ToString();
                    if (string.IsNullOrEmpty(cosmeticStr)) continue;

                    string lower = cosmeticStr.ToLower();
                    if (lower.Contains("s. first login"))
                    {
                        info.Platform = "Steam / PCVR";
                        return;
                    }
                    if (lower.Contains("first login"))
                    {
                        info.Platform = "Meta Quest";
                        return;
                    }
                }
            }
            catch { }

            // Strategy 4: Check Photon CustomProperties count (>1 non-tutorial = likely Quest/Oculus)
            try
            {
                object creator = GetCreator(rig, rigType);
                if (creator != null)
                {
                    object photonPlayer = GetPhotonPlayer(creator);
                    if (photonPlayer == null) photonPlayer = GetPhotonPlayerFromRig(rig);

                    if (photonPlayer != null)
                    {
                        PropertyInfo propsProp = photonPlayer.GetType().GetProperty("CustomProperties");
                        if (propsProp != null)
                        {
                            var dict = propsProp.GetValue(photonPlayer, null) as System.Collections.IDictionary;
                            if (dict != null && dict.Count > 1)
                            {
                                info.Platform = "Meta Quest";
                                return;
                            }
                        }
                    }
                }
            }
            catch { }

            // Default fallback
            info.Platform = "Meta Quest";
        }

        public List<ModInfo> ScanLocalMods()
        {
            var mods = new List<ModInfo>();
            try
            {
                string pluginsPath = Path.Combine(Paths.BepInExRootPath, "plugins");
                if (!Directory.Exists(pluginsPath)) return mods;

                foreach (string dll in Directory.GetFiles(pluginsPath, "*.dll", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(dll);
                    if (name.Equals("VenneChecker", StringComparison.OrdinalIgnoreCase)) continue;
                    mods.Add(new ModInfo { Name = name, IsCheat = CheatDatabase.IsCheat(name) });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"ScanLocalMods failed: {ex.Message}");
            }
            return mods;
        }
    }

    /// <summary>
    /// Helper class for PlayFab async callbacks.
    /// Methods must accept 'object' parameter to work with reflection-created delegates.
    /// </summary>
    public class PlayFabCallbackHelper
    {
        private readonly string _userId;
        private readonly PlayerInfo _info;

        public PlayFabCallbackHelper(string userId, PlayerInfo info)
        {
            _userId = userId;
            _info = info;
        }

        public void OnSuccess(object result)
        {
            try
            {
                // result is GetAccountInfoResult
                // result.AccountInfo.Created is DateTime
                Type resultType = result.GetType();
                PropertyInfo accountInfoProp = resultType.GetProperty("AccountInfo");
                if (accountInfoProp == null) return;

                object accountInfo = accountInfoProp.GetValue(result, null);
                if (accountInfo == null) return;

                Type aiType = accountInfo.GetType();
                PropertyInfo createdProp = aiType.GetProperty("Created");
                if (createdProp == null) return;

                object createdObj = createdProp.GetValue(accountInfo, null);
                if (createdObj is DateTime created)
                {
                    PlayerScanner._SetAccountDate(_userId, created);
                    _info.JoinTime = created.ToString("yyyy-MM-dd");
                    Log.Info($"Account created: {_userId} = {created:yyyy-MM-dd}");

                    // Update the menu if it's showing this player
                    try
                    {
                        if (MenuManager.Instance != null && PlayerScanner.Instance?.LastScanResult == _info)
                            MenuManager.Instance.DisplayPlayerInfo(_info);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"PlayFab OnSuccess parse failed: {ex}");
            }
        }

        public void OnError(object error)
        {
            try
            {
                Type et = error.GetType();
                PropertyInfo msgProp = et.GetProperty("ErrorMessage");
                string msg = msgProp?.GetValue(error, null)?.ToString() ?? "unknown error";
                Log.Warn($"PlayFab GetAccountInfo failed for {_userId}: {msg}");
            }
            catch { }
        }
    }

    // Partial extension to allow callback helper to set dates
    public partial class PlayerScanner
    {
        internal static void _SetAccountDate(string userId, DateTime date)
        {
            _accountDates[userId] = date;
            _pendingQueries.Remove(userId);
        }
    }
}
