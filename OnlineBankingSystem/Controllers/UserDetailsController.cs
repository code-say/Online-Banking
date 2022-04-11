using OnlineBankingSystem.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace OnlineBankingSystem.Controllers
{
    public class UserDetailsController : Controller
    {
        OnlineBankEntities1 dbModel = new OnlineBankEntities1();
        // GET: UserDetails
        [HttpGet]
        public ActionResult userDetails()
        {
            AspNetUser sa = new AspNetUser();

            return View(sa);
        }

        [HttpPost]
        public ActionResult userDetails(AspNetUser Model)
        {
            dbModel.AspNetUsers.Add(Model);
            dbModel.SaveChanges();

            ModelState.Clear();
            ViewBag.SuccessMessage = "Your user Details Added Succesfully.";
            return RedirectToAction("uDetails");
        }

        public ActionResult uDetails()
        {
            var fetch = dbModel.AspNetUsers.ToList();

            return View(fetch);
        }

        [HttpGet]
        public ActionResult Edit(string id)
        {
            var obj = dbModel.AspNetUsers.Single(x => x.Id == id);

            return View(obj);
        }

        [HttpPost]
        public ActionResult Edit(AspNetUser sa)
        {
            if (ModelState.IsValid)
            {
                dbModel.Entry(sa).State = EntityState.Modified;
                dbModel.SaveChanges();
                return RedirectToAction("uDetails");
            }

            return View(sa);
        }

        [HttpGet]
        public ActionResult Delete(string id)
        {
            var obj = dbModel.AspNetUsers.Single(x => x.Id == id);
            dbModel.AspNetUsers.Remove(obj);
            dbModel.SaveChanges();
            return RedirectToAction("uDetails");
        }
    }
}