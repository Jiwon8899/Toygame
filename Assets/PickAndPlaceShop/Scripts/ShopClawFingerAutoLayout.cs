using System;
using UnityEngine;

namespace PickAndPlaceShop
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ShopClawFingerAutoLayout : MonoBehaviour
    {
        [SerializeField] private Transform[] fingers = Array.Empty<Transform>();
        [Min(0.05f)] [SerializeField] private float radius = 0.69f;
        [SerializeField] private float height = -0.38f;
        [Range(0f, 180f)] [SerializeField] private float tiltAngle = 120f;

        public Transform[] Fingers => fingers;
        public float Radius => radius;
        public float Height => height;
        public float TiltAngle => tiltAngle;

        [ContextMenu("발톱 대칭 정렬 적용")]
        public void ApplyLayout()
        {
            if (fingers == null || fingers.Length == 0)
            {
                Debug.LogError("[ClawLayout] 정렬할 발톱이 없습니다.", this);
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RecordObjects(fingers, "집게발 대칭 정렬");
#endif

            float step = 360f / fingers.Length;
            for (int index = 0; index < fingers.Length; index++)
            {
                Transform finger = fingers[index];
                if (finger == null)
                {
                    Debug.LogError("[ClawLayout] 발톱 배열에 빈 참조가 있습니다. index=" + index, this);
                    continue;
                }

                float angle = step * index;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 localPosition = new(
                    Mathf.Sin(radians) * radius,
                    height,
                    Mathf.Cos(radians) * radius);
                Quaternion localRotation = Quaternion.Euler(tiltAngle, angle, 0f);
                finger.SetPositionAndRotation(
                    transform.TransformPoint(localPosition),
                    transform.rotation * localRotation);

#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(finger);
#endif
            }

            Debug.Log("[ClawLayout] ALIGNED count=" + fingers.Length +
                      " radius=" + radius.ToString("F3") +
                      " height=" + height.ToString("F3") +
                      " tilt=" + tiltAngle.ToString("F1"), this);
        }

#if UNITY_EDITOR
        public void EditorConfigure(Transform[] targets, float layoutRadius,
            float layoutHeight, float layoutTilt)
        {
            fingers = targets ?? Array.Empty<Transform>();
            radius = Mathf.Max(0.05f, layoutRadius);
            height = layoutHeight;
            tiltAngle = Mathf.Clamp(layoutTilt, 0f, 180f);
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
