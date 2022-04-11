using OnlineBankingSystem.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;

namespace OnlineBankingSystem.Controllers
{
    public class SavingsaccountController : Controller
    {
        OnlineBankEntities2 dbModel = new OnlineBankEntities2();
        // GET: Transfer

        [Authorize]
        [HttpGet]
        public ActionResult SavingsAccount()
        {
            savingsaccount sa = new savingsaccount();

            return View(sa);
        }

        [HttpPost]
        public ActionResult SavingsAccount(savingsaccount Model)
        {
            dbModel.savingsaccounts.Add(Model);
            dbModel.SaveChanges();

            ModelState.Clear();
            ViewBag.SuccessMessage = "Your transfer Details Added Succesfully.";
            return RedirectToAction("AccountDetails");
        }

        public ActionResult AccountDetails()
        {
            var fetch = dbModel.savingsaccounts.ToList();

            return View(fetch);
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            var obj = dbModel.savingsaccounts.Single(x => x.accountId == id);

            return View(obj);
        }

        [HttpPost]
        public ActionResult Edit(savingsaccount sa)
        {
            if (ModelState.IsValid)
            {
                dbModel.Entry(sa).State = EntityState.Modified;
                dbModel.SaveChanges();
                return RedirectToAction("AccountDetails");
            }

            return View(sa);
        }

        [HttpGet]
        public ActionResult Delete(int id)
        {
            var obj = dbModel.savingsaccounts.Single(x => x.accountId == id);
            dbModel.savingsaccounts.Remove(obj);
            dbModel.SaveChanges();
            return RedirectToAction("AccountDetails");
        }

      /*  [HttpPost]
        [ActionName("Delete")]
        public ActionResult DeleteConfirmed(int id)
        {
            var obj = dbModel.savingsaccounts.Single(x => x.accountId == id);
            dbModel.savingsaccounts.Remove(obj);
            dbModel.SaveChanges();

            return RedirectToAction("Index");
        }
*/

        public ActionResult Details(int accountId)
        {
            var obj = dbModel.savingsaccounts.Find();

            return View(obj);
        }

    }
}