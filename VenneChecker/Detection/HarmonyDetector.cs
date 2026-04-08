using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Advanced cheat detection via Harmony patch enumeration, Harmony ID audit,
    /// BepInEx Chainloader inspection, AppDomain assembly scanning, and scene MonoBehaviour scanning.
    /// These detect cheats even when they strip CustomProperties or rename their DLLs.
    /// </summary>
    public static class HarmonyDetector
    {
        // ═══════════════════════════════════════════
        //  KNOWN CHEAT HARMONY IDs
        // ═══════════════════════════════════════════
        private static readonly Dictionary<string, string> KnownCheatHarmonyIds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Exact IDs extracted from cheat mod DLLs & source code ──
            { "org.seralyth.gorillatag.seralythmenu", "Seralyth Menu" },
            { "Juul", "Juul Mod Menu" },
            { "org.Slkyy.gorillatag.menu", "Saturn Client" },
            { "com.goldentrophy.gorillatag.forevercosmetx", "ForeverCosmetx" },
            { "com.goldentrophy.gorillatag.fortniteemotewheel", "FortniteEmoteWheel" },
            { "org.Juul.gorillatag.NotifiLib", "Juul NotifiLib" },
            { "org.gorillatag.lars.notifications2", "Saturn/Astre NotifiLib" },
            { "org.Euph.gorillatag.NotifiLib", "Euph NotifiLib (CanvasGUI)" },
            { "com.euph.gorillatag.lol", "CanvasGUI / Euph Menu" },
            { "com.yourname.gtag.nametags", "Astre Nametags" },
            { "com.nxo.nxoremastered.org", "NXO Remastered" },
            { "com.f0.mods.nylox.wtf.menu", "Nylox Menu" },
            { "com.peanut.speedboost", "SpeedboostMod" },
            { "com.alta.gorillatag.unlock", "Unlock V.I.M." },
            { "com.deadcourtvr.Dead", "GrayScreen (DeadCourt)" },
            { "com.user.micmod", "Loud Mic Mod" },
            { "com.mist.chaspullmod", "Lusid Pull Mod" },
            { "com.kylethescientist.gorillatag.walksimulator", "WalkSim" },
            // ── Common cheat name patterns ──
            { "obsidian", "Obsidian" },
            { "genesis", "Genesis" },
            { "elux", "Elux" },
            { "violet", "Violet" },
            { "void", "Void" },
            { "cronos", "Cronos" },
            { "orbit", "Orbit" },
            { "elixir", "Elixir" },
            { "control", "Control" },
            { "colossal", "Colossal" },
            { "malachi", "Malachi" },
            { "astre", "Astre" },
            { "mist", "Mist" },
            { "explicit", "Explicit" },
            { "destiny", "Destiny" },
            { "rexon", "Rexon" },
            { "chqser", "Chqser" },
            { "nxo", "NXO" },
            { "nylox", "Nylox" },
            { "xenon", "XENON/Astre" },
            { "canvasgui", "CanvasGUI" },
            { "gkong", "GKong Menu" },
        };

        // ═══════════════════════════════════════════
        //  KNOWN CHEAT BepInEx GUIDs
        // ═══════════════════════════════════════════
        private static readonly Dictionary<string, string> KnownCheatGuids =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Exact BepInPlugin GUIDs extracted from DLLs ──
            { "org.seralyth.gorillatag.seralythmenu", "Seralyth Menu" },
            { "Juul", "Juul Mod Menu" },
            { "org.Slkyy.gorillatag.menu", "Saturn Client" },
            { "com.goldentrophy.gorillatag.forevercosmetx", "ForeverCosmetx" },
            { "com.goldentrophy.gorillatag.fortniteemotewheel", "FortniteEmoteWheel" },
            { "org.Juul.gorillatag.NotifiLib", "Juul NotifiLib" },
            { "org.gorillatag.lars.notifications2", "Saturn/Astre NotifiLib" },
            { "org.Euph.gorillatag.NotifiLib", "Euph NotifiLib (CanvasGUI)" },
            { "com.euph.gorillatag.lol", "CanvasGUI / Euph Menu" },
            { "com.yourname.gtag.nametags", "Astre Nametags" },
            { "com.nxo.nxoremastered.org", "NXO Remastered" },
            { "com.f0.mods.nylox.wtf.menu", "Nylox Menu" },
            { "com.peanut.speedboost", "SpeedboostMod" },
            { "com.alta.gorillatag.unlock", "Unlock V.I.M." },
            { "com.deadcourtvr.Dead", "GrayScreen (DeadCourt)" },
            { "com.user.micmod", "Loud Mic Mod" },
            { "com.mist.chaspullmod", "Lusid Pull Mod" },
            { "com.kylethescientist.gorillatag.walksimulator", "WalkSim" },
        };

        // ═══════════════════════════════════════════
        //  CRITICAL GAME METHODS — patches on these are suspicious
        // ═══════════════════════════════════════════
        private static readonly HashSet<string> CriticalPatchTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Anti-cheat bypasses
            "MonkeAgent.SendReport",
            "MonkeAgent.CloseInvalidRoom",
            "MonkeAgent.CheckReports",
            "MonkeAgent.DispatchReport",
            "MonkeAgent.GetRPCCallTracker",
            "MonkeAgent.LogErrorCount",
            "MonkeAgent.QuitDelay",
            "MonkeAgent.ShouldDisconnectFromRoom",
            "MonkeAgent.IncrementRPCCall",
            "MonkeAgent.IncrementRPCCallLocal",

            // VRRig exploits
            "VRRig.IsItemAllowed",
            "VRRig.IsPositionInRange",
            "VRRig.IncrementRPC",
            "VRRig.SerializeReadShared",

            // Game manager
            "GorillaGameManager.ForceStopGame_DisconnectAndDestroy",
            "GorillaGameManager.ValidGameMode",

            // Player physics
            "GTPlayer.LateUpdate",
            "GTPlayer.AntiTeleportTechnology",
            "GTPlayer.ApplyKnockback",

            // Ban/security
            "GorillaServer.CheckForBadName",
            "GorillaComputer.CheckAutoBanListForName",
            "CosmeticsController.ReauthOrBan",

            // Telemetry evasion
            "GorillaTelemetry.EnqueueTelemetryEvent",
            "GorillaTelemetry.EnqueueTelemetryEventPlayFab",
            "PlayFabDeviceUtil.SendDeviceInfoToPlayFab",

            // Network
            "GorillaNetworkPublicTestsJoin.GracePeriod",
            "GorillaNetworkPublicTestJoin2.GracePeriod",

            // Cosmetic bypass
            "CosmeticsController.CosmeticSet.LoadFromPlayerPreferences",

            // KID permission bypass
            "KIDManager.HasPermissionToUseFeature",
        };

        // Known safe Harmony IDs (won't flag these)
        private static readonly HashSet<string> SafeHarmonyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "com.venne.vennechecker",
            "com.venne.vennechecker.detector",
        };

        // Known safe assemblies
        private static readonly HashSet<string> SafeAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Assembly-CSharp", "Assembly-CSharp-firstpass",
            "VenneChecker",
            "0Harmony", "BepInEx", "BepInEx.Preloader",
            "MonoMod.Utils", "MonoMod.RuntimeDetour", "Mono.Cecil",
        };

        private static bool _scanned;
        private static readonly List<string> _results = new List<string>();

        /// <summary>
        /// Runs all Harmony/assembly/BepInEx scans. Returns list of detection strings.
        /// Results are cached after first scan. Call Rescan() to force re-scan.
        /// </summary>
        public static List<string> Scan()
        {
            if (_scanned) return new List<string>(_results);
            _scanned = true;
            _results.Clear();

            try { ScanHarmonyIds(); } catch (Exception ex) { Log.Warn($"[HARMONY] ID scan failed: {ex.Message}"); }
            try { ScanHarmonyPatches(); } catch (Exception ex) { Log.Warn($"[HARMONY] Patch scan failed: {ex.Message}"); }
            try { ScanBepInExPlugins(); } catch (Exception ex) { Log.Warn($"[HARMONY] BepInEx scan failed: {ex.Message}"); }
            try { ScanBepInExManagerObject(); } catch (Exception ex) { Log.Warn($"[HARMONY] Manager scan failed: {ex.Message}"); }
            try { ScanAppDomainAssemblies(); } catch (Exception ex) { Log.Warn($"[HARMONY] Assembly scan failed: {ex.Message}"); }
            try { ScanSceneMonoBehaviours(); } catch (Exception ex) { Log.Warn($"[HARMONY] Scene scan failed: {ex.Message}"); }
            try { ScanDependencyErrors(); } catch (Exception ex) { Log.Warn($"[HARMONY] DependencyErrors scan failed: {ex.Message}"); }

            Log.Info($"[HARMONY] Scan complete: {_results.Count} detections");
            return new List<string>(_results);
        }

        public static void Rescan()
        {
            _scanned = false;
            Scan();
        }

        // ═══════════════════════════════════════════
        //  1. HARMONY ID AUDIT
        // ═══════════════════════════════════════════

        private static void ScanHarmonyIds()
        {
            // Harmony.HasAnyPatches(id) checks if a specific ID has patches
            foreach (var kvp in KnownCheatHarmonyIds)
            {
                try
                {
                    if (Harmony.HasAnyPatches(kvp.Key))
                    {
                        string msg = $"Cheat Harmony ID: {kvp.Value} ({kvp.Key})";
                        _results.Add(msg);
                        Log.Warn($"[HARMONY] {msg}");
                    }
                }
                catch { }
            }
        }

        // ═══════════════════════════════════════════
        //  2. HARMONY PATCH ENUMERATION
        // ═══════════════════════════════════════════

        private static void ScanHarmonyPatches()
        {
            var patchedMethods = Harmony.GetAllPatchedMethods();
            if (patchedMethods == null) return;

            HashSet<string> flaggedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (MethodBase method in patchedMethods)
            {
                if (method == null) continue;

                string methodKey = $"{method.DeclaringType?.Name}.{method.Name}";
                bool isCritical = CriticalPatchTargets.Contains(methodKey);

                Patches patchInfo = Harmony.GetPatchInfo(method);
                if (patchInfo == null) continue;

                // Check all patches (prefixes, postfixes, transpilers) on this method
                CheckPatchList(patchInfo.Prefixes, methodKey, isCritical, flaggedIds);
                CheckPatchList(patchInfo.Postfixes, methodKey, isCritical, flaggedIds);
                CheckPatchList(patchInfo.Transpilers, methodKey, isCritical, flaggedIds);
            }
        }

        private static void CheckPatchList(IEnumerable<Patch> patches, string methodKey, bool isCritical, HashSet<string> flaggedIds)
        {
            if (patches == null) return;

            foreach (Patch patch in patches)
            {
                string owner = patch.owner;
                if (string.IsNullOrEmpty(owner)) continue;
                if (SafeHarmonyIds.Contains(owner)) continue;

                // Check if this is a known cheat Harmony ID
                if (KnownCheatHarmonyIds.TryGetValue(owner, out string cheatName))
                {
                    if (flaggedIds.Add(owner))
                    {
                        string msg = $"Cheat patches detected: {cheatName}";
                        _results.Add(msg);
                        Log.Warn($"[HARMONY] {msg} (ID: {owner}, method: {methodKey})");
                    }
                }
                // If not known but patching critical methods, flag as suspicious
                else if (isCritical)
                {
                    string msg = $"Suspicious patch on {methodKey} by '{owner}'";
                    if (flaggedIds.Add($"{owner}_{methodKey}"))
                    {
                        _results.Add(msg);
                        Log.Warn($"[HARMONY] {msg}");
                    }
                }
            }
        }

        // ═══════════════════════════════════════════
        //  3. BepInEx CHAINLOADER PLUGIN SCANNING
        // ═══════════════════════════════════════════

        private static void ScanBepInExPlugins()
        {
            try
            {
                // BepInEx.Bootstrap.Chainloader.PluginInfos is a Dictionary<string, PluginInfo>
                Type chainloaderType = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    chainloaderType = asm.GetType("BepInEx.Bootstrap.Chainloader");
                    if (chainloaderType != null) break;
                }
                if (chainloaderType == null) return;

                PropertyInfo pluginInfosProp = chainloaderType.GetProperty("PluginInfos",
                    BindingFlags.Public | BindingFlags.Static);
                if (pluginInfosProp == null) return;

                object pluginInfos = pluginInfosProp.GetValue(null, null);
                if (pluginInfos == null) return;

                var dict = pluginInfos as System.Collections.IDictionary;
                if (dict == null) return;

                foreach (object key in dict.Keys)
                {
                    string guid = key?.ToString();
                    if (string.IsNullOrEmpty(guid)) continue;

                    // Check against known cheat GUIDs
                    if (KnownCheatGuids.TryGetValue(guid, out string cheatName))
                    {
                        string msg = $"Cheat plugin loaded: {cheatName} ({guid})";
                        _results.Add(msg);
                        Log.Warn($"[BEPINEX] {msg}");
                    }
                }

                Log.Info($"[BEPINEX] Scanned {dict.Count} loaded plugins");
            }
            catch (Exception ex) { Log.Warn($"[BEPINEX] PluginInfos scan failed: {ex.Message}"); }
        }

        // ═══════════════════════════════════════════
        //  4. BepInEx MANAGER GAMEOBJECT INSPECTION
        // ═══════════════════════════════════════════

        private static void ScanBepInExManagerObject()
        {
            try
            {
                // BepInEx attaches all plugins as components on a manager GameObject
                GameObject[] allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                foreach (GameObject obj in allObjects)
                {
                    if (obj.name != "BepInEx_Manager" && obj.name != "BepInExManager") continue;

                    MonoBehaviour[] components = obj.GetComponents<MonoBehaviour>();
                    foreach (MonoBehaviour mb in components)
                    {
                        if (mb == null) continue;
                        string typeName = mb.GetType().FullName ?? mb.GetType().Name;

                        // Check namespace against known cheats
                        string ns = mb.GetType().Namespace ?? "";
                        if (CheatDatabase.IsCheatNamespace(ns))
                        {
                            string displayName = CheatDatabase.GetNamespaceDisplayName(ns);
                            string msg = $"Cheat component on BepInEx Manager: {displayName}";
                            _results.Add(msg);
                            Log.Warn($"[BEPINEX] {msg} (type: {typeName})");
                        }
                    }
                    break;
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════
        //  5. APPDOMAIN ASSEMBLY ENUMERATION
        //  Catches renamed/obfuscated DLLs that namespace checks miss
        // ═══════════════════════════════════════════

        private static void ScanAppDomainAssemblies()
        {
            // Build set of known vanilla GT assemblies
            HashSet<string> vanillaAssemblyPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "mscorlib", "netstandard", "Microsoft",
                "Unity", "UnityEngine", "UnityEditor",
                "Photon", "ExitGames",
                "BepInEx", "0Harmony", "MonoMod", "Mono.",
                "Assembly-CSharp", "Assembly-CSharp-firstpass",
                "PlayFab", "Newtonsoft", "LIV",
                "Oculus", "OVR", "Meta",
                "TextMeshPro", "TMPro",
                "SteamVR", "Valve",
                "GorillaNetworking", // part of game
                "VenneChecker",
                "Cinemachine", "PostProcessing",
                "FlatBuffers", "Google", "Firebase",
                "Steamworks", "Facepunch",
            };

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            int suspiciousCount = 0;

            foreach (Assembly asm in assemblies)
            {
                try
                {
                    string asmName = asm.GetName().Name;
                    if (string.IsNullOrEmpty(asmName)) continue;

                    // Check if it's a known safe assembly
                    bool safe = false;
                    foreach (string prefix in vanillaAssemblyPrefixes)
                    {
                        if (asmName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                            asmName.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            safe = true;
                            break;
                        }
                    }
                    if (safe) continue;

                    // Check if it matches known cheat namespaces
                    bool isCheat = false;
                    try
                    {
                        Type[] types = asm.GetTypes();
                        foreach (Type t in types)
                        {
                            string ns = t.Namespace;
                            if (!string.IsNullOrEmpty(ns) && CheatDatabase.IsCheatNamespace(ns))
                            {
                                string displayName = CheatDatabase.GetNamespaceDisplayName(ns);
                                string msg = $"Cheat assembly: {displayName} ({asmName})";
                                _results.Add(msg);
                                Log.Warn($"[ASSEMBLY] {msg}");
                                isCheat = true;
                                break;
                            }
                        }
                    }
                    catch { }

                    if (!isCheat)
                    {
                        // Unknown non-vanilla assembly — log it but don't flag
                        suspiciousCount++;
                        Log.Info($"[ASSEMBLY] Non-vanilla assembly: {asmName}");
                    }
                }
                catch { }
            }

            if (suspiciousCount > 0)
                Log.Info($"[ASSEMBLY] Found {suspiciousCount} non-vanilla assemblies (not flagged)");
        }

        // ═══════════════════════════════════════════
        //  6. SCENE MONOBEHAVIOUR SCAN
        //  Finds unexpected components cheats inject (mod menus, ESP, etc.)
        // ═══════════════════════════════════════════

        private static void ScanSceneMonoBehaviours()
        {
            HashSet<string> safeNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "UnityEngine", "Unity", "Photon", "GorillaTag", "GorillaNetworking",
                "GorillaLocomotion", "VenneChecker", "BepInEx", "TMPro",
                "Oculus", "OVR", "ExitGames",
            };

            MonoBehaviour[] allComponents = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            HashSet<string> flagged = new HashSet<string>();

            foreach (MonoBehaviour mb in allComponents)
            {
                if (mb == null) continue;

                try
                {
                    Type t = mb.GetType();
                    string ns = t.Namespace ?? "";
                    string fullName = t.FullName ?? t.Name;

                    // Skip safe namespaces
                    bool isSafe = string.IsNullOrEmpty(ns);
                    foreach (string safeNs in safeNamespaces)
                    {
                        if (ns.StartsWith(safeNs, StringComparison.OrdinalIgnoreCase))
                        {
                            isSafe = true;
                            break;
                        }
                    }
                    if (isSafe) continue;

                    // Check against known cheat namespaces
                    if (CheatDatabase.IsCheatNamespace(ns) || CheatDatabase.IsCheatNamespace(fullName))
                    {
                        string displayName = CheatDatabase.GetNamespaceDisplayName(ns);
                        if (flagged.Add(displayName))
                        {
                            string msg = $"Cheat MonoBehaviour: {displayName}";
                            _results.Add(msg);
                            Log.Warn($"[SCENE] {msg} (type: {fullName}, object: {mb.gameObject.name})");
                        }
                    }
                }
                catch { }
            }
        }

        // ═══════════════════════════════════════════
        //  7. BepInEx DEPENDENCY ERRORS
        //  Failed cheat plugin loads are still suspicious
        // ═══════════════════════════════════════════

        private static void ScanDependencyErrors()
        {
            try
            {
                Type chainloaderType = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    chainloaderType = asm.GetType("BepInEx.Bootstrap.Chainloader");
                    if (chainloaderType != null) break;
                }
                if (chainloaderType == null) return;

                // DependencyErrors is a List<string>
                PropertyInfo depErrorsProp = chainloaderType.GetProperty("DependencyErrors",
                    BindingFlags.Public | BindingFlags.Static);
                if (depErrorsProp == null)
                {
                    FieldInfo depErrorsField = chainloaderType.GetField("DependencyErrors",
                        BindingFlags.Public | BindingFlags.Static);
                    if (depErrorsField == null) return;

                    object errors = depErrorsField.GetValue(null);
                    if (errors is System.Collections.IList list && list.Count > 0)
                    {
                        foreach (object err in list)
                        {
                            string errStr = err?.ToString() ?? "";
                            Log.Info($"[BEPINEX] Dependency error: {errStr}");
                            // Check if the error mentions known cheat mods
                            foreach (var kvp in KnownCheatGuids)
                            {
                                if (errStr.Contains(kvp.Key))
                                {
                                    _results.Add($"Failed cheat plugin: {kvp.Value}");
                                    break;
                                }
                            }
                        }
                    }
                    return;
                }

                object errorsObj = depErrorsProp.GetValue(null, null);
                if (errorsObj is System.Collections.IList errorList && errorList.Count > 0)
                {
                    foreach (object err in errorList)
                    {
                        string errStr = err?.ToString() ?? "";
                        Log.Info($"[BEPINEX] Dependency error: {errStr}");
                    }
                }
            }
            catch { }
        }
    }
}
