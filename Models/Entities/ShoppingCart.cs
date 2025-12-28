using System;
using System.Collections.Generic;

namespace Bloomie.Models.Entities
{
    public class ShoppingCart
    {
        public int Id { get; set; }
        public string? UserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public ICollection<CartItem>? CartItems { get; set; }

        // Thông tin mã giảm giá đã áp dụng
        public string? PromotionCode { get; set; }
        public int? SelectedVoucherId { get; set; } // ID của UserVoucher đã chọn
        public decimal? DiscountAmount { get; set; } // Tổng số tiền giảm
        public bool FreeShipping { get; set; } // Đã áp dụng mã miễn phí ship chưa
        public int? GiftProductId { get; set; } // Sản phẩm tặng kèm (nếu có)
        public int? GiftQuantity { get; set; }

        public void AddItem(CartItem item)
        {
            if (CartItems == null)
                CartItems = new List<CartItem>();
            // Kiểm tra xem sản phẩm đã tồn tại
            var existingItem = CartItems.FirstOrDefault(i => i.ProductId == item.ProductId);
            if (existingItem != null)
            {
                existingItem.Quantity += item.Quantity;
                // Cập nhật discount (có thể thay đổi theo thời gian)
                existingItem.Discount = item.Discount;
                // Cập nhật delivery info nếu có
                if (item.DeliveryDate.HasValue)
                    existingItem.DeliveryDate = item.DeliveryDate;
                if (!string.IsNullOrEmpty(item.DeliveryTime))
                    existingItem.DeliveryTime = item.DeliveryTime;
            }
            else
            {
                CartItems.Add(item);
            }
        }

        // Xóa sản phẩm
        public void RemoveItem(int productId)
        {
            if (CartItems == null) return;
            var toRemove = new List<CartItem>();
            foreach (var i in CartItems)
            {
                if (i.ProductId == productId)
                    toRemove.Add(i);
            }
            foreach (var i in toRemove)
                CartItems.Remove(i);
        }
    }
}
