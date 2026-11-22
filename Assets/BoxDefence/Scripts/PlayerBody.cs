using UnityEngine;

namespace BoxDefence
{
    /// <summary>
    /// Your own arms. The headset only knows where your head and hands are, so the
    /// shoulders are estimated from the head and the elbows are solved with two-bone IK.
    /// With a real model (puppet) the body stands under your head, its head is hidden,
    /// and Unity's humanoid IK puts its hands inside your gloves.
    /// </summary>
    public class PlayerBody : MonoBehaviour
    {
        public PlayerRig rig;
        public Transform upperL, foreL, upperR, foreR;
        public float upperArm = 0.30f;
        public float foreArm = 0.28f;

        [Header("Real model (optional)")]
        public HumanoidPuppet puppet;
        public Transform wristL, wristR;
        public float floorY;
        public float backOffset = 0.14f;   // body behind the eyes, so you don't see inside the neck

        Vector3 bodyFacing = Vector3.forward;
        float lastHeight = -1f;

        void Update()
        {
            if (!puppet || !rig || !rig.head) return;
            Vector3 h = rig.head.position;
            Vector3 f = rig.Facing;
            if (f.sqrMagnitude > 1e-4f) bodyFacing = Vector3.Slerp(bodyFacing, f, 1f - Mathf.Exp(-6f * Time.deltaTime)).normalized;

            // fit once to your standing height (not every duck)
            float eye = h.y - floorY;
            if (eye > lastHeight + 0.02f) { puppet.FitHeight(eye - 0.1f); lastHeight = eye; }
            puppet.transform.rotation = Quaternion.LookRotation(bodyFacing, Vector3.up);
            Vector3 p = h - bodyFacing * backOffset - puppet.HeadOffsetFlat;
            // when you duck the body sinks with you instead of shrinking (you look ahead, not at your feet)
            float sink = Mathf.Min(0f, eye - lastHeight);
            puppet.transform.position = new Vector3(p.x, floorY + sink, p.z);

            SetWrist(wristL, rig.leftGlove);
            SetWrist(wristR, rig.rightGlove);
        }

        static void SetWrist(Transform wrist, Transform glove)
        {
            if (wrist && glove) wrist.position = glove.position - glove.forward * 0.07f;
        }

        void LateUpdate()
        {
            if (puppet || !rig || !rig.head) return;
            Vector3 h = rig.head.position;
            Vector3 f = rig.Facing;
            Vector3 right = Vector3.Cross(Vector3.up, f);

            Draw(-1f, rig.leftGlove, upperL, foreL, h, f, right);
            Draw(1f, rig.rightGlove, upperR, foreR, h, f, right);
        }

        void Draw(float side, Transform glove, Transform upper, Transform fore, Vector3 head, Vector3 f, Vector3 right)
        {
            if (!glove) return;
            Vector3 shoulder = head + Vector3.down * 0.25f + right * side * 0.19f - f * 0.08f;
            Vector3 wrist = glove.position - glove.forward * 0.07f;
            Vector3 pole = shoulder + Vector3.down * 0.4f + right * side * 0.3f - f * 0.15f;
            Vector3 elbow = DefenceMath.Elbow(shoulder, wrist, upperArm, foreArm, pole);
            Limbs.Stretch(upper, shoulder, elbow, 0.085f);
            Limbs.Stretch(fore, elbow, wrist, 0.07f);
        }
    }
}
