using System.Collections.Generic;

namespace PickAndPlaceShop
{
    public sealed class ShopStockLedger
    {
        private readonly Dictionary<int, int> stock = new();
        private readonly Dictionary<int, int> reserved = new();
        private readonly Dictionary<ulong, int> customerReservations = new();
        private readonly HashSet<ulong> pickedUpCustomers = new();
        private readonly HashSet<ulong> completedCustomers = new();

        public int GetStock(int productId) => stock.TryGetValue(productId, out int value) ? value : 0;
        public int GetReserved(int productId) => reserved.TryGetValue(productId, out int value) ? value : 0;
        public int GetAvailable(int productId) => System.Math.Max(0, GetStock(productId) - GetReserved(productId));

        public void SetStock(int productId, int amount)
        {
            stock[productId] = System.Math.Max(0, amount);
            reserved[productId] = System.Math.Min(GetReserved(productId), stock[productId]);
        }

        public void AddStock(int productId, int amount)
        {
            SetStock(productId, GetStock(productId) + amount);
        }

        public bool TryReserve(ulong customerId, int productId)
        {
            if (completedCustomers.Contains(customerId) || customerReservations.ContainsKey(customerId) || GetAvailable(productId) <= 0)
            {
                return false;
            }

            customerReservations.Add(customerId, productId);
            reserved[productId] = GetReserved(productId) + 1;
            return true;
        }

        public bool CancelReservation(ulong customerId)
        {
            return CancelReservation(customerId, true);
        }

        public bool CancelReservation(ulong customerId, bool restorePickedUpStock)
        {
            if (!customerReservations.Remove(customerId, out int productId))
            {
                return false;
            }

            if (pickedUpCustomers.Remove(customerId))
            {
                if (restorePickedUpStock) stock[productId] = GetStock(productId) + 1;
            }
            else
            {
                reserved[productId] = System.Math.Max(0, GetReserved(productId) - 1);
            }
            return true;
        }

        public bool TryPickupReservation(ulong customerId, out int productId)
        {
            productId = -1;
            if (!customerReservations.TryGetValue(customerId, out int reservedProduct) ||
                pickedUpCustomers.Contains(customerId) || GetStock(reservedProduct) <= 0)
            {
                return false;
            }

            reserved[reservedProduct] = System.Math.Max(0, GetReserved(reservedProduct) - 1);
            stock[reservedProduct] = System.Math.Max(0, GetStock(reservedProduct) - 1);
            pickedUpCustomers.Add(customerId);
            productId = reservedProduct;
            return true;
        }

        public bool HasPickedUp(ulong customerId) => pickedUpCustomers.Contains(customerId);

        public bool TryCheckout(ulong customerId, out int productId)
        {
            productId = -1;
            if (completedCustomers.Contains(customerId) || !customerReservations.TryGetValue(customerId, out int reservedProduct))
            {
                return false;
            }

            bool pickedUp = pickedUpCustomers.Remove(customerId);
            if (!pickedUp && GetStock(reservedProduct) <= 0)
            {
                CancelReservation(customerId);
                return false;
            }

            customerReservations.Remove(customerId);
            if (!pickedUp)
            {
                reserved[reservedProduct] = System.Math.Max(0, GetReserved(reservedProduct) - 1);
                stock[reservedProduct] = System.Math.Max(0, GetStock(reservedProduct) - 1);
            }
            completedCustomers.Add(customerId);
            productId = reservedProduct;
            return true;
        }

        public int TotalStock()
        {
            int total = 0;
            foreach (int value in stock.Values) total += value;
            return total;
        }

        public void ResetTransactions()
        {
            customerReservations.Clear();
            reserved.Clear();
            pickedUpCustomers.Clear();
            completedCustomers.Clear();
        }
    }

    public static class ShopSaleProcessor
    {
        public static bool TryComplete(ShopStockLedger ledger, ulong customerId, int price,
            ref int sharedCoins, ref int soldCount, out int productId)
        {
            if (!ledger.TryCheckout(customerId, out productId))
            {
                return false;
            }

            sharedCoins += System.Math.Max(0, price);
            soldCount++;
            return true;
        }
    }
}
