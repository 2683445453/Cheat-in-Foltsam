using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FlotsamTrainer
{
    public static class Entry
    {
        public static int Start(string directory)
        {
            try
            {
                using (var file = File.OpenRead(Path.Combine(Application.dataPath, "Managed", "Assembly-CSharp.dll")))
                using (var sha = SHA256.Create())
                {
                    var hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                    if (hash != "5B5C9F730ED71D5F66C0267064FC57667D2FF5644A018E9189B140CE025C1CD8")
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
                Debug.LogError("[Flotsam Trainer] " + error);
                return 23;
            }
        }
    }
}
