using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Metre-scale mesh writer for the tutorial wing's first-principles props (corebuster_tutorial_001). Every vertex
/// carries the normal it should face; each triangle is wound to face its vertices' mean normal, so callers never
/// track winding. Geometry goes through <see cref="Transform"/> (rotation, translation and uniform scale only).
/// </summary>
sealed class TWMesh
{
    readonly List<Vector3> m_V = new List<Vector3>();
    readonly List<Vector3> m_N = new List<Vector3>();
    readonly List<Vector2> m_UV = new List<Vector2>();
    readonly List<List<int>> m_Sub = new List<List<int>> { new List<int>() };
    List<int> m_T;

    public Matrix4x4 Transform = Matrix4x4.identity;

    public TWMesh() => m_T = m_Sub[0];

    /// <summary>Sends further geometry to material slot index.</summary>
    public TWMesh Sub(int index)
    {
        while (m_Sub.Count <= index) m_Sub.Add(new List<int>());
        m_T = m_Sub[index];
        return this;
    }

    public TWMesh At(Vector3 position, Quaternion rotation, float scale = 1f)
    {
        Transform = Matrix4x4.TRS(position, rotation, Vector3.one * scale);
        return this;
    }

    int V(Vector3 p, Vector3 n, Vector2 uv)
    {
        m_V.Add(Transform.MultiplyPoint3x4(p));
        m_N.Add(Transform.MultiplyVector(n).normalized);
        m_UV.Add(uv);
        return m_V.Count - 1;
    }

    void Tri(int a, int b, int c)
    {
        Vector3 facing = m_N[a] + m_N[b] + m_N[c];
        Vector3 cross = Vector3.Cross(m_V[b] - m_V[a], m_V[c] - m_V[a]);
        if (cross.sqrMagnitude < 1e-12f) return;
        m_T.Add(a);
        if (Vector3.Dot(cross, facing) >= 0f)
        {
            m_T.Add(b);
            m_T.Add(c);
        }
        else
        {
            m_T.Add(c);
            m_T.Add(b);
        }
    }

    void Quad(int a, int b, int c, int d)
    {
        Tri(a, b, c);
        Tri(a, c, d);
    }

