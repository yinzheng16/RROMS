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
    public List<TableTypeDefinition> TableTypes { get; set; } = new();
    public List<RestaurantTable> AllTables =>
        Areas.SelectMany(a => a.Tables).OrderBy(t => t.Type).ThenBy(t => t.TableNumber.Length).ThenBy(t => t.TableNumber).ToList();

    [BindProperty] public TableTypeInput TableTypeForm { get; set; } = new();
    [BindProperty] public AreaInput AreaForm { get; set; } = new();
    [BindProperty] public TableInput TableForm { get; set; } = new();
    [BindProperty] public List<OperatingHour> Hours { get; set; } = new();

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public class TableTypeInput
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Table type name is required.")]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Prefix is required.")]
        [RegularExpression(@"^[A-Za-z]+$", ErrorMessage = "Prefix must contain letters only.")]
        [StringLength(10)]
        public string Prefix { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int StartingNumber { get; set; } = 1;

        [Range(1, 10)]
        public int NumberPadding { get; set; } = 1;

        [Required(ErrorMessage = "Choose a dining area.")]
        public int? DefaultDiningAreaId { get; set; }

        [Range(1, 20)]
        public int DefaultMinGuests { get; set; } = 1;

        [Range(1, 20)]
        public int DefaultMaxGuests { get; set; } = 2;

        [Range(0, 100000)]
        public decimal? DefaultMinimumSpend { get; set; }

        [StringLength(300)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public bool CreateFirstTable { get; set; }

        [StringLength(10)]
        public string? FirstTableName { get; set; }

        public bool ApplyToExistingTables { get; set; }
        public IFormFile? Photo { get; set; }
    }

    public class AreaInput
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Area name is required.")]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string? Description { get; set; }

        [StringLength(40)]
        public string IconName { get; set; } = "layout-grid";
    }

    public class TableInput
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Table name is required."), StringLength(10, ErrorMessage = "Table name can be at most 10 characters."), Display(Name = "Table name")] public string TableNumber { get; set; } = string.Empty;
        public TableType Type { get; set; }
        public int? TableTypeDefinitionId { get; set; }
        [Required(ErrorMessage = "Choose a dining area.")] public int? DiningAreaId { get; set; }
        [Range(1, 20, ErrorMessage = "Minimum guests must be between 1 and 20.")] public int MinGuests { get; set; } = 1;
        [Range(1, 20, ErrorMessage = "Capacity must be between 1 and 20.")] public int Capacity { get; set; } = 2;
        [Range(0, 100000, ErrorMessage = "Minimum spend must be 0 or more.")] public decimal MinimumSpend { get; set; }
        [StringLength(300)] public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public TableStatus Status { get; set; } = TableStatus.Available;
    }

    public async Task OnGetAsync()
    {
        Areas = await _service.GetAreasWithTablesAsync();
        TableTypes = await _service.GetTableTypesAsync();
        Hours = await _service.GetOperatingHoursAsync();
    }

    public async Task<IActionResult> OnPostSaveTableTypeAsync()
    {
        KeepOnly(nameof(TableTypeForm));

        if (TableTypeForm.DefaultMinGuests > TableTypeForm.DefaultMaxGuests)
        {
            ModelState.AddModelError(
                "TableTypeForm.DefaultMaxGuests",
                "Maximum guests must be at least minimum guests.");
        }

        if (TableTypeForm.CreateFirstTable && TableTypeForm.Id != 0)
        {
            ModelState.AddModelError(
                "TableTypeForm.CreateFirstTable",
                "First table creation is only available for new types.");
        }

        if (!ModelState.IsValid)
            return InvalidInput();

        var input = new TableTypeDefinition
        {
            Id = TableTypeForm.Id,
            Name = TableTypeForm.Name,
            Prefix = TableTypeForm.Prefix,
            StartingNumber = TableTypeForm.StartingNumber,
            NumberPadding = TableTypeForm.NumberPadding,
            DefaultDiningAreaId = TableTypeForm.DefaultDiningAreaId!.Value,
            DefaultMinGuests = TableTypeForm.DefaultMinGuests,
            DefaultMaxGuests = TableTypeForm.DefaultMaxGuests,
            DefaultMinimumSpend = TableTypeForm.DefaultMinimumSpend,
            Description = TableTypeForm.Description,
            IsActive = TableTypeForm.IsActive
        };

        var result = await _service.SaveTableTypeAsync(
            input,
            TableTypeForm.CreateFirstTable,
            TableTypeForm.FirstTableName,
            TableTypeForm.ApplyToExistingTables);

        if (!result.Success)
            return Done(result, "Table type saved.");

        // Upload photo only when Admin selected a file.
        if (TableTypeForm.Photo is { Length: > 0 })
        {
            var photoResult = await _service.SetTableTypePhotoAsync(
                input.Id,
                TableTypeForm.Photo);

            if (!photoResult.Success)
            {
                ErrorMessage = "Table type saved, but photo upload failed: "
                                + photoResult.Error;

                return RedirectToPage();
            }
        }

        return Done(result, "Table type saved.");
    }

    public async Task<IActionResult> OnPostDeleteTableTypeAsync(int id) =>
    Done(
        await _service.DeleteTableTypeAsync(id),
        "Table type deleted.");

    public async Task<IActionResult> OnPostTableTypePhotoAsync(
        int id,
        IFormFile? photo) =>
        Done(
            await _service.SetTableTypePhotoAsync(id, photo!),
            "Table type photo updated.");

    public async Task<IActionResult> OnGetNextTableNameAsync(int id)
    {
        try
        {
            var name = await _service.GetNextTableNameAsync(id);
            return new JsonResult(new { success = true, name });
        }
        catch (Exception ex) when (
            ex is ArgumentException ||
            ex is InvalidOperationException)
        {
            return new JsonResult(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    public async Task<IActionResult> OnPostSaveAreaAsync()
    {
        KeepOnly(nameof(AreaForm));

        var allowedIcons = new HashSet<string>
    {
        "wine",
        "utensils-crossed",
        "door-closed",
        "trees",
        "sunset",
        "coffee",
        "sofa",
        "layout-grid"
    };

        if (!allowedIcons.Contains(AreaForm.IconName))
        {
            ModelState.AddModelError(
                "AreaForm.IconName",
                "Please select a valid area icon.");
        }

        if (!ModelState.IsValid)
            return InvalidInput();

        var area = new DiningArea
        {
            Id = AreaForm.Id,
            Name = AreaForm.Name,
            Description = AreaForm.Description,
            IconName = AreaForm.IconName
        };

        var result = await _service.SaveAreaAsync(area);

        return Done(result, "Dining area saved.");
    }

    public async Task<IActionResult> OnPostDeleteAreaAsync(int id) =>
        Done(await _service.DeleteAreaAsync(id), "Dining area deleted.");

    public async Task<IActionResult> OnPostSaveTableAsync()
    {
        KeepOnly(nameof(TableForm));
        if (!Enum.IsDefined(TableForm.Type))
            ModelState.AddModelError("TableForm.Type", "Choose a valid table type.");
        if (TableForm.MinGuests > TableForm.Capacity)
            ModelState.AddModelError("TableForm.Capacity", "Maximum capacity must be at least the minimum booking guests.");
        if (!Enum.IsDefined(TableForm.Status))
            ModelState.AddModelError("TableForm.Status", "Choose a valid table status.");
        if (!ModelState.IsValid) return InvalidInput();
        var result = await _service.SaveTableAsync(new RestaurantTable
        {
            Id = TableForm.Id,
            TableNumber = TableForm.TableNumber,
            Type = TableForm.Type,
            TableTypeDefinitionId = TableForm.TableTypeDefinitionId,
            DiningAreaId = TableForm.DiningAreaId!.Value,
            MinGuests = TableForm.MinGuests,
            Capacity = TableForm.Capacity,
            MinimumSpend = TableForm.MinimumSpend,
            Description = TableForm.Description,
            IsActive = TableForm.IsActive,
            Status = TableForm.Status
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
        ErrorMessage = string.Join(" ",
            ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));

        return RedirectToPage();
    }

    private IActionResult Done(RROMS.Services.ReservationResult result, string success)
    {
        if (result.Success)
            SuccessMessage = success;
        else
            ErrorMessage = result.Error;

        return RedirectToPage();
    }
}
