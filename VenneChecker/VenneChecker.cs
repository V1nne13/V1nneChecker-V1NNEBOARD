using System.Collections;
using BepInEx;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// VenneChecker — BepInEx plugin for Gorilla Tag.
    /// A player mod checker with a physical 3D hand menu and laser pointer scanner.
    /// Hold X to open menu on left hand, aim laser with right hand, hold trigger to scan players.
    /// </summary>
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class VenneCheckerPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.venne.vennechecker";
        public const string PluginName = "VenneChecker";
        public const string PluginVersion = "1.0.0";

        /// <summary>Singleton instance of the plugin.</summary>
        public static VenneCheckerPlugin Instance { get; private set; }

        private GameObject _managerObject;
        private bool _initialized;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            Log.Init(Logger);

            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded!");

            CheatDatabase.Load();
            Logger.LogInfo($"Cheat database loaded from: {CheatDatabase.FilePath} ({CheatDatabase.Count} entries)");
        }

        private void Start()
        {
            StartCoroutine(WaitForGameInit());
        }

        private IEnumerator WaitForGameInit()
        {
            Logger.LogInfo("Waiting for GorillaTagger to initialize...");

            while (GorillaTagger.Instance == null)
                yield return null;

            // Give extra time for all hand transforms to set up
            yield return new WaitForSeconds(2f);

            Logger.LogInfo("GorillaTagger found. Initializing VenneChecker components...");
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            if (_initialized) return;

            try
            {
                _managerObject = new GameObject("VenneChecker_Manager");
                DontDestroyOnLoad(_managerObject);

                SoundManager soundMgr = _managerObject.AddComponent<SoundManager>();
                PlayerScanner scanner = _managerObject.AddComponent<PlayerScanner>();
                LaserPointer laser = _managerObject.AddComponent<LaserPointer>();
                FingerTouch finger = _managerObject.AddComponent<FingerTouch>();
                NotificationManager notifMgr = _managerObject.AddComponent<NotificationManager>();
                RoomScanner roomScanner = _managerObject.AddComponent<RoomScanner>();
                DelayedAction delayedAction = _managerObject.AddComponent<DelayedAction>();
                MovementTracker movementTracker = _managerObject.AddComponent<MovementTracker>();
                MenuManager menu = _managerObject.AddComponent<MenuManager>();

                // Use GorillaTagger.leftHandTransform directly for menu
                Transform leftHand = GorillaTagger.Instance.leftHandTransform;
                if (leftHand == null)
                {
                    Logger.LogWarning("leftHandTransform is null, using fallback search");
                    leftHand = FindHandTransform("left");
                }
                if (leftHand == null)
                    leftHand = GorillaTagger.Instance.transform;

                Logger.LogInfo($"Left hand: {leftHand.name} (path: {GetPath(leftHand)})");

                menu.Initialize(leftHand);
                laser.Initialize();
                finger.Initialize();

                // Share font with notification manager
                notifMgr.SetFont(FindFont());

                // Initialize behavioral cheat detection (Seralyth RPC/event monitoring)
                NetworkEventDetector.Initialize();

                _initialized = true;
                Logger.LogInfo("VenneChecker fully initialized! Hold X to open menu.");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Failed to initialize VenneChecker: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Finds a hand transform by searching multiple sources with proper left/right distinction.
        /// </summary>
        private Transform FindHandTransform(string side)
        {
            bool isLeft = side.ToLower() == "left";

            // Strategy 1: GorillaTagger direct properties
            try
            {
                if (isLeft && GorillaTagger.Instance.leftHandTransform != null)
                {
                    Logger.LogInfo($"Found {side} hand via GorillaTagger.leftHandTransform: {GorillaTagger.Instance.leftHandTransform.name}");
                    return GorillaTagger.Instance.leftHandTransform;
                }
                if (!isLeft && GorillaTagger.Instance.rightHandTransform != null)
                {
                    Logger.LogInfo($"Found {side} hand via GorillaTagger.rightHandTransform: {GorillaTagger.Instance.rightHandTransform.name}");
                    return GorillaTagger.Instance.rightHandTransform;
                }
            }
            catch { }

            // Strategy 2: Search Camera Rig hierarchy for controller anchors
            // Gorilla Tag uses OVR camera rig — controllers are typically under:
            // OVRCameraRig/TrackingSpace/LeftHandAnchor and RightHandAnchor
            string[] searchNames = isLeft
                ? new[] { "LeftHandAnchor", "LeftControllerAnchor", "left_hand", "LeftHand", "Left Hand" }
                : new[] { "RightHandAnchor", "RightControllerAnchor", "right_hand", "RightHand", "Right Hand" };

            // Search from GorillaTagger
            Transform result = SearchForTransform(GorillaTagger.Instance.transform, searchNames);
            if (result != null)
            {
                Logger.LogInfo($"Found {side} hand in GorillaTagger hierarchy: {result.name}");
                return result;
            }

            // Search from Camera.main
            if (Camera.main != null)
            {
                // Go up to the camera rig root
                Transform camRoot = Camera.main.transform.root;
                result = SearchForTransform(camRoot, searchNames);
                if (result != null)
                {
                    Logger.LogInfo($"Found {side} hand in camera rig: {result.name}");
                    return result;
                }
            }

            // Strategy 3: Global search for OVR anchors
            foreach (string name in searchNames)
            {
                GameObject obj = GameObject.Find(name);
                if (obj != null)
                {
                    Logger.LogInfo($"Found {side} hand via GameObject.Find: {obj.name}");
                    return obj.transform;
                }
            }

            // Strategy 4: Search all transforms in scene
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            string sideKey = isLeft ? "left" : "right";
            foreach (Transform t in allTransforms)
            {
                string lower = t.name.ToLower();
                if (lower.Contains(sideKey) && (lower.Contains("anchor") || lower.Contains("controller")))
                {
                    Logger.LogInfo($"Found {side} hand via scene search: {t.name}");
                    return t;
                }
            }

            // Strategy 5: offlineVRRig hand transforms (might both be CenterHand but try)
            try
            {
                VRRig rig = GorillaTagger.Instance.offlineVRRig;
                if (rig != null)
                {
                    Transform hand = isLeft ? rig.leftHandTransform : rig.rightHandTransform;
                    if (hand != null)
                    {
                        Logger.LogInfo($"Found {side} hand via offlineVRRig (fallback): {hand.name}");
                        return hand;
                    }
                }
            }
            catch { }

            Logger.LogWarning($"Could not find {side} hand transform!");
            return null;
        }

        /// <summary>
        /// Searches a transform hierarchy for any child matching one of the target names.
        /// </summary>
        private Transform SearchForTransform(Transform root, string[] targetNames)
        {
            if (root == null) return null;

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (string target in targetNames)
                {
                    if (child.name.Equals(target, System.StringComparison.OrdinalIgnoreCase))
                        return child;
                }
            }
            return null;
        }

        /// <summary>
        /// Logs the hierarchy of important game objects to help debug hand transforms.
        /// </summary>
        private void LogHierarchy()
        {
            try
            {
                Logger.LogInfo("=== GorillaTagger Hierarchy ===");
                LogChildren(GorillaTagger.Instance.transform, 0, 4);

                if (Camera.main != null)
                {
                    Logger.LogInfo("=== Camera Rig Hierarchy ===");
                    LogChildren(Camera.main.transform.root, 0, 4);
                }
            }
            catch { }
        }

        private void LogChildren(Transform parent, int depth, int maxDepth)
        {
            if (depth >= maxDepth || parent == null) return;

            string indent = new string(' ', depth * 2);
            Logger.LogInfo($"{indent}{parent.name}");

            foreach (Transform child in parent)
            {
                LogChildren(child, depth + 1, maxDepth);
            }
        }

        private TMPro.TMP_FontAsset FindFont()
        {
            try
            {
                var font = TMPro.TMP_Settings.defaultFontAsset;
                if (font != null) return font;
            }
            catch { }

            try
            {
                var fonts = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0)
                    return fonts[0];
            }
            catch { }

            return null;
        }

        private string GetPath(Transform t)
        {
            string path = t.name;
            Transform parent = t.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private void OnDestroy()
        {
            if (_managerObject != null)
                Destroy(_managerObject);
        }
    }
}
