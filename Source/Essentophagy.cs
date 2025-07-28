using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using System.Linq;
using System;

namespace Recatek.Essentophagy
{
    public static class EssentophagyUtil
    {
        public static List<Trait> GetSuitableTraits(Pawn invoker, Pawn target)
        {
            List<Trait> suitable = new List<Trait>();

            if (invoker == null || target == null)
            {
                return suitable;
            }

            foreach (Trait trait in target.story.traits.allTraits)
            {
                if (invoker.IsTraitSuitable(trait))
                    suitable.Add(trait);
            }

            return suitable;
        }

        private static bool IsTraitSuitable(this Pawn pawn, Trait trait)
        {
            List<WorkTypeDef> requiredWorkTypes = trait.def.requiredWorkTypes;
            WorkTags requiredWorkTags = trait.def.requiredWorkTags;

            if (trait.suppressedByTrait || trait.Suppressed || trait.sourceGene != null)
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

        public override List<PsychicRitualToil> CreateToils(PsychicRitual psychicRitual, PsychicRitualGraph parent)
        {
            List<PsychicRitualToil> list = base.CreateToils(psychicRitual, parent);
            list.Add(new PsychicRitualToil_Essentophagy(InvokerRole, TargetRole, brainDamageRange));
            list.Add(new PsychicRitualToil_TargetCleanup(InvokerRole, TargetRole));
            return list;
        }

        public override IEnumerable<string> BlockingIssues(PsychicRitualRoleAssignments assignments, Map map)
        {
            foreach (string blocker in base.BlockingIssues(assignments, map))
                yield return blocker;

            Pawn invoker = assignments.FirstAssignedPawn(invokerRole);
            Pawn target = assignments.FirstAssignedPawn(targetRole);

            List<Trait> suitableTraits = EssentophagyUtil.GetSuitableTraits(invoker, target);
            if (suitableTraits.Count == 0)
                yield return "recatek.essentophagy.noEligibleTrait".Translate(invoker.Named("INVOKER"));
        }

        public override TaggedString OutcomeDescription(FloatRange qualityRange, string qualityNumber, PsychicRitualRoleAssignments assignments)
        {
            CalculateMaxPower(assignments, new List<QualityFactor>(), out float power);
            string percentChance = successChanceFromQualityCurve.Evaluate(power).ToStringPercent();

            List<Trait> suitableTraits = EssentophagyUtil.GetSuitableTraits(
                assignments.FirstAssignedPawn(InvokerRole),
                assignments.FirstAssignedPawn(TargetRole));

            string addendum = "";
            if (suitableTraits.Count > 0)
            {
                string traits = suitableTraits.Select(t => t.LabelCap).ToCommaList();
                if (suitableTraits.Count == 0)
                    addendum = "recatek.essentophagy.eligibleTraits.single".Translate(traits);
                else
                    addendum = "recatek.essentophagy.eligibleTraits.multiple".Translate(traits);
            }

            return outcomeDescription.Formatted(percentChance, addendum);
        }

        public override IEnumerable<string> GetPawnTooltipExtras(Pawn pawn)
        {
            if (pawn.story.traits != null)
            {
                string traitsList = "";
                foreach (Trait trait in pawn.story.traits.allTraits)
                {
                    if (!trait.suppressedByTrait && !trait.Suppressed && trait.sourceGene == null)
                        traitsList += "\n      - " + trait.LabelCap;
                }

                if (traitsList.Length > 0)
                {
                    yield return "recatek.essentophagy.availableTraits".Translate(traitsList);
                }
            }
        }
    }

    public class PsychicRitualToil_Essentophagy : PsychicRitualToil
    { 
        public PsychicRitualRoleDef invokerRole;
        public PsychicRitualRoleDef targetRole;

        public FloatRange brainDamageRange;
        public float successChance;

        protected PsychicRitualToil_Essentophagy() 
        {
            // Pass
        }

        public PsychicRitualToil_Essentophagy(PsychicRitualRoleDef invokerRole, PsychicRitualRoleDef targetRole, FloatRange brainDamageRange)
        {
            this.invokerRole = invokerRole;
            this.targetRole = targetRole;
            this.brainDamageRange = brainDamageRange;
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
            // Apply psychic shock
            Hediff hediff = HediffMaker.MakeHediff(HediffDefOf.DarkPsychicShock, target);
            target.health.AddHediff(hediff);

            // Add memories and thoughts
            target.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.PsychicRitualVictim);
            foreach (Pawn pawn in psychicRitual.assignments.AllAssignedPawns.Except(target))
                target.needs?.mood?.thoughts?.memories?.TryGainMemory(ThoughtDefOf.UsedMeForPsychicRitual, pawn);

