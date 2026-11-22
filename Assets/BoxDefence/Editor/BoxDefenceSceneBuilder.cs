using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoxDefence.EditorTools
{
    /// <summary>
    /// BoxDefence > Build Training Scene. Makes Assets/BoxDefence/Scenes/BoxingScene.unity
    /// from the template's BasicScene (which has a working XR rig), then builds the
    /// arena, the crowd, the ring, the opponent, your gloves and arms, the level pads
    /// and the text panels, and wires every reference. No dragging in the editor.
    /// </summary>
    public static class BoxDefenceSceneBuilder
    {
        const string Root = "Assets/BoxDefence";
        const string SceneDir = Root + "/Scenes";
        const string MatDir = Root + "/Materials";
        const string ScenePath = SceneDir + "/BoxingScene.unity";
        static readonly Vector3 RingCentre = new Vector3(0f, 0f, 0.5f);
        const float PlatformHeight = 0.9f;

        [MenuItem("BoxDefence/Build Training Scene", priority = 0)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string basic = FindBasicScene();
            if (basic == null)
            {
                EditorUtility.DisplayDialog("BoxDefence", "Could not find the template's BasicScene.unity.", "OK");
                return;
            }

            EnsureFolder(Root); EnsureFolder(SceneDir); EnsureFolder(MatDir);
            if (File.Exists(ScenePath))
            {
                if (!EditorUtility.DisplayDialog("BoxDefence", "BoxingScene already exists. Rebuild it from scratch?", "Rebuild", "Cancel")) return;
                AssetDatabase.DeleteAsset(ScenePath);
            }
            AssetDatabase.CopyAsset(basic, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // ---------------------------------------------------------- materials
            var m = new Mats();

            // ---------------------------------------------------------- XR rig
            var origin = GameObject.Find("XR Origin (XR Rig)");
            if (!origin)
            {
                EditorUtility.DisplayDialog("BoxDefence", "No 'XR Origin (XR Rig)' in BasicScene. Tell Claude.", "OK");
                return;
            }
            var cam = FindDeep(origin.transform, "Main Camera");
            var leftHand = FindDeep(origin.transform, "Left Controller");
            var rightHand = FindDeep(origin.transform, "Right Controller");
            if (!cam || !leftHand || !rightHand)
            {
                EditorUtility.DisplayDialog("BoxDefence", "Could not find the camera or controllers in the XR rig. Tell Claude.", "OK");
                return;
            }
            var loco = FindDeep(origin.transform, "Locomotion");
            if (loco) loco.gameObject.SetActive(false);              // you stay in your real space
            foreach (var hand in new[] { leftHand, rightHand })
                foreach (Transform child in hand)
                    if (child.name.Contains("Interactor")) child.gameObject.SetActive(false); // no laser pointers

            var camera = cam.GetComponent<Camera>();
            if (camera)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.02f, 0.035f);
            }

            var gloveL = PlayerGlove(leftHand, -1f, m);
            var gloveR = PlayerGlove(rightHand, 1f, m);

            var flash = Prim(PrimitiveType.Quad, "Hit Flash", cam, new Vector3(0f, 0f, 0.12f), Vector3.one * 0.6f, m.flash);
            var flashR = flash.GetComponent<Renderer>();
            flashR.shadowCastingMode = ShadowCastingMode.Off;
            flashR.enabled = false;

            var bodyRoot = new GameObject("Player Arms").transform;
            var pUpperL = Prim(PrimitiveType.Capsule, "Upper Arm L", bodyRoot, Vector3.zero, Vector3.one * 0.08f, m.skin);
            var pForeL = Prim(PrimitiveType.Capsule, "Forearm L", bodyRoot, Vector3.zero, Vector3.one * 0.07f, m.skin);
            var pUpperR = Prim(PrimitiveType.Capsule, "Upper Arm R", bodyRoot, Vector3.zero, Vector3.one * 0.08f, m.skin);
            var pForeR = Prim(PrimitiveType.Capsule, "Forearm R", bodyRoot, Vector3.zero, Vector3.one * 0.07f, m.skin);

            // ---------------------------------------------------------- arena, ring, crowd
            BuildArena(m);

            // ---------------------------------------------------------- opponent
            var opp = BuildOpponent(m);
            BoxDefenceModels.ApplyOpponent(opp);      // real boxer / gloves if the files are there

            var top = Text("Scoreboard", opp.transform, new Vector3(0f, 0.45f, 0f), new Vector2(1.3f, 0.3f), 0.9f);
            var popup = Text("Popup", opp.transform, new Vector3(0f, 0.24f, 0.05f), new Vector2(0.9f, 0.22f), 1.2f);
            popup.fontStyle = FontStyles.Bold;

            // ---------------------------------------------------------- stats panel
            var panel = new GameObject("Panel").transform;
            panel.position = new Vector3(0f, 1.6f, 1.6f);
            Prim(PrimitiveType.Quad, "Background", panel, new Vector3(0f, -0.15f, 0.02f), new Vector3(1.55f, 1.25f, 1f), m.panel);
            var panelText = Text("Text", panel, new Vector3(0f, 0.05f, 0f), new Vector2(1.4f, 0.82f), 0.7f, facePlayer: false);
            panelText.alignment = TextAlignmentOptions.TopLeft;
            var bars = new GameObject("Bars").transform;
            bars.SetParent(panel, false);
            bars.localPosition = new Vector3(0f, -0.74f, 0f);

            // ---------------------------------------------------------- level pads
            var menuGo = new GameObject("Level Menu");
            menuGo.transform.position = new Vector3(0f, 1.4f, 0.55f);
            var menu = menuGo.AddComponent<LevelMenu>();
            string[] names = { "EASY", "MEDIUM", "HARD", "PRO" };
            for (int i = 0; i < 4; i++)
            {
                float x = -0.48f + i * 0.32f;
                var pad = Prim(PrimitiveType.Cube, "Pad " + names[i], menuGo.transform, new Vector3(x, 0f, 0f), new Vector3(0.24f, 0.18f, 0.06f), m.pad);
                menu.pads[i] = pad.transform;
                menu.padRenderers[i] = pad.GetComponent<Renderer>();
                var label = Text("Label " + names[i], menuGo.transform, new Vector3(x, 0f, -0.04f), new Vector2(0.22f, 0.1f), 0.6f, facePlayer: false);
                label.text = $"<b>{names[i]}</b>";
                label.color = Color.black;
                menu.labels[i] = label;
            }

            // ---------------------------------------------------------- the logic
            var game = new GameObject("BoxDefence");
            var rig = game.AddComponent<PlayerRig>();
            rig.head = cam; rig.leftHand = leftHand; rig.rightHand = rightHand;
            rig.leftGlove = gloveL; rig.rightGlove = gloveR;
            rig.gloveRadius = 0.065f;

            var body = game.AddComponent<PlayerBody>();
            body.rig = rig;
            body.upperL = pUpperL.transform; body.foreL = pForeL.transform;
            body.upperR = pUpperR.transform; body.foreR = pForeR.transform;
            BoxDefenceModels.ApplyPlayer(leftHand, rightHand, gloveL, gloveR, body, bodyRoot, origin.transform.position.y);

            var audio = game.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            var crowdGo = new GameObject("Crowd Sound");
            crowdGo.transform.SetParent(game.transform, false);
            var crowdAudio = crowdGo.AddComponent<AudioSource>();
            crowdAudio.playOnAwake = false;

            var fb = game.AddComponent<Feedback>();
            fb.rig = rig; fb.sfx2D = audio; fb.flash = flashR; fb.popup = popup; fb.crowd = crowdAudio;

            var hud = game.AddComponent<Hud>();
            hud.rig = rig; hud.top = top; hud.panel = panel; hud.panelText = panelText; hud.bars = bars; hud.barMaterial = m.bar;

            opp.rig = rig;
            menu.rig = rig;

            var session = game.AddComponent<TrainerSession>();
            session.rig = rig; session.opponent = opp; session.hud = hud; session.feedback = fb; session.levelMenu = menu;

            // ---------------------------------------------------------- lighting
            var sun = GameObject.Find("Directional Light");
            if (sun)
            {
                sun.transform.rotation = Quaternion.Euler(65f, 20f, 0f);
                var light = sun.GetComponent<Light>();
                if (light) { light.intensity = 1.3f; light.shadows = LightShadows.None; }
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.36f, 0.42f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 34f;
            RenderSettings.fogColor = new Color(0.03f, 0.03f, 0.05f);

            // ---------------------------------------------------------- save
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "PatrikKlimko";
            PlayerSettings.productName = "Box Defence Trainer";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.patrikklimko.boxdefencetrainer");
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = game;
            Debug.Log("[BoxDefence] BoxingScene built and added to the build.");
            string models = (BoxDefenceModels.HasBoxer ? "Real boxer: YES" : "Real boxer: no (Models/Boxer/Boxer.fbx missing)")
                          + "\n" + (BoxDefenceModels.HasGlove ? "Real gloves: YES" : "Real gloves: no (no .fbx/.obj in Models/Glove)");
            EditorUtility.DisplayDialog("BoxDefence", "BoxingScene is ready.\n\n" + models + "\n\nFile > Build Profiles > Build And Run.", "OK");
        }

        [MenuItem("BoxDefence/Open Saved Sessions Folder", priority = 20)]
        public static void OpenSessions()
        {
            string dir = Path.Combine(Application.persistentDataPath, "sessions");
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        // ---------------------------------------------------------------- the player's gloves

        static Transform PlayerGlove(Transform hand, float side, Mats m)
        {
            // the fist is the scoring centre; thumb and cuff are separate so they keep their shape
            var fist = Prim(PrimitiveType.Sphere, side < 0 ? "Glove L" : "Glove R", hand, new Vector3(0f, -0.01f, 0.035f), new Vector3(0.10f, 0.105f, 0.13f), m.playerGlove);
            Prim(PrimitiveType.Capsule, "Thumb", hand, new Vector3(-side * 0.045f, 0.0f, 0.045f), new Vector3(0.035f, 0.035f, 0.035f), m.playerGlove)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Prim(PrimitiveType.Cylinder, "Cuff", hand, new Vector3(0f, -0.012f, -0.045f), new Vector3(0.085f, 0.035f, 0.085f), m.white)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return fist.transform;
        }

        // ---------------------------------------------------------------- opponent

        static Opponent BuildOpponent(Mats m)
        {
            var go = new GameObject("Opponent");
            go.transform.SetPositionAndRotation(new Vector3(0f, 1.7f, 0.95f), Quaternion.LookRotation(Vector3.back));
            var opp = go.AddComponent<Opponent>();
            var t = go.transform;

            var head = Prim(PrimitiveType.Sphere, "Head", t, Vector3.zero, new Vector3(0.21f, 0.24f, 0.23f), m.skin);
            var h = head.transform;
            Prim(PrimitiveType.Sphere, "Hair", h, new Vector3(0f, 0.16f, -0.08f), new Vector3(1.02f, 0.75f, 0.95f), m.hair);
            Prim(PrimitiveType.Sphere, "Eye L", h, new Vector3(-0.2f, 0.1f, 0.44f), Vector3.one * 0.13f, m.eye);
            Prim(PrimitiveType.Sphere, "Eye R", h, new Vector3(0.2f, 0.1f, 0.44f), Vector3.one * 0.13f, m.eye);
            Prim(PrimitiveType.Cube, "Brow L", h, new Vector3(-0.2f, 0.22f, 0.43f), new Vector3(0.24f, 0.05f, 0.06f), m.hair);
            Prim(PrimitiveType.Cube, "Brow R", h, new Vector3(0.2f, 0.22f, 0.43f), new Vector3(0.24f, 0.05f, 0.06f), m.hair);
            Prim(PrimitiveType.Capsule, "Nose", h, new Vector3(0f, -0.02f, 0.49f), new Vector3(0.12f, 0.12f, 0.12f), m.skin);
            Prim(PrimitiveType.Sphere, "Ear L", h, new Vector3(-0.5f, 0.02f, 0f), new Vector3(0.12f, 0.28f, 0.18f), m.skin);
            Prim(PrimitiveType.Sphere, "Ear R", h, new Vector3(0.5f, 0.02f, 0f), new Vector3(0.12f, 0.28f, 0.18f), m.skin);
            Prim(PrimitiveType.Cube, "Mouthguard", h, new Vector3(0f, -0.25f, 0.45f), new Vector3(0.32f, 0.08f, 0.08f), m.white);

            Prim(PrimitiveType.Cylinder, "Neck", t, new Vector3(0f, -0.18f, 0f), new Vector3(0.11f, 0.07f, 0.11f), m.skin);
            var torso = Prim(PrimitiveType.Capsule, "Torso", t, new Vector3(0f, -0.47f, 0f), new Vector3(0.44f, 0.3f, 0.26f), m.skin);
            Prim(PrimitiveType.Sphere, "Shoulder L", t, new Vector3(-0.2f, -0.3f, 0f), Vector3.one * 0.14f, m.skin);
            Prim(PrimitiveType.Sphere, "Shoulder R", t, new Vector3(0.2f, -0.3f, 0f), Vector3.one * 0.14f, m.skin);
            Prim(PrimitiveType.Cylinder, "Trunks", t, new Vector3(0f, -0.86f, 0f), new Vector3(0.38f, 0.13f, 0.28f), m.trunks);
            Prim(PrimitiveType.Cylinder, "Waistband", t, new Vector3(0f, -0.74f, 0f), new Vector3(0.385f, 0.02f, 0.285f), m.white);

            var gl = Prim(PrimitiveType.Sphere, "Glove L", t, new Vector3(-0.13f, -0.1f, 0.22f), new Vector3(0.14f, 0.15f, 0.17f), m.oppGlove);
            var gr = Prim(PrimitiveType.Sphere, "Glove R", t, new Vector3(0.13f, -0.1f, 0.22f), new Vector3(0.14f, 0.15f, 0.17f), m.oppGlove);
            opp.upperArmL = Prim(PrimitiveType.Capsule, "Upper Arm L", t, Vector3.zero, Vector3.one * 0.1f, m.skin).transform;
            opp.foreArmL = Prim(PrimitiveType.Capsule, "Forearm L", t, Vector3.zero, Vector3.one * 0.1f, m.skin).transform;
            opp.upperArmR = Prim(PrimitiveType.Capsule, "Upper Arm R", t, Vector3.zero, Vector3.one * 0.1f, m.skin).transform;
            opp.foreArmR = Prim(PrimitiveType.Capsule, "Forearm R", t, Vector3.zero, Vector3.one * 0.1f, m.skin).transform;
            opp.thighL = Prim(PrimitiveType.Capsule, "Thigh L", t, Vector3.zero, Vector3.one * 0.1f, m.skin).transform;
            opp.shinL = Prim(PrimitiveType.Capsule, "Shin L", t, Vector3.zero, Vector3.one * 0.1f, m.skin).transform;
            opp.thighR = Prim(PrimitiveType.Capsule, "Thigh R", t, Vector3.zero, Vector3.one * 0.1f, m.skin).transform;
            opp.shinR = Prim(PrimitiveType.Capsule, "Shin R", t, Vector3.zero, Vector3.one * 0.1f, m.skin).transform;
            opp.footL = Prim(PrimitiveType.Cube, "Shoe L", t, Vector3.zero, new Vector3(0.11f, 0.08f, 0.27f), m.shoe).transform;
            opp.footR = Prim(PrimitiveType.Cube, "Shoe R", t, Vector3.zero, new Vector3(0.11f, 0.08f, 0.27f), m.shoe).transform;

            opp.head = h;
            opp.torso = torso.transform;
            opp.gloveL = gl.transform; opp.gloveR = gr.transform;
            opp.headRenderer = head.GetComponent<Renderer>();
            opp.gloveLRenderer = gl.GetComponent<Renderer>();
            opp.gloveRRenderer = gr.GetComponent<Renderer>();
            opp.floorY = 0f;
            return opp;
        }

        // ---------------------------------------------------------------- arena

        static void BuildArena(Mats m)
        {
            float floorY = -PlatformHeight;
            var arena = new GameObject("Arena").transform;

            var floor = GameObject.Find("Plane");
            if (floor)
            {
                floor.name = "Arena Floor";
                floor.transform.SetParent(arena, true);
                floor.transform.position = new Vector3(RingCentre.x, floorY, RingCentre.z);
                floor.transform.localScale = new Vector3(6f, 1f, 6f);
                floor.GetComponent<Renderer>().sharedMaterial = m.arenaFloor;
            }

            // ring: raised platform, canvas on top at the real floor height (y = 0)
            var ring = new GameObject("Ring").transform;
            ring.SetParent(arena, false);
            Prim(PrimitiveType.Cube, "Apron", ring, RingCentre + new Vector3(0f, floorY / 2f - 0.05f, 0f), new Vector3(5.6f, PlatformHeight - 0.1f, 5.6f), m.apron);
            Prim(PrimitiveType.Cube, "Canvas", ring, RingCentre + new Vector3(0f, -0.05f, 0f), new Vector3(5.4f, 0.1f, 5.4f), m.canvas);
            const float half = 2.5f;
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 1) })
            {
                Prim(PrimitiveType.Cylinder, "Post", ring, RingCentre + new Vector3(c.x * half, 0.65f, c.y * half), new Vector3(0.12f, 0.65f, 0.12f), m.post);
                Prim(PrimitiveType.Cube, "Corner Pad", ring, RingCentre + new Vector3(c.x * (half - 0.08f), 0.9f, c.y * (half - 0.08f)), new Vector3(0.14f, 0.8f, 0.14f), c.x * c.y > 0 ? m.ropeRed : m.cornerBlue);
            }
            float[] heights = { 0.55f, 0.9f, 1.25f };
            for (int i = 0; i < heights.Length; i++)
            {
                var mat = i == 1 ? m.white : m.ropeRed;
                float y = heights[i];
                Prim(PrimitiveType.Cylinder, "Rope", ring, RingCentre + new Vector3(0f, y, half), new Vector3(0.035f, half, 0.035f), mat).transform.rotation = Quaternion.Euler(0, 0, 90);
                Prim(PrimitiveType.Cylinder, "Rope", ring, RingCentre + new Vector3(0f, y, -half), new Vector3(0.035f, half, 0.035f), mat).transform.rotation = Quaternion.Euler(0, 0, 90);
                Prim(PrimitiveType.Cylinder, "Rope", ring, RingCentre + new Vector3(half, y, 0f), new Vector3(0.035f, half, 0.035f), mat).transform.rotation = Quaternion.Euler(90, 0, 0);
                Prim(PrimitiveType.Cylinder, "Rope", ring, RingCentre + new Vector3(-half, y, 0f), new Vector3(0.035f, half, 0.035f), mat).transform.rotation = Quaternion.Euler(90, 0, 0);
            }

            // stands and crowd on four sides
            var crowd = new GameObject("Crowd").transform;
            crowd.SetParent(arena, false);
            var rnd = new System.Random(42);
            var sides = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            foreach (var n in sides)
            {
                Vector3 tangent = Vector3.Cross(Vector3.up, n);
                for (int r = 0; r < 3; r++)
                {
                    float d = 4.4f + r * 1.0f;
                    float height = 0.6f + r * 0.6f;
                    float topY = floorY + height;
                    var riser = Prim(PrimitiveType.Cube, "Stand", crowd, RingCentre + n * d + Vector3.up * (floorY + height / 2f), Vector3.one, m.stand);
                    riser.transform.rotation = Quaternion.LookRotation(n, Vector3.up);
                    riser.transform.localScale = new Vector3(2f * d + 1f, height, 1.0f);
                    for (float s = -d + 0.4f; s <= d - 0.4f; s += 0.75f)
                    {
                        if (rnd.NextDouble() < 0.15) continue; // empty seat
                        Vector3 seat = RingCentre + n * d + tangent * (s + (float)(rnd.NextDouble() - 0.5) * 0.15f);
                        float tall = 0.9f + (float)rnd.NextDouble() * 0.2f;
                        var shirt = m.shirts[rnd.Next(m.shirts.Length)];
                        var skin = m.crowdSkins[rnd.Next(m.crowdSkins.Length)];
                        var bodyGo = Prim(PrimitiveType.Capsule, "Fan", crowd, new Vector3(seat.x, topY + 0.4f * tall, seat.z), new Vector3(0.34f, 0.32f * tall, 0.26f), shirt);
                        bodyGo.transform.rotation = Quaternion.LookRotation(-n, Vector3.up);
                        Prim(PrimitiveType.Sphere, "Fan Head", crowd, new Vector3(seat.x, topY + 0.85f * tall, seat.z), Vector3.one * 0.2f, skin);
                    }
                }
            }

            // walls, roof, lights, banner
            var hall = new GameObject("Hall").transform;
            hall.SetParent(arena, false);
            const float wall = 11f, wallH = 10f;
            Prim(PrimitiveType.Quad, "Wall N", hall, RingCentre + new Vector3(0f, floorY + wallH / 2f, wall), new Vector3(2f * wall, wallH, 1f), m.wall).transform.rotation = Quaternion.identity;
            Prim(PrimitiveType.Quad, "Wall S", hall, RingCentre + new Vector3(0f, floorY + wallH / 2f, -wall), new Vector3(2f * wall, wallH, 1f), m.wall).transform.rotation = Quaternion.Euler(0, 180, 0);
            Prim(PrimitiveType.Quad, "Wall E", hall, RingCentre + new Vector3(wall, floorY + wallH / 2f, 0f), new Vector3(2f * wall, wallH, 1f), m.wall).transform.rotation = Quaternion.Euler(0, 90, 0);
            Prim(PrimitiveType.Quad, "Wall W", hall, RingCentre + new Vector3(-wall, floorY + wallH / 2f, 0f), new Vector3(2f * wall, wallH, 1f), m.wall).transform.rotation = Quaternion.Euler(0, -90, 0);
            Prim(PrimitiveType.Quad, "Roof", hall, RingCentre + new Vector3(0f, floorY + wallH, 0f), new Vector3(2f * wall, 2f * wall, 1f), m.roof).transform.rotation = Quaternion.Euler(-90, 0, 0);

            float rigY = 5.2f;
            Prim(PrimitiveType.Cube, "Light Bar N", hall, RingCentre + new Vector3(0f, rigY, 2.8f), new Vector3(5f, 0.12f, 0.12f), m.lightBar);
            Prim(PrimitiveType.Cube, "Light Bar S", hall, RingCentre + new Vector3(0f, rigY, -2.8f), new Vector3(5f, 0.12f, 0.12f), m.lightBar);
            Prim(PrimitiveType.Cube, "Light Bar E", hall, RingCentre + new Vector3(2.8f, rigY, 0f), new Vector3(0.12f, 0.12f, 5f), m.lightBar);
            Prim(PrimitiveType.Cube, "Light Bar W", hall, RingCentre + new Vector3(-2.8f, rigY, 0f), new Vector3(0.12f, 0.12f, 5f), m.lightBar);
            Prim(PrimitiveType.Cube, "Truss", hall, RingCentre + new Vector3(0f, rigY + 0.4f, 0f), new Vector3(6f, 0.1f, 6f), m.truss);

            var banner = Text("Banner", hall, RingCentre + new Vector3(0f, 4.6f, wall - 0.05f), new Vector2(14f, 3f), 30f, facePlayer: false);
            banner.text = "<b>BOX DEFENCE ARENA</b>\n<size=45%>spar without the concussions</size>";
            banner.color = new Color(1f, 0.82f, 0.1f);

            // nothing in the arena moves: let Unity merge it into a few big meshes
            foreach (var tr in arena.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic);
        }

        // ---------------------------------------------------------------- helpers

        class Mats
        {
            public Material arenaFloor, canvas, apron, post, ropeRed, white, cornerBlue, stand, wall, roof, lightBar, truss;
            public Material skin, hair, eye, trunks, shoe, oppGlove, playerGlove, panel, bar, flash, pad;
            public Material[] shirts, crowdSkins;

            public Mats()
            {
                arenaFloor = Lit("ArenaFloor", new Color(0.07f, 0.07f, 0.09f), 0.1f);
                canvas = Lit("Canvas", new Color(0.62f, 0.68f, 0.78f), 0.1f);
                apron = Lit("Apron", new Color(0.08f, 0.12f, 0.35f), 0.2f);
                post = Lit("Post", new Color(0.85f, 0.85f, 0.88f), 0.5f);
                ropeRed = Lit("RopeRed", new Color(0.8f, 0.1f, 0.1f), 0.4f);
                white = Lit("White", new Color(0.93f, 0.93f, 0.93f), 0.4f);
                cornerBlue = Lit("CornerBlue", new Color(0.1f, 0.3f, 0.9f), 0.4f);
                stand = Lit("Stand", new Color(0.12f, 0.12f, 0.15f), 0.1f);
                wall = Lit("Wall", new Color(0.08f, 0.08f, 0.11f), 0.0f);
                roof = Lit("Roof", new Color(0.03f, 0.03f, 0.04f), 0.0f);
                truss = Lit("Truss", new Color(0.15f, 0.15f, 0.17f), 0.3f);
                lightBar = Unlit("LightBar", new Color(1f, 0.97f, 0.88f));
                skin = Lit("Skin", new Color(0.80f, 0.58f, 0.44f), 0.35f);
                hair = Lit("Hair", new Color(0.12f, 0.08f, 0.06f), 0.2f);
                eye = Lit("Eye", new Color(0.05f, 0.05f, 0.05f), 0.8f);
                trunks = Lit("Trunks", new Color(0.75f, 0.08f, 0.1f), 0.5f);
                shoe = Lit("Shoe", new Color(0.06f, 0.06f, 0.07f), 0.4f);
                oppGlove = Lit("OpponentGlove", new Color(0.75f, 0.06f, 0.06f), 0.6f);
                playerGlove = Lit("PlayerGlove", new Color(0.12f, 0.3f, 0.9f), 0.6f);
                panel = Lit("PanelBack", new Color(0.05f, 0.06f, 0.08f), 0.0f);
                bar = Lit("Bar", Color.white, 0.2f);
                pad = Lit("Pad", Color.white, 0.3f);
                flash = TransparentUnlit("HitFlash", new Color(0.9f, 0f, 0f, 0f));
                shirts = new[]
                {
                    Lit("Shirt1", new Color(0.8f, 0.15f, 0.15f), 0.2f),
                    Lit("Shirt2", new Color(0.15f, 0.3f, 0.8f), 0.2f),
                    Lit("Shirt3", new Color(0.9f, 0.9f, 0.9f), 0.2f),
                    Lit("Shirt4", new Color(0.12f, 0.12f, 0.12f), 0.2f),
                    Lit("Shirt5", new Color(0.95f, 0.75f, 0.15f), 0.2f),
                    Lit("Shirt6", new Color(0.2f, 0.6f, 0.3f), 0.2f),
                };
                crowdSkins = new[]
                {
                    Lit("CrowdSkin1", new Color(0.92f, 0.74f, 0.6f), 0.3f),
                    Lit("CrowdSkin2", new Color(0.72f, 0.5f, 0.36f), 0.3f),
                    Lit("CrowdSkin3", new Color(0.45f, 0.3f, 0.2f), 0.3f),
                };
            }
        }

        static string FindBasicScene()
        {
            foreach (var guid in AssetDatabase.FindAssets("BasicScene t:Scene"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == "BasicScene") return p;
            }
            return null;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var r = FindDeep(child, name);
                if (r) return r;
            }
            return null;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static TextMeshPro Text(string name, Transform parent, Vector3 localPos, Vector2 size, float maxFont, bool facePlayer = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            // text reads correctly when you look along its +Z. The opponent's +Z points at the player.
            go.transform.localRotation = facePlayer ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = size;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.05f;
            tmp.fontSizeMax = maxFont;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.text = "";
            return tmp;
        }

        static Material Lit(string name, Color color, float smoothness)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Color color)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LoadOrCreate(string name, string shader)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static Material TransparentUnlit(string name, Color color)
        {
            var m = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent + 100;
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
