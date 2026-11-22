# Post 6: Personal reflection

## Why this project

When the brief asked for an HMD app that solves a real problem, the list of ideas had plenty of the usual: training simulators, visualisations, virtual showrooms. The one I kept coming back to was personal. I like boxing, and what puts me off sparring isn't getting tired or losing. It's the head impacts, and what years of them do to a brain. The question "can I train the defensive part of sparring without getting hit?" was one I actually wanted answered, and that made the project easy to care about.

It also turned out to be a good fit for the medium. Most VR apps could be screen apps with a headset bolted on. This one can't: the whole point is that the punch goes for your *real* head and you get out of the way with your *real* body. That gave me a simple test for every feature: does it need VR? If the answer was no, it waited.

## What went well

**Getting the core loop working early.** The first build that ran on the headset was ugly (a capsule man, sphere gloves, a grey ring), but slips, ducks, pull-backs and blocks were already detected correctly. Everything after that was improvement on something that worked, not a rescue of something that didn't. I'll do it this way again: make the smallest thing that's *true* first, then make it pretty.

**Recording every playtest.** Mirroring the headset to the laptop with scrcpy and recording it changed how I worked. In the headset you're busy defending, and you don't notice that your gloves cover half the view, or that you started half a metre from the ropes. On the recording it's obvious. Almost every improvement in this project came from watching myself back.

**The data told me things I didn't know.** I expected the trainer to confirm I was decent at defence. Instead it showed me three habits:

- I bit on 13 of 14 feints.
- I never slipped to the right, not once in three rounds.
- When I started punching back in Pro, my guard dropped from about 93 % of the round to 58 %, and the share of punches that hit me tripled.

That last one is the classic beginner mistake, and I'd have sworn I don't make it. This is the moment the project went from "a cool demo" to "a tool I'd use".

**Keeping the logic separate from the looks.** Because the game logic only moves the gloves and everything else follows, replacing the capsule man with a real animated character didn't break the punch, block or hit code at all. The same goes for building the scene from code: I could rebuild the whole thing from scratch with one click, and it always came out the same.

## What was hard

**The tooling, not the code.** The biggest time sinks had nothing to do with boxing:

- Meta Link refused to run on my laptop ("hardware incompatible"), so I couldn't test in the editor with the headset on. Every test was a real Android build pushed over USB, taking 4 to 9 minutes. That slows down every small tweak.
- A URP settings file was one version ahead of the installed package, and the first build failed on it.
- The real models fought back: textures hidden inside the FBX, a character that imported 3.3 metres tall, a glove only available in formats Unity can't open, and a red texture that couldn't be tinted blue.

None of these are interesting problems, but together they took a real share of the time. I underestimated the asset pipeline. Next time, I'd get the slowest part of the loop (editor-to-headset preview) working first, even if it costs a day, and bring in real assets early, so their problems show up before the end.

**Designing for a body, not a screen.** Some of the most important decisions weren't programming decisions:

- **How tall he is.** At my own height he felt short and unthreatening. Ten centimetres taller felt right.
- **How big my gloves are.** Realistic size covered too much of the view, so I made them smaller than real.
- **How much warning the wind-up gives.** A few hundredths of a second decide whether a level feels fair or impossible.
- **Where I stand.** Moving the whole arena so I always start in the centre had to happen between rounds, never during one, or it would make you sick.

None of that comes from code. It comes from putting the headset on and feeling it.

**Being honest about the numbers.** It was tempting to present "average reaction 139 ms" as a result. But one duck showed 55 ms, which is impossible as a reaction. Working out why (the timer counts any head movement after the wind-up, including movement I'd already started) taught me more about measurement than the rest of the project. A number from a sensor is only as good as the definition behind it.

## Working with an AI assistant

I built this project together with an AI assistant (Claude). It's important to me to be clear about how that worked.

- **The assistant wrote most of the code and the first drafts of these posts.**
- **I decided what to build** (the boxing idea, the levels, feints, Pro mode, the real opponent, the arena, the lighting and the sounds).
- **I did all the setup, every playtest and the recordings,** and fed back what felt wrong in the headset.
- **I sourced the 3D models** and checked their licences.

The assistant couldn't put the headset on. It could only see what I recorded and described. So my job became knowing what to look for, and describing it precisely enough to fix: "the bare fists poke out under the gloves", "I'm too close to the ropes", "the hits sound thin".

What I learned from it: the bottleneck moved. Writing an IK solver was no longer the hard part. The hard parts were judgement (is this fun, is this fair, is this honest), testing, and the physical setup. I think that's where software development is heading, and this project was a good place to practise it.

## Would I use it?

Yes, as a supplement. It won't replace sparring:

- you can't move your feet or manage distance
- he never goes to the body
- a controller weighs nothing compared with a glove

But for the specific skill of reading a punch and getting your head out of the way, and for catching your own habits (biting on feints, slipping to one side, dropping your hands when you throw), it does something no bag or pad can do, without a single blow to the head.

## If I had another month

1. **A fairer reaction metric** that ignores movement already in progress (Post 5).
2. **Punches that look for gaps in the guard**, and body shots, so a high guard alone doesn't win.
3. **Footwork:** he steps in and out, and you have to manage range.
4. **A progress screen** built from the saved sessions, showing feints bitten, left/right balance and guard percentage, week by week.
5. **A real study:** a handful of people training with it for a few weeks, compared with a group that doesn't. That's the only way to find out whether it actually improves defence, which is the question that matters.
