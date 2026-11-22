using UnityEngine;

namespace BoxDefence
{
    /// <summary>
    /// The crowd is a few dozen combined meshes (one per block of seats), so moving it is cheap.
    /// Each block bobs on its own rhythm; when the crowd gets excited they bounce higher and faster.
    /// </summary>
    public class CrowdAnimator : MonoBehaviour
    {
        public Transform[] groups;
        float[] phase, speed;
        Vector3[] basePos;
        float excite;

        void Awake()
        {
            if (groups == null) groups = new Transform[0];
            phase = new float[groups.Length];
            speed = new float[groups.Length];
            basePos = new Vector3[groups.Length];
            var rnd = new System.Random(7);
            for (int i = 0; i < groups.Length; i++)
            {
                phase[i] = (float)rnd.NextDouble() * 6.28f;
                speed[i] = 0.8f + (float)rnd.NextDouble() * 0.5f;
                if (groups[i]) basePos[i] = groups[i].localPosition;
            }
        }

        /// <summary>0..1. A cheer for a counter is about 1, an "ooh" about 0.35.</summary>
        public void Excite(float amount) => excite = Mathf.Max(excite, Mathf.Clamp01(amount));

        void Update()
        {
            excite = Mathf.MoveTowards(excite, 0f, Time.deltaTime * 0.45f);
            float t = Time.time;
            float amp = 0.012f + 0.13f * excite;
            float rate = 2.2f + 7f * excite;
            for (int i = 0; i < groups.Length; i++)
            {
                var g = groups[i];
                if (!g) continue;
                float b = Mathf.Abs(Mathf.Sin(t * rate * speed[i] + phase[i]));
                g.localPosition = basePos[i] + Vector3.up * (b * amp);
            }
        }
    }
}
