using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

using GK2Mounts.Api;

namespace GK2Mounts
{
    // Mount sounds (Round 12 in PROBE-FINDINGS.md). The game's donkey walks silently (its walk clips have no step
    // events) and there is no cart sound at all, so everything here is played by the mod through the game's own sound
    // player (LazyAudio): string ids from the game's AudioConfig, 3D at the donkey, in the game's mixer groups, so the
    // in-game volume sliders apply. Timing comes from the donkey's animator: footfalls and wheel turns are points in
    // the walk clip's normalised time, grazing is the graze clip starting. Nothing here touches game data.
    internal static class MountSounds
    {
        private static readonly RaycastHit[] hits = new RaycastHit[8];
        private static readonly List<AnimatorClipInfo> clipInfo = new List<AnimatorClipInfo>(2);

        private static AnimationComponentBase boundAnim;
        internal static AnimationComponentBase DonkeyAnimation => boundAnim;

        private static bool inWalkClip;
        private static long lastHoofK, lastWheelK;
        private static bool inGrazeClip;
        private static SoundHandler grazeHandler;
        private static float nextBray = -1f, mountBrayAt = -1f;
        private static bool wasWalking;
        private static float idleSince;
        private static readonly HashSet<string> warnedIds = new HashSet<string>();
        public static string LastEvent { get; private set; } = "";

        private static StyleSettings S => Mount.S;

        // A fresh mount (not a re-summon after a door): maybe bray, and count the standing time for the bell from now.
        public static void OnMounted()
        {
            Reset();
            float now = Time.time;
            idleSince = now;
            nextBray = now + NextBrayDelay();
            if (S.SoundsOn.Value && UnityEngine.Random.value < S.BrayOnMountChance.Value)
            {
                mountBrayAt = now + UnityEngine.Random.Range(0.3f, 0.8f);
            }
        }

        // The donkey view went away (dismount, door, cleanup).
        public static void OnDonkeyGone()
        {
            StopGraze();
            boundAnim = null;
            inWalkClip = false;
            inGrazeClip = false;
        }

        private static void Reset()
        {
            OnDonkeyGone();
            mountBrayAt = -1f;
            wasWalking = false;
        }

