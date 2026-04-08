using System;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Tracks remote players' positions over time to detect impossible movement:
    /// speed hacks, fly hacks, and teleporting.
    /// Runs as a MonoBehaviour on the VenneChecker_Manager object.
    /// </summary>
    public class MovementTracker : MonoBehaviour
    {
        public static MovementTracker Instance { get; private set; }

        // Detection thresholds
        private const float SpeedThreshold = 25f;       // m/s — normal GT max ~15 m/s
        private const float TeleportThreshold = 40f;     // units in one sample = teleport
        private const float FlyYThreshold = 5f;          // 5m Y gain over 3 samples with minimal X/Z
        private const float FlyXZThreshold = 2f;         // less than 2m horizontal = vertical-only
        private const int FlagThreshold = 3;             // 3 suspicious readings before flagging
        private const int MaxSamples = 6;
        private const float SampleInterval = 0.5f;       // seconds between checks
        private const float FlagExpireTime = 10f;         // seconds before suspicious count resets

        private readonly Dictionary<int, PlayerMoveData> _tracked = new Dictionary<int, PlayerMoveData>();
        private readonly Dictionary<int, List<string>> _flags = new Dictionary<int, List<string>>();
        private readonly HashSet<int> _notified = new HashSet<int>();
        private float _lastCheckTime;
        private string _lastRoomName;

        private class PlayerMoveData
        {
            public Vector3 LastPosition;
            public float LastCheckTime;
            public Queue<float> SpeedSamples = new Queue<float>();
            public Queue<float> YDeltas = new Queue<float>();
            public Queue<float> XZDeltas = new Queue<float>();
            public int SuspiciousCount;
            public float FirstSuspiciousTime;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            // Only check every ~SampleInterval seconds
            if (Time.time - _lastCheckTime < SampleInterval) return;
            _lastCheckTime = Time.time;

            // Clear on room change
            try
            {
                string currentRoom = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null
                    ? PhotonNetwork.CurrentRoom.Name : "";
                if (currentRoom != _lastRoomName)
                {
                    _lastRoomName = currentRoom;
                    _tracked.Clear();
                    _flags.Clear();
                    _notified.Clear();
                }
            }
            catch { }

            // Track all remote VRRigs
            try
            {
                VRRig[] rigs = FindObjectsByType<VRRig>(FindObjectsSortMode.None);
                foreach (VRRig rig in rigs)
                {
                    if (rig == null) continue;
                    try { TrackRig(rig); }
                    catch { }
                }
            }
            catch { }
        }

        private void TrackRig(VRRig rig)
        {
            // Get actor number
            int actorNumber = GetActorNumber(rig);
            if (actorNumber <= 0) return;

            // Skip local player
            if (IsLocal(rig)) return;

            Vector3 currentPos = rig.transform.position;
            float now = Time.time;

            if (!_tracked.TryGetValue(actorNumber, out PlayerMoveData data))
            {
                data = new PlayerMoveData
                {
                    LastPosition = currentPos,
                    LastCheckTime = now
                };
                _tracked[actorNumber] = data;
                return; // First sample — just record position
            }

            float timeDelta = now - data.LastCheckTime;
            if (timeDelta < 0.1f) return; // Too soon

            float distance = Vector3.Distance(currentPos, data.LastPosition);
            float speed = distance / timeDelta;
            float yDelta = currentPos.y - data.LastPosition.y;
            float xzDist = Vector2.Distance(
                new Vector2(currentPos.x, currentPos.z),
                new Vector2(data.LastPosition.x, data.LastPosition.z));

            // Record samples
            data.SpeedSamples.Enqueue(speed);
            data.YDeltas.Enqueue(yDelta);
            data.XZDeltas.Enqueue(xzDist);
            while (data.SpeedSamples.Count > MaxSamples) data.SpeedSamples.Dequeue();
            while (data.YDeltas.Count > MaxSamples) data.YDeltas.Dequeue();
            while (data.XZDeltas.Count > MaxSamples) data.XZDeltas.Dequeue();

            data.LastPosition = currentPos;
            data.LastCheckTime = now;

            // Reset suspicious count if too old
            if (data.SuspiciousCount > 0 && now - data.FirstSuspiciousTime > FlagExpireTime)
                data.SuspiciousCount = 0;

            // Check for speed/teleport
            bool suspicious = false;

            if (speed > TeleportThreshold)
            {
                suspicious = true;
                Log.Info($"[MOVEMENT] Actor {actorNumber}: teleport detected ({distance:F1}m in {timeDelta:F2}s)");
            }
            else if (speed > SpeedThreshold)
            {
                suspicious = true;
                Log.Info($"[MOVEMENT] Actor {actorNumber}: high speed ({speed:F1} m/s)");
            }

            // Check for fly hack: consistent Y gain without horizontal movement
            if (data.YDeltas.Count >= 3)
            {
                float totalY = 0f;
                float totalXZ = 0f;
                foreach (float y in data.YDeltas) totalY += y;
                foreach (float xz in data.XZDeltas) totalXZ += xz;

                if (totalY > FlyYThreshold && totalXZ < FlyXZThreshold)
                {
                    suspicious = true;
                    Log.Info($"[MOVEMENT] Actor {actorNumber}: fly hack suspected (Y+{totalY:F1}m, XZ={totalXZ:F1}m)");
                }
            }

            if (suspicious)
            {
                if (data.SuspiciousCount == 0)
                    data.FirstSuspiciousTime = now;
                data.SuspiciousCount++;

                if (data.SuspiciousCount >= FlagThreshold)
                {
                    // Build flags
                    var flags = new List<string>();

                    // Determine what kind of movement anomaly
                    float avgSpeed = 0f;
                    foreach (float s in data.SpeedSamples) avgSpeed += s;
                    avgSpeed /= data.SpeedSamples.Count;

                    if (avgSpeed > TeleportThreshold)
                        flags.Add($"Teleport ({data.SuspiciousCount}x)");
                    else if (avgSpeed > SpeedThreshold)
                        flags.Add($"Speed Hack ({data.SuspiciousCount}x)");

                    // Check fly
                    float totalYCheck = 0f;
                    float totalXZCheck = 0f;
                    foreach (float y in data.YDeltas) totalYCheck += y;
                    foreach (float xz in data.XZDeltas) totalXZCheck += xz;
                    if (totalYCheck > FlyYThreshold && totalXZCheck < FlyXZThreshold)
                        flags.Add("Fly Hack");

                    if (flags.Count == 0)
                        flags.Add($"Suspicious Movement ({data.SuspiciousCount}x)");

                    _flags[actorNumber] = flags;

                    // Notify once per player per room
                    if (!_notified.Contains(actorNumber))
                    {
                        _notified.Add(actorNumber);
                        string playerName = GetPlayerName(actorNumber);
                        string flagStr = string.Join(", ", flags);
                        if (NotificationManager.Instance != null)
                            NotificationManager.Instance.Show(
                                $"\u26A0 {playerName}: {flagStr}",
                                new Color(1f, 0.5f, 0.1f, 1f));
                        Log.Warn($"[MOVEMENT] FLAGGED actor {actorNumber} ({playerName}): {flagStr}");
                    }
                }
            }
        }

        /// <summary>Gets movement-related flags for a player.</summary>
        public List<string> GetFlags(int actorNumber)
        {
            if (_flags.TryGetValue(actorNumber, out List<string> flags))
                return new List<string>(flags);
            return new List<string>();
        }

        /// <summary>Gets a suspicion level 0-1 for a player.</summary>
        public float GetSuspicionLevel(int actorNumber)
        {
            if (!_tracked.TryGetValue(actorNumber, out PlayerMoveData data))
                return 0f;
            return Mathf.Clamp01(data.SuspiciousCount / (float)FlagThreshold);
        }

        private int GetActorNumber(VRRig rig)
        {
            try
            {
                PropertyInfo creatorProp = typeof(VRRig).GetProperty("Creator",
                    BindingFlags.Public | BindingFlags.Instance);
                if (creatorProp == null) return -1;

                object creator = creatorProp.GetValue(rig, null);
                if (creator == null) return -1;

                PropertyInfo actorProp = creator.GetType().GetProperty("ActorNumber");
                if (actorProp != null) return (int)actorProp.GetValue(creator, null);
            }
            catch { }
            return -1;
        }

        private bool IsLocal(VRRig rig)
        {
            try
            {
                PropertyInfo creatorProp = typeof(VRRig).GetProperty("Creator",
                    BindingFlags.Public | BindingFlags.Instance);
                if (creatorProp == null) return false;

                object creator = creatorProp.GetValue(rig, null);
                if (creator == null) return false;

                PropertyInfo localProp = creator.GetType().GetProperty("IsLocal");
                if (localProp != null) return (bool)localProp.GetValue(creator, null);
            }
            catch { }
            return false;
        }

        private string GetPlayerName(int actorNumber)
        {
            try
            {
                VRRig[] rigs = FindObjectsByType<VRRig>(FindObjectsSortMode.None);
                foreach (VRRig rig in rigs)
                {
                    if (rig == null) continue;
                    if (GetActorNumber(rig) == actorNumber)
                    {
                        PropertyInfo creatorProp = typeof(VRRig).GetProperty("Creator",
                            BindingFlags.Public | BindingFlags.Instance);
                        object creator = creatorProp?.GetValue(rig, null);
                        if (creator != null)
                        {
                            PropertyInfo nameProp = creator.GetType().GetProperty("NickName");
                            if (nameProp != null) return nameProp.GetValue(creator, null)?.ToString() ?? "Unknown";
                        }
                    }
                }
            }
            catch { }
            return $"Actor#{actorNumber}";
        }
    }
}
