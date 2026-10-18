// Owner: Member 2 (Tables & Reservations) - US11
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services.Interfaces;

namespace RROMS.Pages.Admin.Tables;

// Role protection for /Admin is done by Program.cs, so no [Authorize] is needed here.
public class IndexModel : PageModel
{
    private readonly IReservationService _service;
    public IndexModel(IReservationService service) => _service = service;

    public List<DiningArea> Areas { get; set; } = new();
    public List<RestaurantTable> AllTables =>
        Areas.SelectMany(a => a.Tables).OrderBy(t => t.Type).ThenBy(t => t.TableNumber.Length).ThenBy(t => t.TableNumber).ToList();

    [BindProperty] public AreaInput AreaForm { get; set; } = new();
    [BindProperty] public TableInput TableForm { get; set; } = new();
    [BindProperty] public List<OperatingHour> Hours { get; set; } = new();

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public class AreaInput
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Area name is required."), StringLength(60)] public string Name { get; set; } = string.Empty;
        [StringLength(200)] public string? Description { get; set; }
    }

    public class TableInput
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Table name is required."), StringLength(10, ErrorMessage = "Table name can be at most 10 characters."), Display(Name = "Table name")] public string TableNumber { get; set; } = string.Empty;
        public TableType Type { get; set; }
        [Required(ErrorMessage = "Choose a dining area.")] public int? DiningAreaId { get; set; }
        [Range(1, 20, ErrorMessage = "Minimum guests must be between 1 and 20.")] public int MinGuests { get; set; } = 1;
        [Range(1, 20, ErrorMessage = "Capacity must be between 1 and 20.")] public int Capacity { get; set; } = 2;
        [Range(0, 100000, ErrorMessage = "Minimum spend must be 0 or more.")] public decimal MinimumSpend { get; set; }
        [StringLength(300)] public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public async Task OnGetAsync()
    {
        Areas = await _service.GetAreasWithTablesAsync();
        Hours = await _service.GetOperatingHoursAsync();
    }

    public async Task<IActionResult> OnPostSaveAreaAsync()
    {
        KeepOnly(nameof(AreaForm));
        if (!ModelState.IsValid) return InvalidInput();
        var result = await _service.SaveAreaAsync(new DiningArea { Id = AreaForm.Id, Name = AreaForm.Name, Description = AreaForm.Description });
        return Done(result, "Dining area saved.");
    }

    public async Task<IActionResult> OnPostDeleteAreaAsync(int id) =>
        Done(await _service.DeleteAreaAsync(id), "Dining area deleted.");

    public async Task<IActionResult> OnPostSaveTableAsync()
    {
        KeepOnly(nameof(TableForm));
        if (!ModelState.IsValid) return InvalidInput();
        var result = await _service.SaveTableAsync(new RestaurantTable
        {
            Id = TableForm.Id,
            TableNumber = TableForm.TableNumber,
            Type = TableForm.Type,
            DiningAreaId = TableForm.DiningAreaId!.Value,
            MinGuests = TableForm.MinGuests,
            Capacity = TableForm.Capacity,
            MinimumSpend = TableForm.MinimumSpend,
            Description = TableForm.Description,
            IsActive = TableForm.IsActive
        });
        return Done(result, "Table saved.");
    }

    public async Task<IActionResult> OnPostDeleteTableAsync(int id) =>
        Done(await _service.DeleteTableAsync(id), "Table deleted.");

    public async Task<IActionResult> OnPostTypePhotoAsync(TableType type, IFormFile? photo) =>
        Done(await _service.SetTypePhotoAsync(type, photo!), "Table type photo updated on this computer.");

    public async Task<IActionResult> OnPostStatusAsync(int id, TableStatus status) =>
        Done(await _service.SetTableStatusAsync(id, status), "Table status updated.");

    public async Task<IActionResult> OnPostPhotoAsync(int id, IFormFile? photo) =>
        Done(await _service.SetTablePhotoAsync(id, photo!), "Photo updated.");

    public async Task<IActionResult> OnPostSaveHoursAsync()
    {
        KeepOnly(nameof(Hours));
        return Done(await _service.SaveOperatingHoursAsync(Hours), "Opening hours saved.");
    }

    // The three forms share one page, so ignore validation errors that belong to the other forms.
    private void KeepOnly(string prefix)
    {
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith(prefix)).ToList()) ModelState.Remove(key);
    }

    private IActionResult InvalidInput()
    {
        Error = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        return RedirectToPage();
    }

    private IActionResult Done(RROMS.Services.ReservationResult result, string success)
    {
        if (result.Success) Message = success; else Error = result.Error;
        return RedirectToPage();
    }
}
