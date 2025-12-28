using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Bloomie.Models.Entities;

namespace Bloomie.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Bảng sản phẩm
        public DbSet<Product> Products { get; set; }

        // Bảng nhật ký truy cập người dùng
        public DbSet<UserAccessLog> UserAccessLogs { get; set; }

        // Bảng lịch sử đăng nhập
        public DbSet<LoginHistory> LoginHistories { get; set; }

        public DbSet<UnlockRequest> UnlockRequests { get; set; }

        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<FlowerType> FlowerTypes { get; set; }
        public DbSet<FlowerVariant> FlowerVariants { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<PurchaseOrderDetail> PurchaseOrderDetails { get; set; }
        public DbSet<ProductDetail> ProductDetails { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<ProductCategory> ProductCategories { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<ShoppingCart> ShoppingCarts { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<ShipperProfile> ShipperProfiles { get; set; }
        public DbSet<PromotionProduct> PromotionProducts { get; set; }
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<PromotionCode> PromotionCodes { get; set; }
        public DbSet<PromotionCategory> PromotionCategories { get; set; }
        public DbSet<PromotionOrder> PromotionOrders { get; set; }
        public DbSet<PromotionShipping> PromotionShippings { get; set; }
        public DbSet<PromotionGift> PromotionGifts { get; set; }
        public DbSet<PromotionGiftBuyProduct> PromotionGiftBuyProducts { get; set; }
        public DbSet<PromotionGiftBuyCategory> PromotionGiftBuyCategories { get; set; }
        public DbSet<PromotionGiftGiftProduct> PromotionGiftGiftProducts { get; set; }
        public DbSet<PromotionGiftGiftCategory> PromotionGiftGiftCategories { get; set; }
        public DbSet<Rating> Ratings { get; set; }
        public DbSet<Reply> Replies { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<UserLike> UserLikes { get; set; }
        public DbSet<ReplyImage> ReplyImages { get; set; }
        public DbSet<RatingImage> RatingImages { get; set; }
        public DbSet<ServiceReview> ServiceReviews { get; set; }
        public DbSet<ShippingFee> ShippingFees { get; set; }
        public DbSet<OrderReturn> OrderReturns { get; set; }
        public DbSet<UserVoucher> UserVouchers { get; set; }
        public DbSet<VoucherCampaign> VoucherCampaigns { get; set; }
        public DbSet<WishList> WishLists { get; set; }
        
        // Bảng giảm giá sản phẩm trực tiếp
        public DbSet<ProductDiscount> ProductDiscounts { get; set; }
        
        // Bảng Blog
        public DbSet<Blog> Blogs { get; set; }
        
        // Bảng điểm danh và đổi thưởng
        public DbSet<UserCheckIn> UserCheckIns { get; set; }
        public DbSet<UserPoints> UserPoints { get; set; }
        
        // Bảng thông báo
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<PointReward> PointRewards { get; set; }
        public DbSet<PointRedemption> PointRedemptions { get; set; }
        public DbSet<PointHistory> PointHistories { get; set; }
        
        // Bảng ChatBot
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<ChatConversation> ChatConversations { get; set; }
        
        // Bảng Support Chat (Customer ↔ Staff)
        public DbSet<SupportConversation> SupportConversations { get; set; }
        public DbSet<SupportMessage> SupportMessages { get; set; }
        
        // Bảng Recently Viewed Products
        public DbSet<RecentlyViewed> RecentlyViewedProducts { get; set; }
        
        // Bảng lịch sử phân công shipper
        public DbSet<OrderAssignmentHistory> OrderAssignmentHistories { get; set; }
        
        // ⭐ Bảng lưu trạng thái voucher của user
        public DbSet<UserCartState> UserCartStates { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Composite key cho ProductCategory
            modelBuilder.Entity<ProductCategory>()
                .HasKey(pc => new { pc.ProductId, pc.CategoryId });

            // Composite key cho PromotionProduct
            modelBuilder.Entity<PromotionProduct>()
                .HasKey(pp => new { pp.PromotionId, pp.ProductId });

            // Composite key cho PromotionOrder
            modelBuilder.Entity<PromotionOrder>()
                .HasKey(po => new { po.PromotionId, po.OrderId });

            // Sửa quan hệ Reply với Rating để tránh multiple cascade paths
            modelBuilder.Entity<Reply>()
                .HasOne(r => r.Rating)
                .WithMany(rat => rat.Replies)
                .HasForeignKey(r => r.RatingId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Report>()
                .HasOne(r => r.Rating)
                .WithMany()
                .HasForeignKey(r => r.RatingId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cấu hình SupportMessage để tránh multiple cascade paths
            modelBuilder.Entity<SupportMessage>()
                .HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

