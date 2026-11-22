using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoxDefence.EditorTools
{
    /// <summary>
    /// Optional real models. If Models/Boxer/Boxer.fbx (Mixamo, with skin, fight idle) exists,
    /// it becomes the opponent and your own body. If a glove model (.fbx/.obj) is in
    /// Models/Glove, it replaces the primitive gloves. Missing files: the primitives stay.
    /// The primitive head and gloves are kept, invisible, as the hit volumes.
    /// </summary>
    public static class BoxDefenceModels
    {
        const string BoxerPath = "Assets/BoxDefence/Models/Boxer/Boxer.fbx";
        const string GloveDir = "Assets/BoxDefence/Models/Glove";
        const string ControllerPath = "Assets/BoxDefence/Models/Boxer/BoxerIdle.controller";

        // tuned after the first look in the headset
        public static Vector3 GloveEuler = Vector3.zero;
        const float GloveLength = 0.28f;      // his gloves: fingertips to cuff of a 12-16 oz glove
        const float MyGloveLength = 0.24f;    // yours, a bit smaller so they do not fill your view

        public static bool HasBoxer => File.Exists(BoxerPath);
        public static bool HasGlove => FindGlove() != null;

        static string FindGlove()
        {
            if (!Directory.Exists(GloveDir)) return null;
            return Directory.GetFiles(GloveDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) || f.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Replace('\\', '/'))
                .FirstOrDefault();
        }

        // ------------------------------------------------------------ import

        static RuntimeAnimatorController PrepareBoxer()
        {
            var imp = AssetImporter.GetAtPath(BoxerPath) as ModelImporter;
            if (!imp) return null;
            bool dirty = false;
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                dirty = true;
            }
            if (imp.importCameras || imp.importLights) { imp.importCameras = false; imp.importLights = false; dirty = true; }
            if (dirty) imp.SaveAndReimport();

            var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            bool needLoop = clips.Any(c => !c.loopTime || !c.lockRootRotation || !c.lockRootHeightY || !c.lockRootPositionXZ);
            if (needLoop && clips.Length > 0)
            {
                foreach (var c in clips)
                {
                    c.loopTime = true; c.loopPose = true;
                    c.lockRootRotation = true; c.keepOriginalOrientation = true;
                    c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                    c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                }
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
            }

            FixBoxerMaterials();

            var clip = AssetDatabase.LoadAllAssetsAtPath(BoxerPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var layers = ctrl.layers;
            layers[0].iKPass = true;
            ctrl.layers = layers;
            if (clip)
            {
                var st = ctrl.layers[0].stateMachine.AddState("Guard");
                st.motion = clip;
                ctrl.layers[0].stateMachine.defaultState = st;
            }
            else Debug.LogWarning("[BoxDefence] Boxer.fbx has no animation clip; he will stand in T-pose with IK arms. Download it from Mixamo 'With Skin' with the fight idle selected.");
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        static void FixBoxerMaterials()
        {
            const string dir = "Assets/BoxDefence/Models/Boxer/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/BoxDefence/Models/Boxer", "Materials");
            bool extracted = false;
            foreach (var mat in AssetDatabase.LoadAllAssetsAtPath(BoxerPath).OfType<Material>().ToArray())
            {
                string to = dir + "/" + mat.name.Replace(':', '_') + ".mat";
                if (File.Exists(to)) continue;
                string err = AssetDatabase.ExtractAsset(mat, to);
                if (string.IsNullOrEmpty(err)) extracted = true;
                else Debug.LogWarning("[BoxDefence] " + err);
            }
            if (extracted) { AssetDatabase.WriteImportSettingsIfDirty(BoxerPath); AssetDatabase.ImportAsset(BoxerPath, ImportAssetOptions.ForceUpdate); }

            var tex = BoxerTextures();

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!mat || !mat.shader || !mat.shader.name.StartsWith("Universal Render Pipeline")) continue;
                bool hair = mat.name.ToLower().Contains("hair");
                // transparent bodies sort badly in VR: opaque everywhere, hair as a cut-out
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", 1f); mat.SetFloat("_DstBlend", 0f);
                mat.SetFloat("_SrcBlendAlpha", 1f); mat.SetFloat("_DstBlendAlpha", 0f);
                mat.SetFloat("_ZWrite", 1f);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.DisableKeyword("_ALPHAMODULATE_ON");
                mat.SetOverrideTag("RenderType", hair ? "TransparentCutout" : "Opaque");
                mat.SetFloat("_AlphaClip", hair ? 1f : 0f);
                mat.SetFloat("_Cutoff", 0.4f);
                if (hair) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = hair ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
                mat.SetFloat("_Cull", hair ? 0f : 2f);
                mat.SetFloat("_Metallic", 0f);
                if (mat.GetFloat("_Smoothness") > 0.35f) mat.SetFloat("_Smoothness", 0.3f);
                var c = mat.GetColor("_BaseColor"); c.a = 1f; mat.SetColor("_BaseColor", c);

                // Mixamo's textures are embedded in the FBX: hook them up by name
                string key = MaterialKey(mat.name);
                bool lashes = mat.name.ToLower().Contains("eyelash");
                if (key != null)
                {
                    Texture2D baseMap = null, normalMap = null;
                    if (hair) tex.TryGetValue("Hair_DiffuseAlpha", out baseMap);
                    else if (lashes) tex.TryGetValue("Lashes_DiffuseAlpha", out baseMap);
                    if (!baseMap) tex.TryGetValue(key + "_Diffuse", out baseMap);
                    tex.TryGetValue(key + "_Normal", out normalMap);
                    if (baseMap) { mat.SetTexture("_BaseMap", baseMap); mat.SetTexture("_MainTex", baseMap); mat.SetColor("_BaseColor", Color.white); }
                    if (normalMap && !lashes) { mat.SetTexture("_BumpMap", normalMap); mat.EnableKeyword("_NORMALMAP"); }
                }
                if (lashes)
                {
                    mat.SetFloat("_AlphaClip", 1f); mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = (int)RenderQueue.AlphaTest;
                    mat.SetFloat("_Cull", 0f);
                }
                mat.SetFloat("_Smoothness", hair ? 0.2f : 0.25f);
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
        }

        static void EditorScale(HumanoidPuppet p, float headBoneHeight)
        {
            var anim = p.GetComponent<Animator>();
            var head = anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            if (!head) return;
            float h = (head.position.y - p.transform.position.y) / Mathf.Max(1e-4f, p.transform.lossyScale.y);
            if (h > 0.3f) p.transform.localScale = Vector3.one * Mathf.Clamp(headBoneHeight / h, 0.1f, 3f);
            Debug.Log($"[BoxDefence] {p.name}: model head bone at {h:F2} m, preview scale {p.transform.localScale.x:F2}");
        }

        static string MaterialKey(string matName)
        {
            string n = matName.ToLower();
            if (n.Contains("hair")) return "Hair";
            if (n.Contains("bottom")) return "Bottom";
            if (n.Contains("shoe")) return "Shoes";
            if (n.Contains("top")) return "Top";
            if (n.Contains("body") || n.Contains("eyelash")) return "Body";
            return null;
        }

        /// <summary>Pulls the embedded textures out of Boxer.fbx and makes cut-out maps for hair and lashes.</summary>
        static System.Collections.Generic.Dictionary<string, Texture2D> BoxerTextures()
        {
            const string dir = "Assets/BoxDefence/Models/Boxer/Textures";
            if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*.png").Length == 0)
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
                var imp = (ModelImporter)AssetImporter.GetAtPath(BoxerPath);
                if (!imp.ExtractTextures(dir)) Debug.LogWarning("[BoxDefence] Could not extract textures from Boxer.fbx");
                AssetDatabase.Refresh();
            }

            Combine(dir, "Remy_Hair_Diffuse", "Remy_Hair_Opacity", "Remy_Hair_DiffuseAlpha");
            Combine(dir, "Remy_Body_Diffuse", "Remy_Body_Opacity", "Remy_Lashes_DiffuseAlpha");

            var map = new System.Collections.Generic.Dictionary<string, Texture2D>();
            foreach (var f in Directory.GetFiles(dir, "*.png"))
            {
                string path = f.Replace('\\', '/');
                string name = Path.GetFileNameWithoutExtension(path);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti)
                {
                    bool normal = name.EndsWith("_Normal");
                    var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    if (ti.textureType != type || ti.maxTextureSize != 1024 || ti.alphaIsTransparency != name.EndsWith("DiffuseAlpha"))
                    {
                        ti.textureType = type;
                        ti.maxTextureSize = 1024;
                        ti.alphaIsTransparency = name.EndsWith("DiffuseAlpha");
                        ti.SaveAndReimport();
                    }
                }
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (t) map[name.StartsWith("Remy_") ? name.Substring(5) : name] = t;
            }
            return map;
        }

        static void Combine(string dir, string rgb, string alpha, string outName)
        {
            string outPath = dir + "/" + outName + ".png";
            string a = dir + "/" + rgb + ".png", b = dir + "/" + alpha + ".png";
            if (File.Exists(outPath) || !File.Exists(a) || !File.Exists(b)) return;
            var ta = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var tb = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ta.LoadImage(File.ReadAllBytes(a));
            tb.LoadImage(File.ReadAllBytes(b));
            int w = Mathf.Min(ta.width, 2048), h = Mathf.Min(ta.height, 2048);
            var o = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    Color c = ta.GetPixelBilinear(u, v);
                    c.a = tb.GetPixelBilinear(u, v).grayscale;
                    px[y * w + x] = c;
                }
            o.SetPixels(px);
            File.WriteAllBytes(outPath, o.EncodeToPNG());
            Object.DestroyImmediate(ta); Object.DestroyImmediate(tb); Object.DestroyImmediate(o);
            AssetDatabase.ImportAsset(outPath);
        }

        static HumanoidPuppet Spawn(string name, RuntimeAnimatorController ctrl)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxerPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            var anim = go.GetComponent<Animator>();
            if (!anim) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.updateWhenOffscreen = true;            // bounds follow IK punches
                r.quality = SkinQuality.Bone2;           // cheaper on Quest 2
            }
            return go.AddComponent<HumanoidPuppet>();
        }

        // ------------------------------------------------------------ gloves

        /// <summary>URP Lit material from BoxingGlove_*.png (white leather, tinted per glove at runtime).</summary>
        static Material GloveMaterial()
        {
            string albedo = GloveDir + "/BoxingGlove_Albedo.png";
            if (!File.Exists(albedo)) return null;
            string normal = GloveDir + "/BoxingGlove_Normal.png";
            string metal = GloveDir + "/BoxingGlove_Metallic.png";
            SetTex(albedo, TextureImporterType.Default, true);
            SetTex(normal, TextureImporterType.NormalMap, false);
            SetTex(metal, TextureImporterType.Default, false);

            string matPath = GloveDir + "/GloveMat.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            mat.SetColor("_BaseColor", Color.white);
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
            if (n) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); }
            var m = AssetDatabase.LoadAssetAtPath<Texture2D>(metal);
            if (m) { mat.SetTexture("_MetallicGlossMap", m); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); }
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.85f);
            mat.SetFloat("_Cull", 0f);   // the model is double sided
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void SetTex(string path, TextureImporterType type, bool srgb)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return;
            if (ti.textureType == type && ti.sRGBTexture == srgb && ti.maxTextureSize == 1024) return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
        }

        static GameObject MakeGlove(string path, Transform parent, Vector3 localPos, Quaternion localRot, bool mirror, string name, float length = GloveLength)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) return null;
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            holder.localPosition = localPos;
            holder.localRotation = localRot;

            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(g, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var t = g.transform;
            t.SetParent(holder, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            // turn the model so its longest side (cuff to fingertips) points along the punch
            var b = LocalBounds(g, holder);
            Vector3 sz = b.size;
            Quaternion along = sz.x >= sz.y && sz.x >= sz.z ? Quaternion.Euler(0f, 90f, 0f)
                             : sz.y >= sz.z ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            t.localRotation = Quaternion.Euler(GloveEuler) * along;

            // longest side = glove length; centre it on the holder
            b = LocalBounds(g, holder);
            float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float s = longest > 1e-5f ? length / longest : 1f;
            t.localScale = new Vector3(mirror ? -s : s, s, s);
            b = LocalBounds(g, holder);
            t.localPosition -= b.center;

            var gm = GloveMaterial();
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (gm) r.sharedMaterials = Enumerable.Repeat(gm, r.sharedMaterials.Length).ToArray();
            }
            return g;
        }

        static Bounds LocalBounds(GameObject g, Transform space)
        {
            var rs = g.GetComponentsInChildren<MeshFilter>(true);
            bool any = false; var bb = new Bounds();
            foreach (var mf in rs)
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { bb = new Bounds(p, Vector3.zero); any = true; } else bb.Encapsulate(p);
                }
            }
            foreach (var sm in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!sm.sharedMesh) continue;
                var mb = sm.sharedMesh.bounds;
                var p0 = space.InverseTransformPoint(sm.transform.TransformPoint(mb.min));
                var p1 = space.InverseTransformPoint(sm.transform.TransformPoint(mb.max));
                if (!any) { bb = new Bounds(p0, Vector3.zero); any = true; } else bb.Encapsulate(p0);
                bb.Encapsulate(p1);
            }
            return bb;
        }

        static void HideRenderers(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        // ------------------------------------------------------------ apply

        /// <summary>Call right after the primitive opponent is built, before its text is added.</summary>
        public static void ApplyOpponent(Opponent opp)
        {
            string glove = FindGlove();
            if (glove != null)
            {
                // his gloves: the sphere stays as the hit volume, the model is drawn
                var l = MakeGlove(glove, opp.gloveL.parent, Vector3.zero, Quaternion.identity, true, "Glove Model L");
                var r = MakeGlove(glove, opp.gloveR.parent, Vector3.zero, Quaternion.identity, false, "Glove Model R");
                if (l && r)
                {
                    // follow the sphere's position and rotation without inheriting its squashed scale
                    l.transform.parent.gameObject.AddComponent<FollowTransform>().target = opp.gloveL;
                    r.transform.parent.gameObject.AddComponent<FollowTransform>().target = opp.gloveR;
                    opp.gloveLRenderers = l.GetComponentsInChildren<Renderer>(true);
                    opp.gloveRRenderers = r.GetComponentsInChildren<Renderer>(true);
                    opp.gloveLRenderer.enabled = false;
                    opp.gloveRRenderer.enabled = false;
                }
            }

            if (!HasBoxer) return;
            var ctrl = PrepareBoxer();
            var puppet = Spawn("Boxer Model", ctrl);
            var t = opp.transform;

            // hide the primitive body, keep head and gloves as hit volumes
            foreach (Transform child in t)
                if (!child.name.StartsWith("Glove Model")) HideRenderers(child);

            puppet.transform.SetParent(t, false);
            puppet.transform.localPosition = new Vector3(0f, -1.7f, 0f);
            puppet.transform.localRotation = Quaternion.identity;
            EditorScale(puppet, 1.6f);   // preview only: at runtime he is fitted to your height

            opp.wristL = new GameObject("Wrist L (IK)").transform; opp.wristL.SetParent(t, false);
            opp.wristR = new GameObject("Wrist R (IK)").transform; opp.wristR.SetParent(t, false);
            puppet.leftWrist = opp.wristL; puppet.rightWrist = opp.wristR;
            puppet.lookWeight = 0f;   // his head follows the root, which already faces you
            opp.puppet = puppet;
            opp.tintRenderers = puppet.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (glove == null)
            {
                // no glove model: keep the red sphere gloves visible over his hands
                opp.gloveLRenderer.enabled = true;
                opp.gloveRRenderer.enabled = true;
            }
        }

        /// <summary>Your gloves and body. hands = controllers, fists = primitive gloves (hit volumes).</summary>
        public static void ApplyPlayer(Transform leftHand, Transform rightHand, Transform fistL, Transform fistR,
                                       PlayerBody body, Transform primitiveArms, float floorY)
        {
            string glove = FindGlove();
            if (glove != null)
            {
                var l = MakeGlove(glove, leftHand, fistL.localPosition + new Vector3(0f, 0f, -0.04f), Quaternion.identity, true, "Glove Model L", MyGloveLength);
                var r = MakeGlove(glove, rightHand, fistR.localPosition + new Vector3(0f, 0f, -0.04f), Quaternion.identity, false, "Glove Model R", MyGloveLength);
                if (l && r)
                {
                    foreach (Transform c in leftHand) if (!c.name.StartsWith("Glove Model")) foreach (var rr in c.GetComponents<Renderer>()) rr.enabled = false;
                    foreach (Transform c in rightHand) if (!c.name.StartsWith("Glove Model")) foreach (var rr in c.GetComponents<Renderer>()) rr.enabled = false;
                    Tint(l, new Color(0.12f, 0.35f, 0.95f));
                    Tint(r, new Color(0.12f, 0.35f, 0.95f));
                }
            }

            if (!HasBoxer) return;
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath) ?? PrepareBoxer();
            var puppet = Spawn("My Body", ctrl);
            puppet.hideHead = true;
            EditorScale(puppet, 1.6f);
            puppet.lookWeight = 0f;
            body.puppet = puppet;
            body.floorY = floorY;
            body.wristL = new GameObject("My Wrist L (IK)").transform;
            body.wristR = new GameObject("My Wrist R (IK)").transform;
            body.wristL.SetParent(body.transform, false);
            body.wristR.SetParent(body.transform, false);
            puppet.leftWrist = body.wristL; puppet.rightWrist = body.wristR;
            HideRenderers(primitiveArms);
        }

        static void Tint(GameObject g, Color c)
        {
            g.AddComponent<TintOnStart>().color = c;   // property blocks are not saved in scenes, so tint at runtime
        }
    }
}
