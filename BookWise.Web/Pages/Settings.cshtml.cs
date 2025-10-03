using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BookWise.Web.Pages
{
    public class SettingsModel : PageModel
    {
        public void OnGet()
        {
            ViewData["Title"] = "Settings";
        }
    }
}
