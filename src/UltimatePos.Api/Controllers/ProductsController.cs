using Microsoft.AspNetCore.Mvc;

namespace UltimatePos.Api.Controllers
{
    public class ProductsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
