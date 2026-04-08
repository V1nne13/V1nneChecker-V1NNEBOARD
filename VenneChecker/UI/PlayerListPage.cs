using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Player List page — all room players with FPS.
    /// Tap name to select, then Scan / Mute / Report (Cheating, Toxicity, Hate Speech).
    /// Reports use GT's actual report system via PlayFab cloud scripts.
    /// </summary>
    public class PlayerListPage : BoardPage
    {
        private const int MaxVisiblePlayers = 6;

        private Action _onBack;
        private Action<PlayerInfo> _onPlayerScanned;

        private BoardButton[] _playerButtons;
        private BoardButton _scanBtn;
        private BoardButton _muteBtn;
        private TextMeshPro _selectedLabel;

        private List<PlayerEntry> _players = new List<PlayerEntry>();
        private int _selectedIndex = -1;
        private int _scrollOffset = 0;

        private class PlayerEntry
        {
            public string Name;
            public int FPS;
            public int ActorNumber;
            public VRRig Rig;
            public object PhotonPlayer;
            public bool IsMuted;
            public bool IsLocal;
        }

        public PlayerListPage(Shader shader, TMP_FontAsset font, Action onBack, Action<PlayerInfo> onPlayerScanned)
            : base(null, shader, font)
        {
            _shader = shader;
            _font = font;
            _onBack = onBack;
            _onPlayerScanned = onPlayerScanned;
        }

        public override void Build(Transform parent)
        {
            Root = new GameObject("Page_PlayerList");
            Root.transform.SetParent(parent, false);

            float zPanel = ZButton;
            float zText = ZText;

            float yPos = BuildPageHeader(Root.transform, "PLAYERS", () => _onBack?.Invoke());
            yPos -= 0.004f;

            // ── Player entries ──
            float entryH = 0.024f;
            float entryW = MenuWidth - Padding * 4;

            _playerButtons = new BoardButton[MaxVisiblePlayers];

            for (int i = 0; i < MaxVisiblePlayers; i++)
            {
                int idx = i;
                yPos -= entryH * 0.5f;

                _playerButtons[i] = CreateButton($"Player_{i}", Root.transform,
                    new Vector3(0f, yPos, zPanel),
                    new Vector3(entryW, entryH, 0.006f),
                    "---", 2.2f,
                    () => OnPlayerSelected(idx));

                yPos -= entryH * 0.5f + RowSpacing;
            }

            yPos -= RowSpacing;
            CreateSeparator("Sep2", Root.transform, yPos);
            yPos -= 0.005f;

            // ── Selected label ──
            _selectedLabel = CreateText("SelectedLabel", Root.transform,
                new Vector3(0f, yPos, zText),
                "Tap a player to select", 2.2f, TextDim, TextAlignmentOptions.Center);
            yPos -= RowHeight * 0.7f;

            // ── SCAN + MUTE buttons ──
            float actionW = (entryW - 0.01f) * 0.5f;
            float actionH = 0.024f;

            yPos -= actionH * 0.5f;
            _scanBtn = CreateButton("ScanBtn", Root.transform,
                new Vector3(-actionW * 0.5f - 0.005f, yPos, zPanel),
                new Vector3(actionW, actionH, 0.006f),
                "SCAN", 2.4f, OnScan);

            _muteBtn = CreateButton("MuteBtn", Root.transform,
                new Vector3(actionW * 0.5f + 0.005f, yPos, zPanel),
                new Vector3(actionW, actionH, 0.006f),
                "MUTE", 2.4f, OnMute);
            yPos -= actionH + RowSpacing * 2;

            // ── REPORT header ──
            CreateText("ReportLabel", Root.transform,
                new Vector3(0f, yPos, zText),
                "REPORT:", 2.2f, CheatRed, TextAlignmentOptions.Center);
            yPos -= RowHeight * 0.65f;

            // ── 3 report buttons ──
            float reportW = (entryW - 0.014f) / 3f;
            float reportH = 0.022f;
            yPos -= reportH * 0.5f;

            float startX = -(entryW * 0.5f) + reportW * 0.5f;

            CreateButton("ReportCheat", Root.transform,
                new Vector3(startX, yPos, zPanel),
                new Vector3(reportW, reportH, 0.006f),
                "CHEAT", 1.9f, () => OnReport("CHEATING"));

            CreateButton("ReportToxic", Root.transform,
                new Vector3(startX + reportW + 0.007f, yPos, zPanel),
                new Vector3(reportW, reportH, 0.006f),
                "TOXIC", 1.9f, () => OnReport("TOXICITY"));

            CreateButton("ReportHate", Root.transform,
                new Vector3(startX + (reportW + 0.007f) * 2f, yPos, zPanel),
                new Vector3(reportW, reportH, 0.006f),
                "HATE", 1.9f, () => OnReport("HATE SPEECH"));
        }

        public override void Show()
        {
            base.Show();
            _selectedIndex = -1;
            RefreshPlayerList();
        }

        public override void OnUpdate()
        {
            if (Time.frameCount % 120 == 0)
                RefreshPlayerList();
        }

        private void RefreshPlayerList()
        {
            _players.Clear();

            try
            {
                VRRig[] allRigs = UnityEngine.Object.FindObjectsByType<VRRig>(FindObjectsSortMode.None);
                foreach (VRRig rig in allRigs)
                {
                    if (rig == null) continue;
                    var entry = new PlayerEntry { Rig = rig };

                    try
                    {
                        PropertyInfo creatorProp = typeof(VRRig).GetProperty("Creator",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (creatorProp != null)
                        {
                            object creator = creatorProp.GetValue(rig, null);
                            if (creator != null)
                            {
                                Type ct = creator.GetType();

                                PropertyInfo nameProp = ct.GetProperty("NickName");
                                if (nameProp != null) entry.Name = nameProp.GetValue(creator, null)?.ToString();

                                PropertyInfo actorProp = ct.GetProperty("ActorNumber");
                                if (actorProp != null) entry.ActorNumber = (int)actorProp.GetValue(creator, null);

                                PropertyInfo playerRefProp = ct.GetProperty("PlayerRef",
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (playerRefProp != null) entry.PhotonPlayer = playerRefProp.GetValue(creator, null);

                                PropertyInfo localProp = ct.GetProperty("IsLocal");
                                if (localProp != null && localProp.GetValue(creator, null) is bool isLocal)
                                    entry.IsLocal = isLocal;
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        FieldInfo fpsField = typeof(VRRig).GetField("fps",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fpsField != null && fpsField.GetValue(rig) is int fps) entry.FPS = fps;
                    }
                    catch { }

                    if (string.IsNullOrEmpty(entry.Name)) entry.Name = rig.gameObject.name;
                    _players.Add(entry);
                }
            }
            catch (Exception ex) { Log.Warn($"RefreshPlayerList: {ex.Message}"); }

            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            for (int i = 0; i < MaxVisiblePlayers; i++)
            {
                int pi = i + _scrollOffset;
                if (pi < _players.Count)
                {
                    var p = _players[pi];
                    string you = p.IsLocal ? " (YOU)" : "";
                    string muted = p.IsMuted ? " [M]" : "";
                    _playerButtons[i].SetLabel($"{p.Name}{you}{muted}  {p.FPS}FPS");

                    _playerButtons[i].SetColor(pi == _selectedIndex ? ButtonHover : ButtonFace);
                }
                else
                {
                    _playerButtons[i].SetLabel("---");
                    _playerButtons[i].SetColor(ButtonFace);
                }
            }

            if (_selectedIndex >= 0 && _selectedIndex < _players.Count)
            {
                if (_selectedLabel != null) _selectedLabel.text = $"SELECTED: {_players[_selectedIndex].Name}";
                if (_muteBtn != null) _muteBtn.SetLabel(_players[_selectedIndex].IsMuted ? "UNMUTE" : "MUTE");
            }
            else
            {
                if (_selectedLabel != null) _selectedLabel.text = "Tap a player to select";
            }
        }

        private void OnPlayerSelected(int visibleIdx)
        {
            int pi = visibleIdx + _scrollOffset;
            if (pi >= _players.Count) return;
            _selectedIndex = pi;
            UpdateDisplay();
        }

        private void OnScan()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _players.Count) return;
            var entry = _players[_selectedIndex];
            if (entry.Rig == null || PlayerScanner.Instance == null) return;

            PlayerInfo info = PlayerScanner.Instance.ScanPlayer(entry.Rig);
            if (info != null) _onPlayerScanned?.Invoke(info);
        }

        private void OnMute()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _players.Count) return;
            var entry = _players[_selectedIndex];
            entry.IsMuted = !entry.IsMuted;

            try
            {
                if (entry.Rig != null)
                {
                    // Mute all AudioSources on the rig and its children
                    AudioSource[] sources = entry.Rig.GetComponentsInChildren<AudioSource>(true);
                    foreach (AudioSource src in sources)
                        if (src != null) src.mute = entry.IsMuted;

                    // Disable Photon Voice Speaker components (handles voice chat)
                    // Check multiple type names since Photon Voice versions differ
                    Component[] comps = entry.Rig.GetComponentsInChildren<Component>(true);
                    foreach (Component c in comps)
                    {
                        if (c == null) continue;
                        string typeName = c.GetType().Name;

                        // Photon Voice Speaker component
                        if (typeName == "Speaker" || typeName == "PhotonVoiceView" ||
                            typeName == "SpeakerAdapter")
                        {
                            if (c is MonoBehaviour mb)
                                mb.enabled = !entry.IsMuted;
                        }
                    }

                    // Also check the rig's parent and siblings for voice components
                    Transform rigParent = entry.Rig.transform.parent;
                    if (rigParent != null)
                    {
                        Component[] parentComps = rigParent.GetComponentsInChildren<Component>(true);
                        foreach (Component c in parentComps)
                        {
                            if (c == null) continue;
                            string typeName = c.GetType().Name;
                            if ((typeName == "Speaker" || typeName == "PhotonVoiceView") && c is MonoBehaviour mb)
                            {
                                // Only disable if it belongs to the same player
                                VRRig parentRig = c.GetComponentInParent<VRRig>();
                                if (parentRig == entry.Rig)
                                    mb.enabled = !entry.IsMuted;
                            }
                        }
                    }

                    // Set volume to 0 as additional safeguard
                    foreach (AudioSource src in sources)
                    {
                        if (src != null)
                            src.volume = entry.IsMuted ? 0f : 1f;
                    }
                }

                string status = entry.IsMuted ? "Muted" : "Unmuted";
                Log.Info($"{status}: {entry.Name}");

                if (NotificationManager.Instance != null)
                    NotificationManager.Instance.Show($"{status} {entry.Name}",
                        entry.IsMuted ? new Color(1f, 0.5f, 0.2f, 1f) : new Color(0.3f, 1f, 0.3f, 1f));
            }
            catch (Exception ex) { Log.Warn($"Mute failed: {ex.Message}"); }

            UpdateDisplay();
        }

        private void OnReport(string reason)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _players.Count) return;
            var entry = _players[_selectedIndex];
            if (entry.PhotonPlayer == null) { Log.Warn("No PhotonPlayer for report"); return; }

            try
            {
                string userId = "";
                PropertyInfo uidProp = entry.PhotonPlayer.GetType().GetProperty("UserId");
                if (uidProp != null) userId = uidProp.GetValue(entry.PhotonPlayer, null)?.ToString() ?? "";

                // Use PlayFab ExecuteCloudScript to submit report (same as GT's built-in system)
                Type pfType = null;
                Type reqType = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (pfType == null) pfType = asm.GetType("PlayFab.PlayFabClientAPI");
                    if (reqType == null) reqType = asm.GetType("PlayFab.ClientModels.ExecuteCloudScriptRequest");
                    if (pfType != null && reqType != null) break;
                }

                if (pfType == null || reqType == null)
                {
                    Log.Warn("PlayFab types not found for report");
                    return;
                }

                object request = Activator.CreateInstance(reqType);

                FieldInfo funcField = reqType.GetField("FunctionName");
                if (funcField != null) funcField.SetValue(request, "ReportPlayer");

                var reportData = new Dictionary<string, object>
                {
                    { "ReportedPlayerID", userId },
                    { "Reason", reason },
                    { "ReportType", reason }
                };

                FieldInfo paramField = reqType.GetField("FunctionParameter");
                if (paramField != null) paramField.SetValue(request, reportData);

                // Find and call ExecuteCloudScript
                Type resultType = null;
                Type errorType = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (resultType == null) resultType = asm.GetType("PlayFab.ClientModels.ExecuteCloudScriptResult");
                    if (errorType == null) errorType = asm.GetType("PlayFab.PlayFabError");
                    if (resultType != null && errorType != null) break;
                }

                if (resultType != null && errorType != null)
                {
                    Type successDel = typeof(Action<>).MakeGenericType(resultType);
                    Type errorDel = typeof(Action<>).MakeGenericType(errorType);

                    var helper = new ReportCallback($"Reported {entry.Name} for {reason}");
                    Delegate successCb = Delegate.CreateDelegate(successDel, helper,
                        typeof(ReportCallback).GetMethod("OnResult"));
                    Delegate errorCb = Delegate.CreateDelegate(errorDel, helper,
                        typeof(ReportCallback).GetMethod("OnResult"));

                    foreach (MethodInfo m in pfType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name != "ExecuteCloudScript") continue;
                        var p = m.GetParameters();
                        try
                        {
                            if (p.Length >= 3)
                                m.Invoke(null, new object[] { request, successCb, errorCb, null, null });
                            else
                                m.Invoke(null, new object[] { request });
                            break;
                        }
                        catch { continue; }
                    }
                }

                Log.Info($"Report submitted: {entry.Name} for {reason}");
            }
            catch (Exception ex) { Log.Error($"Report failed: {ex.Message}"); }
        }

        private class ReportCallback
        {
            private readonly string _msg;
            public ReportCallback(string msg) { _msg = msg; }
            public void OnResult(object result) { Log.Info(_msg); }
        }
    }
}
