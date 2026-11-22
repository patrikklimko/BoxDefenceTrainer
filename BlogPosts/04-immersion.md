# Post 4: Immersion. Arena, sound, haptics and a real body

A defence drill works in an empty grey room, but you don't train like you fight there. This post covers everything that makes you forget you're standing in a classroom holding two controllers.

## The arena

You stand in a ring: a raised canvas with ropes, posts and corner pads. Around it are three rows of stands on each side with about **150 fans**, and above it walls, a roof, light bars and a banner.

Everything is generated in code, the fans with a fixed random seed so the crowd is the same every time. Quest 2 is a phone chip driving two screens, so the arena is cheap by design:

- primitives and flat colours instead of textures
- no shadows
- every static object marked for **static batching**, so Unity can combine the crowd and the stands into far fewer draw calls
- linear fog to hide the far walls

## Sound without sound files

There isn't a single audio file in the project. Every sound is **synthesised at start-up** from noise, sine waves and envelopes (`Feedback.cs`):

- the bell
- the thud of a hit, and the slap of a block
- the whoosh of a punch
- a looping crowd murmur, a cheer and an "ooh"

The crowd reacts. It cheers when you pick a level and at the end of each round, groans "ooh" when you get hit, and cheers for a clean counter in Pro.

## Haptics

The controllers are the only thing that touch you, so they do the talking:

- **Getting hit:** both controllers buzz hard (0.25 s), the view flashes red and a thud plays.
- **A block:** a short buzz in the glove that took it.
- **Your punch landing** (Pro): a buzz in the glove that landed.

![Getting hit: the red flash](images/hit-flash.jpg)

## A real opponent

For most of the project the opponent was a capsule man with IK arms (Post 2). It worked, and it was readable, but it didn't feel like a person. After watching a recording of myself playing, the first item on my list was "a real guy model".

I used **Remy** from Mixamo with the **Fighting Idle** animation, a proper boxing guard that bounces on the spot. Making him fight took a new component, `HumanoidPuppet`:

- The animation plays the idle bounce. Unity's **humanoid IK** then pulls both hands to where the game logic says the gloves are. He punches with his real arms and keeps the bounce in his legs.
- His upper body bends when he slips or flinches, by rotating the chest bone towards the offset.
- He's **scaled to your height** at the start of each session.
- His animated head is placed **exactly over the invisible head sphere** the game uses. In Pro, what you see is what you can hit.

The old primitive head and gloves are still there, but invisible: they're the hit volumes. The new body is purely visual. If the model files are missing, the scene builder keeps the primitive body, so the project always builds.

## Your own body

Looking down and seeing nothing breaks presence immediately. So you get a body too: the **same Remy**, standing under the headset, with:

- **his head hidden**, so you don't see inside your own face
- **his hands pulled by IK into your gloves**, so your arms bend at the elbow when you move the controllers
- **the body sinking with you** when you duck, rather than shrinking, so you never see inside your own neck

![Looking down: your arms and gloves](images/my-gloves.jpg)

## Real gloves

The glove is **"Boxing Glove" by Incg5764** on Sketchfab (CC Attribution, about 4,000 triangles). Two problems:

1. **The format.** Sketchfab offered only glTF, GLB, USDZ and .blend, and Unity can't read any of them without plug-ins. I wrote a short Python script that reads the GLB, applies its node transforms and writes a plain OBJ, which Unity imports natively.
2. **The colour.** The red was painted into the texture, and tinting a red texture blue gives near-black. The script builds a new, **white** texture from the original: the red leather becomes white (keeping its wrinkles and shading), and the cuff stays a darker grey. Now one material gives blue gloves for you, red gloves for him, and the **yellow wind-up** still works on his.

The model's longest side is detected and turned to point along the punch, then scaled to 28 cm for his gloves and 24 cm for yours. Your gloves started at 28 cm too, and in the headset they filled half the view.

## The model pipeline took longer than the models

Nothing about the real models worked first time:

| Problem | Cause | Fix |
|---|---|---|
| Remy rendered plain white | Mixamo embeds textures inside the FBX, and Unity didn't unpack them | The builder extracts them and connects each texture to its material by name |
| His hair rendered as solid blocks | Hair is cards with a separate opacity texture | Diffuse and opacity are merged into one RGBA texture, and the hair uses alpha cut-out |
| Risk of a see-through body | Mixamo characters ship opacity maps, and their materials can import as transparent, which sorts badly in VR | All materials are forced opaque before it can show up |
| A 3.3-metre opponent | The FBX's units import with Remy's head at 3.31 m | Height-fitting measures the animated head and scales to you, with a wide enough range |
| Bare fists poking out under the gloves | The IK hand kept the animation's rotation | The hand bones are hidden, because the glove is the hand |

All of these fixes are in `BoxDefenceModels.cs` and run automatically when the scene is built. Nobody needs to repeat them by hand.

## Next

[Post 5](05-results.md): testing it, what the numbers say, and what I'd change.
