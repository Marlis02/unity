using System;
using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Stats that set how a character's motions look: the pace of walking and running in place
    /// and the height and speed of its jumps. Loaded from character.json for imported characters.
    /// </summary>
    [Serializable]
    public class CharacterProfile
    {
        public string displayName = "Персонаж";
        public float walkSpeed = 3f;
        public float runSpeed = 6f;
        public float jumpHeight = 1.2f;
        public float gravity = 20f;

        public CharacterProfile Clone()
        {
            return (CharacterProfile)MemberwiseClone();
        }

        public float JumpVelocity => Mathf.Sqrt(2f * Mathf.Max(0.01f, gravity) * Mathf.Max(0f, jumpHeight));
    }
}
