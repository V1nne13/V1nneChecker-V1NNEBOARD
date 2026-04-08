using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Base class for all V1NNEBOARD pages.
    /// Provides shared helpers for creating 3D panels, text, and buttons.
    /// Visual style: dark background, red accents, soft glow edges (inspired by Bark/BillBoard menus).
    /// </summary>
    public abstract class BoardPage
    {
        public GameObject Root { get; protected set; }
        public bool IsActive => Root != null && Root.activeSelf;

        // Shared dimensions — slightly larger for readability
        protected const float MenuWidth = 0.28f;
        protected const float MenuHeight = 0.36f;
        protected const float MenuDepth = 0.005f;
        protected const float Padding = 0.014f;
        protected const float RowHeight = 0.026f;
        protected const float RowSpacing = 0.004f;
        protected const float TextScale = 0.015f;
        protected const float ButtonHeight = 0.028f;
        protected const float ButtonDepth = 0.006f;

        // ── Color palette — dark red theme ──
        protected static readonly Color BgColor = new Color(0.12f, 0.04f, 0.04f, 1f);             // Dark red-black bg
        protected static readonly Color BgInnerColor = new Color(0.07f, 0.03f, 0.03f, 1f);       // Slightly lighter inner area
        protected static readonly Color AccentRed = new Color(0.7f, 0.1f, 0.1f, 1f);             // Strong red accent
        protected static readonly Color AccentRedDim = new Color(0.3f, 0.04f, 0.04f, 1f);        // Dark red (title bar)
        protected static readonly Color GlowRed = new Color(1f, 0.15f, 0.1f, 0.5f);              // Red glow
        protected static readonly Color GlowRedBright = new Color(1f, 0.2f, 0.15f, 0.7f);        // Bright red glow
        protected static readonly Color ButtonFace = new Color(0.6f, 0.08f, 0.08f, 1f);          // Bright red button face
        protected static readonly Color ButtonBase = new Color(0.15f, 0.02f, 0.02f, 1f);         // Very dark button base (3D depth)
        protected static readonly Color ButtonHover = new Color(0.75f, 0.12f, 0.12f, 1f);        // Brighter hover
        protected static readonly Color ButtonBorder = new Color(1f, 0.2f, 0.15f, 0.7f);         // Bright red border glow
        protected static readonly Color TextWhite = new Color(1f, 1f, 1f, 1f);                   // Pure white
        protected static readonly Color TextDim = new Color(0.6f, 0.5f, 0.5f, 1f);               // Dim text
        protected static readonly Color CheatRed = new Color(1f, 0.25f, 0.2f, 1f);               // Cheat warning
        protected static readonly Color HeaderColor = new Color(1f, 0.35f, 0.25f, 1f);            // Section header

        protected Shader _shader;
        protected Shader _unlitShader; // For glow elements
        protected TMP_FontAsset _font;

        protected BoardPage(Transform parent, Shader shader, TMP_FontAsset font)
        {
            // Unlit/Color — exact colors, no lighting, stable from all angles
            _unlitShader = Shader.Find("Unlit/Color")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("GUI/Text Shader")
                ?? shader;
            _shader = _unlitShader;
        }

        public abstract void Build(Transform parent);

        public virtual void Show()
        {
            if (Root != null) Root.SetActive(true);
        }

        public virtual void Hide()
        {
            if (Root != null) Root.SetActive(false);
        }

        public virtual void OnUpdate() { }

        // ═══════════════════════════════════════════
        //  SHARED BUILDERS
        // ═══════════════════════════════════════════

        protected GameObject CreatePanel(string name, Transform parent, Vector3 localPos, Vector3 scale, Color color)
        {
            float width = scale.x;
            float height = scale.y;
            float radius = Mathf.Min(width, height) * 0.18f;
            radius = Mathf.Max(radius, 0.004f);

            GameObject panel = RoundedRectMesh.CreatePanel(name, parent, localPos,
                width, height, radius, color, _shader);

            return panel;
        }

        /// <summary>
        /// Creates a glow/accent panel (same as CreatePanel but uses unlit shader).
        /// </summary>
        protected GameObject CreateGlowPanel(string name, Transform parent, Vector3 localPos, Vector3 scale, Color color)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = name;
            panel.transform.SetParent(parent, false);
            panel.transform.localPosition = localPos;
            panel.transform.localScale = scale;

            var col = panel.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            Renderer rend = panel.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(_unlitShader);
                mat.color = color;
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                rend.material = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            return panel;
        }

        protected TextMeshPro CreateText(string name, Transform parent, Vector3 localPos,
            string text, float fontSize, Color color, TextAlignmentOptions alignment)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(parent, false);
            textObj.transform.localPosition = localPos;
            textObj.transform.localScale = Vector3.one * TextScale;

            TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize * 1.3f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.sortingOrder = 10;
            tmp.rectTransform.sizeDelta = new Vector2(
                (MenuWidth - Padding * 3) / TextScale,
                RowHeight * 1.5f / TextScale);

            if (_font != null) tmp.font = _font;

            return tmp;
        }

        protected TextMeshPro CreateWrappingText(string name, Transform parent, Vector3 localPos,
            string text, float fontSize, Color color, float width, float height)
        {
            TextMeshPro tmp = CreateText(name, parent, localPos, text, fontSize, color, TextAlignmentOptions.TopLeft);
            tmp.rectTransform.sizeDelta = new Vector2(width / TextScale, height / TextScale);
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.richText = true;
            return tmp;
        }

        // ── Z-layer constants (all negative = toward viewer) ──
        protected const float ZPanel = -0.004f;    // Inner panels (title bar, mods area)
        protected const float ZSep = -0.005f;      // Separators
        protected const float ZButton = -0.008f;   // Buttons
        protected const float ZText = -0.015f;     // Text (always in front)

        protected void CreateSeparator(string name, Transform parent, float yPos)
        {
            CreateGlowPanel(name, parent,
                new Vector3(0f, yPos, ZSep),
                new Vector3(MenuWidth - Padding * 4, 0.0015f, 0.001f),
                GlowRed);
        }

        protected float BuildPageHeader(Transform parent, string title, System.Action onBack)
        {
            float yPos = MenuHeight * 0.5f - Padding;
            float topBarH = 0.028f;
            yPos -= topBarH * 0.5f;

            // Title bar background
            CreatePanel("TopBar", parent,
                new Vector3(0f, yPos, ZPanel),
                new Vector3(MenuWidth - Padding * 2, topBarH, 0.004f),
                AccentRedDim);

            // Glow line under title bar
            CreateGlowPanel("TopGlow", parent,
                new Vector3(0f, yPos - topBarH * 0.5f, ZSep),
                new Vector3(MenuWidth - Padding * 2, 0.0015f, 0.001f),
                GlowRedBright);

            // Back button
            CreateButton("BackBtn", parent,
                new Vector3(-MenuWidth * 0.5f + Padding + 0.028f, yPos, ZButton),
                new Vector3(0.05f, topBarH - 0.006f, 0.012f),
                "< BACK", 2.0f, onBack);

            // Title text
            CreateText("Title", parent,
                new Vector3(0.01f, yPos, ZText),
                title, 3.8f, TextWhite, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;

            yPos -= topBarH * 0.5f + RowSpacing * 2;
            return yPos;
        }

        protected BoardButton CreateButton(string name, Transform parent, Vector3 localPos, Vector3 size,
            string label, float fontSize, System.Action onPressed)
        {
            return BoardButton.Create(name, parent, localPos, size, label, fontSize, onPressed,
                ButtonFace, ButtonHover, _shader, _font);
        }

        protected static void SetMatColor(Material mat, Color color)
        {
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
        }
    }
}
