using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace FlotsamTrainer
{
    [DataContract]
    internal sealed class TrainerSettings
    {
        [DataContract]
        internal sealed class Option
        {
            [DataMember] public string Key;
            [DataMember] public bool Enabled;
        }

        [DataContract]
        internal sealed class Parameter
        {
            [DataMember] public string Key;
            [DataMember] public float Value;
        }

        [DataMember] public int Version = 1;
        [DataMember] public List<Option> Options = new List<Option>();
        [DataMember] public List<Parameter> Parameters = new List<Parameter>();
        [DataMember] public bool MinimapFog;

        internal static TrainerSettings Load(string path)
        {
            if (!File.Exists(path))
                return new TrainerSettings();
            TrainerSettings settings;
            using (var file = File.OpenRead(path))
                settings = (TrainerSettings)new DataContractJsonSerializer(typeof(TrainerSettings)).ReadObject(file);
            if (settings == null || settings.Version != 1)
                throw new InvalidDataException("Unsupported trainer settings format.");
            return settings;
        }

        internal bool TryGetOption(string key, out bool enabled)
        {
            var option = Options?.FindLast(entry => entry != null && entry.Key == key);
            enabled = option != null && option.Enabled;
            return option != null;
        }

        internal bool TryGetParameter(string key, out float value)
        {
            var parameter = Parameters?.FindLast(entry => entry != null && entry.Key == key);
            value = parameter?.Value ?? 0f;
            return parameter != null && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal void Save(string path)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(file, Encoding.UTF8, false, true, "  "))
                    new DataContractJsonSerializer(typeof(TrainerSettings)).WriteObject(writer, this);
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    public sealed partial class TrainerPanel
    {
        private readonly Dictionary<string, Toggle> savedToggles = new Dictionary<string, Toggle>();
        private readonly Dictionary<string, Func<float>> savedParameters = new Dictionary<string, Func<float>>();
        private TrainerSettings savedSettings;
        private bool settingsReady;
        private bool suppressSettingsSave;
        private bool settingsDirty;
        private float settingsSaveTime;

        private void LoadSettings()
        {
            try
            {
                savedSettings = TrainerSettings.Load(Entry.SettingsPath);
            }
            catch (Exception error)
            {
                Debug.LogError($"[Flotsam Trainer] Settings could not be read: {error}");
                savedSettings = new TrainerSettings();
            }
        }

        private void RestoreSettings()
        {
            foreach (var binding in savedToggles)
                if (savedSettings.TryGetOption(binding.Key, out bool enabled))
                    binding.Value.isOn = enabled;
            if (savedSettings.MinimapFog != showMinimapFog)
                ToggleMinimapFog();
            settingsReady = true;
        }

        private void SettingsChanged()
        {
            if (!settingsReady || suppressSettingsSave)
                return;
            settingsDirty = true;
            settingsSaveTime = Time.unscaledTime + 0.5f;
        }

        private void UpdateSettings()
        {
            if (settingsDirty && Time.unscaledTime >= settingsSaveTime)
                SaveSettings();
        }

        private void SaveSettings()
        {
            if (!settingsReady || suppressSettingsSave || !settingsDirty)
                return;
            var settings = new TrainerSettings { MinimapFog = showMinimapFog };
            foreach (var binding in savedToggles)
                settings.Options.Add(new TrainerSettings.Option { Key = binding.Key, Enabled = binding.Value.isOn });
            foreach (var binding in savedParameters)
                settings.Parameters.Add(new TrainerSettings.Parameter { Key = binding.Key, Value = binding.Value() });
            try
            {
                settings.Save(Entry.SettingsPath);
            }
            catch (Exception error)
            {
                Debug.LogError($"[Flotsam Trainer] Settings could not be saved: {error}");
                if (status != null)
                    status.text = "设置保存失败，请检查修改器目录写入权限";
                settingsSaveTime = Time.unscaledTime + 5f;
                return;
            }
            settingsDirty = false;
        }

        private void OnApplicationQuit() => SaveSettings();
    }
}
