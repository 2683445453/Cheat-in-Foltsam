using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace FlotsamTrainer
{
    internal static class AcquisitionTools
    {
        internal static bool NoTools;
        internal static bool MultiplyItems;
        internal static float ItemMultiplier = 2f;
        internal static bool FastConstruction;
        internal static float ConstructionMultiplier = 2f;
        internal static bool FastDemolition;
        internal static float DemolitionMultiplier = 2f;
        [ThreadStatic] internal static int IgnoreGain;
        private static readonly Dictionary<ItemProperties, double> BonusRemainders = new Dictionary<ItemProperties, double>();
        private static readonly FieldInfo ConstructionSource =
            Entry.RequireField<SubInventoryType>(typeof(MoveConstructionResource), "_from");

        private static bool IsPlayerInventory(InventoryBase inventory)
        {
            if (inventory == null || Community.PlayerCommunity == null)
                return false;
            if (inventory.TryGetComponent<Agent>(out var agent))
                return agent.Community == Community.PlayerCommunity;
            return inventory.TryGetComponent<Buildable>(out var buildable) &&
                buildable.Community == Community.PlayerCommunity;
        }

        internal static int CollectItems(InventoryBase source, Item[] items, int limit)
        {
            var town = Community.PlayerCommunity;
            if (town == null || source == null || limit <= 0)
                return 0;
            int collected = 0;
            foreach (var item in items)
            {
                if (item.Inventory != source || item.IsReserved || item.MoveToInventory != null || item.Project != null)
                    continue;
                var payload = item.ReturnSubItem();
                var type = (payload.Properties.Tags & Item.Tags.Liquid) != 0
                    ? SubInventoryType.Liquid : SubInventoryType.Storage;
                foreach (var storage in town.ReturnAvailableStorages(payload))
                {
                    var inventory = storage.Inventory;
                    if (storage.Buildable == null || storage.Buildable.BuildPhase != BuildPhase.Finished ||
                        !inventory.HasSubInventory(type))
                        continue;
                    var destination = inventory.ReturnInventory(type);
                    if (destination.AvailableCapacity <= destination.IncomingItems.Count)
                        continue;
                    var taken = source.TakeItem(item);
                    if (taken != null)
                    {
                        // Complete the checked transfer even if a removal event changed available space.
                        var acquired = taken.ReturnSubItem();
                        inventory.RestoreItem(acquired, destination);
                        ItemEvent.Dispatch(GameEventType.ItemSalvaged, acquired);
                        collected++;
                    }
                    break;
                }
                if (collected >= limit)
                    break;
            }
            return collected;
        }

        internal static int VisitNearbyLandmarks(bool collect, int limit)
        {
            var world = GameManager.WorldManager?.World;
            var town = Community.PlayerCommunity;
            if (world == null || town == null || LoadingScreen.IsLoading)
                return 0;
            int collected = 0;
            foreach (var spawner in world.ReturnAllLandmarks())
            {
                // A region lookout also sets RequiresScouting on the whole landmark.
                // Its map action must not prevent visiting the landmark's salvage categories.
                if (!(spawner.LandmarkBehaviour is ActionsBehaviour behaviour) || behaviour.Landmark == null ||
                    !WorldManager.IsInInteractionRadius(behaviour.Landmark.transform.position))
                    continue;
                foreach (var action in behaviour.Actions)
                {
                    if (!(action is LandmarkActionSalvage salvage) || salvage.Categories == null)
                        continue;
                    foreach (var category in salvage.Categories)
                    {
                        if (NoTools && !category.Unlocked && category.RequiresItem)
                        {
                            category.Unlocked = true;
                            category.InitializeUnlockables(behaviour.Landmark.Unlockables);
                            salvage.CompositionUpdated.Invoke();
                        }
                        if (!collect || !category.CanBeSalvaged || collected >= limit)
                            continue;
                        foreach (var member in category.Members)
                        {
                            if (member.Instance == null || member.Instance.Inventory == null)
                                continue;
                            var source = member.Instance.Inventory;
                            var items = source.ReturnAllItems(SubInventoryType.Composition, includeReserved: false);
                            items.RemoveAll(item => !category.IsItemFilterToggled(item.Properties));
                            collected += CollectItems(source, items.ToArray(), limit - collected);
                        }
                    }
                }
            }
            if (collected > 0)
                town.ForceStorageLateUpdate();
            return collected;
        }

        [HarmonyPatch(typeof(Inventory), "OnItemAdded")]
        private static class ItemAcquisition
        {
            private static void Prefix(Inventory __instance, Item item, SubInventory subInventory, out bool __state)
            {
                __state = MultiplyItems && IgnoreGain == 0 && !LoadingScreen.IsLoading &&
                    subInventory.Type != SubInventoryType.Composition && subInventory.Type != SubInventoryType.Resources &&
                    !item.IsQuestItem && !item.Properties.IsSuperItem &&
                    IsPlayerInventory(__instance) && !IsPlayerInventory(item.Inventory);
            }

            private static void Postfix(Inventory __instance, Item item, SubInventory subInventory, bool __state)
            {
                if (!__state || item.Inventory != __instance)
                    return;
                BonusRemainders.TryGetValue(item.Properties, out double remainder);
                double bonus = Math.Max(0f, ItemMultiplier - 1f) + remainder;
                int extra = (int)Math.Floor(bonus);
                BonusRemainders[item.Properties] = bonus - extra;
                IgnoreGain++;
                try
                {
                    for (int i = 0; i < extra && subInventory.AvailableCapacity > subInventory.IncomingItems.Count; i++)
                    {
                        if (!__instance.AddItem(new Item(item.Properties), subInventory.Type))
                            break;
                    }
                }
                finally
                {
                    IgnoreGain--;
                }
            }
        }

        [HarmonyPatch]
        private static class ConstructionSpeed
        {
            private static MethodBase TargetMethod()
            {
                var coroutine = AccessTools.Method(typeof(MoveConstructionResource), nameof(MoveConstructionResource.RunTaskCoroutine));
                var state = coroutine.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
                if (state == null)
                    throw new MissingMethodException("MoveConstructionResource coroutine state machine was not found.");
                return AccessTools.Method(state, "MoveNext");
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            {
                var state = __originalMethod.DeclaringType;
                var task = Entry.RequireField<MoveConstructionResource>(state, "<>4__this");
                var agent = Entry.RequireField<Agent>(state, "agent");
                var deltaTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.deltaTime));
                var scaledTime = AccessTools.Method(typeof(ConstructionSpeed), nameof(ScaledTime));
                int count = 0;
                foreach (var instruction in instructions)
                {
                    if (!instruction.Calls(deltaTime))
                    {
                        yield return instruction;
                        continue;
                    }
                    count++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Ldfld, task);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, agent);
                    yield return new CodeInstruction(OpCodes.Call, scaledTime);
                }
                if (count != 1)
                    throw new InvalidOperationException("Expected one construction timer input.");
            }

            private static float ScaledTime(MoveConstructionResource task, Agent agent)
            {
                float multiplier = 1f;
                if (agent != null && Community.PlayerCommunity != null && agent.Community == Community.PlayerCommunity)
                {
                    bool demolition = (SubInventoryType)ConstructionSource.GetValue(task) == SubInventoryType.Composition;
                    if (demolition && FastDemolition)
                        multiplier = DemolitionMultiplier;
                    if (!demolition && FastConstruction)
                        multiplier = ConstructionMultiplier;
                }
                return Time.deltaTime * multiplier;
            }
        }
    }
}
