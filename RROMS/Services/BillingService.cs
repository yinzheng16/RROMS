using Microsoft.EntityFrameworkCore;
using RROMS.Models;
using RROMS.Repositories;
using RROMS.Services.Interfaces;

namespace RROMS.Services
{
    public class BillingService : IBillingService
    {
        private readonly IRepository<Order> _orders;
        private readonly IRepository<Payment> _payments;
        private readonly IRepository<Bill> _bills;

        // Member 2's DiningSession (read only here).
        private readonly IRepository<DiningSession> _sessions;

        private readonly IBillingTransactionRepository _billingTransaction;

        // US32: Kitchen statuses that trigger the confirmation warning.
        private static readonly OrderStatus[] KitchenStatuses =
        {
            OrderStatus.Pending,
            OrderStatus.Preparing,
            OrderStatus.ReadyToServe
        };

        public BillingService(
            IRepository<Order> orders,
            IRepository<Payment> payments,
            IRepository<Bill> bills,
            IRepository<DiningSession> sessions,
            IBillingTransactionRepository billingTransaction)
        {
            _orders = orders;
            _payments = payments;
            _bills = bills;
            _sessions = sessions;
            _billingTransaction = billingTransaction;
        }


        // US32: Create one Bill for one takeaway Order.
        // Use the amounts saved in the Order at checkout.
        // Never recalculate tax or service charge.
        public async Task<Bill> CreateTakeawayBillAsync(int orderId)
        {
            ValidateOrderId(orderId);

            // US32: Lock this Order before reading or creating its Bill.
            return await _billingTransaction.ExecuteForTakeawayOrderAsync(
                orderId,
                async () =>
                {
                    // Re-read the Order after acquiring the database lock.
                    // AsNoTracking prevents reuse of an older tracked Order.
                    var order = await _orders.Query()
                        .AsNoTracking()
                        .Include(o => o.Items)
                        .Include(o => o.Bill)
                        .FirstOrDefaultAsync(o => o.Id == orderId);

                    if (order == null)
                        throw new InvalidOperationException(
                            "Order does not exist.");

                    if (order.OrderType != OrderType.Takeaway)
                        throw new InvalidOperationException(
                            "This method only supports takeaway orders.");

                    if (order.Status == OrderStatus.Cancelled)
                        throw new InvalidOperationException(
                            "Cannot bill a cancelled order.");

                    ValidateOrderItems(order);

                    if (order.Subtotal <= 0 ||
                        order.TotalAmount <= 0 ||
                        order.TaxAmount < 0 ||
                        order.ServiceCharge < 0)
                    {
                        throw new InvalidOperationException(
                            "Order does not have valid saved amounts.");
                    }

                    if (order.TotalAmount !=
                        order.Subtotal + order.TaxAmount + order.ServiceCharge)
                    {
                        throw new InvalidOperationException(
                            "Saved order amounts are inconsistent.");
                    }

                    // Reuse an existing Bill instead of creating another.
                    if (order.BillId.HasValue)
                    {
                        if (order.Bill == null)
                            throw new InvalidOperationException(
                                "Order references a Bill that could not be loaded.");

                        return order.Bill;
                    }

                    bool hasActiveLegacyPayment = await _payments.Query()
                        .AsNoTracking()
                        .AnyAsync(p =>
                            p.OrderId == orderId &&
                            (p.Status == PaymentStatus.Pending ||
                             p.Status == PaymentStatus.Paid));

                    if (hasActiveLegacyPayment)
                        throw new InvalidOperationException(
                            "Resolve the existing payment before creating a Bill.");

                    var bill = new Bill
                    {
                        DiningSessionId = null,
                        Subtotal = order.Subtotal,
                        TaxAmount = order.TaxAmount,
                        ServiceCharge = order.ServiceCharge,
                        TotalAmount = order.TotalAmount,
                        Status = BillStatus.Draft
                    };

                    // Save the Bill first so it gets an Id.
                    // This runs inside the same transaction and lock as the
                    // link below, so if the link fails the Bill is rolled back.
                    await _bills.AddAsync(bill);
                    await _bills.SaveChangesAsync();

                    int billId = bill.Id;
                    decimal subtotal = bill.Subtotal;
                    decimal taxAmount = bill.TaxAmount;
                    decimal serviceCharge = bill.ServiceCharge;
                    decimal totalAmount = bill.TotalAmount;

                    // US32: Conditional update (compare-and-set).
                    // Link the Order only if it is still a takeaway order,
                    // not cancelled, not billed yet, and its saved amounts
                    // are exactly the amounts copied into the Bill.
                    // Same pattern as Member 3's AcceptStaffOrderAsync, so
                    // other modules never get a concurrency exception.
                    int linked = await _orders.Query()
                        .Where(o =>
                            o.Id == orderId &&
                            o.OrderType == OrderType.Takeaway &&
                            o.BillId == null &&
                            o.Status != OrderStatus.Cancelled &&
                            o.Subtotal == subtotal &&
                            o.TaxAmount == taxAmount &&
                            o.ServiceCharge == serviceCharge &&
                            o.TotalAmount == totalAmount)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(o => o.BillId, (int?)billId));

                    // 0 rows = the Order changed after it was read
                    // (cancelled, amounts updated or billed elsewhere).
                    // Throwing here skips the commit, so the new Bill
                    // is rolled back together with the failed link.
                    if (linked != 1)
                    {
                        throw new InvalidOperationException(
                            "The order changed while the bill was being created. " +
                            "Please refresh and try again.");
                    }

                    return bill;
                });
        }



