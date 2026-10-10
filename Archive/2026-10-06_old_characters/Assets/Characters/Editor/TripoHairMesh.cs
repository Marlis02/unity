using System.Collections.Generic;
using UnityEngine;

// Hair for the Tripo head (body units, the head as modelled in Hero_Parts.fbx). Hair models are made separately (grey,
// facing +Z, hollow for the head) and fitted onto the head: moved into body units, plus a thin scalp in the hair's colour
// so the gaps between the locks show hair rather than skin. Every Tripo character shares the head, so a fit made once
// works for all of them.
public static class TripoHairMesh
{
    const int Rings = 64, Segments = 144;
    const float MaxPolar = 2.3f;         // radians from the top; the scalp never reaches lower than this
    const float Lip = 0.012f;            // width of the scalp's edge ramp
    const float Sink = -0.006f;          // scalp offset outside the hairline: just under the skin
    const float ScalpThickness = 0.004f;

    // The model's mesh in body units (uniform `scale`, then `offset`) together with the scalp, as one mesh
    public static Mesh Fit(GameObject model, float scale, Vector3 offset, Mesh head)
    {
        var renderer = model.GetComponentInChildren<MeshRenderer>();
        var source = renderer.GetComponent<MeshFilter>().sharedMesh;
        var toBody = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one * scale) * renderer.transform.localToWorldMatrix;
        var vertices = source.vertices;
        var normals = source.normals;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = toBody.MultiplyPoint3x4(vertices[i]);
            normals[i] = toBody.MultiplyVector(normals[i]).normalized;
        }
        var hair = new Mesh { indexFormat = source.indexFormat, vertices = vertices, normals = normals, triangles = source.triangles };
        var scalp = Scalp(head);

        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(new[] { new CombineInstance { mesh = hair }, new CombineInstance { mesh = scalp } }, true, false);
        mesh.RecalculateBounds();
        Object.DestroyImmediate(hair);
        Object.DestroyImmediate(scalp);
        return mesh;
    }

    // A thin cap hugging the skin above the hairline: the forehead in front, above the ears at the sides, the nape at
    // the back. The head is sampled along rays from its centre on a grid around the vertical axis; inside the hairline
    // the cap stands off by a thickness ramped up over a narrow band, outside it sinks under the skin, hiding the grid's
    // ragged edge.
    static Mesh Scalp(Mesh head)
    {
        var center = head.bounds.center;
        var headVertices = head.vertices;
        var headTriangles = head.triangles;

        float Hairline(float azimuth) // azimuth 0 = front, +-pi = back
        {
            float a = Mathf.Abs(azimuth);
            float y = Mathf.Lerp(0.96f, 0.9f, Smooth(0.55f, 1.35f, a));
            return Mathf.Lerp(y, 0.87f, Smooth(1.7f, 2.7f, a));
        }

        var vertices = new List<Vector3>();
        var inside = new List<bool>();
        void Add(float polar, float azimuth)
        {
            var direction = new Vector3(Mathf.Sin(polar) * Mathf.Sin(azimuth), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Cos(azimuth));
            var surface = center + direction * Reach(headVertices, headTriangles, center, direction);
            float depth = (surface.y - Hairline(azimuth)) / Lip;
            float ramp = depth <= 0f ? 0f : Mathf.Sqrt(Mathf.Clamp01(depth));
            vertices.Add(surface + direction * Mathf.Lerp(Sink, ScalpThickness, ramp));
            inside.Add(depth > 0f);
        }

        // One vertex on top, then rings going down; ring vertices wrap round, so the surface is closed and smooth
        Add(0f, 0f);
        for (int r = 1; r <= Rings; r++)
            for (int s = 0; s < Segments; s++)
                Add(MaxPolar * r / Rings, Mathf.PI * 2f * s / Segments - Mathf.PI);

        int Ring(int r, int s) => 1 + (r - 1) * Segments + (s % Segments);
        var triangles = new List<int>();
        for (int s = 0; s < Segments; s++) triangles.AddRange(new[] { 0, Ring(1, s), Ring(1, s + 1) });
        for (int r = 1; r < Rings; r++)
            for (int s = 0; s < Segments; s++)
            {
                int a = Ring(r, s), b = Ring(r, s + 1), c = Ring(r + 1, s), d = Ring(r + 1, s + 1);
                if (!(inside[a] || inside[b] || inside[c] || inside[d])) continue; // bald
                triangles.AddRange(new[] { a, c, d, a, d, b });
            }

        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    // Distance from `origin` to the outermost surface of the mesh along `direction`
    static float Reach(Vector3[] v, int[] t, Vector3 origin, Vector3 direction)
    {
        float far = 0f;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], e1 = v[t[i + 1]] - a, e2 = v[t[i + 2]] - a;
            var p = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) continue;
            var o = origin - a;
            float u = Vector3.Dot(o, p) / det;
            if (u < 0f || u > 1f) continue;
            var q = Vector3.Cross(o, e1);
            float w = Vector3.Dot(direction, q) / det;
            if (w < 0f || u + w > 1f) continue;
            far = Mathf.Max(far, Vector3.Dot(e2, q) / det);
        }
        return far;
    }

    static float Smooth(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));
}
