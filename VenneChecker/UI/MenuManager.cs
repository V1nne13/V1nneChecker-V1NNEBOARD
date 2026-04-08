using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// V1NNEBOARD — multi-page utility board attached to the player's left hand.
    /// Hold X to open. Navigate pages by finger-touching app tiles.
    /// Pages: Home, ModChecker, Settings, RoomControl, PlayerList.
    /// </summary>
    public class MenuManager : MonoBehaviour
    {
        public static MenuManager Instance { get; private set; }
        public bool IsMenuOpen { get; private set; }

        /// <summary>The currently active page index. -1 = none, 0 = home.</summary>
        public int CurrentPageIndex { get; private set; } = -1;

        // ── Menu dimensions (matches BoardPage) ──
        private const float MenuWidth = 0.28f;
        private const float MenuHeight = 0.36f;
        private const float MenuDepth = 0.005f;
        private const float Padding = 0.014f;

        // ── Colors ──
        private static readonly Color BgColor = new Color(0.12f, 0.04f, 0.04f, 1f);
        private static readonly Color GlowRed = new Color(1f, 0.15f, 0.1f, 0.5f);

        // ── Objects ──
        private GameObject _menuRoot;
        private Transform _contentRoot;
        private Transform _leftHandTransform;
        private bool _initialized;
        private bool _wasOpen;

        // ── Pages ──
        private HomePage _homePage;
        private ModCheckerPage _modCheckerPage;
        private SettingsPage _settingsPage;
        private RoomControlPage _roomControlPage;
        private PlayerListPage _playerListPage;
        private BoardPage[] _pages;

        // ── Shared resources ──
        private Shader _shader;
        private TMP_FontAsset _font;

        private void Awake()
        {
            Instance = this;
        }

        public void Initialize(Transform leftHand)
        {
            _leftHandTransform = leftHand;
            _shader = FindBestShader();
            _font = FindFont();

            BuildBoard();

            _menuRoot.SetActive(false);
            _initialized = true;

            Log.Info($"MenuManager initialized (V1NNEBOARD). Shader: {(_shader != null ? _shader.name : "NULL")}");
        }

        private void Update()
        {
            if (!_initialized || _leftHandTransform == null)
                return;

            bool xHeld = false;
            try { xHeld = OVRInput.Get(OVRInput.Button.Three); } catch { return; }

            if (xHeld && !_wasOpen)
                OpenMenu();
            else if (!xHeld && _wasOpen)
                CloseMenu();

            _wasOpen = xHeld;
            IsMenuOpen = xHeld;

            // Position board above left hand, facing the player
            if (_menuRoot != null && _menuRoot.activeSelf && _leftHandTransform != null)
            {
                Vector3 menuPos = _leftHandTransform.position + Vector3.up * 0.25f;
                _menuRoot.transform.position = menuPos;

                if (Camera.main != null)
                {
                    Vector3 lookDir = Camera.main.transform.position - menuPos;
                    lookDir.y = 0f;
                    if (lookDir.sqrMagnitude > 0.001f)
                        _menuRoot.transform.rotation = Quaternion.LookRotation(-lookDir, Vector3.up);
                }
            }

            // Update active page
            if (IsMenuOpen && CurrentPageIndex >= 0 && CurrentPageIndex < _pages.Length)
                _pages[CurrentPageIndex]?.OnUpdate();
        }

        private void OpenMenu()
        {
            if (_menuRoot == null) return;

            CheatDatabase.ReloadList();
            _menuRoot.SetActive(true);

            // Always open to home page
            NavigateToPage(0);

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlayMenuOpen();
        }

        private void CloseMenu()
        {
            if (_menuRoot == null) return;

            _menuRoot.SetActive(false);
            CurrentPageIndex = -1;

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlayMenuClose();
        }

        /// <summary>
        /// Switches to a page by index. 0=Home, 1=ModChecker, 2=Settings, 3=RoomCtrl, 4=PlayerList.
        /// </summary>
        public void NavigateToPage(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _pages.Length) return;

            // Hide all pages
            for (int i = 0; i < _pages.Length; i++)
                _pages[i]?.Hide();

            // Show target page
            _pages[pageIndex]?.Show();
            CurrentPageIndex = pageIndex;
        }

        private void NavigateHome()
        {
            NavigateToPage(0);
        }

        /// <summary>
        /// Called from app tile selection on home page.
        /// Maps tile index to page: 0=ModChecker, 1=Settings, 2=RoomCtrl, 3=PlayerList.
        /// </summary>
        private void OnAppSelected(int appIndex)
        {
            // appIndex 0-3 maps to pages 1-4
            NavigateToPage(appIndex + 1);
        }

        /// <summary>
        /// Called from ModCheckerPage or PlayerListPage when a player is scanned.
        /// </summary>
        public void DisplayPlayerInfo(PlayerInfo info)
        {
            if (info == null) return;

            // If we're on the mod checker page, update it
            _modCheckerPage?.DisplayPlayerInfo(info);

            // If called from player list and we're not on mod checker, switch to it
            if (CurrentPageIndex == 4 && info != null)
            {
                _modCheckerPage?.DisplayPlayerInfo(info);
                NavigateToPage(1);
            }
        }

        // ═══════════════════════════════════════════════════
        //  BUILD THE BOARD
        // ═══════════════════════════════════════════════════

        private void BuildBoard()
        {
            // Root object
            _menuRoot = new GameObject("V1NNEBOARD");
            _menuRoot.transform.SetParent(null, false);
            _menuRoot.transform.localScale = Vector3.one;

            // Content root (unscaled parent for all content)
            GameObject contentObj = new GameObject("Content");
            contentObj.transform.SetParent(_menuRoot.transform, false);
            _contentRoot = contentObj.transform;

            // ── Background panel (rounded corners) ──
            float bgRadius = 0.014f;
            GameObject bgPanel = RoundedRectMesh.CreatePanel("BgPanel", _contentRoot,
                Vector3.zero, MenuWidth, MenuHeight, bgRadius, BgColor, _shader);

            // ── Create all pages ──
            _homePage = new HomePage(_shader, _font, OnAppSelected);
            _homePage.Build(_contentRoot);

            _modCheckerPage = new ModCheckerPage(_shader, _font, NavigateHome);
            _modCheckerPage.Build(_contentRoot);

            _settingsPage = new SettingsPage(_shader, _font, NavigateHome);
            _settingsPage.Build(_contentRoot);

            _roomControlPage = new RoomControlPage(_shader, _font, NavigateHome);
            _roomControlPage.Build(_contentRoot);

            _playerListPage = new PlayerListPage(_shader, _font, NavigateHome, DisplayPlayerInfo);
            _playerListPage.Build(_contentRoot);

            _pages = new BoardPage[]
            {
                _homePage,        // 0
                _modCheckerPage,  // 1
                _settingsPage,    // 2
                _roomControlPage, // 3
                _playerListPage   // 4
            };

            // Hide all pages initially
            foreach (var page in _pages)
                page.Hide();
        }

        // ═══════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════

        private static void SetMatColor(Material mat, Color color)
        {
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
        }

        private static Shader FindBestShader()
        {
            // Unlit/Color is stable from all angles — no lighting, no face culling issues
            Shader shader = Shader.Find("Unlit/Color");
            if (shader != null) return shader;

            shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) return shader;

            shader = Shader.Find("GUI/Text Shader");
            if (shader != null) return shader;

            shader = Shader.Find("Standard");
            if (shader != null) return shader;

            return Shader.Find("Diffuse");
        }

        private static TMP_FontAsset FindFont()
        {
            try
            {
                TMP_FontAsset font = TMP_Settings.defaultFontAsset;
                if (font != null) return font;
            }
            catch { }

            try
            {
                TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0)
                {
                    foreach (var f in fonts)
                    {
                        if (f != null && f.name.Contains("Liberation"))
                            return f;
                    }
                    return fonts[0];
                }
            }
            catch { }

            return null;
        }
    }
}
