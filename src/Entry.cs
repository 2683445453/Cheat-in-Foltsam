using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace FlotsamTrainer
{
    public static class Entry
    {
        internal static FieldInfo RequireField<T>(Type owner, string name)
        {
            var field = AccessTools.Field(owner, name);
            if (field == null || field.IsStatic || field.FieldType != typeof(T))
                throw new MissingFieldException(owner.FullName, name);
            return field;
        }

        public static int Start(string directory)
        {
            try
            {
                if (!SupportsPlayerCommunity(typeof(Community)))
                {
                    Debug.LogError($"[Flotsam Trainer] Unsupported game API: missing static Community.get_PlayerCommunity() returning Community. " +
                        $"Assembly-CSharp module: {typeof(Community).Module.ModuleVersionId}");
                    return 20;
                }

                Assembly.LoadFrom(Path.Combine(directory, "0Harmony.dll"));
                var context = (SynchronizationContext)typeof(Awaitable)
                    .GetField("_synchronizationContext", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.GetValue(null);
                if (context == null)
                    return 21;

                // Unity objects and game hooks are installed on the player loop.
                var startup = new Task<int>(TrainerPanel.Install);
                context.Post(_ =>
                {
                    if (startup.Status == TaskStatus.Created)
                        startup.RunSynchronously(TaskScheduler.Default);
                }, null);
                if (!startup.Wait(TimeSpan.FromSeconds(12)))
                    return 22;
                return startup.Result;
            }
            catch (Exception error)
            {
                Debug.LogError($"[Flotsam Trainer] Startup failed ({typeof(Entry).Module.ModuleVersionId}): {error}");
                return 23;
            }
        }

        private static bool SupportsPlayerCommunity(Type community)
        {
            var getter = community.GetMethod("get_PlayerCommunity", BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            return getter != null && !getter.IsGenericMethod && getter.ReturnType == community;
        }
    }
}
