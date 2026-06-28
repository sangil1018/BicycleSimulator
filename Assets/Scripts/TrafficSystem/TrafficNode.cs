using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TrafficSystem
{
    public class TrafficNode : MonoBehaviour
    {
        [Serializable]
        public struct Exit
        {
            public TrafficNode node;
            [Range(1, 100)]
            [Tooltip("선택 확률 가중치. 연결된 exits의 가중치 합계 기준으로 정규화됩니다.")]
            public int weight;
        }

        [Tooltip("다음 노드 목록. 가중치에 비례해 확률 선택됩니다. 비어있으면 경로 끝.")]
        [SerializeField] Exit[] exits;
        [Tooltip("이 노드에서 대기해야 하는 차량 신호. null이면 자유 통과.")]
        [SerializeField] TrafficSignal stopSignal;

        public TrafficSignal StopSignal => stopSignal;
        public bool IsEndOfPath
        {
            get
            {
                if (exits == null || exits.Length == 0) return true;
                foreach (var e in exits)
                    if (e.node != null) return false;
                return true;
            }
        }

        // 가중치 기반 랜덤 선택. 연결 노드 없으면 null (경로 끝 → 리스폰 트리거).
        public TrafficNode PickNext()
        {
            if (exits == null || exits.Length == 0) return null;

            int total = 0;
            foreach (var e in exits)
                if (e.node != null) total += Mathf.Max(1, e.weight);

            if (total <= 0) return null;

            int r = Random.Range(0, total);
            foreach (var e in exits)
            {
                if (e.node == null) continue;
                r -= Mathf.Max(1, e.weight);
                if (r < 0) return e.node;
            }
            // 부동소수점 오차 보정 — 마지막 유효 exits 반환
            for (int i = exits.Length - 1; i >= 0; i--)
                if (exits[i].node != null) return exits[i].node;
            return null;
        }

        // 첫 번째 exits[0] 방향으로 GameObject 회전
        public void AutoOrient()
        {
            if (exits == null || exits.Length == 0 || exits[0].node == null) return;
            Vector3 dir = exits[0].node.transform.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // 노드 구: stopSignal 있으면 빨강, 없으면 노랑
            Gizmos.color = stopSignal != null
                ? new Color(1f, 0.2f, 0.2f, 0.9f)
                : new Color(1f, 0.85f, 0f, 0.9f);
            Gizmos.DrawSphere(transform.position, 0.4f);

            if (exits == null) return;
            foreach (var e in exits)
            {
                if (e.node == null) continue;
                Gizmos.color = new Color(1f, 0.85f, 0f, 0.6f);
                Gizmos.DrawLine(transform.position, e.node.transform.position);
            }

            // stopSignal 연결선
            if (stopSignal != null)
            {
                Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
                Gizmos.DrawLine(transform.position, stopSignal.transform.position);
            }
        }
#endif
    }
}
