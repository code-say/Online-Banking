using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using OnlineBankingSystem.Models;

namespace OnlineBankingSystem.Controllers
{
    public class AccountDetailsController : Controller
    {
        private OnlineBankEntities2 db = new OnlineBankEntities2();

        // GET: AccountDetails
        public ActionResult AccountDetails()
        {
            return View(db.savingsaccounts.ToList());
        }

        // GET: AccountDetails/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            savingsaccount savingsaccount = db.savingsaccounts.Find(id);
            if (savingsaccount == null)
            {
                return HttpNotFound();
            }
            return View(savingsaccount);
        }

        // GET: AccountDetails/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: AccountDetails/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "accountId,Title,FirstName,MiddleName,LastName,FatherName,Mobile,Email,AdharNo,DOB,Addressline1,Addressline2,Landmark,State,City,Pincode,OccupationType,Sourceincome,Grossalary")] savingsaccount savingsaccount)
        {
            if (ModelState.IsValid)
            {
                db.savingsaccounts.Add(savingsaccount);
                db.SaveChanges();
                return RedirectToAction("Index");
            }

            return View(savingsaccount);
        }

        // GET: AccountDetails/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            savingsaccount savingsaccount = db.savingsaccounts.Find(id);
            if (savingsaccount == null)
            {
                return HttpNotFound();
            }
            return View(savingsaccount);
        }

        // POST: AccountDetails/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "accountId,Title,FirstName,MiddleName,LastName,FatherName,Mobile,Email,AdharNo,DOB,Addressline1,Addressline2,Landmark,State,City,Pincode,OccupationType,Sourceincome,Grossalary")] savingsaccount savingsaccount)
        {
            if (ModelState.IsValid)
            {
                db.Entry(savingsaccount).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(savingsaccount);
        }

        // GET: AccountDetails/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            savingsaccount savingsaccount = db.savingsaccounts.Find(id);
            if (savingsaccount == null)
            {
                return HttpNotFound();
            }
            return View(savingsaccount);
        }

        // POST: AccountDetails/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            savingsaccount savingsaccount = db.savingsaccounts.Find(id);
            db.savingsaccounts.Remove(savingsaccount);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
