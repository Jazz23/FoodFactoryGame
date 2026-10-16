// Company cash and its ledger (Unity GoodsWorld.Company.cs and .Ledger.cs). Cash is whole cents, never negative, and changes
// only on the server inside the mutation that moves it; every change adds one ledger entry in the same mutation, so opening +
// carried + kept entries == cash at every revision. Retention is bounded per company; older entries fold into the carried total.
use super::state::*;
use super::{CashNote, GoodsWorld, Result};

pub const LEDGER_ENTRIES_KEPT: usize = 50;
pub const LEDGER_SALE: &str = "sale";
pub const LEDGER_SUPPLIER_GOODS: &str = "supplier-goods";
pub const LEDGER_SUPPLIER_EQUIPMENT: &str = "supplier-equipment";
pub const LEDGER_TRUCK: &str = "truck";
pub const LEDGER_PROPERTY: &str = "property";
pub const LEDGER_FLOOR: &str = "floor";
pub const LEDGER_FURNISH: &str = "build-furnish";
pub const LEDGER_SELL_BACK: &str = "sell-back";
pub const LEDGER_SHELL: &str = "build-shell";
pub const LEDGER_ADJUSTMENT: &str = "adjustment";
pub const LEDGER_WAGE: &str = "wage";
pub const LEDGER_FARM_GOODS: &str = "farm-goods";
pub const LEDGER_FARM_DELIVERY: &str = "farm-delivery";
pub const LEDGER_FARM_REFUND: &str = "farm-refund";

/// The ledger rule for one company: opening + carried + kept entries == cash.
pub fn ledger_balances(state: &Snapshot, company: &Company) -> bool {
    let entries: i64 = state.ledger.iter().filter(|x| x.company_id == company.id).map(|x| x.cents).sum();
    company.opening_cents + company.ledger_carried_cents + entries == company.cash
}

/// The company's ledger entries, newest first.
pub fn ledger_of<'a>(state: &'a Snapshot, company_id: &str) -> Vec<&'a LedgerEntry> {
    state.ledger.iter().filter(|x| x.company_id == company_id).rev().collect()
}

impl GoodsWorld {
    /// Server-only: a company with its starting cash, which opens its ledger.
    pub fn bootstrap_company(&mut self, mut company: Company) -> Result<()> {
        let mut seen = std::collections::HashSet::new();
        if company.id.trim().is_empty() || company.cash < 0 || self.state.companies.iter().any(|x| x.id == company.id)
            || company.site_ids.iter().any(|s| !Self::site_exists(&self.state, s) || !seen.insert(s.clone()) || self.state.company_of_site(s).is_some())
        {
            return Err(format!("Invalid or duplicate company {}.", company.id));
        }
        company.opening_cents = company.cash;
        company.ledger_carried_cents = 0;
        company.ledger_next_number = 0;
        self.state.companies.push(company);
        self.invalidate_diners();
        self.state.revision += 1;
        Ok(())
    }

    /// Server-only: puts an existing, unowned site into a company's holdings.
    pub fn add_company_site(&mut self, company_id: &str, site_id: &str) -> Result<()> {
        if site_id == super::ROAD_SITE_ID || !Self::site_exists(&self.state, site_id) || self.state.company_of_site(site_id).is_some() {
            return Err("Unknown company, or a site that does not exist or is already owned.".into());
        }
        let company = self.state.companies.iter_mut().find(|x| x.id == company_id).ok_or("Unknown company.")?;
        company.site_ids.push(site_id.to_string());
        self.invalidate_diners();
        self.state.revision += 1;
        Ok(())
    }

    pub fn company_of_site(&self, site_id: &str) -> Option<String> { self.state.company_of_site(site_id).map(|x| x.id.clone()) }

    pub(crate) fn cash_of(&self, company_id: &str) -> i64 { self.state.companies.iter().find(|x| x.id == company_id).map_or(0, |x| x.cash) }

    /// Admin cash change (development tools and tests).
    pub fn adjust_cash(&mut self, company_id: &str, delta: i64) -> std::result::Result<(), &'static str> {
        if !self.state.companies.iter().any(|x| x.id == company_id) {
            return Err("unknown-company");
        }
        if delta == 0 || delta == i64::MIN {
            return Err("invalid-amount");
        }
        let note = CashNote::new(LEDGER_ADJUSTMENT, "");
        let ok = if delta > 0 { self.try_credit(company_id, delta, note) } else { self.try_debit(company_id, -delta, note) };
        if ok { Ok(()) } else { Err("insufficient-funds") }
    }

    pub(crate) fn try_credit(&mut self, company_id: &str, cents: i64, note: CashNote) -> bool {
        let Some(index) = self.state.companies.iter().position(|x| x.id == company_id) else { return false };
        if cents <= 0 {
            return false;
        }
        let Some(cash) = self.state.companies[index].cash.checked_add(cents) else { return false };
        self.state.companies[index].cash = cash;
        self.state.revision += 1;
        self.add_ledger_entry(index, cents, note);
        true
    }

    /// Refuses rather than letting the balance go below zero.
    pub(crate) fn try_debit(&mut self, company_id: &str, cents: i64, note: CashNote) -> bool {
        let Some(index) = self.state.companies.iter().position(|x| x.id == company_id) else { return false };
        if cents <= 0 || self.state.companies[index].cash < cents {
            return false;
        }
        self.state.companies[index].cash -= cents;
        self.state.revision += 1;
        self.add_ledger_entry(index, -cents, note);
        true
    }

    fn add_ledger_entry(&mut self, company_index: usize, cents: i64, note: CashNote) {
        let clock = note.clock_seconds.unwrap_or(self.state.clock_seconds);
        let revision = self.state.revision;
        let company = &mut self.state.companies[company_index];
        company.ledger_next_number += 1;
        let company_id = company.id.clone();
        self.state.ledger.push(LedgerEntry {
            id: format!("{}:{}", company_id, company.ledger_next_number), company_id: company_id.clone(), site_id: note.site_id, kind: note.kind.to_string(),
            cents, clock_seconds: clock, revision, request_id: note.request_id, equipment_id: note.equipment_id, customer_id: note.customer_id,
            offer_id: note.offer_id,
        });
        let mut kept = self.state.ledger.iter().filter(|x| x.company_id == company_id).count();
        while kept > LEDGER_ENTRIES_KEPT {
            let oldest = self.state.ledger.iter().position(|x| x.company_id == company_id).expect("kept entries exist");
            let entry = self.state.ledger.remove(oldest);
            self.state.companies[company_index].ledger_carried_cents += entry.cents;
            kept -= 1;
        }
    }
}
