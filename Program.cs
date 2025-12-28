using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Bloomie.Data;
using Bloomie.Services.Implementations;
using Bloomie.Services.Filter;
using Bloomie.Services.Interfaces;
using Bloomie.Services;
using Bloomie.Models.Entities;
// using Bloomie.Areas.Admin.Models;
using Bloomie.Middleware;
using Bloomie.Models.Momo;
using Hangfire;
using Hangfire.SqlServer;

// using Python.Runtime;
// using Bloomie.Hubs;
using QuestPDF;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// THÊM DÒNG NÀY ↓↓↓
builder.WebHost.UseUrls("http://0.0.0.0:5229", "https://0.0.0.0:7229");
// ↑↑↑ THÊM DÒNG TRÊN

builder.Services.AddSignalR(); // Thư viện cho phép giao tiếp thời gian thực

// Đăng ký NotificationService
builder.Services.AddScoped<Bloomie.Services.INotificationService, Bloomie.Services.NotificationService>();

// Đăng ký AutoReplyService
builder.Services.AddScoped<Bloomie.Services.AutoReplyService>();

// Đăng ký RateLimitService (Singleton để share state across requests)
builder.Services.AddSingleton<Bloomie.Services.RateLimitService>();

// Đăng ký SpamDetectionService (Scoped để inject DbContext)
builder.Services.AddScoped<Bloomie.Services.SpamDetectionService>();

// // Connect MomoAPI
builder.Services.Configure<MomoOptionModel>(builder.Configuration.GetSection("MomoAPI"));
builder.Services.AddScoped<IMomoService, MomoService>();

// Connect VnPayService (nếu chưa dùng thì tạm thời comment hoặc tạo service rỗng)
builder.Services.AddScoped<IVNPAYService, VNPAYService>();

// Cấu hình logging
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole(); // Ghi log ra console
    logging.AddDebug();   // Ghi log ra debug output (Visual Studio)
    
    // Enable SignalR detailed logging to see hub invocation errors
    logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Debug);
    logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Debug);
});

// Cấu hình Email Service
builder.Services.AddTransient<IEmailService, EmailService>();

// Cấu hình OrderCancellationService
builder.Services.AddScoped<OrderCancellationService>();

// Đăng ký dịch vụ tự động hoàn thành đơn hàng
builder.Services.AddHostedService<OrderAutoCompleteService>();

// Đăng ký dịch vụ tự động xóa lịch sử xem cũ
builder.Services.AddHostedService<RecentlyViewedCleanupService>();

// Đăng ký ShippingService
builder.Services.AddScoped<IShippingService, ShippingService>();

// Đăng ký ShipperAssignmentService
builder.Services.AddScoped<IShipperAssignmentService, ShipperAssignmentService>();

// Đăng ký Gemini AI Service
builder.Services.AddScoped<IGeminiService, GeminiService>();

// Đăng ký IHttpContextAccessor (required for Session access in services)
builder.Services.AddHttpContextAccessor();

// Đăng ký ChatBot Function Service (for AI function calling)
builder.Services.AddScoped<IChatBotFunctionService, ChatBotFunctionService>();

// Đăng ký ChatBot Service
builder.Services.AddScoped<IChatBotService, ChatBotService>();

// Đăng ký Flower Detection Service
builder.Services.AddHttpClient(); // Required for HttpClientFactory
builder.Services.AddScoped<IFlowerDetectionService, FlowerDetectionService>();

// Cấu hình CORS cho phép Flutter app gửi/nhận cookie
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.WithOrigins(
                "http://localhost:5229",
                "http://10.0.2.2:5229", // Android emulator
                "http://127.0.0.1:5229",
                "http://192.168.2.177:5229"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials(); // Quan trọng: Cho phép gửi/nhận cookie
    });
});

// Cấu hình Session và Cache
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".Bloomie.Session";
    options.Cookie.SameSite = SameSiteMode.Lax; 
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; 
});

// Cấu hình Controllers và Views
builder.Services.AddControllersWithViews();

//builder.Services.AddControllers()
//    .AddApplicationPart(typeof(Bloomie.Areas.Admin.Controllers.NotificationsController).Assembly);

// Cấu hình Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Cấu hình Hangfire
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"), new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));

