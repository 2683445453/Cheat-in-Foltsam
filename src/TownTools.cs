using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using PajamaLlama.Fltsm;
using PajamaLlama.Flotsam.Morale;
using UnityEngine;

namespace FlotsamTrainer
{
    internal static class TownTools
    {
        internal static bool FreeCrafting;
        internal static bool PollutionProof;
        internal static bool FastWorkers;
        internal static float WorkerMultiplier = 2f;
        internal static bool HappyWorkers;
        internal static bool FastTownShip;
        internal static float TownShipMultiplier = 2f;
        internal static bool FastSalvageBoats;
        internal static float SalvageBoatMultiplier = 2f;
        internal static bool FastFishingBoats;
        internal static float FishingBoatMultiplier = 2f;
        internal static bool FastCrafting;
        internal static float CraftMultiplier = 2f;

        private static bool IsPlayer(Community community) =>
            community != null && community == Community.PlayerCommunity;

        internal static void SetResearchPoints(int points)
        {
            var research = Community.PlayerCommunity?.Research;
            if (research != null && research.ResearchPoints != points)
            {
                research.ResearchPoints = points;
                GameEventDispatcher.Dispatch(GameEventType.ResearchPointsUpdated);
            }
        }

        internal static int CountStoredItems(ItemProperties item)
        {
            int count = 0;
            foreach (var storage in TownInventories(item))
                count += storage.Inventory.ReturnInventory(storage.Type).ReturnItemCount(item, includeReserved: true);
            return count;
        }

        internal static int SetStoredItems(ItemProperties item, int target)
        {
            AcquisitionTools.IgnoreGain++;
            try
            {
                return SetStoredItemsCore(item, target);
            }
            finally
            {
                AcquisitionTools.IgnoreGain--;
            }
        }

        private static int SetStoredItemsCore(ItemProperties item, int target)
        {
            int count = CountStoredItems(item);
            int originalCount = count;
            foreach (var storage in TownInventories(item))
            {
                if (count == target)
                    break;

                if (count < target)
                {
                    var inventory = storage.Inventory.ReturnInventory(storage.Type);
                    while (count < target && inventory.AvailableCapacity > inventory.IncomingItems.Count &&
                        storage.Inventory.ReturnAcceptsItem(item) &&
                        storage.Inventory.AddItem(new Item(item), storage.Type))
                        count++;
                }
                else
                {
                    var items = storage.Inventory.ReturnAllItems(storage.Type, includeReserved: false);
                    foreach (var candidate in items)
                    {
                        if (count == target)
                            break;
                        if (candidate.Properties == item && candidate.Project == null &&
                            candidate.MoveToInventory == null && candidate.TakeFromInventory())
                            count--;
                    }
                }
            }

            if (count != originalCount)
                Community.PlayerCommunity.ForceStorageLateUpdate();
            return CountStoredItems(item);
        }

        private static IEnumerable<(Inventory Inventory, SubInventoryType Type)> TownInventories(ItemProperties item)
        {
            var town = Community.PlayerCommunity;
            if (town == null || LoadingScreen.IsLoading)
                yield break;

            var type = (item.Tags & Item.Tags.Liquid) != 0 ? SubInventoryType.Liquid : SubInventoryType.Storage;
            foreach (var storage in town.Storages)
            {
                if (storage != null && storage.Buildable != null && storage.Buildable.BuildPhase == BuildPhase.Finished &&
                    storage.Inventory != null && storage.Inventory.HasSubInventory(type))
                    yield return (storage.Inventory, type);
            }
        }

        internal static void SetPollutionProof(bool enabled)
        {
            PollutionProof = enabled;
            var town = Community.PlayerCommunity;
            if (enabled && town != null)
                foreach (var agent in town.Agents)
                    if (agent != null && agent.IsAlive)
                        agent.Vitals?.Pollution?.Reset();
        }

        internal static void RefreshWorkerSpeed()
        {
            var town = Community.PlayerCommunity;
            if (town == null)
                return;

            foreach (var agent in town.Agents)
            {
                if (agent == null || !agent.IsAlive)
                    continue;

                var navigator = agent.ReturnNavigator(alwaysReturnDrifter: true);
                if (navigator != null && navigator.Terrain == Navigator.TerrainType.UnityNavMesh &&
                    navigator.TryGetComponent<UnityNavMeshNavigator>(out var path) && path.UnityNavMeshAgent != null)
                    path.UnityNavMeshAgent.speed = navigator.ReturnSpeed(Navigator.TerrainType.UnityNavMesh);
                RefreshWorkerAnimation(agent);
            }
        }

