using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Shows on-screen text notifications in VR.
    /// Text is parented to the camera so it follows your head.
    /// </summary>
    public class NotificationManager : MonoBehaviour
    {
        public static NotificationManager Instance { get; private set; }

        private const float DisplayDuration = 6f;
        private const float FadeOutTime = 1.5f;
        private const float NotifSpacing = 0.032f;
        private const float DistFromCam = 1.2f;

        private TMP_FontAsset _font;
        private readonly List<NotifEntry> _active = new List<NotifEntry>();

        private class NotifEntry
        {
            public GameObject Obj;
            public TextMeshPro Text;
            public float TimeLeft;
            public int Slot;
        }

        private void Awake()
        {
            Instance = this;
        }

        public void SetFont(TMP_FontAsset font)
        {
            _font = font;
        }

        public void Show(string message, Color? color = null)
        {
            if (Camera.main == null) return;

            Color c = color ?? new Color(1f, 0.3f, 0.2f, 1f);

            int slot = 0;
            while (SlotTaken(slot)) slot++;

            // Create as child of camera so it follows your head
            GameObject obj = new GameObject("VC_Notif");
            obj.transform.SetParent(Camera.main.transform, false);

            // Position: in front of camera, upper-center-right area
            obj.transform.localPosition = new Vector3(0.85f, 0.18f - slot * NotifSpacing, DistFromCam);
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one * 0.025f;

            TextMeshPro tmp = obj.AddComponent<TextMeshPro>();
            tmp.text = message;
            tmp.fontSize = 7f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = c;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.sortingOrder = 100;
            tmp.rectTransform.sizeDelta = new Vector2(120f, 8f);

            if (_font != null) tmp.font = _font;

            var entry = new NotifEntry
            {
                Obj = obj,
                Text = tmp,
                TimeLeft = DisplayDuration,
                Slot = slot
            };
            _active.Add(entry);

            Log.Info($"[NOTIF] {message}");
        }

        public void ShowCheatAlert(string playerName, List<string> flaggedMods)
        {
            if (flaggedMods == null || flaggedMods.Count == 0) return;

            string modsStr = string.Join(", ", flaggedMods);
            string msg = $"\u26A0 {playerName}: {modsStr}";
            Show(msg, new Color(1f, 0.2f, 0.15f, 1f));
        }

        private void Update()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var entry = _active[i];
                entry.TimeLeft -= Time.deltaTime;

                if (entry.TimeLeft <= 0f || entry.Obj == null)
                {
                    if (entry.Obj != null) Destroy(entry.Obj);
                    _active.RemoveAt(i);
                    continue;
                }

                // Fade out
                if (entry.TimeLeft < FadeOutTime && entry.Text != null)
                {
                    float alpha = entry.TimeLeft / FadeOutTime;
                    Color col = entry.Text.color;
                    col.a = alpha;
                    entry.Text.color = col;
                }
            }
        }

        private bool SlotTaken(int slot)
        {
            foreach (var e in _active)
                if (e.Slot == slot) return true;
            return false;
        }
    }
}
