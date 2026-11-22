using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace BoxDefence
{
    /// <summary>
    /// The training session: level choice, countdown, timed rounds with rests,
    /// stats after each round, adaptive speed, and saving every punch to JSON.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class TrainerSession : MonoBehaviour
    {
        public PlayerRig rig;
        public Opponent opponent;
        public Hud hud;
        public Feedback feedback;
        public LevelMenu levelMenu;

        [Header("Session")]
        public int rounds = 3;
        public float roundSeconds = 60f;
        public float restSeconds = 20f;
        public float countdownSeconds = 3f;

        enum State { Boot, Menu, Countdown, Round, Rest, Done }
        State state;
        float stateEnd;
        int round;                       // 1-based
        Level level = Level.Easy;
        Vector3 facing = Vector3.forward;

        SessionRecord session;
        float roundStart, roundTime, guardTime;
        float adapt;                     // -0.5 .. +1.0
        readonly List<Outcome> recent = new List<Outcome>();
        string restSummaryText = "";
        string savedPath = "";
        float hudRefresh;

        const string Yellow = "#FFD21A", Blue = "#4A9BFF", Green = "#3AD35A", Red = "#F03A3A", Grey = "#9A9A9A", Purple = "#B266FF";

        void OnEnable()
        {
            if (!opponent) return;
            opponent.PunchResolved += OnPunch;
            opponent.PlayerLanded += OnLanded;
        }

        void OnDisable()
        {
            if (!opponent) return;
            opponent.PunchResolved -= OnPunch;
            opponent.PlayerLanded -= OnLanded;
        }

        void Start()
        {
            if (levelMenu) levelMenu.Hide();
            // give tracking a moment so the menu appears in front of the real head
            Invoke(nameof(EnterMenu), 0.5f);
        }

        void Update()
        {
            bool pressed = rig && rig.StartPressed();   // read every frame for clean edge detection
            switch (state)
            {
                case State.Boot:
                    break;

                case State.Menu:
                    if (levelMenu && levelMenu.TryGetChoice(out Level chosen)) StartSession(chosen);
                    else if (pressed) StartSession(levelMenu ? levelMenu.Current : level);
                    break;

                case State.Countdown:
                {
                    float left = stateEnd - Time.time;
                    hud.ShowTop($"<size=55%>{DefenceMath.Label(level)} · ROUND {round}/{rounds}</size>\n{Mathf.CeilToInt(Mathf.Max(left, 0.01f))}");
                    if (left <= 0f) BeginRound();
                    break;
                }

                case State.Round:
                {
                    roundTime += Time.deltaTime;
                    if (DefenceMath.GuardUp(rig.HeadPos, rig.LeftGlovePos, rig.RightGlovePos)) guardTime += Time.deltaTime;
                    if (Time.time >= hudRefresh)
                    {
                        hudRefresh = Time.time + 0.25f;
                        float left = Mathf.Max(0f, stateEnd - Time.time);
                        var s = Summarize(round);
                        string landed = level == Level.Pro ? $"  landed {s.landed}" : "";
                        hud.ShowTop(
                            $"{DefenceMath.Label(level)} · R{round}/{rounds}   {(int)left / 60}:{(int)left % 60:00}\n" +
                            $"<size=60%><color={Green}>evade {Evades(s)}</color>  <color={Blue}>block {s.blocks}</color>  " +
                            $"<color={Red}>hit {s.hits}</color>{landed}</size>");
                    }
                    if (Time.time >= stateEnd) EndRound();
                    break;
                }

                case State.Rest:
                {
                    float left = Mathf.Max(0f, stateEnd - Time.time);
                    hud.SetPanelText(restSummaryText + $"\n\n<size=80%><color={Yellow}>Next round in {Mathf.CeilToInt(left)} s. Press A to start now.</color></size>");
                    if (pressed || left <= 0f) StartCountdown();
                    break;
                }

                case State.Done:
                    if (pressed) EnterMenu();
                    break;
            }
        }

        // ---------------------------------------------------------------- flow

        [Header("Ring")]
        public bool centreInRing = true;
        public Vector3 ringCentre = new Vector3(0f, 0f, 0.5f);

        /// <summary>You and him either side of the ring's centre, facing along the ring.</summary>
        void PlaceInRing()
        {
            if (!centreInRing || !rig) return;
            float half = opponent ? opponent.standDistance * 0.5f : 0.475f;
            rig.PlaceInRing(ringCentre - Vector3.forward * half, Vector3.forward);
        }

        void EnterMenu()
        {
            state = State.Menu;
            PlaceInRing();
            facing = rig ? rig.Facing : Vector3.forward;
            opponent.StopAttacking();
            opponent.SetVisible(false);
            hud.HideTop();
            hud.ShowPanel(
                "<b>BOX DEFENCE TRAINER</b>\n<size=60%>\n" +
                "Train your defence without taking real punches.\n" +
                $"Watch his gloves: <color={Yellow}>yellow = a punch is coming</color>. Slip, duck, pull back or block.\n\n" +
                $"<color=#33CC59><b>EASY</b></color>  slower punches, single shots and 2-punch combos\n" +
                $"<color=#F2CC26><b>MEDIUM</b></color>  faster, combinations of up to 5\n" +
                $"<color=#F2731A><b>HARD</b></color>  faster still, and he <b>feints</b>: don't bite\n" +
                $"<color=#E62633><b>PRO</b></color>  hard, and you fight back: when his head turns <color={Blue}>blue</color> he's open\n\n" +
                $"<color={Yellow}><b>Punch a pad below to choose your level.</b></color>  {rounds} rounds of {roundSeconds:0} s.</size>",
                facing, 1.8f, 0.25f);
            if (levelMenu) levelMenu.Show(facing);
        }

        void StartSession(Level chosen)
        {
            level = chosen;
            if (levelMenu) levelMenu.Hide();
            PlaceInRing();
            facing = rig.Facing;
            opponent.Recenter(facing, rig.HeadPos.y);
            session = new SessionRecord
            {
                id = DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                utc = DateTime.UtcNow.ToString("o"),
                device = SystemInfo.deviceModel,
                level = DefenceMath.Label(level),
                rounds = rounds,
                roundSeconds = roundSeconds,
            };
            round = 0;
            adapt = 0f;
            savedPath = "";
            feedback.Cheer(0.4f);
            StartCountdown();
        }

        void StartCountdown()
        {
            round++;
            state = State.Countdown;
            stateEnd = Time.time + countdownSeconds;
            hud.HidePanel();
            opponent.StopAttacking();
            opponent.SetVisible(true);
        }

        void BeginRound()
        {
            state = State.Round;
            stateEnd = Time.time + roundSeconds;
            roundStart = Time.time;
            roundTime = guardTime = 0f;
            hudRefresh = 0f;
            recent.Clear();
            feedback.Bell(1);
            opponent.StartAttacking(Difficulty.For(level, round, adapt));
        }

        void EndRound()
        {
            opponent.StopAttacking();
            feedback.Bell(3);
            feedback.Cheer(0.7f);
            var s = Summarize(round);
            session.summaries.RemoveAll(x => x.round == round);
            session.summaries.Add(s);
            Save();

            opponent.SetVisible(false);
            hud.HideTop();
            var punches = session.punches.Where(p => p.round == round).ToList();

            if (round >= rounds)
            {
                state = State.Done;
                hud.ShowPanel(FinalText(Total()), facing, 1.5f);
                hud.ShowBars(session.punches);
            }
            else
            {
                state = State.Rest;
                stateEnd = Time.time + restSeconds;
                restSummaryText = RoundText(s);
                hud.ShowPanel(restSummaryText, facing, 1.5f);
                hud.ShowBars(punches);
            }
        }

        // ---------------------------------------------------------------- events from the opponent

        void OnPunch(PunchResult r)
        {
            if (state != State.Round) return;
            bool feint = DefenceMath.IsFeint(r.outcome);
            session.punches.Add(new PunchRecord
            {
                round = round,
                time = Time.time - roundStart,
                punch = feint ? "feint" : DefenceMath.Label(r.kind),
                outcome = DefenceMath.Label(r.outcome),
                windupMs = r.windup * 1000f,
                strikeMs = r.strike * 1000f,
                reactionMs = r.reaction >= 0f ? r.reaction * 1000f : -1f,
            });

            switch (r.outcome)
            {
                case Outcome.Hit:
                    feedback.TookHit();
                    feedback.Ooh();
                    feedback.Popup($"<color={Red}>HIT</color>\n<size=60%>{DefenceMath.Label(r.kind)}</size>", Color.white);
                    break;
                case Outcome.Blocked:
                    feedback.Blocked(r.at, r.leftHand);
                    feedback.Popup($"<color={Blue}>BLOCKED</color>", Color.white);
                    break;
                case Outcome.Missed:
                    feedback.PunchThrown(r.at);
                    feedback.Popup($"<color={Grey}>MISSED</color>", Color.white, 0.6f);
                    break;
                case Outcome.FeintBit:
                    feedback.Popup($"<color={Purple}>FEINT</color>\n<size=60%>you bit</size>", Color.white);
                    break;
                case Outcome.FeintRead:
                    feedback.Popup($"<color={Purple}>FEINT</color>\n<size=60%>well read</size>", Color.white);
                    break;
                default:
                    feedback.PunchThrown(r.at);
                    string ms = r.reaction >= 0f ? $"\n<size=60%>{r.reaction * 1000f:0} ms</size>" : "";
                    feedback.Popup($"<color={Green}>{DefenceMath.Label(r.outcome)}</color>{ms}", Color.white);
                    break;
            }

            if (!feint) Adapt(r.outcome);
        }

        void OnLanded(LandedHit h)
        {
            if (state != State.Round) return;
            if (h.guarded)
            {
                feedback.Padded(h.at, h.leftHand);
                session.landed.Add(new LandedRecord
                {
                    round = round, time = Time.time - roundStart, hand = h.leftHand ? "left" : "right",
                    target = "guard", speed = h.speed, counter = false,
                });
                return;
            }
            session.landed.Add(new LandedRecord
            {
                round = round,
                time = Time.time - roundStart,
                hand = h.leftHand ? "left" : "right",
                target = h.head ? "head" : "body",
                speed = h.speed,
                counter = h.counter,
            });
            feedback.Landed(h.at, h.leftHand);
            if (h.counter) feedback.Cheer(0.8f);
            feedback.Popup(h.counter
                ? $"<color={Yellow}>COUNTER!</color>\n<size=60%>{h.speed:0.0} m/s</size>"
                : $"{(h.head ? "HEAD" : "BODY")}\n<size=60%>{h.speed:0.0} m/s</size>", Color.white, 0.9f);
        }

        /// <summary>Every six punches: a bit faster if you defend almost everything, slower if you get hit a lot.</summary>
        void Adapt(Outcome o)
        {
            recent.Add(o);
            if (recent.Count < 6) return;
            float defended = recent.Count(x => x != Outcome.Hit) / (float)recent.Count;
            recent.Clear();
            if (defended >= 0.85f) adapt = Mathf.Min(1f, adapt + 0.25f);
            else if (defended <= 0.4f) adapt = Mathf.Max(-0.5f, adapt - 0.25f);
            opponent.SetDifficulty(Difficulty.For(level, round, adapt));
        }

        // ---------------------------------------------------------------- stats

        static int Evades(RoundSummary s) => s.slipsLeft + s.slipsRight + s.ducks + s.pullBacks;

        static bool IsFeintLabel(string o) => o == "BIT ON FEINT" || o == "READ FEINT";

        RoundSummary Summarize(int r)
        {
            var all = session.punches.Where(p => p.round == r).ToList();
            var ps = all.Where(p => !IsFeintLabel(p.outcome)).ToList();
            var ls = session.landed.Where(l => l.round == r && l.target != "guard").ToList();
            var reactions = ps.Where(p => p.outcome != "HIT" && p.reactionMs > 0f).Select(p => p.reactionMs).ToList();
            return new RoundSummary
            {
                round = r,
                level = DefenceMath.Label(level),
                thrown = ps.Count,
                hits = ps.Count(p => p.outcome == "HIT"),
                blocks = ps.Count(p => p.outcome == "BLOCKED"),
                slipsLeft = ps.Count(p => p.outcome == "SLIP LEFT"),
                slipsRight = ps.Count(p => p.outcome == "SLIP RIGHT"),
                ducks = ps.Count(p => p.outcome == "DUCK"),
                pullBacks = ps.Count(p => p.outcome == "PULL BACK"),
                missed = ps.Count(p => p.outcome == "MISSED"),
                feints = all.Count(p => IsFeintLabel(p.outcome)),
                feintsBit = all.Count(p => p.outcome == "BIT ON FEINT"),
                avgReactionMs = reactions.Count > 0 ? reactions.Average() : -1f,
                guardPct = roundTime > 0f ? 100f * guardTime / roundTime : 0f,
                landed = ls.Count,
                counters = ls.Count(l => l.counter),
                guardedByHim = session.landed.Count(l => l.round == r && l.target == "guard"),
                avgSpeed = ls.Count > 0 ? ls.Average(l => l.speed) : 0f,
                maxSpeed = ls.Count > 0 ? ls.Max(l => l.speed) : 0f,
                windupMs = opponent ? opponent.CurrentDifficulty.windup * 1000f : 0f,
            };
        }

        RoundSummary Total()
        {
            var t = new RoundSummary { round = 0, level = DefenceMath.Label(level) };
            var all = session.summaries;
            foreach (var s in all)
            {
                t.thrown += s.thrown; t.hits += s.hits; t.blocks += s.blocks;
                t.slipsLeft += s.slipsLeft; t.slipsRight += s.slipsRight; t.ducks += s.ducks;
                t.pullBacks += s.pullBacks; t.missed += s.missed; t.landed += s.landed; t.counters += s.counters;
                t.feints += s.feints; t.feintsBit += s.feintsBit; t.guardedByHim += s.guardedByHim;
                t.maxSpeed = Mathf.Max(t.maxSpeed, s.maxSpeed);
            }
            var reactions = session.punches.Where(p => !IsFeintLabel(p.outcome) && p.outcome != "HIT" && p.reactionMs > 0f).Select(p => p.reactionMs).ToList();
            t.avgReactionMs = reactions.Count > 0 ? reactions.Average() : -1f;
            t.guardPct = all.Count > 0 ? all.Average(s => s.guardPct) : 0f;
            var landed = session.landed.Where(l => l.target != "guard").ToList();
            t.avgSpeed = landed.Count > 0 ? landed.Average(l => l.speed) : 0f;
            return t;
        }

        string Lines(RoundSummary s)
        {
            string react = s.avgReactionMs > 0f ? $"{s.avgReactionMs:0} ms" : "–";
            var sb = new StringBuilder();
            sb.Append($"Punches at you: <b>{s.thrown}</b>\n");
            sb.Append($"<color={Green}>Evaded {Evades(s)}</color>  (slips L {s.slipsLeft} / R {s.slipsRight}, ducks {s.ducks}, pull-backs {s.pullBacks})\n");
            sb.Append($"<color={Blue}>Blocked {s.blocks}</color>    <color={Red}>Hit {s.hits}</color>\n");
            if (s.feints > 0) sb.Append($"<color={Purple}>Feints {s.feints}: you bit on {s.feintsBit}</color>\n");
            sb.Append($"Average reaction: <b>{react}</b>     Guard up: <b>{s.guardPct:0}%</b>");
            if (level == Level.Pro)
                sb.Append($"\nYour punches landed: <b>{s.landed}</b> ({s.counters} counters), {s.guardedByHim} on his guard, fastest {s.maxSpeed:0.0} m/s");
            return sb.ToString();
        }

        string RoundText(RoundSummary s)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{DefenceMath.Label(level)} · ROUND {s.round} DONE</b>\n<size=60%>\n");
            sb.Append(Lines(s));
            sb.Append("\n\n");
            foreach (var n in DefenceMath.Insights(s)) sb.Append("• ").Append(n).Append('\n');
            sb.Append($"\n<color={Grey}>Bars: one per punch, height = reaction time. " +
                      $"<color={Green}>green</color> evaded, <color={Blue}>blue</color> blocked, <color={Red}>red</color> hit, <color={Purple}>purple</color> feint.</color></size>");
            return sb.ToString();
        }

        string FinalText(RoundSummary t)
        {
            var sb = new StringBuilder();
            sb.Append($"<b>{DefenceMath.Label(level)} · SESSION DONE</b>\n<size=58%>\n");
            sb.Append(Lines(t));
            sb.Append("\n\nBy round:  ");
            foreach (var s in session.summaries.OrderBy(x => x.round))
            {
                float defended = s.thrown > 0 ? 100f * (s.thrown - s.hits) / s.thrown : 0f;
                sb.Append($"R{s.round} {defended:0}% defended   ");
            }
            sb.Append("\n\n");
            foreach (var n in DefenceMath.Insights(t)) sb.Append("• ").Append(n).Append('\n');
            if (!string.IsNullOrEmpty(savedPath)) sb.Append($"\n<color={Grey}>Saved: {Path.GetFileName(savedPath)}</color>");
            sb.Append($"\n<color={Yellow}>Press A to choose a level again.</color></size>");
            return sb.ToString();
        }

        void Save()
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "sessions");
                Directory.CreateDirectory(dir);
                savedPath = Path.Combine(dir, $"session-{session.id}.json");
                File.WriteAllText(savedPath, JsonUtility.ToJson(session, true));
                Debug.Log($"[BoxDefence] saved {savedPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BoxDefence] could not save session: {e.Message}");
            }
        }
    }
}
