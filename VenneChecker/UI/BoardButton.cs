using System;
using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// 3D button: dark cube body for depth + rounded rect face on front.
    /// Big Z gap between layers prevents clipping.
    /// </summary>
    public class BoardButton : MonoBehaviour
    {
        public Action OnPressed;
        public string Label { get; private set; }

        private Renderer _faceRenderer;
        private TextMeshPro _label;
        private Material _mat;

        private float _cooldown;

        private static readonly Color DefaultNormal = new Color(0.6f, 0.08f, 0.08f, 1f);
        private static readonly Color BaseColor = new Color(0.08f, 0.02f, 0.02f, 1f);
        private const float PressCooldown = 3f;
        private const float BtnDepth = 0.006f;

        public static BoardButton Create(
            string name, Transform parent, Vector3 localPos, Vector3 size,
            string label, float fontSize, Action onPressed,
            Color? normalColor = null, Color? hoverColor = null,
            Shader shader = null, TMP_FontAsset font = null)
        {
            Shader sh = Shader.Find("Unlit/Color")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("GUI/Text Shader")
                ?? shader;

            float btnWidth = size.x;
            float btnHeight = size.y;
            Color faceColor = normalColor ?? DefaultNormal;

            // ── Cube body — gives visible 3D sides/depth ──
            GameObject btnObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            btnObj.name = name;
            btnObj.layer = LayerMask.NameToLayer("Ignore Raycast");
            btnObj.transform.SetParent(parent, false);
            btnObj.transform.localPosition = localPos;
            btnObj.transform.localScale = new Vector3(btnWidth - 0.003f, btnHeight - 0.002f, BtnDepth);

            var existingCol = btnObj.GetComponent<BoxCollider>();
            if (existingCol != null) UnityEngine.Object.Destroy(existingCol);

            // Collider covers the full button area
            var col = btnObj.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(btnWidth / (btnWidth - 0.003f), btnHeight / (btnHeight - 0.002f), 2f);
            col.center = Vector3.zero;

            var rb = btnObj.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // Dark base material for the cube sides
            Material baseMat = new Material(sh);
            baseMat.color = BaseColor;
            if (baseMat.HasProperty("_Color")) baseMat.SetColor("_Color", BaseColor);
            Renderer baseRend = btnObj.GetComponent<Renderer>();
            baseRend.material = baseMat;
            baseRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            baseRend.receiveShadows = false;

            // ── Rounded rect face — clearly in front of cube (Z gap = 0.005) ──
            float faceZ = localPos.z - BtnDepth * 0.5f - 0.005f;
            float cornerRadius = Mathf.Min(btnWidth, btnHeight) * 0.22f;
            cornerRadius = Mathf.Max(cornerRadius, 0.004f);

            GameObject face = RoundedRectMesh.CreatePanel(name + "_Face", parent,
                new Vector3(localPos.x, localPos.y, faceZ),
                btnWidth, btnHeight, cornerRadius, faceColor, sh);

            // Component setup
            BoardButton btn = btnObj.AddComponent<BoardButton>();
            btn.Label = label;
            btn.OnPressed = onPressed;
            btn._faceRenderer = face.GetComponent<Renderer>();
            btn._mat = btn._faceRenderer.material;

            // ── Text — in front of face ──
            float zText = faceZ - 0.003f;
            GameObject textObj = new GameObject(name + "_Text");
            textObj.transform.SetParent(parent, false);
            textObj.transform.localPosition = new Vector3(localPos.x, localPos.y, zText);
            textObj.transform.localScale = Vector3.one * 0.015f;

            TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.fontSize = fontSize * 1.3f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.sortingOrder = 10;
            tmp.rectTransform.sizeDelta = new Vector2(btnWidth / 0.015f, btnHeight / 0.015f);
            if (font != null) tmp.font = font;

            btn._label = tmp;

            return btn;
        }

        public void SetLabel(string text)
        {
            Label = text;
            if (_label != null) _label.text = text;
        }

        public void SetColor(Color color)
        {
            if (_mat != null)
            {
                _mat.color = color;
                if (_mat.HasProperty("_BaseColor"))
                    _mat.SetColor("_BaseColor", color);
                if (_mat.HasProperty("_Color"))
                    _mat.SetColor("_Color", color);
            }
        }

        private void Update()
        {
            if (_cooldown > 0f)
                _cooldown -= Time.deltaTime;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.name != "VC_FingerInteractor") return;

            if (_cooldown <= 0f)
            {
                _cooldown = PressCooldown;
                try { SoundManager.Instance?.PlayMenuOpen(); } catch { }
                try { OnPressed?.Invoke(); } catch (Exception ex) { Log.Error($"Button '{Label}' press failed: {ex.Message}"); }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.gameObject.name != "VC_FingerInteractor") return;
        }
    }
}
