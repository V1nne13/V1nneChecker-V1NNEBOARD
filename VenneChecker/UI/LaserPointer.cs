using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Laser pointer with lock-on targeting. When aimed near a player,
    /// snaps to them and shows their name. Trigger to scan.
    /// </summary>
    public class LaserPointer : MonoBehaviour
    {
        public static LaserPointer Instance { get; private set; }
        public VRRig LockedRig { get; private set; }

        private bool _triggerWasHeld;
        private bool _initialized;

        private LineRenderer _beam;
        private GameObject _hitMarker;
        private MeshRenderer _hitRenderer;
        private Material _beamMat;
        private Material _hitMat;

        // Lock-on label
        private GameObject _labelObj;
        private TextMeshPro _labelTmp;

        // Lock-on state
        private VRRig _lockTarget;
        private float _lockTimer;
        private const float LockOnRadius = 3f;
        private const float LockOnAngle = 15f; // degrees

        private void Awake()
        {
            Instance = this;
        }

        public void Initialize()
        {
            _initialized = true;

            Shader sh = Shader.Find("Unlit/Color")
                ?? Shader.Find("GUI/Text Shader")
                ?? Shader.Find("Standard");

            // ── Single beam ──
            GameObject beamObj = new GameObject("VC_Laser");
            beamObj.transform.SetParent(transform, false);
            _beam = beamObj.AddComponent<LineRenderer>();
            _beamMat = new Material(sh);
            _beam.material = _beamMat;
            _beam.positionCount = 2;
            _beam.useWorldSpace = true;
            _beam.startWidth = 0.012f;
            _beam.endWidth = 0.004f;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;
            beamObj.SetActive(false);

            // ── Hit marker (diamond) ──
            _hitMarker = new GameObject("VC_HitMarker");
            _hitMarker.transform.SetParent(transform, false);
            MeshFilter mf = _hitMarker.AddComponent<MeshFilter>();
            mf.mesh = CreateDiamondMesh();
            _hitRenderer = _hitMarker.AddComponent<MeshRenderer>();
            _hitMat = new Material(sh);
            _hitRenderer.material = _hitMat;
            _hitRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _hitMarker.SetActive(false);

            // ── Lock-on name label ──
            _labelObj = new GameObject("VC_LockLabel");
            _labelObj.transform.SetParent(transform, false);
            _labelTmp = _labelObj.AddComponent<TextMeshPro>();
            _labelTmp.fontSize = 4f;
            _labelTmp.fontStyle = FontStyles.Bold;
            _labelTmp.color = Color.white;
            _labelTmp.alignment = TextAlignmentOptions.Center;
            _labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
            _labelTmp.overflowMode = TextOverflowModes.Overflow;
            _labelTmp.sortingOrder = 100;
            _labelTmp.rectTransform.sizeDelta = new Vector2(40f, 5f);
            _labelObj.transform.localScale = Vector3.one * 0.015f;
            _labelObj.SetActive(false);

            // Try to get font
            try
            {
                var font = TMPro.TMP_Settings.defaultFontAsset;
                if (font != null) _labelTmp.font = font;
            }
            catch { }

            Log.Info("LaserPointer initialized");
        }

        private void Update()
        {
            if (!_initialized || GorillaTagger.Instance == null) return;

            bool menuOpen = MenuManager.Instance != null && MenuManager.Instance.IsMenuOpen;
            bool onModCheckerPage = MenuManager.Instance != null && MenuManager.Instance.CurrentPageIndex == 1;

            if (!menuOpen || !onModCheckerPage)
            {
                HideLaser();
                _lockTarget = null;
                _triggerWasHeld = false;
                return;
            }

            DoLaser();
        }

        private void HideLaser()
        {
            if (_beam != null) _beam.gameObject.SetActive(false);
            if (_hitMarker != null) _hitMarker.SetActive(false);
            if (_labelObj != null) _labelObj.SetActive(false);
        }

        private void DoLaser()
        {
            bool gripHeld = ControllerInputPoller.instance.rightControllerGripFloat > 0.5f;
            if (!gripHeld)
            {
                HideLaser();
                _lockTarget = null;
                _triggerWasHeld = false;
                return;
            }

            Transform rightHand = GorillaTagger.Instance.rightHandTransform;
            if (rightHand == null) return;

            Vector3 origin = rightHand.position;
            Vector3 direction = -rightHand.up;
            bool triggerHeld = ControllerInputPoller.instance.rightControllerIndexFloat > 0.5f;

            Color laserColor = SettingsPage.CurrentLaserColor;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);

            // ── Lock-on: find nearest player near the laser line ──
            VRRig bestRig = FindLockOnTarget(origin, direction);

            if (bestRig != null)
                _lockTarget = bestRig;
            else if (_lockTarget != null)
            {
                // Keep lock for a short time even if aim drifts
                _lockTimer -= Time.deltaTime;
                if (_lockTimer <= 0f)
                    _lockTarget = null;
            }

            if (bestRig != null)
                _lockTimer = 0.5f;

            // ── Determine endpoint ──
            Vector3 endPoint;
            bool locked = _lockTarget != null;

            if (locked)
            {
                endPoint = _lockTarget.transform.position + Vector3.up * 0.3f; // Aim at chest area
                LockedRig = _lockTarget;
            }
            else
            {
                RaycastHit hit;
                if (Physics.Raycast(origin, direction, out hit, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    endPoint = hit.point;
                    VRRig hitRig = hit.collider.GetComponentInParent<VRRig>();
                    if (hitRig != null) LockedRig = hitRig;
                }
                else
                {
                    endPoint = origin + direction * 80f;
                }
            }

            // ── Colors ──
            Color beamColor;
            if (triggerHeld)
                beamColor = Color.white;
            else if (locked)
                beamColor = new Color(
                    Mathf.Min(laserColor.r * 1.5f, 1f),
                    Mathf.Min(laserColor.g * 1.5f, 1f),
                    Mathf.Min(laserColor.b * 1.5f, 1f), 1f);
            else
                beamColor = new Color(laserColor.r * pulse, laserColor.g * pulse, laserColor.b * pulse, 1f);

            _beamMat.color = beamColor;

            // ── Show beam ──
            _beam.gameObject.SetActive(true);
            _beam.SetPosition(0, origin);
            _beam.SetPosition(1, endPoint);

            // ── Hit marker ──
            _hitMarker.SetActive(true);
            _hitMarker.transform.position = endPoint;
            _hitMarker.transform.rotation *= Quaternion.Euler(0f, 200f * Time.deltaTime, 0f);
            float mScale = locked ? 0.07f : 0.04f;
            mScale *= 1f + 0.15f * Mathf.Sin(Time.time * 8f);
            _hitMarker.transform.localScale = Vector3.one * mScale;
            _hitMat.color = beamColor;

            // ── Lock-on label ──
            if (locked && _labelObj != null)
            {
                _labelObj.SetActive(true);
                _labelObj.transform.position = endPoint + Vector3.up * 0.2f;
                // Face camera
                if (Camera.main != null)
                    _labelObj.transform.rotation = Camera.main.transform.rotation;

                string playerName = GetPlayerName(_lockTarget);
                _labelTmp.text = playerName;
                _labelTmp.color = beamColor;
            }
            else if (_labelObj != null)
            {
                _labelObj.SetActive(false);
            }

            // ── Scan on trigger press ──
            bool triggerJustPressed = triggerHeld && !_triggerWasHeld;
            _triggerWasHeld = triggerHeld;

            if (triggerJustPressed)
            {
                VRRig targetRig = LockedRig;
                if (targetRig == null)
                    targetRig = FindNearestVRRig(endPoint, 3f);

                if (targetRig != null && PlayerScanner.Instance != null)
                {
                    if (SoundManager.Instance != null)
                        SoundManager.Instance.PlayScanStart();

                    try
                    {
                        PlayerInfo info = PlayerScanner.Instance.ScanPlayer(targetRig);
                        if (info != null && MenuManager.Instance != null)
                            MenuManager.Instance.DisplayPlayerInfo(info);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"ScanPlayer failed: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Finds the best lock-on target near the laser direction.
        /// </summary>
        private VRRig FindLockOnTarget(Vector3 origin, Vector3 direction)
        {
            VRRig[] allRigs = FindObjectsByType<VRRig>(FindObjectsSortMode.None);
            VRRig best = null;
            float bestAngle = LockOnAngle;

            foreach (VRRig rig in allRigs)
            {
                if (rig == null) continue;
                try
                {
                    if (GorillaTagger.Instance != null && rig == GorillaTagger.Instance.offlineVRRig)
                        continue;
                }
                catch { }

                Vector3 toRig = rig.transform.position - origin;
                float dist = toRig.magnitude;
                if (dist > LockOnRadius * 10f || dist < 0.5f) continue;

                float angle = Vector3.Angle(direction, toRig);

                // Scale acceptable angle by distance (more forgiving close up)
                float maxAngle = LockOnAngle * Mathf.Clamp01(LockOnRadius / dist);
                maxAngle = Mathf.Max(maxAngle, 5f);

                if (angle < maxAngle && angle < bestAngle)
                {
                    bestAngle = angle;
                    best = rig;
                }
            }

            return best;
        }

        private string GetPlayerName(VRRig rig)
        {
            try
            {
                var prop = rig.GetType().GetProperty("Creator",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    object creator = prop.GetValue(rig, null);
                    if (creator != null)
                    {
                        var nameProp = creator.GetType().GetProperty("NickName");
                        if (nameProp != null)
                            return nameProp.GetValue(creator, null)?.ToString() ?? rig.gameObject.name;
                    }
                }
            }
            catch { }
            return rig.gameObject.name;
        }

        private static Mesh CreateDiamondMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "Diamond";
            mesh.vertices = new Vector3[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(-1f, 0f, 0f),
                new Vector3(0f, 0f, -1f),
                new Vector3(0f, -1f, 0f),
            };
            mesh.triangles = new int[]
            {
                0,1,2, 0,2,3, 0,3,4, 0,4,1,
                5,2,1, 5,3,2, 5,4,3, 5,1,4,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private VRRig FindNearestVRRig(Vector3 position, float maxDistance)
        {
            VRRig[] allRigs = FindObjectsByType<VRRig>(FindObjectsSortMode.None);
            VRRig closest = null;
            float closestDist = maxDistance;

            foreach (VRRig rig in allRigs)
            {
                if (rig == null) continue;
                try
                {
                    if (GorillaTagger.Instance != null && rig == GorillaTagger.Instance.offlineVRRig)
                        continue;
                }
                catch { }

                float dist = Vector3.Distance(position, rig.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = rig;
                }
            }

            return closest;
        }
    }
}