    /// <summary>A box with hard edges, UVs in metres.</summary>
    public TWMesh Box(Vector3 center, Vector3 size, Quaternion rotation)
    {
        Vector3 h = size * .5f;
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        for (int axis = 0; axis < 3; axis++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 n = axes[axis] * sign;
                Vector3 u = axes[(axis + 1) % 3], v = axes[(axis + 2) % 3];
                float hn = h[axis], hu = h[(axis + 1) % 3], hv = h[(axis + 2) % 3];
                Vector3 c = center + rotation * (n * hn);
                Vector3 ru = rotation * u * hu, rv = rotation * v * hv, rn = rotation * n;
                int a = V(c - ru - rv, rn, new Vector2(0f, 0f));
                int b = V(c + ru - rv, rn, new Vector2(hu * 2f, 0f));
                int cc = V(c + ru + rv, rn, new Vector2(hu * 2f, hv * 2f));
                int d = V(c - ru + rv, rn, new Vector2(0f, hv * 2f));
                Quad(a, b, cc, d);
            }
        }
        return this;
    }

    public TWMesh Box(Vector3 center, Vector3 size) => Box(center, size, Quaternion.identity);

    /// <summary>
    /// Revolves a (radius, height) profile about the local Y axis. Each profile segment is its own band (a crease at
    /// every profile corner). Flat sides give faceted bands, as on the octagonal podiums.
    /// </summary>
    public TWMesh Lathe(IList<Vector2> profile, int segments, Vector3 origin, float startDegrees = 0f, bool flatSides = false)
    {
        float step = 360f / segments;
        for (int i = 0; i < profile.Count - 1; i++)
        {
            Vector2 p0 = profile[i], p1 = profile[i + 1];
            Vector2 tangent = p1 - p0;
            if (tangent.sqrMagnitude < 1e-10f) continue;
            Vector2 n2 = new Vector2(tangent.y, -tangent.x).normalized;
            float v0 = 0f, v1 = tangent.magnitude;
            if (flatSides)
            {
                for (int j = 0; j < segments; j++)
                {
                    float a0 = (startDegrees + j * step) * Mathf.Deg2Rad, a1 = (startDegrees + (j + 1) * step) * Mathf.Deg2Rad;
                    float am = (a0 + a1) * .5f;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    // A flat face: its normal points through the middle of the facet, scaled so the faces stay planar.
                    Vector3 dm = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                    Vector3 n = (dm * n2.x + Vector3.up * n2.y).normalized;
                    float width = Vector3.Distance(d0, d1);
                    int a = V(origin + d0 * p0.x + Vector3.up * p0.y, n, new Vector2(0f, v0));
                    int b = V(origin + d1 * p0.x + Vector3.up * p0.y, n, new Vector2(width * p0.x, v0));
                    int c = V(origin + d1 * p1.x + Vector3.up * p1.y, n, new Vector2(width * p1.x, v1));
                    int d = V(origin + d0 * p1.x + Vector3.up * p1.y, n, new Vector2(0f, v1));
                    Quad(a, b, c, d);
                }
                continue;
            }
            int start = m_V.Count;
            for (int j = 0; j <= segments; j++)
            {
                float angle = (startDegrees + j * step) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 n = (dir * n2.x + Vector3.up * n2.y).normalized;
                float u = j / (float)segments;
                V(origin + dir * p0.x + Vector3.up * p0.y, n, new Vector2(u, v0));
                V(origin + dir * p1.x + Vector3.up * p1.y, n, new Vector2(u, v1));
            }
            for (int j = 0; j < segments; j++)
            {
                int a = start + j * 2;
                Quad(a, a + 2, a + 3, a + 1);
            }
        }
        return this;
    }

    /// <summary>A tube along a path (parallel-transported frames), optionally capped.</summary>
    public TWMesh Tube(IList<Vector3> path, float radius, int sides, bool caps)
    {
        if (path.Count < 2) return this;
        Vector3 tangent = (path[1] - path[0]).normalized;
        Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < .9f ? Vector3.up : Vector3.right).normalized;
        int start = m_V.Count;
        float along = 0f;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 next = i < path.Count - 1 ? (path[i + 1] - path[i]).normalized : tangent;
            Vector3 t = i == 0 ? next : (tangent + next).normalized;
            normal = Vector3.ProjectOnPlane(normal, t).normalized;
            Vector3 binormal = Vector3.Cross(t, normal);
            if (i > 0) along += Vector3.Distance(path[i - 1], path[i]);
            for (int j = 0; j <= sides; j++)
            {
                float angle = j / (float)sides * Mathf.PI * 2f;
                Vector3 radial = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                V(path[i] + radial * radius, radial, new Vector2(j / (float)sides, along));
            }
            tangent = next;
        }
        int stride = sides + 1;
        for (int i = 0; i < path.Count - 1; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = start + i * stride + j;
                Quad(a, a + 1, a + stride + 1, a + stride);
            }
        if (caps)
        {
            Disk(path[0], (path[0] - path[1]).normalized, radius, sides);
            Disk(path[path.Count - 1], (path[path.Count - 1] - path[path.Count - 2]).normalized, radius, sides);
        }
        return this;
    }

    public TWMesh Cylinder(Vector3 a, Vector3 b, float radius, int sides, bool caps = true) =>
        Tube(new[] { a, b }, radius, sides, caps);

    public TWMesh Disk(Vector3 center, Vector3 normal, float radius, int sides)
    {
        Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right).normalized;
        Vector3 w = Vector3.Cross(normal, u);
        int c = V(center, normal, new Vector2(.5f, .5f));
        int first = m_V.Count;
        for (int j = 0; j <= sides; j++)
        {
            float angle = j / (float)sides * Mathf.PI * 2f;
            V(center + (u * Mathf.Cos(angle) + w * Mathf.Sin(angle)) * radius, normal,
                new Vector2(.5f + .5f * Mathf.Cos(angle), .5f + .5f * Mathf.Sin(angle)));
        }
        for (int j = 0; j < sides; j++) Tri(c, first + j, first + j + 1);
        return this;
    }

    /// <summary>
    /// Part of a cylinder along local Z (radius in XY, angles from +X toward +Y), its surface facing outward or
    /// inward, with flat half-disk caps closing it at each end when capped.
    /// </summary>
    public TWMesh ArcZ(float radius, float z0, float z1, float fromDegrees, float toDegrees, int sides, bool inward, bool caps)
    {
        int start = m_V.Count;
        for (int j = 0; j <= sides; j++)
        {
            float angle = Mathf.Lerp(fromDegrees, toDegrees, j / (float)sides) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 n = inward ? -radial : radial;
            V(radial * radius + Vector3.forward * z0, n, new Vector2(j / (float)sides, 0f));
            V(radial * radius + Vector3.forward * z1, n, new Vector2(j / (float)sides, z1 - z0));
        }
        for (int j = 0; j < sides; j++)
        {
            int a = start + j * 2;
            Quad(a, a + 2, a + 3, a + 1);
        }
        if (!caps) return this;
        foreach (float z in new[] { z0, z1 })
        {
            Vector3 n = z == z0 ? Vector3.back : Vector3.forward;
            int c = V(new Vector3(0f, 0f, z), n, new Vector2(.5f, .5f));
            int first = m_V.Count;
            for (int j = 0; j <= sides; j++)
            {
                float angle = Mathf.Lerp(fromDegrees, toDegrees, j / (float)sides) * Mathf.Deg2Rad;
                V(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z), n,
                    new Vector2(.5f + .5f * Mathf.Cos(angle), .5f + .5f * Mathf.Sin(angle)));
            }
            for (int j = 0; j < sides; j++) Tri(c, first + j, first + j + 1);
        }
        return this;
    }

    /// <summary>A single-sided rectangle facing normal, UVs 0..1 (screens and labels).</summary>
    public TWMesh Panel(Vector3 center, Vector3 normal, Vector3 up, float width, float height)
    {
        Vector3 right = Vector3.Cross(up, normal).normalized * (width * .5f);
        Vector3 u = up.normalized * (height * .5f);
        int a = V(center - right - u, normal, new Vector2(0f, 0f));
        int b = V(center + right - u, normal, new Vector2(1f, 0f));
        int c = V(center + right + u, normal, new Vector2(1f, 1f));
        int d = V(center - right + u, normal, new Vector2(0f, 1f));
        Quad(a, b, c, d);
        return this;
    }

    /// <summary>Writes the geometry into a mesh asset at path (created the first time, rewritten in place after).</summary>
    public Mesh Save(string path)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = !mesh;
        if (create) mesh = new Mesh();
        mesh.Clear();
        mesh.name = Path.GetFileNameWithoutExtension(path);
        mesh.indexFormat = m_V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(m_V);
        mesh.SetNormals(m_N);
        mesh.SetUVs(0, m_UV);
        mesh.subMeshCount = m_Sub.Count;
        for (int i = 0; i < m_Sub.Count; i++) mesh.SetTriangles(m_Sub[i], i);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        if (create) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }
}

