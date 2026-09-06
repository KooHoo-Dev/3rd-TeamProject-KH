using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    public readonly struct PlayerInputData
    {
        public readonly float MoveX;
        public readonly float MoveY;
        public readonly bool JumpPressed;
        public readonly bool ClimbTogglePressed;

        public PlayerInputData(float moveX, float moveY, bool jumpPressed, bool climbTogglePressed)
        {
            MoveX = moveX;
            MoveY = moveY;
            JumpPressed = jumpPressed;
            ClimbTogglePressed = climbTogglePressed;
        }
    }

    /// <summary>플레이어 입력을 Update에서 수집해 FixedUpdate에 전달</summary>
    public sealed class PlayerInputReader
    {
        public PlayerInputData CurrentInput { get; private set; }

        public void Tick()
        {
            if (ChatInputState.IsTyping)
            {
                CurrentInput = new PlayerInputData(0f, 0f, false, false);
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                CurrentInput = new PlayerInputData(0f, 0f, false, false);
                return;
            }

            CurrentInput = new PlayerInputData(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f),
                keyboard.spaceKey.isPressed,
                keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame);
        }
    }
}
