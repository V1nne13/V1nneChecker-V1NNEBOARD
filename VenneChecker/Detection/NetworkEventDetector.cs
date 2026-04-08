using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Monitors network events for behavioral cheat detection.
    /// Detects Seralyth and similar mods that strip CustomProperties but still
    /// leave traces through RPCs, Photon events, and suspicious patterns.
    /// </summary>
    public static class NetworkEventDetector
    {
        // Maps actor number -> set of flags detected for that player
        private static readonly Dictionary<int, HashSet<string>> _flaggedPlayers =
            new Dictionary<int, HashSet<string>>();

        // Track RPC_PlayHandTap abuse (speed 999999f = Seralyth/cheat menu signature)
        private static readonly Dictionary<int, int> _abnormalHandTapCounts =
            new Dictionary<int, int>();

        // RPC frequency tracking per actor — Queue of timestamps
        private static readonly Dictionary<int, Queue<float>> _rpcTimestamps =
            new Dictionary<int, Queue<float>>();

        // Master client change tracking — Queue of timestamps per actor
        private static readonly Dictionary<int, Queue<float>> _masterChangeTimes =
            new Dictionary<int, Queue<float>>();

        // Track suspicious Photon event codes sent by players
        private static readonly HashSet<byte> SuspiciousEventCodes =
            new HashSet<byte> { 69, 200, 201, 202, 204, 207 };

        // RPC flood thresholds
        private const int RpcFloodThreshold = 40;      // RPCs per second
        private const float RpcFloodWindow = 1f;        // 1 second window
        private const int RpcFloodSustained = 3;        // sustained for 3 checks
        private const int MasterChangeThreshold = 3;    // 3 master changes
        private const float MasterChangeWindow = 5f;    // in 5 seconds

        private static readonly Dictionary<int, int> _rpcFloodCounts =
            new Dictionary<int, int>();

        private static Harmony _harmony;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                _harmony = new Harmony("com.venne.vennechecker.detector");
                ApplyPatches();
                _initialized = true;
                Log.Info("NetworkEventDetector initialized");
            }
            catch (Exception ex)
            {
                Log.Error($"NetworkEventDetector.Initialize failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all behavioral flags detected for a player by actor number.
        /// </summary>
        public static List<string> GetFlags(int actorNumber)
        {
            if (_flaggedPlayers.TryGetValue(actorNumber, out HashSet<string> flags))
                return new List<string>(flags);
            return new List<string>();
        }

        /// <summary>
        /// Checks if a player has any behavioral cheat flags.
        /// </summary>
        public static bool HasFlags(int actorNumber)
        {
            return _flaggedPlayers.ContainsKey(actorNumber) && _flaggedPlayers[actorNumber].Count > 0;
        }

        /// <summary>
        /// Flags a player with a detection reason.
        /// </summary>
        public static void FlagPlayer(int actorNumber, string reason)
        {
            if (!_flaggedPlayers.ContainsKey(actorNumber))
                _flaggedPlayers[actorNumber] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_flaggedPlayers[actorNumber].Add(reason))
                Log.Warn($"[DETECTION] Actor {actorNumber} flagged: {reason}");
        }

        /// <summary>
        /// Clears flags for a player (e.g., when they leave the room).
        /// </summary>
        public static void ClearFlags(int actorNumber)
        {
            _flaggedPlayers.Remove(actorNumber);
            _abnormalHandTapCounts.Remove(actorNumber);
        }

        /// <summary>
        /// Clears all tracked data (e.g., on room change).
        /// </summary>
        public static void ClearAll()
        {
            _flaggedPlayers.Clear();
            _abnormalHandTapCounts.Clear();
            _rpcTimestamps.Clear();
            _masterChangeTimes.Clear();
            _rpcFloodCounts.Clear();
        }

        // ═══════════════════════════════════════════════════
        //  HARMONY PATCHES
        // ═══════════════════════════════════════════════════

        private static void ApplyPatches()
        {
            // Patch 1: RPC_PlayHandTap — detect abnormal handTapSpeed (999999f = cheat menu)
            try
            {
                PatchHandTap();
            }
            catch (Exception ex)
            {
                Log.Warn($"HandTap patch failed: {ex.Message}");
            }

            // Patch 2: OnEvent — detect suspicious Photon event codes
            try
            {
                PatchPhotonEvents();
            }
            catch (Exception ex)
            {
                Log.Warn($"Photon event patch failed: {ex.Message}");
            }
        }

        private static void PatchHandTap()
        {
            // VRRig has RPC handler for hand taps — look for the method
            // In GT, the hand tap RPC handler is on VRRigSerializer or VRRig
            Type[] searchTypes = new Type[]
            {
                typeof(VRRig),
                // Also check VRRigSerializer if it exists
            };

            // Try to find VRRigSerializer type
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type serializer = asm.GetType("VRRigSerializer");
                if (serializer != null)
                {
                    TryPatchHandTapOnType(serializer);
                    break;
                }
            }

            // Also try VRRig itself
            TryPatchHandTapOnType(typeof(VRRig));
        }

        private static void TryPatchHandTapOnType(Type type)
        {
            // Look for OnHandTapRPC, RPC_PlayHandTap, or OnHandTapRPCShared
            string[] methodNames = { "OnHandTapRPCShared", "OnHandTapRPC", "RPC_PlayHandTap", "PlayHandTap" };

            foreach (string name in methodNames)
            {
                MethodInfo method = type.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (method != null)
                {
                    try
                    {
                        MethodInfo prefix = typeof(NetworkEventDetector).GetMethod(
                            nameof(HandTapPrefix), BindingFlags.Static | BindingFlags.NonPublic);

                        _harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                        Log.Info($"Patched {type.Name}.{name} for hand tap detection");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Failed to patch {type.Name}.{name}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Harmony prefix for hand tap RPC. Detects abnormal speed values.
        /// </summary>
        private static void HandTapPrefix(object __instance, object[] __args)
        {
            try
            {
                if (__args == null || __args.Length == 0) return;

                // Look for a float parameter that's abnormally high (999999f)
                foreach (object arg in __args)
                {
                    if (arg is float speed && speed > 10000f)
                    {
                        // This is a cheat-injected hand tap
                        int actorNum = GetActorFromRig(__instance);
                        if (actorNum > 0)
                        {
                            if (!_abnormalHandTapCounts.ContainsKey(actorNum))
                                _abnormalHandTapCounts[actorNum] = 0;

                            _abnormalHandTapCounts[actorNum]++;

                            // Flag after 3 occurrences to reduce false positives
                            if (_abnormalHandTapCounts[actorNum] >= 3)
                            {
                                FlagPlayer(actorNum, "Abnormal HandTap (Seralyth/Cheat Menu)");
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        private static void PatchPhotonEvents()
        {
            // Try to patch LoadBalancingClient.OnEvent or PhotonNetwork.OnEvent
            // This lets us see raw Photon events from other players
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Try Photon.Realtime.LoadBalancingClient
                Type lbcType = asm.GetType("Photon.Realtime.LoadBalancingClient");
                if (lbcType != null)
                {
                    MethodInfo onEvent = lbcType.GetMethod("OnEvent",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (onEvent != null)
                    {
                        try
                        {
                            MethodInfo prefix = typeof(NetworkEventDetector).GetMethod(
                                nameof(OnEventPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                            _harmony.Patch(onEvent, prefix: new HarmonyMethod(prefix));
                            Log.Info("Patched LoadBalancingClient.OnEvent for event detection");
                            return;
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Failed to patch OnEvent: {ex.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Harmony prefix for Photon OnEvent. Detects suspicious event codes.
        /// </summary>
        private static void OnEventPrefix(object __0)
        {
            try
            {
                if (__0 == null) return;

                Type eventType = __0.GetType();

                // Get event code
                PropertyInfo codeProp = eventType.GetProperty("Code");
                if (codeProp == null) return;

                object codeObj = codeProp.GetValue(__0, null);
                if (codeObj == null) return;

                byte code = Convert.ToByte(codeObj);

                // Get sender actor number
                PropertyInfo senderProp = eventType.GetProperty("Sender");
                int sender = 0;
                if (senderProp != null)
                {
                    object senderObj = senderProp.GetValue(__0, null);
                    if (senderObj != null) sender = Convert.ToInt32(senderObj);
                }

                if (sender <= 0) return;

                // Don't flag ourselves
                try
                {
                    if (Photon.Pun.PhotonNetwork.LocalPlayer != null &&
                        sender == Photon.Pun.PhotonNetwork.LocalPlayer.ActorNumber)
                        return;
                }
                catch { }

                // ── RPC frequency tracking (all events count) ──
                TrackRpcFrequency(sender);

                // ── Master client change detection (event 253) ──
                if (code == 253)
                {
                    TrackMasterChange(sender);
                }

                // ── Suspicious event code detection ──
                if (!SuspiciousEventCodes.Contains(code)) return;

                string reason;
                switch (code)
                {
                    case 69:
                        reason = "Platform Spawn (Event 69)";
                        break;
                    case 200:
                        reason = "Instantiate Exploit (Event 200)";
                        break;
                    case 201:
                        reason = "Destroy Exploit (Event 201)";
                        break;
                    case 202:
                        reason = "Kick/Mute Exploit (Event 202)";
                        break;
                    case 204:
                        reason = "Ghost/Destroy Rig (Event 204)";
                        break;
                    case 207:
                        reason = "Destroy Player (Event 207)";
                        break;
                    default:
                        reason = $"Suspicious Event ({code})";
                        break;
                }

                FlagPlayer(sender, reason);
            }
            catch { }
        }

        /// <summary>
        /// Tracks RPC frequency per actor. Flags if >40 RPCs/sec sustained.
        /// </summary>
        private static void TrackRpcFrequency(int actorNumber)
        {
            float now = Time.time;

            if (!_rpcTimestamps.ContainsKey(actorNumber))
                _rpcTimestamps[actorNumber] = new Queue<float>();

            var timestamps = _rpcTimestamps[actorNumber];
            timestamps.Enqueue(now);

            // Remove timestamps older than the window
            while (timestamps.Count > 0 && now - timestamps.Peek() > RpcFloodWindow)
                timestamps.Dequeue();

            int rpcCount = timestamps.Count;

            if (rpcCount > RpcFloodThreshold)
            {
                if (!_rpcFloodCounts.ContainsKey(actorNumber))
                    _rpcFloodCounts[actorNumber] = 0;

                _rpcFloodCounts[actorNumber]++;

                if (_rpcFloodCounts[actorNumber] >= RpcFloodSustained)
                {
                    FlagPlayer(actorNumber, $"RPC Flood ({rpcCount}/sec)");
                }
            }
            else
            {
                // Reset flood count if under threshold
                _rpcFloodCounts.Remove(actorNumber);
            }
        }

        /// <summary>
        /// Tracks master client change events. Flags rapid changes (Seralyth exploit).
        /// </summary>
        private static void TrackMasterChange(int actorNumber)
        {
            float now = Time.time;

            if (!_masterChangeTimes.ContainsKey(actorNumber))
                _masterChangeTimes[actorNumber] = new Queue<float>();

            var times = _masterChangeTimes[actorNumber];
            times.Enqueue(now);

            // Remove old entries
            while (times.Count > 0 && now - times.Peek() > MasterChangeWindow)
                times.Dequeue();

            if (times.Count >= MasterChangeThreshold)
            {
                FlagPlayer(actorNumber, "Master Client Exploit");
                Log.Warn($"[RPC] Actor {actorNumber}: rapid master client changes ({times.Count} in {MasterChangeWindow}s)");
            }
        }

        // ═══════════════════════════════════════════════════
        //  HELPER: Get actor number from a VRRig instance
        // ═══════════════════════════════════════════════════

        private static int GetActorFromRig(object rigOrSerializer)
        {
            try
            {
                // Try to get the VRRig from the object
                VRRig rig = rigOrSerializer as VRRig;

                if (rig == null)
                {
                    // Maybe it's a serializer — try to get the rig from it
                    Type t = rigOrSerializer.GetType();
                    FieldInfo rigField = t.GetField("rig",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (rigField != null)
                        rig = rigField.GetValue(rigOrSerializer) as VRRig;
                }

                if (rig == null) return -1;

                // Get Creator.ActorNumber
                PropertyInfo creatorProp = typeof(VRRig).GetProperty("Creator",
                    BindingFlags.Public | BindingFlags.Instance);
                if (creatorProp == null) return -1;

                object creator = creatorProp.GetValue(rig, null);
                if (creator == null) return -1;

                PropertyInfo actorProp = creator.GetType().GetProperty("ActorNumber");
                if (actorProp == null) return -1;

                return (int)actorProp.GetValue(creator, null);
            }
            catch
            {
                return -1;
            }
        }

        public static void Shutdown()
        {
            try
            {
                _harmony?.UnpatchSelf();
                _flaggedPlayers.Clear();
                _abnormalHandTapCounts.Clear();
                _initialized = false;
            }
            catch { }
        }
    }
}
