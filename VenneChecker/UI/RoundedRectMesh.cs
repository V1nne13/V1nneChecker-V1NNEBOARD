using UnityEngine;

namespace VenneChecker
{
    /// <summary>
    /// Generates a flat rounded-rectangle Mesh at runtime.
    /// The mesh lies in the XY plane, centered at origin, facing -Z (like a Quad).
    /// Use this instead of PrimitiveType.Cube for panels and buttons to get rounded corners.
    /// </summary>
    public static class RoundedRectMesh
    {
        /// <summary>
        /// Creates a flat rounded rectangle mesh.
        /// </summary>
        /// <param name="width">Total width of the rectangle.</param>
        /// <param name="height">Total height of the rectangle.</param>
        /// <param name="radius">Corner radius. Clamped to half the smaller dimension.</param>
        /// <param name="cornerSegments">Number of segments per corner arc (4-16 is typical). More = smoother.</param>
        public static Mesh Create(float width, float height, float radius, int cornerSegments = 8)
        {
            // Clamp radius so it never exceeds half the smaller side
            float maxRadius = Mathf.Min(width, height) * 0.5f;
            radius = Mathf.Clamp(radius, 0f, maxRadius);

            if (cornerSegments < 1) cornerSegments = 1;

            // Total vertices: 1 center + 4 corners * (cornerSegments + 1)
            int vertsPerCorner = cornerSegments + 1;
            int totalVerts = 1 + 4 * vertsPerCorner;
            int totalTris = 4 * cornerSegments + 4 + 4; // corner fans + edge quads + center quads
            // Actually easier to think of it as a triangle fan from center to all perimeter verts.
            int perimeterVerts = 4 * vertsPerCorner;

            Vector3[] vertices = new Vector3[totalVerts];
            Vector2[] uvs = new Vector2[totalVerts];
            int[] triangles = new int[perimeterVerts * 3]; // triangle fan

            // Center vertex
            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            // Inner rect corners (where the arcs are centered)
            // Order: top-right, top-left, bottom-left, bottom-right (CCW from top-right)
            Vector2[] centers = new Vector2[]
            {
                new Vector2( halfW - radius,  halfH - radius), // top-right
                new Vector2(-halfW + radius,  halfH - radius), // top-left
                new Vector2(-halfW + radius, -halfH + radius), // bottom-left
                new Vector2( halfW - radius, -halfH + radius), // bottom-right
            };

            // Start angles for each corner arc (in radians)
            // top-right: 0 to PI/2, top-left: PI/2 to PI, bottom-left: PI to 3PI/2, bottom-right: 3PI/2 to 2PI
            float[] startAngles = new float[]
            {
                0f,
                Mathf.PI * 0.5f,
                Mathf.PI,
                Mathf.PI * 1.5f,
            };

            int vi = 1; // vertex index (0 is center)
            for (int corner = 0; corner < 4; corner++)
            {
                for (int seg = 0; seg <= cornerSegments; seg++)
                {
                    float t = (float)seg / cornerSegments;
                    float angle = startAngles[corner] + t * (Mathf.PI * 0.5f);

                    float x = centers[corner].x + Mathf.Cos(angle) * radius;
                    float y = centers[corner].y + Mathf.Sin(angle) * radius;

                    vertices[vi] = new Vector3(x, y, 0f);
                    uvs[vi] = new Vector2((x + halfW) / width, (y + halfH) / height);
                    vi++;
                }
            }

            // Build triangle fan from center to perimeter
            int ti = 0;
            for (int i = 0; i < perimeterVerts; i++)
            {
                int current = 1 + i;
                int next = 1 + (i + 1) % perimeterVerts;

                // Wind so the face points toward -Z (same as Unity Quad)
                triangles[ti++] = 0;
                triangles[ti++] = next;
                triangles[ti++] = current;
            }

            Mesh mesh = new Mesh();
            mesh.name = "RoundedRect";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;

            // All normals face -Z (toward the viewer, same as a Quad)
            Vector3[] normals = new Vector3[totalVerts];
            for (int i = 0; i < totalVerts; i++)
                normals[i] = -Vector3.forward;
            mesh.normals = normals;

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Creates a double-sided rounded rectangle (visible from both sides).
        /// Useful if the menu can be seen from behind.
        /// </summary>
        public static Mesh CreateDoubleSided(float width, float height, float radius, int cornerSegments = 8)
        {
            Mesh front = Create(width, height, radius, cornerSegments);

            Vector3[] fVerts = front.vertices;
            Vector2[] fUvs = front.uv;
            int[] fTris = front.triangles;

            int fCount = fVerts.Length;
            int fTriCount = fTris.Length;

            Vector3[] vertices = new Vector3[fCount * 2];
            Vector2[] uvs = new Vector2[fCount * 2];
            Vector3[] normals = new Vector3[fCount * 2];
            int[] triangles = new int[fTriCount * 2];

            // Front face
            for (int i = 0; i < fCount; i++)
            {
                vertices[i] = fVerts[i];
                uvs[i] = fUvs[i];
                normals[i] = -Vector3.forward;
            }
            for (int i = 0; i < fTriCount; i++)
                triangles[i] = fTris[i];

            // Back face (same verts, reversed winding, +Z normal)
            for (int i = 0; i < fCount; i++)
            {
                vertices[fCount + i] = fVerts[i];
                uvs[fCount + i] = fUvs[i];
                normals[fCount + i] = Vector3.forward;
            }
            for (int i = 0; i < fTriCount; i += 3)
            {
                triangles[fTriCount + i + 0] = fTris[i + 0] + fCount;
                triangles[fTriCount + i + 1] = fTris[i + 2] + fCount; // swapped
                triangles[fTriCount + i + 2] = fTris[i + 1] + fCount; // swapped
            }

            Mesh mesh = new Mesh();
            mesh.name = "RoundedRectDouble";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Helper: creates a ready-to-use GameObject with a rounded rectangle mesh, MeshRenderer, and material.
        /// Drop-in replacement for CreatePrimitive(Cube) panels.
        /// </summary>
        /// <param name="name">GameObject name.</param>
        /// <param name="parent">Parent transform.</param>
        /// <param name="localPos">Local position.</param>
        /// <param name="width">Panel width in world units.</param>
        /// <param name="height">Panel height in world units.</param>
        /// <param name="radius">Corner radius in world units.</param>
        /// <param name="color">Panel color.</param>
        /// <param name="shader">Shader to use for the material.</param>
        /// <param name="cornerSegments">Smoothness of corners (default 8).</param>
        public static GameObject CreatePanel(string name, Transform parent, Vector3 localPos,
            float width, float height, float radius, Color color, Shader shader, int cornerSegments = 8)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.mesh = CreateDoubleSided(width, height, radius, cornerSegments);

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Material mat = new Material(shader);
            // Set color on all common shader properties
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            // Enable transparency if alpha < 1
            if (color.a < 1f)
            {
                try
                {
                    if (mat.HasProperty("_Surface"))
                        mat.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
                    if (mat.HasProperty("_Blend"))
                        mat.SetFloat("_Blend", 0f);   // 0=Alpha
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.renderQueue = 3000;
                }
                catch { }
            }
            mr.material = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return go;
        }
    }
}