        public static void Tick(Wgo donkey, bool walking)
        {
            AnimationComponentBase ac = donkey != null ? donkey.MainWgoPart?.AnimationComponent : null;
            if (ac != boundAnim)
            {
                StopGraze();
                boundAnim = ac;
                inWalkClip = false;
                inGrazeClip = false;
            }
            float now = Time.time;
            if (!walking && wasWalking)
            {
                idleSince = now;
            }
            bool setOff = walking && !wasWalking;
            float stoodFor = now - idleSince;
            wasWalking = walking;

            if (!S.SoundsOn.Value || ac == null)
            {
                StopGraze();
                return;
            }
            Transform at = donkey.transform;

            if (Plugin.BellKey.Value != KeyCode.None && UnityInput.Current.GetKeyDown(Plugin.BellKey.Value))
            {
                RingBell();
            }
            if (setOff && S.BellOnSetOff.Value && stoodFor >= S.BellSetOffIdleSeconds.Value
                && (!S.BellOnlyWithCargo.Value || Cargo.Count > 0) && !CargoSheet.Running)
            {
                RingBell();
            }

            if (mountBrayAt > 0f && now >= mountBrayAt)
            {
                mountBrayAt = -1f;
                Bray();
            }
            if (S.BrayIdleEverySeconds.Value > 0f)
            {
                if (nextBray < 0f)
                {
                    nextBray = now + NextBrayDelay();
                }
                else if (now >= nextBray)
                {
                    if (!walking || S.BrayWhileWalking.Value)
                    {
                        Bray();
                        nextBray = now + NextBrayDelay();
                    }
                    else
                    {
                        nextBray = now + UnityEngine.Random.Range(2f, 6f); // not mid-stride; soon after stopping
                    }
                }
            }
            else
            {
                nextBray = -1f;
            }

            Animator a = ac.Animator;
            if (a == null || !a.isActiveAndEnabled)
            {
                return;
            }
            string clip = "";
            a.GetCurrentAnimatorClipInfo(0, clipInfo);
            if (clipInfo.Count > 0 && clipInfo[0].clip != null)
            {
                clip = clipInfo[0].clip.name;
            }
            float t = a.GetCurrentAnimatorStateInfo(0).normalizedTime;

            bool walkClip = clip.StartsWith("walk", StringComparison.Ordinal);
            if (walkClip)
            {
                int n = Mathf.Max(1, S.HoofStepsPerCycle.Value);
                long hk = (long)Math.Floor(t * n + S.HoofPhase.Value);
                int every = Mathf.Max(1, S.WheelEveryCycles.Value);
                long wk = (long)Math.Floor(t / every + S.WheelPhase.Value);
                if (!inWalkClip)
                {
                    // Setting off: the first footfall and wheel turn sound at once. A left/right clip change while
                    // walking keeps counting from the new clip without an extra step.
                    lastHoofK = hk - 1;
                    lastWheelK = wk - 1;
                    inWalkClip = true;
                }
                if (hk < lastHoofK)
                {
                    lastHoofK = hk; // the clip restarted (turned round): no burst of steps
                }
                if (hk > lastHoofK)
                {
                    lastHoofK = hk;
                    if (S.HoofOn.Value)
                    {
                        Hoof(ac, at);
                    }
                }
                if (wk < lastWheelK)
                {
                    lastWheelK = wk;
                }
                if (wk > lastWheelK)
                {
                    lastWheelK = wk;
                    if (S.WheelOn.Value && Mount.CurrentStyle == MountStyle.Cart)
                    {
                        Wheel(a, at);
                    }
                }
            }
            else
            {
                inWalkClip = false;
            }

            bool grazeClip = clip.StartsWith("graze", StringComparison.Ordinal);
            if (grazeClip && !inGrazeClip && S.GrazeOn.Value)
            {
                StopGraze();
                grazeHandler = Play(S.GrazeSoundId.Value, at, S.GrazeVolume.Value, 1f);
                LastEvent = "graze";
            }
            else if (!grazeClip && inGrazeClip)
            {
                StopGraze(); // cut short (the donkey set off): the chewing stops with the animation
            }
            inGrazeClip = grazeClip;
        }

        private static float NextBrayDelay()
        {
            float every = S.BrayIdleEverySeconds.Value;
            return every <= 0f ? 1e6f : every * UnityEngine.Random.Range(0.5f, 1.5f);
        }

        private static void StopGraze()
        {
            try
            {
                if (grazeHandler != null && grazeHandler.IsActive)
                {
                    grazeHandler.Stop();
                }
            }
            catch
            {
            }
            grazeHandler = null;
        }

        // ------------------------------------------------------------------ the sounds

        private static void Hoof(AnimationComponentBase ac, Transform at)
        {
            string id = S.HoofSoundId.Value;
            if (string.IsNullOrEmpty(id))
            {
                SurfaceType surface = SurfaceType.Dirt;
                if (S.HoofSurfaceAware.Value)
                {
                    try
                    {
                        surface = SurfaceUnder(at.position);
                    }
                    catch
                    {
                        surface = SurfaceType.Dirt;
                    }
                    if (surface == SurfaceType.None)
                    {
                        surface = SurfaceType.Dirt; // no tagged ground under the donkey: still a step
                    }
                }
                id = StepIdFor(surface);
            }
            float v = S.HoofPitchVariation.Value;
            Play(id, at, S.HoofVolume.Value, S.HoofPitch.Value * (1f + UnityEngine.Random.Range(-v, v)));
            LastEvent = "hoof " + id;
        }

