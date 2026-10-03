using UnityEngine;
using UnityEngine.InputSystem;

namespace TagGame.Gameplay
{
    /// <summary>
    /// Simple top-down / third-person movement for the Runner (the human player).
    /// Attach to a CharacterController-driven capsule, or swap MovePlayer for
    /// Rigidbody.MovePosition if you prefer physics-driven movement.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float turnSpeedDegrees = 720f;
        [SerializeField] private float gravity = -20f;

        [Header("Sprint (optional, drains stamina)")]
        [SerializeField] private bool allowSprint = true;
        [SerializeField] private float sprintMultiplier = 1.6f;
        [SerializeField] private float maxStamina = 4f;
        [SerializeField] private float staminaRegenPerSecond = 1f;

        [Header("Lobby circle (start screen / between rounds)")]
        [SerializeField] private float lobbyRadius = 2f;
        [SerializeField] private Color lobbyCircleColor = new Color(1f, 0.92f, 0.25f, 1f);

        private CharacterController _controller;
        private float _verticalVelocity;
        private float _stamina;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private bool _confinedToLobby;
        private GameObject _lobbyCircle;

        public float StaminaNormalized => maxStamina <= 0f ? 0f : Mathf.Clamp01(_stamina / maxStamina);
        public bool IsTagged { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _stamina = maxStamina;
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            CreateLobbyCircle();
            _confinedToLobby = true; // Start screen: stay inside the little circle.
        }

        /// <summary>Flat disc on the ground marking the lobby area. Reuses the player's
        /// own material so it still exists in WebGL builds (Shader.Find can be stripped).</summary>
        private void CreateLobbyCircle()
        {
            _lobbyCircle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _lobbyCircle.name = "LobbyCircle";
            var col = _lobbyCircle.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _lobbyCircle.transform.position = new Vector3(_homePosition.x, 0.02f, _homePosition.z);
            _lobbyCircle.transform.localScale = new Vector3(lobbyRadius * 2f, 0.01f, lobbyRadius * 2f);

            var circleRenderer = _lobbyCircle.GetComponent<Renderer>();
            var playerRenderer = GetComponent<Renderer>();
            if (playerRenderer != null && playerRenderer.sharedMaterial != null)
            {
                var mat = new Material(playerRenderer.sharedMaterial) { color = lobbyCircleColor };
                circleRenderer.sharedMaterial = mat;
            }
            circleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Teleports back to the lobby circle and confines the player to it.</summary>
        public void EnterLobby()
        {
            IsTagged = false;
            _verticalVelocity = 0f;
            _confinedToLobby = true;
            TeleportTo(_homePosition, _homeRotation);
            if (_lobbyCircle != null) _lobbyCircle.SetActive(true);
        }

        /// <summary>Lets the player leave the circle (round started).</summary>
        public void ExitLobby()
        {
            _confinedToLobby = false;
            if (_lobbyCircle != null) _lobbyCircle.SetActive(false);
        }

        private void TeleportTo(Vector3 position, Quaternion rotation)
        {
            // CharacterController overrides transform changes unless disabled first.
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;
        }

        private void Update()
        {
            if (IsTagged)
            {
                return; // Frozen once tagged; TagGameManager decides what happens next.
            }

            Vector2 move = ReadMoveInput();
            Vector3 inputDir = new Vector3(move.x, 0f, move.y);
            if (inputDir.sqrMagnitude > 1f)
            {
                inputDir.Normalize();
            }

            bool wantsSprint = allowSprint && IsSprintHeld() && _stamina > 0f;
            float speed = moveSpeed * (wantsSprint ? sprintMultiplier : 1f);

            if (wantsSprint && inputDir.sqrMagnitude > 0.01f)
            {
                _stamina = Mathf.Max(0f, _stamina - Time.deltaTime);
            }
            else
            {
                _stamina = Mathf.Min(maxStamina, _stamina + staminaRegenPerSecond * Time.deltaTime);
            }

            Vector3 worldMove = transform.TransformDirection(new Vector3(inputDir.x, 0f, inputDir.z));
            // Re-flatten in case of any rotation drift.
            worldMove = new Vector3(inputDir.x, 0f, inputDir.z);

            if (worldMove.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(worldMove, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeedDegrees * Time.deltaTime);
            }

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -1f;
            }
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = worldMove * speed + Vector3.up * _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);

            if (_confinedToLobby)
            {
                Vector3 offset = transform.position - _homePosition;
                offset.y = 0f;
                if (offset.magnitude > lobbyRadius)
                {
                    Vector3 clamped = _homePosition + offset.normalized * lobbyRadius;
                    clamped.y = transform.position.y;
                    TeleportTo(clamped, transform.rotation);
                }
            }
        }

        /// <summary>
        /// Reads WASD/arrow keys (keyboard) and the left stick (gamepad) using the
        /// new Input System's low-level device API directly — no .inputactions
        /// asset required. This project has Active Input Handling set to
        /// "Input System Package (New)" only, so the legacy UnityEngine.Input
        /// class is disabled and would throw at runtime.
        /// </summary>
        private static Vector2 ReadMoveInput()
        {
            Vector2 move = Vector2.zero;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
            }

            var gamepad = Gamepad.current;
            if (gamepad != null && move.sqrMagnitude < 0.01f)
            {
                move = gamepad.leftStick.ReadValue();
            }

            // On-screen joystick (touch devices).
            if (move.sqrMagnitude < 0.01f)
            {
                move = MobileControls.MoveInput;
            }

            return move;
        }

        private static bool IsSprintHeld()
        {
            bool keyboardSprint = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
            bool gamepadSprint = Gamepad.current != null && Gamepad.current.leftShoulder.isPressed;
            return keyboardSprint || gamepadSprint || MobileControls.SprintHeld;
        }

        /// <summary>Called by TagGameManager when a Tagger touches this player.</summary>
        public void MarkTagged()
        {
            IsTagged = true;
        }

        /// <summary>Called when a new round starts.</summary>
        public void ResetForNewRound()
        {
            IsTagged = false;
            _stamina = maxStamina;
            _verticalVelocity = 0f;
            ExitLobby();
        }
    }
}
