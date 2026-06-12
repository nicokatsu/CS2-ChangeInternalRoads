using Colossal.UI.Binding;
using Game;
using Game.Tools;
using Game.UI;

namespace ChangeInternalRoads.Systems
{
    public partial class InternalRoadOptionsUISystem : UISystemBase
    {
        private const string Group = "changeInternalRoads";

        private ToolSystem toolSystem;
        private NetToolSystem netToolSystem;
        private ValueBinding<bool> internalRoadsEnabledBinding;
        private ValueBinding<bool> showInternalRoadsToggleBinding;

        protected override void OnCreate()
        {
            base.OnCreate();

            toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            netToolSystem = World.GetOrCreateSystemManaged<NetToolSystem>();

            bool enabled = InternalRoadOptionsStore.LoadInternalRoadsEnabled();
            InternalRoadNativeToolPatchSystem.SetInternalRoadsEnabled(enabled, "settings-load");

            AddBinding(internalRoadsEnabledBinding = new ValueBinding<bool>(Group, "internalRoadsEnabled", enabled));
            AddBinding(showInternalRoadsToggleBinding = new ValueBinding<bool>(Group, "showInternalRoadsToggle", false));
            AddBinding(new TriggerBinding<bool>(Group, "setInternalRoadsEnabled", SetInternalRoadsEnabled));

            Mod.LogInfo($"[UI] Created options UI system group={Group} internalRoadsEnabled={enabled} settingsPath={InternalRoadOptionsStore.SettingsPath}.");
        }

        protected override void OnDestroy()
        {
            Mod.LogDebugInfo("[UI] Destroying options UI system.");
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            internalRoadsEnabledBinding.Update(InternalRoadNativeToolPatchSystem.InternalRoadsEnabled);
            showInternalRoadsToggleBinding.Update(ShouldShowInternalRoadsToggle());
        }

        private void SetInternalRoadsEnabled(bool enabled)
        {
            InternalRoadNativeToolPatchSystem.SetInternalRoadsEnabled(enabled, "ui-toggle");
            InternalRoadOptionsStore.SaveInternalRoadsEnabled(enabled);
            internalRoadsEnabledBinding.Update(enabled);
            Mod.LogInfo($"[UI] Internal roads toggle changed enabled={enabled} settingsPath={InternalRoadOptionsStore.SettingsPath}.");
        }

        private bool ShouldShowInternalRoadsToggle()
        {
            return toolSystem != null &&
                   netToolSystem != null &&
                   toolSystem.activeTool == netToolSystem &&
                   netToolSystem.actualMode == NetToolSystem.Mode.Replace;
        }
    }
}