/// <summary>
/// The tutorial wing's first-principles meshes and textures: the hibernation pod, the gallery podiums, stanchions and
/// velvet ropes, the officer's weapon rack, plaque lecterns, the gallery's light rail, the pod's vitals screen, a
/// hazard stripe and the black keycard (a recolour of the blue floppy disk). Each is generated from code alone.
/// </summary>
static class TutorialWingArt
{
    // --- Pod: its local +Z runs foot to head, -X is the side with the console. The body tilts head-up. ---
    public const float PodTilt = 14f;
    public static readonly Vector3 PodBodyOrigin = new Vector3(0f, .98f, 0f);
    public const float PodRadius = .62f, PodHalfLength = 1.2f;
    // The canopy hinges along the top of its head end.
    public static readonly Vector3 CanopyHinge = new Vector3(0f, PodRadius, PodHalfLength);

    /// <summary>Plinth, cradle, tub, end rings, light strips, bed and console. Slots: shell, trim, glow, padding.</summary>
    public static void PodBase(TWMesh m)
    {
        // Plinth with a glowing skirt.
        m.Sub(1).Box(new Vector3(0f, .09f, 0f), new Vector3(1.7f, .18f, 3.1f));
        m.Sub(2).Box(new Vector3(0f, .2f, 0f), new Vector3(1.6f, .03f, 3f));
        m.Sub(0).Box(new Vector3(0f, .27f, 0f), new Vector3(1.5f, .12f, 2.9f));

        // Cradle and the fins it stands on.
        Quaternion tilt = Quaternion.Euler(-PodTilt, 0f, 0f);
        m.Sub(1).Box(PodBodyOrigin + tilt * new Vector3(0f, -.55f, 0f), new Vector3(.9f, .7f, 2f), tilt);
        for (int side = -1; side <= 1; side += 2)
            m.Sub(0).Box(PodBodyOrigin + tilt * new Vector3(side * .42f, -.62f, -.5f), new Vector3(.1f, .62f, 1.3f), tilt);

        // The body, tilted head-up.
        m.At(PodBodyOrigin, tilt);
        m.Sub(0).ArcZ(PodRadius, -PodHalfLength, PodHalfLength, 180f, 360f, 28, false, true);
        // Interior: a padded bed and pillow inside the tub.
        m.Sub(3).ArcZ(PodRadius - .04f, -PodHalfLength + .02f, PodHalfLength - .02f, 180f, 360f, 28, true, false);
        m.Box(new Vector3(0f, -.32f, 0f), new Vector3(.82f, .12f, 2.2f));
        m.Box(new Vector3(0f, -.22f, .88f), new Vector3(.5f, .1f, .32f));
        // Seam rims and blue strips along both sides.
        for (int side = -1; side <= 1; side += 2)
        {
            m.Sub(1).Box(new Vector3(side * (PodRadius + .005f), .015f, 0f), new Vector3(.07f, .05f, 2.42f));
            m.Sub(2).Box(new Vector3(side * (PodRadius + .045f), -.03f, 0f), new Vector3(.02f, .05f, 2.3f));
            m.Sub(2).Box(new Vector3(side * (PodRadius - .2f), -.46f, 0f), new Vector3(.03f, .04f, 2.2f), Quaternion.Euler(0f, 0f, side * 50f));
        }

        // End rings (lathed about the pod's axis), ribbed, with a glowing band.
        foreach (float sign in new[] { -1f, 1f })
        {
            float z0 = sign > 0f ? PodHalfLength - .02f : -PodHalfLength - .23f;
            m.At(PodBodyOrigin + tilt * new Vector3(0f, 0f, z0), tilt * Quaternion.Euler(90f, 0f, 0f));
            var ring = new[]
            {
                new Vector2(.5f, 0f), new Vector2(.69f, 0f), new Vector2(.72f, .04f), new Vector2(.72f, .21f),
                new Vector2(.69f, .25f), new Vector2(.5f, .25f), new Vector2(.5f, 0f),
            };
            m.Sub(1).Lathe(ring, 32, Vector3.zero);
            m.Sub(2).Lathe(new[] { new Vector2(.722f, .1f), new Vector2(.728f, .1f), new Vector2(.728f, .15f), new Vector2(.722f, .15f) }, 32, Vector3.zero);
            // Closed foot end; the head end stays open under the canopy's hinge.
            if (sign < 0f)
            {
                m.Sub(1).Disk(new Vector3(0f, .02f, 0f), Vector3.down, .5f, 32);
                m.Sub(2).Lathe(new[] { new Vector2(.2f, .015f), new Vector2(.32f, .015f), new Vector2(.32f, .0f), new Vector2(.2f, 0f) }, 32, Vector3.zero);
            }
            for (int rib = 0; rib < 10; rib++)
            {
                float angle = (rib + .5f) / 10f * 360f;
                Quaternion around = Quaternion.Euler(0f, angle, 0f);
                m.Sub(1).Box(around * new Vector3(.735f, .125f, 0f), new Vector3(.04f, .16f, .07f), around);
            }
        }

        // Console on the -X side near the head: post, arm, screen frame and keyboard.
        m.At(Vector3.zero, Quaternion.identity);
        m.Sub(1).Box(new Vector3(-1.02f, .78f, .62f), new Vector3(.12f, 1.2f, .12f));
        m.Sub(0).Box(new Vector3(-1.02f, .2f, .62f), new Vector3(.34f, .06f, .34f));
        Quaternion screenTilt = Quaternion.Euler(0f, 0f, -14f);
        m.Sub(1).Box(new Vector3(-1.06f, 1.62f, .62f), new Vector3(.07f, .56f, .74f), screenTilt);
        m.Sub(1).Box(new Vector3(-1.16f, 1.2f, .62f), new Vector3(.26f, .035f, .56f), Quaternion.Euler(0f, 0f, 16f));
        m.Sub(2).Box(new Vector3(-1.16f, 1.225f, .62f), new Vector3(.16f, .01f, .44f), Quaternion.Euler(0f, 0f, 16f));
        // Status buttons on the plinth's console side.
        m.Sub(2).Box(new Vector3(-.86f, .1f, -.8f), new Vector3(.02f, .06f, .06f));
        m.Sub(2).Box(new Vector3(-.86f, .1f, -.6f), new Vector3(.02f, .06f, .06f));
    }

