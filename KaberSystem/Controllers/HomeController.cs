using Microsoft.AspNetCore.Mvc;

namespace KaberSystem.Controllers
{
    public class HomeController : Controller
    {
        // عرض الصفحة الرئيسية للموقع
        public IActionResult Index()
        {
            ViewData["IsHome"] = true;
            return View();
        }

        // عرض صفحة من نحن
        public IActionResult About()
        {
            return View();
        }

        // عرض صفحة اتصل بنا
        public IActionResult Contact()
        {
            return View();
        }

        // عرض صفحة المدونة
        public IActionResult Blog()
        {
            return View();
        }

        // عرض صفحة تفاصيل المدونة
        public IActionResult BlogDetails()
        {
            return View();
        }

        // عرض صفحة تفاصيل الخدمة
        public IActionResult ServiceDetails()
        {
            return View();
        }

        // صفحة الخطأ الافتراضية
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
}