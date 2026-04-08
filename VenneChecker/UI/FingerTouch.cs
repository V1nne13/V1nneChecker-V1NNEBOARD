using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Tracks the right index fingertip and creates a small trigger collider on it.
    /// Buttons detect presses via OnTriggerEnter with this collider.
    /// Uses the bone "f_index.03.R" from the Local VRRig skeleton (same approach as Bark).
    /// </summary>
    public class FingerTouch : MonoBehaviour
    {
        public static FingerTouch Instance { get; private set; }

        /// <summary>The fingertip transform (f_index.03.R bone).</summary>
        public Transform FingerTip { get; private set; }

        private GameObject _interactor;
        private bool _initialized;

        private void Awake()
        {
            Instance = this;
        }

        public void Initialize()
        {
            if (_initialized) return;

            // Strategy 1: Find via Local VRRig skeleton bone hierarchy
            Transform tip = FindFingerBone();

            // Strategy 2: Via VRRig.rightIndex.fingerBone3
            if (tip == null)
                tip = FindViaVRRig();

            if (tip == null)
            {
                Log.Error("FingerTouch: Could not find right index fingertip bone!");
                return;
            }

            FingerTip = tip;

            // Create a small sphere trigger collider on the fingertip
            _interactor = new GameObject("VC_FingerInteractor");
            _interactor.transform.SetParent(tip, false);
            _interactor.transform.localPosition = Vector3.zero;
            _interactor.transform.localScale = Vector3.one * (1f / 32f);

            // Use IgnoreRaycast layer so GT's platform system doesn't see it
            _interactor.layer = LayerMask.NameToLayer("Ignore Raycast");

            SphereCollider col = _interactor.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.5f;

            Rigidbody rb = _interactor.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            _initialized = true;

            // Only enable collider when menu is open
            _interactor.SetActive(false);

            Log.Info($"FingerTouch initialized on bone: {tip.name}");
        }

        private void Update()
        {
            if (!_initialized || _interactor == null) return;

            bool menuOpen = MenuManager.Instance != null && MenuManager.Instance.IsMenuOpen;
            if (_interactor.activeSelf != menuOpen)
                _interactor.SetActive(menuOpen);
        }

        private Transform FindFingerBone()
        {
            try
            {
                GameObject localRig = GameObject.Find("Player Objects/Local VRRig");
                if (localRig == null)
                {
                    Log.Warn("FingerTouch: 'Player Objects/Local VRRig' not found");
                    return null;
                }

                // Use recursive search for the bone name
                Transform bone = FindChildRecursive(localRig.transform, "f_index.03.R");
                if (bone != null)
                {
                    Log.Info($"Found fingertip bone via skeleton: {bone.name}");
                    return bone;
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn($"FindFingerBone failed: {ex.Message}");
            }

            return null;
        }

        private Transform FindViaVRRig()
        {
            try
            {
                if (GorillaTagger.Instance == null || GorillaTagger.Instance.offlineVRRig == null)
                    return null;

                VRRig rig = GorillaTagger.Instance.offlineVRRig;

                // Try rightIndex.fingerBone3 via reflection
                var rigType = rig.GetType();
                var rightIndexField = rigType.GetField("rightIndex",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (rightIndexField == null) return null;

                object rightIndex = rightIndexField.GetValue(rig);
                if (rightIndex == null) return null;

                var bone3Field = rightIndex.GetType().GetField("fingerBone3",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (bone3Field == null) return null;

                Transform bone3 = bone3Field.GetValue(rightIndex) as Transform;
                if (bone3 != null)
                {
                    Log.Info($"Found fingertip bone via VRRig.rightIndex.fingerBone3: {bone3.name}");
                    return bone3;
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn($"FindViaVRRig failed: {ex.Message}");
            }

            return null;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }

            return null;
        }
    }
}
