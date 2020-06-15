using ff14bot;
using ff14bot.Managers;
using Magitek.Extensions;
using ff14bot.Objects;
using Magitek.Logic;
using Magitek.Logic.Summoner;
using Magitek.Utilities;
using System.Linq;
using System.Threading.Tasks;
using Magitek.Utilities.Routines;
using System.Collections.Generic;
using Auras = Magitek.Utilities.Auras;
using Common.Logging.Simple;
using System;
using Buddy.Coroutines;

namespace Magitek.Rotations
{
    public enum SmnStateIds
    {
        Start,
        Filler,
        FirstFillerWeaveDWT,
        SecondFillerWeaveDWT,
        FirstFillerWeave,
        SecondFillerWeave,
        Dreadwyrm,
        FirstDreadwyrmWeave,
        SecondDreadwyrmWeave,
        DreadwyrmFirstFinalWeave,
        DreadwyrmSecondFinalWeave,
        BahamutFirstEnkindle,
        BahamutSecondEnkindle,
        BahamutRuinUntilSecondEnkindle,
        FirstBahamutWeave,
        SecondBahamutWeave,
    }

    public static class Summoner
    {
        private const int DoTPrecastTime = 1900;
        
        private static StateMachine<SmnStateIds> mStateMachine;

        private static bool HasMyAura(GameObject target, uint aura)
        {
            return (target as Character)?.Auras.Any(x => x.Id == aura && x.CasterId == Core.Player.ObjectId) ?? false;
        }

        private static double MyAuraTimeRemaining(GameObject target, uint auraId)
        {
            Character tc = target as Character;
            
            if (tc == null)
                return 0;

            Aura aura = tc.Auras.FirstOrDefault(a => a.Caster == Core.Me && a.Id == auraId);

            if (aura == null)
                return 0;

            return aura.TimespanLeft.TotalMilliseconds;
        }

        private static int FurtherRuinStacks => Core.Me.Auras.GetAuraStacksById(Auras.FurtherRuin);

        private static bool BurnRuin4 => FurtherRuinStacks == 4 && (Spells.EgiAssault.Cooldown == TimeSpan.Zero || Spells.EgiAssault2.Cooldown == TimeSpan.Zero);

        private static bool UseMiasma3 => MyAuraTimeRemaining(Core.Me.CurrentTarget, Auras.Miasma3) <= Spells.Miasma3.AdjustedCastTime.TotalMilliseconds + DoTPrecastTime && Spells.TriDisaster.Cooldown > Spells.Miasma3.AdjustedCastTime;

        private static bool UseFester => Core.Me.CurrentTarget.HasAura(Auras.Miasma3, true);
        //TODO Check balance for how early to start recasting dots
        private static bool UseTriDisaster => MyAuraTimeRemaining(Core.Me.CurrentTarget, Auras.Miasma3) <= DoTPrecastTime;

        private static double GcdLeft => Math.Max(Spells.Ruin.Cooldown.TotalMilliseconds - 350, 0);

        private static bool ReadyForDreadwyrm => FurtherRuinStacks >= 3;

        private static bool EnergyDrain => ActionResourceManager.Arcanist.Aetherflow == 0;

        private static bool EndDreadwyrm =>
               (   Spells.TriDisaster.Cooldown > TimeSpan.Zero
                && Spells.Enkindle.Cooldown > TimeSpan.Zero
                && (Spells.Fester.Cooldown > TimeSpan.Zero || ActionResourceManager.Arcanist.Aetherflow == 0))
            || Spells.Trance.AdjustedCooldown.TotalMilliseconds - Spells.Trance.Cooldown.TotalMilliseconds > 9000;

        public static async Task<bool> Dreadwyrm()
        {
            if (await SmUtil.SyncedCast(Spells.Trance, Core.Me))
            {
                await Coroutine.Wait(2000, () => ActionResourceManager.Summoner.DreadwyrmTrance);
                return true;
            }
            return false;
        }

