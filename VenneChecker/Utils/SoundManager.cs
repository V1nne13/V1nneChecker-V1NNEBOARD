using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Manages audio feedback for VenneChecker events.
    /// Tries procedural audio first, falls back to game sounds.
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        /// <summary>Singleton instance.</summary>
        public static SoundManager Instance { get; private set; }

        private AudioSource _audioSource;
        private AudioClip _menuOpenClip;
        private AudioClip _menuCloseClip;
        private AudioClip _scanCompleteClip;
        private AudioClip _scanStartClip;
        private bool _ready;
        private bool _enabled = true;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            try
            {
                // Create AudioSource
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 0f; // 2D sound
                _audioSource.volume = 0.5f;
                _audioSource.playOnAwake = false;
                _audioSource.loop = false;

                // Generate procedural clips
                _menuOpenClip = GenerateTone(800f, 0.12f);
                _menuCloseClip = GenerateTone(500f, 0.08f);
                _scanStartClip = GenerateTone(600f, 0.06f);
                _scanCompleteClip = GenerateTwoTone(1000f, 1400f, 0.2f);

                _ready = _menuOpenClip != null && _audioSource != null;

                Debug.Log($"[VenneChecker] SoundManager ready: {_ready}, AudioSource: {_audioSource != null}, Clips: open={_menuOpenClip != null} close={_menuCloseClip != null} scan={_scanCompleteClip != null}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[VenneChecker] SoundManager.Start failed: {ex.Message}");
                _ready = false;
            }
        }

        /// <summary>
        /// Plays the menu open sound effect.
        /// </summary>
        public void PlayMenuOpen()
        {
            PlayClip(_menuOpenClip, 0.4f);
        }

        /// <summary>
        /// Plays the menu close sound effect.
        /// </summary>
        public void PlayMenuClose()
        {
            PlayClip(_menuCloseClip, 0.3f);
        }

        /// <summary>
        /// Plays the scan started sound effect.
        /// </summary>
        public void PlayScanStart()
        {
            PlayClip(_scanStartClip, 0.35f);
        }

        /// <summary>
        /// Plays the scan complete sound effect (two-tone beep).
        /// </summary>
        public void PlayScanComplete()
        {
            PlayClip(_scanCompleteClip, 0.5f);
        }

        /// <summary>Enable or disable all sound effects.</summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (!_enabled || !_ready || _audioSource == null || clip == null)
            {
                // Fallback: try to use a game sound
                TryPlayGameSound();
                return;
            }

            try
            {
                _audioSource.PlayOneShot(clip, volume);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[VenneChecker] PlayClip failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Fallback: try to play a tap sound from the game.
        /// </summary>
        private void TryPlayGameSound()
        {
            try
            {
                if (GorillaTagger.Instance != null && _audioSource != null)
                {
                    // GorillaTagger has various sound references
                    AudioSource tagSource = GorillaTagger.Instance.GetComponent<AudioSource>();
                    if (tagSource != null && tagSource.clip != null)
                    {
                        _audioSource.PlayOneShot(tagSource.clip, 0.3f);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Generates a single-frequency sine wave audio clip.
        /// </summary>
        private static AudioClip GenerateTone(float frequency, float duration)
        {
            try
            {
                int sampleRate = 44100;
                int sampleCount = Mathf.Max(1, (int)(sampleRate * duration));
                float[] data = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    float t = (float)i / sampleRate;
                    float sample = Mathf.Sin(2f * Mathf.PI * frequency * t);

                    // Fade out envelope
                    float envelope = 1f - ((float)i / sampleCount);
                    data[i] = sample * envelope * 0.5f;
                }

                AudioClip clip = AudioClip.Create("vc_tone_" + (int)frequency, sampleCount, 1, sampleRate, false);
                if (clip != null)
                {
                    clip.SetData(data, 0);
                }
                return clip;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[VenneChecker] GenerateTone failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Generates a two-tone ascending beep.
        /// </summary>
        private static AudioClip GenerateTwoTone(float freq1, float freq2, float totalDuration)
        {
            try
            {
                int sampleRate = 44100;
                int totalSamples = Mathf.Max(1, (int)(sampleRate * totalDuration));
                int halfSamples = totalSamples / 2;
                float[] data = new float[totalSamples];

                for (int i = 0; i < totalSamples; i++)
                {
                    float t = (float)i / sampleRate;
                    float freq = (i < halfSamples) ? freq1 : freq2;
                    float sample = Mathf.Sin(2f * Mathf.PI * freq * t);

                    float envelope = 1f - ((float)i / totalSamples);
                    data[i] = sample * envelope * 0.5f;
                }

                AudioClip clip = AudioClip.Create("vc_twotone", totalSamples, 1, sampleRate, false);
                if (clip != null)
                {
                    clip.SetData(data, 0);
                }
                return clip;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[VenneChecker] GenerateTwoTone failed: {ex.Message}");
                return null;
            }
        }
    }
}
