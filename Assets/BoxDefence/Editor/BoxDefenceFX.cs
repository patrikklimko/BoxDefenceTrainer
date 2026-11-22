using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoxDefence.EditorTools
{
    /// <summary>
    /// Fight-night extras for the scene builder: a crowd made of combined, animatable meshes,
    /// light beams and a pool of light on the canvas, and particle bursts for impacts.
    /// </summary>
    public static class BoxDefenceFX
    {
        const string Dir = "Assets/BoxDefence/Materials";

        // ------------------------------------------------------------ crowd

        class Group { public List<Vector3> v = new(); public List<Vector3> n = new(); public List<Color> c = new(); public List<int> t = new(); public Vector3 sum; public int fans; }
        static readonly Dictionary<string, Group> groups = new();
        static Mesh capsule, sphere;

        public static void BeginCrowd()
        {
            groups.Clear();
            capsule = BuiltIn(PrimitiveType.Capsule);
            sphere = BuiltIn(PrimitiveType.Sphere);
        }

        static Mesh BuiltIn(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return mesh;
        }

        /// <summary>Adds one fan (body capsule + head sphere) to the block of seats called key.</summary>
        public static void AddFan(string key, Vector3 bodyPos, Quaternion bodyRot, Vector3 bodyScale, Vector3 headPos, float headSize, Color shirt, Color skin)
        {
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = new Group();
            Append(g, capsule, Matrix4x4.TRS(bodyPos, bodyRot, bodyScale), shirt);
            Append(g, sphere, Matrix4x4.TRS(headPos, Quaternion.identity, Vector3.one * headSize), skin);
            g.sum += bodyPos; g.fans++;
        }

        static void Append(Group g, Mesh m, Matrix4x4 mat, Color col)
        {
            int start = g.v.Count;
            var nm = mat.inverse.transpose;
            foreach (var p in m.vertices) g.v.Add(mat.MultiplyPoint3x4(p));
            foreach (var n in m.normals) g.n.Add(nm.MultiplyVector(n).normalized);
            for (int i = 0; i < m.vertexCount; i++) g.c.Add(col);
            foreach (var i in m.triangles) g.t.Add(start + i);
        }

        /// <summary>Turns the collected fans into one mesh per block, under an animated parent.</summary>
        public static CrowdAnimator EndCrowd(string name)
        {
            var root = new GameObject(name);
            var anim = root.AddComponent<CrowdAnimator>();
            var mat = VertexColorLit("CrowdVertexColour");
            var list = new List<Transform>();
            foreach (var kv in groups)
            {
                var g = kv.Value;
                Vector3 pivot = g.sum / Mathf.Max(1, g.fans);
                var go = new GameObject("Fans " + kv.Key);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = pivot;
                var verts = new Vector3[g.v.Count];
                for (int i = 0; i < verts.Length; i++) verts[i] = g.v[i] - pivot;
                var mesh = new Mesh { name = "Fans " + kv.Key, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetNormals(g.n);
                mesh.SetColors(g.c);
                mesh.SetTriangles(g.t, 0);
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                list.Add(go.transform);
            }
            anim.groups = list.ToArray();
            groups.Clear();
            return anim;
        }

        // ------------------------------------------------------------ lights

        /// <summary>Soft beams from the lighting rig onto the ring, and a pool of light on the canvas.</summary>
        public static void FightNightLights(Transform parent, Vector3 ringCentre, float rigY)
        {
            var beamMat = Additive("LightBeam", new Color(1f, 0.93f, 0.78f, 1f));
            var root = new GameObject("Light Beams").transform;
            root.SetParent(parent, false);
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) })
            {
                Vector3 top = ringCentre + new Vector3(c.x * 2.3f, rigY - 0.1f, c.y * 2.3f);
                Vector3 aim = ringCentre + new Vector3(c.x * 0.6f, 0f, c.y * 0.6f);
                var go = new GameObject("Beam");
                go.transform.SetParent(root, false);
                go.transform.position = top;
                go.transform.rotation = Quaternion.LookRotation(aim - top);
                float len = (aim - top).magnitude;
                go.AddComponent<MeshFilter>().sharedMesh = Cone(0.18f, 1.0f, len, 0.13f, 2.4f / len);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = beamMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            var pool = new GameObject("Light Pool");
            pool.transform.SetParent(root, false);
            pool.transform.position = ringCentre + Vector3.up * 0.006f;
            pool.AddComponent<MeshFilter>().sharedMesh = Disc(2.6f, 0.22f);
            var pr = pool.AddComponent<MeshRenderer>();
            pr.sharedMaterial = beamMat;
            pr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A cone along +Z. Alpha is alphaTop at the narrow end and fades to 0 by fadeAt (0..1 of the length).</summary>
        static Mesh Cone(float r0, float r1, float len, float alphaTop, float fadeAt)
        {
            const int seg = 24, rings = 8;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float k = i / (float)rings;
                float r = Mathf.Lerp(r0, r1, k);
                float a = alphaTop * Mathf.Clamp01(1f - k / Mathf.Max(0.05f, 1f - fadeAt));
                for (int j = 0; j < seg; j++)
                {
                    float ang = j * Mathf.PI * 2f / seg;
                    v.Add(new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, k * len));
                    c.Add(new Color(1f, 1f, 1f, a));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a = i * seg + j, b = i * seg + (j + 1) % seg, d = a + seg, e = b + seg;
                    t.AddRange(new[] { a, d, b, b, d, e });
                }
            var m = new Mesh { name = "Beam" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh Disc(float radius, float alphaCentre)
        {
            const int seg = 40;
            var v = new List<Vector3> { Vector3.zero }; var c = new List<Color> { new Color(1f, 1f, 1f, alphaCentre) }; var t = new List<int>();
            for (int j = 0; j < seg; j++)
            {
                float ang = j * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(ang) * radius * 0.55f, 0f, Mathf.Sin(ang) * radius * 0.55f)); c.Add(new Color(1f, 1f, 1f, alphaCentre * 0.6f));
            }
            for (int j = 0; j < seg; j++)
            {
                float ang = j * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius)); c.Add(new Color(1f, 1f, 1f, 0f));
            }
            for (int j = 0; j < seg; j++)
            {
                int a = 1 + j, b = 1 + (j + 1) % seg, d = 1 + seg + j, e = 1 + seg + (j + 1) % seg;
                t.AddRange(new[] { 0, b, a, a, b, e, a, e, d });
            }
            var m = new Mesh { name = "Light Pool" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        public static void Glow(Material m, float amount)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", Color.white * amount);
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------ impact particles

        public static ParticleSystem Sparks(string name)
        {
            var ps = Burst(name, Additive("Spark", new Color(1f, 0.95f, 0.8f, 1f), Dot()),
                life: new Vector2(0.12f, 0.28f), speed: new Vector2(1.6f, 3.8f), size: new Vector2(0.008f, 0.02f),
                colA: new Color(1f, 1f, 0.9f, 1f), colB: new Color(1f, 0.75f, 0.35f, 1f), gravity: 0.6f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.035f;
            r.lengthScale = 1f;
            return ps;
        }

        public static ParticleSystem Sweat(string name)
        {
            return Burst(name, AlphaBlended("Sweat", Dot()),
                life: new Vector2(0.35f, 0.7f), speed: new Vector2(0.8f, 2.4f), size: new Vector2(0.006f, 0.016f),
                colA: new Color(0.85f, 0.93f, 1f, 0.85f), colB: new Color(1f, 1f, 1f, 0.6f), gravity: 1.3f);
        }

        static ParticleSystem Burst(string name, Material mat, Vector2 life, Vector2 speed, Vector2 size, Color colA, Color colB, float gravity)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(colA, colB);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.03f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // ------------------------------------------------------------ materials

        static Material Load(string name, string shader)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader) ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        /// <summary>Lit, opaque, colour comes from the mesh's vertex colours (one material for the whole crowd).</summary>
        static Material VertexColorLit(string name)
        {
            var m = Load(name, "Universal Render Pipeline/Particles/Simple Lit");
            m.SetFloat("_Surface", 0f);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Cull", 2f);
            m.renderQueue = (int)RenderQueue.Geometry;
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Additive(string name, Color color, Texture2D tex = null)
        {
            var m = Load(name, "Universal Render Pipeline/Particles/Unlit");
            Transparent(m, additive: true);
            m.SetColor("_BaseColor", color);
            if (tex) m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material AlphaBlended(string name, Texture2D tex)
        {
            var m = Load(name, "Universal Render Pipeline/Particles/Unlit");
            Transparent(m, additive: false);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Transparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>A soft round dot, so particles aren't squares.</summary>
        static Texture2D Dot()
        {
            string path = Dir + "/SoftDot.png";
            if (!File.Exists(path))
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
