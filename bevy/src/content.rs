// Game content exported from the Unity-authored assets by tools/export_content.py (items, recipes and menu, machines and
// furniture with their models and icons, supplier offers, truck models and restaurant styles). Content is registered with the
// server on every start and never saved; presentation reads the same records for names, icons and models.
use std::collections::BTreeMap;

use serde::Deserialize;

use crate::goods::{self, EquipmentOffer, FloorOffer, GoodsWorld, PurchaseOffer, RecipeDefinition, RecipeInput, TruckOffer};

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ItemContent {
    pub id: String,
    pub display_name: String,
    pub max_stack: i32,
    #[serde(default)]
    pub icon: String,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct IngredientEntry {
    item_id: String,
    quantity: i32,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct RecipeEntry {
    id: String,
    display_name: String,
    station_kind: String,
    duration_seconds: i64,
    inputs: Vec<IngredientEntry>,
    output_item_id: String,
    output_quantity: i32,
    output_spoil_after_seconds: i64,
    sale_cents: i64,
    tier: i32,
    cuisine: String,
}

/// Where a placed visual stands (presentation only).
#[derive(Clone, Copy, Debug, Deserialize, PartialEq, Eq, Default)]
#[serde(rename_all = "lowercase")]
pub enum Mount {
    #[default]
    Floor,
    Flush,
    Wall,
    Ceiling,
    Tabletop,
    Backed,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EquipmentContent {
    pub kind: String,
    pub width: i32,
    pub depth: i32,
    pub input_capacity: i32,
    pub output_capacity: i32,
    pub input_refrigerated: bool,
    pub output_refrigerated: bool,
    pub seats: i32,
    pub layer: String,
    pub ambience: i32,
    pub display_name: String,
    pub category: String,
    pub mount: Mount,
    pub mount_height: f32,
    pub hide_from_above: bool,
    pub opens_screen: bool,
    pub manual_power: bool,
    pub worker_bound: bool,
    pub model: String,
    pub icon: String,
}

impl EquipmentContent {
    /// Kind, footprint and capacities only: the server gives a bought piece its ID, site and holder.
    pub fn template(&self) -> goods::Equipment {
        goods::Equipment {
            kind: self.kind.clone(),
            state: goods::EquipmentState::Held,
            width: self.width,
            depth: self.depth,
            input_capacity: self.input_capacity,
            output_capacity: self.output_capacity,
            input_refrigerated: self.input_refrigerated,
            output_refrigerated: self.output_refrigerated,
            seats: self.seats,
            layer: self.layer.clone(),
            ambience: self.ambience,
            ..Default::default()
        }
    }

    pub fn placed(&self, id: &str, site: &str, x: i32, z: i32, rotation: i32) -> goods::Equipment {
        goods::Equipment {
            id: id.to_string(),
            site_id: site.to_string(),
            state: goods::EquipmentState::Placed,
            cell_x: x,
            cell_z: z,
            rotation,
            ..self.template()
        }
    }
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TruckModel {
    pub display_name: String,
    pub cargo_slots: i32,
    pub speed_metres_per_second: i32,
    pub load_units_per_second: i32,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct OfferEntry {
    id: String,
    price_cents: i64,
    #[serde(default)]
    item_id: String,
    #[serde(default)]
    quantity: i32,
    #[serde(default)]
    spoil_after_seconds: i64,
    #[serde(default)]
    equipment_kind: String,
    #[serde(default)]
    truck: Option<TruckModel>,
}

#[derive(Clone, Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct WallStyle {
    pub style: String,
    pub display_name: String,
    pub wall1m: String,
    pub wall2m: String,
    pub end: String,
}

#[derive(Clone, Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct DoorStyle {
    pub style: String,
    pub display_name: String,
    pub leaf: String,
}

#[derive(Clone, Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct WindowStyle {
    pub style: String,
    pub display_name: String,
    pub bay: String,
    pub window: String,
}

#[derive(Clone, Debug, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct Styles {
    pub walls: Vec<WallStyle>,
    pub doors: Vec<DoorStyle>,
    pub windows: Vec<WindowStyle>,
    pub single_doorway: String,
    pub double_doorway: String,
    pub single_frame: String,
    pub double_frame: String,
}

impl Styles {
    pub fn wall(&self, style: &str) -> Option<&WallStyle> { self.walls.iter().find(|w| w.style == style).or(self.walls.first()) }
    pub fn door(&self, style: &str) -> Option<&DoorStyle> { self.doors.iter().find(|w| w.style == style).or(self.doors.first()) }
    pub fn window(&self, style: &str) -> Option<&WindowStyle> { self.windows.iter().find(|w| w.style == style).or(self.windows.first()) }
    pub fn name_of(&self, style: &str) -> String {
        self.walls.iter().find(|x| x.style == style).map(|x| x.display_name.clone())
            .or_else(|| self.doors.iter().find(|x| x.style == style).map(|x| x.display_name.clone()))
            .or_else(|| self.windows.iter().find(|x| x.style == style).map(|x| x.display_name.clone()))
            .unwrap_or_else(|| style.to_string())
    }
}

#[derive(Deserialize)]
struct Package {
    format: u32,
    items: Vec<ItemContent>,
    recipes: Vec<RecipeEntry>,
    offers: Vec<OfferEntry>,
    equipment: Vec<EquipmentContent>,
    #[serde(default)]
    styles: Styles,
}

/// One supplier offer as presentation lists it.
#[derive(Clone, Debug)]
pub enum OfferKind {
    Goods(PurchaseOffer),
    Equipment(EquipmentOffer),
    Truck(TruckOffer),
}

#[derive(Clone, Debug)]
pub struct Offer {
    pub id: String,
    pub price_cents: i64,
    pub kind: OfferKind,
}

/// PROTOTYPE floor offer: 500 cents per interior cell, three storeys.
pub const FLOOR_OFFER: FloorOffer = FloorOffer { cents_per_cell: 500, max_floors: 3 };

/// Every piece of content, keyed for lookup; offers keep their authored order (supplier packs first).
#[derive(Clone, Debug, Default)]
pub struct GameContent {
    pub items: BTreeMap<String, ItemContent>,
    pub recipes: BTreeMap<String, RecipeDefinition>,
    pub recipe_names: BTreeMap<String, String>,
    pub equipment: BTreeMap<String, EquipmentContent>,
    pub offers: Vec<Offer>,
    pub styles: Styles,
}

impl GameContent {
    pub fn bundled() -> Result<Self, String> {
        let text = include_str!("../assets/content/catalog.json").trim_start_matches('\u{feff}');
        Self::parse(text)
    }

    pub fn parse(text: &str) -> Result<Self, String> {
        let package: Package = serde_json::from_str(text).map_err(|e| format!("content: {e}"))?;
        if package.format != 2 {
            return Err(format!("unsupported content format {}", package.format));
        }
        let mut content = GameContent { styles: package.styles, ..Default::default() };
        for item in package.items {
            content.items.insert(item.id.clone(), item);
        }
        for recipe in package.recipes {
            content.recipe_names.insert(recipe.id.clone(), recipe.display_name.clone());
            content.recipes.insert(recipe.id.clone(), RecipeDefinition {
                id: recipe.id,
                station_kind: recipe.station_kind,
                duration_seconds: recipe.duration_seconds,
                inputs: recipe.inputs.into_iter().map(|i| RecipeInput { item_id: i.item_id, quantity: i.quantity }).collect(),
                output_item_id: recipe.output_item_id,
                output_quantity: recipe.output_quantity,
                output_spoil_after_seconds: recipe.output_spoil_after_seconds,
                sale_cents: recipe.sale_cents,
                tier: recipe.tier,
                cuisine: recipe.cuisine,
            });
        }
        for equipment in package.equipment {
            content.equipment.insert(equipment.kind.clone(), equipment);
        }
        for offer in package.offers {
            let kind = if let Some(truck) = offer.truck {
                OfferKind::Truck(TruckOffer {
                    id: offer.id.clone(), price_cents: offer.price_cents, name: truck.display_name, cargo_slots: truck.cargo_slots,
                    speed_metres_per_second: truck.speed_metres_per_second, load_units_per_second: truck.load_units_per_second,
                })
            } else if !offer.equipment_kind.is_empty() {
                let Some(definition) = content.equipment.get(&offer.equipment_kind) else { continue };
                OfferKind::Equipment(EquipmentOffer { id: offer.id.clone(), price_cents: offer.price_cents, equipment: definition.template() })
            } else {
                OfferKind::Goods(PurchaseOffer {
                    id: offer.id.clone(), item_id: offer.item_id, quantity: offer.quantity.max(1), price_cents: offer.price_cents,
                    spoil_after_seconds: offer.spoil_after_seconds.max(1),
                })
            };
            content.offers.push(Offer { id: offer.id, price_cents: offer.price_cents, kind });
        }
        Ok(content)
    }

    pub fn item_name(&self, id: &str) -> String { self.items.get(id).map(|i| i.display_name.clone()).unwrap_or_else(|| id.to_string()) }

    pub fn equipment_name(&self, kind: &str) -> String {
        match kind {
            goods::COUNTER_KIND => "Register".to_string(),
            _ => self.equipment.get(kind).map(|e| e.display_name.clone()).unwrap_or_else(|| kind.to_string()),
        }
    }

    pub fn recipe_name(&self, id: &str) -> String { self.recipe_names.get(id).cloned().unwrap_or_else(|| id.to_string()) }

    pub fn max_stack(&self, item: &str) -> i32 { self.items.get(item).map_or(1, |i| i.max_stack.max(1)) }

    pub fn offer(&self, id: &str) -> Option<&Offer> { self.offers.iter().find(|o| o.id == id) }

    /// The supplier offer for packs of an item (farm orders price their packs from it).
    pub fn goods_offer_for(&self, item: &str) -> Option<&PurchaseOffer> {
        self.offers.iter().find_map(|o| match &o.kind {
            OfferKind::Goods(g) if g.item_id == item => Some(g),
            _ => None,
        })
    }

    pub fn truck_offers(&self) -> Vec<TruckOffer> {
        self.offers.iter().filter_map(|o| if let OfferKind::Truck(t) = &o.kind { Some(t.clone()) } else { None }).collect()
    }

    /// Registers everything the server needs: max stacks, recipes, power and worker kinds, every offer and the floor offer.
    pub fn register_with(&self, world: &mut GoodsWorld) -> Result<(), String> {
        for item in self.items.values() {
            world.register_item(&item.id, item.max_stack)?;
        }
        for recipe in self.recipes.values() {
            world.register_recipe(recipe.clone())?;
        }
        for definition in self.equipment.values() {
            if definition.manual_power {
                world.register_manual_power(&definition.kind);
            }
            if definition.worker_bound {
                world.register_worker_bound(&definition.kind)?;
            }
        }
        for offer in &self.offers {
            match &offer.kind {
                OfferKind::Goods(goods) => world.register_offer(goods.clone())?,
                OfferKind::Equipment(equipment) => world.register_equipment_offer(equipment.clone())?,
                OfferKind::Truck(truck) => world.register_truck_offer(truck.clone())?,
            }
        }
        world.register_floor_offer(FLOOR_OFFER)?;
        world.content.automatic_jobs = true;
        Ok(())
    }

    /// Machine buffer slot counts follow content, so saved machines created with older counts are brought up to date.
    pub fn apply_capacities(&self, world: &mut GoodsWorld) -> bool {
        let mut changed = false;
        for definition in self.equipment.values() {
            changed |= world.apply_equipment_capacities(&definition.kind, definition.input_capacity, definition.output_capacity);
        }
        changed
    }
}