    /// <summary>The console's screen face (one panel facing -X, tilted with its frame).</summary>
    public static void PodScreen(TWMesh m)
    {
        Quaternion screenTilt = Quaternion.Euler(0f, 0f, -14f);
        m.At(new Vector3(-1.06f, 1.62f, .62f), screenTilt);
        m.Panel(new Vector3(-.037f, 0f, 0f), Vector3.left, Vector3.up, .66f, .48f);
    }

    /// <summary>The glass canopy (upper half-cylinder), relative to its hinge. Slots: glass, trim.</summary>
    public static void PodCanopy(TWMesh m)
    {
        // Hinge space: the canopy's head-end top edge is at the origin.
        Vector3 offset = -CanopyHinge;
        m.At(offset, Quaternion.identity);
        m.Sub(0).ArcZ(PodRadius, -PodHalfLength, PodHalfLength, 0f, 180f, 28, false, true);
        m.Sub(0).ArcZ(PodRadius - .01f, -PodHalfLength, PodHalfLength, 0f, 180f, 28, true, false);
        // Frame: side rails and end hoops.
        for (int side = -1; side <= 1; side += 2)
            m.Sub(1).Box(new Vector3(side * PodRadius, .02f, 0f), new Vector3(.06f, .06f, 2.4f));
        foreach (float z in new[] { -PodHalfLength, PodHalfLength })
        {
            var hoop = new List<Vector3>();
            for (int i = 0; i <= 20; i++)
            {
                float a = i / 20f * Mathf.PI;
                hoop.Add(new Vector3(Mathf.Cos(a) * (PodRadius + .01f), Mathf.Sin(a) * (PodRadius + .01f), z));
            }
            m.Sub(1).Tube(hoop, .03f, 8, true);
        }
        // A spine along the top.
        m.Sub(1).Box(new Vector3(0f, PodRadius + .015f, 0f), new Vector3(.08f, .03f, 2.4f));
    }

