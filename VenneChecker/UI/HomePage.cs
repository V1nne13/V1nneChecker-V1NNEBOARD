using System;
using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Home page of V1NNEBOARD. Title with glow, live clock, 4 app tiles in a 2x2 grid.
    /// </summary>
    public class HomePage : BoardPage
    {
        private TextMeshPro _clockText;
        private Action<int> _onAppSelected;

        public HomePage(Shader shader, TMP_FontAsset font, Action<int> onAppSelected)
            : base(null, shader, font)
        {
            _shader = shader;
            _font = font;
            _onAppSelected = onAppSelected;
        }

        public override void Build(Transform parent)
        {
            Root = new GameObject("Page_Home");
            Root.transform.SetParent(parent, false);

            float yPos = MenuHeight * 0.5f - Padding;

            // ── Title bar ──
            float titleBarHeight = 0.035f;
            yPos -= titleBarHeight * 0.5f;

            CreatePanel("TitleBar", Root.transform,
                new Vector3(0f, yPos, ZPanel),
                new Vector3(MenuWidth - Padding * 2, titleBarHeight, 0.004f),
                AccentRedDim);

            CreateGlowPanel("TitleGlow", Root.transform,
                new Vector3(0f, yPos - titleBarHeight * 0.5f, ZSep),
                new Vector3(MenuWidth - Padding * 2, 0.0015f, 0.001f),
                GlowRedBright);

            var title = CreateText("Title", Root.transform,
                new Vector3(0f, yPos, ZText),
                "V1NNEBOARD", 5.0f, TextWhite, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            yPos -= titleBarHeight * 0.5f + RowSpacing * 2;

            // ── Clock ──
            yPos -= 0.018f;
            _clockText = CreateText("Clock", Root.transform,
                new Vector3(0f, yPos, ZText),
                "00:00:00", 4.0f, TextDim, TextAlignmentOptions.Center);
            yPos -= 0.024f + RowSpacing;

            CreateSeparator("Sep1", Root.transform, yPos);
            yPos -= 0.008f;

            // ── 2x2 App Grid ──
            float tileW = 0.105f;
            float tileH = 0.08f;
            float tileD = 0.012f;
            float gapX = 0.016f;
            float gapY = 0.014f;

            float leftX = -(tileW + gapX) * 0.5f;
            float rightX = (tileW + gapX) * 0.5f;

            float row1Y = yPos - tileH * 0.5f;
            float row2Y = row1Y - tileH - gapY;

            CreateAppTile("ModCheck", Root.transform,
                new Vector3(leftX, row1Y, ZButton), new Vector3(tileW, tileH, tileD),
                "MOD\nCHECK", () => _onAppSelected?.Invoke(0));

            CreateAppTile("Settings", Root.transform,
                new Vector3(rightX, row1Y, ZButton), new Vector3(tileW, tileH, tileD),
                "SET-\nTINGS", () => _onAppSelected?.Invoke(1));

            CreateAppTile("RoomCtrl", Root.transform,
                new Vector3(leftX, row2Y, ZButton), new Vector3(tileW, tileH, tileD),
                "ROOM\nCTRL", () => _onAppSelected?.Invoke(2));

            CreateAppTile("PlayerList", Root.transform,
                new Vector3(rightX, row2Y, ZButton), new Vector3(tileW, tileH, tileD),
                "PLAYER\nLIST", () => _onAppSelected?.Invoke(3));

            // ── Bottom glow accent ──
            CreateGlowPanel("BottomGlow", Root.transform,
                new Vector3(0f, -MenuHeight * 0.5f + 0.006f, ZSep),
                new Vector3(MenuWidth - Padding * 2, 0.0015f, 0.001f),
                GlowRed);
        }

        public override void OnUpdate()
        {
            if (_clockText != null)
                _clockText.text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void CreateAppTile(string name, Transform parent, Vector3 pos, Vector3 size,
            string label, Action onPressed)
        {
            BoardButton.Create("Tile_" + name, parent, pos, size,
                label, 3.2f, onPressed, ButtonFace, ButtonHover, _shader, _font);
        }
    }
}
