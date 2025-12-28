using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.temp_check;

public partial class BloomieContext : DbContext
{
    public BloomieContext()
    {
    }

    public BloomieContext(DbContextOptions<BloomieContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AggregatedCounter> AggregatedCounters { get; set; }

    public virtual DbSet<AspNetRole> AspNetRoles { get; set; }

    public virtual DbSet<AspNetRoleClaim> AspNetRoleClaims { get; set; }

    public virtual DbSet<AspNetUser> AspNetUsers { get; set; }

    public virtual DbSet<AspNetUserClaim> AspNetUserClaims { get; set; }

    public virtual DbSet<AspNetUserLogin> AspNetUserLogins { get; set; }

    public virtual DbSet<AspNetUserToken> AspNetUserTokens { get; set; }

    public virtual DbSet<CartItem> CartItems { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Counter> Counters { get; set; }

    public virtual DbSet<FlowerType> FlowerTypes { get; set; }

    public virtual DbSet<FlowerVariant> FlowerVariants { get; set; }

    public virtual DbSet<Hash> Hashes { get; set; }

    public virtual DbSet<Job> Jobs { get; set; }

    public virtual DbSet<JobParameter> JobParameters { get; set; }

    public virtual DbSet<JobQueue> JobQueues { get; set; }

    public virtual DbSet<List> Lists { get; set; }

    public virtual DbSet<LoginHistory> LoginHistories { get; set; }

    public virtual DbSet<Order> Orders { get; set; }

    public virtual DbSet<OrderDetail> OrderDetails { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<ProductCategory> ProductCategories { get; set; }

    public virtual DbSet<ProductDetail> ProductDetails { get; set; }

    public virtual DbSet<ProductImage> ProductImages { get; set; }

    public virtual DbSet<Promotion> Promotions { get; set; }

    public virtual DbSet<PromotionCategory> PromotionCategories { get; set; }

    public virtual DbSet<PromotionCode> PromotionCodes { get; set; }

    public virtual DbSet<PromotionGift> PromotionGifts { get; set; }

    public virtual DbSet<PromotionGiftBuyCategory> PromotionGiftBuyCategories { get; set; }

    public virtual DbSet<PromotionGiftBuyProduct> PromotionGiftBuyProducts { get; set; }

    public virtual DbSet<PromotionGiftGiftCategory> PromotionGiftGiftCategories { get; set; }

    public virtual DbSet<PromotionGiftGiftProduct> PromotionGiftGiftProducts { get; set; }

    public virtual DbSet<PromotionOrder> PromotionOrders { get; set; }

    public virtual DbSet<PromotionProduct> PromotionProducts { get; set; }

    public virtual DbSet<PromotionShipping> PromotionShippings { get; set; }

    public virtual DbSet<PurchaseOrder> PurchaseOrders { get; set; }

    public virtual DbSet<PurchaseOrderDetail> PurchaseOrderDetails { get; set; }

    public virtual DbSet<Rating> Ratings { get; set; }

    public virtual DbSet<RatingImage> RatingImages { get; set; }

    public virtual DbSet<Reply> Replies { get; set; }

    public virtual DbSet<ReplyImage> ReplyImages { get; set; }

    public virtual DbSet<Report> Reports { get; set; }

    public virtual DbSet<Schema> Schemas { get; set; }

    public virtual DbSet<Server> Servers { get; set; }

    public virtual DbSet<ServiceReview> ServiceReviews { get; set; }

    public virtual DbSet<Set> Sets { get; set; }

    public virtual DbSet<ShoppingCart> ShoppingCarts { get; set; }

    public virtual DbSet<State> States { get; set; }

    public virtual DbSet<Supplier> Suppliers { get; set; }

    public virtual DbSet<UnlockRequest> UnlockRequests { get; set; }

    public virtual DbSet<UserAccessLog> UserAccessLogs { get; set; }

    public virtual DbSet<UserLike> UserLikes { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Name=ConnectionStrings:DefaultConnection");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AggregatedCounter>(entity =>
        {
            entity.HasKey(e => e.Key).HasName("PK_HangFire_CounterAggregated");

            entity.ToTable("AggregatedCounter", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_AggregatedCounter_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<AspNetRole>(entity =>
        {
            entity.HasIndex(e => e.NormalizedName, "RoleNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedName] IS NOT NULL)");

            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.NormalizedName).HasMaxLength(256);
        });

        modelBuilder.Entity<AspNetRoleClaim>(entity =>
        {
            entity.HasIndex(e => e.RoleId, "IX_AspNetRoleClaims_RoleId");

            entity.HasOne(d => d.Role).WithMany(p => p.AspNetRoleClaims).HasForeignKey(d => d.RoleId);
        });

        modelBuilder.Entity<AspNetUser>(entity =>
        {
            entity.HasIndex(e => e.NormalizedEmail, "EmailIndex");

            entity.HasIndex(e => e.NormalizedUserName, "UserNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedUserName] IS NOT NULL)");

            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.NormalizedEmail).HasMaxLength(256);
            entity.Property(e => e.NormalizedUserName).HasMaxLength(256);
            entity.Property(e => e.UserName).HasMaxLength(256);

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "AspNetUserRole",
                    r => r.HasOne<AspNetRole>().WithMany().HasForeignKey("RoleId"),
                    l => l.HasOne<AspNetUser>().WithMany().HasForeignKey("UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.ToTable("AspNetUserRoles");
                        j.HasIndex(new[] { "RoleId" }, "IX_AspNetUserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<AspNetUserClaim>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_AspNetUserClaims_UserId");

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserClaims).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserLogin>(entity =>
        {
            entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });

            entity.HasIndex(e => e.UserId, "IX_AspNetUserLogins_UserId");

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserLogins).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserToken>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.LoginProvider, e.Name });

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserTokens).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.HasIndex(e => e.FlowerVariantId, "IX_CartItems_FlowerVariantId");

            entity.HasIndex(e => e.ProductId, "IX_CartItems_ProductId");

            entity.HasIndex(e => e.ShoppingCartId, "IX_CartItems_ShoppingCartId");

            entity.Property(e => e.Discount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.FlowerVariant).WithMany(p => p.CartItems).HasForeignKey(d => d.FlowerVariantId);

            entity.HasOne(d => d.Product).WithMany(p => p.CartItems).HasForeignKey(d => d.ProductId);

            entity.HasOne(d => d.ShoppingCart).WithMany(p => p.CartItems).HasForeignKey(d => d.ShoppingCartId);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(e => e.ParentId, "IX_Categories_ParentId");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent).HasForeignKey(d => d.ParentId);
        });

        modelBuilder.Entity<Counter>(entity =>
        {
            entity.HasKey(e => new { e.Key, e.Id }).HasName("PK_HangFire_Counter");

            entity.ToTable("Counter", "HangFire");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<FlowerVariant>(entity =>
        {
            entity.HasIndex(e => e.FlowerTypeId, "IX_FlowerVariants_FlowerTypeId");

            entity.HasOne(d => d.FlowerType).WithMany(p => p.FlowerVariants).HasForeignKey(d => d.FlowerTypeId);
        });

        modelBuilder.Entity<Hash>(entity =>
        {
            entity.HasKey(e => new { e.Key, e.Field }).HasName("PK_HangFire_Hash");

            entity.ToTable("Hash", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_Hash_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Field).HasMaxLength(100);
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_HangFire_Job");

            entity.ToTable("Job", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_Job_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.HasIndex(e => e.StateName, "IX_HangFire_Job_StateName").HasFilter("([StateName] IS NOT NULL)");

            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
            entity.Property(e => e.StateName).HasMaxLength(20);
        });

        modelBuilder.Entity<JobParameter>(entity =>
        {
            entity.HasKey(e => new { e.JobId, e.Name }).HasName("PK_HangFire_JobParameter");

            entity.ToTable("JobParameter", "HangFire");

            entity.Property(e => e.Name).HasMaxLength(40);

            entity.HasOne(d => d.Job).WithMany(p => p.JobParameters)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK_HangFire_JobParameter_Job");
        });

        modelBuilder.Entity<JobQueue>(entity =>
        {
            entity.HasKey(e => new { e.Queue, e.Id }).HasName("PK_HangFire_JobQueue");

            entity.ToTable("JobQueue", "HangFire");

            entity.Property(e => e.Queue).HasMaxLength(50);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.FetchedAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<List>(entity =>
        {
            entity.HasKey(e => new { e.Key, e.Id }).HasName("PK_HangFire_List");

            entity.ToTable("List", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_List_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<LoginHistory>(entity =>
        {
            entity.Property(e => e.Ipaddress).HasColumnName("IPAddress");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<OrderDetail>(entity =>
        {
            entity.HasIndex(e => e.OrderId, "IX_OrderDetails_OrderId");

            entity.HasIndex(e => e.ProductId, "IX_OrderDetails_ProductId");

            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Order).WithMany(p => p.OrderDetails).HasForeignKey(d => d.OrderId);

            entity.HasOne(d => d.Product).WithMany(p => p.OrderDetails).HasForeignKey(d => d.ProductId);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(e => e.Price).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<ProductCategory>(entity =>
        {
            entity.HasKey(e => new { e.ProductId, e.CategoryId });

            entity.HasIndex(e => e.CategoryId, "IX_ProductCategories_CategoryId");

            entity.HasOne(d => d.Category).WithMany(p => p.ProductCategories).HasForeignKey(d => d.CategoryId);

            entity.HasOne(d => d.Product).WithMany(p => p.ProductCategories).HasForeignKey(d => d.ProductId);
        });

        modelBuilder.Entity<ProductDetail>(entity =>
        {
            entity.HasIndex(e => e.FlowerTypeId, "IX_ProductDetails_FlowerTypeId");

            entity.HasIndex(e => e.FlowerVariantId, "IX_ProductDetails_FlowerVariantId");

            entity.HasIndex(e => e.ProductId, "IX_ProductDetails_ProductId");

            entity.HasOne(d => d.FlowerType).WithMany(p => p.ProductDetails).HasForeignKey(d => d.FlowerTypeId);

            entity.HasOne(d => d.FlowerVariant).WithMany(p => p.ProductDetails).HasForeignKey(d => d.FlowerVariantId);

            entity.HasOne(d => d.Product).WithMany(p => p.ProductDetails).HasForeignKey(d => d.ProductId);
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.HasIndex(e => e.ProductId, "IX_ProductImages_ProductId");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductImages).HasForeignKey(d => d.ProductId);
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.Property(e => e.ApplyRadiusKm).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ConditionValue).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.MinOrderValue).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.MinProductValue).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ShippingDiscountValue).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<PromotionCategory>(entity =>
        {
            entity.HasIndex(e => e.CategoryId, "IX_PromotionCategories_CategoryId");

            entity.HasIndex(e => e.PromotionId, "IX_PromotionCategories_PromotionId");

            entity.HasOne(d => d.Category).WithMany(p => p.PromotionCategories).HasForeignKey(d => d.CategoryId);

            entity.HasOne(d => d.Promotion).WithMany(p => p.PromotionCategories).HasForeignKey(d => d.PromotionId);
        });

        modelBuilder.Entity<PromotionCode>(entity =>
        {
            entity.HasIndex(e => e.PromotionId, "IX_PromotionCodes_PromotionId");

            entity.Property(e => e.MaxDiscount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Value).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Promotion).WithMany(p => p.PromotionCodes).HasForeignKey(d => d.PromotionId);
        });

        modelBuilder.Entity<PromotionGift>(entity =>
        {
            entity.HasIndex(e => e.PromotionId, "IX_PromotionGifts_PromotionId");

            entity.Property(e => e.BuyApplyType).HasDefaultValue("");
            entity.Property(e => e.BuyConditionType).HasDefaultValue("");
            entity.Property(e => e.BuyConditionValueMoney).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.GiftApplyType).HasDefaultValue("");
            entity.Property(e => e.GiftDiscountMoneyValue).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.GiftDiscountType).HasDefaultValue("");

            entity.HasOne(d => d.Promotion).WithMany(p => p.PromotionGifts).HasForeignKey(d => d.PromotionId);
        });

        modelBuilder.Entity<PromotionGiftBuyCategory>(entity =>
        {
            entity.HasIndex(e => e.CategoryId, "IX_PromotionGiftBuyCategories_CategoryId");

            entity.HasIndex(e => e.PromotionGiftId, "IX_PromotionGiftBuyCategories_PromotionGiftId");

            entity.HasOne(d => d.Category).WithMany(p => p.PromotionGiftBuyCategories).HasForeignKey(d => d.CategoryId);

            entity.HasOne(d => d.PromotionGift).WithMany(p => p.PromotionGiftBuyCategories).HasForeignKey(d => d.PromotionGiftId);
        });

        modelBuilder.Entity<PromotionGiftBuyProduct>(entity =>
        {
            entity.HasIndex(e => e.ProductId, "IX_PromotionGiftBuyProducts_ProductId");

            entity.HasIndex(e => e.PromotionGiftId, "IX_PromotionGiftBuyProducts_PromotionGiftId");

            entity.HasOne(d => d.Product).WithMany(p => p.PromotionGiftBuyProducts).HasForeignKey(d => d.ProductId);

            entity.HasOne(d => d.PromotionGift).WithMany(p => p.PromotionGiftBuyProducts).HasForeignKey(d => d.PromotionGiftId);
        });

        modelBuilder.Entity<PromotionGiftGiftCategory>(entity =>
        {
            entity.HasIndex(e => e.CategoryId, "IX_PromotionGiftGiftCategories_CategoryId");

            entity.HasIndex(e => e.PromotionGiftId, "IX_PromotionGiftGiftCategories_PromotionGiftId");

            entity.HasOne(d => d.Category).WithMany(p => p.PromotionGiftGiftCategories).HasForeignKey(d => d.CategoryId);

            entity.HasOne(d => d.PromotionGift).WithMany(p => p.PromotionGiftGiftCategories).HasForeignKey(d => d.PromotionGiftId);
        });

        modelBuilder.Entity<PromotionGiftGiftProduct>(entity =>
        {
            entity.HasIndex(e => e.ProductId, "IX_PromotionGiftGiftProducts_ProductId");

            entity.HasIndex(e => e.PromotionGiftId, "IX_PromotionGiftGiftProducts_PromotionGiftId");

            entity.HasOne(d => d.Product).WithMany(p => p.PromotionGiftGiftProducts).HasForeignKey(d => d.ProductId);

            entity.HasOne(d => d.PromotionGift).WithMany(p => p.PromotionGiftGiftProducts).HasForeignKey(d => d.PromotionGiftId);
        });

        modelBuilder.Entity<PromotionOrder>(entity =>
        {
            entity.HasKey(e => new { e.PromotionId, e.OrderId });

            entity.HasOne(d => d.Promotion).WithMany(p => p.PromotionOrders).HasForeignKey(d => d.PromotionId);
        });

        modelBuilder.Entity<PromotionProduct>(entity =>
        {
            entity.HasKey(e => new { e.PromotionId, e.ProductId });

            entity.HasIndex(e => e.ProductId, "IX_PromotionProducts_ProductId");

            entity.HasOne(d => d.Product).WithMany(p => p.PromotionProducts).HasForeignKey(d => d.ProductId);

            entity.HasOne(d => d.Promotion).WithMany(p => p.PromotionProducts).HasForeignKey(d => d.PromotionId);
        });

        modelBuilder.Entity<PromotionShipping>(entity =>
        {
            entity.HasIndex(e => e.PromotionId, "IX_PromotionShippings_PromotionId");

            entity.Property(e => e.ShippingDiscount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Promotion).WithMany(p => p.PromotionShippings).HasForeignKey(d => d.PromotionId);
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasIndex(e => e.SupplierId, "IX_PurchaseOrders_SupplierId");

            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Supplier).WithMany(p => p.PurchaseOrders).HasForeignKey(d => d.SupplierId);
        });

        modelBuilder.Entity<PurchaseOrderDetail>(entity =>
        {
            entity.HasIndex(e => e.FlowerVariantId, "IX_PurchaseOrderDetails_FlowerVariantId");

            entity.HasIndex(e => e.PurchaseOrderId, "IX_PurchaseOrderDetails_PurchaseOrderId");

            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.FlowerVariant).WithMany(p => p.PurchaseOrderDetails).HasForeignKey(d => d.FlowerVariantId);

            entity.HasOne(d => d.PurchaseOrder).WithMany(p => p.PurchaseOrderDetails).HasForeignKey(d => d.PurchaseOrderId);
        });

        modelBuilder.Entity<Rating>(entity =>
        {
            entity.HasIndex(e => e.ProductId, "IX_Ratings_ProductId");

            entity.HasIndex(e => e.UserId, "IX_Ratings_UserId");

            entity.HasOne(d => d.Product).WithMany(p => p.Ratings).HasForeignKey(d => d.ProductId);

            entity.HasOne(d => d.User).WithMany(p => p.Ratings).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<RatingImage>(entity =>
        {
            entity.ToTable("RatingImage");

            entity.HasIndex(e => e.RatingId, "IX_RatingImage_RatingId");

            entity.HasOne(d => d.Rating).WithMany(p => p.RatingImages).HasForeignKey(d => d.RatingId);
        });

        modelBuilder.Entity<Reply>(entity =>
        {
            entity.HasIndex(e => e.RatingId, "IX_Replies_RatingId");

            entity.HasIndex(e => e.UserId, "IX_Replies_UserId");

            entity.HasOne(d => d.Rating).WithMany(p => p.Replies)
                .HasForeignKey(d => d.RatingId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(d => d.User).WithMany(p => p.Replies).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<ReplyImage>(entity =>
        {
            entity.ToTable("ReplyImage");

            entity.HasIndex(e => e.ReplyId, "IX_ReplyImage_ReplyId");

            entity.HasOne(d => d.Reply).WithMany(p => p.ReplyImages).HasForeignKey(d => d.ReplyId);
        });

        modelBuilder.Entity<Report>(entity =>
        {
            entity.HasIndex(e => e.RatingId, "IX_Reports_RatingId");

            entity.HasIndex(e => e.RatingId1, "IX_Reports_RatingId1");

            entity.HasIndex(e => e.ReporterId, "IX_Reports_ReporterId");

            entity.HasOne(d => d.Rating).WithMany(p => p.ReportRatings)
                .HasForeignKey(d => d.RatingId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(d => d.RatingId1Navigation).WithMany(p => p.ReportRatingId1Navigations).HasForeignKey(d => d.RatingId1);

            entity.HasOne(d => d.Reporter).WithMany(p => p.Reports).HasForeignKey(d => d.ReporterId);
        });

        modelBuilder.Entity<Schema>(entity =>
        {
            entity.HasKey(e => e.Version).HasName("PK_HangFire_Schema");

            entity.ToTable("Schema", "HangFire");

            entity.Property(e => e.Version).ValueGeneratedNever();
        });

        modelBuilder.Entity<Server>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_HangFire_Server");

            entity.ToTable("Server", "HangFire");

            entity.HasIndex(e => e.LastHeartbeat, "IX_HangFire_Server_LastHeartbeat");

            entity.Property(e => e.Id).HasMaxLength(200);
            entity.Property(e => e.LastHeartbeat).HasColumnType("datetime");
        });

        modelBuilder.Entity<ServiceReview>(entity =>
        {
            entity.HasIndex(e => e.OrderId, "IX_ServiceReviews_OrderId");

            entity.HasIndex(e => e.UserId, "IX_ServiceReviews_UserId");

            entity.Property(e => e.Comment).HasMaxLength(1000);

            entity.HasOne(d => d.Order).WithMany(p => p.ServiceReviews).HasForeignKey(d => d.OrderId);

            entity.HasOne(d => d.User).WithMany(p => p.ServiceReviews).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<Set>(entity =>
        {
            entity.HasKey(e => new { e.Key, e.Value }).HasName("PK_HangFire_Set");

            entity.ToTable("Set", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_Set_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.HasIndex(e => new { e.Key, e.Score }, "IX_HangFire_Set_Score");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Value).HasMaxLength(256);
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<ShoppingCart>(entity =>
        {
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<State>(entity =>
        {
            entity.HasKey(e => new { e.JobId, e.Id }).HasName("PK_HangFire_State");

            entity.ToTable("State", "HangFire");

            entity.HasIndex(e => e.CreatedAt, "IX_HangFire_State_CreatedAt");

            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.Name).HasMaxLength(20);
            entity.Property(e => e.Reason).HasMaxLength(100);

            entity.HasOne(d => d.Job).WithMany(p => p.States)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK_HangFire_State_Job");
        });

        modelBuilder.Entity<UserAccessLog>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_UserAccessLogs_UserId");

            entity.HasOne(d => d.User).WithMany(p => p.UserAccessLogs).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<UserLike>(entity =>
        {
            entity.HasIndex(e => e.RatingId, "IX_UserLikes_RatingId");

            entity.HasIndex(e => e.ReplyId, "IX_UserLikes_ReplyId");

            entity.HasIndex(e => e.UserId, "IX_UserLikes_UserId");

            entity.HasOne(d => d.Rating).WithMany(p => p.UserLikes).HasForeignKey(d => d.RatingId);

            entity.HasOne(d => d.Reply).WithMany(p => p.UserLikes).HasForeignKey(d => d.ReplyId);

            entity.HasOne(d => d.User).WithMany(p => p.UserLikes).HasForeignKey(d => d.UserId);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
