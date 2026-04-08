using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Mod Checker page — shows scanned player info, suspicious behavior, and detected mods/cosmetics.
    /// Laser pointer only active on this page.
    /// </summary>
    public class ModCheckerPage : BoardPage
    {
        private TextMeshPro _playerNameText;
        private TextMeshPro _platformText;
        private TextMeshPro _fpsText;
        private TextMeshPro _joinTimeText;

        // Suspicious Behavior section
        private TextMeshPro _behaviorHeader;
        private TextMeshPro _behaviorText;

        // Mods section
        private TextMeshPro _modsSectionHeader;
        private TextMeshPro _modsListText;

        private static readonly Color BehaviorWarningColor = new Color(1f, 0.6f, 0.15f, 1f); // Orange
        private static readonly Color BehaviorHeaderColor = new Color(1f, 0.5f, 0.1f, 1f);   // Dark orange

        private Action _onBack;

        public ModCheckerPage(Shader shader, TMP_FontAsset font, Action onBack)
            : base(null, shader, font)
        {
            _shader = shader;
            _font = font;
            _onBack = onBack;
        }

        public override void Build(Transform parent)
        {
            Root = new GameObject("Page_ModChecker");
            Root.transform.SetParent(parent, false);

            float zPanel = ZPanel;
            float zText = ZText;

            // Standard page header with BACK button
            float yPos = BuildPageHeader(Root.transform, "MOD CHECK", () => _onBack?.Invoke());

            yPos -= 0.004f;

            // ── Info rows ──
            _playerNameText = CreateText("PlayerName", Root.transform,
                new Vector3(0f, yPos, zText), "PLAYER: ---", 2.8f, TextWhite, TextAlignmentOptions.Left);
            yPos -= RowHeight;

            _platformText = CreateText("Platform", Root.transform,
                new Vector3(0f, yPos, zText), "PLATFORM: ---", 2.8f, TextWhite, TextAlignmentOptions.Left);
            yPos -= RowHeight;

            _fpsText = CreateText("FPS", Root.transform,
                new Vector3(0f, yPos, zText), "FPS: ---", 2.8f, TextWhite, TextAlignmentOptions.Left);
            yPos -= RowHeight;

            _joinTimeText = CreateText("JoinTime", Root.transform,
                new Vector3(0f, yPos, zText), "JOINED: ---", 2.8f, TextWhite, TextAlignmentOptions.Left);
            yPos -= RowHeight * 0.5f;

            // ── Separator ──
            CreateSeparator("Sep2", Root.transform, yPos);
            yPos -= 0.005f;

            // ── Suspicious Behavior section ──
            _behaviorHeader = CreateText("BehaviorHeader", Root.transform,
                new Vector3(0f, yPos, zText),
                "SUSPICIOUS BEHAVIOR", 2.6f, BehaviorHeaderColor, TextAlignmentOptions.Center);
            _behaviorHeader.fontStyle = FontStyles.Bold;
            yPos -= RowHeight * 0.7f;

            float behaviorAreaHeight = 0.04f;
            yPos -= behaviorAreaHeight * 0.5f;

            CreatePanel("BehaviorBg", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(MenuWidth - Padding * 3, behaviorAreaHeight, 0.002f),
                BgInnerColor);

            _behaviorText = CreateWrappingText("BehaviorList", Root.transform,
                new Vector3(0f, yPos + behaviorAreaHeight * 0.35f, zText),
                "None detected",
                2.2f, TextDim, MenuWidth - Padding * 4, behaviorAreaHeight);

            yPos -= behaviorAreaHeight * 0.5f + RowSpacing;

            // ── Separator ──
            CreateSeparator("Sep3", Root.transform, yPos);
            yPos -= 0.005f;

            // ── Mods section ──
            _modsSectionHeader = CreateText("ModsHeader", Root.transform,
                new Vector3(0f, yPos, zText),
                "DETECTED MODS", 2.6f, HeaderColor, TextAlignmentOptions.Center);
            _modsSectionHeader.fontStyle = FontStyles.Bold;
            yPos -= RowHeight * 0.7f;

            // Mods list area
            float modsAreaHeight = 0.065f;
            yPos -= modsAreaHeight * 0.5f;

            CreatePanel("ModsBg", Root.transform,
                new Vector3(0f, yPos, zPanel),
                new Vector3(MenuWidth - Padding * 3, modsAreaHeight, 0.002f),
                BgInnerColor);

            _modsListText = CreateWrappingText("ModsList", Root.transform,
                new Vector3(0f, yPos + modsAreaHeight * 0.35f, zText),
                "Aim laser + trigger to scan",
                2.2f, TextWhite, MenuWidth - Padding * 4, modsAreaHeight);

            yPos -= modsAreaHeight * 0.5f + RowSpacing;

            // ── Note ──
            CreateText("Note", Root.transform,
                new Vector3(0f, yPos, zText),
                "PROPS + HARMONY + BEHAVIOR + MOVEMENT", 1.6f, TextDim, TextAlignmentOptions.Center);
        }

        public void DisplayPlayerInfo(PlayerInfo info)
        {
            if (info == null) return;

            if (_playerNameText != null) _playerNameText.text = $"PLAYER: {info.PlayerName}";
            if (_platformText != null) _platformText.text = $"PLATFORM: {info.Platform}";
            if (_fpsText != null)
            {
                string fpsStr = info.FPS > 0 ? $"{info.FPS:F0}" : "N/A";
                _fpsText.text = $"FPS: {fpsStr}";

                // Color FPS red if below 60
                if (info.FPS > 0 && info.FPS < 60)
                    _fpsText.color = CheatRed;
                else
                    _fpsText.color = TextWhite;
            }
            if (_joinTimeText != null) _joinTimeText.text = $"JOINED: {info.JoinTime}";

            DisplayBehaviorFlags(info.BehaviorFlags);
            DisplayModList(info.DetectedMods);
        }

        public void ClearDisplay()
        {
            if (_playerNameText != null) _playerNameText.text = "PLAYER: ---";
            if (_platformText != null) _platformText.text = "PLATFORM: ---";
            if (_fpsText != null) { _fpsText.text = "FPS: ---"; _fpsText.color = TextWhite; }
            if (_joinTimeText != null) _joinTimeText.text = "JOINED: ---";
            if (_behaviorText != null) { _behaviorText.text = "None detected"; _behaviorText.color = TextDim; }
            if (_behaviorHeader != null) _behaviorHeader.color = BehaviorHeaderColor;
            if (_modsListText != null) _modsListText.text = "Aim laser + trigger to scan";
            if (_modsSectionHeader != null) { _modsSectionHeader.text = "DETECTED MODS"; _modsSectionHeader.color = HeaderColor; }
        }

        private void DisplayBehaviorFlags(List<string> flags)
        {
            if (_behaviorText == null) return;

            if (flags == null || flags.Count == 0)
            {
                _behaviorText.text = "None detected";
                _behaviorText.color = TextDim;
                if (_behaviorHeader != null) _behaviorHeader.color = BehaviorHeaderColor;
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (string flag in flags)
            {
                sb.AppendLine($"<color=#FF9926>\u26A0 {flag}</color>");
            }

            _behaviorText.text = sb.ToString().TrimEnd();
            _behaviorText.color = BehaviorWarningColor;

            if (_behaviorHeader != null)
            {
                _behaviorHeader.text = $"SUSPICIOUS BEHAVIOR ({flags.Count})";
                _behaviorHeader.color = CheatRed;
            }
        }

        private void DisplayModList(List<ModInfo> mods)
        {
            if (_modsListText == null) return;

            if (mods == null || mods.Count == 0)
            {
                _modsListText.text = "No mods detected";
                _modsListText.color = TextDim;
                return;
            }

            int cheatCount = 0;
            var sb = new System.Text.StringBuilder();

            foreach (var mod in mods)
            {
                if (mod.IsCheat)
                {
                    sb.AppendLine($"<color=#FF4444>\u26A0 {mod.Name}</color>");
                    cheatCount++;
                }
                else if (mod.Name.StartsWith("Cosmetic:"))
                {
                    // Cosmetics in a lighter color
                    sb.AppendLine($"<color=#AADDFF>{mod.Name}</color>");
                }
                else
                {
                    sb.AppendLine($"  {mod.Name}");
                }
            }

            _modsListText.text = sb.ToString().TrimEnd();
            _modsListText.color = TextWhite;

            if (_modsSectionHeader != null)
            {
                string cheatNote = cheatCount > 0 ? $" ({cheatCount} FLAGGED)" : "";
                _modsSectionHeader.text = $"DETECTED MODS{cheatNote}";
                _modsSectionHeader.color = cheatCount > 0 ? CheatRed : HeaderColor;
            }
        }
    }
}
