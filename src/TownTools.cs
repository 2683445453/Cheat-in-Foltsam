using System;
using System.Collections.Generic;
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
        internal static bool FastBoats;
        internal static float BoatMultiplier = 2f;

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
                foreach (var item in source.ReturnAllItems().ToArray())
                {
                    if (item.Inventory != source || item.IsReserved || item.MoveToInventory != null || item.Project != null)
                        continue;

                    var payload = item.ReturnSubItem();
                    var type = (payload.Properties.Tags & Item.Tags.Liquid) != 0
                        ? SubInventoryType.Liquid : SubInventoryType.Storage;
                    foreach (var storage in town.ReturnAvailableStorages(payload))
                    {
                        var inventory = storage.Inventory;
                        if (!inventory.HasSubInventory(type))
                            continue;

                        var destination = inventory.ReturnInventory(type);
                        if (destination.AvailableCapacity <= destination.IncomingItems.Count)
                            continue;

                        var taken = source.TakeItem(item);
                        if (taken != null)
                        {
                            // Space was checked above; finish the transfer even if a salvage event changed capacity.
                            inventory.RestoreItem(taken, destination);
                            ItemEvent.Dispatch(GameEventType.ItemSalvaged, taken);
                            collected++;
                            if (collected >= limit)
                            {
                                town.ForceStorageLateUpdate();
                                return collected;
                            }
                        }
                        break;
                    }
                }
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
                if (FastBoats && ____agent == null && __instance.TryGetComponent<Boat>(out var boat) &&
                    boat.Buildable != null && IsPlayer(boat.Buildable.Community))
                    __result *= BoatMultiplier;
            }
        }

        [HarmonyPatch(typeof(WorldMap), nameof(WorldMap.MovementSpeed), MethodType.Getter)]
        private static class TownShipSpeed
        {
            private static void Postfix(Community ____playerCommunity, ref float __result)
            {
                if (FastBoats && IsPlayer(____playerCommunity))
                    __result *= BoatMultiplier;
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
            private static readonly Action<Morale, float> SetMorale = AccessTools
                .MethodDelegate<Action<Morale, float>>(AccessTools.PropertySetter(typeof(Morale), nameof(Morale.CurrentMoraleFloat)));
            private static readonly AccessTools.FieldRef<Morale, Agent> AgentFor =
                AccessTools.FieldRefAccess<Morale, Agent>("_agent");

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
                var agent = HappyWorkers ? AgentFor(morale) : null;
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
        private static readonly Action<SubInventory, int> SetCapacity = AccessTools
            .MethodDelegate<Action<SubInventory, int>>(AccessTools.PropertySetter(typeof(SubInventory), nameof(SubInventory.Capacity)));
        private static readonly Action<SubInventory, int> SetAvailable = AccessTools
            .MethodDelegate<Action<SubInventory, int>>(AccessTools.PropertySetter(typeof(SubInventory), nameof(SubInventory.AvailableCapacity)));
        private static readonly Action<SubInventory, bool> SetHasCapacity = AccessTools
            .MethodDelegate<Action<SubInventory, bool>>(AccessTools.PropertySetter(typeof(SubInventory), nameof(SubInventory.HasCapacity)));
        private static readonly Action<Storage> NotifyStorage = AccessTools
            .MethodDelegate<Action<Storage>>(AccessTools.Method(typeof(Storage), "OnStorageInventoryUpdated"));

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
