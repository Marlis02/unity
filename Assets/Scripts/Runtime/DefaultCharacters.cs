using System.Collections.Generic;
using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// Built-in primitive characters with deliberately different proportions and stats, so the
    /// playground is useful before any real character has been imported.
    /// </summary>
    public static class DefaultCharacters
    {
        public struct Spec
        {
            public string name;
            public Color color;
            public float height;
            public float width;
            public CharacterProfile profile;
        }

        public static IEnumerable<Spec> All()
        {
            yield return new Spec
            {
                name = "Рыцарь",
                color = new Color(0.25f, 0.45f, 0.85f),
                height = 1.8f,
                width = 0.55f,
                profile = new CharacterProfile { displayName = "Рыцарь", walkSpeed = 3f, runSpeed = 6f, jumpHeight = 1.2f, gravity = 20f },
            };
            yield return new Spec
            {
                name = "Скаут",
                color = new Color(0.2f, 0.7f, 0.35f),
                height = 1.6f,
                width = 0.42f,
                profile = new CharacterProfile { displayName = "Скаут", walkSpeed = 4f, runSpeed = 9f, jumpHeight = 1.0f, gravity = 22f },
            };
            yield return new Spec
            {
                name = "Голем",
                color = new Color(0.55f, 0.45f, 0.35f),
                height = 2.5f,
                width = 1.0f,
                profile = new CharacterProfile { displayName = "Голем", walkSpeed = 2f, runSpeed = 4f, jumpHeight = 0.6f, gravity = 28f },
            };
            yield return new Spec
            {
                name = "Прыгун",
                color = new Color(0.95f, 0.55f, 0.15f),
                height = 1.35f,
                width = 0.45f,
                profile = new CharacterProfile { displayName = "Прыгун", walkSpeed = 3.5f, runSpeed = 6.5f, jumpHeight = 2.8f, gravity = 14f },
            };
        }

        public static GameObject Create(Spec spec, Material baseMaterial)
        {
            var root = new GameObject(spec.name);
            float h = spec.height;
            float w = spec.width;

            var controller = root.AddComponent<CharacterController>();
            controller.height = h;
            controller.radius = Mathf.Clamp(w * 0.5f, 0.15f, h * 0.5f);
            controller.center = new Vector3(0f, h * 0.5f + controller.skinWidth, 0f);
            controller.stepOffset = Mathf.Min(0.3f, h * 0.25f);

            var character = root.AddComponent<PlayableCharacter>();
            character.profile = spec.profile.Clone();

            var bodyMaterial = new Material(baseMaterial) { color = spec.color };
            var limbMaterial = new Material(baseMaterial) { color = spec.color * 0.75f };
            var visorMaterial = new Material(baseMaterial) { color = new Color(0.08f, 0.08f, 0.1f) };

            float legLength = h * 0.45f;
            float torsoLength = h * 0.33f;
            float headSize = h * 0.2f;
            float limbThickness = Mathf.Max(0.08f, w * 0.32f);

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            // Legs hang from hip pivots so ProceduralGait can swing them.
            Transform leftLeg = CreateLimb("LeftLeg", body, new Vector3(-w * 0.22f, legLength, 0f), legLength, limbThickness, limbMaterial);
            Transform rightLeg = CreateLimb("RightLeg", body, new Vector3(w * 0.22f, legLength, 0f), legLength, limbThickness, limbMaterial);

            var torso = CreatePart(PrimitiveType.Cube, "Torso", body, bodyMaterial);
            torso.localPosition = new Vector3(0f, legLength + torsoLength * 0.5f, 0f);
            torso.localScale = new Vector3(w, torsoLength, w * 0.6f);

            float shoulderY = legLength + torsoLength * 0.92f;
            float armLength = h * 0.36f;
            Transform leftArm = CreateLimb("LeftArm", body, new Vector3(-(w * 0.5f + limbThickness * 0.6f), shoulderY, 0f), armLength, limbThickness * 0.85f, limbMaterial);
            Transform rightArm = CreateLimb("RightArm", body, new Vector3(w * 0.5f + limbThickness * 0.6f, shoulderY, 0f), armLength, limbThickness * 0.85f, limbMaterial);

            var head = CreatePart(PrimitiveType.Sphere, "Head", body, bodyMaterial);
            head.localPosition = new Vector3(0f, legLength + torsoLength + headSize * 0.5f, 0f);
            head.localScale = Vector3.one * headSize;

            // Visor marks the facing direction.
            var visor = CreatePart(PrimitiveType.Cube, "Visor", head, visorMaterial);
            visor.localPosition = new Vector3(0f, 0.08f, 0.42f);
            visor.localScale = new Vector3(0.7f, 0.22f, 0.25f);

            var gait = root.AddComponent<ProceduralGait>();
            gait.character = character;
            gait.body = body;
            gait.leftArm = leftArm;
            gait.rightArm = rightArm;
            gait.leftLeg = leftLeg;
            gait.rightLeg = rightLeg;
            gait.strideLength = legLength * 1.4f;

            return root;
        }

        static Transform CreateLimb(string name, Transform parent, Vector3 pivot, float length, float thickness, Material material)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.localPosition = pivot;
            var segment = CreatePart(PrimitiveType.Capsule, name + "Mesh", joint, material);
            segment.localPosition = new Vector3(0f, -length * 0.5f, 0f);
            segment.localScale = new Vector3(thickness, length * 0.5f, thickness);
            return joint;
        }

        static Transform CreatePart(PrimitiveType type, string name, Transform parent, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<Renderer>().sharedMaterial = material;
            part.transform.SetParent(parent, false);
            return part.transform;
        }
    }
}
