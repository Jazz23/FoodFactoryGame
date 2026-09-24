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
                    || company.SiteIds.Any(x => string.IsNullOrWhiteSpace(x) || _state.Locations.All(y => y.SiteId != x))
                    || company.SiteIds.Distinct().Count() != company.SiteIds.Count
                    || company.SiteIds.Any(x => _state.Companies.Any(y => y.SiteIds.Contains(x))))
                    throw new ArgumentException("Invalid or duplicate company, or a site that does not exist or is already owned.");
                _state.Companies.Add(JsonUtility.FromJson<GoodsCompany>(JsonUtility.ToJson(company)));
                _state.Revision++;
            }
        }

        // The ID of the company that owns the site, or null.
        public string CompanyOfSite(string siteId)
        {
            lock (_gate) return _state.Companies.FirstOrDefault(x => x.SiteIds.Contains(siteId))?.Id;
        }

        // Server-only cash change committed on its own, for dev/admin use and tests. Returns null when committed, otherwise
        // the reason nothing changed: unknown-company, invalid-amount, insufficient-funds or persistence-unavailable.
        // Sales and purchases do not use this: they call TryCredit/TryDebit inside their own commit, with their goods.
        internal string AdjustCashDurably(string companyId, long delta, string savePath)
        {
            lock (_gate)
            {
                if (_state.Companies.All(x => x.Id != companyId)) return "unknown-company";
                if (delta == 0 || delta == long.MinValue) return "invalid-amount";
                return Durably(savePath,
                    () => (delta > 0 ? TryCredit(companyId, delta) : TryDebit(companyId, -delta)) ? null : "insufficient-funds",
                    () => "persistence-unavailable");
            }
        }

        // Call only under _gate, inside a commit. Overflow throws (checked) rather than wrapping.
        private bool TryCredit(string companyId, long cents)
        {
            var company = _state.Companies.FirstOrDefault(x => x.Id == companyId);
            if (company is null || cents <= 0) return false;
            checked { company.Cash += cents; }
            _state.Revision++;
            return true;
        }

        // Call only under _gate, inside a commit. Refuses rather than letting the balance go below zero.
        private bool TryDebit(string companyId, long cents)
        {
            var company = _state.Companies.FirstOrDefault(x => x.Id == companyId);
            if (company is null || cents <= 0 || company.Cash < cents) return false;
            company.Cash -= cents;
            _state.Revision++;
            return true;
        }

        private static void ValidateCompanies(GoodsSnapshot state)
        {
            var owned = state.Companies.Where(x => x?.SiteIds is not null).SelectMany(x => x.SiteIds).ToList();
            if (state.Companies.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || x.Cash < 0 || x.SiteIds is null
                    || x.SiteIds.Any(y => string.IsNullOrWhiteSpace(y) || state.Locations.All(z => z.SiteId != y)))
                || state.Companies.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || owned.Distinct().Count() != owned.Count)
                throw new InvalidOperationException("Goods snapshot violates company invariants.");
        }
    }
}