        // The game's footstep surface probe (AnimationComponentBase.GetSurfaceType), cast from the donkey's root: the
        // game never runs it for the donkey, and its animation component may sit on an offset child.
        private static SurfaceType SurfaceUnder(Vector3 pos)
        {
            int n = Physics.RaycastNonAlloc(new Ray(pos + Vector3.up * 0.01666667f, Physics.gravity.normalized), hits, 1f, 2060);
            int best = int.MaxValue;
            SurfaceType type = SurfaceType.None;
            for (int i = 0; i < n; i++)
            {
                IOrderedSurface s = hits[i].collider != null ? hits[i].collider.GetComponent<IOrderedSurface>() : null;
                if (s != null && s.Depth < best)
                {
                    best = s.Depth;
                    type = s.SurfaceType;
                }
            }
            return type;
        }

        private static string StepIdFor(SurfaceType surface)
        {
            try
            {
                SurfaceStepSoundSettings settings = LazySingletonSO<SurfaceStepSoundSettings>.Instance;
                if (settings != null)
                {
                    string id = settings.GetStepSoundIdForSurfaceType(surface);
                    if (!string.IsNullOrEmpty(id))
                    {
                        return id;
                    }
                }
            }
            catch
            {
            }
            return "step_dirt";
        }

        private static void Wheel(Animator a, Transform at)
        {
            float pitch = S.WheelPitch.Value;
            if (S.WheelPitchFollowsSpeed.Value)
            {
                try
                {
                    pitch *= Mathf.Clamp(a.GetFloat("walk_speed"), 0.5f, 2.5f);
                }
                catch
                {
                }
            }
            Play(S.WheelSoundId.Value, at, S.WheelVolume.Value, pitch);
            LastEvent = "wheel";
        }

        public static void Bray()
        {
            Transform at = Mount.Donkey != null ? Mount.Donkey.transform : null;
            SoundHandler h = Play(S.BraySoundId.Value, at, S.BrayVolume.Value, S.BrayPitch.Value);
            if (h != null && S.BrayInSfxChannel.Value)
            {
                AudioConfig cfg = LazySingletonSO<AudioConfig>.Instance;
                Sound sfx = cfg != null ? cfg.Get("donkey_bell") : null; // an ordinary SFX-group sound
                if (sfx != null && sfx.group != null)
                {
                    h.SetMixerGroup(sfx.group);
                }
            }
            LastEvent = "bray";
        }

        public static void RingBell()
        {
            Transform at = Mount.Donkey != null ? Mount.Donkey.transform : null;
            Play(S.BellSoundId.Value, at, S.BellVolume.Value, S.BellPitch.Value);
            LastEvent = "bell";
        }

        // An item landed on the cart (a pickup or the head's load at mount).
        public static void CargoLanded()
        {
            if (Mount.CurrentStyle != MountStyle.Cart || CargoSheet.Running || !S.SoundsOn.Value || !S.CargoThumpOn.Value)
            {
                return;
            }
            Transform at = Mount.Donkey != null ? Mount.Donkey.transform : null;
            Play(S.CargoThumpSoundId.Value, at, S.CargoThumpVolume.Value, S.CargoThumpPitch.Value);
            LastEvent = "cargo thump";
        }

        // The donkey's own lips clip calls AnimationComponentBase.PlaySound("donkey_lips") from an animation event;
        // the PlaySound prefix sends it here so its volume follows the Sounds tab.
        internal static void Lips(string id, Transform at)
        {
            Play(id, at, S.LipsVolume.Value, 1f);
            LastEvent = "lips";
        }

