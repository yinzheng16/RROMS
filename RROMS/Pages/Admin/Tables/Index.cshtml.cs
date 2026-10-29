// Owner: Member 2 (Tables & Reservations) - US11
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RROMS.Models;
using RROMS.Services;
using RROMS.Services.Interfaces;
using System.ComponentModel.DataAnnotations;
using RROMS.Data;
using Microsoft.EntityFrameworkCore;

namespace RROMS.Pages.Admin.Tables;

// Role protection for /Admin is done by Program.cs, so no [Authorize] is needed here.
public class IndexModel : PageModel
{
    private readonly IReservationService _service;
    private readonly ApplicationDbContext _db;
    public IndexModel(IReservationService service, ApplicationDbContext db)
    {
    _service = service;
    _db = db;
    }
    public List<DiningArea> Areas { get; set; } = new();
    public List<TableTypeDefinition> TableTypes { get; set; } = new();
    public List<RestaurantTable> AllTables =>
        Areas.SelectMany(a => a.Tables).OrderBy(t => t.Type).ThenBy(t => t.TableNumber.Length).ThenBy(t => t.TableNumber).ToList();

    [BindProperty] public TableTypeInput TableTypeForm { get; set; } = new();
    [BindProperty] public AreaInput AreaForm { get; set; } = new();
    [BindProperty] public TableInput TableForm { get; set; } = new();
    [BindProperty] public List<OperatingHour> Hours { get; set; } = new();
    // Special Dates
    public List<SpecialDate> SpecialDates { get; set; } = new();

    [BindProperty]
    public SpecialDate SpecialDateInput { get; set; } = new();

    [BindProperty]
    public int DeleteSpecialDateId { get; set; }

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public class TableTypeInput
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Table type name is required.")]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        // Shown to customers next to the English name, e.g. 二名席 (optional).
        [StringLength(50)]
        public string? JapaneseName { get; set; }

        // Legacy fields: no longer entered by Admin.
        public string? Prefix { get; set; }

        public int StartingNumber { get; set; } = 1;

        // New numbering rule.
        [Range(1, 4, ErrorMessage = "Number padding must be between 1 and 4.")]
        public int NumberPadding { get; set; } = 3;

        [Range(1, int.MaxValue)]

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

        // Off: parties below Min Guests cannot book this type.
        // On: they can, but must accept the minimum spend.
        public bool AllowBelowMinGuests { get; set; }

