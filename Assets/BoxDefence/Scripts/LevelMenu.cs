using TMPro;
using UnityEngine;

namespace BoxDefence
{
    /// <summary>
    /// Four pads in front of you. Punch one to choose the level. A is a shortcut
    /// for the last level you chose.
    /// </summary>
    public class LevelMenu : MonoBehaviour
    {
        public PlayerRig rig;
        public Transform[] pads = new Transform[4];
        public Renderer[] padRenderers = new Renderer[4];
        public TextMeshPro[] labels = new TextMeshPro[4];
        public Color[] colors =
        {
            new Color(0.2f, 0.8f, 0.35f),
            new Color(0.95f, 0.8f, 0.15f),
            new Color(0.95f, 0.45f, 0.1f),
            new Color(0.9f, 0.15f, 0.2f),
        };
        public float padHalfSize = 0.13f;

        public Level Current { get; private set; } = Level.Easy;

        bool chosen;
        Vector3[] baseScale;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            baseScale = new Vector3[pads.Length];
            for (int i = 0; i < pads.Length; i++) if (pads[i]) baseScale[i] = pads[i].localScale;
        }

        public void Show(Vector3 facing)
        {
            gameObject.SetActive(true);
            Vector3 h = rig ? rig.HeadPos : Vector3.up * 1.6f;
            transform.position = h + facing * 0.55f + Vector3.down * 0.22f;
            transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            chosen = false;
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>True once, on the frame a pad was punched.</summary>
        public bool TryGetChoice(out Level level)
        {
            level = Current;
            if (!chosen) return false;
            chosen = false;
            return true;
        }

        void Update()
        {
            if (!rig) return;
            for (int i = 0; i < pads.Length; i++)
            {
                if (!pads[i]) continue;
                bool hover = false, hit = false;
                Check(rig.LeftGlovePos, rig.LeftVelocity, pads[i], ref hover, ref hit);
                Check(rig.RightGlovePos, rig.RightVelocity, pads[i], ref hover, ref hit);

                if (hover) Current = (Level)i;
                if (hit && !chosen)
                {
                    Current = (Level)i;
                    chosen = true;
                    rig.HapticBoth(0.7f, 0.12f);
                }

                bool lit = hover || Current == (Level)i;
                pads[i].localScale = baseScale[i] * (hover ? 1.15f : 1f);
                padRenderers[i].GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, lit ? colors[i] : colors[i] * 0.45f);
                padRenderers[i].SetPropertyBlock(mpb);
            }
        }

        void Check(Vector3 glove, Vector3 velocity, Transform pad, ref bool hover, ref bool hit)
        {
            float d = (glove - pad.position).magnitude;
            if (d < padHalfSize + 0.08f) hover = true;
            if (d < padHalfSize + 0.03f && velocity.magnitude > 0.8f) hit = true;
        }
    }
}
