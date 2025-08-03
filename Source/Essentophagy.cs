using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Recatek.Essentophagy
{
    public static class EssentophagyUtil
    {
        public static bool IsStealable(this Trait trait)
        {
            return (trait.suppressedByTrait == false)
                && (trait.Suppressed == false)
                && (trait.sourceGene == null)
                // No creepjoiner traits
                && (trait.def != TraitDefOf.Occultist)
                && (trait.def != TraitDefOf.Joyous)
                && (trait.def != TraitDefOf.PerfectMemory)
                && (trait.def != TraitDefOf.Disturbing)
                && (trait.def != TraitDefOf.VoidFascination)
                && (trait.def != TraitDef.Named("BodyMastery"));
        }

        public static bool IsDroppable(this Trait trait)
        {
            return trait.sourceGene == null;
        }

        public static List<Trait> GetCompatibleTraits(Pawn invoker, Pawn target)
        {
            List<Trait> compatible = new List<Trait>();

            if (invoker == null || target == null)
            {
                return compatible;
            }

            foreach (Trait trait in target.story.traits.allTraits)
            {
                if (invoker.IsTraitCompatible(trait))
                    compatible.Add(trait);
            }

            return compatible;
        }

        private static bool IsTraitCompatible(this Pawn pawn, Trait trait)
        {
            List<WorkTypeDef> requiredWorkTypes = trait.def.requiredWorkTypes;
            WorkTags requiredWorkTags = trait.def.requiredWorkTags;

            if (trait.IsStealable() == false)
                return false; // Can't grab the trait
            if (pawn.HasConflictingTraits(trait))
                return false; // Pawn has a conflicting trait
            if (pawn.AnyWorkTypesDisabled(requiredWorkTypes) || pawn.AnyWorkTagsDisabled(requiredWorkTags))
                return false; // Pawn has a disabled work type that the trait requires

            // Special case for same-degree traits
            Trait existing = pawn.story.traits.GetTrait(trait.def);
            if (existing != null)
            {
                if (existing.Degree == trait.Degree)
                    return false; // We already have this trait at the same degree
                if (existing.sourceGene != null)
                    return false; // Don't remove this trait since it'll rip the gene out
            }

            return true;
        }

        private static bool AnyWorkTypesDisabled(this Pawn pawn, List<WorkTypeDef> requiredWorkTypes)
        {
            foreach (WorkTypeDef requiredWorkType in requiredWorkTypes)
            {
                if (pawn.WorkTypeIsDisabled(requiredWorkType))
                    return true;
            }
            return false;
        }

        private static bool AnyWorkTagsDisabled(this Pawn pawn, WorkTags requiredWorkTags)
        {
            return pawn.WorkTagIsDisabled(requiredWorkTags);
        }

        private static bool HasConflictingTraits(this Pawn pawn, Trait trait)
        {
            foreach (TraitDef conflictingTrait in trait.def.conflictingTraits)
            {
                if (pawn.story.traits.HasTrait(conflictingTrait))
                    return true;
            }
            return false;
        }
    }

    public class PsychicRitualDef_Essentophagy : PsychicRitualDef_InvocationCircle
    {
        public FloatRange brainDamageRange;
        public SimpleCurve successChanceFromQualityCurve;
        public SimpleCurve breakdownSeverityCurve;

        public override List<PsychicRitualToil> CreateToils(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            List<PsychicRitualToil> list = base.CreateToils(psychicRitual, parent);
            list.Add(new PsychicRitualToil_Essentophagy(InvokerRole, TargetRole));
            return list;
        }

        public override IEnumerable<string> BlockingIssues(PsychicRitualRoleAssignments assignments, Map map)
        {
            foreach (string blocker in base.BlockingIssues(assignments, map))
                yield return blocker;

            Pawn invoker = assignments.FirstAssignedPawn(invokerRole);
            Pawn target = assignments.FirstAssignedPawn(targetRole);

            List<Trait> compatibleTraits = EssentophagyUtil.GetCompatibleTraits(invoker, target);
            if (compatibleTraits.Count == 0)
                yield return "recatek.essentophagy.noCompatibleTrait".Translate(invoker.Named("INVOKER"));
        }

        public override TaggedString OutcomeDescription(FloatRange qualityRange, string qualityNumber, PsychicRitualRoleAssignments assignments)
        {
            CalculateMaxPower(assignments, new List<QualityFactor>(), out float power);
            string percentChance = successChanceFromQualityCurve.Evaluate(power).ToStringPercent();

            List<Trait> compatibleTraits = EssentophagyUtil.GetCompatibleTraits(
                assignments.FirstAssignedPawn(InvokerRole),
                assignments.FirstAssignedPawn(TargetRole));

            string addendum = "";
            if (compatibleTraits.Count > 0)
            {
                string traits = compatibleTraits.Select(t => t.LabelCap).ToCommaList();
                if (compatibleTraits.Count == 0)
                    addendum = "recatek.essentophagy.compatibleTraits.single".Translate(traits);
                else
                    addendum = "recatek.essentophagy.compatibleTraits.multiple".Translate(traits);
            }

            return outcomeDescription.Formatted(percentChance, addendum);
        }

        public override IEnumerable<string> GetPawnTooltipExtras(Pawn pawn)
        {
            if (pawn.story.traits != null)
            {
                string traitList = "";
                foreach (Trait trait in pawn.story.traits.allTraits)
                {
                    if (trait.IsStealable())
                        traitList += "\n      - " + trait.LabelCap;
                }

                if (traitList.Length > 0)
                {
                    yield return "recatek.essentophagy.stealableTraits".Translate(traitList);
                }
            }
        }
    }

    public class PsychicRitualToil_Essentophagy : PsychicRitualToil
    { 
        public PsychicRitualRoleDef invokerRole;
        public PsychicRitualRoleDef targetRole;

        public float successChance = 0f;
        public float breakdownSeverity = 0f;

        protected PsychicRitualToil_Essentophagy() 
        {
            // Pass
        }

        public PsychicRitualToil_Essentophagy(PsychicRitualRoleDef invokerRole, PsychicRitualRoleDef targetRole)
        {
            this.invokerRole = invokerRole;
            this.targetRole = targetRole;
        }

        public override void Start(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            Pawn invoker = psychicRitual.assignments.FirstAssignedPawn(invokerRole);
            Pawn target = psychicRitual.assignments.FirstAssignedPawn(targetRole);

            if (invoker == null || target == null)
                return;
            ApplyOutcome(psychicRitual, invoker, target);
        }

        private void ApplyOutcome(PsychicRitual psychicRitual, Pawn invoker, Pawn target)
        {
            // Get the success chance
            PsychicRitualDef_Essentophagy psychicRitualDef_Essentophagy = (PsychicRitualDef_Essentophagy)psychicRitual.def;
            successChance = psychicRitualDef_Essentophagy.successChanceFromQualityCurve.Evaluate(psychicRitual.PowerPercent);

            // Get the breakdown severity
            float breakdownProb = Rand.Range(0.0f, Mathf.Max(1.0f - psychicRitual.PowerPercent, 0.0f));
            breakdownSeverity = Mathf.Ceil(psychicRitualDef_Essentophagy.breakdownSeverityCurve.Evaluate(breakdownProb));

            // Determine success or failure
            bool succeeded = Rand.Chance(successChance);

            // Fail due to missing traits
            Trait traitToSteal = SelectTraitToSteal(invoker, target);
            if (traitToSteal == null)
            {
                string noTrait = "recatek.essentophagy.outcome.noCompatibleTrait".Translate(target.Named("TARGET"), target.Named("INVOKER"));
                ApplyConsquences(psychicRitual, invoker, target, noTrait);
                return;
            }

            // Fail due to random chance (after failing due to missing traits, for clarity)
            if (succeeded == false)
            {
                string failure = "recatek.essentophagy.outcome.failed".Translate(target.Named("TARGET"));
                ApplyConsquences(psychicRitual, invoker, target, failure);
                return;
            }

            // Steal the trait
            string stolenTraitName = traitToSteal.LabelCap;
            string droppedTraitName = TryDropTrait(invoker, maxTraits: 4);
            ExtractTrait(target, traitToSteal);
            InstallTrait(invoker, traitToSteal);

            // Report success
            string success = "recatek.essentophagy.outcome.completed".Translate(invoker.Named("INVOKER"), target.Named("TARGET"), stolenTraitName);
            success += (droppedTraitName != null)
                ? "recatek.essentophagy.outcome.droppedTrait".Translate(invoker.Named("INVOKER"), droppedTraitName)
                : TaggedString.Empty;

            ApplyConsquences(psychicRitual, invoker, target, success);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref invokerRole, "invokerRole");
            Scribe_Defs.Look(ref targetRole, "targetRole");
            Scribe_Values.Look(ref successChance, "successChance", 0);
            Scribe_Values.Look(ref breakdownSeverity, "breakdownSeverity", 0f);
        }

        private void ApplyConsquences(PsychicRitual psychicRitual, Pawn invoker, Pawn target, string outcome)
        {
            // Add guilt
            PsychicRitualUtility.AddPsychicRitualGuiltToPawns(
                psychicRitual.def,
                psychicRitual.Map.mapPawns.FreeColonistsSpawned.Where((Pawn p) => p != target));

            // Add memories and thoughts
            target.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.PsychicRitualVictim);
            foreach (Pawn pawn in psychicRitual.assignments.AllAssignedPawns.Except(target))
                target.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.UsedMeForPsychicRitual, pawn);

            // Apply brain damage and psychic shock to target
            DamageBrain(psychicRitual, invoker, target);
            Hediff targetShock = HediffMaker.MakeHediff(HediffDefOf.DarkPsychicShock, target);
            target.health.AddHediff(targetShock);

            // Apply ritual sickness to the target
            Hediff invokerSickness = HediffMaker.MakeHediff(HediffDef.Named("recatek_RitualSickness"), invoker);
            invoker.health.AddHediff(invokerSickness);

            // Damage relations with the target's home faction, if applicable
            bool affectGoodWill =
                invoker.IsPlayerControlled
                && target.Faction != null
                && target.HomeFaction != null
                && target.HomeFaction != Faction.OfPlayer
                && target.HomeFaction.def.humanlikeFaction
                && !target.Faction.def.PermanentlyHostileTo(FactionDefOf.PlayerColony);
            if (affectGoodWill)
            {
                target.HomeFaction.TryAffectGoodwillWith(
                    Faction.OfPlayer,
                    -50,
                    canSendMessage: true,
                    canSendHostilityLetter: true,
                    HistoryEventDefOf.WasPsychicRitualTarget);
            }

            QuestUtility.SendQuestTargetSignals(
                target.questTags,
                "PsychicRitualTarget",
                target.Named("SUBJECT"));

            outcome += target.Dead
                ? "\n\n" + "PsychicRitualTargetBrainLiquified".Translate(target.Named("TARGET"))
                : "\n\n" + "PhilophagyDamagedTarget".Translate(target.Named("TARGET"));
            Find.LetterStack.ReceiveLetter(
                "PsychicRitualCompleteLabel".Translate(psychicRitual.def.label),
                outcome,
                LetterDefOf.NeutralEvent,
                new LookTargets(invoker, target));

            if (breakdownSeverity > 0.0f)
            {
                Hediff invokerBreakdown = HediffMaker.MakeHediff(HediffDef.Named("PsychicBreakdown"), invoker);
                invokerBreakdown.Severity = Mathf.Ceil(breakdownSeverity);
                invoker.health.AddHediff(invokerBreakdown);

                Find.LetterStack.ReceiveLetter(
                    "recatek.essentophagy.breakdown.label".Translate(invoker.Named("INVOKER")),
                    "recatek.essentophagy.breakdown.message".Translate(invoker.Named("INVOKER")),
                    LetterDefOf.NeutralEvent,
                    new LookTargets(invoker));
            }
        }

        private void DamageBrain(PsychicRitual psychicRitual, Pawn invoker, Pawn target)
        {
            BodyPartRecord brain = target.health.hediffSet.GetBrain();

            if (brain != null)
            {
                PsychicRitualDef_Essentophagy psychicRitualDef_Essentophagy = (PsychicRitualDef_Essentophagy)psychicRitual.def;
                target.TakeDamage(
                    new DamageInfo(
                        DamageDefOf.Psychic, 
                        psychicRitualDef_Essentophagy.brainDamageRange.RandomInRange, 
                        0f, 
                        -1f, 
                        null, 
                        brain));
            }

            if (target.Dead)
            {
                PsychicRitualUtility.RegisterAsExecutionIfPrisoner(target, invoker);
            }
        }
        
        private static Trait SelectTraitToSteal(Pawn invoker, Pawn target)
        {
            List<Trait> targetTraits = EssentophagyUtil.GetCompatibleTraits(invoker, target);

            if (targetTraits.Count == 0)
            {
                return null;
            }

            return targetTraits.RandomElement();
        }

        private static string TryDropTrait(Pawn pawn, int maxTraits)
        { 
            List<Trait> traitsToDrop = new List<Trait>();
            foreach (Trait trait in pawn.story.traits.allTraits)
            {
                if (trait.IsDroppable())
                {
                    traitsToDrop.Add(trait);
                }
            }

            if ((traitsToDrop.Count + 1) <= maxTraits)
            {
                return null;
            }

            Trait dropped = traitsToDrop.RandomElement();
            ExtractTrait(pawn, dropped);

            return dropped.LabelCap;
        }

        private static void ExtractTrait(Pawn pawn, Trait trait)
        {
            pawn.story.traits.RemoveTrait(trait, true);

            List<SkillGain> skillGains = trait.CurrentData.skillGains;
            if (skillGains != null && pawn.skills != null)
            {
                foreach (SkillGain skillGain in skillGains)
                {
                    SkillRecord skill = pawn.skills.GetSkill(skillGain.skill);
                    if (skill != null && !skill.PermanentlyDisabled)
                    {
                        int num = skill.GetLevel(false) - skillGain.amount;
                        skill.Level = num < 0 ? 0 : num;
                    }
                }
            }

            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
        }

        private static void InstallTrait(Pawn pawn, Trait trait)
        {
            // Special case: clobber same-trait/different-degree traits
            for (int i = pawn.story.traits.allTraits.Count - 1; i >= 0; i--)
            {
                Trait existingTrait = pawn.story.traits.allTraits[i];

                if (existingTrait.def == trait.def && existingTrait.Degree != trait.Degree)
                {
                    pawn.story.traits.RemoveTrait(existingTrait, true);
                }
            }

            pawn.story.traits.GainTrait(trait, true);

            List<SkillGain> skillGains = trait.CurrentData.skillGains;
            if (skillGains != null && pawn.skills != null)
            {
                foreach (SkillGain skillGain in skillGains)
                {
                    SkillRecord skill = pawn.skills.GetSkill(skillGain.skill);
                    if (skill != null && !skill.PermanentlyDisabled)
                    {
                        skill.Level = skill.GetLevel(false) + skillGain.amount;
                    }
                }
            }

            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
        }
    }
}
