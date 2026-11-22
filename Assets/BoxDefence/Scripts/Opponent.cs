using System;
using System.Collections;
using UnityEngine;

namespace BoxDefence
{
    public struct PunchResult
    {
        public PunchKind kind;
        public Outcome outcome;
        public float windup;      // s
        public float strike;      // s
        public float reaction;    // s from wind-up start to first head move, -1 if none
        public bool leftHand;
        public Vector3 at;        // where the glove ended up
    }

    public struct LandedHit
    {
        public bool leftHand;
        public bool head;
        public float speed;       // m/s
        public bool counter;
        public bool guarded;      // caught by his gloves, did not land
        public Vector3 at;
    }

    /// <summary>
    /// The sparring partner. He keeps his distance in front of the player, throws
    /// telegraphed punches, combinations and feints at the player's real head
    /// position, and reports what happened to each one. At Pro level he also takes
    /// punches, and blocks or slips some of them.
    /// His body is built from primitives and posed every frame with two-bone IK, so
    /// every position used for scoring is known exactly.
    /// </summary>
    public class Opponent : MonoBehaviour
    {
        public PlayerRig rig;

        [Header("Body parts (set by the scene builder)")]
        public Transform head;
        public Transform torso;
        public Transform gloveL, gloveR;
        public Transform upperArmL, foreArmL, upperArmR, foreArmR;
        public Transform thighL, shinL, thighR, shinR;
        public Transform footL, footR;
        public Renderer headRenderer, gloveLRenderer, gloveRRenderer;

        [Header("Real model (optional, set by the scene builder)")]
        public HumanoidPuppet puppet;
        public Transform wristL, wristR;          // IK targets for the model's hands
        public Renderer[] gloveLRenderers, gloveRRenderers;
        public Renderer[] tintRenderers;           // tinted blue when he is open (Pro)

        [Header("Fight geometry, metres")]
        public float standDistance = 0.95f;
        public float reach = 1.0f;
        public float gloveRadius = 0.075f;
        public float headRadius = 0.12f;
        public float torsoRadius = 0.2f;
        public float floorY = 0f;
        [Tooltip("How much higher his eyes are than yours. A boxing stance lowers your head, so a little extra makes him look your height.")]
        public float heightOffset = 0.1f;
        public float upperArm = 0.34f, foreArm = 0.36f, thigh = 0.47f, shin = 0.47f;

        [Header("Colours")]
        public Color gloveColor = new Color(0.75f, 0.06f, 0.06f);
        public Color telegraphColor = new Color(1f, 0.82f, 0.1f);
        public Color skinColor = new Color(0.80f, 0.58f, 0.44f);
        public Color openColor = new Color(0.45f, 0.65f, 1f);

        public event Action<PunchResult> PunchResolved;
        public event Action<LandedHit> PlayerLanded;

        public Difficulty CurrentDifficulty => diff;
        public bool IsOpen => diff.playerCanPunch && Time.time < openUntil;