    // --- Gallery ---

    public const float PodiumHeight = 1.04f;

    /// <summary>Octagonal podium. Slots: body, top, glow.</summary>
    public static void Podium(TWMesh m)
    {
        const float start = 22.5f;
        m.Sub(0).Lathe(new[]
        {
            new Vector2(0f, 0f), new Vector2(.66f, 0f), new Vector2(.66f, .1f), new Vector2(.6f, .16f),
            new Vector2(.5f, .2f), new Vector2(.5f, .86f), new Vector2(.56f, .92f), new Vector2(.62f, .95f),
            new Vector2(.62f, PodiumHeight),
        }, 8, Vector3.zero, start, true);
        m.Sub(1).Lathe(new[] { new Vector2(.62f, PodiumHeight), new Vector2(.56f, PodiumHeight + .005f), new Vector2(0f, PodiumHeight + .005f) },
            8, Vector3.zero, start, true);
        m.Sub(2).Lathe(new[] { new Vector2(.5f, .6f), new Vector2(.512f, .6f), new Vector2(.512f, .64f), new Vector2(.5f, .64f) },
            8, Vector3.zero, start, true);
        m.Sub(2).Lathe(new[] { new Vector2(.66f, .035f), new Vector2(.668f, .035f), new Vector2(.668f, .06f), new Vector2(.66f, .06f) },
            8, Vector3.zero, start, true);
    }

    public const float StanchionHookHeight = .93f;

    /// <summary>A polished stanchion post. One slot.</summary>
    public static void Stanchion(TWMesh m)
    {
        var profile = new List<Vector2>
        {
            new Vector2(0f, 0f), new Vector2(.17f, 0f), new Vector2(.17f, .02f), new Vector2(.15f, .035f),
            new Vector2(.1f, .055f), new Vector2(.05f, .075f), new Vector2(.028f, .1f), new Vector2(.022f, .13f),
            new Vector2(.022f, .9f), new Vector2(.032f, .905f), new Vector2(.032f, .95f), new Vector2(.022f, .96f),
        };
        // A ball finial.
        for (int i = 0; i <= 8; i++)
        {
            float a = Mathf.Lerp(-60f, 90f, i / 8f) * Mathf.Deg2Rad;
            profile.Add(new Vector2(Mathf.Cos(a) * .045f, 1f + Mathf.Sin(a) * .045f));
        }
        profile[profile.Count - 1] = new Vector2(0f, 1.045f);
        m.Lathe(profile, 20, Vector3.zero);
    }

