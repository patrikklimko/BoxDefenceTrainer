# Post 1: The idea. Sparring without head impacts

I wanted to build something useful for a real sport. In boxing, the hardest thing to train alone is defence.

## The problem

You learn defence by having punches thrown at you. Shadow boxing and pad work build offence, but nothing comes back, so you never practise the moment that matters: seeing the shoulder move, reading the glove, and getting your head out of the way in time.

The only realistic way to train that is sparring, and sparring means getting hit in the head. One clean shot is not the issue. The issue is hundreds of light ones, round after round, for years. That's why a lot of gyms limit hard sparring, and why beginners often never get enough of it to learn to slip.

## Why VR, and why not a screen

A head-mounted display gives three things a screen can't:

1. **Your real head is the target.** The headset knows where your head is in 3D, every frame (72 times a second on a Quest 2). The opponent can aim at it, and you avoid the punch the way you would in the ring, by moving your head. A game on a screen would need a button for "slip", which trains a thumb, not a reflex.
2. **The movement can be measured.** Because the head and both hands are tracked, every punch has an answer: did you block it, slip it left or right, duck, pull back, or take it? How many milliseconds after the wind-up did you start moving? A sparring partner can't give you that.
3. **Depth and scale.** A punch coming at your face in stereo, at real size, makes you flinch. A punch on a monitor doesn't.

Physically, nothing touches you. The worst that can happen is a missed controller swing.

## Scope

The brief is an MVP of about 40 hours. I set the core loop as the MVP and everything else as extras:

**MVP:**

- an opponent at a realistic distance throws punches at your head
- each punch is telegraphed, so it's readable
- slips, ducks, pull-backs and blocks are detected and timed
- rounds with a summary at the end

**Then, if time allows:**

- levels, feints, letting you punch back, an arena with a crowd, sound, haptics, and a realistic opponent and body

All of that got done. The next posts cover how.

## Setup

| | |
|---|---|
| Headset | Meta Quest 2 (standalone, Android) |
| Engine | Unity 6.3 LTS, Universal Render Pipeline |
| XR | OpenXR with the Meta Quest feature group, XR Interaction Toolkit 3.5 |
| Dev machine | Lenovo Legion (Windows) |
| Deploy | Build And Run over USB |
| Mirroring | scrcpy, to see and record what the headset shows |

Meta's Link software reported my laptop's GPU setup as incompatible, so I couldn't preview in the editor through the headset. Every test was a real Android build pushed over USB, about 4–9 minutes each time. That pushed me towards two habits that paid off:

1. **The scene is built from code.** One menu item (BoxDefence → Build Training Scene) generates the arena, the opponent, the gloves, the menus and all the references between components. Nothing depends on dragging things around in the editor, so a rebuild always gives the same scene. The code is the documentation.
2. **The logic is tested without the headset.** The maths that decides what happened to a punch (block, slip, duck…) lives in a plain C# class with no scene dependencies. An editor self-check runs 31 tests on it in under a second. A mistake there shows up before I spend 9 minutes on a build.

## Next

[Post 2](02-the-opponent.md): how the opponent punches, and how a punch becomes "SLIP LEFT, 153 ms".
