using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.Owin;
using OnlineBankingSystem.Models;
using Owin;

[assembly: OwinStartupAttribute(typeof(OnlineBankingSystem.Startup))]
namespace OnlineBankingSystem
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
            createRolesandUsers();
        }

        public void createRolesandUsers()
        {
            ApplicationDbContext context = new ApplicationDbContext();

            var roleManager = new RoleManager<IdentityRole>(new RoleStore<IdentityRole>(context));
            var UserManager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(context));

          /*  var user = new ApplicationUser
            {
                UserName = "admin",
                Email = "admin@online.com",
                BirthDate = System.DateTime.Now
            };

            var password = "password";

            var usr = UserManager.Create(user, password);

            if (usr.Succeeded)
            {
                var result = UserManager.AddToRole(user.Id, "Admin");
            }*/


            if (!roleManager.RoleExists("Admin"))
            {
                var role = new IdentityRole();
                role.Name = "Admin";
                roleManager.Create(role);

                var user = new ApplicationUser
                {
                    UserName = "admin",
                    Email = "admin@online.com",
                    BirthDate = System.DateTime.Now
                };

                var password = "password";

                var usr = UserManager.Create(user, password);

                if (usr.Succeeded)
                {
                    var result = UserManager.AddToRole(user.Id, "Admin");
                }
            }

           

            if (!roleManager.RoleExists("Manager"))
            {
                var role = new IdentityRole();
                role.Name = "Manager";
                roleManager.Create(role);

            }

            if (!roleManager.RoleExists("Customer"))
            {
                var role = new IdentityRole();
                role.Name = "Customer";
                roleManager.Create(role);

            }
        }

    }
}
