using UnityEngine;

namespace PickAndPlaceShop
{
    public sealed class ShopPlayerAppearance : MonoBehaviour
    {
        private static readonly int MovingParameter = Animator.StringToHash("Moving");
        private static readonly int RunningParameter = Animator.StringToHash("Running");
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int Attack1Parameter = Animator.StringToHash("Attack1");
        private static readonly int Attack2Parameter = Animator.StringToHash("Attack2");
        private static readonly int AttackSpeedParameter = Animator.StringToHash("AttackSpeed");
        private static readonly int Attack1State = Animator.StringToHash("Attack1");
        private static readonly int Attack2State = Animator.StringToHash("Attack2");
        private static readonly int IdleState = Animator.StringToHash("Idle");

        [SerializeField] private Animator appearanceAnimator;
        [Min(0.1f)] [SerializeField] private float runThreshold = 3.8f;

        private Vector3 lastPosition;

        public Animator AppearanceAnimator => appearanceAnimator;
        public float CurrentSpeed { get; private set; }
        public bool IsRunning { get; private set; }
        public bool AttackAnimationActive
        {
            get
            {
                if (appearanceAnimator == null || !appearanceAnimator.isActiveAndEnabled) return false;

                AnimatorStateInfo current = appearanceAnimator.GetCurrentAnimatorStateInfo(0);
                if (IsAttackState(current.shortNameHash) && current.normalizedTime < 1f) return true;

                if (!appearanceAnimator.IsInTransition(0)) return false;
                AnimatorStateInfo next = appearanceAnimator.GetNextAnimatorStateInfo(0);
                return IsAttackState(next.shortNameHash);
            }
        }

        public void PlayAttack(int comboIndex, float transitionSeconds, float playbackSpeed)
        {
            if (appearanceAnimator == null) return;
            appearanceAnimator.ResetTrigger(comboIndex == 0 ? Attack2Parameter : Attack1Parameter);
            appearanceAnimator.ResetTrigger(comboIndex == 0 ? Attack1Parameter : Attack2Parameter);
            int state = comboIndex == 0 ? Attack1State : Attack2State;
            if (!appearanceAnimator.HasState(0, state))
            {
                Debug.LogError("[ShopPlayerAppearance] 공격 Animator 상태가 없습니다: " +
                               (comboIndex == 0 ? "Attack1" : "Attack2"), this);
                return;
            }
            appearanceAnimator.SetFloat(AttackSpeedParameter, Mathf.Max(0.01f, playbackSpeed));
            appearanceAnimator.CrossFadeInFixedTime(state, Mathf.Max(0f, transitionSeconds), 0, 0f);
        }

        private static bool IsAttackState(int shortNameHash)
        {
            return shortNameHash == Attack1State || shortNameHash == Attack2State;
        }

#if UNITY_EDITOR
        public void EditorConfigure(Animator animator, float runningSpeedThreshold)
        {
            appearanceAnimator = animator;
            runThreshold = runningSpeedThreshold;
        }
#endif

        private void Awake()
        {
            if (appearanceAnimator == null) appearanceAnimator = GetComponentInChildren<Animator>(true);
            lastPosition = transform.position;

            if (appearanceAnimator == null)
            {
                Debug.LogError("[ShopPlayerAppearance] 플레이어 외형 Animator가 연결되지 않았습니다.", this);
                enabled = false;
                return;
            }

            appearanceAnimator.applyRootMotion = false;
            appearanceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Debug.Log(
                $"[ShopPlayerAppearance] READY model={appearanceAnimator.gameObject.name} " +
                $"controller={appearanceAnimator.runtimeAnimatorController?.name ?? "none"} " +
                $"renderers={appearanceAnimator.GetComponentsInChildren<Renderer>(true).Length}",
                this);
        }

        private void Update()
        {
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 displacement = transform.position - lastPosition;
            displacement.y = 0f;
            CurrentSpeed = displacement.magnitude / deltaTime;
            lastPosition = transform.position;

            bool moving = CurrentSpeed > 0.08f;
            IsRunning = moving && CurrentSpeed >= runThreshold;
            appearanceAnimator.SetFloat(SpeedParameter, CurrentSpeed);
            appearanceAnimator.SetBool(MovingParameter, moving);
            appearanceAnimator.SetBool(RunningParameter, IsRunning);
            FinishAttackStateIfNeeded();
        }

        private void FinishAttackStateIfNeeded()
        {
            AnimatorStateInfo current = appearanceAnimator.GetCurrentAnimatorStateInfo(0);
            if (!IsAttackState(current.shortNameHash) || current.normalizedTime < 1f) return;

            appearanceAnimator.SetFloat(AttackSpeedParameter, 1f);
            if (!appearanceAnimator.IsInTransition(0) && appearanceAnimator.HasState(0, IdleState))
                appearanceAnimator.CrossFadeInFixedTime(IdleState, 0.06f, 0, 0f);
        }
    }
}
