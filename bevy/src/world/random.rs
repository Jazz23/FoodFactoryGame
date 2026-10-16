// Explicit seeded PRNG for world generation (xoshiro256** seeded through SplitMix64), ported bit for bit from Unity's
// WorldRandom. Each generation phase forks its own stream by a fixed label so one phase cannot shift another's draws.

#[derive(Clone, Debug)]
pub struct WorldRandom {
    s0: u64,
    s1: u64,
    s2: u64,
    s3: u64,
}

impl WorldRandom {
    pub fn new(seed: u64) -> Self {
        let mut state = seed;
        let s0 = split_mix(&mut state);
        let s1 = split_mix(&mut state);
        let s2 = split_mix(&mut state);
        let s3 = split_mix(&mut state);
        Self { s0, s1, s2, s3 }
    }

    /// An independent stream for one named phase, fixed by the parent seed and the label only.
    pub fn fork(seed: u64, label: &str) -> Self {
        Self::new(mix(seed ^ fnv1a(label)))
    }

    pub fn next_u64(&mut self) -> u64 {
        let result = self.s1.wrapping_mul(5).rotate_left(7).wrapping_mul(9);
        let t = self.s1 << 17;
        self.s2 ^= self.s0;
        self.s3 ^= self.s1;
        self.s1 ^= self.s2;
        self.s0 ^= self.s3;
        self.s2 ^= t;
        self.s3 = self.s3.rotate_left(45);
        result
    }

    /// Uniform in [0, bound) without modulo bias.
    pub fn next(&mut self, bound: i32) -> i32 {
        assert!(bound > 0, "bound must be positive");
        let bound = bound as u64;
        let limit = u64::MAX - u64::MAX % bound;
        loop {
            let value = self.next_u64();
            if value < limit {
                return (value % bound) as i32;
            }
        }
    }

    /// Uniform in [min, max], both inclusive.
    pub fn range(&mut self, min: i32, max: i32) -> i32 {
        assert!(max >= min, "max below min");
        min + self.next(max - min + 1)
    }

    pub fn chance(&mut self, percent: i32) -> bool {
        self.next(100) < percent
    }

    /// Index chosen with probability proportional to its non-negative weight; None when every weight is zero.
    pub fn weighted(&mut self, weights: &[i32]) -> Option<usize> {
        let total: i32 = weights.iter().map(|w| (*w).max(0)).sum();
        if total == 0 {
            return None;
        }
        let mut roll = self.next(total);
        for (index, weight) in weights.iter().enumerate() {
            roll -= (*weight).max(0);
            if roll < 0 {
                return Some(index);
            }
        }
        Some(weights.len() - 1)
    }
}

/// The seed of retry attempt n: attempt 0 is the seed itself.
pub fn derive_seed(seed: u64, attempt: i32) -> u64 {
    if attempt == 0 { seed } else { mix(seed.wrapping_add(0x9E37_79B9_7F4A_7C15u64.wrapping_mul(attempt as u64))) }
}

/// FNV-1a over UTF-8: a stable string hash.
pub fn fnv1a(text: &str) -> u64 {
    let mut hash = 14_695_981_039_346_656_037u64;
    for byte in text.as_bytes() {
        hash ^= u64::from(*byte);
        hash = hash.wrapping_mul(1_099_511_628_211);
    }
    hash
}

pub fn mix(value: u64) -> u64 {
    let mut state = value;
    split_mix(&mut state)
}

fn split_mix(state: &mut u64) -> u64 {
    *state = state.wrapping_add(0x9E37_79B9_7F4A_7C15);
    let mut z = *state;
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
    z ^ (z >> 31)
}

/// Turns the seed a world's creator types into the generator's 64-bit seed: a whole number is used as is (negative numbers
/// wrap), other text hashes with FNV-1a, and a blank entry picks a random seed that is then recorded like a typed one.
pub fn seed_from_text(text: &str) -> u64 {
    let trimmed = text.trim();
    if !trimmed.starts_with('+') {
        if let Ok(value) = trimmed.parse::<u64>() {
            return value;
        }
    }
    if let Ok(value) = trimmed.parse::<i64>() {
        return value as u64;
    }
    fnv1a(trimmed)
}

pub fn resolve_seed(text: &str) -> (String, u64) {
    if !text.trim().is_empty() {
        return (text.trim().to_string(), seed_from_text(text));
    }
    let nanos = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_nanos() as u64;
    let seed = mix(nanos ^ u64::from(std::process::id()).rotate_left(32));
    (seed.to_string(), seed)
}
