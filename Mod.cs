using System;
using System.Diagnostics;
using ChangeInternalRoads.Systems;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace ChangeInternalRoads
{
    public class Mod : IMod
    {
        public const string ModId = nameof(ChangeInternalRoads);

        public static readonly ILog log = LogManager.GetLogger(ModId).SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            LogInfo(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                LogDebugInfo($"Current mod asset at {asset.path}");
            }

            updateSystem.UpdateAt<InternalRoadNativeToolPatchSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<InternalRoadOptionsUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            LogDebugInfo(nameof(OnDispose));
        }

        internal static void LogInfo(string message)
        {
            log.Info(message);
        }

        internal static void LogException(Exception exception, string message)
        {
            log.Info(exception, $"[ERROR] {message}");
        }

        [Conditional("DEBUG")]
        internal static void LogDebugInfo(string message)
        {
            log.Info(message);
        }

        [Conditional("DEBUG")]
        internal static void LogDebugException(Exception exception, string message)
        {
            log.Info(exception, $"[ERROR] {message}");
        }
    }
}
