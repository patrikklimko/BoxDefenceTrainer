using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace BoxDefence
{
    /// <summary>
    /// Everything the player sees, hears and feels when something happens:
    /// spatial sounds (made in code, so there are no audio files), a red flash
    /// when hit, a short text popup, and controller vibration.
    /// </summary>
    public class Feedback : MonoBehaviour
    {
        public PlayerRig rig;
        public AudioSource sfx2D;      // non-spatial: bell, being hit
        public Renderer flash;         // red quad in front of the eyes
        public TextMeshPro popup;      // above the opponent
        public AudioSource crowd;      // looping arena murmur

        [Header("Effects (optional)")]
        public ParticleSystem blockFx;   // sparks where gloves meet
        public ParticleSystem sweatFx;   // spray when your punch lands
        public CrowdAnimator crowdAnim;  // fans bob, and jump when they cheer

        AudioClip bell, whoosh, pad, crowdLoop, cheer, ooh;
        AudioClip[] thuds, blocks, landeds;     // three takes each, so repeated hits don't sound like a loop
        float flashAlpha;
        float popupUntil;
        Color popupColor = Color.white;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            bell = Synth.Make("bell", 1.6f, t =>
                (0.5f * Synth.Sin(880f, t) + 0.3f * Synth.Sin(1320f, t) + 0.15f * Synth.Sin(2637f, t)) * Mathf.Exp(-2.5f * t));
            // impacts: a low body thump with a falling pitch, a sharp leather smack, a tiny crack,
            // all pushed through soft saturation so they hit like a punch instead of beeping
            thuds = new AudioClip[3]; blocks = new AudioClip[3]; landeds = new AudioClip[3];
            for (int v = 0; v < 3; v++)
            {
                float f = 1f + 0.07f * (v - 1);
                thuds[v] = Synth.Impact("thud" + v, 10 + v, 0.45f, 140f * f, 42f * f, 11f, 1.15f, 0.36f, 0.92f, 1.7f, 70f, 0.6f, 1.9f);
                landeds[v] = Synth.Impact("landed" + v, 20 + v, 0.35f, 170f * f, 65f * f, 15f, 0.9f, 0.45f, 0.9f, 1.9f, 60f, 0.7f, 1.7f);
                blocks[v] = Synth.Impact("block" + v, 30 + v, 0.25f, 115f * f, 60f * f, 22f, 0.8f, 0.17f, 0.95f, 1.3f, 45f, 0.15f, 1.4f);
            }
            var n3 = new System.Random(3);
            float lp = 0f;
            whoosh = Synth.Make("whoosh", 0.25f, t =>
            {
                lp += 0.15f * (Synth.Noise(n3) - lp);                 // one-pole low-pass: airy, not hissy
                return 1.4f * lp * Mathf.Sin(Mathf.PI * t / 0.25f);
            });
            var n5 = new System.Random(5);
            pad = Synth.Make("pad", 0.12f, t =>
                0.4f * Synth.Noise(n5) * Mathf.Exp(-70f * t) + 0.3f * Synth.Sin(420f, t) * Mathf.Exp(-40f * t));
            // arena crowd: low-passed noise with a slow swell, looped
            var n6 = new System.Random(6);
            float c1 = 0f, c2 = 0f;
            crowdLoop = Synth.Make("crowd", 6f, t =>
            {
                c1 += 0.05f * (Synth.Noise(n6) - c1);
                c2 += 0.05f * (c1 - c2);
                float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * t / 6f);
                return 3.2f * c2 * swell;
            });
            var n7 = new System.Random(7);
            float h1 = 0f;
            cheer = Synth.Make("cheer", 1.8f, t =>
            {
                h1 += 0.3f * (Synth.Noise(n7) - h1);
                float env = Mathf.Min(1f, t / 0.25f) * Mathf.Exp(-1.6f * t);
                return 1.6f * h1 * env + 0.08f * Synth.Sin(1900f + 300f * Mathf.Sin(9f * t), t) * env;
            });
            var n8 = new System.Random(8);
            float o1 = 0f;
            ooh = Synth.Make("ooh", 1.0f, t =>
            {
                o1 += 0.08f * (Synth.Noise(n8) - o1);
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.0f));
                return (2.2f * o1 + 0.15f * Synth.Sin(190f - 40f * t, t)) * env;
            });
            if (crowd)
            {
                crowd.clip = crowdLoop;
                crowd.loop = true;
                crowd.spatialBlend = 0f;
                crowd.volume = 0.25f;
                crowd.Play();
            }
            if (flash) flash.enabled = false;
            if (popup) popup.text = "";
        }

        // ---------------------------------------------------------------- events

        public void Bell(int times = 1) => StartCoroutine(RingBell(times));

        IEnumerator RingBell(int times)
        {
            for (int i = 0; i < times; i++)
            {
                if (sfx2D) sfx2D.PlayOneShot(bell, 0.7f);
                yield return new WaitForSeconds(0.35f);
            }
        }

        public void PunchThrown(Vector3 at) => PlayAt(whoosh, at, 0.6f);

        public void Cheer(float volume = 0.6f)
        {
            if (sfx2D) sfx2D.PlayOneShot(cheer, volume);
            if (crowdAnim) crowdAnim.Excite(Mathf.Clamp01(volume + 0.3f));
        }

        public void Ooh(float volume = 0.45f)
        {
            if (sfx2D) sfx2D.PlayOneShot(ooh, volume);
            if (crowdAnim) crowdAnim.Excite(0.35f);
        }

        public void TookHit()
        {
            if (sfx2D) sfx2D.PlayOneShot(Pick(thuds), 1f);
            flashAlpha = 0.55f;
            if (rig) rig.HapticBoth(0.9f, 0.25f);
        }

        public void Blocked(Vector3 at, bool leftHand)
        {
            PlayAt(Pick(blocks), at, 1f);
            Burst(blockFx, at, 26);
            if (rig) rig.Haptic(leftHand, 0.6f, 0.08f);
        }

        public void Landed(Vector3 at, bool leftHand)
        {
            PlayAt(Pick(landeds), at, 1f);
            Burst(sweatFx, at, 32);
            if (rig) rig.Haptic(leftHand, 0.8f, 0.1f);
        }

        public void Padded(Vector3 at, bool leftHand)
        {
            PlayAt(pad, at, 0.7f);
            if (rig) rig.Haptic(leftHand, 0.3f, 0.05f);
        }

        public void Popup(string text, Color color, float seconds = 1.1f)
        {
            if (!popup) return;
            popup.text = text;
            popupColor = color;
            popupUntil = Time.time + seconds;
        }

        static AudioClip Pick(AudioClip[] clips) => clips[UnityEngine.Random.Range(0, clips.Length)];

        static void Burst(ParticleSystem fx, Vector3 at, int count)
        {
            if (!fx) return;
            fx.transform.position = at;
            fx.Emit(count);
        }

        static void PlayAt(AudioClip clip, Vector3 at, float volume)
        {
            if (clip) AudioSource.PlayClipAtPoint(clip, at, volume); // 3D: you hear where it happened
        }

        // ---------------------------------------------------------------- fades

        void Update()
        {
            if (flash)
            {
                flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.deltaTime * 2f);
                flash.enabled = flashAlpha > 0.01f;
                flash.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, new Color(0.9f, 0f, 0f, flashAlpha));
                flash.SetPropertyBlock(mpb);
            }
            if (popup)
            {
                float a = Mathf.Clamp01((popupUntil - Time.time) / 0.3f);
                popup.color = new Color(popupColor.r, popupColor.g, popupColor.b, a);
            }
        }
    }

    /// <summary>Tiny sound synthesiser, so the project needs no audio assets.</summary>
    public static class Synth
    {
        const int Rate = 44100;

        public static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        public static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

        /// <summary>
        /// A punch-like impact. body: sine sweeping from bodyHi to bodyLo Hz, decaying at bodyDecay.
        /// smack: noise through a low-pass (lpA, 0..1, higher = brighter) and a high-pass (hpA, near 1 = higher cut),
        /// decaying at smackDecay. crack: a 3 ms click. drive: saturation amount.
        /// </summary>
        public static AudioClip Impact(string name, int seed, float seconds, float bodyHi, float bodyLo, float bodyDecay,
                                       float bodyGain, float lpA, float hpA, float smackGain, float smackDecay, float crackGain, float drive)
        {
            var rnd = new System.Random(seed);
            float phase = 0f, lp = 0f, prevLp = 0f, hp = 0f;
            return Make(name, seconds, t =>
            {
                float f = bodyLo + (bodyHi - bodyLo) * Mathf.Exp(-25f * t);
                phase += 2f * Mathf.PI * f / Rate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-bodyDecay * t) * Mathf.Min(1f, t / 0.0015f);
                float n = Noise(rnd);
                lp += lpA * (n - lp);
                hp = hpA * (hp + lp - prevLp);
                prevLp = lp;
                float smack = hp * Mathf.Exp(-smackDecay * t);
                float crack = t < 0.003f ? n * (1f - t / 0.003f) : 0f;
                float x = bodyGain * body + smackGain * smack + crackGain * crack;
                return 0.92f * (float)Math.Tanh(drive * x);
            });
        }

        public static AudioClip Make(string name, float seconds, Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(Rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
