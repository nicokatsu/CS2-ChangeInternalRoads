using Game.Prefabs;
using Unity.Entities;

namespace ChangeInternalRoads.Diagnostics
{
    internal static class DiagnosticFormat
    {
        public static string Entity(Entity entity)
        {
            return entity == Unity.Entities.Entity.Null
                ? "Entity.Null"
                : $"Entity(index={entity.Index},version={entity.Version})";
        }

        public static string PrefabName(PrefabSystem prefabSystem, Entity prefabEntity)
        {
            if (prefabEntity == Unity.Entities.Entity.Null)
            {
                return "<null prefab>";
            }

            if (prefabSystem.TryGetPrefab(prefabEntity, out PrefabBase prefabBase))
            {
                return prefabBase.name;
            }

            return $"<unresolved {Entity(prefabEntity)}>";
        }
    }
}
