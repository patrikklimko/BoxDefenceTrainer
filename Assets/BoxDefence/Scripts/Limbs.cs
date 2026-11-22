using UnityEngine;

namespace BoxDefence
{
    /// <summary>Places a capsule or cylinder so it runs from point a to point b.</summary>
    public static class Limbs
    {
        public static void Stretch(Transform t, Vector3 a, Vector3 b, float thickness)
        {
            if (!t) return;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            t.position = (a + b) * 0.5f;
            t.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            t.localScale = new Vector3(thickness, len * 0.5f, thickness); // primitive is 2 units tall
        }

        public static void Place(Transform t, Vector3 position)
        {
            if (t) t.position = position;
        }
    }
}
