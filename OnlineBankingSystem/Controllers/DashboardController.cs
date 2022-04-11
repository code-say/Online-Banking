using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace OnlineBankingSystem.Controllers
{
    public class DashboardController : Controller
    {
        // GET: Dashboard

        [Authorize] 
        public ActionResult Dashboard()
        {
            return View();
        }
    }
}