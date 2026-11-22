using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoxDefence
{
    public enum Level { Easy, Medium, Hard, Pro }

    /// <summary>One opponent punch (or feint) and what the player did about it.</summary>
    [Serializable]
    public class PunchRecord
    {
        public int round;
        public float time;          // seconds since the round started
        public string punch;        // jab, cross, left hook, right hook, feint
        public string outcome;      // HIT, BLOCKED, SLIP LEFT, ..., BIT ON FEINT, READ FEINT
        public float windupMs;      // how long the warning lasted
        public float strikeMs;      // how fast the punch itself was
        public float reactionMs = -1f; // wind-up start -> head first moved 5 cm (-1 = never)
    }

    /// <summary>One of the player's own punches that landed (Pro level).</summary>
    [Serializable]
    public class LandedRecord
    {
        public int round;
        public float time;
        public string hand;         // left / right
        public string target;       // head / body
        public float speed;         // m/s at impact
        public bool counter;        // landed in the gap after a defence
    }

    [Serializable]
    public class RoundSummary
    {
        public int round;
        public string level;
        public int thrown, hits, blocks, slipsLeft, slipsRight, ducks, pullBacks, missed;
        public int feints, feintsBit;
        public float avgReactionMs = -1f;
        public float guardPct;
        public int landed, counters, guardedByHim;
        public float avgSpeed, maxSpeed;
        public float windupMs;
    }

    [Serializable]
    public class SessionRecord
    {
        public string id;
        public string utc;
        public string device;
        public string level;
        public int rounds;
        public float roundSeconds;
        public List<RoundSummary> summaries = new List<RoundSummary>();
        public List<PunchRecord> punches = new List<PunchRecord>();
        public List<LandedRecord> landed = new List<LandedRecord>();
    }

    /// <summary>How the opponent fights at one level.</summary>
    [Serializable]
    public struct Difficulty
    {
        public Level level;
        public float windup;        // seconds the glove glows before the punch
        public float strike;        // seconds the punch takes to land
        public float minGap, maxGap;
        public float comboChance;
        public int minCombo, maxCombo;
        public float feintChance;
        public bool playerCanPunch;
        public float activeDefence; // chance he blocks or slips one of your punches

        static Difficulty Preset(Level level) => level switch
        {
            Level.Easy => new Difficulty
            {
                level = level, windup = 0.62f, strike = 0.22f, minGap = 1.5f, maxGap = 2.5f,
                comboChance = 0.3f, minCombo = 2, maxCombo = 2,
            },
            Level.Medium => new Difficulty
            {
                level = level, windup = 0.46f, strike = 0.17f, minGap = 1.0f, maxGap = 1.9f,
                comboChance = 0.55f, minCombo = 2, maxCombo = 5,
            },
            Level.Hard => new Difficulty
            {
                level = level, windup = 0.34f, strike = 0.13f, minGap = 0.7f, maxGap = 1.4f,
                comboChance = 0.6f, minCombo = 2, maxCombo = 5, feintChance = 0.25f,
            },
            _ => new Difficulty
            {
                level = level, windup = 0.34f, strike = 0.13f, minGap = 0.8f, maxGap = 1.6f,
                comboChance = 0.6f, minCombo = 2, maxCombo = 5, feintChance = 0.25f,
                playerCanPunch = true, activeDefence = 0.5f,
            },
        };

        /// <summary>
        /// The level's preset, a little faster each round, and adjusted to how
        /// well you are defending (adapt: -0.5 = struggling, +1 = defending everything).
        /// </summary>
        public static Difficulty For(Level level, int round, float adapt)
        {
            var d = Preset(level);
            float r = Mathf.Clamp(round - 1, 0, 4);
            float speed = 1f - 0.05f * r - 0.12f * adapt;
            d.windup = Mathf.Max(0.18f, d.windup * speed);
            d.strike = Mathf.Max(0.10f, d.strike * Mathf.Lerp(1f, speed, 0.5f));
            d.minGap *= speed;
            d.maxGap *= speed;
            return d;
        }
    }
}
