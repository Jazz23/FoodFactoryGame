// The company cash ledger (decision 0038): every change to a company's cash adds one entry in the same mutation, so it commits,
// rolls back and replays with the cash itself; TryCredit and TryDebit are the only writers and every caller names the kind of
// change. Retention is bounded per company; entries that fall out are summed into the company's carried total, so for every
// company opening cash + carried total + the kept entries == cash at every revision (checked by Validate).
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class GoodsLedgerEntry
    {
        // "<company>:<number>", numbered per company from 1; never reused.
        public string Id;
        public string CompanyId;
        // The site whose action moved the cash (the paying site of a property purchase); empty for an admin adjustment.
        public string SiteId = "";
        public string Kind;
        // Signed whole cents: positive when the company received cash, negative when it paid.
        public long Cents;
        public long ClockSeconds;
        // The world revision of the cash change.
        public long Revision;
        // Related records where they exist, otherwise empty: the player request, the machine, truck or counter, the customer,
        // and the offer, lot or menu item.
        public string RequestId = "";
        public string EquipmentId = "";
        public string CustomerId = "";
        public string OfferId = "";
    }

    public sealed partial class GoodsWorld
    {
        // PROTOTYPE (decision 0038): ledger entries kept per company. Each one replicates with every baseline of the company's
        // sites, so this also bounds the payload.
        public const int LedgerEntriesKept = 50;

        public const string LedgerSale = "sale";
        public const string LedgerSupplierGoods = "supplier-goods";
        public const string LedgerSupplierEquipment = "supplier-equipment";
        public const string LedgerTruck = "truck";
        public const string LedgerProperty = "property";
        public const string LedgerFloor = "floor";
        public const string LedgerFurnish = "build-furnish";
        public const string LedgerSellBack = "sell-back";
        public const string LedgerShell = "build-shell";
        public const string LedgerAdjustment = "adjustment";

        // What a cash change is, for its ledger entry.
        private readonly struct CashNote
        {
            public readonly string Kind;
            public readonly string SiteId;
            public readonly string RequestId;
            public readonly string EquipmentId;
            public readonly string CustomerId;
            public readonly string OfferId;
            // The simulated moment of the change inside a clock step; null means the world clock.
            public readonly long? ClockSeconds;

            public CashNote(string kind, string siteId, string requestId = "", string equipmentId = "", string customerId = "", string offerId = "",
                long? clockSeconds = null)
            {
                Kind = kind;
                SiteId = siteId ?? "";
                RequestId = requestId ?? "";
                EquipmentId = equipmentId ?? "";
                CustomerId = customerId ?? "";
                OfferId = offerId ?? "";
                ClockSeconds = clockSeconds;
            }
        }

        // Call only under _gate, right after the cash change and its revision step.
        private void AddLedgerEntry(GoodsCompany company, long cents, CashNote note)
        {
            if (string.IsNullOrEmpty(note.Kind)) throw new ArgumentException("A cash change needs a ledger kind.");
            checked { company.LedgerNextNumber++; }
            _state.Ledger.Add(new GoodsLedgerEntry
            {
                Id = $"{company.Id}:{company.LedgerNextNumber}", CompanyId = company.Id, SiteId = note.SiteId, Kind = note.Kind, Cents = cents,
                ClockSeconds = note.ClockSeconds ?? _state.ClockSeconds, Revision = _state.Revision, RequestId = note.RequestId, EquipmentId = note.EquipmentId,
                CustomerId = note.CustomerId, OfferId = note.OfferId
            });
            // Oldest first: entries are appended in order, so the company's first entry is its oldest.
            var kept = 0;
            foreach (var entry in _state.Ledger) if (entry.CompanyId == company.Id) kept++;
            while (kept > LedgerEntriesKept)
            {
                var oldest = _state.Ledger.First(x => x.CompanyId == company.Id);
                checked { company.LedgerCarriedCents += oldest.Cents; }
                _state.Ledger.Remove(oldest);
                kept--;
            }
        }

        // The ledger rule for one company: opening + carried + kept entries == cash. Shared by Validate and tests.
        public static bool LedgerBalances(GoodsSnapshot state, GoodsCompany company)
        {
            long total;
            checked { total = company.OpeningCents + company.LedgerCarriedCents + state.Ledger.Where(x => x.CompanyId == company.Id).Sum(x => x.Cents); }
            return total == company.Cash;
        }

        private static void ValidateLedger(GoodsSnapshot state)
        {
            if (state.Ledger is null || state.Ledger.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Kind)
                    || x.Cents == 0 || x.SiteId is null || x.RequestId is null || x.EquipmentId is null || x.CustomerId is null || x.OfferId is null
                    || state.Companies.All(y => y.Id != x.CompanyId))
                || state.Ledger.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Companies.Any(x => x.LedgerNextNumber < 0 || state.Ledger.Count(y => y.CompanyId == x.Id) > LedgerEntriesKept
                    || !LedgerBalances(state, x)))
                throw new InvalidOperationException("Goods snapshot violates ledger invariants.");
        }

        // The company's ledger entries in a snapshot or view, newest first.
        public static IReadOnlyList<GoodsLedgerEntry> LedgerOf(GoodsSnapshot state, string companyId) =>
            state?.Ledger?.Where(x => x != null && x.CompanyId == companyId).Reverse().ToList() ?? new List<GoodsLedgerEntry>();
    }
}
