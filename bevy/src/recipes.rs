// Content-only recipe calculator (Unity PhoneRecipePage): whole batches, explicit spare output, path-local expansion and cycles.
use std::collections::{BTreeMap, HashSet};
use crate::goods::RecipeDefinition;

pub fn per_batch(recipe: &RecipeDefinition) -> i64 { if recipe.is_sale() { 1 } else { recipe.output_quantity.max(1) as i64 } }
pub fn batches(need: i64, output: i64) -> i64 { need / output + i64::from(need % output != 0) }
pub fn batch_text(need: i64, output: i64) -> String {
    let count = batches(need, output);
    format!("{} of {output} ({} spare)", plural(count, "batch", "batches"), count.saturating_mul(output).saturating_sub(need))
}
pub fn plural(count: i64, one: &str, many: &str) -> String { format!("{count} {}", if count == 1 { one } else { many }) }

pub struct Ingredient {
    pub path: String, pub item: String, pub need: i64, pub depth: usize,
    pub maker: Option<String>, pub cycle: bool, pub expanded: bool, pub batches: i64, pub spare: i64,
}

pub fn ingredients(recipes: &BTreeMap<String, RecipeDefinition>, recipe: &RecipeDefinition, quantity: i64, expanded: &HashSet<String>) -> Vec<Ingredient> {
    let mut makers = BTreeMap::new();
    for r in recipes.values().filter(|r| !r.is_sale() && !r.output_item_id.is_empty()) { makers.entry(r.output_item_id.clone()).or_insert(r); }
    let mut ancestors = HashSet::new();
    if !recipe.is_sale() { ancestors.insert(recipe.output_item_id.clone()); }
    let mut result = vec![];
    add(recipe, batches(quantity.clamp(1, 9999), per_batch(recipe)), "", 0, &ancestors, &makers, expanded, &mut result);
    result
}

fn add(recipe: &RecipeDefinition, count: i64, path: &str, depth: usize, ancestors: &HashSet<String>, makers: &BTreeMap<String, &RecipeDefinition>, expanded: &HashSet<String>, result: &mut Vec<Ingredient>) {
    for (i, input) in recipe.inputs.iter().enumerate() {
        if result.len() >= 2000 { return; }
        let path = format!("{path}/{i}");
        let need = count.saturating_mul(input.quantity.max(1) as i64);
        let maker = makers.get(&input.item_id).copied();
        let cycle = maker.is_some() && ancestors.contains(&input.item_id);
        let open = maker.is_some() && !cycle && expanded.contains(&path) && depth < 64;
        let count = maker.map_or(0, |r| batches(need, per_batch(r)));
        result.push(Ingredient { path: path.clone(), item: input.item_id.clone(), need, depth, maker: maker.map(|r| r.id.clone()),
            cycle, expanded: open, batches: count, spare: maker.map_or(0, |r| count.saturating_mul(per_batch(r)).saturating_sub(need)) });
        if open {
            let mut below = ancestors.clone(); below.insert(input.item_id.clone());
            add(maker.unwrap(), count, &path, depth + 1, &below, makers, expanded, result);
        }
    }
}