        // US32: Confirm a Draft Bill (used by takeaway bills created by
        // CreateTakeawayBillAsync). Dine-in session bills will use a
        // separate session confirmation method after Member 2's
        // DiningSession (PR #96) is merged.
        public async Task<Bill> ConfirmBillAsync(
            int billId,
            string staffUserId)
        {
            if (billId <= 0)
                throw new ArgumentException(
                    "Invalid bill ID.", nameof(billId));

            if (string.IsNullOrWhiteSpace(staffUserId))
                throw new ArgumentException(
                    "Staff user ID is required.", nameof(staffUserId));

            // Read-only copy for validation. The status change below is a
            // conditional update, so nothing is saved through this entity.
            var bill = await _bills.Query()
                .AsNoTracking()
                .Include(b => b.Orders)
                .FirstOrDefaultAsync(b => b.Id == billId);

            if (bill == null)
                throw new InvalidOperationException(
                    "Bill does not exist.");

            if (bill.Status != BillStatus.Draft)
                throw new InvalidOperationException(
                    "Only draft bills can be confirmed.");

            // US32: A Bill must contain a valid positive total.
            if (bill.Subtotal <= 0 ||
                bill.TaxAmount < 0 ||
                bill.ServiceCharge < 0 ||
                bill.TotalAmount <= 0)
            {
                throw new InvalidOperationException(
                    "Cannot confirm a Bill with invalid amounts.");
            }

            if (bill.Orders.Any(o =>
                o.Status == OrderStatus.Cancelled))
            {
                throw new InvalidOperationException(
                    "Bill contains a cancelled order.");
            }

            if (bill.Orders.Any(o =>
                o.Subtotal < 0 ||
                o.TaxAmount < 0 ||
                o.ServiceCharge < 0 ||
                o.TotalAmount <= 0 ||
                o.TotalAmount !=
                    o.Subtotal + o.TaxAmount + o.ServiceCharge))
            {
                throw new InvalidOperationException(
                    "Bill contains invalid saved order amounts.");
            }

            // US32: Aggregate existing saved amounts only.
            // Never recalculate SST or service charge.
            decimal subtotal = bill.Orders.Sum(o => o.Subtotal);
            decimal taxAmount = bill.Orders.Sum(o => o.TaxAmount);
            decimal serviceCharge = bill.Orders.Sum(o => o.ServiceCharge);
            decimal totalAmount = bill.Orders.Sum(o => o.TotalAmount);

            if (bill.Subtotal != subtotal ||
                bill.TaxAmount != taxAmount ||
                bill.ServiceCharge != serviceCharge ||
                bill.TotalAmount != totalAmount)
            {
                throw new InvalidOperationException(
                    "Bill totals do not match saved order amounts.");
            }

            bool hasActivePayment = await _payments.Query()
                .AsNoTracking()
                .AnyAsync(p =>
                    p.BillId == billId &&
                    (p.Status == PaymentStatus.Pending ||
                     p.Status == PaymentStatus.Paid));

            if (hasActivePayment)
                throw new InvalidOperationException(
                    "Cannot confirm a bill with an existing pending or completed payment.");

            DateTime confirmedAt = DateTime.Now;
            string confirmedBy = staffUserId.Trim();

            // US32: Conditional update (compare-and-set).
            // Only one request can move this Bill from Draft to Confirmed.
            // A double click or a second staff member gets a clear message
            // instead of silently overwriting ConfirmedAt / ConfirmedByUserId.
            int confirmed = await _bills.Query()
                .Where(b => b.Id == billId && b.Status == BillStatus.Draft)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(b => b.Status, BillStatus.Confirmed)
                    .SetProperty(b => b.ConfirmedAt, (DateTime?)confirmedAt)
                    .SetProperty(b => b.ConfirmedByUserId, confirmedBy));

            if (confirmed != 1)
            {
                throw new InvalidOperationException(
                    "This bill has already been confirmed. Please refresh the page.");
            }

            // Return the confirmed values to the caller.
            bill.Status = BillStatus.Confirmed;
            bill.ConfirmedAt = confirmedAt;
            bill.ConfirmedByUserId = confirmedBy;

