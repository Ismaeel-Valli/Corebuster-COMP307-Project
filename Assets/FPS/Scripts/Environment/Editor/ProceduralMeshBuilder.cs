using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Metre-scale mesh primitives shared by the first-principles art builders (corebuster_art_001 and
/// corebuster_art_002). A triangle (a, b, c) faces the side that cross(b - a, c - a) points to.
/// Geometry goes to submesh 0 unless <see cref="UseSubmesh"/> selects another material slot.
/// </summary>
sealed class ProceduralMeshBuilder
{
    readonly List<Vector3> m_Vertices = new List<Vector3>();
    readonly List<Vector2> m_Uvs = new List<Vector2>();
    readonly List<List<int>> m_Submeshes = new List<List<int>> { new List<int>() };
    List<int> m_Triangles;

    public ProceduralMeshBuilder()
    {
        m_Triangles = m_Submeshes[0];
    }

    /// <summary>Sends subsequent geometry to material slot <paramref name="index"/>.</summary>
    public void UseSubmesh(int index)
    {
        while (m_Submeshes.Count <= index) m_Submeshes.Add(new List<int>());
        m_Triangles = m_Submeshes[index];
    }

    public void AddCylinder(Vector3 start, Vector3 end, float startRadius, float endRadius,
        int segments, bool caps)
    {
        Vector3 axis = end - start;
        float length = axis.magnitude;
        axis /= Mathf.Max(length, .0001f);
        Basis(axis, out Vector3 right, out Vector3 up);
        int sideStart = m_Vertices.Count;
        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float angle = u * Mathf.PI * 2f;
            Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            AddVertex(start + radial * startRadius, new Vector2(u, 0f));
            AddVertex(end + radial * endRadius, new Vector2(u, length));
        }
        for (int i = 0; i < segments; i++)
        {
            int a = sideStart + i * 2;
            Quad(a, a + 2, a + 3, a + 1);
        }
        if (caps)
        {
            AddDisk(start, -axis, startRadius, segments);
            AddDisk(end, axis, endRadius, segments);
        }
    }

    public void AddElbow(float bendRadius, float tubeRadius, int radialSegments, int bendSegments)
    {
        int start = m_Vertices.Count;
        for (int bend = 0; bend <= bendSegments; bend++)
        {
            float t = bend / (float)bendSegments;
            float angle = t * Mathf.PI * .5f;
            Vector3 center = new Vector3(bendRadius * (1f - Mathf.Cos(angle)), 0f,
                bendRadius * Mathf.Sin(angle));
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, -Mathf.Sin(angle));
            for (int radial = 0; radial <= radialSegments; radial++)
            {
                float u = radial / (float)radialSegments;
                float around = u * Mathf.PI * 2f;
                Vector3 offset = outward * (Mathf.Cos(around) * tubeRadius) +
                                 Vector3.up * (Mathf.Sin(around) * tubeRadius);
                AddVertex(center + offset, new Vector2(u, t * bendRadius * Mathf.PI * .5f));
            }
        }
        int stride = radialSegments + 1;
        for (int bend = 0; bend < bendSegments; bend++)
        {
            for (int radial = 0; radial < radialSegments; radial++)
            {
                int a = start + bend * stride + radial;
                Quad(a, a + stride, a + stride + 1, a + 1);
            }
        }
        AddDisk(Vector3.zero, Vector3.back, tubeRadius, radialSegments);
        AddDisk(new Vector3(bendRadius, 0f, bendRadius), Vector3.right, tubeRadius, radialSegments);
    }

    public void AddTorus(Vector3 center, Vector3 normal, float majorRadius, float minorRadius,
        int majorSegments, int minorSegments)
    {
        normal.Normalize();
        Basis(normal, out Vector3 basisA, out Vector3 basisB);
        int start = m_Vertices.Count;
        for (int major = 0; major <= majorSegments; major++)
        {
            float u = major / (float)majorSegments;
            float majorAngle = u * Mathf.PI * 2f;
            Vector3 radial = basisA * Mathf.Cos(majorAngle) + basisB * Mathf.Sin(majorAngle);
            Vector3 ringCenter = center + radial * majorRadius;
            for (int minor = 0; minor <= minorSegments; minor++)
            {
                float v = minor / (float)minorSegments;
                float minorAngle = v * Mathf.PI * 2f;
                Vector3 offset = radial * (Mathf.Cos(minorAngle) * minorRadius) +
                                 normal * (Mathf.Sin(minorAngle) * minorRadius);
                AddVertex(ringCenter + offset, new Vector2(u, v));
            }
        }
        int stride = minorSegments + 1;
        for (int major = 0; major < majorSegments; major++)
        {
            for (int minor = 0; minor < minorSegments; minor++)
            {
                int a = start + major * stride + minor;
                Quad(a, a + stride, a + stride + 1, a + 1);
            }
        }
    }

    public void AddAnnularCylinderZ(float length, float outerRadius, float innerRadius, int segments)
    {
        int start = m_Vertices.Count;
        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float angle = u * Mathf.PI * 2f;
            Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            AddVertex(radial * outerRadius, new Vector2(u, 0f));
            AddVertex(radial * outerRadius + Vector3.forward * length, new Vector2(u, length));
            AddVertex(radial * innerRadius, new Vector2(u, 0f));
            AddVertex(radial * innerRadius + Vector3.forward * length, new Vector2(u, length));
        }
        for (int i = 0; i < segments; i++)
        {
            int a = start + i * 4;
            int b = a + 4;
            Quad(a, b, b + 1, a + 1);
            Quad(a + 3, b + 3, b + 2, a + 2);
            Quad(a + 2, b + 2, b, a);
            Quad(a + 1, b + 1, b + 3, a + 3);
        }
    }

    public void AddLatheY(Vector2[] profile, int segments, bool caps)
    {
        int start = m_Vertices.Count;
        for (int p = 0; p < profile.Length; p++)
        {
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float angle = u * Mathf.PI * 2f;
                AddVertex(new Vector3(Mathf.Cos(angle) * profile[p].x, profile[p].y,
                    Mathf.Sin(angle) * profile[p].x), new Vector2(u, profile[p].y));
            }
        }
        int stride = segments + 1;
        for (int p = 0; p < profile.Length - 1; p++)
        {
            for (int i = 0; i < segments; i++)
            {
                int a = start + p * stride + i;
                Quad(a, a + 1, a + stride + 1, a + stride);
            }
        }
        if (caps)
        {
            AddDisk(Vector3.up * profile[0].y, Vector3.down, profile[0].x, segments);
            AddDisk(Vector3.up * profile[profile.Length - 1].y, Vector3.up,
                profile[profile.Length - 1].x, segments);
        }
    }

    public void AddBox(Vector3 center, Vector3 size)
    {
        Vector3 half = size * .5f;
        Vector3[] corners =
        {
            center + new Vector3(-half.x, -half.y, -half.z),
            center + new Vector3(half.x, -half.y, -half.z),
            center + new Vector3(half.x, half.y, -half.z),
            center + new Vector3(-half.x, half.y, -half.z),
            center + new Vector3(-half.x, -half.y, half.z),
            center + new Vector3(half.x, -half.y, half.z),
            center + new Vector3(half.x, half.y, half.z),
            center + new Vector3(-half.x, half.y, half.z),
        };
        foreach (int[] face in k_HexahedronFaces)
        {
            int start = m_Vertices.Count;
            AddVertex(corners[face[0]], Vector2.zero);
            AddVertex(corners[face[1]], Vector2.right);
            AddVertex(corners[face[2]], Vector2.one);
            AddVertex(corners[face[3]], Vector2.up);
            Quad(start, start + 1, start + 2, start + 3);
        }
    }

    /// <summary>
    /// Adds a block tapering along Y from <paramref name="bottomSize"/> (X, Z) at
    /// <paramref name="bottomY"/> to <paramref name="topSize"/> at <paramref name="topY"/>, centred on
    /// the local Y axis. UVs are in metres so tiling finishes are not stretched.
    /// </summary>
    public void AddFrustumY(float bottomY, float topY, Vector2 bottomSize, Vector2 topSize)
    {
        Vector2 b = bottomSize * .5f;
        Vector2 t = topSize * .5f;
        AddHexahedron(new[]
        {
            new Vector3(-b.x, bottomY, -b.y), new Vector3(b.x, bottomY, -b.y),
            new Vector3(t.x, topY, -t.y), new Vector3(-t.x, topY, -t.y),
            new Vector3(-b.x, bottomY, b.y), new Vector3(b.x, bottomY, b.y),
            new Vector3(t.x, topY, t.y), new Vector3(-t.x, topY, t.y),
        });
    }

    /// <summary>Axis-aligned box with metre-scale UVs (unlike <see cref="AddBox"/>'s 0-1 faces).</summary>
    public void AddBlock(Vector3 center, Vector3 size)
    {
        AddFrustumY(center.y - size.y * .5f, center.y + size.y * .5f,
            new Vector2(size.x, size.z), new Vector2(size.x, size.z));
        // AddFrustumY centres on the Y axis; shift its 24 new vertices to the requested X/Z.
        for (int i = m_Vertices.Count - 24; i < m_Vertices.Count; i++)
            m_Vertices[i] += new Vector3(center.x, 0f, center.z);
    }

    /// <summary>
    /// Sweeps a closed tube along <paramref name="path"/> with parallel-transported frames, so a
    /// curved hose never twists. V advances in metres along the path.
    /// </summary>
    public void AddTube(IList<Vector3> path, float radius, int segments, bool caps)
    {
        int count = path.Count;
        var tangents = new Vector3[count];
        for (int i = 0; i < count; i++)
            tangents[i] = (path[Mathf.Min(i + 1, count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;

        Basis(tangents[0], out Vector3 normal, out _);
        int start = m_Vertices.Count;
        float travelled = 0f;
        for (int p = 0; p < count; p++)
        {
            if (p > 0)
            {
                normal = Quaternion.FromToRotation(tangents[p - 1], tangents[p]) * normal;
                Vector3.OrthoNormalize(ref tangents[p], ref normal);
                travelled += Vector3.Distance(path[p - 1], path[p]);
            }
            Vector3 binormal = Vector3.Cross(tangents[p], normal).normalized;
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float angle = u * Mathf.PI * 2f;
                AddVertex(path[p] + (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radius,
                    new Vector2(u, travelled));
            }
        }
        int stride = segments + 1;
        for (int p = 0; p < count - 1; p++)
        {
            for (int i = 0; i < segments; i++)
            {
                int a = start + p * stride + i;
                Quad(a, a + 1, a + stride + 1, a + stride);
            }
        }
        if (caps)
        {
            AddDisk(path[0], -tangents[0], radius, segments);
            AddDisk(path[count - 1], tangents[count - 1], radius, segments);
        }
    }

    /// <summary>Samples a cubic Bezier into <paramref name="steps"/> + 1 points for <see cref="AddTube"/>.</summary>
    public static List<Vector3> Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int steps)
    {
        var points = new List<Vector3>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float s = 1f - t;
            points.Add(s * s * s * p0 + 3f * s * s * t * p1 + 3f * s * t * t * p2 + t * t * t * p3);
        }
        return points;
    }

    void AddHexahedron(Vector3[] corners)
    {
        foreach (int[] face in k_HexahedronFaces)
        {
            Vector3 a = corners[face[0]];
            Vector3 b = corners[face[1]];
            Vector3 d = corners[face[3]];
            float width = Vector3.Distance(a, b);
            float height = Vector3.Distance(a, d);
            int start = m_Vertices.Count;
            AddVertex(a, Vector2.zero);
            AddVertex(b, new Vector2(width, 0f));
            AddVertex(corners[face[2]], new Vector2(width, height));
            AddVertex(d, new Vector2(0f, height));
            Quad(start, start + 1, start + 2, start + 3);
        }
    }

    static readonly int[][] k_HexahedronFaces =
    {
        new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 4, 7, 3 },
        new[] { 1, 2, 6, 5 }, new[] { 3, 7, 6, 2 }, new[] { 0, 1, 5, 4 },
    };

    void AddDisk(Vector3 center, Vector3 normal, float radius, int segments)
    {
        Basis(normal.normalized, out Vector3 right, out Vector3 up);
        int centerIndex = m_Vertices.Count;
        AddVertex(center, new Vector2(.5f, .5f));
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            AddVertex(center + radial * radius,
                new Vector2(.5f + Mathf.Cos(angle) * .5f, .5f + Mathf.Sin(angle) * .5f));
        }
        for (int i = 0; i < segments; i++)
            Triangle(centerIndex, centerIndex + i + 1, centerIndex + i + 2);
    }

    public Mesh ToMesh(string name)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(m_Vertices);
        mesh.SetUVs(0, m_Uvs);
        if (m_Submeshes.Count == 1)
        {
            mesh.SetTriangles(m_Submeshes[0], 0);
        }
        else
        {
            mesh.subMeshCount = m_Submeshes.Count;
            for (int i = 0; i < m_Submeshes.Count; i++) mesh.SetTriangles(m_Submeshes[i], i);
        }
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    void AddVertex(Vector3 vertex, Vector2 uv)
    {
        m_Vertices.Add(vertex);
        m_Uvs.Add(uv);
    }

    void Quad(int a, int b, int c, int d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    void Triangle(int a, int b, int c)
    {
        m_Triangles.Add(a);
        m_Triangles.Add(b);
        m_Triangles.Add(c);
    }

    static void Basis(Vector3 axis, out Vector3 right, out Vector3 up)
    {
        Vector3 helper = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > .95f ? Vector3.right : Vector3.up;
        right = Vector3.Cross(helper, axis).normalized;
        up = Vector3.Cross(axis, right).normalized;
    }
}
