using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Menu
{
    // Public image delivery only. Does not expose the admin menu or permit ordering.
    [AllowAnonymous]
    public class ItemImageModel : PageModel
    {
        private readonly IOrderService _service;
        public ItemImageModel(IOrderService service) { _service = service; }
        public async Task<IActionResult> OnGetAsync(string fileName, CancellationToken cancellationToken)
        {
            var image = await _service.OpenMenuImageAsync(fileName, cancellationToken);
            if (image is null) return NotFound();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "public,max-age=3600";
            return File(image.Content, image.ContentType);
        }
    }
}
