using Microsoft.AspNetCore.Mvc;

namespace UltimatePos.Api.Controllers
{
    public class SalesController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
