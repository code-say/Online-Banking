using OnlineBankingSystem.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace OnlineBankingSystem.Controllers
{
    public class TransferController : Controller
    {
        OnlineBankEntities3 dbModel = new OnlineBankEntities3();
        // GET: Transfer

        [Authorize]
        [HttpGet]
        public ActionResult Transfer()
        {
            Transfer sa = new Transfer();

            return View(sa);
        }

        [HttpPost]
        public ActionResult Transfer(Transfer Model)
        {
                dbModel.Transfers.Add(Model);
                dbModel.SaveChanges();

                ModelState.Clear();
                ViewBag.SuccessMessage = "Your transfer Details Added Succesfully.";
                 return RedirectToAction("TransferDeatils");
        }

        public ActionResult TransferDeatils()
        {
            var fetch = dbModel.Transfers.ToList();

            return View(fetch);
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            var obj = dbModel.Transfers.Single(x => x.Userid == id);

            return View(obj);
        }

        [HttpPost]
        public ActionResult Edit(Transfer sa)
        {
            if (ModelState.IsValid)
            {
                dbModel.Entry(sa).State = EntityState.Modified;
                dbModel.SaveChanges();
                return RedirectToAction("TransferDeatils");
            }

            return View(sa);
        }

        [HttpGet]
        public ActionResult Delete(int id)
        {
            var obj = dbModel.Transfers.Single(x => x.Userid == id);
            dbModel.Transfers.Remove(obj);
            dbModel.SaveChanges();
            return RedirectToAction("TransferDeatils");
        }
    }
}