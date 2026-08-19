using Microsoft.AspNetCore.Mvc;

namespace Essessay.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}
