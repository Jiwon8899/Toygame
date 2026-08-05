using System.Collections.Generic;
using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopClawMachineState
    {
        Idle,
        Reserved,
        Aiming,
        Descend,
        Close,
        Ascend,
        Return,
        Release,
        Judge,
        Cooldown
    }

    public static class ShopClawRules
    {
        public const ulong NoOccupant = ulong.MaxValue;

        public static bool CanOperateDuring(ShopPhase phase)
        {
            return phase == ShopPhase.PrizeHunt || phase == ShopPhase.Setup || phase == ShopPhase.Open;
        }

        public static bool CanReserve(ShopPhase phase, ulong occupantClientId, ulong requesterClientId,
            bool isInRange, bool playerBusy)
        {
            return CanOperateDuring(phase) && occupantClientId == NoOccupant &&
                   requesterClientId != NoOccupant && isInRange && !playerBusy;
        }

        public static bool CanAcceptOperatorCommand(ShopClawMachineState state, ulong occupantClientId,
            ulong requesterClientId)
        {
            return state == ShopClawMachineState.Aiming &&
                   occupantClientId == requesterClientId;
        }

        public static bool TryChargeAttempt(ref int sharedCoins, int cost, int attemptId,
            HashSet<int> chargedAttempts)
        {
            if (cost < 0 || sharedCoins < cost || chargedAttempts == null || chargedAttempts.Contains(attemptId))
                return false;
            sharedCoins -= cost;
            chargedAttempts.Add(attemptId);
            return true;
        }

        public static Vector2 ClampRail(Vector2 position, Vector2 xBounds, Vector2 zBounds)
        {
            return new Vector2(
                Mathf.Clamp(position.x, Mathf.Min(xBounds.x, xBounds.y), Mathf.Max(xBounds.x, xBounds.y)),
                Mathf.Clamp(position.y, Mathf.Min(zBounds.x, zBounds.y), Mathf.Max(zBounds.x, zBounds.y)));
        }

        public static bool IsDescentTerminalContact(float actualHeight, float dropHeight,
            float scoopDiameter, Vector3 contactNormal, Vector3 machineUp)
        {
            float nearFloorHeight = dropHeight + Mathf.Max(0.18f, scoopDiameter * 0.3f);
            float upwardSurface = Vector3.Dot(contactNormal.normalized, machineUp.normalized);
            return actualHeight <= nearFloorHeight || upwardSurface >= 0.45f;
        }

        public static bool CanAwardChutePrize(ShopClawMachineState state)
        {
            return state == ShopClawMachineState.Release || state == ShopClawMachineState.Judge ||
                   state == ShopClawMachineState.Cooldown;
        }

        public static bool IsChuteSettled(Vector3 linearVelocity, Vector3 angularVelocity,
            float maximumLinearSpeed, float maximumAngularSpeed = 1.2f)
        {
            return linearVelocity.sqrMagnitude <= maximumLinearSpeed * maximumLinearSpeed &&
                   angularVelocity.sqrMagnitude <= maximumAngularSpeed * maximumAngularSpeed;
        }

        public static bool IsFullyInsideChute(Bounds prizeBounds, Bounds chuteBounds, float horizontalInset)
        {
            float insetX = Mathf.Min(Mathf.Max(0f, horizontalInset), chuteBounds.extents.x * 0.25f);
            float insetZ = Mathf.Min(Mathf.Max(0f, horizontalInset), chuteBounds.extents.z * 0.25f);
            return prizeBounds.min.x >= chuteBounds.min.x + insetX &&
                   prizeBounds.max.x <= chuteBounds.max.x - insetX &&
                   prizeBounds.min.z >= chuteBounds.min.z + insetZ &&
                   prizeBounds.max.z <= chuteBounds.max.z - insetZ &&
                   prizeBounds.center.y >= chuteBounds.min.y &&
                   prizeBounds.center.y <= chuteBounds.max.y;
        }

        public static bool IsPrizeOutsidePlayableArea(Vector3 localPosition, Vector2 xBounds, Vector2 zBounds,
            float horizontalMargin = 0.25f, float minimumHeight = 0.55f, float maximumHeight = 3f)
        {
            float minX = Mathf.Min(xBounds.x, xBounds.y) - horizontalMargin;
            float maxX = Mathf.Max(xBounds.x, xBounds.y) + horizontalMargin;
            float minZ = Mathf.Min(zBounds.x, zBounds.y) - horizontalMargin;
            float maxZ = Mathf.Max(zBounds.x, zBounds.y) + horizontalMargin;
            return localPosition.x < minX || localPosition.x > maxX || localPosition.z < minZ ||
                   localPosition.z > maxZ || localPosition.y < minimumHeight || localPosition.y > maximumHeight;
        }
    }

    public sealed class ShopClawAwardLedger
    {
        private readonly HashSet<ulong> awardedNetworkObjects = new();

        public bool TryAward(ulong networkObjectId, bool belongsToMachine, bool reachedChute, bool alreadyAwarded)
        {
            return belongsToMachine && reachedChute && !alreadyAwarded && awardedNetworkObjects.Add(networkObjectId);
        }

        public void Reset() => awardedNetworkObjects.Clear();
    }

    public sealed class ShopClawSessionModel
    {
        public ulong OccupantClientId { get; private set; } = ShopClawRules.NoOccupant;
        public ShopClawMachineState State { get; private set; } = ShopClawMachineState.Idle;
        public int AttemptId { get; private set; }

        public bool TryReserve(ShopPhase phase, ulong requester, bool inRange, bool busy)
        {
            if (!ShopClawRules.CanReserve(phase, OccupantClientId, requester, inRange, busy)) return false;
            OccupantClientId = requester;
            AttemptId++;
            State = ShopClawMachineState.Aiming;
            return true;
        }

        public void BeginDrop() => State = ShopClawMachineState.Descend;

        public void Release()
        {
            OccupantClientId = ShopClawRules.NoOccupant;
            State = ShopClawMachineState.Idle;
        }

        public void OnDisconnected(ulong clientId)
        {
            if (OccupantClientId == clientId) Release();
        }

        public void OnPhaseChanged(ShopPhase phase)
        {
            if (!ShopClawRules.CanOperateDuring(phase) &&
                (State == ShopClawMachineState.Aiming || State == ShopClawMachineState.Reserved)) Release();
        }

        public void OnNextDay() => Release();
    }
}
