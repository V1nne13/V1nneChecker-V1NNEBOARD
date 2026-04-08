using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Settings page — voice mode (push-to-talk / open mic), sound toggle, laser color, reload cheats.
    /// Settings are saved to BepInEx/config/VenneChecker_Settings.txt and loaded on startup.
    /// </summary>
    public class SettingsPage : BoardPage
    {
        private BoardButton _voiceBtn;
        private BoardButton _soundBtn;
        private BoardButton _laserBtn;

        private bool _pushToTalk = true;
        private bool _soundOn = true;
        private int _laserColorIdx = 0;

        private static readonly string[] LaserColorNames = { "RED", "GREEN", "BLUE", "PURPLE", "YELLOW", "CYAN" };
        private static readonly Color[] LaserColors = {
            new Color(1f, 0f, 0f, 0.9f),
            new Color(0f, 1f, 0.3f, 0.9f),
            new Color(0.3f, 0.5f, 1f, 0.9f),
            new Color(0.7f, 0.2f, 1f, 0.9f),
            new Color(1f, 0.9f, 0.1f, 0.9f),
            new Color(0f, 0.9f, 0.9f, 0.9f)
        };

        public static Color CurrentLaserColor { get; private set; } = new Color(1f, 0f, 0f, 0.9f);

        private Action _onBack;
        private static string _settingsPath;

        public SettingsPage(Shader shader, TMP_FontAsset font, Action onBack)
            : base(null, shader, font)
        {
            _shader = shader;
            _font = font;
            _onBack = onBack;

            LoadSettings();
        }

        public override void Build(Transform parent)
        {
            Root = new GameObject("Page_Settings");
            Root.transform.SetParent(parent, false);

            float zPanel = ZButton;

            float yPos = BuildPageHeader(Root.transform, "SETTINGS", () => _onBack?.Invoke());
            yPos -= 0.008f;

            float btnW = MenuWidth - Padding * 4;
            float btnH = 0.032f;
            float btnD = ButtonDepth;
            float btnGap = RowSpacing * 3;

            ReadCurrentVoiceSetting();

            // ── Voice Mode ──
            yPos -= btnH * 0.5f;
            _voiceBtn = CreateButton("VoiceBtn", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(btnW, btnH, btnD),
                $"VOICE: {(_pushToTalk ? "PUSH TO TALK" : "OPEN MIC")}", 2.5f,
                OnVoiceToggle);
            yPos -= btnH + btnGap;

            // ── Sound ──
            yPos -= btnH * 0.5f;
            _soundBtn = CreateButton("SoundBtn", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(btnW, btnH, btnD),
                $"SOUND: {(_soundOn ? "ON" : "OFF")}", 2.5f,
                OnSoundToggle);
            yPos -= btnH + btnGap;

            // ── Laser Color ──
            yPos -= btnH * 0.5f;
            _laserBtn = CreateButton("LaserBtn", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(btnW, btnH, btnD),
                $"LASER: {LaserColorNames[_laserColorIdx]}", 2.5f,
                OnLaserColorCycle);
            yPos -= btnH + btnGap;

            // ── Reload Cheats ──
            yPos -= btnH * 0.5f;
            CreateButton("ReloadBtn", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(btnW, btnH, btnD),
                "RELOAD CHEATS", 2.5f,
                OnReloadCheats);
        }

        private void OnVoiceToggle()
        {
            _pushToTalk = !_pushToTalk;
            _voiceBtn?.SetLabel($"VOICE: {(_pushToTalk ? "PUSH TO TALK" : "OPEN MIC")}");
            ApplyVoiceSetting();
            SaveSettings();
        }

        private void OnSoundToggle()
        {
            _soundOn = !_soundOn;
            _soundBtn?.SetLabel($"SOUND: {(_soundOn ? "ON" : "OFF")}");
            if (SoundManager.Instance != null)
                SoundManager.Instance.SetEnabled(_soundOn);
            SaveSettings();
        }

        private void OnLaserColorCycle()
        {
            _laserColorIdx = (_laserColorIdx + 1) % LaserColorNames.Length;
            _laserBtn?.SetLabel($"LASER: {LaserColorNames[_laserColorIdx]}");
            CurrentLaserColor = LaserColors[_laserColorIdx];
            SaveSettings();
        }

        private void OnReloadCheats()
        {
            CheatDatabase.ReloadList();
            Log.Info("Cheat database reloaded from settings");

            if (NotificationManager.Instance != null)
                NotificationManager.Instance.Show("Cheat list reloaded", new Color(0.3f, 1f, 0.3f, 1f));
        }

        private void ReadCurrentVoiceSetting()
        {
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type gcType = asm.GetType("GorillaComputer");
                    if (gcType == null) continue;

                    object inst = GetInstance(gcType);
                    if (inst == null) continue;

                    FieldInfo pttField = gcType.GetField("pttType",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pttField != null)
                    {
                        string val = pttField.GetValue(inst)?.ToString();
                        _pushToTalk = val != null && val.Contains("PUSH");
                    }
                    return;
                }
            }
            catch { }
        }

        private void ApplyVoiceSetting()
        {
            try
            {
                string pttValue = _pushToTalk ? "PUSH TO TALK" : "ALL CHAT";

                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type gcType = asm.GetType("GorillaComputer");
                    if (gcType == null) continue;

                    object inst = GetInstance(gcType);
                    if (inst == null) continue;

                    // Set the pttType field
                    FieldInfo pttField = gcType.GetField("pttType",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pttField != null)
                        pttField.SetValue(inst, pttValue);

                    // Save to PlayerPrefs (GT reads this on startup)
                    PlayerPrefs.SetString("pttType", pttValue);
                    PlayerPrefs.Save();

                    // Try to call the method that applies voice settings at runtime
                    MethodInfo applyMethod = gcType.GetMethod("OnModeSelectButtonPress",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    // Fallback: try other method names GT might use
                    if (applyMethod == null)
                        applyMethod = gcType.GetMethod("UpdateVoiceMode",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    // Also try to directly update the Photon Voice Recorder
                    try
                    {
                        UpdatePhotonVoiceRecorder();
                    }
                    catch { }

                    Log.Info($"Voice mode set to: {pttValue}");
                    return;
                }
            }
            catch (Exception ex) { Log.Warn($"Voice setting failed: {ex.Message}"); }
        }

        private void UpdatePhotonVoiceRecorder()
        {
            // Find the Recorder component and update its transmit enabled state
            try
            {
                if (GorillaTagger.Instance == null) return;

                // GorillaTagger has a myRecorder field
                Type taggerType = typeof(GorillaTagger);

                FieldInfo recorderField = taggerType.GetField("myRecorder",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (recorderField == null) return;

                object recorder = recorderField.GetValue(GorillaTagger.Instance);
                if (recorder == null) return;

                Type recType = recorder.GetType();

                if (!_pushToTalk)
                {
                    // Open mic: set TransmitEnabled = true
                    PropertyInfo transmitProp = recType.GetProperty("TransmitEnabled",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (transmitProp != null)
                        transmitProp.SetValue(recorder, true, null);
                }

                Log.Info($"Updated Photon Voice Recorder: pushToTalk={_pushToTalk}");
            }
            catch (Exception ex)
            {
                Log.Warn($"UpdatePhotonVoiceRecorder: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        //  SAVE / LOAD SETTINGS
        // ═══════════════════════════════════════════

        private static string GetSettingsPath()
        {
            if (_settingsPath != null) return _settingsPath;

            try
            {
                _settingsPath = Path.Combine(BepInEx.Paths.ConfigPath, "VenneChecker_Settings.txt");
            }
            catch
            {
                _settingsPath = "VenneChecker_Settings.txt";
            }
            return _settingsPath;
        }

        private void SaveSettings()
        {
            try
            {
                string path = GetSettingsPath();
                string content =
                    $"# VenneChecker Settings — auto-saved\n" +
                    $"pushToTalk={(_pushToTalk ? "true" : "false")}\n" +
                    $"soundOn={(_soundOn ? "true" : "false")}\n" +
                    $"laserColorIdx={_laserColorIdx}\n";

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, content);
                Log.Info($"Settings saved to {path}");
            }
            catch (Exception ex)
            {
                Log.Warn($"SaveSettings failed: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path)) return;

                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                    string[] parts = trimmed.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;

                    string key = parts[0].Trim();
                    string val = parts[1].Trim();

                    switch (key)
                    {
                        case "pushToTalk":
                            _pushToTalk = val == "true";
                            break;
                        case "soundOn":
                            _soundOn = val == "true";
                            if (SoundManager.Instance != null)
                                SoundManager.Instance.SetEnabled(_soundOn);
                            break;
                        case "laserColorIdx":
                            if (int.TryParse(val, out int idx) && idx >= 0 && idx < LaserColors.Length)
                            {
                                _laserColorIdx = idx;
                                CurrentLaserColor = LaserColors[idx];
                            }
                            break;
                    }
                }

                Log.Info($"Settings loaded: ptt={_pushToTalk}, sound={_soundOn}, laser={LaserColorNames[_laserColorIdx]}");
            }
            catch (Exception ex)
            {
                Log.Warn($"LoadSettings failed: {ex.Message}");
            }
        }

        private static object GetInstance(Type type)
        {
            try
            {
                PropertyInfo p = type.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (p != null) return p.GetValue(null, null);

                FieldInfo f = type.GetField("instance", BindingFlags.Public | BindingFlags.Static);
                if (f != null) return f.GetValue(null);
            }
            catch { }
            return null;
        }
    }
}
