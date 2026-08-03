using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PickAndPlaceShop
{
    public sealed class ShopKujiScratchView : MonoBehaviour
    {
        private const int Columns = 12;
        private const int Rows = 6;
        private const float ScratchRadius = 34f;

        [SerializeField] private ShopKujiStationNetwork station;
        [SerializeField] private Font uiFont;

        private CanvasGroup canvasGroup;
        private RectTransform panel;
        private RectTransform scratchArea;
        private RectTransform scratchCursor;
        private Text titleText;
        private Text instructionText;
        private Text resultText;
        private Text progressText;
        private Image rankGlow;
        private readonly List<Image> coverTiles = new();
        private readonly List<Image> trails = new();
        private bool[] scratched;
        private int scratchedCount;
        private int observedAttempt = -1;
        private bool visible;
        private float nextProgressSendTime;
        private int trailIndex;

#if UNITY_EDITOR
        public void EditorConfigure(ShopKujiStationNetwork target, Font font)
        {
            station = target;
            uiFont = font;
        }
#endif

        private void Update()
        {
            if (station == null || !station.IsSpawned || NetworkManager.Singleton == null) return;
            bool isOccupant = station.OccupantClientId.Value == NetworkManager.Singleton.LocalClientId;
            ShopKujiState state = station.State.Value;
            bool shouldShow = isOccupant && state != ShopKujiState.Idle && state != ShopKujiState.DrawingTicket &&
                              state != ShopKujiState.Cooldown;
            SetVisible(shouldShow);
            if (!shouldShow) return;

            EnsureBuilt();
            if (observedAttempt != station.AttemptId.Value) ResetTicket(station.AttemptId.Value);
            UpdatePresentation(state);
            if (state == ShopKujiState.AwaitingScratch || state == ShopKujiState.Scratching)
                ProcessScratchInput();
        }

        private void EnsureBuilt()
        {
            if (canvasGroup != null) return;
            GameObject canvasObject = new("쿠지 긁기 화면", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGroup = canvasObject.GetComponent<CanvasGroup>();

            panel = CreateImage("쿠지 패널", canvasObject.transform, new Vector2(760f, 560f),
                new Color(0.035f, 0.055f, 0.11f, 0.97f)).rectTransform;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;

            titleText = CreateText("제목", panel, "쿠지 티켓 긁기", 42, new Vector2(680f, 64f),
                new Vector2(0f, 222f), Color.white);
            instructionText = CreateText("안내", panel, "왼쪽 마우스를 누른 채 은박을 긁으세요", 25,
                new Vector2(680f, 44f), new Vector2(0f, 170f), new Color(0.72f, 0.92f, 1f));

            scratchArea = CreateImage("스크래치 영역", panel, new Vector2(600f, 280f),
                new Color(0.12f, 0.15f, 0.23f, 1f)).rectTransform;
            scratchArea.anchoredPosition = new Vector2(0f, 5f);
            rankGlow = CreateImage("등급 광채", scratchArea, new Vector2(570f, 250f),
                new Color(0.25f, 0.45f, 1f, 0.3f));
            resultText = CreateText("결과", scratchArea, "등급 공개 대기", 38, new Vector2(540f, 210f),
                Vector2.zero, Color.white);

            scratched = new bool[Columns * Rows];
            Vector2 tileSize = new(48f, 38f);
            Vector2 start = new(-264f, 99f);
            for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                Image tile = CreateImage("은박_" + row + "_" + column, scratchArea, tileSize,
                    ((row + column) & 1) == 0
                        ? new Color(0.72f, 0.77f, 0.83f, 1f)
                        : new Color(0.54f, 0.61f, 0.7f, 1f));
                tile.rectTransform.anchoredPosition = start + new Vector2(column * 48f, -row * 40f);
                coverTiles.Add(tile);
            }

            for (int index = 0; index < 14; index++)
            {
                Image trail = CreateImage("긁기 잔상_" + index, scratchArea, new Vector2(26f, 26f),
                    new Color(0.7f, 0.9f, 1f, 0f));
                trail.gameObject.SetActive(false);
                trails.Add(trail);
            }
            scratchCursor = CreateImage("긁기 커서", scratchArea, new Vector2(54f, 54f),
                new Color(0.25f, 0.9f, 1f, 0.72f)).rectTransform;
            scratchCursor.gameObject.SetActive(false);

            progressText = CreateText("진행도", panel, "긁기 진행도 0%", 24, new Vector2(680f, 42f),
                new Vector2(0f, -170f), new Color(1f, 0.85f, 0.35f));
            CreateText("보상 안내", panel, "등급 보상 + 천장 보너스 + 마지막상", 21,
                new Vector2(680f, 38f), new Vector2(0f, -218f), new Color(0.8f, 0.78f, 0.96f));
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        private void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            if (value)
            {
                EnsureBuilt();
                ShopInputModeManager.Push(this, ShopInputMode.UI);
            }
            else
            {
                ShopInputModeManager.Pop(this);
            }
            if (canvasGroup == null) return;
            canvasGroup.alpha = value ? 1f : 0f;
            canvasGroup.interactable = value;
            canvasGroup.blocksRaycasts = value;
        }

        private void ResetTicket(int attempt)
        {
            observedAttempt = attempt;
            scratchedCount = 0;
            for (int index = 0; index < scratched.Length; index++)
            {
                scratched[index] = false;
                coverTiles[index].gameObject.SetActive(true);
            }
            resultText.text = "등급 공개 대기";
            progressText.text = "긁기 진행도 0%";
            nextProgressSendTime = 0f;
        }

        private void ProcessScratchInput()
        {
            if (Mouse.current == null || scratchArea == null) return;
            Vector2 screenPosition = Mouse.current.position.ReadValue();
            bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(scratchArea, screenPosition,
                null, out Vector2 localPosition) && scratchArea.rect.Contains(localPosition);
            bool pressed = Mouse.current.leftButton.isPressed;
            scratchCursor.gameObject.SetActive(inside);
            if (inside)
            {
                scratchCursor.anchoredPosition = localPosition;
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 18f) * 0.12f;
                scratchCursor.localScale = Vector3.one * pulse;
                scratchCursor.localRotation = Quaternion.Euler(0f, 0f, Time.unscaledTime * -220f);
            }
            FadeTrails();
            if (!inside || !pressed) return;

            for (int index = 0; index < coverTiles.Count; index++)
            {
                if (scratched[index]) continue;
                if (Vector2.Distance(coverTiles[index].rectTransform.anchoredPosition, localPosition) > ScratchRadius)
                    continue;
                scratched[index] = true;
                scratchedCount++;
                coverTiles[index].gameObject.SetActive(false);
            }
            SpawnTrail(localPosition);
            float progress = scratchedCount / (float)scratched.Length;
            progressText.text = "긁기 진행도 " + Mathf.RoundToInt(progress * 100f) + "%";
            if (Time.unscaledTime >= nextProgressSendTime)
            {
                station.RequestScratchProgress(progress);
                nextProgressSendTime = Time.unscaledTime + 0.1f;
            }
        }

        private void UpdatePresentation(ShopKujiState state)
        {
            float entrance = Mathf.Clamp01(station.StateProgress.Value * 2f);
            panel.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, entrance);
            if (state == ShopKujiState.AwaitingScratch || state == ShopKujiState.Scratching)
            {
                instructionText.text = "왼쪽 마우스를 누른 채 65% 이상 긁으세요";
                resultText.text = "은박 아래에 등급이 숨겨져 있습니다";
                return;
            }

            scratchCursor.gameObject.SetActive(false);
            foreach (Image tile in coverTiles) tile.gameObject.SetActive(false);
            string bonus = station.CurrentDrawHasCeiling.Value ? "\n천장 보너스: " + station.CeilingPrize : string.Empty;
            if (station.CurrentDrawHasLastPrize.Value) bonus += "\n마지막상: " + station.LastPrize;
            resultText.text = station.ResultRank.Value + "상\n" + station.ResultProduct.Value + bonus;
            Color rankColor = RankColor(station.ResultRank.Value);
            rankGlow.color = new Color(rankColor.r, rankColor.g, rankColor.b,
                0.35f + Mathf.Sin(Time.unscaledTime * 7f) * 0.12f);
            resultText.color = rankColor;
            instructionText.text = state == ShopKujiState.Result ? "가게 창고에 보상이 지급되었습니다" : "등급 공개 중...";
            float revealPulse = 1f + Mathf.Sin(Time.unscaledTime * 10f) * 0.035f;
            rankGlow.rectTransform.localScale = Vector3.one * revealPulse;
        }

        private void SpawnTrail(Vector2 position)
        {
            Image trail = trails[trailIndex++ % trails.Count];
            trail.gameObject.SetActive(true);
            trail.rectTransform.anchoredPosition = position;
            trail.rectTransform.localScale = Vector3.one;
            trail.color = new Color(0.55f, 0.92f, 1f, 0.7f);
        }

        private void FadeTrails()
        {
            foreach (Image trail in trails)
            {
                if (!trail.gameObject.activeSelf) continue;
                Color color = trail.color;
                color.a = Mathf.MoveTowards(color.a, 0f, Time.unscaledDeltaTime * 2.8f);
                trail.color = color;
                trail.rectTransform.localScale += Vector3.one * (Time.unscaledDeltaTime * 0.8f);
                if (color.a <= 0.01f) trail.gameObject.SetActive(false);
            }
        }

        private Image CreateImage(string objectName, Transform parent, Vector2 size, Color color)
        {
            GameObject item = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            Image image = item.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(string objectName, Transform parent, string value, int size, Vector2 dimensions,
            Vector2 position, Color color)
        {
            GameObject item = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            Text text = item.GetComponent<Text>();
            text.font = uiFont;
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static Color RankColor(ShopKujiRank rank) => rank switch
        {
            ShopKujiRank.S => new Color(1f, 0.76f, 0.18f),
            ShopKujiRank.A => new Color(1f, 0.38f, 0.72f),
            ShopKujiRank.B => new Color(0.38f, 0.7f, 1f),
            ShopKujiRank.C => new Color(0.35f, 1f, 0.62f),
            _ => new Color(0.9f, 0.92f, 0.96f)
        };

        private void OnDisable()
        {
            ShopInputModeManager.Pop(this);
        }
    }
}
