using UnityEditor;
using UnityEngine;

namespace BoxDefence.EditorTools
{
    /// <summary>BoxDefence > Run Self-Check: tests the scoring rules without a headset.</summary>
    public static class BoxDefenceSelfCheck
    {
        static int pass, fail;

        [MenuItem("BoxDefence/Run Self-Check", priority = 10)]
        public static void Run()
        {
            pass = fail = 0;
            Vector3 right = Vector3.right, up = Vector3.up, away = Vector3.back; // opponent in front (+Z)

            Debug.Log("=== Box Defence self-check ===");
            Check("Head 12 cm to the right = slip right", DefenceMath.ClassifyEvade(new Vector3(0.12f, 0, 0), right, up, away) == Outcome.SlipRight);
            Check("Head 12 cm to the left = slip left", DefenceMath.ClassifyEvade(new Vector3(-0.12f, 0, 0), right, up, away) == Outcome.SlipLeft);
            Check("Head 15 cm down = duck", DefenceMath.ClassifyEvade(new Vector3(0.05f, -0.15f, 0), right, up, away) == Outcome.Duck);
            Check("Head 20 cm back = pull back", DefenceMath.ClassifyEvade(new Vector3(0.03f, 0, -0.2f), right, up, away) == Outcome.PullBack);
            Check("Head moved 2 cm = no evasion", DefenceMath.ClassifyEvade(new Vector3(0.02f, 0, 0), right, up, away) == Outcome.Missed);
            Check("Moving towards the punch is not a pull back", DefenceMath.ClassifyEvade(new Vector3(0, 0, 0.2f), right, up, away) == Outcome.Missed);

            Check("Spheres 0.15 m apart, radii 0.08 + 0.08, touch", DefenceMath.SpheresTouch(Vector3.zero, 0.08f, new Vector3(0.15f, 0, 0), 0.08f));
            Check("Spheres 0.20 m apart, radii 0.08 + 0.08, do not touch", !DefenceMath.SpheresTouch(Vector3.zero, 0.08f, new Vector3(0.2f, 0, 0), 0.08f));

            Vector3 a = Vector3.zero, c = new Vector3(0.5f, 0, 0.5f), b = new Vector3(0, 0, 1);
            Check("Hook path starts at the glove", (DefenceMath.Bezier(a, c, b, 0f) - a).magnitude < 1e-5f);
            Check("Hook path ends at the target", (DefenceMath.Bezier(a, c, b, 1f) - b).magnitude < 1e-5f);
            Check("Hook path bends to the side", DefenceMath.Bezier(a, c, b, 0.5f).x > 0.2f);

            Check("Punch cannot outreach the arm", Mathf.Abs((DefenceMath.ClampReach(Vector3.zero, new Vector3(0, 0, 2f), 1f)).magnitude - 1f) < 1e-5f);
            Check("Target within reach is unchanged", (DefenceMath.ClampReach(Vector3.zero, new Vector3(0, 0, 0.5f), 1f) - new Vector3(0, 0, 0.5f)).magnitude < 1e-5f);

            Vector3 head = new Vector3(0, 1.6f, 0);
            Check("Gloves by the chin = guard up", DefenceMath.GuardUp(head, head + new Vector3(-0.1f, -0.12f, 0.15f), head + new Vector3(0.1f, -0.12f, 0.15f)));
            Check("One glove at the hip = guard down", !DefenceMath.GuardUp(head, head + new Vector3(-0.1f, -0.12f, 0.15f), head + new Vector3(0.25f, -0.7f, 0.1f)));

            var easy = Difficulty.For(Level.Easy, 1, 0f);
            var medium = Difficulty.For(Level.Medium, 1, 0f);
            var hard = Difficulty.For(Level.Hard, 1, 0f);
            var pro = Difficulty.For(Level.Pro, 1, 0f);
            Check("Each level gives less warning than the one before", easy.windup > medium.windup && medium.windup > hard.windup);
            Check("Each level punches faster than the one before", easy.strike > medium.strike && medium.strike > hard.strike);
            Check("Easy: combinations of at most 2", easy.maxCombo <= 2);
            Check("Medium: combinations of up to 5", medium.maxCombo == 5);
            Check("Only Hard and Pro feint", easy.feintChance == 0f && medium.feintChance == 0f && hard.feintChance > 0f && pro.feintChance > 0f);
            Check("Only Pro lets you punch", !easy.playerCanPunch && !medium.playerCanPunch && !hard.playerCanPunch && pro.playerCanPunch);
            Check("Later rounds are faster", Difficulty.For(Level.Medium, 3, 0f).windup < medium.windup);
            Check("Struggling slows him down", Difficulty.For(Level.Medium, 1, -0.5f).windup > medium.windup);

            Vector3 sh = Vector3.zero, hand = new Vector3(0f, -0.2f, 0.4f), pole = new Vector3(0f, -1f, 0f);
            Vector3 el = DefenceMath.Elbow(sh, hand, 0.3f, 0.28f, pole);
            Check("Elbow keeps the upper-arm length", Mathf.Abs((el - sh).magnitude - 0.3f) < 1e-3f);
            Check("Elbow keeps the forearm length", Mathf.Abs((hand - el).magnitude - 0.28f) < 1e-3f);
            Check("Elbow bends towards the pole (down)", el.y < Mathf.Lerp(sh.y, hand.y, 0.5f));
            Vector3 far = DefenceMath.Elbow(sh, new Vector3(0, 0, 2f), 0.3f, 0.28f, pole);
            Check("Out of reach: the arm is straight", Mathf.Abs(far.y) < 1e-4f && far.z > 0f);

            var s = new RoundSummary { thrown = 10, hits = 5, slipsLeft = 4, slipsRight = 0, guardPct = 40f };
            var notes = DefenceMath.Insights(s);
            var f = new RoundSummary { thrown = 10, feints = 4, feintsBit = 3, guardPct = 80f };
            Check("Coach notices biting on feints", DefenceMath.Insights(f).Exists(n => n.Contains("3 of 4 feints")));
            Check("Coach notices taking half the punches", notes.Exists(n => n.Contains("5 of 10")));
            Check("Coach notices always slipping one way", notes.Exists(n => n.Contains("went left")));
            Check("Coach notices a low guard", notes.Exists(n => n.Contains("guard was up 40%")));

            Debug.Log($"=== {pass} passed, {fail} failed ===");
        }

        static void Check(string name, bool ok)
        {
            if (ok) { pass++; Debug.Log($"  PASS  {name}"); }
            else { fail++; Debug.LogError($"  FAIL  {name}"); }
        }
    }
}
