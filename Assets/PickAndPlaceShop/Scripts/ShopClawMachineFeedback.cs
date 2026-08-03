using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PickAndPlaceShop
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class ShopClawMachineFeedback : MonoBehaviour
    {
        [SerializeField] private ShopClawMachineNetwork machine;
        private AudioSource oneShot;
        private AudioSource motor;
        private ShopClawMachineState previousState;
        private bool initialized;
        private float rumbleUntil;

        public void Configure(ShopClawMachineNetwork target) => machine = target;

        private void Awake()
        {
            if (machine == null) machine = GetComponent<ShopClawMachineNetwork>();
            oneShot = GetComponent<AudioSource>();
            oneShot.spatialBlend = 0.65f;
            oneShot.playOnAwake = false;
            motor = gameObject.AddComponent<AudioSource>();
            motor.spatialBlend = 0.7f;
            motor.loop = true;
            motor.volume = 0.12f;
            motor.clip = Tone("레일 모터", 118f, 0.35f, 0.24f);
        }

        private void Update()
        {
            if (machine == null || !machine.IsSpawned) return;
            bool moving = machine.State.Value == ShopClawMachineState.Aiming &&
                          machine.OperatorInput.Value.sqrMagnitude > 0.02f;
            if (moving && !motor.isPlaying) motor.Play();
            if (!moving && motor.isPlaying) motor.Stop();

            if (!initialized || previousState != machine.State.Value)
            {
                initialized = true;
                previousState = machine.State.Value;
                PlayStateFeedback(previousState);
            }

            if (Gamepad.current != null && Time.unscaledTime >= rumbleUntil)
                Gamepad.current.SetMotorSpeeds(0f, 0f);
        }

        private void OnDisable()
        {
            if (Gamepad.current != null) Gamepad.current.SetMotorSpeeds(0f, 0f);
        }

        private void PlayStateFeedback(ShopClawMachineState state)
        {
            float frequency = state switch
            {
                ShopClawMachineState.Reserved => 520f,
                ShopClawMachineState.Descend => 150f,
                ShopClawMachineState.Close => 310f,
                ShopClawMachineState.Ascend => 190f,
                ShopClawMachineState.Release => 240f,
                ShopClawMachineState.Cooldown => machine.LastResultSuccess.Value ? 780f : 105f,
                _ => 0f
            };
            if (frequency > 0f) oneShot.PlayOneShot(Tone(state.ToString(), frequency,
                state == ShopClawMachineState.Cooldown ? 0.38f : 0.16f, 0.35f));

            if (NetworkManager.Singleton == null || machine.OccupantClientId.Value != NetworkManager.Singleton.LocalClientId ||
                Gamepad.current == null) return;
            if (state == ShopClawMachineState.Descend || state == ShopClawMachineState.Close ||
                state == ShopClawMachineState.Cooldown)
            {
                Gamepad.current.SetMotorSpeeds(state == ShopClawMachineState.Cooldown ? 0.35f : 0.18f,
                    state == ShopClawMachineState.Cooldown ? 0.65f : 0.35f);
                rumbleUntil = Time.unscaledTime + (state == ShopClawMachineState.Cooldown ? 0.28f : 0.12f);
            }
        }

        private static AudioClip Tone(string label, float frequency, float duration, float volume)
        {
            const int sampleRate = 22050;
            int length = Mathf.Max(64, Mathf.CeilToInt(sampleRate * duration));
            float[] data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float time = i / (float)sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * i / Mathf.Max(1, length - 1));
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * time) * envelope * volume;
            }
            AudioClip clip = AudioClip.Create("Claw_" + label, length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
