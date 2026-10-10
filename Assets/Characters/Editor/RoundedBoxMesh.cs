using System.Collections.Generic;
using UnityEngine;

// Box with rounded edges and corners (the soft "Roblox part" look). Each face is its own grid with 0..1 UVs;
// normals come from the rounding so shading is smooth across the bevels.
public static class RoundedBoxMesh
{
    public static Mesh Create(Vector3 size, float radius, int segments = 4)
    {
        Vector3 half = size * 0.5f;
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(half.x, half.y, half.z));
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        // (normal, u axis, v axis)
        Vector3[,] faces =
        {
            { Vector3.right, Vector3.back, Vector3.up },
            { Vector3.left, Vector3.forward, Vector3.up },
            { Vector3.up, Vector3.right, Vector3.back },
            { Vector3.down, Vector3.right, Vector3.forward },
            { Vector3.forward, Vector3.right, Vector3.up },
            { Vector3.back, Vector3.left, Vector3.up },
        };
        for (int f = 0; f < 6; f++)
        {
            Vector3 n = faces[f, 0], a = faces[f, 1], b = faces[f, 2];
            float[] cu = Coords(Vector3.Scale(Abs(a), half).magnitude, radius, segments);
            float[] cv = Coords(Vector3.Scale(Abs(b), half).magnitude, radius, segments);
            float hn = Vector3.Scale(Abs(n), half).magnitude;
            int start = verts.Count;
            for (int j = 0; j < cv.Length; j++)
                for (int i = 0; i < cu.Length; i++)
                {
                    Vector3 p = n * hn + a * cu[i] + b * cv[j];
                    Vector3 inner = new Vector3(
                        Mathf.Clamp(p.x, -half.x + radius, half.x - radius),
                        Mathf.Clamp(p.y, -half.y + radius, half.y - radius),
                        Mathf.Clamp(p.z, -half.z + radius, half.z - radius));
                    Vector3 d = p - inner;
                    Vector3 normal = d.sqrMagnitude > 1e-12f ? d.normalized : n;
                    verts.Add(inner + normal * radius);
                    normals.Add(normal);
                    uvs.Add(new Vector2(Mathf.InverseLerp(cu[0], cu[cu.Length - 1], cu[i]), Mathf.InverseLerp(cv[0], cv[cv.Length - 1], cv[j])));
                }
            // Unity's front faces wind clockwise seen from outside
            bool flip = Vector3.Dot(Vector3.Cross(a, b), n) > 0f;
            for (int j = 0; j < cv.Length - 1; j++)
                for (int i = 0; i < cu.Length - 1; i++)
                {
                    int v00 = start + j * cu.Length + i, v10 = v00 + 1, v01 = v00 + cu.Length, v11 = v01 + 1;
                    if (flip) tris.AddRange(new[] { v00, v11, v01, v00, v10, v11 });
                    else tris.AddRange(new[] { v00, v01, v11, v00, v11, v10 });
                }
        }

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // Grid lines along one axis: `segments` steps through each bevel, one flat span in between
    static float[] Coords(float half, float radius, int segments)
    {
        var c = new float[2 * (segments + 1)];
        for (int i = 0; i <= segments; i++)
        {
            c[i] = -half + radius * i / segments;
            c[segments + 1 + i] = half - radius + radius * i / segments;
        }
        return c;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
