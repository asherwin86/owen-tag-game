using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TagGame.Gameplay
{
    /// <summary>
    /// Third-person orbit camera. Follows the target; hold the RIGHT mouse button and drag to swing
    /// around / tilt, scroll wheel to zoom (controller: right stick, triggers to zoom; touch: drag an empty part of the screen, pinch to zoom), Home (or middle click) to reset. Movement in
    /// PlayerController is relative to this camera, so "up" always runs away from the lens.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 4f, -7f);
        [SerializeField] private float followSpeed = 8f;
        [SerializeField] private float lookAtHeightOffset = 1f;
        [SerializeField] private float turnSensitivity = 0.25f;

        private float _yaw, _pitchDelta, _zoom = 1f;
        private readonly List<int> _camTouches = new List<int>();
        private float _lastPinch;

        public void SetTarget(Transform newTarget) => target = newTarget;

        /// <summary>Camera offset from the target at yaw 0 (bigger maps use a higher, wider view).</summary>
        public Vector3 Offset
        {
            get => offset;
            set { if (value != offset) { offset = value; _zoom = 1f; _pitchDelta = 0f; } }
        }

        public float Yaw => _yaw;

        private void LateUpdate()
        {
            if (target == null) return;
            ReadInput();

            float dist = offset.magnitude * _zoom;
            float basePitch = Mathf.Asin(Mathf.Clamp(offset.y / Mathf.Max(0.01f, offset.magnitude), -1f, 1f)) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(basePitch + _pitchDelta, 8f, 80f);
            Vector3 dir = Quaternion.Euler(pitch, _yaw, 0f) * Vector3.back; // behind the target, tilted up
            Vector3 desiredPosition = target.position + dir * dist;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.deltaTime);
            transform.LookAt(target.position + Vector3.up * lookAtHeightOffset);
        }

        private void ReadInput()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    _yaw += d.x * turnSensitivity;
                    _pitchDelta = Mathf.Clamp(_pitchDelta + d.y * turnSensitivity, -40f, 50f);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) _zoom = Mathf.Clamp(_zoom - Mathf.Sign(scroll) * 0.1f, 0.45f, 1.8f);
                if (mouse.middleButton.wasPressedThisFrame) ResetView();
            }
            ReadTouch();
            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 rs = pad.rightStick.ReadValue();
                if (rs.sqrMagnitude > 0.04f)
                {
                    _yaw += rs.x * 150f * Time.deltaTime;
                    _pitchDelta = Mathf.Clamp(_pitchDelta + rs.y * 80f * Time.deltaTime, -40f, 50f);
                }
                float zoomIn = pad.leftTrigger.ReadValue(), zoomOut = pad.rightTrigger.ReadValue();
                if (zoomIn > 0.1f || zoomOut > 0.1f) _zoom = Mathf.Clamp(_zoom + (zoomOut - zoomIn) * 0.9f * Time.deltaTime, 0.45f, 1.8f);
                if (pad.rightStickButton.wasPressedThisFrame) ResetView();
            }
            var kb = Keyboard.current;
            if (kb != null && kb.homeKey.wasPressedThisFrame) ResetView();
        }

        /// <summary>Touch: a finger that starts on empty screen (not the joystick or a button) drags the camera;
        /// two such fingers pinch to zoom.</summary>
        private void ReadTouch()
        {
            var ts = Touchscreen.current;
            if (ts == null) return;
            var active = new List<TouchControl>();
            foreach (var t in ts.touches)
            {
                int id = t.touchId.ReadValue();
                var ph = t.phase.ReadValue();
                if (ph == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(id);
                    if (!overUi && !_camTouches.Contains(id)) { _camTouches.Add(id); _lastPinch = 0f; }
                }
                else if (ph == UnityEngine.InputSystem.TouchPhase.Ended || ph == UnityEngine.InputSystem.TouchPhase.Canceled || ph == UnityEngine.InputSystem.TouchPhase.None)
                {
                    _camTouches.Remove(id);
                    continue;
                }
                if (_camTouches.Contains(id)) active.Add(t);
            }
            if (active.Count == 1)
            {
                Vector2 d = active[0].delta.ReadValue();
                _yaw += d.x * 0.2f;
                _pitchDelta = Mathf.Clamp(_pitchDelta + d.y * 0.2f, -40f, 50f);
                _lastPinch = 0f;
            }
            else if (active.Count >= 2)
            {
                float dist = Vector2.Distance(active[0].position.ReadValue(), active[1].position.ReadValue());
                if (_lastPinch > 0f) _zoom = Mathf.Clamp(_zoom * (_lastPinch / Mathf.Max(1f, dist)), 0.45f, 1.8f);
                _lastPinch = dist;
            }
            else _lastPinch = 0f;
        }

        private void ResetView() { _yaw = 0f; _pitchDelta = 0f; _zoom = 1f; }
    }
}