        // Preview buttons on the Sounds tab.
        public static void Preview(string what)
        {
            Wgo d = Mount.Donkey;
            if (d == null)
            {
                return;
            }
            switch (what)
            {
                case "hoof":
                    if (d.MainWgoPart?.AnimationComponent != null)
                    {
                        Hoof(d.MainWgoPart.AnimationComponent, d.transform);
                    }
                    break;
                case "graze":
                    StopGraze();
                    grazeHandler = Play(S.GrazeSoundId.Value, d.transform, S.GrazeVolume.Value, 1f);
                    break;
                case "lips": Lips("donkey_lips", d.transform); break;
                case "bray": Bray(); break;
                case "thump": Play(S.CargoThumpSoundId.Value, d.transform, S.CargoThumpVolume.Value, S.CargoThumpPitch.Value); break;
                case "wheel": Play(S.WheelSoundId.Value, d.transform, S.WheelVolume.Value, S.WheelPitch.Value); break;
                case "bell": RingBell(); break;
            }
        }

        // Plays a game sound id 3D at the donkey. Volume and pitch multiply the game's own values for that sound.
        // An id the game does not have is refused (the game would play its placeholder clip instead) and logged once.
        private static SoundHandler Play(string id, Transform at, float volume, float pitch)
        {
            if (string.IsNullOrEmpty(id) || at == null || !LazyAudio.IsInitialized)
            {
                return null;
            }
            float v = volume * S.SoundVolume.Value;
            if (v <= 0f)
            {
                return null;
            }
            try
            {
                AudioConfig cfg = LazySingletonSO<AudioConfig>.Instance;
                if (cfg == null || cfg.Get(id) == null)
                {
                    if (warnedIds.Add(id))
                    {
                        Plugin.Log.LogWarning($"Sounds: the game has no sound '{id}'; nothing played.");
                    }
                    return null;
                }
                SoundHandler h = LazyAudio.PlayAtGameObject(id, at, SpatialType.sound3D, checkDelay: false);
                if (h == null)
                {
                    return null;
                }
                h.SetVolume(v);
                h.SetPitch(Mathf.Clamp(pitch, 0.1f, 3f));
                // The game's pooled players keep the fade times of whatever used them last, and a fade re-applies
                // the volume read at fade setup every frame; zero fades with the volume above as the target.
                h.SetFadeInFadeOut(0f, 0f);
                return h;
            }
            catch (Exception e)
            {
                if (warnedIds.Add("!" + id))
                {
                    Plugin.Log.LogWarning($"Sounds: playing '{id}' failed: {e.Message}");
                }
                return null;
            }
        }

        public static bool KnownId(string id)
        {
            try
            {
                AudioConfig cfg = LazySingletonSO<AudioConfig>.Instance;
                return !string.IsNullOrEmpty(id) && cfg != null && cfg.Get(id) != null;
            }
            catch
            {
                return false;
            }
        }
    }

    // The keeper rides the donkey in both styles, but the Cart riding pose is the game's walk animation, whose step
    // events would sound footsteps. While mounted the keeper's step events are skipped; nothing else is affected.
    [HarmonyPatch(typeof(AnimationComponentBase), nameof(AnimationComponentBase.OnAnimationStepCompleted))]
    internal static class AnimationComponentBase_OnAnimationStepCompleted_Patch
    {
        private static bool Prefix(AnimationComponentBase __instance)
        {
            try
            {
                if (!Mount.IsMounted)
                {
                    return true;
                }
                PlayerController pc = MainGame.PlayerController;
                return pc == null || !ReferenceEquals(__instance, pc.View?.PlayerAnimation);
            }
            catch
            {
                return true;
            }
        }
    }

    // The donkey's lips clip plays donkey_lips through an animation event; while mounted it goes through the Sounds
    // tab's volume instead. Every other caller and every other character runs the game's code.
    [HarmonyPatch(typeof(AnimationComponentBase), nameof(AnimationComponentBase.PlaySound))]
    internal static class AnimationComponentBase_PlaySound_Patch
    {
        private static bool Prefix(AnimationComponentBase __instance, string sound)
        {
            try
            {
                if (!Mount.IsMounted || __instance == null || !ReferenceEquals(__instance, MountSounds.DonkeyAnimation) || !Mount.S.SoundsOn.Value)
                {
                    return true;
                }
                MountSounds.Lips(sound, __instance.transform);
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