        private static void RefreshWorkerAnimation(Agent agent)
        {
            if (agent == null || agent.Attributes == null || agent.DrifterRig == null ||
                agent.DrifterRig.MeshAnimator == null)
                return;
            var attribute = agent.Attributes.ReturnAttribute(DrifterAttributes.AttributeType.Athletics);
            if (attribute != null && !string.IsNullOrEmpty(attribute.AnimationParameter))
                agent.DrifterRig.MeshAnimator.SetFloat(attribute.AnimationParameter,
                    agent.Attributes.ReturnAttributeModifier(attribute) *
                    (FastWorkers && IsPlayer(agent.Community) ? WorkerMultiplier : 1f));
        }

        [HarmonyPatch(typeof(Agent), "OnModifierUpdated")]
        private static class WorkerAnimation
        {
            private static void Postfix(Agent __instance) => RefreshWorkerAnimation(__instance);
        }

        [HarmonyPatch(typeof(Producer), nameof(Producer.Produce))]
        private static class CraftSpeed
        {
            private static void Prefix(Producer __instance, ref float agentModifier)
            {
                if (FastCrafting && __instance.Buildable != null && IsPlayer(__instance.Buildable.Community))
                    agentModifier *= CraftMultiplier;
            }
        }

        [HarmonyPatch(typeof(PathfinderNavigator), nameof(PathfinderNavigator.FollowPath))]
        private static class WorkerPathSubsteps
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var step = Entry.RequireField<float>(typeof(PathfinderNavigator), "_fixedDeltaTime");
                var scale = AccessTools.Method(typeof(WorkerPathSubsteps), nameof(SafeStep));
                int count = 0;
                foreach (var instruction in instructions)
                {
                    yield return instruction;
                    if (instruction.opcode != OpCodes.Ldfld || !Equals(instruction.operand, step))
                        continue;
                    count++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, scale);
                }
                if (count != 3)
                    throw new InvalidOperationException("Expected three path integration step reads in PathfinderNavigator.FollowPath.");
            }

            private static float SafeStep(float original, PathfinderNavigator path)
            {
                if (!FastWorkers || WorkerMultiplier <= 1f || path.Agent == null || !IsPlayer(path.Agent.Community))
                    return original;
                float speed = path.Navigator.ReturnSpeed();
                return Mathf.Max(0.001f, Mathf.Min(original / WorkerMultiplier,
                    path.Radius / Mathf.Max(0.01f, speed) * 0.4f));
            }
        }

        internal static int SalvageNearby(int limit)
        {
            var town = Community.PlayerCommunity;
            var world = GameManager.WorldManager;
            if (town == null || world == null || LoadingScreen.IsLoading)
                return 0;

            int collected = 0;
            foreach (var flotsam in world.FlotsamInWorld.ToArray())
            {
                if (flotsam == null || !flotsam.Initialized || !WorldManager.IsInInteractionRadius(flotsam.Position))
                    continue;

                var source = flotsam.Inventory;
                collected += AcquisitionTools.CollectItems(source, source.ReturnAllItems().ToArray(), limit - collected);
                if (collected >= limit)
                    break;
            }
            if (collected > 0)
                town.ForceStorageLateUpdate();
            return collected;
        }

        [HarmonyPatch(typeof(Pollution), nameof(Pollution.Set))]
        private static class PollutionLevel
        {
            private static void Prefix(Pollution __instance, ref float amount)
            {
                if (PollutionProof && __instance.Agent != null &&
                    __instance.Agent.Community == Community.PlayerCommunity && Community.PlayerCommunity != null)
                    amount = 0f;
            }
        }

        [HarmonyPatch(typeof(Navigator), nameof(Navigator.ReturnSpeed), typeof(Navigator.TerrainType))]
        private static class WorkerSpeed
        {
            private static void Postfix(Navigator __instance, Agent ____agent, ref float __result)
            {
                if (FastWorkers && ____agent != null && IsPlayer(____agent.Community))
                    __result *= WorkerMultiplier;
                if (____agent == null && __instance.TryGetComponent<Boat>(out var boat) &&
                    boat.Buildable != null && IsPlayer(boat.Buildable.Community))
                {
                    if (FastSalvageBoats && boat.Type == BoatType.SalvagingBoat)
                        __result *= SalvageBoatMultiplier;
                    if (FastFishingBoats && boat.Type == BoatType.FishingBoat)
                        __result *= FishingBoatMultiplier;
                }
            }
        }

        [HarmonyPatch(typeof(WorldMap), nameof(WorldMap.MovementSpeed), MethodType.Getter)]
        private static class TownShipSpeed
        {
            private static void Postfix(Community ____playerCommunity, ref float __result)
            {
                if (FastTownShip && IsPlayer(____playerCommunity))
                    __result *= TownShipMultiplier;
            }
        }

        [HarmonyPatch(typeof(PathfinderNavigator), nameof(PathfinderNavigator.FollowPathFixedDeltaTime))]
        private static class WorkerPathStep
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);
                var normalize = AccessTools.PropertyGetter(typeof(Vector3), nameof(Vector3.normalized));
                var speed = AccessTools.Method(typeof(Navigator), nameof(Navigator.ReturnSpeed), Type.EmptyTypes);
                var navigator = AccessTools.PropertyGetter(typeof(PathfinderNavigator), nameof(PathfinderNavigator.Navigator));
                var acceleration = Entry.RequireField<float>(typeof(PathfinderNavigator), "_accelerationMultiplier");
                var multiply = AccessTools.Method(typeof(Vector3), "op_Multiply", new[] { typeof(Vector3), typeof(float) });
                var clamp = AccessTools.Method(typeof(WorkerPathStep), nameof(ClampVelocity));
                int insertion = -1;
                CodeInstruction offset = null;
                for (int i = 1; i + 7 < code.Count; i++)
                {
                    var loadOffset = code[i - 1].opcode;
                    if ((loadOffset != OpCodes.Ldloca && loadOffset != OpCodes.Ldloca_S) ||
                        !code[i].Calls(normalize) || code[i + 1].opcode != OpCodes.Ldarg_0 ||
                        !code[i + 2].Calls(navigator) || !code[i + 3].Calls(speed) ||
                        !code[i + 4].Calls(multiply) || code[i + 5].opcode != OpCodes.Ldarg_0 ||
                        code[i + 6].opcode != OpCodes.Ldfld || !Equals(code[i + 6].operand, acceleration) ||
                        !code[i + 7].Calls(multiply))
                        continue;
                    if (insertion >= 0)
                        throw new InvalidOperationException("Multiple movement calculations in PathfinderNavigator.");
                    insertion = i + 8;
                    offset = code[i - 1];
                }
                if (insertion < 0)
                    throw new InvalidOperationException("PathfinderNavigator movement calculation was not found.");

                code.InsertRange(insertion, new[]
                {
                    new CodeInstruction(offset.opcode, offset.operand),
                    new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, clamp)
                });
                return code;
            }

            private static Vector3 ClampVelocity(Vector3 velocity, ref Vector3 waypointOffset, float deltaTime,
                PathfinderNavigator path)
            {
                if (FastWorkers && WorkerMultiplier > 1f && deltaTime > 0f &&
                    path.Agent != null && IsPlayer(path.Agent.Community) &&
                    (velocity * deltaTime).sqrMagnitude > waypointOffset.sqrMagnitude)
                    return waypointOffset / deltaTime;

                return velocity;
            }
        }

        [HarmonyPatch(typeof(Morale), nameof(Morale.ReturnMoodScore))]
        private static class MoodScore
        {
            private static void Postfix(Morale __instance, Agent ____agent, ref int __result)
            {
                if (HappyWorkers && ____agent != null && IsPlayer(____agent.Community))
                    __result = __instance.Properties.MoraleRange.Maximum;
            }
        }

        [HarmonyPatch(typeof(Morale), "Update")]
        private static class MoraleUpdate
        {
            private static readonly Action<Morale, float> SetMorale =
                (Action<Morale, float>)AccessTools.PropertySetter(typeof(Morale), nameof(Morale.CurrentMoraleFloat))
                    .CreateDelegate(typeof(Action<Morale, float>));
            private static readonly FieldInfo AgentFor =
                Entry.RequireField<Agent>(typeof(Morale), "_agent");

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var round = AccessTools.Method(typeof(Mathf), nameof(Mathf.RoundToInt));
                var replacement = AccessTools.Method(typeof(MoraleUpdate), nameof(RoundMorale));
                var matcher = new CodeMatcher(instructions)
                    .MatchStartForward(new CodeMatch(instruction => instruction.Calls(round)))
                    .ThrowIfInvalid("Morale.Update no longer rounds morale.");
                var original = matcher.Instruction;
                return matcher.SetInstruction(new CodeInstruction(OpCodes.Ldarg_0)
                        .MoveLabelsFrom(original).MoveBlocksFrom(original))
                    .Advance(1)
                    .Insert(new CodeInstruction(OpCodes.Call, replacement))
                    .InstructionEnumeration();
            }

            private static int RoundMorale(float score, Morale morale)
            {
                var agent = HappyWorkers ? (Agent)AgentFor.GetValue(morale) : null;
                if (agent != null && IsPlayer(agent.Community))
                {
                    score = morale.Properties.MoraleRange.Maximum;
                    SetMorale(morale, score);
                }
                return Mathf.RoundToInt(score);
            }
        }

        [HarmonyPatch]
        private static class CraftIngredients
        {
            private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(Producer),
                "TryReserveRequiredItems", new[] { typeof(List<Item>).MakeByRefType(), typeof(Producer.Recipe), typeof(bool) });

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var getter = AccessTools.PropertyGetter(typeof(Producer.Recipe), nameof(Producer.Recipe.Ingredients));
                var replacement = AccessTools.Method(typeof(CraftIngredients), nameof(IngredientsFor));
                int matches = 0;
                foreach (var instruction in instructions)
                {
                    if (!instruction.Calls(getter))
                    {
                        yield return instruction;
                        continue;
                    }

                    matches++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                }
                if (matches != 2)
                    throw new InvalidOperationException("Expected two ingredient reads in Producer.TryReserveRequiredItems.");
            }

            private static IReadOnlyList<CountedItemProperty> IngredientsFor(Producer.Recipe recipe, Producer producer)
            {
                if (FreeCrafting && producer.Buildable.Community == Community.PlayerCommunity &&
                    Community.PlayerCommunity != null)
                    return Array.Empty<CountedItemProperty>();
                return recipe.Ingredients;
            }
        }
    }

    internal sealed class CapacityOverrides
    {
        private const int ExpandedCapacity = 100000;
        private static readonly Action<SubInventory, int> SetCapacity =
            (Action<SubInventory, int>)AccessTools.PropertySetter(typeof(SubInventory), nameof(SubInventory.Capacity))
                .CreateDelegate(typeof(Action<SubInventory, int>));
        private static readonly Action<SubInventory, int> SetAvailable =
            (Action<SubInventory, int>)AccessTools.PropertySetter(typeof(SubInventory), nameof(SubInventory.AvailableCapacity))
                .CreateDelegate(typeof(Action<SubInventory, int>));
        private static readonly Action<SubInventory, bool> SetHasCapacity =
            (Action<SubInventory, bool>)AccessTools.PropertySetter(typeof(SubInventory), nameof(SubInventory.HasCapacity))
                .CreateDelegate(typeof(Action<SubInventory, bool>));
        private static readonly Action<Storage> NotifyStorage =
            (Action<Storage>)AccessTools.Method(typeof(Storage), "OnStorageInventoryUpdated").CreateDelegate(typeof(Action<Storage>));

        private readonly Dictionary<SubInventory, (int Capacity, Inventory Owner)> originals =
            new Dictionary<SubInventory, (int, Inventory)>();

        internal void Expand(Inventory owner, SubInventoryType type)
        {
            if (owner == null || !owner.HasSubInventory(type))
                return;

            var inventory = owner.ReturnInventory(type);
            if (inventory.Capacity >= ExpandedCapacity || originals.ContainsKey(inventory))
                return;

            originals.Add(inventory, (inventory.Capacity, owner));
            Apply(inventory, ExpandedCapacity, owner);
        }

        internal void Restore()
        {
            foreach (var entry in originals)
                if (entry.Value.Owner != null)
                    Apply(entry.Key, entry.Value.Capacity, entry.Value.Owner);
            originals.Clear();
        }

        private static void Apply(SubInventory inventory, int capacity, Inventory owner)
        {
            SetCapacity(inventory, capacity);
            SetAvailable(inventory, capacity - inventory.Count);
            SetHasCapacity(inventory, inventory.Count < capacity);
            owner.OnInventoryUpdated();
            if (owner.Storage != null)
                NotifyStorage(owner.Storage);
        }
    }
}
