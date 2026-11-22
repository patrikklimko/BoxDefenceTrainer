using UnityEngine;

namespace BoxDefence
{
    /// <summary>Tints every renderer below this object (a white glove model becomes blue).</summary>
    public class TintOnStart : MonoBehaviour
    {
        public Color color = Color.white;
        void Start()
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", color);
                mpb.SetColor("_Color", color);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