builder.Services.AddHangfireServer();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Gán provider custom cho từng loại token
    options.Tokens.EmailConfirmationTokenProvider = "CustomEmail";
    options.Tokens.PasswordResetTokenProvider = "CustomReset";
    options.Tokens.AuthenticatorTokenProvider = "Custom2FA";
})
    .AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders()
    .AddTokenProvider<CustomEmailTokenProvider<ApplicationUser>>("CustomEmail")
    .AddTokenProvider<CustomEmailTokenProvider<ApplicationUser>>("CustomReset")
    .AddTokenProvider<CustomEmailTokenProvider<ApplicationUser>>("Custom2FA");

// Cấu hình thời gian sống cho từng provider
builder.Services.Configure<DataProtectionTokenProviderOptions>("CustomEmail", opt =>
{
    opt.TokenLifespan = TimeSpan.FromHours(24); // Xác thực email: 24h
});
builder.Services.Configure<DataProtectionTokenProviderOptions>("CustomReset", opt =>
{
    opt.TokenLifespan = TimeSpan.FromHours(1); // Đặt lại mật khẩu: 1h
});
builder.Services.Configure<DataProtectionTokenProviderOptions>("Custom2FA", opt =>
{
    opt.TokenLifespan = TimeSpan.FromMinutes(5); // 2FA: 5 phút
});

builder.Services.Configure<IdentityOptions>(options =>
{
    // Password settings.
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
    options.Password.RequiredLength = 12;
    options.Password.RequiredUniqueChars = 1;

    // Lockout settings.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings.
    options.User.AllowedUserNameCharacters =
    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
    options.User.RequireUniqueEmail = true;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    // Cookie settings
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);

    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
});

// builder.Services.Configure<DataProtectionTokenProviderOptions>(opt =>
// {
//     opt.TokenLifespan = TimeSpan.FromMinutes(15);
// });

// Cấu hình xác thực qua Google, Facebook, Twitter
builder.Services.AddAuthentication(options =>
{
    // options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    // options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    // options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
}).AddCookie().AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
{
    options.ClientId = builder.Configuration.GetSection("GoogleKeys:ClientId").Value;
    options.ClientSecret = builder.Configuration.GetSection("GoogleKeys:ClientSecret").Value;
}).AddFacebook(facebookOptions =>
{
    facebookOptions.AppId = builder.Configuration.GetSection("FacebookKeys:AppId").Value;
    facebookOptions.AppSecret = builder.Configuration.GetSection("FacebookKeys:AppSecret").Value;
    facebookOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;;
    facebookOptions.Scope.Add("email"); // Thêm dòng này
    facebookOptions.Fields.Add("email");

    // Xử lý lỗi ngay trong middleware
    facebookOptions.Events.OnRemoteFailure = context =>
    {
        context.Response.Redirect("/Account/Login?info=" + Uri.EscapeDataString("Bạn hãy đăng nhập bằng Facebook."));
        context.HandleResponse(); // Ngăn middleware tiếp tục xử lý
        return Task.CompletedTask;
    };
// }).AddTwitter(twitterOptions =>
// {
//     twitterOptions.ConsumerKey = builder.Configuration.GetSection("TwitterKeys:ClientId").Value;
//     twitterOptions.ConsumerSecret = builder.Configuration.GetSection("TwitterKeys:ClientSecret").Value;
}); 

// Cấu hình Data Protection
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "Keys")))
    .SetApplicationName("BloomieApp");

// Cấu hình cho macOS để tránh lỗi GDI+
if (OperatingSystem.IsMacOS())
{
    Environment.SetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1");
    AppContext.SetSwitch("System.Drawing.EnableUnixSupport", false);
}

// // Connect VNPay API
// builder.Services.AddScoped<IVnPayService, VnPayService>();

builder.Services.AddHostedService<AutoHardDeleteService>();

var app = builder.Build();

// Khai báo license cho QuestPDF (bắt buộc)
QuestPDF.Settings.License = LicenseType.Community;

