using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>
    /// A helper bone that sits on a joint and turns by half of the joint's rotation. The skin
    /// around the joint is weighted to it, so a bend is spread over two half steps instead of
    /// one: the surface keeps its volume instead of pinching, as it would with a single blend.
    /// Runs after the scripts that pose the joints.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class HalfJoint : MonoBehaviour
    {
        public Transform joint;

        void LateUpdate()
        {
            if (joint == null) return;
            transform.localRotation = Quaternion.Slerp(Quaternion.identity, joint.localRotation, 0.5f);
        }
    }
}
