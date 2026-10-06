using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ReplayLogger
{

    internal static class BossTrackingHelpers
    {
        internal static bool ShouldTrackHealthManager(HealthManager manager)
        {
            if (manager == null || manager.gameObject == null || manager.hp <= 0 || manager.isDead)
            {
                return false;
            }

            HeroController hero = HeroController.instance;
            GameObject heroObject = hero?.gameObject;
            if (heroObject == null)
            {
                return true;
            }

            if (ReferenceEquals(manager.gameObject, heroObject))
            {
                return false;
            }

            Transform managerRoot = manager.gameObject.transform?.root;
            Transform heroRoot = heroObject.transform?.root;
            return managerRoot == null || heroRoot == null || !ReferenceEquals(managerRoot, heroRoot);
        }

        internal static void EnsureUniqueBossBuffers(
    Dictionary<HealthManager, (int maxHP, int lastHP)> infoBoss,
    Dictionary<GameObject, HealthManager> uniqueBossByGameObject,
    HashSet<HealthManager> uniqueBossSet,
    ref bool uniqueBossBuffersDirty)
        {
            if (!uniqueBossBuffersDirty)
            {
                return;
            }

            uniqueBossByGameObject.Clear();
            uniqueBossSet.Clear();

            foreach (HealthManager boss in infoBoss.Keys)
            {
                if (boss == null)
                {
                    continue;
                }

                uniqueBossByGameObject[boss.gameObject] = boss;
            }

            foreach (HealthManager boss in uniqueBossByGameObject.Values)
            {
                uniqueBossSet.Add(boss);
            }

            uniqueBossBuffersDirty = false;
        }

        internal const float EnemyHealthCacheCleanupTickSeconds = 0.25f;
        internal const int EnemyHealthCacheCleanupMinSize = 128;
        internal const int EnemyHealthCacheCleanupBatchSize = 64;

        internal static void CleanupEnemyHealthManagerCacheIfNeeded(
            Dictionary<GameObject, HealthManager> enemyHealthManagerByGameObject,
            List<GameObject> cleanupBuffer,
            ref float lastCleanupTime,
            ref int cleanupCursor,
            float now)
        {
            if (enemyHealthManagerByGameObject.Count < EnemyHealthCacheCleanupMinSize)
            {
                cleanupCursor = 0;
                return;
            }

            if (now - lastCleanupTime < EnemyHealthCacheCleanupTickSeconds)
            {
                return;
            }

            lastCleanupTime = now;
            cleanupBuffer.Clear();
            int startIndex = cleanupCursor;
            int endIndexExclusive = startIndex + EnemyHealthCacheCleanupBatchSize;
            int index = 0;
            foreach (var pair in enemyHealthManagerByGameObject)
            {
                if (index < startIndex)
                {
                    index++;
                    continue;
                }

                if (index >= endIndexExclusive)
                {
                    break;
                }

                if (pair.Key == null || pair.Value == null)
                {
                    cleanupBuffer.Add(pair.Key);
                }

                index++;
            }

            foreach (GameObject key in cleanupBuffer)
            {
                enemyHealthManagerByGameObject.Remove(key);
            }

            if (index < endIndexExclusive)
            {
                cleanupCursor = 0;
                return;
            }

            int remainingCount = enemyHealthManagerByGameObject.Count;
            cleanupCursor = endIndexExclusive >= remainingCount ? 0 : endIndexExclusive;
        }

        internal static string FormatState(int state)
        {
            return state switch
            {
                1 => "On",
                0 => "Off",
                _ => "N/A"
            };
        }

        internal const int EnemyColliderBufferMaxSize = 32768;

        internal static int CollectEnemyCollidersNonAlloc(ref Collider2D[] buffer, Vector2 center, Vector2 size, int layerMask)
        {
            int colliderCount = Physics2D.OverlapBoxNonAlloc(center, size, 0f, buffer, layerMask);
            while (colliderCount >= buffer.Length && buffer.Length < EnemyColliderBufferMaxSize)
            {
                int nextSize = Math.Min(buffer.Length * 2, EnemyColliderBufferMaxSize);
                buffer = new Collider2D[nextSize];
                colliderCount = Physics2D.OverlapBoxNonAlloc(center, size, 0f, buffer, layerMask);
            }

            return colliderCount;
        }

        internal static GameObject ResolveHeroBoxObject(HeroController hero, ref Transform cachedHeroTransform, ref GameObject cachedHeroBoxObject)
        {
            if (hero == null)
            {
                cachedHeroTransform = null;
                cachedHeroBoxObject = null;
                return null;
            }

            Transform heroTransform = hero.transform;
            if (cachedHeroTransform != heroTransform)
            {
                cachedHeroTransform = heroTransform;
                cachedHeroBoxObject = null;
            }

            if (cachedHeroBoxObject == null)
            {
                Transform heroBoxTransform = heroTransform.Find("HeroBox");
                cachedHeroBoxObject = heroBoxTransform != null ? heroBoxTransform.gameObject : null;
            }

            return cachedHeroBoxObject;
        }

        internal static int BuildHeroCollisionLayerMask(int heroLayer)
        {
            if ((uint)heroLayer >= 32u)
            {
                return Physics2D.AllLayers;
            }

            int mask = 0;
            for (int layer = 0; layer < 32; layer++)
            {
                if (!Physics2D.GetIgnoreLayerCollision(heroLayer, layer))
                {
                    mask |= 1 << layer;
                }
            }

            return mask != 0 ? mask : Physics2D.AllLayers;
        }

        private static readonly string[] HitTargetMemberNames =
        {
            "Target",
            "target",
            "TargetObject",
            "targetObject",
            "TargetCollider",
            "targetCollider",
            "Other",
            "other",
            "GameObject",
            "gameObject"
        };

        private static readonly Dictionary<Type, MemberInfo> HitTargetMemberByType = new();
        private static readonly HashSet<Type> HitTargetMemberMissTypes = new();

        internal static object GetCachedHitTargetRaw(object boxedHit, Type hitType)
        {
            if (boxedHit == null || hitType == null)
            {
                return null;
            }

            if (HitTargetMemberByType.TryGetValue(hitType, out MemberInfo cachedMember))
            {
                return ReadHitTargetMemberValue(cachedMember, boxedHit);
            }

            if (HitTargetMemberMissTypes.Contains(hitType))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string memberName in HitTargetMemberNames)
            {
                FieldInfo field = hitType.GetField(memberName, flags);
                if (field != null)
                {
                    object fieldValue = ReadHitTargetMemberValue(field, boxedHit);
                    if (fieldValue != null)
                    {
                        HitTargetMemberByType[hitType] = field;
                        return fieldValue;
                    }
                }

                PropertyInfo property = hitType.GetProperty(memberName, flags);
                if (property == null || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object propertyValue = ReadHitTargetMemberValue(property, boxedHit);
                if (propertyValue != null)
                {
                    HitTargetMemberByType[hitType] = property;
                    return propertyValue;
                }
            }

            foreach (FieldInfo field in hitType.GetFields(flags))
            {
                object fieldValue = ReadHitTargetMemberValue(field, boxedHit);
                if (IsPotentialHitTargetValue(fieldValue))
                {
                    HitTargetMemberByType[hitType] = field;
                    return fieldValue;
                }
            }

            foreach (PropertyInfo property in hitType.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object propertyValue = ReadHitTargetMemberValue(property, boxedHit);
                if (IsPotentialHitTargetValue(propertyValue))
                {
                    HitTargetMemberByType[hitType] = property;
                    return propertyValue;
                }
            }

            HitTargetMemberMissTypes.Add(hitType);
            return null;
        }

        internal static object ReadHitTargetMemberValue(MemberInfo memberInfo, object boxedHit)
        {
            if (memberInfo == null || boxedHit == null)
            {
                return null;
            }

            try
            {
                return memberInfo switch
                {
                    FieldInfo field => field.GetCachedValue(boxedHit),
                    PropertyInfo property => property.GetCachedValue(boxedHit),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryUnwrapTargetGameObject(object rawTarget, out GameObject targetObject)
        {
            return TryExtractGameObject(rawTarget, depth: 2, out targetObject);
        }

        internal static bool IsPotentialHitTargetValue(object value)
        {
            return value is GameObject or Transform or Component or HealthManager;
        }

        internal static bool TryExtractGameObject(object value, int depth, out GameObject gameObject)
        {
            if (value == null || depth < 0)
            {
                gameObject = null;
                return false;
            }

            switch (value)
            {
                case GameObject directGameObject:
                    gameObject = directGameObject;
                    return gameObject != null;
                case Transform directTransform:
                    gameObject = directTransform.gameObject;
                    return gameObject != null;
                case Component directComponent:
                    gameObject = directComponent.gameObject;
                    return gameObject != null;
            }

            if (depth == 0)
            {
                gameObject = null;
                return false;
            }

            Type valueType = value.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] nestedNames =
            {
                "gameObject",
                "GameObject",
                "transform",
                "Transform",
                "target",
                "Target",
                "other",
                "Other"
            };

            foreach (string nestedName in nestedNames)
            {
                FieldInfo field = valueType.GetField(nestedName, flags);
                if (field != null && TryExtractGameObject(ReadHitTargetMemberValue(field, value), depth - 1, out gameObject))
                {
                    return true;
                }

                PropertyInfo property = valueType.GetProperty(nestedName, flags);
                if (property != null &&
                    property.GetIndexParameters().Length == 0 &&
                    TryExtractGameObject(ReadHitTargetMemberValue(property, value), depth - 1, out gameObject))
                {
                    return true;
                }
            }

            gameObject = null;
            return false;
        }
    }
}
