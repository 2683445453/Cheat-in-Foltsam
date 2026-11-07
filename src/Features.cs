using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace FlotsamTrainer
{
    internal static class Features
    {
        internal static bool Immortal;
        internal static bool FastLevel;
        internal static float ExperienceMultiplier = 10f;
        internal static bool FastResearch;
        internal static float ResearchMultiplier = 5f;
        internal static bool NoMeals;
        internal static bool UnlimitedWeight;
        internal static bool UnlimitedPower;

        private static bool IsPlayer(Agent agent) =>
            agent != null && agent.Community != null && agent.Community == Community.PlayerCommunity;

        internal static void SetFreeBuild(bool active)
        {
            BuildingDevTools.InstantBuild = active;
            Community.PlayerCommunity?.UpdateBuildables();
        }

        internal static void SetUnlimitedWeight(bool active)
        {
            UnlimitedWeight = active;
            Community.PlayerCommunity?.UpdateBuildables();
        }

        private static bool HasUnlimitedPower(EnergyGrid grid)
        {
            if (!UnlimitedPower)
                return false;

            var town = Community.PlayerCommunity;
            if (town == null)
                return false;

            if (town.Engine != null && town.Engine.Connector?.EnergyGrid == grid)
                return true;

            foreach (var link in grid.Links)
            {
                if (link is EnergyGridBuildableComponent building && building != null &&
                    building.Buildable != null && building.Buildable.Community == town)
                    return true;
            }
            return false;
        }

        private static bool HasUnlimitedTravelPower(Engine engine) =>
            UnlimitedPower && engine != null && engine == Community.PlayerCommunity?.Engine;

        [HarmonyPatch(typeof(Engine), nameof(Engine.CanTug))]
        private static class TugLimit
        {
            private static void Postfix(ref bool __result)
            {
                if (UnlimitedWeight && Community.PlayerCommunity?.Engine != null)
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(EnergyGrid), nameof(EnergyGrid.CalculateEfficiency))]
        private static class RechargeGrid
        {
            private static void Prefix(EnergyGrid __instance)
            {
                if (!HasUnlimitedPower(__instance))
                    return;

                var capacity = __instance.ReturnStorageCapacity();
                if (__instance.ReturnStorageEnergy() < capacity)
                {
                    var added = __instance.FillStorageEnergy(capacity);
                    if (added > 0f)
                        new EnergyEvent(GameEventType.EnergyProduced, added).Dispatch();
                }
            }
        }

        [HarmonyPatch(typeof(EnergyGrid), nameof(EnergyGrid.ReturnEnergyProduction))]
        private static class SupplyGrid
        {
            private static void Postfix(EnergyGrid __instance, ref float __result)
            {
                if (HasUnlimitedPower(__instance))
                    // A small idle supply lets facilities start even on grids without batteries.
                    __result = Mathf.Max(__result, Mathf.Max(1f, __instance.ReturnEnergyConsumption()));
            }
        }

        [HarmonyPatch(typeof(Engine), nameof(Engine.ReturnEnergyCost))]
        private static class TravelCost
        {
            private static void Postfix(Engine __instance, ref float __result)
            {
                if (HasUnlimitedTravelPower(__instance))
                    __result = 0f;
            }
        }

        [HarmonyPatch(typeof(Engine), nameof(Engine.ConsumeEnergy))]
        private static class TravelConsumption
        {
            private static bool Prefix(Engine __instance, ref bool __result)
            {
                if (!HasUnlimitedTravelPower(__instance))
                    return true;

                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Engine), nameof(Engine.ReturnEnergyRange), new System.Type[] { })]
        private static class TravelRange
        {
            private static bool Prefix(Engine __instance, ref float __result)
            {
                if (!HasUnlimitedTravelPower(__instance))
                    return true;

                // Keep the range indicator's transform finite.
                __result = 100000f;
                return false;
            }
        }

        [HarmonyPatch(typeof(Engine), nameof(Engine.ReturnMoveableDistance))]
        private static class TravelCooldown
        {
            private static void Postfix(Engine __instance, float desiredDistance, ref float __result)
            {
                if (HasUnlimitedTravelPower(__instance))
                    __result = desiredDistance;
            }
        }

        internal static void MaxAttributes()
        {
            var town = Community.PlayerCommunity;
            if (town == null)
                return;

            foreach (var agent in town.Agents)
            {
                var attributes = agent.Attributes;
                if (!agent.IsAlive || attributes == null)
                    continue;

                foreach (var attribute in attributes.Attributes)
                {
                    if (attribute.Expertise < attributes.MaximumAttributeLevel)
                        attribute.Expertise = attributes.MaximumAttributeLevel;
                }
                attributes.AttributesUpdatedEvent.Invoke();
            }
        }

        internal static void TreatAllDrifters()
        {
            var town = Community.PlayerCommunity;
            if (town == null)
                return;

            foreach (var agent in town.Agents)
            {
                if (agent == null || !agent.IsAlive || agent.Vitals == null)
                    continue;

                var pollution = agent.Vitals.Pollution;
                if (pollution == null)
                    continue;

                var disease = pollution.CurrentDisease;
                if (disease != null)
                {
                    var pod = pollution.CurrentDiseaseMedPod;
                    var occupied = pod != null && pod.OccupyingPatient == agent;
                    disease.FinishDisease(agent);
                    if (pod != null)
                        pod.Unreserve(agent);
                    if (occupied)
                    {
                        // Mirror the slot cleanup normally done when a doctor finishes treatment.
                        ReleaseMedicalSlot(pod, nameof(MedPod.OccupyingPatient), pod.OccupyingPatient);
                        ReleaseMedicalSlot(pod, nameof(MedPod.Doctor), pod.Doctor);
                    }
                    pollution.CurrentDiseaseMedPod = null;
                }
                pollution.Reset();
            }
        }

        private static void ReleaseMedicalSlot(MedPod pod, string slot, Agent agent)
        {
            if (agent == null)
                return;

            AccessTools.PropertySetter(typeof(MedPod), slot).Invoke(pod, new object[] { null });
            if (agent.transform.IsChildOf(pod.transform))
            {
                agent.ReturnNavigator().AttachToTarget(pod.Obstalce);
                agent.UpdateActivity(Activity.Idling);
            }
        }

        internal static void ClearMeals(Vitals vitals)
        {
            vitals.Hunger.Project?.Stop(ProjectFlags.Cancelled);
            vitals.Thirst.Project?.Stop(ProjectFlags.Cancelled);
            vitals.UnreserveItemToConsume(VitalType.Hunger, VitalType.Thirst);
            vitals.Hunger.Reset();
            vitals.Thirst.Reset();
        }

        [HarmonyPatch(typeof(Agent), nameof(Agent.KillAgent))]
        private static class AgentDeath
        {
            private static bool Prefix(Agent __instance)
            {
                if (!Immortal || !IsPlayer(__instance))
                    return true;

                // Boat removal can call KillAgent while the captain is parented to the boat.
                var boat = __instance.Boat;
                if (boat != null && boat.Captain == __instance)
                {
                    if (boat.CurrentMooringPoint != null)
                        boat.Disembark(__instance, disembarkPassengers: true);
                    else
                    {
                        var navigator = __instance.ReturnNavigator(alwaysReturnDrifter: true);
                        var position = boat.transform.localPosition + UnityEngine.Vector3.left *
                            (boat.Navigator.Radius + navigator.Radius + 1f);
                        navigator.PlaceAt(position, overrideCheck: true, placeOnObstacle: false);
                        boat.ClearCaptain();
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(Vitals), nameof(Vitals.KillAgent))]
        private static class VitalDeath
        {
            private static bool Prefix(Vitals __instance)
            {
                if (!Immortal || !IsPlayer(__instance.Agent))
                    return true;

                while (__instance.Hunger.IsCauseOfDeath())
                    __instance.Hunger.DecreaseAmount();
                while (__instance.Thirst.IsCauseOfDeath())
                    __instance.Thirst.DecreaseAmount();
                return false;
            }
        }

        [HarmonyPatch(typeof(Vitals), nameof(Vitals.TryReserveItemToConsume))]
        private static class ReserveMeal
        {
            private static bool Prefix(Vitals __instance, ref bool __result)
            {
                if (!NoMeals || !IsPlayer(__instance.Agent))
                    return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Vitals), nameof(Vitals.TryAddConsumeProject))]
        private static class StartMeal
        {
            private static bool Prefix(Vitals __instance, ref bool __result)
            {
                if (!NoMeals || !IsPlayer(__instance.Agent))
                    return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(DrifterAttributes), nameof(DrifterAttributes.AddTotalExperience))]
        private static class Experience
        {
            private static void Prefix(Agent ____agent, ref float experience)
            {
                if (FastLevel && IsPlayer(____agent) && experience > 0f)
                    experience *= ExperienceMultiplier;
            }
        }

        [HarmonyPatch(typeof(ResearchStation), nameof(ResearchStation.Research))]
        private static class ResearchSpeed
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var deltaTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.deltaTime));
                var scaledDeltaTime = AccessTools.Method(typeof(ResearchSpeed), nameof(ScaledDeltaTime));
                var matcher = new CodeMatcher(instructions)
                    .MatchStartForward(new CodeMatch(instruction => instruction.Calls(deltaTime)))
                    .ThrowIfInvalid("ResearchStation.Research no longer reads Time.deltaTime.");

                // Replace only the per-frame time input, preserving costs and completion logic.
                return matcher.SetInstruction(new CodeInstruction(OpCodes.Ldarg_1)
                        .MoveLabelsFrom(matcher.Instruction).MoveBlocksFrom(matcher.Instruction))
                    .Advance(1)
                    .Insert(new CodeInstruction(OpCodes.Call, scaledDeltaTime))
                    .InstructionEnumeration();
            }

            private static float ScaledDeltaTime(Agent agent)
            {
                return Time.deltaTime * (FastResearch && IsPlayer(agent) ? ResearchMultiplier : 1f);
            }
        }
    }
}
