using System;
using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Simple MonoBehaviour that runs a callback after a delay.
    /// Lives on the VenneChecker_Manager object so it persists even when menu is closed.
    /// </summary>
    public class DelayedAction : MonoBehaviour
    {
        public static DelayedAction Instance { get; private set; }

        private Action _pendingAction;
        private float _timer;
        private bool _waiting;

        private Action _pendingAction2;
        private float _timer2;
        private bool _waiting2;

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>Run an action after a delay (seconds). Slot 1.</summary>
        public void RunAfter(float delay, Action action)
        {
            _pendingAction = action;
            _timer = delay;
            _waiting = true;
            Log.Info($"[DelayedAction] Queued action in {delay}s");
        }

        /// <summary>Run a second action after a delay. Slot 2.</summary>
        public void RunAfter2(float delay, Action action)
        {
            _pendingAction2 = action;
            _timer2 = delay;
            _waiting2 = true;
        }

        private void Update()
        {
            if (_waiting)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    _waiting = false;
                    try { _pendingAction?.Invoke(); }
                    catch (Exception ex) { Log.Error($"[DelayedAction] Slot1 failed: {ex.Message}"); }
                    _pendingAction = null;
                }
            }

            if (_waiting2)
            {
                _timer2 -= Time.deltaTime;
                if (_timer2 <= 0f)
                {
                    _waiting2 = false;
                    try { _pendingAction2?.Invoke(); }
                    catch (Exception ex) { Log.Error($"[DelayedAction] Slot2 failed: {ex.Message}"); }
                    _pendingAction2 = null;
                }
            }
        }
    }
}
