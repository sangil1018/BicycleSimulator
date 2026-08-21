using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetTools.Editor
{
    /// <summary>
    /// 씬 오브젝트를 프리팹으로 교체하거나, 트랜스폼에 랜덤 변형을 주는 기능.
    ///
    /// 모두 씬 계층에서만 의미가 있는 동작이라 <see cref="AssetToolsSelection"/> 대신
    /// Hierarchy 선택(GameObject 배열)을 그대로 받는다.
    /// </summary>
    public static class AssetReplacerOps
    {
        public enum BaseMode { Current = 0, Reference = 1 }

        /// <summary>이동·회전·크기 각각의 랜덤 변형 설정.</summary>
        [System.Serializable]
        public class RandomGroup
        {
            public bool enabled;

            /// <summary>true 면 min/max 를 퍼센트가 아닌 실제 단위로 사용.</summary>
            public bool absolute;

            public BaseMode baseMode = BaseMode.Reference;
            public Vector3 reference = Vector3.one;
            public float min = -10f;
            public float max = 10f;
            public bool x = true, y = true, z = true;
        }

        /// <summary>탭에 표시되는 설정 전체. EditorPrefs 에 JSON 으로 저장된다.</summary>
        [System.Serializable]
        public class Settings
        {
            public bool randomFoldout = true;
            public RandomGroup move = new RandomGroup { reference = Vector3.one };
            public RandomGroup rotation = new RandomGroup { absolute = true, min = 0f, max = 360f, x = false, z = false };
            public RandomGroup scale = new RandomGroup { baseMode = BaseMode.Current };
            public bool linkXZ = true;
            public bool useSeed;
            public int seed;

            /// <summary>교체할 프리팹의 GUID. 오브젝트 참조는 EditorPrefs 에 담을 수 없어 경로로 복원한다.</summary>
            public string prefabGuid = "";

            public bool AnyRandomGroup => move.enabled || rotation.enabled || scale.enabled;
        }

        /// <summary>선택한 오브젝트를 프리팹 인스턴스로 교체한다. 이름·월드 트랜스폼은 유지.</summary>
        public static void Replace(GameObject[] targets, GameObject prefab)
        {
            if (prefab == null || targets == null || targets.Length == 0) return;

            // 파괴가 시작되기 전에 트랜스폼을 먼저 스냅샷한다.
            // 원본도 함께 담아둬야 null 이 섞여 있어도 대상과 스냅샷이 어긋나지 않는다.
            var snapshots = new List<(GameObject source, Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale)>();
            foreach (var go in targets)
            {
                if (go == null) continue;
                var t = go.transform;
                snapshots.Add((go, t.parent, go.name, t.position, t.rotation, t.lossyScale));
            }

            if (snapshots.Count == 0) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("에셋 리플레이서 적용");
            int undoGroup = Undo.GetCurrentGroup();

            var newObjects = new List<GameObject>();

            for (int i = 0; i < snapshots.Count; i++)
            {
                var (source, parent, origName, pos, rot, worldScale) = snapshots[i];

                Undo.DestroyObjectImmediate(source);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                Undo.RegisterCreatedObjectUndo(instance, "");

                instance.name = origName;
                instance.transform.position = pos;
                instance.transform.rotation = rot;
                instance.transform.localScale = ToLocalScale(worldScale, parent);

                newObjects.Add(instance);
            }

            Undo.CollapseUndoOperations(undoGroup);

            Selection.objects = newObjects.ToArray();
            if (newObjects.Count > 0) EditorGUIUtility.PingObject(newObjects[0]);
        }

        /// <summary>월드 스케일을 부모 기준 로컬 스케일로 변환한다.</summary>
        static Vector3 ToLocalScale(Vector3 worldScale, Transform parent)
        {
            if (parent == null) return worldScale;

            var p = parent.lossyScale;
            return new Vector3(
                p.x != 0f ? worldScale.x / p.x : worldScale.x,
                p.y != 0f ? worldScale.y / p.y : worldScale.y,
                p.z != 0f ? worldScale.z / p.z : worldScale.z);
        }

        /// <summary>선택한 오브젝트들의 평균 위치에 빈 오브젝트를 만든다.</summary>
        public static GameObject CreateCenterEmpty(GameObject[] targets)
        {
            if (targets == null || targets.Length == 0) return null;

            var center = Vector3.zero;
            int count = 0;
            foreach (var go in targets)
            {
                if (go == null) continue;
                center += go.transform.position;
                count++;
            }

            if (count == 0) return null;
            center /= count;

            var empty = new GameObject("Center");
            Undo.RegisterCreatedObjectUndo(empty, "센터 빈 오브젝트 생성");
            empty.transform.position = center;

            Selection.activeGameObject = empty;
            EditorGUIUtility.PingObject(empty);
            return empty;
        }

        /// <summary>선택한 오브젝트마다 축별로 랜덤값을 뽑아 트랜스폼에 적용한다.</summary>
        public static int ApplyRandom(GameObject[] targets, Settings settings)
        {
            if (targets == null || targets.Length == 0 || !settings.AnyRandomGroup) return 0;

            if (settings.useSeed) Random.InitState(settings.seed);

            var transforms = new List<Transform>(targets.Length);
            foreach (var go in targets)
            {
                if (go != null) transforms.Add(go.transform);
            }

            if (transforms.Count == 0) return 0;

            Undo.RecordObjects(transforms.ToArray(), "랜덤 변형 적용");

            foreach (var t in transforms)
            {
                if (settings.move.enabled)
                {
                    var r = RandomValue(settings.move, false);
                    if (settings.move.absolute)
                    {
                        t.localPosition += r;
                    }
                    else
                    {
                        var b = settings.move.baseMode == BaseMode.Current ? t.localPosition : settings.move.reference;
                        t.localPosition += new Vector3(b.x * r.x, b.y * r.y, b.z * r.z);
                    }
                }

                if (settings.rotation.enabled)
                {
                    var r = RandomValue(settings.rotation, false);
                    if (settings.rotation.absolute)
                    {
                        t.localEulerAngles += r;
                    }
                    else
                    {
                        var b = settings.rotation.baseMode == BaseMode.Current ? t.localEulerAngles : settings.rotation.reference;
                        t.localEulerAngles += new Vector3(b.x * r.x, b.y * r.y, b.z * r.z);
                    }
                }

                if (settings.scale.enabled)
                {
                    var r = RandomValue(settings.scale, settings.linkXZ);
                    var s = t.localScale;

                    if (settings.scale.absolute)
                    {
                        if (settings.scale.x) s.x = r.x;
                        if (settings.scale.y) s.y = r.y;
                        if (settings.scale.z) s.z = r.z;
                    }
                    else
                    {
                        var b = settings.scale.baseMode == BaseMode.Current ? t.localScale : settings.scale.reference;
                        if (settings.scale.x) s.x = b.x * (1f + r.x);
                        if (settings.scale.y) s.y = b.y * (1f + r.y);
                        if (settings.scale.z) s.z = b.z * (1f + r.z);
                    }

                    t.localScale = s;
                }

                EditorUtility.SetDirty(t);
            }

            return transforms.Count;
        }

        /// <summary>
        /// 축별 랜덤값. 퍼센트 모드면 비율(%/100), 절대값 모드면 입력값 그대로.
        /// 꺼진 축은 0, X·Z 연동 시 Z 는 X 와 동일.
        /// </summary>
        static Vector3 RandomValue(RandomGroup g, bool link)
        {
            float scale = g.absolute ? 1f : 0.01f;
            float rx = g.x ? Random.Range(g.min, g.max) * scale : 0f;
            float ry = g.y ? Random.Range(g.min, g.max) * scale : 0f;
            float rz = link ? rx : (g.z ? Random.Range(g.min, g.max) * scale : 0f);
            return new Vector3(rx, ry, rz);
        }
    }
}
