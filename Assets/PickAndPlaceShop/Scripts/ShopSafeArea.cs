using UnityEngine;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class ShopSafeArea : MonoBehaviour
    {
        private Rect lastSafeArea;
        private Vector2Int lastScreen;

        private void OnEnable() => Apply();

        private void Update()
        {
            if (lastSafeArea != Screen.safeArea || lastScreen.x != Screen.width || lastScreen.y != Screen.height) Apply();
        }

        private void Apply()
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            Rect safe = Screen.safeArea;
            if (safe.width <= 1f || safe.height <= 1f)
                safe = new Rect(0f, 0f, width, height);
            safe.xMin = Mathf.Clamp(safe.xMin, 0f, width);
            safe.xMax = Mathf.Clamp(safe.xMax, 0f, width);
            safe.yMin = Mathf.Clamp(safe.yMin, 0f, height);
            safe.yMax = Mathf.Clamp(safe.yMax, 0f, height);
            RectTransform rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / width, safe.yMin / height);
            rect.anchorMax = new Vector2(safe.xMax / width, safe.yMax / height);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            lastSafeArea = safe;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
        }
    }
}
