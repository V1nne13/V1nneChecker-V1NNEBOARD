using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Auto-scans all players when joining a room.
    /// If any flagged mods/cheats are detected, sends a notification.
    /// </summary>
    public class RoomScanner : MonoBehaviour
    {
        public static RoomScanner Instance { get; private set; }

        private string _lastRoomName = "";
        private float _scanDelay;
        private bool _pendingScan;
        private readonly HashSet<int> _scannedActors = new HashSet<int>();

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            // Check for room change every second
            if (Time.frameCount % 60 != 0) return;

            try
            {
                string currentRoom = GetCurrentRoomName();

                if (currentRoom != _lastRoomName)
                {
                    _lastRoomName = currentRoom;

                    if (!string.IsNullOrEmpty(currentRoom))
                    {
                        // Joined a new room — wait a bit for all players to load
                        _pendingScan = true;
                        _scanDelay = 3f;
                        _scannedActors.Clear();
                        Log.Info($"[RoomScanner] Joined room: {currentRoom}, scanning in 3s...");
                    }
                }

                if (_pendingScan)
                {
                    _scanDelay -= Time.deltaTime * 60f; // compensate for frame skip
                    if (_scanDelay <= 0f)
                    {
                        _pendingScan = false;
                        ScanAllPlayers();
                    }
                }

                // Also periodically check for new players joining (every 10s)
                if (!_pendingScan && !string.IsNullOrEmpty(_lastRoomName) && Time.frameCount % 600 == 0)
                {
                    ScanAllPlayers();
                }
            }
            catch { }
        }

        private void ScanAllPlayers()
        {
            if (PlayerScanner.Instance == null || NotificationManager.Instance == null) return;

            try
            {
                VRRig[] allRigs = UnityEngine.Object.FindObjectsByType<VRRig>(FindObjectsSortMode.None);

                foreach (VRRig rig in allRigs)
                {
                    if (rig == null) continue;

                    try
                    {
                        ScanSinglePlayer(rig);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[RoomScanner] Scan failed for rig: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[RoomScanner] ScanAllPlayers failed: {ex.Message}");
            }
        }

        private void ScanSinglePlayer(VRRig rig)
        {
            // Get actor number to avoid re-scanning
            int actorNum = GetActorNumber(rig);
            if (actorNum > 0 && _scannedActors.Contains(actorNum))
                return;

            // Temporarily set cooldown to 0 so we can scan rapid-fire
            PlayerInfo info = ForceScan(rig);
            if (info == null) return;

            if (actorNum > 0)
                _scannedActors.Add(actorNum);

            // Skip local player
            if (info.IsLocal) return;

            // Check for flagged mods
            List<string> flagged = new List<string>();
            foreach (var mod in info.DetectedMods)
            {
                if (mod.IsCheat)
                    flagged.Add(mod.Name);
            }

            // Also check behavior flags
            if (info.BehaviorFlags != null)
            {
                foreach (string flag in info.BehaviorFlags)
                    flagged.Add(flag);
            }

            // Also check movement tracker directly (may have flags from before scan)
            if (MovementTracker.Instance != null && actorNum > 0)
            {
                List<string> moveFlags = MovementTracker.Instance.GetFlags(actorNum);
                foreach (string f in moveFlags)
                {
                    if (!flagged.Contains(f))
                        flagged.Add(f);
                }
            }

            if (flagged.Count > 0)
            {
                NotificationManager.Instance.ShowCheatAlert(info.PlayerName, flagged);

                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlayScanComplete();
            }
        }

        /// <summary>
        /// Scans a player without respecting the cooldown (for batch scanning).
        /// </summary>
        private PlayerInfo ForceScan(VRRig rig)
        {
            if (PlayerScanner.Instance == null) return null;

            // Use reflection to bypass cooldown
            var scannerType = typeof(PlayerScanner);
            var cooldownField = scannerType.GetField("_scanCooldown",
                BindingFlags.NonPublic | BindingFlags.Instance);

            float oldCooldown = 0f;
            if (cooldownField != null)
            {
                oldCooldown = (float)cooldownField.GetValue(PlayerScanner.Instance);
                cooldownField.SetValue(PlayerScanner.Instance, 0f);
            }

            PlayerInfo info = PlayerScanner.Instance.ScanPlayer(rig);

            // Don't restore — we want rapid scanning
            if (cooldownField != null)
                cooldownField.SetValue(PlayerScanner.Instance, 0f);

            return info;
        }

        private int GetActorNumber(VRRig rig)
        {
            try
            {
                Type rigType = rig.GetType();
                PropertyInfo creatorProp = rigType.GetProperty("Creator",
                    BindingFlags.Public | BindingFlags.Instance);
                if (creatorProp == null) return -1;

                object creator = creatorProp.GetValue(rig, null);
                if (creator == null) return -1;

                PropertyInfo actorProp = creator.GetType().GetProperty("ActorNumber");
                if (actorProp == null) return -1;

                return (int)actorProp.GetValue(creator, null);
            }
            catch { return -1; }
        }

        private string GetCurrentRoomName()
        {
            try
            {
                Type pnType = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    pnType = asm.GetType("Photon.Pun.PhotonNetwork");
                    if (pnType != null) break;
                }
                if (pnType == null) return "";

                PropertyInfo roomProp = pnType.GetProperty("CurrentRoom",
                    BindingFlags.Public | BindingFlags.Static);
                if (roomProp == null) return "";

                object room = roomProp.GetValue(null, null);
                if (room == null) return "";

                PropertyInfo nameProp = room.GetType().GetProperty("Name");
                return nameProp?.GetValue(room, null)?.ToString() ?? "";
            }
            catch { return ""; }
        }
    }
}