            // Get the success chance
            PsychicRitualDef_Essentophagy psychicRitualDef_Essentophagy = (PsychicRitualDef_Essentophagy)psychicRitual.def;
            successChance = psychicRitualDef_Essentophagy.successChanceFromQualityCurve.Evaluate(psychicRitual.PowerPercent);
            bool succeeded = Rand.Chance(successChance);

            // Apply brain damage and give guilt to map pawns
            DamageBrain(invoker, target);
            PsychicRitualUtility.AddPsychicRitualGuiltToPawns(
                psychicRitual.def, 
                psychicRitual.Map.mapPawns.FreeColonistsSpawned.Where((Pawn p) => p != target));

            // Fail due to missing traits
            Trait traitToSteal = SelectTraitToSteal(invoker, target);
            if (traitToSteal == null)
            {
                DamageBrain(invoker, target);

                string failed = "recatek.essentophagy.outcome.noEligibleTrait".Translate(
                    target.Named("TARGET"), 
                    target.Named("INVOKER"));
                failed += target.Dead
                    ? "\n\n" + "PsychicRitualTargetBrainLiquified".Translate(target.Named("TARGET"))
                    : "\n\n" + "PhilophagyDamagedTarget".Translate(target.Named("TARGET"));
                Find.LetterStack.ReceiveLetter(
                    "PsychicRitualCompleteLabel".Translate(psychicRitual.def.label),
                    failed,
                    LetterDefOf.NeutralEvent,
                    new LookTargets(invoker, target));

                return;
            }

            // Fail due to random chance (after failing due to missing traits, for clarity)
            if (succeeded == false)
            {
                string failed = "recatek.essentophagy.outcome.failed".Translate(
                    target.Named("TARGET"));
                failed += target.Dead 
                    ? "\n\n" + "PsychicRitualTargetBrainLiquified".Translate(target.Named("TARGET"))
                    : "\n\n" + "PhilophagyDamagedTarget".Translate(target.Named("TARGET"));
                Find.LetterStack.ReceiveLetter(
                    "PsychicRitualCompleteLabel".Translate(psychicRitual.def.label),
                    failed,
                    LetterDefOf.NeutralEvent,
                    new LookTargets(invoker, target));

                return;
            }

            // Steal the trait
            string stolenTraitName = traitToSteal.LabelCap;
            ExtractTrait(target, traitToSteal);
            InstallTrait(invoker, traitToSteal);

            // Report success
            string completed = "recatek.essentophagy.outcome.completed".Translate(
                invoker.Named("INVOKER"), 
                target.Named("TARGET"), 
                stolenTraitName);
            completed += target.Dead
                ? "\n\n" + "PsychicRitualTargetBrainLiquified".Translate(target.Named("TARGET"))
                : "\n\n" + "PhilophagyDamagedTarget".Translate(target.Named("TARGET"));
            Find.LetterStack.ReceiveLetter(
                "PsychicRitualCompleteLabel".Translate(psychicRitual.def.label),
                completed,
                LetterDefOf.NeutralEvent,
                new LookTargets(invoker, target));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref invokerRole, "invokerRole");
            Scribe_Defs.Look(ref targetRole, "targetRole");
            Scribe_Values.Look(ref brainDamageRange, "brainDamageRange");
            Scribe_Values.Look(ref successChance, "successChance", 0);
        }

        private void DamageBrain(Pawn invoker, Pawn target)
        {
            BodyPartRecord brain = target.health.hediffSet.GetBrain();

            if (brain != null)
            {
                target.TakeDamage(new DamageInfo(DamageDefOf.Psychic, brainDamageRange.RandomInRange, 0f, -1f, null, brain));
            }

            if (target.Dead)
            {
                PsychicRitualUtility.RegisterAsExecutionIfPrisoner(target, invoker);
            }
        }
        
        private static Trait SelectTraitToSteal(Pawn invoker, Pawn target)
        {
            List<Trait> targetTraits = EssentophagyUtil.GetSuitableTraits(invoker, target);

            if (targetTraits.Count == 0)
            {
                return null;
            }

            return targetTraits.RandomElement();
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
