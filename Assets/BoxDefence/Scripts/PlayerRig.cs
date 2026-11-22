using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRNode = UnityEngine.XR.XRNode;

namespace BoxDefence
{
    /// <summary>
    /// The player's body as the app sees it: the headset (head) and the two
    /// controllers (gloves). Also reads the start button and drives vibration.
    /// Runs before everything else so all scripts see the same positions in a frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerRig : MonoBehaviour
    {
        [Header("Tracked transforms (set by the scene builder)")]
        public Transform head;
        public Transform leftHand;
        public Transform rightHand;

        [Header("Glove spheres, children of the hands")]
        public Transform leftGlove;
        public Transform rightGlove;

        [Header("Sizes, metres")]
        public float headRadius = 0.11f;
        public float gloveRadius = 0.07f;

        public Vector3 HeadPos => head ? head.position : Vector3.up * 1.6f;
        public Vector3 LeftGlovePos => leftGlove ? leftGlove.position : HeadPos;
        public Vector3 RightGlovePos => rightGlove ? rightGlove.position : HeadPos;
        public Vector3 LeftVelocity { get; private set; }
        public Vector3 RightVelocity { get; private set; }

        /// <summary>Horizontal direction the player is looking.</summary>
        public Vector3 Facing
        {
            get
            {
                Vector3 f = head ? head.forward : Vector3.forward;
                f.y = 0f;
                return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            }
        }

        /// <summary>
        /// Moves the whole XR rig so your head is at spot (keeping your height) and you face forward.
        /// Your real room doesn't change; the arena is placed around you.
        /// </summary>
        public void PlaceInRing(Vector3 spot, Vector3 forward)
        {
            var origin = head ? head.GetComponentInParent<XROrigin>() : null;
            if (!origin) return;
            float angle = Vector3.SignedAngle(Facing, forward, Vector3.up);
            origin.RotateAroundCameraUsingOriginUp(angle);
            origin.MoveCameraToWorldLocation(new Vector3(spot.x, head.position.y, spot.z));
            prevLeft = LeftGlovePos; prevRight = RightGlovePos;   // no fake velocity spike from the jump
            LeftVelocity = RightVelocity = Vector3.zero;
        }

        Vector3 prevLeft, prevRight;
        bool prevButton;
        bool hasPrev;

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 l = LeftGlovePos, r = RightGlovePos;
            if (hasPrev)
            {
                // light smoothing: tracking jitter would otherwise look like punches
                LeftVelocity = Vector3.Lerp(LeftVelocity, (l - prevLeft) / dt, 0.6f);
                RightVelocity = Vector3.Lerp(RightVelocity, (r - prevRight) / dt, 0.6f);
            }
            prevLeft = l;
            prevRight = r;
            hasPrev = true;
        }

        /// <summary>True on the frame A, X or either trigger is pressed (Space in the editor).</summary>
        public bool StartPressed()
        {
            bool now = ButtonHeld();
            bool pressed = now && !prevButton;
            prevButton = now;
            return pressed;
        }

        bool ButtonHeld()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed) return true;
            if (Held(XRController.leftHand) || Held(XRController.rightHand)) return true;

            // fallback: the XR input API, in case the Input System device has other control names
            foreach (var node in new[] { XRNode.LeftHand, XRNode.RightHand })
            {
                var d = XRInputDevices.GetDeviceAtXRNode(node);
                if (!d.isValid) continue;
                if (d.TryGetFeatureValue(XRCommonUsages.primaryButton, out bool a) && a) return true;
                if (d.TryGetFeatureValue(XRCommonUsages.triggerButton, out bool t) && t) return true;
            }
            return false;
        }

        static bool Held(XRController c)
        {
            if (c == null) return false;
            foreach (var name in new[] { "primaryButton", "triggerPressed", "triggerButton" })
            {
                var b = c.TryGetChildControl<ButtonControl>(name);
                if (b != null && b.isPressed) return true;
            }
            return false;
        }

        /// <summary>Vibrate one controller. amplitude 0..1, duration in seconds.</summary>
        public void Haptic(bool left, float amplitude, float duration)
        {
            if ((left ? XRController.leftHand : XRController.rightHand) is XRControllerWithRumble rumble)
            {
                rumble.SendImpulse(amplitude, duration);
                return;
            }
            var d = XRInputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
            if (d.isValid) d.SendHapticImpulse(0, amplitude, duration);
        }

        public void HapticBoth(float amplitude, float duration)
        {
            Haptic(true, amplitude, duration);
            Haptic(false, amplitude, duration);
        }
    }
}
