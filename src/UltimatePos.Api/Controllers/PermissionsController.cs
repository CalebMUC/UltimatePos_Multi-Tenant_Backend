using Microsoft.AspNetCore.Mvc;

namespace UltimatePos.Api.Controllers
{
    public class PermissionsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