        // Set by the "Remove photo" button on the type form.
        public bool RemoveTypePhoto { get; set; }

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
        // Occupied area: all its tables become Out of service.
        public bool IsOccupied { get; set; }
    }


    public class TableInput
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Table number is required.")]
        [StringLength(10, ErrorMessage = "Table number cannot exceed 10 characters.")]
        public string TableNumber { get; set; } = string.Empty;

        // Dynamic Table Type
        public int? TableTypeDefinitionId { get; set; }

        // Editable on existing tables
        public TableStatus Status { get; set; } = TableStatus.Available;

        // Legacy fields retained temporarily for existing UI compatibility.
        // They are no longer used by SaveTableAsync().
        public TableType Type { get; set; }
        public int? DiningAreaId { get; set; }
        public int MinGuests { get; set; } = 1;
        public int Capacity { get; set; } = 2;
        public decimal MinimumSpend { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }


    public async Task OnGetAsync()
    {
        Areas = await _service.GetAreasWithTablesAsync();
        TableTypes = await _service.GetTableTypesAsync();
        Hours = await _service.GetOperatingHoursAsync();
        SpecialDates = await _service.GetSpecialDatesAsync();
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
            JapaneseName = TableTypeForm.JapaneseName,

            // Legacy model compatibility.
            // The service generates Code and Prefix on creation.
            Prefix = string.Empty,
            StartingNumber = 1,

            NumberPadding = TableTypeForm.NumberPadding,
            DefaultDiningAreaId = TableTypeForm.DefaultDiningAreaId!.Value,
            DefaultMinGuests = TableTypeForm.DefaultMinGuests,
            DefaultMaxGuests = TableTypeForm.DefaultMaxGuests,
            DefaultMinimumSpend = TableTypeForm.DefaultMinimumSpend,
            Description = TableTypeForm.Description,
            IsActive = TableTypeForm.IsActive,
            AllowBelowMinGuests = TableTypeForm.AllowBelowMinGuests
        };

        var result = await _service.SaveTableTypeAsync(
            input,
            createFirstTable: TableTypeForm.CreateFirstTable);

        if (!result.Success)
            return Done(result, "Table type saved.");
        
        // "Remove photo" was pressed and no new file replaces it.
        if (TableTypeForm.RemoveTypePhoto &&
            TableTypeForm.Photo is not { Length: > 0 })
        {
            await _service.RemoveTableTypePhotoAsync(input.Id);
        }

        // Upload photo only when Admin selected a file.
        if (TableTypeForm.Photo is { Length: > 0 })
        {
            var photoResult = await _service.SetTableTypePhotoAsync(
                input.Id,
                TableTypeForm.Photo);
            if (!photoResult.Success)
            {
                var message =
                    "Table type saved, but photo upload failed: " +
                    photoResult.Error;

                if (IsAjaxRequest)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        message,
                        savedId = input.Id
                    })
                    {
                        StatusCode = 400
                    };
                }

                ErrorMessage = message;

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
            IconName = AreaForm.IconName,
            IsOccupied = AreaForm.IsOccupied
        };

        var result = await _service.SaveAreaAsync(area);

        return Done(result, "Dining area saved.");
    }

    public async Task<IActionResult> OnPostDeleteAreaAsync(int id) =>
        Done(await _service.DeleteAreaAsync(id), "Dining area deleted.");


    public async Task<IActionResult> OnPostSaveTableAsync()
    {
        KeepOnly(nameof(TableForm));

        // 1. Validate status
        if (!Enum.IsDefined(TableForm.Status))
        {
            ModelState.AddModelError(
                "TableForm.Status",
                "Choose a valid table status.");
        }

        // 2. Validate table type for new tables
        if (TableForm.Id == 0 &&
            (!TableForm.TableTypeDefinitionId.HasValue ||
             TableForm.TableTypeDefinitionId.Value <= 0))
        {
            ModelState.AddModelError(
                "TableForm.TableTypeDefinitionId",
                "Please select a table type.");
        }

        if (!ModelState.IsValid)
            return InvalidInput();

        // 3. Load existing table to compare changes
        RestaurantTable? oldTable = null;

        if (TableForm.Id != 0)
        {
            oldTable = await _db.RestaurantTables
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == TableForm.Id);

            if (oldTable == null)
            {
                ModelState.AddModelError(
                    string.Empty, "Table not found.");
                return InvalidInput();
            }
        }

        // 4. Use the existing table's type when editing
        int? typeId = oldTable == null
            ? TableForm.TableTypeDefinitionId
            : oldTable.TableTypeDefinitionId;

        if (!typeId.HasValue)
        {
            ModelState.AddModelError(
                "TableForm.TableTypeDefinitionId",
                "Please select a valid table type.");
            return InvalidInput();
        }

        var type = await _db.TableTypeDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == typeId.Value);

        if (type == null || string.IsNullOrWhiteSpace(type.Code))
        {
            ModelState.AddModelError(
                "TableForm.TableTypeDefinitionId",
                "Table type not found or has no code.");
            return InvalidInput();
        }

        // 5. Compare table number and status
        string submittedNumber = TableForm.TableNumber?.Trim() ?? "";

        // The service accepts both "008" and "CS008".
        string suffix = submittedNumber.StartsWith(
            type.Code, StringComparison.OrdinalIgnoreCase)
            ? submittedNumber.Substring(type.Code.Length)
            : submittedNumber;

        string fullNumber;

        try
        {
            fullNumber = TableNumberHelper.Build(
                type.Code, suffix, type.NumberPadding);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(
                "TableForm.TableNumber", ex.Message);
            return InvalidInput();
        }

        bool numberChanged = oldTable != null &&
            !string.Equals(
                oldTable.TableNumber,
                fullNumber,
                StringComparison.OrdinalIgnoreCase);

        bool statusChanged = oldTable != null &&
            oldTable.Status != TableForm.Status;

        // 6. No changes
        if (oldTable != null &&
            !numberChanged &&
            !statusChanged)
        {
            return Done(
                ReservationResult.Ok(),
                "No changes were made.");
        }

        // 7. Save only fields managed by the table.
        // Shared settings come from the table type in the service.
        var result = await _service.SaveTableAsync(
            new RestaurantTable
            {
                Id = TableForm.Id,
                TableNumber = fullNumber,
                TableTypeDefinitionId = typeId,
                Status = TableForm.Status
            });

        // 8. Give an accurate success message
        string message;

        if (oldTable == null)
        {
            message = $"Table {fullNumber} created successfully.";
        }
        else if (numberChanged && statusChanged)
        {
            message = "Table number and status updated successfully.";
        }
        else if (numberChanged)
        {
            message = "Table number updated successfully.";
        }
        else
        {
            message = "Table status updated successfully.";
        }

        return Done(result, message);
    }


    public async Task<IActionResult> OnPostDeleteTableAsync(int id) =>
        Done(await _service.DeleteTableAsync(id), "Table deleted.");
    public async Task<IActionResult> OnPostDeleteAllTablesAsync(int typeId)
    {
        var result = await _service.DeleteAllTablesAsync(typeId);

        if (!result.Success)
            return Done(RROMS.Services.ReservationResult.Fail(result.Error!), "");

        var message = result.Kept == 0
            ? $"Deleted {result.Deleted} table(s)."
            : $"Deleted {result.Deleted} table(s). {result.Kept} table(s) with booking records were kept.";

        return Done(RROMS.Services.ReservationResult.Ok(), message);
    }

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
    // Add or edit a special date.
    public async Task<IActionResult> OnPostSaveSpecialDateAsync()
    {
        KeepOnly(nameof(SpecialDateInput));

        if (!ModelState.IsValid)
            return InvalidInput();

        return Done(
            await _service.SaveSpecialDateAsync(SpecialDateInput),
            "Special date saved successfully.");
    }

    // Delete a special date.
    public async Task<IActionResult> OnPostDeleteSpecialDateAsync()
    {
        KeepOnly(nameof(DeleteSpecialDateId));

        return Done(
            await _service.DeleteSpecialDateAsync(DeleteSpecialDateId),
            "Special date deleted successfully.");
    }

    // The three forms share one page, so ignore validation errors that belong to the other forms.
    private void KeepOnly(string prefix)
    {
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith(prefix)).ToList()) ModelState.Remove(key);
    }

    private bool IsAjaxRequest =>
    Request.Headers["X-Requested-With"].ToString() == "XMLHttpRequest";

    private IActionResult InvalidInput()
    {
        var message = string.Join(" ",
            ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                    ? "Please check the values entered."
                    : e.ErrorMessage));

        if (IsAjaxRequest)
        {
            return new JsonResult(new
            {
                success = false,
                message
            })
            {
                StatusCode = 400
            };
        }

        ErrorMessage = message;

        return RedirectToPage();
    }

    private IActionResult Done(
        RROMS.Services.ReservationResult result,
        string success)
    {
        if (IsAjaxRequest)
        {
            if (result.Success)
            {
                SuccessMessage = success;
            }

            return new JsonResult(new
            {
                success = result.Success,
                message = result.Success ? success : result.Error
            })
            {
                StatusCode = result.Success ? 200 : 400
            };
        }

        if (result.Success)
        {
            SuccessMessage = success;
        }
        else
        {
            ErrorMessage = result.Error;
        }

        return RedirectToPage();
    }
}
