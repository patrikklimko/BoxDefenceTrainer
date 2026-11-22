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

        AudioClip bell, thud, block, whoosh, landed, pad, crowdLoop, cheer, ooh;
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
            var n1 = new System.Random(1);
            thud = Synth.Make("thud", 0.35f, t =>
                0.9f * Synth.Sin(70f * (1f - 0.8f * t), t) * Mathf.Exp(-9f * t) + 0.5f * Synth.Noise(n1) * Mathf.Exp(-40f * t));
            var n2 = new System.Random(2);
            block = Synth.Make("block", 0.2f, t =>
                0.5f * Synth.Sin(240f, t) * Mathf.Exp(-25f * t) + 0.5f * Synth.Noise(n2) * Mathf.Exp(-60f * t));
            var n3 = new System.Random(3);
            float lp = 0f;
            whoosh = Synth.Make("whoosh", 0.25f, t =>
            {
                lp += 0.15f * (Synth.Noise(n3) - lp);                 // one-pole low-pass: airy, not hissy
                return 1.4f * lp * Mathf.Sin(Mathf.PI * t / 0.25f);
            });
            var n4 = new System.Random(4);
            landed = Synth.Make("landed", 0.25f, t =>
                0.8f * Synth.Sin(130f, t) * Mathf.Exp(-18f * t) + 0.6f * Synth.Noise(n4) * Mathf.Exp(-45f * t));
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
        }

        public void Ooh(float volume = 0.45f)
        {
            if (sfx2D) sfx2D.PlayOneShot(ooh, volume);
        }

        public void TookHit()
        {
            if (sfx2D) sfx2D.PlayOneShot(thud, 1f);
            flashAlpha = 0.55f;
            if (rig) rig.HapticBoth(0.9f, 0.25f);
        }

        public void Blocked(Vector3 at, bool leftHand)
        {
            PlayAt(block, at, 1f);
            if (rig) rig.Haptic(leftHand, 0.6f, 0.08f);
        }

        public void Landed(Vector3 at, bool leftHand)
        {
            PlayAt(landed, at, 1f);
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