    /// <summary>A velvet rope hanging from x = 0 to x = span (hook height at both ends), with brass clips. Slots: velvet, brass.</summary>
    public static void Rope(TWMesh m, float span, float sag)
    {
        var path = new List<Vector3>();
        const int steps = 20;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float x = Mathf.Lerp(.07f, span - .07f, t);
            path.Add(new Vector3(x, StanchionHookHeight - sag * (1f - (2f * t - 1f) * (2f * t - 1f)), 0f));
        }
        m.Sub(0).Tube(path, .027f, 10, true);
        m.Sub(1).Cylinder(new Vector3(.03f, StanchionHookHeight, 0f), new Vector3(.085f, StanchionHookHeight - .01f, 0f), .022f, 10);
        m.Sub(1).Cylinder(new Vector3(span - .085f, StanchionHookHeight - .01f, 0f), new Vector3(span - .03f, StanchionHookHeight, 0f), .022f, 10);
    }

    /// <summary>A lectern for an exhibit's plaque; the plate faces +Z, tilted back. Slots: body, plate, glow.</summary>
    public static void Lectern(TWMesh m, out Vector3 plateCenter, out Quaternion plateRotation)
    {
        m.Sub(0).Box(new Vector3(0f, .015f, 0f), new Vector3(.36f, .03f, .3f));
        m.Sub(0).Box(new Vector3(0f, .5f, -.04f), new Vector3(.07f, .96f, .07f));
        plateRotation = Quaternion.Euler(-55f, 0f, 0f);
        plateCenter = new Vector3(0f, 1.02f, 0f);
        m.Sub(0).Box(plateCenter + plateRotation * new Vector3(0f, 0f, -.03f), new Vector3(.68f, .46f, .04f), plateRotation);
        m.Sub(1).Box(plateCenter, new Vector3(.64f, .42f, .02f), plateRotation);
        m.Sub(2).Box(plateCenter + plateRotation * new Vector3(0f, -.215f, .005f), new Vector3(.6f, .012f, .02f), plateRotation);
    }

    /// <summary>The officer's weapon rack: a framed backplate with two cradle hooks, facing +Z. Slots: plate, frame.</summary>
    public static void WeaponRack(TWMesh m)
    {
        m.Sub(1).Box(new Vector3(0f, 0f, .015f), new Vector3(1.42f, .9f, .03f));
        m.Sub(0).Box(new Vector3(0f, 0f, .045f), new Vector3(1.3f, .78f, .04f));
        // Recessed grooves and the cradle hooks.
        for (int i = -2; i <= 2; i++) m.Sub(1).Box(new Vector3(i * .24f, -.25f, .066f), new Vector3(.012f, .2f, .006f));
        for (int side = -1; side <= 1; side += 2)
        {
            m.Sub(1).Box(new Vector3(side * .26f, -.06f, .12f), new Vector3(.05f, .03f, .14f));
            m.Sub(1).Box(new Vector3(side * .26f, -.03f, .18f), new Vector3(.05f, .06f, .025f));
        }
        // Hood over the lamp bar.
        m.Sub(1).Box(new Vector3(0f, .43f, .09f), new Vector3(1.0f, .03f, .14f));
    }

    /// <summary>A ceiling-hung track: a rail along +Z of the given length, hung on rods up to the ceiling.</summary>
    public static void Rail(TWMesh m, float length, float drop)
    {
        m.Box(new Vector3(0f, 0f, length * .5f), new Vector3(.09f, .07f, length));
        for (float z = .6f; z < length; z += 6f) m.Cylinder(new Vector3(0f, .03f, z), new Vector3(0f, drop, z), .015f, 8);
    }

    /// <summary>A spot head (can) aimed along +Z, its yoke at the origin.</summary>
    public static void SpotHead(TWMesh m)
    {
        m.Box(new Vector3(0f, -.04f, 0f), new Vector3(.03f, .08f, .03f));
        m.Cylinder(new Vector3(0f, -.1f, -.08f), new Vector3(0f, -.1f, .1f), .055f, 14);
        m.Cylinder(new Vector3(0f, -.1f, .1f), new Vector3(0f, -.1f, .13f), .06f, 14, false);
    }

    /// <summary>Slant length of <see cref="LightCone"/>'s side, the V extent of its UVs.</summary>
    public static readonly float LightConeSlant = new Vector2(.95f, 1f).magnitude;

    /// <summary>
    /// A showcase spot's visible beam: an open cone from a small lens ring at the origin down to a unit-radius rim at
    /// y = -1 (scaled per spot). V runs from 0 at the lens to <see cref="LightConeSlant"/> at the rim.
    /// </summary>
    public static void LightCone(TWMesh m)
    {
        m.Lathe(new[] { new Vector2(.05f, 0f), new Vector2(1f, -1f) }, 28, Vector3.zero);
    }

    /// <summary>The Disassembler drill's electrodes: two posts with ball terminals, for arcs to jump between.</summary>
    public static void TeslaPosts(TWMesh m, float gap)
    {
        foreach (float x in new[] { -gap * .5f, gap * .5f })
        {
            m.Sub(0).Cylinder(new Vector3(x, 0f, 0f), new Vector3(x, .32f, 0f), .025f, 12);
            m.Sub(0).Lathe(new[] { new Vector2(0f, 0f), new Vector2(.07f, 0f), new Vector2(.07f, .03f), new Vector2(0f, .03f) }, 16, new Vector3(x, 0f, 0f));
            var ball = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                float a = Mathf.Lerp(-90f, 90f, i / 10f) * Mathf.Deg2Rad;
                ball.Add(new Vector2(Mathf.Cos(a) * .055f, .37f + Mathf.Sin(a) * .055f));
            }
            m.Sub(1).Lathe(ball, 16, new Vector3(x, 0f, 0f));
        }
    }

    /// <summary>A dashed guide line on the floor along a polyline, raised a few millimetres.</summary>
    public static void FloorDashes(TWMesh m, IList<Vector3> points, float dash, float gap, float width)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 a = points[i], b = points[i + 1];
            Vector3 dir = (b - a).normalized;
            float length = Vector3.Distance(a, b);
            Quaternion rotation = Quaternion.LookRotation(dir, Vector3.up);
            for (float s = 0f; s + dash <= length; s += dash + gap)
                m.Box(a + dir * (s + dash * .5f) + Vector3.up * .006f, new Vector3(width, .006f, dash), rotation);
        }
    }

    // ------------------------------------------------------------------ textures

    /// <summary>Saves a generated texture as a PNG (only when its pixels change) and imports it.</summary>
    public static Texture2D SavePng(Texture2D texture, string path, bool srgb = true, bool alpha = false)
    {
        byte[] bytes = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        if (!File.Exists(path) || !AreEqual(File.ReadAllBytes(path), bytes))
        {
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer && (importer.sRGBTexture != srgb || importer.alphaIsTransparency != alpha))
        {
            importer.sRGBTexture = srgb;
            importer.alphaIsTransparency = alpha;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static bool AreEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>The pod console's vitals readout: a grid, a body scan, a heartbeat trace, a reticle and level bars.</summary>
    public static Texture2D PodScreenTexture()
    {
        const int w = 512, h = 384;
        var px = new Color32[w * h];
        var bg = new Color32(4, 14, 30, 255);
        var grid = new Color32(14, 46, 80, 255);
        var cyan = new Color32(60, 200, 255, 255);
        var orange = new Color32(255, 150, 30, 255);
        var green = new Color32(70, 255, 120, 255);
        for (int i = 0; i < px.Length; i++) px[i] = bg;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x % 32 == 0 || y % 32 == 0) px[y * w + x] = grid;

        void Dot(int x, int y, Color32 c, int r = 1)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && yy >= 0 && xx < w && yy < h && dx * dx + dy * dy <= r * r) px[yy * w + xx] = c;
                }
        }
        void Line(Vector2 a, Vector2 b, Color32 c, int r = 1)
        {
            int n = Mathf.CeilToInt(Vector2.Distance(a, b));
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, n == 0 ? 0f : i / (float)n);
                Dot(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), c, r);
            }
        }
        void Ellipse(Vector2 c, Vector2 radius, Color32 col)
        {
            for (int i = 0; i < 64; i++)
            {
                float a0 = i / 64f * Mathf.PI * 2f, a1 = (i + 1) / 64f * Mathf.PI * 2f;
                Line(c + new Vector2(Mathf.Cos(a0) * radius.x, Mathf.Sin(a0) * radius.y),
                    c + new Vector2(Mathf.Cos(a1) * radius.x, Mathf.Sin(a1) * radius.y), col);
            }
        }
        void Rect(int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c;
        }

        // Body scan (left): head, torso, arms and legs as outlines.
        Ellipse(new Vector2(110, 318), new Vector2(18, 22), cyan);
        Ellipse(new Vector2(110, 235), new Vector2(38, 60), cyan);
        Line(new Vector2(74, 270), new Vector2(48, 170), cyan, 2);
        Line(new Vector2(146, 270), new Vector2(172, 170), cyan, 2);
        Line(new Vector2(96, 178), new Vector2(86, 40), cyan, 2);
        Line(new Vector2(124, 178), new Vector2(134, 40), cyan, 2);
        Ellipse(new Vector2(110, 238), new Vector2(10, 10), orange);

        // Reticle (upper right).
        Ellipse(new Vector2(392, 290), new Vector2(54, 54), orange);
        Ellipse(new Vector2(392, 290), new Vector2(30, 30), orange);
        Line(new Vector2(392, 226), new Vector2(392, 354), orange);
        Line(new Vector2(328, 290), new Vector2(456, 290), orange);

        // Heartbeat trace (lower right).
        var trace = new List<Vector2>();
        for (int x = 230; x < 500; x += 3)
        {
            float t = (x - 230) / 90f % 1f;
            float y = 120f + (t > .4f && t < .46f ? 50f * Mathf.Sin((t - .4f) / .06f * Mathf.PI) : 0f)
                           - (t > .46f && t < .5f ? 22f * Mathf.Sin((t - .46f) / .04f * Mathf.PI) : 0f)
                           + (t > .62f && t < .76f ? 10f * Mathf.Sin((t - .62f) / .14f * Mathf.PI) : 0f);
            trace.Add(new Vector2(x, y));
        }
        for (int i = 0; i < trace.Count - 1; i++) Line(trace[i], trace[i + 1], green);

        // Level bars (low vitals in orange).
        int[] levels = { 22, 64, 41, 88 };
        for (int i = 0; i < levels.Length; i++)
        {
            int y0 = 30 + i * 14;
            Rect(230, y0, 230 + 260, y0 + 8, grid);
            Rect(230, y0, 230 + levels[i] * 260 / 100, y0 + 8, levels[i] < 50 ? orange : cyan);
        }
        // Frame.
        Rect(0, 0, w, 4, cyan);
        Rect(0, h - 4, w, h, cyan);
        Rect(0, 0, 4, h, cyan);
        Rect(w - 4, 0, w, h, cyan);

        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        texture.SetPixels32(px);
        texture.Apply();
        return texture;
    }

    /// <summary>A beam's falloff along V: white at the lens (V = 0) fading to black, for an additive cone.</summary>
    public static Texture2D BeamFadeTexture()
    {
        const int w = 4, h = 128;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float t = y / (h - 1f);
            // A soft start just off the lens, then a long tail.
            float k = Mathf.SmoothStep(0f, 1f, t / .06f) * Mathf.Pow(1f - t, 1.1f);
            byte b = (byte)Mathf.RoundToInt(k * 255f);
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32(b, b, b, 255);
        }
        texture.SetPixels32(px);
        texture.Apply();
        return texture;
    }

    /// <summary>Diagonal yellow-and-black hazard stripes, tiling along U.</summary>
    public static Texture2D HazardTexture()
    {
        const int w = 256, h = 64;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        var yellow = new Color32(232, 176, 24, 255);
        var black = new Color32(22, 22, 24, 255);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Worn: a little seeded speckle on the paint.
                int hash = (x * 73856093) ^ (y * 19349663);
                bool stripe = ((x + y) / 32) % 2 == 0;
                Color32 c = stripe ? yellow : black;
                if ((hash & 63) == 0) c = Color32.Lerp(c, new Color32(90, 90, 90, 255), .5f);
                px[y * w + x] = c;
            }
        texture.SetPixels32(px);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// The black keycard: the blue floppy's texture (or icon) recoloured to charcoal plastic. Saturated blue/teal
    /// plastic turns dark grey with a faint cool sheen; the grey shutter, white hub and alpha are kept.
    /// </summary>
    public static Texture2D BlackFloppy(string sourcePath)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        source.LoadImage(File.ReadAllBytes(sourcePath));
        var px = source.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            Color.RGBToHSV(c, out float hue, out float saturation, out float value);
            bool plastic = saturation > .2f && hue > .4f && hue < .75f;
            if (!plastic) continue;
            float grey = value * .26f + .03f;
            var dark = new Color(grey * .95f, grey, grey * 1.08f, c.a);
            px[i] = Color.Lerp(c, dark, Mathf.Clamp01((saturation - .2f) / .2f));
        }
        var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        result.SetPixels32(px);
        result.Apply();
        Object.DestroyImmediate(source);
        return result;
    }
}
