using UnityEngine;

namespace CharacterPlayground
{
    public static class CharacterMetrics
    {
        /// <summary>World-space bounds of every renderer under root (labels excluded).</summary>
        public static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            bool found = false;
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            foreach (var renderer in renderers)
            {
                if (renderer.GetComponent<TextMesh>() != null) continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return bounds;
        }

        public static float MeasureHeight(GameObject root)
        {
            Bounds bounds = CalculateBounds(root);
            if (bounds.size.y > 0.01f) return bounds.size.y;
            var controller = root.GetComponent<CharacterController>();
            return controller != null ? controller.height : 1.8f;
        }
    }
}
