using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace PickAndPlaceShop
{
    public enum ShopContainerKind : byte
    {
        PersonalInventory = 0,
        SharedStorage = 1,
        SharedDisplay = 2,
        AutomationBuffer = 3,
        CapsuleRecycler = 4,
        ConsignmentDisplay = 5
    }

    public enum ShopAppraisalGrade : byte
    {
        None = 0,
        C = 1,
        B = 2,
        A = 3,
        S = 4
    }

    [Serializable]
    public struct ShopContainerItem : INetworkSerializable, IEquatable<ShopContainerItem>
    {
        public ulong OwnerClientId;
        public ShopContainerKind Container;
        public int SlotIndex;
        public int ProductId;
        public int VisualPrefabIndex;
        public int Quantity;
        public int MaxStack;
        public int UnitPrice;
        public ShopProductRarity Rarity;
        public FixedString64Bytes DisplayName;
        public ulong InstanceId;
        public ShopAppraisalGrade AppraisalGrade;

        public bool IsAppraised => AppraisalGrade != ShopAppraisalGrade.None;

        public ShopContainerItem(ulong ownerClientId, ShopContainerKind container, int slotIndex,
            ShopProductDefinition product, int visualPrefabIndex)
        {
            OwnerClientId = ownerClientId;
            Container = container;
            SlotIndex = slotIndex;
            ProductId = product != null ? product.ProductId : visualPrefabIndex;
            VisualPrefabIndex = visualPrefabIndex;
            Quantity = 1;
            MaxStack = product != null ? Mathf.Max(1, product.MaxStack) : 1;
            UnitPrice = product != null ? product.SalePrice : 100;
            Rarity = product != null ? product.Rarity : ShopProductRarity.Common;
            DisplayName = new FixedString64Bytes(product != null ? product.DisplayName : "Prize");
            InstanceId = 0;
            AppraisalGrade = ShopAppraisalGrade.None;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref OwnerClientId);
            byte container = (byte)Container;
            serializer.SerializeValue(ref container);
            if (serializer.IsReader) Container = (ShopContainerKind)container;
            serializer.SerializeValue(ref SlotIndex);
            serializer.SerializeValue(ref ProductId);
            serializer.SerializeValue(ref VisualPrefabIndex);
            serializer.SerializeValue(ref Quantity);
            serializer.SerializeValue(ref MaxStack);
            serializer.SerializeValue(ref UnitPrice);
            byte rarity = (byte)Rarity;
            serializer.SerializeValue(ref rarity);
            if (serializer.IsReader) Rarity = (ShopProductRarity)rarity;
            serializer.SerializeValue(ref DisplayName);
            serializer.SerializeValue(ref InstanceId);
            byte appraisalGrade = (byte)AppraisalGrade;
            serializer.SerializeValue(ref appraisalGrade);
            if (serializer.IsReader) AppraisalGrade = (ShopAppraisalGrade)appraisalGrade;
        }

        public bool Equals(ShopContainerItem other)
        {
            return OwnerClientId == other.OwnerClientId && Container == other.Container &&
                   SlotIndex == other.SlotIndex && ProductId == other.ProductId &&
                   VisualPrefabIndex == other.VisualPrefabIndex && Quantity == other.Quantity &&
                   MaxStack == other.MaxStack && UnitPrice == other.UnitPrice &&
                   Rarity == other.Rarity && DisplayName.Equals(other.DisplayName) &&
                   InstanceId == other.InstanceId && AppraisalGrade == other.AppraisalGrade;
        }

        public override bool Equals(object obj) => obj is ShopContainerItem other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(OwnerClientId, (byte)Container, SlotIndex, ProductId);
    }

    public readonly struct ShopContainerSnapshot
    {
        public readonly int Used;
        public readonly int Capacity;

        public ShopContainerSnapshot(int used, int capacity)
        {
            Used = used;
            Capacity = capacity;
        }

        public bool IsFull => Used >= Capacity;
    }

    public static class ShopContainerRules
    {
        public const ulong SharedOwner = ulong.MaxValue;
        public const int PersonalCapacity = 10;

        public static bool CanAccept(int used, int capacity)
        {
            return capacity > 0 && Mathf.Max(0, used) < capacity;
        }

        public static bool CanMoveAtomic(int sourceQuantity, int destinationUsed,
            int destinationCapacity)
        {
            return sourceQuantity > 0 && CanAccept(destinationUsed, destinationCapacity);
        }

        public static bool BelongsTo(in ShopContainerItem item, ulong owner, ShopContainerKind container)
        {
            return item.Container == container &&
                   (container != ShopContainerKind.PersonalInventory || item.OwnerClientId == owner);
        }

        public static int UsedCount(NetworkList<ShopContainerItem> items, ulong owner,
            ShopContainerKind container)
        {
            int total = 0;
            if (items == null) return total;
            for (int i = 0; i < items.Count; i++)
                if (BelongsTo(items[i], owner, container) && items[i].Quantity > 0)
                    total++;
            return total;
        }

        public static int TotalQuantity(NetworkList<ShopContainerItem> items, ulong owner,
            ShopContainerKind container)
        {
            int total = 0;
            if (items == null) return total;
            for (int i = 0; i < items.Count; i++)
                if (BelongsTo(items[i], owner, container))
                    total += Mathf.Max(0, items[i].Quantity);
            return total;
        }

        public static bool CanAcceptProduct(NetworkList<ShopContainerItem> items, ulong owner,
            ShopContainerKind container, int productId, int capacity)
        {
            if (items == null || capacity <= 0) return false;
            for (int i = 0; i < items.Count; i++)
            {
                ShopContainerItem item = items[i];
                if (BelongsTo(item, owner, container) && item.ProductId == productId &&
                    !item.IsAppraised && item.Quantity > 0 &&
                    item.Quantity < Mathf.Max(1, item.MaxStack))
                    return true;
            }
            return UsedCount(items, owner, container) < capacity;
        }

        public static bool CanStack(in ShopContainerItem first, in ShopContainerItem second) =>
            !first.IsAppraised && !second.IsAppraised && first.InstanceId == 0 &&
            second.InstanceId == 0 && first.ProductId == second.ProductId;

        public static int FindFirst(NetworkList<ShopContainerItem> items, ulong owner,
            ShopContainerKind container, int productId = int.MinValue)
        {
            if (items == null) return -1;
            for (int i = 0; i < items.Count; i++)
            {
                ShopContainerItem item = items[i];
                if (!BelongsTo(item, owner, container) || item.Quantity <= 0) continue;
                if (productId != int.MinValue && item.ProductId != productId) continue;
                return i;
            }
            return -1;
        }

        public static int FindFreeSlot(NetworkList<ShopContainerItem> items, ulong owner,
            ShopContainerKind container, int capacity)
        {
            for (int slot = 0; slot < capacity; slot++)
            {
                bool occupied = false;
                for (int i = 0; i < items.Count; i++)
                {
                    ShopContainerItem item = items[i];
                    if (BelongsTo(item, owner, container) && item.SlotIndex == slot)
                    {
                        occupied = true;
                        break;
                    }
                }
                if (!occupied) return slot;
            }
            return -1;
        }
    }
}
