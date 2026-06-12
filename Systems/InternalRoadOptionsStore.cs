using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace ChangeInternalRoads.Systems
{
    internal static class InternalRoadOptionsStore
    {
        private static readonly string PathValue =
            Path.Combine(Application.persistentDataPath, "ModsData", Mod.ModId, "settings.json");

        internal static string SettingsPath => PathValue;

        internal static bool LoadInternalRoadsEnabled()
        {
            try
            {
                if (!File.Exists(PathValue))
                {
                    return false;
                }

                string json = File.ReadAllText(PathValue);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                var serializer = new DataContractJsonSerializer(typeof(SettingsFile));
                var settings = serializer.ReadObject(stream) as SettingsFile;
                return settings?.InternalRoadsEnabled == true;
            }
            catch (System.Exception ex)
            {
                Mod.LogException(ex, $"[Settings] Failed to load settings; using defaults path={PathValue}");
                return false;
            }
        }

        internal static void SaveInternalRoadsEnabled(bool enabled)
        {
            try
            {
                string directory = Path.GetDirectoryName(PathValue);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var stream = new MemoryStream();
                var serializer = new DataContractJsonSerializer(typeof(SettingsFile));
                serializer.WriteObject(stream, new SettingsFile { InternalRoadsEnabled = enabled });
                File.WriteAllText(PathValue, Encoding.UTF8.GetString(stream.ToArray()));
            }
            catch (System.Exception ex)
            {
                Mod.LogException(ex, $"[Settings] Failed to save settings path={PathValue} internalRoadsEnabled={enabled}");
            }
        }

        [DataContract]
        private sealed class SettingsFile
        {
            [DataMember(Name = "internalRoadsEnabled")]
            public bool InternalRoadsEnabled;
        }
    }
}