        static Summoner()
        {
            mStateMachine = new StateMachine<SmnStateIds>(
                SmnStateIds.Start,
                new Dictionary<SmnStateIds, State<SmnStateIds>>()
                {
                    {
                        SmnStateIds.Start,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {                                
                                new StateTransition<SmnStateIds>(() => true,                    () => SmUtil.NoOp(), SmnStateIds.Filler, TransitionType.Immediate)
                            })
                    },
                   {
                        SmnStateIds.Filler,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {                                
                                new StateTransition<SmnStateIds>(() => BurnRuin4,  () => SmUtil.SyncedCast(Spells.Ruin4, Core.Me.CurrentTarget), SmnStateIds.FirstFillerWeave),
                                new StateTransition<SmnStateIds>(() => FurtherRuinStacks >= 2,  () => SmUtil.SyncedCast(Spells.EgiAssault, Core.Me.CurrentTarget), SmnStateIds.FirstFillerWeaveDWT),
                                new StateTransition<SmnStateIds>(() => FurtherRuinStacks >= 2,  () => SmUtil.SyncedCast(Spells.EgiAssault2, Core.Me.CurrentTarget), SmnStateIds.FirstFillerWeaveDWT),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.EgiAssault, Core.Me.CurrentTarget), SmnStateIds.FirstFillerWeave),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.EgiAssault2, Core.Me.CurrentTarget), SmnStateIds.FirstFillerWeave),
                                //new StateTransition<SmnStateIds>(() => UseMiasma3,  () => SmUtil.SyncedCastAura(Spells.Miasma3, Core.Me.CurrentTarget, Auras.Miasma3), SmnStateIds.Bio),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.Ruin3, Core.Me.CurrentTarget), SmnStateIds.FirstFillerWeave)
                            })
                    },
                    {
                        SmnStateIds.FirstFillerWeaveDWT,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                
                                new StateTransition<SmnStateIds>(() => GcdLeft < 1400, () => SmUtil.NoOp(),  SmnStateIds.SecondFillerWeave, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => EnergyDrain,  () => SmUtil.SyncedCast(Spells.EnergyDrain, Core.Me.CurrentTarget), SmnStateIds.SecondFillerWeaveDWT),
                                new StateTransition<SmnStateIds>(() => UseTriDisaster,  () => SmUtil.SyncedCastAura(Spells.TriDisaster, Core.Me.CurrentTarget, Auras.Miasma3), SmnStateIds.SecondFillerWeaveDWT),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.SmnAetherpact, Core.Me.CurrentTarget), SmnStateIds.SecondFillerWeaveDWT),
                                new StateTransition<SmnStateIds>(() => true,  () => Dreadwyrm(), SmnStateIds.Dreadwyrm),

                            })
                    },
                    {
                        SmnStateIds.SecondFillerWeaveDWT,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => GcdLeft < 700,        () => SmUtil.NoOp(),    SmnStateIds.Filler, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => EnergyDrain,  () => SmUtil.SyncedCast(Spells.EnergyDrain, Core.Me.CurrentTarget), SmnStateIds.Filler),
                                new StateTransition<SmnStateIds>(() => UseTriDisaster,  () => SmUtil.SyncedCastAura(Spells.TriDisaster, Core.Me.CurrentTarget, Auras.Miasma3), SmnStateIds.Filler),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.SmnAetherpact, Core.Me.CurrentTarget), SmnStateIds.Filler),
                                new StateTransition<SmnStateIds>(() => true,  () => Dreadwyrm(), SmnStateIds.Dreadwyrm),

                            })
                    },
                    {
                        SmnStateIds.FirstFillerWeave,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {

                                new StateTransition<SmnStateIds>(() => GcdLeft < 1400, () => SmUtil.NoOp(),  SmnStateIds.SecondFillerWeave, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => EnergyDrain,  () => SmUtil.SyncedCast(Spells.EnergyDrain, Core.Me.CurrentTarget), SmnStateIds.SecondFillerWeave),
                                new StateTransition<SmnStateIds>(() => UseTriDisaster,  () => SmUtil.SyncedCastAura(Spells.TriDisaster, Core.Me.CurrentTarget, Auras.Miasma3), SmnStateIds.SecondFillerWeave),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.SmnAetherpact, Core.Me.CurrentTarget), SmnStateIds.SecondFillerWeave),
                                new StateTransition<SmnStateIds>(() => ReadyForDreadwyrm,  () => Dreadwyrm(), SmnStateIds.Dreadwyrm),

                            })
                    },
                    {
                        SmnStateIds.SecondFillerWeave,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => GcdLeft < 700,        () => SmUtil.NoOp(),    SmnStateIds.Filler, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => EnergyDrain,  () => SmUtil.SyncedCast(Spells.EnergyDrain, Core.Me.CurrentTarget), SmnStateIds.Filler),
                                new StateTransition<SmnStateIds>(() => UseTriDisaster,  () => SmUtil.SyncedCastAura(Spells.TriDisaster, Core.Me.CurrentTarget, Auras.Miasma3), SmnStateIds.Filler),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.SmnAetherpact, Core.Me.CurrentTarget), SmnStateIds.Filler),
                                new StateTransition<SmnStateIds>(() => ReadyForDreadwyrm,  () => Dreadwyrm(), SmnStateIds.Dreadwyrm),

                            })
                    },
                    {
                        SmnStateIds.Dreadwyrm,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => !ActionResourceManager.Summoner.DreadwyrmTrance,  () => SmUtil.NoOp(), SmnStateIds.DreadwyrmSecondFinalWeave, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.EgiAssault, Core.Me.CurrentTarget), SmnStateIds.FirstDreadwyrmWeave),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.EgiAssault2, Core.Me.CurrentTarget), SmnStateIds.FirstDreadwyrmWeave),                                
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.Ruin3, Core.Me.CurrentTarget), SmnStateIds.FirstDreadwyrmWeave)

                            })
                    },
                    {
                        SmnStateIds.FirstDreadwyrmWeave,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => EndDreadwyrm,  () => SmUtil.NoOp(), SmnStateIds.DreadwyrmFirstFinalWeave),
                                new StateTransition<SmnStateIds>(() => GcdLeft < 1400, () => SmUtil.NoOp(),  SmnStateIds.SecondDreadwyrmWeave, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => !Core.Me.CurrentTarget.HasAura(Auras.Miasma3, true),  () => SmUtil.SyncedCastAura(Spells.TriDisaster, Core.Me.CurrentTarget, Auras.Miasma3), SmnStateIds.SecondDreadwyrmWeave),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.Enkindle, Core.Me.CurrentTarget), SmnStateIds.SecondDreadwyrmWeave),
                                new StateTransition<SmnStateIds>(() => UseFester,  () => SmUtil.SyncedCast(Spells.Fester, Core.Me.CurrentTarget), SmnStateIds.SecondDreadwyrmWeave),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCastAura(Spells.TriDisaster, Core.Me.CurrentTarget, Auras.Miasma3), SmnStateIds.SecondDreadwyrmWeave),
                            })
                    },
                    {
                        SmnStateIds.SecondDreadwyrmWeave,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => GcdLeft < 700,        () => SmUtil.NoOp(),    SmnStateIds.Dreadwyrm, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => !Core.Me.CurrentTarget.HasAura(Auras.Miasma3, true),  () => SmUtil.SyncedCastAura(Spells.TriDisaster, Core.Me.CurrentTarget, Auras.Miasma3), SmnStateIds.Dreadwyrm),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.Enkindle, Core.Me.CurrentTarget), SmnStateIds.Dreadwyrm),
                                new StateTransition<SmnStateIds>(() => UseFester,  () => SmUtil.SyncedCast(Spells.Fester, Core.Me.CurrentTarget), SmnStateIds.Dreadwyrm),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCastAura(Spells.TriDisaster, Core.Me.CurrentTarget, Auras.Miasma3), SmnStateIds.Dreadwyrm),
                            })
                    },
                    {
                        SmnStateIds.DreadwyrmFirstFinalWeave,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => GcdLeft < 1400, () => SmUtil.NoOp(),  SmnStateIds.DreadwyrmSecondFinalWeave, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.Deathflare, Core.Me.CurrentTarget), SmnStateIds.DreadwyrmSecondFinalWeave),
                            })
                    },
                    {
                        SmnStateIds.DreadwyrmSecondFinalWeave,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => GcdLeft < 700,        () => SmUtil.NoOp(),    SmnStateIds.Dreadwyrm, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.SummonBahamut, Core.Me.CurrentTarget), SmnStateIds.BahamutFirstEnkindle),
                            })
                    },
                    {
                        //TODO What happens if bahamut goes away before we finish these
                        SmnStateIds.BahamutFirstEnkindle,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {                                
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.Ruin4, Core.Me.CurrentTarget), SmnStateIds.FirstBahamutWeave),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.Ruin2, Core.Me.CurrentTarget), SmnStateIds.FirstBahamutWeave),                                
                            })
                    },
                    {
                            //TODO Recover if first enkindle is missed by casting another ruin4 or 2 now and recasting enkindle one  gcd later
                        SmnStateIds.FirstBahamutWeave,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => GcdLeft < 1400, () => SmUtil.NoOp(),  SmnStateIds.SecondBahamutWeave, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.EnkindleBahamut, Core.Me.CurrentTarget), SmnStateIds.SecondBahamutWeave),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.Fester, Core.Me.CurrentTarget), SmnStateIds.SecondBahamutWeave),
                            })
                    },
                    {
                        SmnStateIds.SecondBahamutWeave,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => GcdLeft < 700,        () => SmUtil.NoOp(),    SmnStateIds.BahamutRuinUntilSecondEnkindle, TransitionType.Immediate),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.EnkindleBahamut, Core.Me.CurrentTarget), SmnStateIds.BahamutRuinUntilSecondEnkindle),
                                new StateTransition<SmnStateIds>(() => true,  () => SmUtil.SyncedCast(Spells.Fester, Core.Me.CurrentTarget), SmnStateIds.BahamutRuinUntilSecondEnkindle),
                            })
                    },
                    {
                        //TODO What happens if bahamut goes away before we finish these
                        SmnStateIds.BahamutRuinUntilSecondEnkindle,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => Spells.EnkindleBahamut.Cooldown.TotalMilliseconds > 1750,  () => SmUtil.SyncedCast(Spells.Ruin3, Core.Me.CurrentTarget), SmnStateIds.BahamutRuinUntilSecondEnkindle),
                                new StateTransition<SmnStateIds>(() => Spells.EnkindleBahamut.Cooldown.TotalMilliseconds <= 1750,  () => SmUtil.NoOp(), SmnStateIds.BahamutSecondEnkindle, TransitionType.Immediate),
                            })
                    },
                    {
                        SmnStateIds.BahamutSecondEnkindle,
                        new State<SmnStateIds>(
                            new List<StateTransition<SmnStateIds>>()
                            {
                                new StateTransition<SmnStateIds>(() => Spells.EnkindleBahamut.Cooldown == TimeSpan.Zero,  () => SmUtil.SyncedCast(Spells.Ruin4, Core.Me.CurrentTarget), SmnStateIds.BahamutSecondEnkindle),
                            })
                    },
                }) ;
            StateMachineManager.RegisterStateMachine(mStateMachine);
        }

        public static async Task<bool> Rest()
        {
            if (Core.Me.CurrentHealthPercent > 70 || Core.Me.ClassLevel < 4)
                return false;

            return await Spells.Physick.Heal(Core.Me);
        }

        public static async Task<bool> PreCombatBuff()
        {


            if (Core.Me.IsCasting)
                return true;

            await Casting.CheckForSuccessfulCast();
            SpellQueueLogic.SpellQueue.Clear();

            return await Pets.Summon();
        }

        public static async Task<bool> Pull()
        {
            if (BotManager.Current.IsAutonomous)
            {
                if (Core.Me.HasTarget)
                {
                    Movement.NavigateToUnitLos(Core.Me.CurrentTarget, 20);
                }

                return await Spells.SmnBio.CastAura(Core.Me.CurrentTarget, Auras.Bio);
            }

            return await Combat();
        }
        public static async Task<bool> Heal()
        {
            if (Core.Me.IsMounted)
                return true;

            if (await Casting.TrackSpellCast()) return true;
            await Casting.CheckForSuccessfulCast();

            Casting.DoHealthChecks = false;

            if (await GambitLogic.Gambit()) return true;
            if (await Logic.Summoner.Heal.Raise()) return true;
            //Force Toggles:
            if (await Logic.Summoner.Heal.ForceRaise()) return true;
            if (await Logic.Summoner.Heal.ForceHardRaise()) return true;
            //Force Toggles End.

            return await Logic.Summoner.Heal.Physick();

        }
        public static async Task<bool> CombatBuff()
        {
            return false;
        }
        public static async Task<bool> Combat()
        {
            if (BotManager.Current.IsAutonomous)
            {
                if (Core.Me.HasTarget)
                {
                    Movement.NavigateToUnitLos(Core.Me.CurrentTarget, 20);
                }
            }

            if (!Core.Me.HasTarget || !Core.Me.CurrentTarget.ThoroughCanAttack())
                return false;

            //Logger.Write("Aetherflow Count: " + MagitekActionResourceManager.Arcanist.Aetherflow);
            //Logger.Write("Can Trance: " + MagitekActionResourceManager.Arcanist.CanTrance);
            //Logger.Write("In Trance: " + MagitekActionResourceManager.Arcanist.CanTrance);

            if (await CustomOpenerLogic.Opener()) return true;
            
            return await mStateMachine.Pulse();

            if (!SpellQueueLogic.SpellQueue.Any()) SpellQueueLogic.InSpellQueue = false;

            if (SpellQueueLogic.SpellQueue.Any()) if (await SpellQueueLogic.SpellQueueMethod()) return true;

            if (Core.Me.CurrentTarget.HasAura(Auras.MagicResistance))
                return false;

            if (Core.Me.CurrentTarget.HasAnyAura(Auras.Invincibility))
                return false;

            //if (await SingleTarget.Ruin4MaxStacks()) return true;


            if (await Aoe.Bane()) return true;
            if (await Buff.DreadwyrmTrance()) return true;
            if (await SingleTarget.EnkindleBahamut()) return true;
            if (await Pets.SummonBahamut()) return true;
            if (await SingleTarget.Deathflare()) return true;
            if (await SingleTarget.TriDisaster()) return true;
            if (await Pets.Summon()) return true;
            if (await Buff.LucidDreaming()) return true;
            if (await SingleTarget.Enkindle()) return true;

            if (await Aoe.Painflare()) return true;
            if (await SingleTarget.Fester()) return true;
            if (await Aoe.EnergySiphon()) return true;
            if (await SingleTarget.EnergyDrain()) return true;

            if (await SingleTarget.Miasma()) return true;
            if (await SingleTarget.Bio()) return true;
            if (await SingleTarget.EgiAssault2()) return true;
            if (await SingleTarget.EgiAssault()) return true;
            if (await Aoe.Outburst()) return true;
            if (await SingleTarget.Ruin4()) return true;
            return await SingleTarget.Ruin();
        }
        public static async Task<bool> PvP()
        {
            return false;
        }
    }
}
