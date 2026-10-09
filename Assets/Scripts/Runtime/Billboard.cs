using UnityEngine;

namespace CharacterPlayground
{
    /// <summary>Keeps a world-space label facing the main camera.</summary>
    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, Vector3.up);
        }
    }
}
