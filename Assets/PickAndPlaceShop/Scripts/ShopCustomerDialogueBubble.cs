using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public sealed class ShopCustomerDialogueBubble : MonoBehaviour
    {
        private static readonly List<ShopCustomerDialogueBubble> Active = new();
        private float expiresAt;

        public static void Show(Transform customer, string message, float duration, int maximumVisible)
        {
            if (customer == null || string.IsNullOrWhiteSpace(message)) return;
            for (int i = Active.Count - 1; i >= 0; i--)
                if (Active[i] == null) Active.RemoveAt(i);
            while (Active.Count >= Mathf.Max(1, maximumVisible))
            {
                ShopCustomerDialogueBubble oldest = Active[0];
                Active.RemoveAt(0);
                if (oldest != null) Destroy(oldest.gameObject);
            }

            GameObject root = new("Customer Dialogue Bubble", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image));
            root.transform.SetParent(customer, false);
            root.transform.localPosition = new Vector3(0f, 2.25f, 0f);
            root.transform.localScale = Vector3.one * 0.0045f;
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(420f, 104f);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 12f;
            Image background = root.GetComponent<Image>();
            background.color = new Color(0.035f, 0.075f, 0.10f, 0.94f);
            background.raycastTarget = false;

            GameObject labelObject = new("Dialogue", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(root.transform, false);
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(22f, 12f);
            labelRect.offsetMax = new Vector2(-22f, -12f);
            Text label = labelObject.GetComponent<Text>();
            label.font = ShopKoreanFontApplier.KoreanFont != null
                ? ShopKoreanFontApplier.KoreanFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 27;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.93f, 1f, 0.98f, 1f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            label.text = message;

            ShopCustomerDialogueBubble bubble = root.AddComponent<ShopCustomerDialogueBubble>();
            bubble.expiresAt = Time.unscaledTime + Mathf.Max(1f, duration);
            Active.Add(bubble);
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                Vector3 direction = transform.position - camera.transform.position;
                if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
            }
            if (Time.unscaledTime >= expiresAt) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Active.Remove(this);
        }
    }
}
