// Company cash lives in the world snapshot, beside the goods, so a sale or purchase commits goods and cash at one revision and
// recovery can never restore one without the other (decision 0012). Cash is whole cents, never negative, and changes only on
// the server. Membership is implied: a player granted a site the company owns acts for that company.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class GoodsCompany
    {
        public string Id;
        // Whole cents.
        public long Cash;
        public List<string> SiteIds = new();
        // Ledger (decision 0038, v20): the cash the company was created with, the sum of entries no longer kept (in a save
        // upgraded from v19, all cash before the ledger), and the number of its last entry.
        public long OpeningCents;
        public long LedgerCarriedCents;
        public long LedgerNextNumber;
    }

    public sealed partial class GoodsWorld
    {
        // Server-only: callers supply the durable ID and the sites it owns. Never expose this method to an RPC.
        public void Bootstrap(GoodsCompany company)
        {
            lock (_gate)
            {
                if (company is null || string.IsNullOrWhiteSpace(company.Id) || company.Cash < 0 || company.SiteIds is null
                    || _state.Companies.Any(x => x.Id == company.Id)
                    || company.SiteIds.Any(x => !SiteExists(_state, x))
                    || company.SiteIds.Distinct().Count() != company.SiteIds.Count
                    || company.SiteIds.Any(x => _state.Companies.Any(y => y.SiteIds.Contains(x))))
                    throw new ArgumentException("Invalid or duplicate company, or a site that does not exist or is already owned.");
                var added = JsonUtility.FromJson<GoodsCompany>(JsonUtility.ToJson(company));
                // Its starting cash opens the ledger; nothing has been recorded yet.
                added.OpeningCents = added.Cash;
                added.LedgerCarriedCents = 0;
                added.LedgerNextNumber = 0;
                _state.Companies.Add(added);
                InvalidateDiners();
                _state.Revision++;
            }
        }

        // The ID of the company that owns the site, or null.
        public string CompanyOfSite(string siteId)
        {
            lock (_gate) return CompanyOfSiteLocked(siteId);
        }

        // Every site of the company that owns this site, in its order; empty when no company owns it.
        public IReadOnlyList<string> CompanySiteIds(string siteId)
        {
            lock (_gate) return _state.Companies.FirstOrDefault(x => x.SiteIds.Contains(siteId))?.SiteIds.ToList() ?? new List<string>();
        }

        private string CompanyOfSiteLocked(string siteId) => _state.Companies.FirstOrDefault(x => x.SiteIds.Contains(siteId))?.Id;

        // Server-only cash change committed on its own, for dev/admin use and tests. Returns null when committed, otherwise
        // the reason nothing changed: unknown-company, invalid-amount, insufficient-funds or persistence-unavailable.
        // Sales and purchases do not use this: they call TryCredit/TryDebit inside their own commit, with their goods.
        internal string AdjustCashDurably(string companyId, long delta, string savePath)
        {
            lock (_gate)
            {
                if (_state.Companies.All(x => x.Id != companyId)) return "unknown-company";
                if (delta == 0 || delta == long.MinValue) return "invalid-amount";
                var note = new CashNote(LedgerAdjustment, "");
                return Durably(savePath,
                    () => (delta > 0 ? TryCredit(companyId, delta, note) : TryDebit(companyId, -delta, note)) ? null : "insufficient-funds",
                    () => "persistence-unavailable");
            }
        }

        // Call only under _gate, inside a commit. Overflow throws (checked) rather than wrapping. Adds the ledger entry in the same
        // mutation (decision 0038).
        private bool TryCredit(string companyId, long cents, CashNote note)
        {
            var company = _state.Companies.FirstOrDefault(x => x.Id == companyId);
            if (company is null || cents <= 0) return false;
            checked { company.Cash += cents; }
            _state.Revision++;
            AddLedgerEntry(company, cents, note);
            return true;
        }

        // Call only under _gate, inside a commit. Refuses rather than letting the balance go below zero. Adds the ledger entry in
        // the same mutation (decision 0038).
        private bool TryDebit(string companyId, long cents, CashNote note)
        {
            var company = _state.Companies.FirstOrDefault(x => x.Id == companyId);
            if (company is null || cents <= 0 || company.Cash < cents) return false;
            company.Cash -= cents;
            _state.Revision++;
            AddLedgerEntry(company, -cents, note);
            return true;
        }

        private static void ValidateCompanies(GoodsSnapshot state)
        {
            var owned = state.Companies.Where(x => x?.SiteIds is not null).SelectMany(x => x.SiteIds).ToList();
            if (state.Companies.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || x.Cash < 0 || x.SiteIds is null
                    || x.SiteIds.Any(y => !SiteExists(state, y)))
                || state.Companies.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || owned.Distinct().Count() != owned.Count
                // A sale in progress must have a company to pay when it completes.
                || state.Jobs.Where(x => x.IsSale).Any(x => !owned.Contains(state.Stations.First(y => y.Id == x.StationId).SiteId)))
                throw new InvalidOperationException("Goods snapshot violates company invariants.");
            ValidateLedger(state);
        }
    }
}
