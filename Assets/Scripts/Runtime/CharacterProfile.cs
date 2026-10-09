using System;
using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Tunable movement stats of a character. Loaded from character.json for imported
    /// characters and editable at runtime from the playground's tuning panel.
    /// </summary>
    [Serializable]
    public class CharacterProfile
    {
        public string displayName = "Персонаж";
        public float walkSpeed = 3f;
        public float runSpeed = 6f;
        public float jumpHeight = 1.2f;
        public float gravity = 20f;
        public float turnSpeed = 720f;
        [Range(0f, 1f)] public float airControl = 0.4f;
        public float acceleration = 30f;

        public CharacterProfile Clone()
        {
            return (CharacterProfile)MemberwiseClone();
        }

        public float JumpVelocity => Mathf.Sqrt(2f * Mathf.Max(0.01f, gravity) * Mathf.Max(0f, jumpHeight));
    }
}