            return bill;
        }


        // US32: Retrieve the combined bill for a dining session.
        public async Task<Bill?> GetBillForSessionAsync(int sessionId)
        {
            if (sessionId <= 0)
                throw new ArgumentException("Invalid session ID.");

            return await _bills.Query()
                .AsNoTracking()
                .SingleOrDefaultAsync(b =>
                    b.DiningSessionId == sessionId);
        }

        // US32: One bill (status and totals only). Null if not found.
        public async Task<Bill?> GetBillByIdAsync(int billId)
        {
            if (billId <= 0)
                return null;

            return await _bills.Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == billId);
        }

        // US32: Billing facts about one order, found by its order number.
        public async Task<OrderBillingInfo?> GetOrderBillingInfoAsync(
            string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                throw new ArgumentException("Order number is required.");

            string normalizedOrderNo = orderNo.Trim();

            return await _orders.Query()
                .AsNoTracking()
                .Where(o => o.OrderNo == normalizedOrderNo)
                .Select(o => new OrderBillingInfo(
                    o.Id,
                    o.OrderNo,
                    o.OrderType,
                    o.Status,
                    o.DiningSessionId,
                    o.BillId))
                .SingleOrDefaultAsync();
        }

        // US32 (staff billing list, dine-in tab): open dining sessions, plus
        // closed sessions whose bill was paid today. A few small read-only
        // queries, no per-row lookups. Older history belongs to US40.
        public async Task<IReadOnlyList<SessionBillingRow>>
            GetSessionBillingRowsAsync()
        {
            var (today, tomorrow) = TodayRange();

            // Sessions whose bill was paid today (they may be closed already).
            var paidTodaySessionIds = await _payments.Query()
                .AsNoTracking()
                .Where(p =>
                    p.Status == PaymentStatus.Paid &&
                    p.BillId != null &&
                    p.PaidAt >= today &&
                    p.PaidAt < tomorrow &&
                    p.Bill!.DiningSessionId != null)
                .Select(p => p.Bill!.DiningSessionId!.Value)
                .Distinct()
                .ToListAsync();

            var sessions = await _sessions.Query()
                .AsNoTracking()
                .Where(s =>
                    s.Status == DiningSessionStatus.Open ||
                    paidTodaySessionIds.Contains(s.Id))
                .Select(s => new
                {
                    s.Id,
                    s.Table.TableNumber,
                    s.OpenedAt,
                    s.Status,
                    ReservationRef = s.Reservation != null
                        ? s.Reservation.ReferenceNo
                        : null,
                    CustomerName = s.Customer != null
                        ? s.Customer.FullName
                        : null
                })
                .ToListAsync();

            if (sessions.Count == 0)
                return Array.Empty<SessionBillingRow>();

            var sessionIds = sessions.Select(s => s.Id).ToList();

            // Non-cancelled orders of these sessions (only the fields needed).
            var orders = await _orders.Query()
                .AsNoTracking()
                .Where(o =>
                    o.DiningSessionId != null &&
                    sessionIds.Contains(o.DiningSessionId.Value) &&
                    o.Status != OrderStatus.Cancelled)
                .Select(o => new
                {
                    SessionId = o.DiningSessionId!.Value,
                    o.Status,
                    o.TotalAmount
                })
                .ToListAsync();

            var bills = await _bills.Query()
                .AsNoTracking()
                .Where(b =>
                    b.DiningSessionId != null &&
                    sessionIds.Contains(b.DiningSessionId.Value))
                .Select(b => new
                {
                    SessionId = b.DiningSessionId!.Value,
                    b.Id,
                    b.Status,
                    b.TotalAmount
                })
                .ToListAsync();

            var paidAtByBill = await GetPaidAtByBillAsync(
                bills.Where(b => b.Status == BillStatus.Paid).Select(b => b.Id));

            var ordersBySession = orders.ToLookup(o => o.SessionId);
            var billBySession = bills.ToDictionary(b => b.SessionId);

            return sessions
                .Select(s =>
                {
                    var sessionOrders = ordersBySession[s.Id].ToList();
                    billBySession.TryGetValue(s.Id, out var bill);

                    DateTime? paidAt = null;
                    if (bill != null && paidAtByBill.TryGetValue(bill.Id, out var at))
                        paidAt = at;

                    return new SessionBillingRow(
                        s.Id,
                        s.TableNumber,
                        s.OpenedAt,
                        s.ReservationRef,
                        string.IsNullOrWhiteSpace(s.CustomerName)
                            ? null
                            : s.CustomerName,
                        sessionOrders.Count,
                        sessionOrders.Count(o =>
                            KitchenStatuses.Contains(o.Status)),
                        bill?.TotalAmount
                            ?? sessionOrders.Sum(o => o.TotalAmount),
                        bill?.Id,
                        bill?.Status,
                        s.Status == DiningSessionStatus.Closed,
                        paidAt);
                })
                // Open tables first (by table number), then today's closed ones.
                .OrderBy(r => r.IsClosed)
                .ThenBy(r => r.TableNumber)
                .ToList();
        }

        // US32 (staff billing list, takeaway tab): takeaway orders still
        // waiting for payment (any day, oldest first), then takeaway bills
        // paid today (latest first). Cancelled orders and older orders
        // already paid through a US31 order payment are left out.
        public async Task<IReadOnlyList<TakeawayBillingRow>>
            GetTakeawayBillingRowsAsync()
        {
            var (today, tomorrow) = TodayRange();

            // Takeaway bills paid today.
            var paidTodayBillIds = await _payments.Query()
                .AsNoTracking()
                .Where(p =>
                    p.Status == PaymentStatus.Paid &&
                    p.BillId != null &&
                    p.PaidAt >= today &&
                    p.PaidAt < tomorrow &&
                    p.Bill!.DiningSessionId == null)
                .Select(p => p.BillId!.Value)
                .Distinct()
                .ToListAsync();

            var rows = await _orders.Query()
                .AsNoTracking()
                .Where(o =>
                    o.OrderType == OrderType.Takeaway &&
                    o.Status != OrderStatus.Cancelled &&
                    (
                        // Not paid yet.
                        ((o.BillId == null || o.Bill!.Status != BillStatus.Paid) &&
                         !o.Payments.Any(p => p.Status == PaymentStatus.Paid))
                        ||
                        // Paid today.
                        (o.BillId != null && paidTodayBillIds.Contains(o.BillId.Value))
                    ))
                .Select(o => new
                {
                    o.Id,
                    o.OrderNo,
                    o.CreatedAt,
                    CustomerName = o.User.FullName,
                    o.Status,
                    o.TotalAmount,
                    o.BillId,
                    BillStatus = o.Bill != null
                        ? (BillStatus?)o.Bill.Status
                        : null
                })
                .ToListAsync();

            var paidAtByBill = await GetPaidAtByBillAsync(
                rows.Where(o => o.BillStatus == BillStatus.Paid && o.BillId != null)
                    .Select(o => o.BillId!.Value));

            return rows
                .Select(o => new TakeawayBillingRow(
                    o.Id,
                    o.OrderNo,
                    o.CreatedAt,
                    string.IsNullOrWhiteSpace(o.CustomerName)
                        ? null
                        : o.CustomerName,
                    o.Status,
                    o.TotalAmount,
                    o.BillId,
                    o.BillStatus,
                    o.BillId is int billId && paidAtByBill.TryGetValue(billId, out var at)
                        ? at
                        : null))
                // Waiting for payment first (oldest first), then paid today (latest first).
                .OrderBy(r => r.BillStatus == BillStatus.Paid)
                .ThenByDescending(r => r.PaidAt) // null for unpaid rows
                .ThenBy(r => r.CreatedAt)
                .ThenBy(r => r.OrderId)
                .ToList();
        }

        // US32: Where a bill belongs (dining session or takeaway order).
        public async Task<BillLocation?> FindBillLocationAsync(int billId)
        {
            if (billId <= 0)
                return null;

            return await _bills.Query()
                .AsNoTracking()
                .Where(b => b.Id == billId)
                .Select(b => new BillLocation(
                    b.Id,
                    b.DiningSessionId,
                    b.DiningSessionId == null
                        ? b.Orders
                            .OrderBy(o => o.Id)
                            .Select(o => o.OrderNo)
                            .FirstOrDefault()
                        : null))
                .SingleOrDefaultAsync();
        }

        // "Today" in local time, same as the rest of billing (DateTime.Now).
        private static (DateTime Today, DateTime Tomorrow) TodayRange()
        {
            DateTime today = DateTime.Today;
            return (today, today.AddDays(1));
        }

        // Latest Paid payment time for each of these bills.
        private async Task<Dictionary<int, DateTime>> GetPaidAtByBillAsync(
            IEnumerable<int> billIds)
        {
            var ids = billIds.Distinct().ToList();

            if (ids.Count == 0)
                return new Dictionary<int, DateTime>();

            var paid = await _payments.Query()
                .AsNoTracking()
                .Where(p =>
                    p.BillId != null &&
                    ids.Contains(p.BillId.Value) &&
                    p.Status == PaymentStatus.Paid &&
                    p.PaidAt != null)
                .Select(p => new { BillId = p.BillId!.Value, PaidAt = p.PaidAt!.Value })
                .ToListAsync();

            return paid
                .GroupBy(p => p.BillId)
                .ToDictionary(g => g.Key, g => g.Max(p => p.PaidAt));
        }

        // US32: Confirmed or paid bills lock the dining session.
        public async Task<bool> IsSessionLockedAsync(int sessionId)
        {
            if (sessionId <= 0)
                return false;

            return await _bills.Query()
                .AsNoTracking()
                .AnyAsync(b =>
                    b.DiningSessionId == sessionId &&
                    (b.Status == BillStatus.Confirmed ||
                     b.Status == BillStatus.Paid));
        }

        // US32 (for Member 2's CloseSessionAsync):
        // A session is financially settled when every non-cancelled order
        // in it is linked to a Paid bill, each of those bills still matches
        // the saved totals of its orders, and no payment is still Pending.
        // An order with no bill never counts as paid.
        // A session with no chargeable orders (e.g. guests left without
        // ordering) has nothing to pay, so it is settled.
        public async Task<bool> IsSessionFinanciallySettledAsync(int sessionId)
        {
            if (sessionId <= 0)
                return false;

            bool sessionExists = await _sessions.Query()
                .AsNoTracking()
                .AnyAsync(s => s.Id == sessionId);

            if (!sessionExists)
                return false;

            var orders = await _orders.Query()
                .AsNoTracking()
                .Where(o =>
                    o.DiningSessionId == sessionId &&
                    o.Status != OrderStatus.Cancelled)
                .Select(o => new
                {
                    o.BillId,
                    o.TotalAmount,
                    BillStatus = o.Bill != null
                        ? (BillStatus?)o.Bill.Status
                        : null,
                    BillTotal = o.Bill != null
                        ? (decimal?)o.Bill.TotalAmount
                        : null
                })
                .ToListAsync();

            if (orders.Count == 0)
                return true;

            // Every chargeable order must be on a Paid bill.
            if (orders.Any(o =>
                o.BillId == null ||
                o.BillStatus != BillStatus.Paid))
            {
                return false;
            }

            // Each bill total must still equal its orders' saved totals
            // (protects against a change after the bill was confirmed).
            bool totalsMatch = orders
                .GroupBy(o => o.BillId!.Value)
                .All(g => g.First().BillTotal == g.Sum(o => o.TotalAmount));

            if (!totalsMatch)
                return false;

            var billIds = orders
                .Select(o => o.BillId!.Value)
                .Distinct()
                .ToList();

            bool paymentInProgress = await _payments.Query()
                .AsNoTracking()
                .AnyAsync(p =>
                    p.BillId != null &&
                    billIds.Contains(p.BillId.Value) &&
                    p.Status == PaymentStatus.Pending);

            return !paymentInProgress;
        }


        // US32: Read-only preview of the combined bill for one session.
        // Totals = sum of the saved Order amounts. No recalculation.
        public async Task<SessionBillPreview> PreviewSessionBillAsync(
            int sessionId)
        {
            ValidateSessionId(sessionId);

            var session = await _sessions.Query()
                .AsNoTracking()
                .Include(s => s.Table)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
                throw new InvalidOperationException(
                    "Dining session does not exist.");

            var orders = await LoadChargeableSessionOrdersAsync(
                sessionId,
                includeItems: true);

            var existingBill = await _bills.Query()
                .AsNoTracking()
                .Where(b => b.DiningSessionId == sessionId)
                .Select(b => new { b.Id, b.Status })
                .SingleOrDefaultAsync();

            decimal subtotal = orders.Sum(o => o.Subtotal);
            decimal taxAmount = orders.Sum(o => o.TaxAmount);
            decimal serviceCharge = orders.Sum(o => o.ServiceCharge);
            decimal totalAmount = orders.Sum(o => o.TotalAmount);

            var (taxRate, serviceChargeRate) = DeriveDisplayRates(
                subtotal,
                taxAmount,
                serviceCharge);

            var lines = orders
                .Select(o => new SessionBillOrderLine(
                    o.Id,
                    o.OrderNo,
                    o.Status,
                    CreateItemSummaries(o),
                    o.Subtotal,
                    o.TaxAmount,
                    o.ServiceCharge,
                    o.TotalAmount))
                .ToList();

            return new SessionBillPreview(
                session.Id,
                session.TableId,
                session.Table?.TableNumber ?? string.Empty,
                session.Status,
                lines,
                subtotal,
                taxRate,
                taxAmount,
                serviceChargeRate,
                serviceCharge,
                totalAmount,
                GetOrdersInKitchen(orders),
                existingBill?.Id,
                existingBill?.Status,
                CreateOrderSnapshot(orders));
        }


        // US32: Staff confirm the combined bill for one dining session.
        // Agreed design: the session bill is created at confirmation
        // (no stored Draft), in one transaction holding the session lock:
        //   1. session must be Open and have no bill yet;
        //   2. orders must still match the preview (OrderSnapshot);
        //   3. orders still in the kitchen need acknowledgeKitchenWarning;
        //   4. create the Confirmed bill from the saved order amounts;
        //   5. link the orders with a conditional update;
        //   6. re-check totals and that no new order slipped in.
        // Any failure rolls back everything.
        public async Task<SessionBillConfirmResult> ConfirmSessionBillAsync(
            int sessionId,
            string staffUserId,
            string orderSnapshot,
            bool acknowledgeKitchenWarning)
        {
            ValidateSessionId(sessionId);

            if (string.IsNullOrWhiteSpace(staffUserId))
                throw new ArgumentException(
                    "Staff user ID is required.", nameof(staffUserId));

            if (string.IsNullOrWhiteSpace(orderSnapshot))
                throw new ArgumentException(
                    "Bill preview is missing. Please refresh the bill preview.",
                    nameof(orderSnapshot));

            string confirmedBy = staffUserId.Trim();

            return await _billingTransaction.ExecuteForSessionBillAsync(
                sessionId,
                async () =>
                {
                    // 1. Session must exist, be Open and have no bill yet.
                    var session = await _sessions.Query()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == sessionId);

                    if (session == null)
                        throw new InvalidOperationException(
                            "Dining session does not exist.");

                    if (session.Status != DiningSessionStatus.Open)
                        throw new InvalidOperationException(
                            "This dining session is already closed.");

                    bool billExists = await _bills.Query()
                        .AsNoTracking()
                        .AnyAsync(b => b.DiningSessionId == sessionId);

                    if (billExists)
                        throw new InvalidOperationException(
                            "This table's bill has already been confirmed. " +
                            "Please refresh the page.");

                    // 2. Orders must still match what staff previewed.
                    var orders = await LoadChargeableSessionOrdersAsync(
                        sessionId,
                        includeItems: false);

                    if (orders.Count == 0)
                        throw new InvalidOperationException(
                            "This dining session has no orders to bill.");

                    if (CreateOrderSnapshot(orders) != orderSnapshot)
                        throw new InvalidOperationException(
                            "The orders for this table have changed since the preview. " +
                            "Please refresh the bill preview and confirm again.");

                    if (orders.Any(o => o.BillId.HasValue))
                        throw new InvalidOperationException(
                            "Some orders in this session are already linked to a bill.");

                    // 3. Kitchen warning (US32 acceptance criteria: warn, not block).
                    var ordersInKitchen = GetOrdersInKitchen(orders);

                    if (ordersInKitchen.Count > 0 && !acknowledgeKitchenWarning)
                    {
                        return new SessionBillConfirmResult(
                            SessionBillConfirmStatus.KitchenWarningRequired,
                            null,
                            ordersInKitchen);
                    }

                    // 4. Create the Confirmed bill from saved amounts only.
                    var bill = new Bill
                    {
                        DiningSessionId = sessionId,
                        Subtotal = orders.Sum(o => o.Subtotal),
                        TaxAmount = orders.Sum(o => o.TaxAmount),
                        ServiceCharge = orders.Sum(o => o.ServiceCharge),
                        TotalAmount = orders.Sum(o => o.TotalAmount),
                        Status = BillStatus.Confirmed,
                        ConfirmedAt = DateTime.Now,
                        ConfirmedByUserId = confirmedBy
                    };

                    if (bill.Subtotal <= 0 || bill.TotalAmount <= 0)
                        throw new InvalidOperationException(
                            "Cannot confirm a bill with invalid amounts.");

                    await _bills.AddAsync(bill);
                    await _bills.SaveChangesAsync();

                    int billId = bill.Id;

                    var orderIds = orders
                        .Select(o => o.Id)
                        .ToList();

                    // 5. Conditional update (compare-and-set): link only
                    // orders that are still in this session, unbilled and
                    // not cancelled. Same pattern as Member 3's
                    // AcceptStaffOrderAsync; no concurrency exceptions
                    // for other modules.
                    int linked = await _orders.Query()
                        .Where(o =>
                            orderIds.Contains(o.Id) &&
                            o.DiningSessionId == sessionId &&
                            o.BillId == null &&
                            o.Status != OrderStatus.Cancelled)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(o => o.BillId, (int?)billId));

                    if (linked != orderIds.Count)
                        throw new InvalidOperationException(
                            "The orders for this table changed while the bill was being confirmed. " +
                            "Please refresh the bill preview and try again.");

                    // 6a. The linked orders' saved totals must still equal
                    // the bill (e.g. no order changed in between).
                    var linkedTotals = await _orders.Query()
                        .AsNoTracking()
                        .Where(o => o.BillId == billId)
                        .Select(o => o.TotalAmount)
                        .ToListAsync();

                    if (linkedTotals.Sum() != bill.TotalAmount)
                        throw new InvalidOperationException(
                            "Order amounts changed while the bill was being confirmed. " +
                            "Please refresh the bill preview and try again.");

                    // 6b. No new chargeable order may be left unbilled.
                    bool unbilledOrderExists = await _orders.Query()
                        .AsNoTracking()
                        .AnyAsync(o =>
                            o.DiningSessionId == sessionId &&
                            o.BillId == null &&
                            o.Status != OrderStatus.Cancelled);

                    if (unbilledOrderExists)
                        throw new InvalidOperationException(
                            "A new order was added to this table while the bill was being confirmed. " +
                            "Please refresh the bill preview and confirm again.");

                    return new SessionBillConfirmResult(
                        SessionBillConfirmStatus.Confirmed,
                        bill,
                        ordersInKitchen);
                });
        }

        public async Task<int?> FindOrderIdByOrderNoAsync(
            string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                throw new ArgumentException(
                    "Order number is required.");

            string normalizedOrderNo = orderNo.Trim();

            return await _orders.Query()
                .AsNoTracking()
                .Where(o => o.OrderNo == normalizedOrderNo)
                .Select(o => (int?)o.Id)
                .SingleOrDefaultAsync();
        }


        public async Task<bool> CanGenerateBillAsync(int orderId)
        {
            if (orderId <= 0)
                return false;

            var order = await _orders.Query()
                .AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null ||
                order.Status == OrderStatus.Cancelled ||
                order.BillId.HasValue)
                return false;

            if (!HasValidOrderItems(order))
                return false;

            bool hasActivePayment = await _payments.Query()
                .AsNoTracking()
                .AnyAsync(p =>
                    p.OrderId == orderId &&
                    (p.Status == PaymentStatus.Pending ||
                     p.Status == PaymentStatus.Paid));

            return !hasActivePayment;
        }


        // US32: Preview the amounts already saved in the Order.
        // Do not recalculate tax or service charge using current settings.
        // This method does not modify the database.
        public async Task<BillSummary> PreviewBillAsync(int orderId)
        {
            ValidateOrderId(orderId);

            var order = await _orders.Query()
                .AsNoTracking()
                .Include(o => o.Items)
                    .ThenInclude(item => item.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new InvalidOperationException(
                    "Order does not exist.");

            if (order.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException(
                    "Cannot preview a cancelled order.");

            ValidateOrderItems(order);

            if (order.TotalAmount <= 0)
                throw new InvalidOperationException(
                    "Order does not have a valid saved total.");

            // US32: Historical rates are not stored in Order.
            // Derive display percentages from saved amounts only.
            // Never use current restaurant settings for an existing order.
            var (taxRate, serviceChargeRate) = DeriveDisplayRates(order);

            return CreateSummary(
                order,
                taxRate,
                serviceChargeRate);
        }



        // US32: Read the amounts already saved in the Order.
        // Never recalculate historical bills using current settings.
        public async Task<BillSummary> GetBillAsync(int orderId)
        {
            ValidateOrderId(orderId);

            var order = await _orders.Query()
                .AsNoTracking()
                .Include(o => o.Items)
                    .ThenInclude(item => item.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new InvalidOperationException(
                    "Order does not exist.");

            if (!order.Items.Any())
                throw new InvalidOperationException(
                    "Order has no items. Cannot display a bill.");

            if (order.TotalAmount <= 0)
                throw new InvalidOperationException(
                    "Order does not have a valid saved total.");

            // Display-only percentages derived from saved amounts.
            // These are estimates, not stored historical tax rates.
            var (taxRate, serviceChargeRate) = DeriveDisplayRates(order);

            return CreateSummary(
                order,
                taxRate,
                serviceChargeRate);
        }

        // US32: Generate the legacy single-order bill summary.
        // Use amounts already saved when the Order was created.
        // Do not recalculate or overwrite Order amounts.
        //
        // This method does not create a Bill database entity.
        // Combined Bill creation is handled separately.
        public async Task<BillSummary> GenerateBillAsync(int orderId)
        {
            ValidateOrderId(orderId);

            var order = await _orders.Query()
                .AsNoTracking()
                .Include(o => o.Items)
                    .ThenInclude(item => item.MenuItem)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new InvalidOperationException(
                    "Order does not exist.");

            if (order.Status == OrderStatus.Cancelled)
                throw new InvalidOperationException(
                    "Cannot generate a bill for a cancelled order.");

            ValidateOrderItems(order);

            if (order.Subtotal <= 0 || order.TotalAmount <= 0)
                throw new InvalidOperationException(
                    "Order does not have valid saved amounts.");

            if (order.TotalAmount !=
                order.Subtotal + order.TaxAmount + order.ServiceCharge)
            {
                throw new InvalidOperationException(
                    "Saved order amounts are inconsistent.");
            }

            // Do not generate a new legacy bill for an order
            // already associated with a combined or takeaway Bill.
            if (order.BillId.HasValue)
                throw new InvalidOperationException(
                    "Order is already linked to a Bill.");

            bool hasActivePayment = await _payments.Query()
                .AsNoTracking()
                .AnyAsync(p =>
                    p.OrderId == orderId &&
                    (p.Status == PaymentStatus.Pending ||
                     p.Status == PaymentStatus.Paid));

            if (hasActivePayment)
                throw new InvalidOperationException(
                    "Order already has a pending or completed payment.");

            // Display-only percentages derived from saved amounts.
            // Do not use current restaurant settings.
            var (taxRate, serviceChargeRate) = DeriveDisplayRates(order);

            return CreateSummary(
                order,
                taxRate,
                serviceChargeRate);
        }


        private static void ValidateOrderId(int orderId)
        {
            if (orderId <= 0)
                throw new ArgumentException("Invalid order ID.");
        }

        private static bool HasValidOrderItems(Order order)
        {
            if (!order.Items.Any())
                return false;

            if (order.Items.Any(item =>
                item.Quantity <= 0 || item.UnitPrice < 0))
                return false;

            return order.Items.Sum(item =>
                item.Quantity * item.UnitPrice) > 0;
        }

        private static void ValidateOrderItems(Order order)
        {
            if (!order.Items.Any())
                throw new InvalidOperationException(
                    "Cannot generate a bill for an empty order.");

            if (order.Items.Any(item =>
                item.Quantity <= 0 || item.UnitPrice < 0))
                throw new InvalidOperationException(
                    "Order contains invalid item values.");
        }


        private static void ValidateSessionId(int sessionId)
        {
            if (sessionId <= 0)
                throw new ArgumentException("Invalid dining session ID.");
        }

        // US32: Non-cancelled orders of a session, oldest first.
        // Every order must have items and consistent saved amounts.
        private async Task<List<Order>> LoadChargeableSessionOrdersAsync(
            int sessionId,
            bool includeItems)
        {
            var query = _orders.Query()
                .AsNoTracking()
                .Where(o =>
                    o.DiningSessionId == sessionId &&
                    o.Status != OrderStatus.Cancelled);

            if (includeItems)
            {
                query = query
                    .Include(o => o.Items)
                        .ThenInclude(item => item.MenuItem);
            }

            var orders = await query
                .OrderBy(o => o.Id)
                .ToListAsync();

            foreach (var order in orders)
            {
                if (includeItems)
                    ValidateOrderItems(order);

                ValidateSavedAmounts(order);
            }

            return orders;
        }

        private static void ValidateSavedAmounts(Order order)
        {
            if (order.Subtotal <= 0 ||
                order.TaxAmount < 0 ||
                order.ServiceCharge < 0 ||
                order.TotalAmount <= 0 ||
                order.TotalAmount !=
                    order.Subtotal + order.TaxAmount + order.ServiceCharge)
            {
                throw new InvalidOperationException(
                    $"Order {order.OrderNo} does not have valid saved amounts.");
            }
        }

        private static IReadOnlyList<string> GetOrdersInKitchen(
            IEnumerable<Order> orders)
        {
            return orders
                .Where(o => KitchenStatuses.Contains(o.Status))
                .Select(o => o.OrderNo)
                .ToList();
        }

        // US32: Fingerprint of the orders shown in the preview.
        // Confirmation is refused if this changes (new order,
        // cancellation or changed amounts).
        private static string CreateOrderSnapshot(IEnumerable<Order> orders)
        {
            return string.Join("|", orders
                .OrderBy(o => o.Id)
                .Select(o => string.Join(":",
                    o.Id,
                    o.Subtotal.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    o.TaxAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    o.ServiceCharge.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    o.TotalAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
        }

        // US32 / US49: Display-only percentages derived from saved amounts.
        // Orders do not store their historical rates, so derive them here.
        // Team rule (leader, US49): service = subtotal x service%;
        //                           SST     = (subtotal + service) x tax%.
        // Example: RM100 dine-in, 10% / 6% -> service RM10.00, SST RM6.60.
        private static (decimal TaxRate, decimal ServiceChargeRate)
            DeriveDisplayRates(Order order)
        {
            return DeriveDisplayRates(
                order.Subtotal,
                order.TaxAmount,
                order.ServiceCharge);
        }

        // Same rule for combined totals (session bill preview).
        private static (decimal TaxRate, decimal ServiceChargeRate)
            DeriveDisplayRates(
                decimal subtotal,
                decimal taxAmount,
                decimal serviceCharge)
        {
            decimal serviceChargeRate = subtotal > 0
                ? Math.Round(serviceCharge / subtotal * 100m, 2)
                : 0m;

            decimal taxBase = subtotal + serviceCharge;

            decimal taxRate = taxBase > 0
                ? Math.Round(taxAmount / taxBase * 100m, 2)
                : 0m;

            return (taxRate, serviceChargeRate);
        }

        private static decimal RoundMoney(decimal amount)
        {
            return Math.Round(
                amount, 2, MidpointRounding.AwayFromZero);
        }

        private static IReadOnlyList<BillItemSummary> CreateItemSummaries(
            Order order)
        {
            return order.Items
                .OrderBy(item => item.Id)
                .Select(item => new BillItemSummary(
                    item.MenuItemId,
                    item.Quantity,
                    item.UnitPrice,
                    RoundMoney(
                        item.Quantity * item.UnitPrice),
                    item.MenuItem?.Name
                        ?? $"Menu Item #{item.MenuItemId}"))
                .ToList();
        }

        private static BillSummary CreateSummary(
            Order order,
            decimal taxRate,
            decimal serviceChargeRate)
        {
            var items = CreateItemSummaries(order);

            return new BillSummary(
                order.Id,
                order.OrderNo,
                items,
                order.Subtotal,
                taxRate,
                order.TaxAmount,
                serviceChargeRate,
                order.ServiceCharge,
                order.TotalAmount);
        }
    }
}