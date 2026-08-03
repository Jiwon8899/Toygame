using UnityEngine;

namespace PickAndPlaceShop
{
    [DisallowMultipleComponent]
    public sealed class ShopSpawnPadMarker : MonoBehaviour
    {
        public Vector3 SafePosition => transform.position;

        private void Start()
        {
            HideRuntimeVisuals();
        }

        public void HideRuntimeVisuals()
        {
            if (!Application.isPlaying) return;
            foreach (Renderer item in GetComponentsInChildren<Renderer>(true)) item.enabled = false;
            foreach (Light item in GetComponentsInChildren<Light>(true)) item.enabled = false;
            foreach (ParticleSystem item in GetComponentsInChildren<ParticleSystem>(true))
                item.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.9f);
            Vector3 position = transform.position + Vector3.up * 0.08f;
            Gizmos.DrawWireSphere(position, 0.65f);
            Gizmos.DrawLine(position, position + Vector3.up * 1.6f);
            Gizmos.DrawWireCube(position + Vector3.up * 1.65f, new Vector3(0.32f, 0.12f, 0.32f));
        }
    }
}
