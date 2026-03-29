using KaberSystem.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// 📌 التحديث الأهم: إضافة ميزة "إعادة المحاولة التلقائية" لحماية الاتصال بقاعدة البيانات الخارجية
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5, // سيحاول الاتصال 5 مرات قبل أن يستسلم
                maxRetryDelay: TimeSpan.FromSeconds(30), // أقصى مدة للانتظار بين المحاولات
                errorNumbersToAdd: null);
        }));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, config =>
    {
        config.Cookie.Name = "KaberSystem.Auth";
        config.LoginPath = "/Account/Login";
        config.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        // 📌 ملاحظة: بما أنك تستخدم المايجريشن حالياً، تأكد أن الدالة 
        // context.Database.EnsureCreated() داخل DbInitializer.cs قد تم تعطيلها لكي لا تتعارض مع المايجريشن
        DbInitializer.Initialize(context);
    }
    catch (Exception ex)
    {
        Console.WriteLine("An error occurred while seeding the database: " + ex.Message);
    }
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();