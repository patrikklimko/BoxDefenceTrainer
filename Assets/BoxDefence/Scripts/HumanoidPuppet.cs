using UnityEngine;

namespace BoxDefence
{
    /// <summary>
    /// Drives a rigged human model (Mixamo): plays its idle, pulls both hands to the
    /// given wrist targets with Unity's humanoid IK, turns the head towards a target,
    /// and can lean the upper body or hide the head (for your own first-person body).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class HumanoidPuppet : MonoBehaviour
    {
        public Transform leftWrist;
        public Transform rightWrist;
        public Transform lookTarget;
        [Range(0f, 1f)] public float handWeight = 1f;
        [Range(0f, 1f)] public float lookWeight = 0.8f;
        public bool hideHead;
        [Tooltip("Shrink the bare hands so they stay inside the gloves")]
        public float handScale = 0.001f;   // hidden: the glove is the hand

        /// <summary>World-space offset for the head, applied by bending the chest (slips, flinches).</summary>
        [HideInInspector] public Vector3 headOffset;

        /// <summary>Where the animated head bone sits, in the model's own (unscaled) space, averaged over the idle bounce.</summary>
        public Vector3 HeadLocal { get; private set; } = new Vector3(0f, 1.6f, 0f);

        /// <summary>Horizontal world offset from the model's feet to its head (fight stance leans forward).</summary>
        public Vector3 HeadOffsetFlat
        {
            get { var v = transform.TransformVector(HeadLocal); v.y = 0f; return v; }
        }

        Animator anim;
        Transform headBone, chestBone, handL, handR;
        float targetHeadHeight = -1f;
        bool measured;

        void Awake()
        {
            anim = GetComponent<Animator>();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            headBone = anim.GetBoneTransform(HumanBodyBones.Head);
            chestBone = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (!chestBone) chestBone = anim.GetBoneTransform(HumanBodyBones.Chest);
            handL = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            handR = anim.GetBoneTransform(HumanBodyBones.RightHand);
        }

        /// <summary>Scale the model so its (animated) head bone sits this high above its feet.</summary>
        public void FitHeight(float headBoneHeight)
        {
            targetHeadHeight = headBoneHeight;
            ApplyScale();
        }

        void ApplyScale()
        {
            if (targetHeadHeight <= 0f || HeadLocal.y < 0.3f) return;
            float s = Mathf.Clamp(targetHeadHeight / HeadLocal.y, 0.1f, 3f);
            transform.localScale = Vector3.one * s;
        }

        void OnAnimatorIK(int layer)
        {
            SetHand(AvatarIKGoal.LeftHand, leftWrist);
            SetHand(AvatarIKGoal.RightHand, rightWrist);
            if (lookTarget)
            {
                anim.SetLookAtWeight(lookWeight, 0.15f, 0.8f, 1f, 0.5f);
                anim.SetLookAtPosition(lookTarget.position);
            }
            else anim.SetLookAtWeight(0f);
        }

        void SetHand(AvatarIKGoal goal, Transform target)
        {
            float w = target ? handWeight : 0f;
            anim.SetIKPositionWeight(goal, w);
            anim.SetIKRotationWeight(goal, 0f); // the glove hides the hand, so only its position matters
            if (target) anim.SetIKPosition(goal, target.position);
        }

        void LateUpdate()
        {
            if (headBone)
            {
                // measured after the animation, before our own lean, so it is the stance itself
                Vector3 hl = transform.InverseTransformPoint(headBone.position);
                HeadLocal = measured ? Vector3.Lerp(HeadLocal, hl, 0.05f) : hl;
                measured = true;
                ApplyScale();
            }
            if (hideHead && headBone) headBone.localScale = Vector3.one * 0.001f;
            if (handL) handL.localScale = Vector3.one * handScale;
            if (handR) handR.localScale = Vector3.one * handScale;
            if (chestBone && headBone && headOffset.sqrMagnitude > 1e-6f)
            {
                Vector3 a = headBone.position - chestBone.position;
                chestBone.rotation = Quaternion.FromToRotation(a, a + headOffset) * chestBone.rotation;
            }
        }
    }
}
