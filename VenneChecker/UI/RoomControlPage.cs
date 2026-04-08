using System;
using System.Reflection;
using GorillaNetworking;
using Photon.Pun;
using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Room Controller — room info, queue/mode selectors, server hop, disconnect, reconnect.
    /// Uses the exact same networking calls as Seralyth/Juul:
    ///   - PhotonNetwork.Disconnect()
    ///   - PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(room, JoinType.Solo)
    ///   - PhotonNetworkController.Instance.AttemptToJoinPublicRoom(trigger, 0)
    /// </summary>
    public class RoomControlPage : BoardPage
    {
        private TextMeshPro _roomNameText;
        private TextMeshPro _playerCountText;

        private BoardButton _queueBtn;
        private BoardButton _modeBtn;

        private int _queueIdx = 0;
        private int _modeIdx = 0;

        private static readonly string[] QueueNames = { "DEFAULT", "COMPETITIVE", "MINIGAMES" };
        private static readonly string[] ModeNames = { "INFECTION", "CASUAL", "HUNT", "PAINTBRAWL" };

        private static string _previousRoomName = "";

        private Action _onBack;

        public RoomControlPage(Shader shader, TMP_FontAsset font, Action onBack)
            : base(null, shader, font)
        {
            _shader = shader;
            _font = font;
            _onBack = onBack;
        }

        public override void Build(Transform parent)
        {
            Root = new GameObject("Page_RoomCtrl");
            Root.transform.SetParent(parent, false);

            float zPanel = ZButton;
            float zText = ZText;

            float yPos = BuildPageHeader(Root.transform, "ROOM CTRL", () => _onBack?.Invoke());
            yPos -= 0.006f;

            _roomNameText = CreateText("RoomName", Root.transform,
                new Vector3(0f, yPos, zText), "ROOM: ---", 2.8f, TextWhite, TextAlignmentOptions.Left);
            yPos -= RowHeight;

            _playerCountText = CreateText("PlayerCount", Root.transform,
                new Vector3(0f, yPos, zText), "PLAYERS: -/-", 2.8f, TextWhite, TextAlignmentOptions.Left);
            yPos -= RowHeight * 0.8f;

            CreateSeparator("Sep2", Root.transform, yPos);
            yPos -= 0.008f;

            float btnW = MenuWidth - Padding * 4;
            float btnH = 0.028f;
            float btnD = ButtonDepth;
            float gap = RowSpacing * 2.5f;

            yPos -= btnH * 0.5f;
            _queueBtn = CreateButton("QueueBtn", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(btnW, btnH, btnD),
                $"QUEUE: {QueueNames[_queueIdx]}", 2.4f,
                OnQueueCycle);
            yPos -= btnH + gap;

            yPos -= btnH * 0.5f;
            _modeBtn = CreateButton("ModeBtn", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(btnW, btnH, btnD),
                $"MODE: {ModeNames[_modeIdx]}", 2.4f,
                OnModeCycle);
            yPos -= btnH + gap;

            CreateSeparator("Sep3", Root.transform, yPos);
            yPos -= 0.008f;

            yPos -= btnH * 0.5f;
            CreateButton("ServerHopBtn", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(btnW, btnH, btnD),
                "SERVER HOP", 2.6f, OnServerHop);
            yPos -= btnH + gap;

            yPos -= btnH * 0.5f;
            CreateButton("DisconnectBtn", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(btnW, btnH, btnD),
                "DISCONNECT", 2.6f, OnDisconnect);
            yPos -= btnH + gap;

            yPos -= btnH * 0.5f;
            CreateButton("ReconnectBtn", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(btnW, btnH, btnD),
                "RECONNECT", 2.6f, OnReconnect);
        }

        public override void Show()
        {
            base.Show();
            RefreshRoomInfo();
        }

        public override void OnUpdate()
        {
            if (Time.frameCount % 60 == 0)
                RefreshRoomInfo();
        }

        private void RefreshRoomInfo()
        {
            try
            {
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                {
                    string roomName = PhotonNetwork.CurrentRoom.Name;
                    if (_roomNameText != null) _roomNameText.text = $"ROOM: {roomName}";

                    if (!string.IsNullOrEmpty(roomName))
                        _previousRoomName = roomName;

                    int count = PhotonNetwork.CurrentRoom.PlayerCount;
                    int max = PhotonNetwork.CurrentRoom.MaxPlayers;
                    if (_playerCountText != null) _playerCountText.text = $"PLAYERS: {count}/{max}";
                }
                else
                {
                    if (_roomNameText != null) _roomNameText.text = "ROOM: Not connected";
                    if (_playerCountText != null) _playerCountText.text = "PLAYERS: -/-";
                }
            }
            catch { }
        }

        private void OnQueueCycle()
        {
            _queueIdx = (_queueIdx + 1) % QueueNames.Length;
            _queueBtn?.SetLabel($"QUEUE: {QueueNames[_queueIdx]}");
        }

        private void OnModeCycle()
        {
            _modeIdx = (_modeIdx + 1) % ModeNames.Length;
            _modeBtn?.SetLabel($"MODE: {ModeNames[_modeIdx]}");
        }

        // ═══════════════════════════════════════════
        //  SERVER HOP
        //  Same as Seralyth: PhotonNetwork.Disconnect() → 5s → AttemptToJoinPublicRoom
        // ═══════════════════════════════════════════

        private void OnServerHop()
        {
            try
            {
                SaveCurrentRoomName();

                if (PhotonNetwork.InRoom)
                {
                    // Step 1: Disconnect
                    PhotonNetwork.Disconnect();
                    Log.Info("[ServerHop] Disconnected, joining new room in 5s...");

                    // Step 2: After 5s, join a random public room (same as Juul JoinRandom)
                    if (DelayedAction.Instance != null)
                    {
                        DelayedAction.Instance.RunAfter(5f, () =>
                        {
                            try { JoinPublicRoom(); }
                            catch (Exception ex) { Log.Error($"[ServerHop] Join failed: {ex.Message}"); }
                        });
                    }
                }
                else
                {
                    // Already disconnected — just join
                    JoinPublicRoom();
                }
            }
            catch (Exception ex) { Log.Error($"Server hop failed: {ex.Message}"); }
        }

        // ═══════════════════════════════════════════
        //  DISCONNECT
        // ═══════════════════════════════════════════

        private void OnDisconnect()
        {
            try
            {
                SaveCurrentRoomName();

                // Try NetworkSystem.Instance.ReturnToSinglePlayer() first (Juul/Saturn approach)
                try
                {
                    Type nsType = FindType("NetworkSystem");
                    if (nsType != null)
                    {
                        object inst = GetStaticInstance(nsType);
                        if (inst != null)
                        {
                            MethodInfo rtsp = nsType.GetMethod("ReturnToSinglePlayer",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (rtsp != null)
                            {
                                rtsp.Invoke(inst, null);
                                Log.Info("Disconnected via NetworkSystem.ReturnToSinglePlayer()");
                                return;
                            }
                        }
                    }
                }
                catch { }

                // Fallback: raw PhotonNetwork.Disconnect()
                PhotonNetwork.Disconnect();
                Log.Info($"Disconnected (saved room: {_previousRoomName})");
            }
            catch (Exception ex) { Log.Error($"Disconnect failed: {ex.Message}"); }
        }

        // ═══════════════════════════════════════════
        //  RECONNECT
        //  Same as Juul AntiReportReconnect: save name → disconnect → join specific room
        //  Same as Seralyth: Disconnect → 5s → AttemptToJoinSpecificRoom
        // ═══════════════════════════════════════════

        private void OnReconnect()
        {
            try
            {
                if (string.IsNullOrEmpty(_previousRoomName))
                {
                    ShowNotification("No previous room saved");
                    return;
                }

                string roomToJoin = _previousRoomName;

                if (PhotonNetwork.InRoom)
                {
                    // Already in a room — disconnect first, then rejoin previous room
                    PhotonNetwork.Disconnect();
                    Log.Info($"[Reconnect] Disconnected, joining '{roomToJoin}' in 5s...");

                    if (DelayedAction.Instance != null)
                    {
                        DelayedAction.Instance.RunAfter(5f, () =>
                        {
                            try { JoinSpecificRoom(roomToJoin); }
                            catch (Exception ex)
                            {
                                Log.Error($"[Reconnect] Join failed: {ex.Message}");
                                ShowNotification($"Could not join {roomToJoin}");
                            }
                        });
                    }
                }
                else
                {
                    // Already disconnected — join directly
                    JoinSpecificRoom(roomToJoin);
                }
            }
            catch (Exception ex) { Log.Error($"Reconnect failed: {ex.Message}"); }
        }

        // ═══════════════════════════════════════════
        //  GT NETWORKING — direct calls, no reflection
        // ═══════════════════════════════════════════

        /// <summary>
        /// Same as Juul's JoinRandom (else branch):
        ///   string text = PhotonNetworkController.Instance.currentJoinTrigger == null
        ///       ? "forest" : PhotonNetworkController.Instance.currentJoinTrigger.networkZone;
        ///   PhotonNetworkController.Instance.AttemptToJoinPublicRoom(
        ///       GorillaComputer.instance.GetJoinTriggerForZone(text), 0);
        /// </summary>
        private void JoinPublicRoom()
        {
            try
            {
                if (PhotonNetworkController.Instance == null)
                {
                    Log.Error("[JoinPublic] PhotonNetworkController.Instance is null");
                    return;
                }

                // Get network zone from current join trigger, default to "forest"
                string zone = "forest";
                try
                {
                    if (PhotonNetworkController.Instance.currentJoinTrigger != null)
                        zone = PhotonNetworkController.Instance.currentJoinTrigger.networkZone;
                }
                catch { }

                // Get the join trigger for that zone
                GorillaNetworkJoinTrigger trigger = GorillaComputer.instance.GetJoinTriggerForZone(zone);
                if (trigger == null)
                {
                    Log.Warn($"[JoinPublic] No join trigger for zone '{zone}', trying all zones...");
                    // Try common zones
                    string[] zones = { "forest", "cave", "canyon", "beach", "mountain", "city", "clouds", "basement" };
                    foreach (string z in zones)
                    {
                        try
                        {
                            trigger = GorillaComputer.instance.GetJoinTriggerForZone(z);
                            if (trigger != null) { zone = z; break; }
                        }
                        catch { }
                    }
                }

                if (trigger != null)
                {
                    PhotonNetworkController.Instance.AttemptToJoinPublicRoom(trigger, JoinType.Solo);
                    Log.Info($"[JoinPublic] AttemptToJoinPublicRoom(zone={zone}) called!");
                }
                else
                {
                    Log.Error("[JoinPublic] No join trigger found at all");
                    ShowNotification("Could not find a room to join");
                }
            }
            catch (Exception ex) { Log.Error($"JoinPublicRoom failed: {ex.Message}"); }
        }

        /// <summary>
        /// Same as Seralyth Console.JoinRoom and Juul Safety.JoinRoom:
        ///   PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(room, JoinType.Solo)
        /// </summary>
        private void JoinSpecificRoom(string roomName)
        {
            try
            {
                if (PhotonNetworkController.Instance == null)
                {
                    Log.Error("[JoinRoom] PhotonNetworkController.Instance is null");
                    ShowNotification("Network controller not available");
                    return;
                }

                PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(roomName, JoinType.Solo);
                Log.Info($"[JoinRoom] AttemptToJoinSpecificRoom('{roomName}', Solo) called!");

                // Check result after a delay
                if (DelayedAction.Instance != null)
                {
                    DelayedAction.Instance.RunAfter2(5f, () =>
                    {
                        if (!PhotonNetwork.InRoom)
                            ShowNotification($"Room \"{roomName}\" full or doesn't exist");
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"JoinSpecificRoom failed: {ex.Message}");
                ShowNotification($"Failed to join {roomName}");
            }
        }

        private void SaveCurrentRoomName()
        {
            try
            {
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                {
                    string name = PhotonNetwork.CurrentRoom.Name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        _previousRoomName = name;
                        Log.Info($"Saved room: {_previousRoomName}");
                    }
                }
            }
            catch { }
        }

        private void ShowNotification(string message)
        {
            if (NotificationManager.Instance != null)
                NotificationManager.Instance.Show(message, new Color(1f, 0.6f, 0.2f, 1f));
        }

        // ═══════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════

        private static object GetStaticInstance(Type type)
        {
            string[] names = { "Instance", "instance", "_instance" };
            foreach (string name in names)
            {
                try
                {
                    PropertyInfo p = type.GetProperty(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (p != null) { object v = p.GetValue(null, null); if (v != null) return v; }
                }
                catch { }
                try
                {
                    FieldInfo f = type.GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null) { object v = f.GetValue(null); if (v != null) return v; }
                }
                catch { }
            }
            return null;
        }

        private Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
