using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoxDefence
{
    public enum PunchKind { Jab, Cross, LeftHook, RightHook }

    /// <summary>What happened to one of the opponent's punches.</summary>
    public enum Outcome { Hit, Blocked, SlipLeft, SlipRight, Duck, PullBack, Missed, FeintBit, FeintRead }

    /// <summary>
    /// The geometry and scoring rules, kept free of MonoBehaviours so they can be
    /// checked without a headset (BoxDefence > Run Self-Check).
    /// Directions are in the PLAYER's frame: "left" is the player's left.
    /// </summary>
    public static class DefenceMath
    {
        /// <summary>Head movement smaller than this does not count as a reaction.</summary>
        public const float MinMove = 0.05f;

        public static bool SpheresTouch(Vector3 a, float ra, Vector3 b, float rb)
        {
            float r = ra + rb;
            return (a - b).sqrMagnitude <= r * r;
        }

        /// <summary>Quadratic Bezier, used for the curved path of a hook.</summary>
        public static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * control + t * t * b;
        }

        /// <summary>Ease in/out, so wind-ups and retractions look less robotic.</summary>
        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>A fist cannot travel further than the arm is long.</summary>
        public static Vector3 ClampReach(Vector3 shoulder, Vector3 target, float reach)
        {
            Vector3 d = target - shoulder;
            return d.magnitude <= reach ? target : shoulder + d.normalized * reach;
        }

        /// <summary>
        /// Names the evasion from how the head moved between the start of the wind-up
        /// and the end of the punch. The largest of the three movements wins.
        /// </summary>
        /// <param name="displacement">head position at the end minus at the start</param>
        /// <param name="playerRight">the player's right, horizontal</param>
        /// <param name="awayFromOpponent">horizontal direction from the opponent to the player</param>
        public static Outcome ClassifyEvade(Vector3 displacement, Vector3 playerRight, Vector3 up, Vector3 awayFromOpponent)
        {
            float lateral = Vector3.Dot(displacement, playerRight);
            float down = -Vector3.Dot(displacement, up);
            float back = Vector3.Dot(displacement, awayFromOpponent);
            float side = Mathf.Abs(lateral);

            if (Mathf.Max(side, Mathf.Max(down, back)) < MinMove) return Outcome.Missed;
            if (down >= side && down >= back) return Outcome.Duck;
            if (back >= side) return Outcome.PullBack;
            return lateral > 0f ? Outcome.SlipRight : Outcome.SlipLeft;
        }

        public static bool IsFeint(Outcome o) => o == Outcome.FeintBit || o == Outcome.FeintRead;

        public static string Label(Level l) => l switch
        {
            Level.Easy => "EASY",
            Level.Medium => "MEDIUM",
            Level.Hard => "HARD",
            _ => "PRO",
        };

        /// <summary>
        /// Two-bone IK: where the elbow (or knee) goes so that both bones keep their
        /// length, bending towards the pole. If the target is out of reach the limb is straight.
        /// </summary>
        public static Vector3 Elbow(Vector3 root, Vector3 end, float upper, float lower, Vector3 pole)
        {
            Vector3 d = end - root;
            float len = d.magnitude;
            if (len < 1e-4f) return root + Vector3.down * upper;
            Vector3 dir = d / len;
            if (len >= upper + lower) return root + dir * (len * upper / (upper + lower));
            len = Mathf.Max(len, Mathf.Abs(upper - lower) + 1e-4f);
            float a = (upper * upper - lower * lower + len * len) / (2f * len);
            float h = Mathf.Sqrt(Mathf.Max(0f, upper * upper - a * a));
            Vector3 bend = Vector3.ProjectOnPlane(pole - root, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir);
            return root + dir * a + bend.normalized * h;
        }

        public static bool IsEvade(Outcome o) =>
            o == Outcome.SlipLeft || o == Outcome.SlipRight || o == Outcome.Duck || o == Outcome.PullBack;

        /// <summary>
        /// Guard is "up" when both gloves are close to the face and not dropped below the chin.
        /// </summary>
        public static bool GuardUp(Vector3 head, Vector3 leftGlove, Vector3 rightGlove,
                                   float maxDistance = 0.32f, float maxDrop = 0.28f)
        {
            return GloveUp(head, leftGlove, maxDistance, maxDrop) && GloveUp(head, rightGlove, maxDistance, maxDrop);
        }

        static bool GloveUp(Vector3 head, Vector3 glove, float maxDistance, float maxDrop) =>
            (glove - head).magnitude <= maxDistance && glove.y >= head.y - maxDrop;

        public static string Label(Outcome o) => o switch
        {
            Outcome.Hit => "HIT",
            Outcome.Blocked => "BLOCKED",
            Outcome.SlipLeft => "SLIP LEFT",
            Outcome.SlipRight => "SLIP RIGHT",
            Outcome.Duck => "DUCK",
            Outcome.PullBack => "PULL BACK",
            Outcome.FeintBit => "BIT ON FEINT",
            Outcome.FeintRead => "READ FEINT",
            _ => "MISSED",
        };

        public static string Label(PunchKind k) => k switch
        {
            PunchKind.Jab => "jab",
            PunchKind.Cross => "cross",
            PunchKind.LeftHook => "left hook",
            _ => "right hook",
        };

        /// <summary>Plain-language coaching notes from a round's numbers.</summary>
        public static List<string> Insights(RoundSummary s)
        {
            var notes = new List<string>();
            int evades = s.slipsLeft + s.slipsRight + s.ducks + s.pullBacks;
            int slips = s.slipsLeft + s.slipsRight;

            if (s.thrown > 0 && s.hits >= Math.Max(2, s.thrown * 0.4f))
                notes.Add($"You took {s.hits} of {s.thrown} punches. Start moving on the wind-up (when the glove turns yellow), not on the punch.");
            if (slips >= 4)
            {
                int bigger = Math.Max(s.slipsLeft, s.slipsRight);
                float share = (float)bigger / slips;
                if (share >= 0.75f)
                    notes.Add($"{Mathf.RoundToInt(share * 100)}% of your slips went {(s.slipsLeft > s.slipsRight ? "left" : "right")}. Mix it up, or a real opponent will read it.");
            }
            if (s.pullBacks > evades / 2 && s.pullBacks >= 3)
                notes.Add("You mostly pulled back. It works, but it takes you out of range to counter. Try slipping.");
            if (s.feints >= 3 && s.feintsBit * 2 >= s.feints)
                notes.Add($"You bit on {s.feintsBit} of {s.feints} feints. A glowing glove is not a punch yet: react when it comes forward.");
            if (s.guardPct < 60f)
                notes.Add($"Your guard was up {s.guardPct:0}% of the round. Bring your hands back to your face after every punch.");
            if (s.level == "PRO" && evades + s.blocks >= 4 && s.counters == 0)
                notes.Add("You defended well but never countered. Punch back in the gap right after a slip or block.");
            if (s.avgReactionMs > 0f && s.avgReactionMs > 550f)
                notes.Add($"Average reaction {s.avgReactionMs:0} ms. Watch the shoulders and gloves, not the face.");
            if (notes.Count == 0)
                notes.Add("Clean round. Next round will be faster.");
            return notes;
        }
    }
}