        Vector3 facing = Vector3.forward;
        float headHeight = 1.7f;
        Vector3 basePos;
        Difficulty diff = Difficulty.For(Level.Easy, 1, 0f);
        Coroutine attackLoop;
        readonly Vector3[] glovePos = new Vector3[2];
        readonly bool[] busy = new bool[2];
        readonly float[] landCooldown = new float[2];
        readonly Vector3[] guardOverride = new Vector3[2];
        readonly float[] overrideUntil = new float[2];
        readonly bool[] defenceRolled = new bool[2];
        float openUntil, headFlashUntil;
        float lunge, lungeTarget;
        float twist, twistTarget;        // degrees his body turns into a punch (real model only)
        float drive, driveTarget;        // metres his head leans forward behind a punch
        Vector3 flinch, lean, leanTarget;
        float leanUntil;
        Vector3 headBaseLocal;
        Renderer[] shown;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (head) headBaseLocal = head.localPosition;
            // remember which renderers are meant to be seen (hidden hit spheres stay hidden)
            var all = GetComponentsInChildren<Renderer>(true);
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in all) if (r.enabled) list.Add(r);
            shown = list.ToArray();
            basePos = transform.position;
            glovePos[0] = GuardPos(0);
            glovePos[1] = GuardPos(1);
            SetGloveColor(0, gloveColor);
            SetGloveColor(1, gloveColor);
        }

        // ---------------------------------------------------------------- control

        /// <summary>Stand in front of where the player is looking, at about their height.</summary>
        public void Recenter(Vector3 playerFacing, float playerHeadHeight)
        {
            facing = playerFacing.sqrMagnitude > 1e-4f ? playerFacing.normalized : Vector3.forward;
            headHeight = playerHeadHeight + heightOffset;
            if (rig)
            {
                Vector3 h = rig.HeadPos;
                basePos = new Vector3(h.x, headHeight, h.z) + facing * standDistance;
                transform.SetPositionAndRotation(basePos, Quaternion.LookRotation(-facing, Vector3.up));
            }
            glovePos[0] = GuardPos(0);
            glovePos[1] = GuardPos(1);
            if (puppet) puppet.FitHeight(headHeight - floorY - 0.1f);
        }

        public void SetDifficulty(Difficulty d) => diff = d;

        public void StartAttacking(Difficulty d)
        {
            StopAttacking();
            diff = d;
            attackLoop = StartCoroutine(AttackLoop());
        }

        public void StopAttacking()
        {
            StopAllCoroutines();
            attackLoop = null;
            busy[0] = busy[1] = false;
            openUntil = 0f;
            lungeTarget = 0f;
            twistTarget = 0f;
            driveTarget = 0f;
            SetGloveColor(0, gloveColor);
            SetGloveColor(1, gloveColor);
        }

        public void SetVisible(bool visible)
        {
            if (shown == null) return;
            foreach (var r in shown) if (r) r.enabled = visible;
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            float dt = Time.deltaTime;
            Follow(dt);

            for (int g = 0; g < 2; g++)
            {
                if (busy[g]) continue;
                float rate = Time.time < overrideUntil[g] ? 24f : 14f;
                glovePos[g] = Vector3.Lerp(glovePos[g], GuardPos(g), 1f - Mathf.Exp(-rate * dt));
            }

            if (diff.playerCanPunch) DetectPlayerPunches();

            flinch = Vector3.Lerp(flinch, Vector3.zero, 1f - Mathf.Exp(-10f * dt));
            leanTarget = Time.time < leanUntil ? leanTarget : Vector3.zero;
            lean = Vector3.Lerp(lean, leanTarget, 1f - Mathf.Exp(-18f * dt));
            lunge = Mathf.MoveTowards(lunge, lungeTarget, dt * 2.5f);
            twist = Mathf.Lerp(twist, twistTarget, 1f - Mathf.Exp(-22f * dt));
            drive = Mathf.Lerp(drive, driveTarget, 1f - Mathf.Exp(-22f * dt));

            Color hc = Time.time < headFlashUntil ? Color.white
                     : IsOpen ? Color.Lerp(skinColor, openColor, 0.75f) : skinColor;
            SetColor(headRenderer, hc);
            if (tintRenderers != null && tintRenderers.Length > 0)
            {
                Color tc = Time.time < headFlashUntil ? new Color(1f, 0.6f, 0.6f)
                         : IsOpen ? Color.Lerp(Color.white, openColor, 0.6f) : Color.white;
                foreach (var r in tintRenderers) SetColor(r, tc);
            }

            // hands of the real model follow the gloves (IK runs after Update)
            for (int g = 0; g < 2; g++)
            {
                var w = g == 0 ? wristL : wristR;
                if (!w) continue;
                Vector3 dir = (glovePos[g] - Shoulder(g)).normalized;
                w.position = glovePos[g] - dir * 0.07f;
            }
            if (puppet)
            {
                puppet.headOffset = flinch + lean + transform.forward * drive;
                puppet.twist = twist;
                // his animated head bone goes under the head hit sphere (about 9 cm below its centre)
                Vector3 p = transform.position - puppet.HeadOffsetFlat;
                puppet.transform.position = new Vector3(p.x, floorY, p.z);
            }
        }

        void Follow(float dt)
        {
            if (!rig) return;
            Vector3 h = rig.HeadPos;
            // keep the distance, but drift slowly so a slip or duck is not copied instantly
            Vector3 target = new Vector3(h.x, headHeight, h.z) + facing * standDistance;
            basePos = Vector3.Lerp(basePos, target, 1f - Mathf.Exp(-2.5f * dt));

            Vector3 look = h - basePos;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look, Vector3.up), 1f - Mathf.Exp(-6f * dt));
            // stepping in behind the punch
            transform.position = basePos + transform.forward * lunge;
        }

        void LateUpdate()
        {
            if (head) head.position = transform.TransformPoint(headBaseLocal) + flinch + lean;
            for (int g = 0; g < 2; g++)
            {
                var gt = g == 0 ? gloveL : gloveR;
                if (!gt) continue;
                gt.position = glovePos[g];
                Vector3 dir = glovePos[g] - Shoulder(g);
                if (dir.sqrMagnitude > 1e-4f) gt.rotation = Quaternion.LookRotation(dir, transform.up);
            }

            for (int g = 0; g < 2; g++)
            {
                float side = Side(g);
                Vector3 shoulder = Shoulder(g);
                Vector3 pole = shoulder + transform.TransformDirection(new Vector3(side * 0.3f, -0.4f, -0.15f));
                Vector3 elbow = DefenceMath.Elbow(shoulder, glovePos[g], upperArm, foreArm, pole);
                Limbs.Stretch(g == 0 ? upperArmL : upperArmR, shoulder, elbow, 0.095f);
                Limbs.Stretch(g == 0 ? foreArmL : foreArmR, elbow, glovePos[g], 0.08f);

                Vector3 hip = Hip(g);
                Vector3 foot = Foot(g);
                Vector3 knee = DefenceMath.Elbow(hip, foot, thigh, shin, hip + transform.forward * 0.6f);
                Limbs.Stretch(g == 0 ? thighL : thighR, hip, knee, 0.15f);
                Limbs.Stretch(g == 0 ? shinL : shinR, knee, foot, 0.12f);
                var f = g == 0 ? footL : footR;
                if (f)
                {
                    f.position = foot + transform.forward * 0.05f + Vector3.down * 0.02f;
                    Vector3 fwd = transform.forward; fwd.y = 0f;
                    if (fwd.sqrMagnitude > 1e-4f) f.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                }
            }
        }

        // ---------------------------------------------------------------- body points

        static float Side(int g) => g == 0 ? -1f : 1f;   // 0 = his left glove

        Vector3 GuardPos(int g)
        {
            if (Time.time < overrideUntil[g]) return guardOverride[g];
            // open after you defended: hands drop and the head is exposed (Pro)
            return IsOpen
                ? transform.TransformPoint(new Vector3(Side(g) * 0.22f, -0.4f, 0.14f))
                : transform.TransformPoint(new Vector3(Side(g) * 0.13f, -0.1f, 0.22f));
        }

        Vector3 Shoulder(int g) => transform.TransformPoint(new Vector3(Side(g) * 0.2f, -0.3f, 0f));
        Vector3 Hip(int g) => transform.TransformPoint(new Vector3(Side(g) * 0.11f, -0.9f, 0f));

        Vector3 Foot(int g)
        {
            // orthodox stance: left foot forward, right foot back
            Vector3 f = Hip(g) + transform.forward * (g == 0 ? 0.14f : -0.14f) + transform.right * Side(g) * 0.07f;
            return new Vector3(f.x, floorY + 0.06f, f.z);
        }

        void SetColor(Renderer r, Color c)
        {
            if (!r) return;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }

        void SetGloveColor(int g, Color c)
        {
            SetColor(g == 0 ? gloveLRenderer : gloveRRenderer, c);
            var arr = g == 0 ? gloveLRenderers : gloveRRenderers;
            if (arr != null) foreach (var r in arr) SetColor(r, c);
        }

        // ---------------------------------------------------------------- attacking

        IEnumerator AttackLoop()
        {
            yield return new WaitForSeconds(0.8f);
            while (true)
            {
                yield return new WaitForSeconds(UnityEngine.Random.Range(diff.minGap, diff.maxGap));

                if (UnityEngine.Random.value < diff.feintChance)
                {
                    // fake it, then often throw the real one while you are still reacting
                    yield return StartCoroutine(Punch(PickFirst(), 1f, feint: true));
                    if (UnityEngine.Random.value < 0.6f)
                        yield return StartCoroutine(Punch(PickFirst(), 0.5f, feint: false));
                    continue;
                }

                int count = UnityEngine.Random.value < diff.comboChance
                    ? UnityEngine.Random.Range(diff.minCombo, diff.maxCombo + 1)
                    : 1;
                bool lastLeft = UnityEngine.Random.value < 0.5f;
                for (int i = 0; i < count; i++)
                {
                    PunchKind kind = i == 0 ? PickFirst() : PickNext(lastLeft);
                    lastLeft = kind == PunchKind.Jab || kind == PunchKind.LeftHook;
                    // follow-up punches in a combination come with less warning
                    yield return StartCoroutine(Punch(kind, i == 0 ? 1f : 0.6f, feint: false));
                    yield return new WaitForSeconds(0.05f);
                }
            }
        }

        static PunchKind PickFirst()
        {
            float r = UnityEngine.Random.value;
            if (r < 0.40f) return PunchKind.Jab;
            if (r < 0.70f) return PunchKind.Cross;
            if (r < 0.85f) return PunchKind.LeftHook;
            return PunchKind.RightHook;
        }

        static PunchKind PickNext(bool lastLeft)
        {
            bool hook = UnityEngine.Random.value < 0.4f;
            if (lastLeft) return hook ? PunchKind.RightHook : PunchKind.Cross;
            return hook ? PunchKind.LeftHook : PunchKind.Jab;
        }

        IEnumerator Punch(PunchKind kind, float windupScale, bool feint)
        {
            int g = kind == PunchKind.Jab || kind == PunchKind.LeftHook ? 0 : 1;
            float side = Side(g);
            bool hook = kind == PunchKind.LeftHook || kind == PunchKind.RightHook;
            float windup = diff.windup * windupScale;
            float strike = diff.strike * (hook ? 1.15f : 1f);

            busy[g] = true;
            float t0 = Time.time;
            Vector3 headStart = rig.HeadPos;
            float reaction = -1f;

            // 1. wind-up: the glove pulls back and glows. A feint looks exactly the same, then stops.
            float windupTime = feint ? windup * UnityEngine.Random.Range(0.5f, 0.75f) : windup;
            Vector3 from = glovePos[g];
            for (float t = 0f; t < windupTime; t += Time.deltaTime)
            {
                float k = t / windup;
                Vector3 back = transform.TransformPoint(hook
                    ? new Vector3(side * 0.36f, -0.12f, 0.06f)
                    : new Vector3(side * 0.15f, -0.13f, -0.02f));
                glovePos[g] = Vector3.Lerp(from, back, DefenceMath.Smooth(k));
                SetGloveColor(g, Color.Lerp(gloveColor, telegraphColor, k));
                if (feint) lungeTarget = 0.06f * k;          // a little shoulder dip sells it
                twistTarget = side * (hook ? 14f : 9f) * k;   // load: the punching shoulder turns back
                if (reaction < 0f && (rig.HeadPos - headStart).magnitude > DefenceMath.MinMove) reaction = Time.time - t0;
                yield return null;
            }

            if (feint)
            {
                // retract, and keep watching for a late flinch
                lungeTarget = 0f;
                twistTarget = 0f;
                Vector3 rf = glovePos[g];
                const float back = 0.28f;
                for (float t = 0f; t < back; t += Time.deltaTime)
                {
                    float k = DefenceMath.Smooth(t / back);
                    glovePos[g] = Vector3.Lerp(rf, GuardPos(g), k);
                    SetGloveColor(g, Color.Lerp(telegraphColor, gloveColor, k));
                    if (reaction < 0f && (rig.HeadPos - headStart).magnitude > DefenceMath.MinMove) reaction = Time.time - t0;
                    yield return null;
                }
                SetGloveColor(g, gloveColor);
                busy[g] = false;
                PunchResolved?.Invoke(new PunchResult
                {
                    kind = kind,
                    outcome = reaction >= 0f ? Outcome.FeintBit : Outcome.FeintRead,
                    windup = windupTime,
                    strike = 0f,
                    reaction = reaction,
                    leftHand = g == 0,
                    at = glovePos[g],
                });
                yield break;
            }
            SetGloveColor(g, telegraphColor);

            // 2. strike: aimed at where the head is NOW, so standing still gets you hit
            lungeTarget = 0.16f;
            // shoulders and hips turn the punching side forward; more for a cross or hook than a jab
            twistTarget = -side * (hook ? 34f : kind == PunchKind.Jab ? 14f : 26f);
            driveTarget = kind == PunchKind.Jab ? 0.04f : 0.08f;
            Vector3 start = glovePos[g];
            Vector3 shoulder = Shoulder(g);
            Vector3 aim = rig.HeadPos;
            Vector3 dir = (aim - shoulder).normalized;
            Vector3 end = DefenceMath.ClampReach(shoulder, aim + dir * 0.12f, reach);
            Vector3 control = hook
                ? (start + end) * 0.5f + transform.right * side * 0.45f
                : (start + end) * 0.5f;

            Outcome outcome = Outcome.Missed;
            bool decided = false;
            for (float t = 0f; ; t += Time.deltaTime)
            {
                float k = Mathf.Min(1f, t / strike);
                glovePos[g] = DefenceMath.Bezier(start, control, end, k);
                if (reaction < 0f && (rig.HeadPos - headStart).magnitude > DefenceMath.MinMove) reaction = Time.time - t0;

                if (DefenceMath.SpheresTouch(glovePos[g], gloveRadius, rig.LeftGlovePos, rig.gloveRadius) ||
                    DefenceMath.SpheresTouch(glovePos[g], gloveRadius, rig.RightGlovePos, rig.gloveRadius))
                {
                    outcome = Outcome.Blocked; decided = true;
                }
                else if (DefenceMath.SpheresTouch(glovePos[g], gloveRadius, rig.HeadPos, rig.headRadius))
                {
                    outcome = Outcome.Hit; decided = true;
                }
                if (decided || k >= 1f) break;
                yield return null;
            }

            if (!decided)
            {
                // transform.forward points from him to the player
                Vector3 away = transform.forward; away.y = 0f; away.Normalize();
                Vector3 playerRight = -transform.right; playerRight.y = 0f; playerRight.Normalize();
                outcome = DefenceMath.ClassifyEvade(rig.HeadPos - headStart, playerRight, Vector3.up, away);
            }

            if (outcome != Outcome.Hit) openUntil = Time.time + 0.9f;

            PunchResolved?.Invoke(new PunchResult
            {
                kind = kind,
                outcome = outcome,
                windup = windup,
                strike = strike,
                reaction = reaction,
                leftHand = g == 0,
                at = glovePos[g],
            });

            // 3. retract to guard
            lungeTarget = 0f;
            twistTarget = 0f;
            driveTarget = 0f;
            Vector3 retractFrom = glovePos[g];
            const float retract = 0.22f;
            for (float t = 0f; t < retract; t += Time.deltaTime)
            {
                float k = DefenceMath.Smooth(t / retract);
                glovePos[g] = Vector3.Lerp(retractFrom, GuardPos(g), k);
                SetGloveColor(g, Color.Lerp(telegraphColor, gloveColor, k));
                yield return null;
            }
            SetGloveColor(g, gloveColor);
            busy[g] = false;
        }

        // ---------------------------------------------------------------- the player's punches (Pro)

        void DetectPlayerPunches()
        {
            if (!rig || !head) return;
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                Vector3 p = left ? rig.LeftGlovePos : rig.RightGlovePos;
                Vector3 v = left ? rig.LeftVelocity : rig.RightVelocity;
                float speed = v.magnitude;

                ActiveDefence(i, p, v, speed);

                if (Time.time < landCooldown[i]) continue;
                if (speed < 1.5f) continue;                            // a touch is not a punch
                if (Vector3.Dot(v, head.position - p) <= 0f) continue;  // must be moving towards him

                // his gloves catch it first
                bool padded = false;
                for (int g = 0; g < 2; g++)
                    if (!busy[g] && DefenceMath.SpheresTouch(p, rig.gloveRadius, glovePos[g], gloveRadius)) padded = true;
                if (padded)
                {
                    landCooldown[i] = Time.time + 0.35f;
                    PlayerLanded?.Invoke(new LandedHit { leftHand = left, speed = speed, guarded = true, at = p });
                    continue;
                }

                bool headHit = DefenceMath.SpheresTouch(p, rig.gloveRadius, head.position, headRadius);
                bool bodyHit = !headHit && torso && DefenceMath.SpheresTouch(p, rig.gloveRadius, torso.position, torsoRadius);
                if (!headHit && !bodyHit) continue;

                landCooldown[i] = Time.time + 0.4f;
                if (headHit)
                {
                    flinch += v.normalized * 0.07f;
                    headFlashUntil = Time.time + 0.12f;
                }
                PlayerLanded?.Invoke(new LandedHit { leftHand = left, head = headHit, speed = speed, counter = IsOpen, at = p });
            }
        }

        /// <summary>
        /// Once per incoming punch, he may block it with a glove or slip his head,
        /// unless he is busy punching or open after a miss.
        /// </summary>
        void ActiveDefence(int i, Vector3 p, Vector3 v, float speed)
        {
            if (speed < 0.9f) { defenceRolled[i] = false; return; }
            if (defenceRolled[i] || IsOpen) return;
            Vector3 toHead = head.position - p;
            if (toHead.magnitude > 0.6f || Vector3.Dot(v, toHead) <= 0f) return;
            defenceRolled[i] = true;
            if (UnityEngine.Random.value > diff.activeDefence) return;

            if (UnityEngine.Random.value < 0.6f)
            {
                int g = -1;
                float best = float.MaxValue;
                for (int k = 0; k < 2; k++)
                {
                    if (busy[k]) continue;
                    float d = (glovePos[k] - p).sqrMagnitude;
                    if (d < best) { best = d; g = k; }
                }
                if (g < 0) return;
                guardOverride[g] = head.position - toHead.normalized * 0.17f;
                overrideUntil[g] = Time.time + 0.45f;
            }
            else
            {
                Vector3 sideways = Vector3.Cross(Vector3.up, toHead).normalized * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                leanTarget = sideways * 0.17f + Vector3.down * 0.04f;
                leanUntil = Time.time + 0.5f;
            }
        }
    }
}