// Tạo roles và admin account
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    // Tạo roles
    string[] roles = new[] { "Admin", "User", "Manager", "Staff", "Shipper" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole { Name = role, NormalizedName = role.ToUpper() });
        }
    }

    // 🔒 Tạo SUPER ADMIN (Admin gốc - không thể bị xóa)
    string superAdminEmail = "superadmin@bloomie.com";
    string superAdminPassword = "SuperAdmin@123456789";
    string superAdminUserName = "superadmin";
    string superAdminFullName = "Super Administrator";

    var superAdmin = await userManager.FindByEmailAsync(superAdminEmail);
    if (superAdmin == null)
    {
        superAdmin = new ApplicationUser
        {
            UserName = superAdminUserName,
            Email = superAdminEmail,
            FullName = superAdminFullName,
            RoleId = (await roleManager.FindByNameAsync("Admin"))?.Id,
            Token = Guid.NewGuid().ToString(),
            EmailConfirmed = true,
            IsSuperAdmin = true, // ⭐ Đánh dấu là Super Admin
            CreatedAt = DateTime.UtcNow
        };
        var result = await userManager.CreateAsync(superAdmin, superAdminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(superAdmin, "Admin");
        }
    }
    else if (!superAdmin.IsSuperAdmin)
    {
        // Nếu Super Admin đã tồn tại nhưng chưa được đánh dấu
        superAdmin.IsSuperAdmin = true;
        await userManager.UpdateAsync(superAdmin);
    }

    // Tạo admin account thường (có thể bị xóa)
    string adminEmail = "admin@bloomie.com";
    string adminPassword = "Admin@123456789";
    string adminUserName = "admin";
    string adminFullName = "Administrator";

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminUserName,
            Email = adminEmail,
            FullName = adminFullName,
            RoleId = (await roleManager.FindByNameAsync("Admin"))?.Id,
            Token = Guid.NewGuid().ToString(),
            IsSuperAdmin = false, // Không phải Super Admin - có thể bị xóa bởi Super Admin
            CreatedByUserId = superAdmin.Id, // Ghi lại ai tạo user này
            CreatedAt = DateTime.UtcNow
        };
        var result = await userManager.CreateAsync(adminUser, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
    }

    // Tự động tạo ShipperProfile cho tất cả user có role Shipper
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var shippers = await userManager.GetUsersInRoleAsync("Shipper");
    
    foreach (var shipper in shippers)
    {
        var existingProfile = await dbContext.ShipperProfiles
            .FirstOrDefaultAsync(sp => sp.UserId == shipper.Id);
        
        if (existingProfile == null)
        {
            var newProfile = new ShipperProfile
            {
                UserId = shipper.Id,
                IsWorking = true, // Mặc định đang làm việc
                MaxActiveOrders = 2, // Tối đa 2 đơn cùng lúc
                CurrentActiveOrders = 0,
                CreatedAt = DateTime.Now
            };
            dbContext.ShipperProfiles.Add(newProfile);
        }
    }
    
    await dbContext.SaveChangesAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

// Thêm CORS middleware - PHẢI đặt sau UseRouting và trước UseAuthentication
app.UseCors();

// Thêm Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

// ⭐ Cấu hình Hangfire Recurring Job - Tự động phân công shipper cho đơn đặt trước
RecurringJob.AddOrUpdate<IShipperAssignmentService>(
    "auto-assign-preorders",
    service => service.AutoAssignPreOrdersForToday(),
    "0 6 * * *"); // Chạy lúc 06:00 sáng mỗi ngày

// ⏰ Cấu hình Hangfire Recurring Job - Kiểm tra đơn hàng URGENT
RecurringJob.AddOrUpdate<IShipperAssignmentService>(
    "check-urgent-orders",
    service => service.CheckUrgentOrders(),
    "*/10 * * * *"); // Chạy mỗi 10 phút

app.UseSession(); // Session phải đứng trước Authentication/Authorization
app.UseAuthentication();
app.UseAuthorization();

// Đăng ký middleware để ghi log truy cập người dùng
app.UseUserAccessLogging();

// Map SignalR Hub
app.MapHub<Bloomie.Hubs.NotificationHub>("/notificationHub");
app.MapHub<Bloomie.Hubs.ChatHub>("/chatHub");

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
        name: "areas",
        pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    endpoints.MapControllers(); // Enable API Controllers
});

//app.MapAreaControllerRoute(
//    name: "admin",
//    areaName: "Admin",
//    pattern: "Admin/{controller=Home}/{action=Index}/{id?}");
//app.MapControllers();

//app.MapRazorPages();
//app.MapControllerRoute(
//    name: "default",
//    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();