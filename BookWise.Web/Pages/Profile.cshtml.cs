using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BookWise.Web.Pages
{
    public class ProfileModel : PageModel
    {
        public void OnGet()
        {
            ViewData["Title"] = "Profile";
        }
    }
}