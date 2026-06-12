using System;
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
                LogInfo($"Current mod asset at {asset.path}");
            }

            updateSystem.UpdateAt<InternalRoadNativeToolPatchSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<InternalRoadOptionsUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            LogInfo(nameof(OnDispose));
        }

        internal static void LogInfo(string message)
        {
            log.Info(message);
        }

        internal static void LogException(Exception exception, string message)
        {
            log.Info(exception, $"[ERROR] {message}");
        }
    }
}
