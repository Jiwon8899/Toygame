using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    /// <summary>
    /// 계산 대기 중인 손님을 시각적으로 표시한다.
    /// - 대기 상태에서는 걷기 모션을 강제로 끄고 Idle 로 고정한다.
    /// - 머리 위에 "구매 대기" 표식과 경과 시간 스톱워치를 띄운다.
    /// 기존 손님 로직은 건드리지 않고 표시만 덧붙인다.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public sealed class ShopCustomerWaitIndicator : MonoBehaviour
    {
        private sealed class Entry
        {
            public ShopCustomerNetwork Customer;
            public Animator Animator;
            public GameObject Root;
            public TextMesh Label;
            public Transform Hand;
            public float Elapsed;
            public bool WasWaiting;
        }

        private static readonly int MovingParameter = Animator.StringToHash("Moving");
        private static ShopCustomerWaitIndicator instance;

        private readonly Dictionary<ShopCustomerNetwork, Entry> entries = new();
        private readonly List<ShopCustomerNetwork> stale = new();
        private float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("[World] Customer Wait Indicators");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopCustomerWaitIndicator>();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.5f;
                Scan();
            }

            // Animator evaluates after Update and before LateUpdate. Applying this only in
            // LateUpdate lets ShopCustomerNetwork set Moving=true again before every evaluation.
            foreach (KeyValuePair<ShopCustomerNetwork, Entry> pair in entries)
            {
                Entry entry = pair.Value;
                bool waiting = entry.Customer != null && IsWaiting(entry.Customer);
                if (waiting) ForceIdle(entry.Animator, !entry.WasWaiting);
                entry.WasWaiting = waiting;
            }
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            stale.Clear();

            foreach (KeyValuePair<ShopCustomerNetwork, Entry> pair in entries)
            {
                Entry entry = pair.Value;
                if (entry.Customer == null)
                {
                    if (entry.Root != null) Destroy(entry.Root);
                    stale.Add(pair.Key);
                    continue;
                }

                bool waiting = IsWaiting(entry.Customer);
                if (entry.Root != null && entry.Root.activeSelf != waiting) entry.Root.SetActive(waiting);

                if (!waiting)
                {
                    entry.Elapsed = 0f;
                    continue;
                }

                ForceIdle(entry.Animator, false);

                entry.Elapsed += Time.deltaTime;
                if (entry.Root == null) continue;

                entry.Root.transform.position = entry.Customer.transform.position + new Vector3(0f, 2.15f, 0f);
                if (cam != null)
                    entry.Root.transform.rotation = Quaternion.LookRotation(
                        entry.Root.transform.position - cam.transform.position, Vector3.up);

                if (entry.Label != null)
                {
                    entry.Label.text = "구매 대기\n" + entry.Elapsed.ToString("F1") + "초";
                    entry.Label.color = entry.Elapsed < 6f
                        ? new Color(0.35f, 1f, 0.55f)
                        : Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.35f, 0.3f),
                            Mathf.Clamp01((entry.Elapsed - 6f) / 6f));
                }

                if (entry.Hand != null)
                    entry.Hand.localRotation = Quaternion.Euler(0f, 0f, -entry.Elapsed * 60f);
            }

            for (int i = 0; i < stale.Count; i++) entries.Remove(stale[i]);
        }

        private static bool IsWaiting(ShopCustomerNetwork customer)
        {
            ShopCustomerState state = customer.State.Value;
            return state == ShopCustomerState.Queue || state == ShopCustomerState.Checkout;
        }

        private static void ForceIdle(Animator animator, bool enterIdleState)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            for (int index = 0; index < animator.parameterCount; index++)
            {
                AnimatorControllerParameter parameter = animator.parameters[index];
                if (parameter.nameHash != MovingParameter || parameter.type != AnimatorControllerParameterType.Bool)
                    continue;
                animator.SetBool(MovingParameter, false);
                break;
            }

            if (!enterIdleState || animator.layerCount <= 0) return;
            int idleState = Animator.StringToHash("Idle");
            if (animator.HasState(0, idleState)) animator.CrossFade(idleState, 0.05f, 0);
        }

        private void Scan()
        {
            ShopCustomerNetwork[] found = FindObjectsByType<ShopCustomerNetwork>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                ShopCustomerNetwork customer = found[i];
                if (customer == null || entries.ContainsKey(customer)) continue;
                entries[customer] = Build(customer);
            }
        }

        private Entry Build(ShopCustomerNetwork customer)
        {
            GameObject root = new("CustomerWaitIndicator");
            root.transform.SetParent(transform, false);
            root.SetActive(false);

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Plate";
            plate.transform.SetParent(root.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            plate.transform.localScale = new Vector3(1.25f, 0.58f, 1f);
            Collider plateCollider = plate.GetComponent<Collider>();
            if (plateCollider != null) Destroy(plateCollider);
            ShopBuildSafeMaterials.ApplyLitColor(plate.GetComponent<Renderer>(),
                new Color(0.09f, 0.11f, 0.16f), false);

            GameObject dial = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dial.name = "Dial";
            dial.transform.SetParent(root.transform, false);
            dial.transform.localPosition = new Vector3(-0.48f, 0f, -0.01f);
            dial.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            dial.transform.localScale = new Vector3(0.15f, 0.02f, 0.15f);
            Collider dialCollider = dial.GetComponent<Collider>();
            if (dialCollider != null) Destroy(dialCollider);
            ShopBuildSafeMaterials.ApplyLitColor(dial.GetComponent<Renderer>(),
                new Color(0.94f, 0.94f, 0.9f), false);

            GameObject hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hand.name = "Hand";
            hand.transform.SetParent(root.transform, false);
            hand.transform.localPosition = new Vector3(-0.48f, 0.035f, -0.03f);
            hand.transform.localScale = new Vector3(0.018f, 0.07f, 0.01f);
            Collider handCollider = hand.GetComponent<Collider>();
            if (handCollider != null) Destroy(handCollider);
            ShopBuildSafeMaterials.ApplyLitColor(hand.GetComponent<Renderer>(),
                new Color(0.85f, 0.2f, 0.2f), false);

            GameObject textObject = new("Label");
            textObject.transform.SetParent(root.transform, false);
            textObject.transform.localPosition = new Vector3(0.13f, 0f, -0.03f);
            TextMesh label = textObject.AddComponent<TextMesh>();
            label.text = "구매 대기";
            label.characterSize = 0.028f;
            label.fontSize = 48;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = new Color(0.35f, 1f, 0.55f);

            return new Entry
            {
                Customer = customer,
                Animator = customer.GetComponentInChildren<Animator>(true),
                Root = root,
                Label = label,
                Hand = hand.transform
            };
        }
    }
}
