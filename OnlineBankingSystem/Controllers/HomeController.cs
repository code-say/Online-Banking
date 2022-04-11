
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using OnlineBankingSystem.Models;

namespace OnlineBankingSystem.Controllers
{
    public class HomeController : Controller
    {
        OnlineBankEntities1 dbModel = new OnlineBankEntities1();
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        [HttpGet]
        public ActionResult Contact()
        {
            sendMail sm = new sendMail();

            ViewBag.Message = "Your contact page.";

            return View(sm);
        }

        [HttpPost]
        public ActionResult Contact(sendMail Model)
        {
            if (!ModelState.IsValid) return View();

            try
            {
                MailMessage mail = new MailMessage();
                mail.From = new MailAddress("mandalsaya4@gmail.com");
                mail.To.Add("sayancse@hotmail.com");

                mail.Subject = Model.Subject;

                mail.IsBodyHtml = true; //true

                string content = "name : " + Model.Name;
                content += "<br/> Message : " + Model.Message;

                mail.Body = content;

                //create SMTP instant

                //you need to pass mail server address and you can also specify the port number if you required
                SmtpClient smtpClient = new SmtpClient();
                smtpClient.Host = "smtp.gmail.com";
                //Create nerwork credential and you need to give from email address and password
                NetworkCredential networkCredential = new NetworkCredential("mandalsaya4@gmail.com", "sayan@8643");
                smtpClient.UseDefaultCredentials = false; //false
                smtpClient.Credentials = networkCredential;
               /* smtpClient.Port = 25; // this is default port number - you can also change this
                smtpClient.EnableSsl = false; // if ssl required you need to enable it //false*/
                smtpClient.Send(mail);

                ViewBag.Message = "Mail has been Sent Succesfully";

                // now i need to create the from 
                ModelState.Clear();

            }
            catch (Exception ex)
            {
                //If any error occured it will show 
                ViewBag.Message = ex.Message.ToString();
            }

            return View();
        }

        [Authorize]
        public ActionResult InternetBanking() //InternetBanking model
        {
            InternetBanking ib = new InternetBanking();
            return View(ib);
        }

        [HttpPost]
        public ActionResult InternetBanking(InternetBanking Model)
        {
           
                dbModel.InternetBankings.Add(Model);
                dbModel.SaveChanges();

            ModelState.Clear();
            ViewBag.SuccessMessage = " Internet Banking form Succesfully Submitted.";
            return View("InternetBanking", new InternetBanking());
            /*return RedirectToAction("");     */   
        }


    }
}