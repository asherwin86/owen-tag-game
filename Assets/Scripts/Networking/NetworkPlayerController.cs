using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TagGame.Networking
{
    /// <summary>
    /// Networked player movement + tag state for real player-vs-player tag
    /// (online via Relay or LAN). Each client only moves its own owned
    /// CharacterController and sends its transform to the server, which is
    /// the single source of truth for who is tagged.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class NetworkPlayerController : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float sprintMultiplier = 1.6f;
        [SerializeField] private float turnSpeedDegrees = 720f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private Renderer[] bodyRenderers; // recolored when tagged as "it"
        [SerializeField] private Color runnerColor = Color.cyan;
        [SerializeField] private Color itColor = Color.red;

        private CharacterController _controller;
        private float _verticalVelocity;

        // Server-owned: true while this player is "it" and can tag others.
        public NetworkVariable<bool> IsIt = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Server-owned: player display name synced for scoreboards.
        public NetworkVariable<Unity.Collections.FixedString32Bytes> PlayerName = new NetworkVariable<Unity.Collections.FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            IsIt.OnValueChanged += HandleIsItChanged;
            HandleIsItChanged(false, IsIt.Value);

            if (IsOwner)
            {
                PlayerName.Value = $"Player{OwnerClientId}";
            }
        }

        public override void OnNetworkDespawn()
        {
            IsIt.OnValueChanged -= HandleIsItChanged;
        }

        private void HandleIsItChanged(bool previous, bool current)
        {
            Color c = current ? itColor : runnerColor;
            foreach (var r in bodyRenderers)
            {
                if (r != null) r.material.color = c;
            }
        }

        private void Update()
        {
            if (!IsOwner) return; // only the owning client drives its own capsule

            Vector2 move = ReadMoveInput();
            Vector3 inputDir = new Vector3(move.x, 0f, move.y);
            if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

            bool sprint = IsSprintHeld();
            float speed = moveSpeed * (sprint ? sprintMultiplier : 1f);

            if (inputDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(inputDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeedDegrees * Time.deltaTime);
            }

            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -1f;
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = inputDir * speed + Vector3.up * _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);
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

            return move;
        }

        private static bool IsSprintHeld()
        {
            bool keyboardSprint = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
            bool gamepadSprint = Gamepad.current != null && Gamepad.current.leftShoulder.isPressed;
            return keyboardSprint || gamepadSprint;
        }

        /// <summary>
        /// Owner-driven "I think I touched someone" trigger. The server
        /// re-validates distance and IsIt state before accepting the tag, so a
        /// modified client can't tag through walls or tag while not "it".
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!IsOwner || !IsIt.Value) return;

            var otherPlayer = hit.collider.GetComponentInParent<NetworkPlayerController>();
            if (otherPlayer != null && otherPlayer != this)
            {
                RequestTagServerRpc(otherPlayer.NetworkObjectId);
            }
        }

        [ServerRpc]
        private void RequestTagServerRpc(ulong targetNetworkObjectId, ServerRpcParams rpcParams = default)
        {
            if (!IsIt.Value) return; // stale/replayed request from a client that lost "it" status

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var targetObj))
            {
                return;
            }

            var target = targetObj.GetComponent<NetworkPlayerController>();
            if (target == null || target == this) return;

            float distance = Vector3.Distance(transform.position, target.transform.position);
            const float maxTagDistance = 3.0f; // generous slack for latency; tune via Remote Config if needed
            if (distance > maxTagDistance) return;

            NetworkTagGameManager.Instance?.ServerHandleTag(this, target);
        }
    }
}
