using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using ChangeInternalRoads.Diagnostics;
using ChangeInternalRoads.Services;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using HarmonyLib;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace ChangeInternalRoads.Systems
{
    public partial class InternalRoadNativeToolPatchSystem : GameSystemBase
    {
        private const string HarmonyId = "glydd.change-internal-roads.native-net-tool";
        private const int VerificationAttempts = 20;
        private const int AllowedEdgeLifetimeFrames = 60;
        private static readonly object PatchLock = new object();
        private static readonly AllowedEdgeHistory AllowedEdges = new AllowedEdgeHistory(AllowedEdgeLifetimeFrames);
        private static Harmony harmony;
        private static bool patched;
        private static bool internalRoadsEnabled;
        private static FieldInfo toolRaycastSystemField;
        private static InternalRoadNativeToolPatchSystem instance;

        private PrefabSystem prefabSystem;
        private InternalRoadNetClassifier netClassifier;
        private readonly List<PendingInstanceVerification> pendingVerifications = new List<PendingInstanceVerification>();
        private readonly Dictionary<string, int> skipLogFrames = new Dictionary<string, int>(StringComparer.Ordinal);
        private string lastRaycastLogKey;

        public static bool InternalRoadsEnabled => internalRoadsEnabled;

        public static void SetInternalRoadsEnabled(bool enabled, string reason)
        {
            if (internalRoadsEnabled == enabled)
            {
                if (!enabled)
                {
                    ClearRuntimeState();
                }

                return;
            }

            internalRoadsEnabled = enabled;
            if (!enabled)
            {
                ClearRuntimeState();
            }

            Mod.LogInfo($"[NativeTool] Internal roads feature state changed enabled={enabled} reason={reason} clearedRuntimeState={!enabled}.");
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            instance = this;
            prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            netClassifier = new InternalRoadNetClassifier();
            EnsurePatched();
            Mod.LogInfo($"[NativeTool] Created patchSystem={nameof(InternalRoadNativeToolPatchSystem)} mode=vanilla-net-tool-patch.");
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (ReferenceEquals(instance, this))
            {
                ResetRuntimeState("system-destroy", this);
                instance = null;
            }
            else
            {
                ClearPendingVerification();
            }

            Unpatch();
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            ProcessPendingVerification();
        }

        private static void EnsurePatched()
        {
            lock (PatchLock)
            {
                if (patched)
                {
                    return;
                }

                try
                {
                    toolRaycastSystemField = AccessTools.Field(typeof(ToolBaseSystem), "m_ToolRaycastSystem");
                    MethodInfo initializeRaycast = AccessTools.Method(typeof(NetToolSystem), nameof(NetToolSystem.InitializeRaycast));
                    MethodInfo filterRaycastResult = AccessTools.Method(
                        typeof(NetToolSystem),
                        "FilterRaycastResult",
                        new[] { typeof(Entity), typeof(RaycastHit) });
                    MethodInfo staticAllowUpgrade = AccessTools.Method(
                        typeof(NetToolSystem),
                        "AllowUpgrade",
                        new[]
                        {
                            typeof(Entity),
                            typeof(bool),
                            typeof(bool),
                            typeof(ComponentLookup<Owner>).MakeByRefType(),
                            typeof(ComponentLookup<Edge>).MakeByRefType()
                        });
                    MethodInfo apply = AccessTools.Method(
                        typeof(NetToolSystem),
                        "Apply",
                        new[] { typeof(JobHandle), typeof(bool) });
                    MethodInfo snapControlPoints = AccessTools.Method(
                        typeof(NetToolSystem),
                        "SnapControlPoints",
                        new[] { typeof(JobHandle), typeof(bool) });
                    MethodInfo updateCourse = AccessTools.Method(
                        typeof(NetToolSystem),
                        "UpdateCourse",
                        new[] { typeof(JobHandle), typeof(bool) });

                    if (toolRaycastSystemField == null ||
                        initializeRaycast == null ||
                        filterRaycastResult == null ||
                        staticAllowUpgrade == null ||
                        apply == null ||
                        snapControlPoints == null ||
                        updateCourse == null)
                    {
                        Mod.LogInfo($"[NativeTool] [ERROR] Patch disabled because a required NetToolSystem member was not found raycastField={toolRaycastSystemField != null} initialize={initializeRaycast != null} filter={filterRaycastResult != null} staticAllow={staticAllowUpgrade != null} apply={apply != null} snap={snapControlPoints != null} updateCourse={updateCourse != null}.");
                        return;
                    }

                    harmony = new Harmony(HarmonyId);
                    harmony.Patch(
                        initializeRaycast,
                        postfix: new HarmonyMethod(typeof(InternalRoadNativeToolPatchSystem), nameof(InitializeRaycastPostfix)));
                    harmony.Patch(
                        filterRaycastResult,
                        postfix: new HarmonyMethod(typeof(InternalRoadNativeToolPatchSystem), nameof(FilterRaycastResultPostfix)));
                    harmony.Patch(
                        staticAllowUpgrade,
                        postfix: new HarmonyMethod(typeof(InternalRoadNativeToolPatchSystem), nameof(StaticAllowUpgradePostfix)));
                    harmony.Patch(
                        apply,
                        postfix: new HarmonyMethod(typeof(InternalRoadNativeToolPatchSystem), nameof(ApplyPostfix)));
                    harmony.Patch(
                        snapControlPoints,
                        transpiler: new HarmonyMethod(typeof(InternalRoadNativeToolPatchSystem), nameof(EditorModeTranspiler)));
                    harmony.Patch(
                        updateCourse,
                        transpiler: new HarmonyMethod(typeof(InternalRoadNativeToolPatchSystem), nameof(EditorModeTranspiler)));

                    patched = true;
                    Mod.LogInfo("[NativeTool] Harmony patches installed methods=NetToolSystem.InitializeRaycast,FilterRaycastResult,AllowUpgrade(static),Apply,SnapControlPoints,UpdateCourse.");
                }
                catch (Exception ex)
                {
                    harmony?.UnpatchAll(HarmonyId);
                    harmony = null;
                    patched = false;
                    Mod.LogException(ex, "[NativeTool] Failed to install Harmony patches; native tool integration disabled");
                }
            }
        }

        private static void Unpatch()
        {
            lock (PatchLock)
            {
                if (!patched || harmony == null)
                {
                    return;
                }

                try
                {
                    harmony.UnpatchAll(HarmonyId);
                    Mod.LogDebugInfo("[NativeTool] Harmony patches uninstalled.");
                }
                catch (Exception ex)
                {
                    Mod.LogException(ex, "[NativeTool] Failed to uninstall Harmony patches");
                }
                finally
                {
                    patched = false;
                    harmony = null;
                }
            }
        }

        private static void InitializeRaycastPostfix(NetToolSystem __instance)
        {
            try
            {
                if (!InternalRoadsEnabled)
                {
                    instance?.LogDisabledSkipOnce(nameof(InitializeRaycastPostfix));
                    return;
                }

                if (__instance.actualMode != NetToolSystem.Mode.Replace || __instance.prefab == null)
                {
                    return;
                }

                if (toolRaycastSystemField?.GetValue(__instance) is not ToolRaycastSystem raycastSystem)
                {
                    return;
                }

                RaycastFlags oldFlags = raycastSystem.raycastFlags;
                raycastSystem.raycastFlags |= RaycastFlags.SubElements;
                raycastSystem.raycastFlags &= ~RaycastFlags.IgnoreSecondary;

                instance?.LogRaycastPatch(__instance.prefab, oldFlags, raycastSystem.raycastFlags);
            }
            catch (Exception ex)
            {
                Mod.LogException(ex, "[NativeTool] InitializeRaycast postfix failed");
            }
        }

        private static void FilterRaycastResultPostfix(
            NetToolSystem __instance,
            Entity entity,
            RaycastHit hit,
            ref ControlPoint __result)
        {
            try
            {
                if (!InternalRoadsEnabled)
                {
                    instance?.LogDisabledSkipOnce(nameof(FilterRaycastResultPostfix));
                    return;
                }

                if (__instance.actualMode != NetToolSystem.Mode.Replace ||
                    __result.m_OriginalEntity != Entity.Null ||
                    instance == null)
                {
                    return;
                }

                Entity candidate = instance.GetBestCandidateEdge(entity, hit);
                if (candidate == Entity.Null)
                {
                    return;
                }

                NetPrefab targetPrefab = __instance.prefab;
                if (!instance.TryAuthorizeInternalEdge(candidate, targetPrefab, out var authorization))
                {
                    return;
                }

                __result = new ControlPoint(candidate, hit);
                RegisterAllowedEdge(authorization);
                Mod.LogInfo($"[NativeTool] Authorized internal edge ownerPrefab={authorization.OwnerPrefabName} subnetIndex={authorization.SubnetIndex} sourcePrefab={authorization.SourcePrefabName} targetPrefab={authorization.TargetPrefabName} targetIsUpgrade={authorization.TargetIsUpgrade}.");
                Mod.LogDebugInfo($"[NativeTool] Allowed internal edge for vanilla replace sourceEdge={DiagnosticFormat.Entity(candidate)} owner={DiagnosticFormat.Entity(authorization.Owner)} ownerPrefab={authorization.OwnerPrefabName} subnetIndex={authorization.SubnetIndex} sourcePrefab={authorization.SourcePrefabName} targetPrefab={authorization.TargetPrefabName} targetIsUpgrade={authorization.TargetIsUpgrade} sourceProfile=\"{authorization.SourceProfile}\" targetProfile=\"{authorization.TargetProfile}\".");
            }
            catch (Exception ex)
            {
                Mod.LogException(ex, "[NativeTool] FilterRaycastResult postfix failed");
            }
        }

        private static void ApplyPostfix(NetToolSystem __instance)
        {
            try
            {
                if (!InternalRoadsEnabled)
                {
                    return;
                }

                if (__instance.actualMode != NetToolSystem.Mode.Replace || instance == null)
                {
                    return;
                }

                instance.QueueRecentVerification(__instance.prefab);
            }
            catch (Exception ex)
            {
                Mod.LogException(ex, "[NativeTool] Apply postfix failed");
            }
        }

        private static IEnumerable<CodeInstruction> EditorModeTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo isEditor = AccessTools.Method(typeof(GameModeExtensions), nameof(GameModeExtensions.IsEditor));
            MethodInfo replacement = AccessTools.Method(typeof(InternalRoadNativeToolPatchSystem), nameof(IsEditorOrInternalRoad));
            var patchedCalls = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, isEditor))
                {
                    patchedCalls++;
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    continue;
                }

                yield return instruction;
            }

            Mod.LogInfo($"[NativeTool] EditorMode transpiler patched IsEditor calls={patchedCalls}.");
        }

        private static bool IsEditorOrInternalRoad(GameMode mode)
        {
            return mode.IsEditor() || (InternalRoadsEnabled && HasRecentAllowedEdge());
        }

        private static void StaticAllowUpgradePostfix(Entity entity, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (!InternalRoadsEnabled)
            {
                return;
            }

            if (IsAllowedEdge(entity))
            {
                __result = true;
            }
        }

        [Conditional("DEBUG")]
        private void LogRaycastPatch(NetPrefab targetPrefab, RaycastFlags oldFlags, RaycastFlags newFlags)
        {
            string key = $"{targetPrefab.name}|{oldFlags}|{newFlags}";
            if (string.Equals(lastRaycastLogKey, key, StringComparison.Ordinal))
            {
                return;
            }

            lastRaycastLogKey = key;
            Mod.LogDebugInfo($"[NativeTool] Replace raycast patched targetPrefab={targetPrefab.name} oldFlags={oldFlags} newFlags={newFlags} note=\"IgnoreSecondary removed for internal subnet picking; vanilla owner highlighting is left untouched.\"");
        }

        private Entity GetBestCandidateEdge(Entity entity, RaycastHit hit)
        {
            if (entity != Entity.Null && EntityManager.HasComponent<Edge>(entity))
            {
                return entity;
            }

            if (hit.m_HitEntity != Entity.Null && EntityManager.HasComponent<Edge>(hit.m_HitEntity))
            {
                return hit.m_HitEntity;
            }

            return Entity.Null;
        }

        private bool TryAuthorizeInternalEdge(Entity edge, NetPrefab targetPrefab, out AllowedInternalEdge authorization)
        {
            authorization = default;

            if (edge == Entity.Null || targetPrefab == null)
            {
                return false;
            }

            if (!EntityManager.Exists(edge) ||
                !EntityManager.HasComponent<Edge>(edge) ||
                !EntityManager.HasComponent<PrefabRef>(edge))
            {
                return false;
            }

            PrefabRef sourcePrefabRef = EntityManager.GetComponentData<PrefabRef>(edge);
            if (!prefabSystem.TryGetPrefab(sourcePrefabRef.m_Prefab, out NetPrefab sourcePrefab))
            {
                return false;
            }

            NetClassification sourceClassification = netClassifier.Classify(sourcePrefab);
            if (!sourceClassification.IsSupported)
            {
                LogSkipOnce(
                    $"source|{edge.Index}|{sourcePrefab?.name}|{sourceClassification.SkipReason}",
                    $"[NativeTool] Skipped internal candidate edge={DiagnosticFormat.Entity(edge)} sourcePrefab={sourcePrefab?.name ?? "<null>"} skipReason={sourceClassification.SkipReason} detail=\"{sourceClassification.Detail}\".");
                return false;
            }

            bool targetIsUpgrade = targetPrefab.TryGet(out NetUpgrade _);
            NetClassification targetClassification = targetIsUpgrade
                ? NetClassification.Supported("target=NetUpgrade; vanilla upgrade flags")
                : netClassifier.Classify(targetPrefab);
            if (!targetClassification.IsSupported)
            {
                LogSkipOnce(
                    $"target|{edge.Index}|{targetPrefab.name}|{targetClassification.SkipReason}",
                    $"[NativeTool] [ERROR] Target rejected edge={DiagnosticFormat.Entity(edge)} targetPrefab={targetPrefab.name} skipReason={targetClassification.SkipReason} detail=\"{targetClassification.Detail}\".");
                return false;
            }

            if (!TryFindInternalOwner(edge, out Entity owner, out int subnetIndex, out string ownerPrefabName))
            {
                return false;
            }

            if (!prefabSystem.TryGetEntity(targetPrefab, out Entity targetPrefabEntity))
            {
                Mod.LogInfo($"[NativeTool] [ERROR] Target rejected edge={DiagnosticFormat.Entity(edge)} targetPrefab={targetPrefab.name} reason=target-prefab-entity-unresolved.");
                return false;
            }

            authorization = new AllowedInternalEdge
            {
                Edge = edge,
                Owner = owner,
                SubnetIndex = subnetIndex,
                TargetPrefab = targetPrefabEntity,
                OwnerPrefabName = ownerPrefabName,
                SourcePrefabName = sourcePrefab.name,
                TargetPrefabName = targetPrefab.name,
                SourceProfile = sourceClassification.Detail,
                TargetProfile = targetClassification.Detail,
                TargetIsUpgrade = targetIsUpgrade,
                Frame = UnityEngine.Time.frameCount
            };
            return true;
        }

        private bool TryFindInternalOwner(Entity edge, out Entity owner, out int subnetIndex, out string ownerPrefabName)
        {
            owner = Entity.Null;
            subnetIndex = -1;
            ownerPrefabName = "<unknown>";

            Entity current = edge;
            for (int depth = 0; depth < 8; depth++)
            {
                if (!EntityManager.HasComponent<Owner>(current))
                {
                    return false;
                }

                Owner ownerComponent = EntityManager.GetComponentData<Owner>(current);
                current = ownerComponent.m_Owner;
                if (current == Entity.Null || !EntityManager.Exists(current))
                {
                    return false;
                }

                if (!EntityManager.HasBuffer<Game.Net.SubNet>(current))
                {
                    continue;
                }

                DynamicBuffer<Game.Net.SubNet> subNets = EntityManager.GetBuffer<Game.Net.SubNet>(current, true);
                for (int i = 0; i < subNets.Length; i++)
                {
                    if (subNets[i].m_SubNet != edge)
                    {
                        continue;
                    }

                    if (!EntityManager.HasComponent<PrefabRef>(current))
                    {
                        return false;
                    }

                    PrefabRef ownerPrefabRef = EntityManager.GetComponentData<PrefabRef>(current);
                    bool supportedOwner = EntityManager.HasComponent<BuildingData>(ownerPrefabRef.m_Prefab) ||
                                          EntityManager.HasComponent<BuildingExtensionData>(ownerPrefabRef.m_Prefab);
                    if (!supportedOwner)
                    {
                        LogSkipOnce(
                            $"owner|{current.Index}|{edge.Index}",
                            $"[NativeTool] Skipped subnet owner={DiagnosticFormat.Entity(current)} edge={DiagnosticFormat.Entity(edge)} reason=owner-prefab-not-building-or-extension ownerPrefab={DiagnosticFormat.PrefabName(prefabSystem, ownerPrefabRef.m_Prefab)}.");
                        return false;
                    }

                    owner = current;
                    subnetIndex = i;
                    ownerPrefabName = DiagnosticFormat.PrefabName(prefabSystem, ownerPrefabRef.m_Prefab);
                    return true;
                }
            }

            return false;
        }

        [Conditional("DEBUG")]
        private void LogSkipOnce(string key, string message)
        {
            int frame = UnityEngine.Time.frameCount;
            if (skipLogFrames.TryGetValue(key, out int lastFrame) && frame - lastFrame < 300)
            {
                return;
            }

            skipLogFrames[key] = frame;
            Mod.LogDebugInfo(message);
        }

        [Conditional("DEBUG")]
        private void LogDisabledSkipOnce(string source)
        {
            LogSkipOnce(
                $"disabled|{source}",
                $"[NativeTool] Skipped internal-road patch path source={source} reason=feature-disabled.");
        }

        private void ClearPendingVerification()
        {
            pendingVerifications.Clear();
        }

        private static void ClearRuntimeState(InternalRoadNativeToolPatchSystem targetInstance = null)
        {
            AllowedEdges.Clear();
            (targetInstance ?? instance)?.ClearPendingVerification();
        }

        private static void ResetRuntimeState(string reason, InternalRoadNativeToolPatchSystem targetInstance = null)
        {
            bool wasEnabled = internalRoadsEnabled;
            internalRoadsEnabled = false;
            ClearRuntimeState(targetInstance);
            Mod.LogDebugInfo($"[NativeTool] Runtime state reset reason={reason} wasEnabled={wasEnabled}.");
        }

        private static void RegisterAllowedEdge(AllowedInternalEdge allowed)
        {
            AllowedEdges.Add(allowed, UnityEngine.Time.frameCount);
        }

        private static bool IsAllowedEdge(Entity edge)
        {
            return AllowedEdges.ContainsEdge(edge, UnityEngine.Time.frameCount);
        }

        private static bool HasRecentAllowedEdge()
        {
            return AllowedEdges.HasRecent(UnityEngine.Time.frameCount);
        }

        private void QueueVerification(AllowedInternalEdge allowed)
        {
            for (int i = pendingVerifications.Count - 1; i >= 0; i--)
            {
                PendingInstanceVerification existing = pendingVerifications[i];
                if (existing.Allowed.Owner == allowed.Owner && existing.Allowed.SubnetIndex == allowed.SubnetIndex)
                {
                    pendingVerifications.RemoveAt(i);
                }
            }

            pendingVerifications.Add(new PendingInstanceVerification
            {
                Allowed = allowed,
                Attempts = 0
            });
        }

        private void QueueRecentVerification(NetPrefab targetPrefab)
        {
            if (targetPrefab == null || !prefabSystem.TryGetEntity(targetPrefab, out Entity targetPrefabEntity))
            {
                return;
            }

            if (!AllowedEdges.TryGetMostRecentByTarget(targetPrefabEntity, UnityEngine.Time.frameCount, out AllowedInternalEdge best))
            {
                return;
            }

            QueueVerification(best);
            Mod.LogDebugInfo($"[NativeTool.Verify] queued owner={DiagnosticFormat.Entity(best.Owner)} ownerPrefab={best.OwnerPrefabName} subnetIndex={best.SubnetIndex} sourceEdge={DiagnosticFormat.Entity(best.Edge)} targetPrefab={best.TargetPrefabName} targetIsUpgrade={best.TargetIsUpgrade}.");
        }

        private void ProcessPendingVerification()
        {
            for (int i = pendingVerifications.Count - 1; i >= 0; i--)
            {
                PendingInstanceVerification pending = pendingVerifications[i];
                pending.Attempts++;

                string mismatch = GetVerificationMismatch(pending.Allowed);
                if (mismatch == null)
                {
                    Mod.LogInfo($"[NativeTool.Verify] matched ownerPrefab={pending.Allowed.OwnerPrefabName} subnetIndex={pending.Allowed.SubnetIndex} targetPrefab={pending.Allowed.TargetPrefabName} targetIsUpgrade={pending.Allowed.TargetIsUpgrade} attempts={pending.Attempts}.");
                    Mod.LogDebugInfo($"[NativeTool.Verify] matched owner={DiagnosticFormat.Entity(pending.Allowed.Owner)} ownerPrefab={pending.Allowed.OwnerPrefabName} subnetIndex={pending.Allowed.SubnetIndex} targetPrefab={pending.Allowed.TargetPrefabName} targetIsUpgrade={pending.Allowed.TargetIsUpgrade} attempts={pending.Attempts}.");
                    pendingVerifications.RemoveAt(i);
                    continue;
                }

                if (pending.Attempts >= VerificationAttempts)
                {
                    Mod.LogInfo($"[NativeTool.Verify] [ERROR] exhausted owner={DiagnosticFormat.Entity(pending.Allowed.Owner)} ownerPrefab={pending.Allowed.OwnerPrefabName} subnetIndex={pending.Allowed.SubnetIndex} targetPrefab={pending.Allowed.TargetPrefabName} targetIsUpgrade={pending.Allowed.TargetIsUpgrade} attempts={pending.Attempts} mismatch=\"{mismatch}\".");
                    pendingVerifications.RemoveAt(i);
                    continue;
                }

                pendingVerifications[i] = pending;
            }
        }

        private string GetVerificationMismatch(AllowedInternalEdge allowed)
        {
            if (!EntityManager.Exists(allowed.Owner) || !EntityManager.HasBuffer<Game.Net.SubNet>(allowed.Owner))
            {
                return "owner-subnet-buffer-missing";
            }

            DynamicBuffer<Game.Net.SubNet> subNets = EntityManager.GetBuffer<Game.Net.SubNet>(allowed.Owner, true);
            if (allowed.SubnetIndex < 0 || allowed.SubnetIndex >= subNets.Length)
            {
                return $"subnet-index-out-of-range length={subNets.Length}";
            }

            Entity currentEdge = subNets[allowed.SubnetIndex].m_SubNet;
            if (currentEdge == Entity.Null || !EntityManager.Exists(currentEdge))
            {
                return $"subnet-entity-missing edge={DiagnosticFormat.Entity(currentEdge)}";
            }

            if (!EntityManager.HasComponent<PrefabRef>(currentEdge))
            {
                return $"prefab-ref-missing edge={DiagnosticFormat.Entity(currentEdge)}";
            }

            PrefabRef currentPrefabRef = EntityManager.GetComponentData<PrefabRef>(currentEdge);
            if (allowed.TargetIsUpgrade)
            {
                if (!EntityManager.HasComponent<Upgraded>(currentEdge))
                {
                    return $"upgrade-component-missing edge={DiagnosticFormat.Entity(currentEdge)} prefab={DiagnosticFormat.PrefabName(prefabSystem, currentPrefabRef.m_Prefab)}";
                }

                return null;
            }

            if (currentPrefabRef.m_Prefab != allowed.TargetPrefab)
            {
                return $"prefab expected={DiagnosticFormat.Entity(allowed.TargetPrefab)} actual={DiagnosticFormat.Entity(currentPrefabRef.m_Prefab)} actualName={DiagnosticFormat.PrefabName(prefabSystem, currentPrefabRef.m_Prefab)}";
            }

            return null;
        }

        private sealed class AllowedEdgeHistory
        {
            private readonly object sync = new object();
            private readonly List<AllowedInternalEdge> edges = new List<AllowedInternalEdge>();
            private readonly int lifetimeFrames;

            public AllowedEdgeHistory(int lifetimeFrames)
            {
                this.lifetimeFrames = lifetimeFrames;
            }

            public void Add(AllowedInternalEdge allowed, int frame)
            {
                lock (sync)
                {
                    PruneExpiredNoLock(frame);
                    edges.Add(allowed);
                }
            }

            public bool ContainsEdge(Entity edge, int frame)
            {
                lock (sync)
                {
                    for (int i = edges.Count - 1; i >= 0; i--)
                    {
                        AllowedInternalEdge allowed = edges[i];
                        if (RemoveIfExpiredNoLock(i, allowed, frame))
                        {
                            continue;
                        }

                        if (allowed.Edge == edge)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            public bool HasRecent(int frame)
            {
                lock (sync)
                {
                    for (int i = edges.Count - 1; i >= 0; i--)
                    {
                        if (RemoveIfExpiredNoLock(i, edges[i], frame))
                        {
                            continue;
                        }

                        return true;
                    }
                }

                return false;
            }

            public bool TryGetMostRecentByTarget(Entity targetPrefab, int frame, out AllowedInternalEdge result)
            {
                lock (sync)
                {
                    for (int i = edges.Count - 1; i >= 0; i--)
                    {
                        AllowedInternalEdge allowed = edges[i];
                        if (RemoveIfExpiredNoLock(i, allowed, frame))
                        {
                            continue;
                        }

                        if (allowed.TargetPrefab == targetPrefab)
                        {
                            result = allowed;
                            return true;
                        }
                    }
                }

                result = default;
                return false;
            }

            public void Clear()
            {
                lock (sync)
                {
                    edges.Clear();
                }
            }

            private void PruneExpiredNoLock(int frame)
            {
                for (int i = edges.Count - 1; i >= 0; i--)
                {
                    RemoveIfExpiredNoLock(i, edges[i], frame);
                }
            }

            private bool RemoveIfExpiredNoLock(int index, AllowedInternalEdge allowed, int frame)
            {
                if (frame - allowed.Frame <= lifetimeFrames)
                {
                    return false;
                }

                edges.RemoveAt(index);
                return true;
            }
        }

        private struct AllowedInternalEdge
        {
            public Entity Edge;
            public Entity Owner;
            public int SubnetIndex;
            public Entity TargetPrefab;
            public string OwnerPrefabName;
            public string SourcePrefabName;
            public string TargetPrefabName;
            public string SourceProfile;
            public string TargetProfile;
            public bool TargetIsUpgrade;
            public int Frame;
        }

        private struct PendingInstanceVerification
        {
            public AllowedInternalEdge Allowed;
            public int Attempts;
        }
    }
}
