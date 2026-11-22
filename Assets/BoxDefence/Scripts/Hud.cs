using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BoxDefence
{
    /// <summary>
    /// World-space text: a small scoreboard above the opponent during a round,
    /// and a big panel for the menu and the stats, with a bar per punch.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public PlayerRig rig;
        public TextMeshPro top;        // child of the opponent
        public Transform panel;        // panel root: background + text + bars
        public TextMeshPro panelText;
        public Transform bars;
        public Material barMaterial;

        readonly List<GameObject> barObjects = new List<GameObject>();
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static readonly Color SlipColor = new Color(0.2f, 0.85f, 0.35f);
        public static readonly Color BlockColor = new Color(0.25f, 0.55f, 1f);
        public static readonly Color HitColor = new Color(0.95f, 0.2f, 0.2f);
        public static readonly Color MissColor = new Color(0.6f, 0.6f, 0.6f);
        public static readonly Color FeintColor = new Color(0.7f, 0.4f, 1f);

        void Awake() => mpb = new MaterialPropertyBlock();

        public void ShowTop(string text)
        {
            if (!top) return;
            top.gameObject.SetActive(true);
            top.text = text;
        }

        public void HideTop()
        {
            if (top) top.gameObject.SetActive(false);
        }

        /// <summary>Show the panel straight ahead of the player, at eye height.</summary>
        public void ShowPanel(string text, Vector3 facing, float distance = 1.4f, float raise = 0f)
        {
            if (!panel) return;
            panel.gameObject.SetActive(true);
            panelText.text = text;
            Vector3 h = rig ? rig.HeadPos : Vector3.up * 1.6f;
            panel.position = h + facing * distance + Vector3.up * (raise - 0.1f);
            panel.rotation = Quaternion.LookRotation(facing, Vector3.up);
            ClearBars();
        }

        public void SetPanelText(string text)
        {
            if (panelText) panelText.text = text;
        }

        public void HidePanel()
        {
            if (panel) panel.gameObject.SetActive(false);
            ClearBars();
        }

        /// <summary>One bar per punch: height = reaction time, colour = what happened.</summary>
        public void ShowBars(List<PunchRecord> punches)
        {
            ClearBars();
            if (!bars || punches.Count == 0) return;
            const float width = 1.2f, maxHeight = 0.2f, maxMs = 900f;
            float step = width / punches.Count;
            float w = Mathf.Min(0.05f, step * 0.8f);
            for (int i = 0; i < punches.Count; i++)
            {
                var p = punches[i];
                float h = p.reactionMs > 0f ? Mathf.Clamp01(p.reactionMs / maxMs) * maxHeight : 0.015f;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(bars, false);
                go.transform.localPosition = new Vector3(-width / 2f + step * (i + 0.5f), h / 2f, 0f);
                go.transform.localScale = new Vector3(w, h, 0.01f);
                var r = go.GetComponent<Renderer>();
                if (barMaterial) r.sharedMaterial = barMaterial;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, ColorFor(p.outcome));
                r.SetPropertyBlock(mpb);
                barObjects.Add(go);
            }
        }

        static Color ColorFor(string outcome) => outcome switch
        {
            "HIT" => HitColor,
            "BLOCKED" => BlockColor,
            "MISSED" => MissColor,
            "BIT ON FEINT" => FeintColor,
            "READ FEINT" => FeintColor,
            _ => SlipColor,
        };

        void ClearBars()
        {
            foreach (var b in barObjects) if (b) Destroy(b);
            barObjects.Clear();
        }
    }
}
