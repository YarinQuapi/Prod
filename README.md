# Prod 3.0 — Admin Entity Inspector

Look at any entity or deployable and get ownership, authorization, lock, and status details for admins.

**Plugin page:** [umod.org/plugins/prod](https://umod.org/plugins/prod)

---

## Features

### Ownership & identity

- Player **Steam IDs and names** (online, sleeping, offline where cached)
- **Building block** owner (entity owner + tracked placer from plugin data)
- **Deployable owner** on containers, traps, vehicles, electrical, and more

### Building & base

- **Building blocks** — grade, HP, **stability %** (color-coded)
- **Tool cupboards** — location, authorized players, code/key locks
- **Doors** — open/closed state, code lock or key lock details

### Locks & access

- **Code locks** — code, whitelist players
- **Key locks** — placer, key code, locked state, players on server who can open (matching keys in inventory)
- Lock info on doors, boxes, TCs, ovens, mixing tables, vehicles, etc.

### Defences & traps

- **Auto turrets** — powered, owner, authorized players
- **SAM sites** — powered, scan ranges, ammo
- **Flame turrets** — fuel
- **Shotgun traps** — ammo
- **Bear traps / landmines** — owner, HP

### Storage & crafting

- **Storage containers** — owner, locks (boxes, lockers, etc.)
- **Mixing tables** — active state, quantity, time remaining, locks
- **Ovens / furnaces** — cooking state, locks
- **Weapon racks** — mounted weapon count
- **Vending machines** — shop name, broadcasting, sell order count, powered

### Electrical & automation

- **Generic IO entities** — powered, RF frequency, seismic range, conveyor mode/filters, branch power, counters, timers
- **HBHF sensors** — powered, authed/others detection, wired
- **Electric batteries** — charge %
- **Tesla coils** — powered
- **Fog machines** — powered, fuel
- **Industrial crafters** — powered, crafting state
- **CCTV cameras** — identifier, powered, viewers, static, yaw/pitch
- **Computer stations** — bookmarks, authorized players (standalone and on vehicles)

### Farming & misc

- **Chicken coops** — animals, incubating/hatching
- **Farm animals** — name, hunger/thirst/love/sunlight
- **Elevators** — floor, powered, busy
- **Sleeping bags** — name, deployer
- **Boom boxes** — playing, station, assigned by, cassette
- **Telephones** — voicemail count
- **Siege weapons / constructables** — owner, HP
- **Vehicles** — owner, locks, embedded computer station

### Other

- **Unified color-coded output** (titles, labels, values, errors)
- **Console or chat output** (configurable)
- **Fully configurable** messages and section titles
- **Building block placer tracking** via `Prod_BuildingData` (persisted)

---

## Permissions

| Permission   | Description                                      |
| ------------ | ------------------------------------------------ |
| `prod.admin` | Alternative to auth level — grants full `/prod` access |

Default access: **auth level 1+** (moderator) **or** `prod.admin`.

> **Note:** Passive mode (`prod.passive.use`) from older versions is **not implemented in 3.0A**. Full inspector access applies to all authorized users. Passive/redacted mode may return in a future update.

---

## Chat Commands

| Command | Description                                              |
| ------- | -------------------------------------------------------- |
| `/prod` | Inspect the entity you are looking at (configurable name) |

---

## Usage

1. Look directly at an entity or building piece.
2. Run `/prod`.
3. Output appears in **chat** or **F1 console**, depending on config.

Example output sections:

```
---- Prod ----
Type: wall (12345)
Owner: PlayerName (7656119...)

--- Building Grade ---
Grade: Metal
HP: 500/500
Stability: 85%
```

---

## Configuration

```json
{
  "Settings": {
    "Print to console instead of chat": false,
    "Required auth level": 1,
    "Chat command": "prod",
    "Show building grade": true,
    "Show building stability": true,
    "Maximum raycast distance": 10.0,
    "Show Rust team information": true,
    "Enable debug logging": false,
    "Permission (Auth alternative)": "prod.admin"
  },
  "Messages": {
    "Information added to console": "New information was printed to your console.",
    "No access": "You don't have permission to use this command.",
    "No target found": "You must look at an entity or building block!",
    "Tool Cupboard (No Auth)": "No players have access to this tool cupboard.",
    "Authorization (TC, Turrets, etc.)": "Authorized Players ({0})",
    "Computer Station (No Auth)": "No authorized players on this computer station.",
    "Computer Station (Error)": "Could not read computer station authorization list.",
    "No block owner": "No owner found for this building block.",
    "No code access": "No players have access to this code lock.",
    "No KeyLock found": "No key lock found.",
    "No KeyLock owner": "Lock placer unknown.",
    "No key access": "No players on the server currently have access to this key lock.",
    "No Codelock": "No code lock found.",
    "No vehicle owner": "No owner found for this vehicle.",
    "No container owner": "No owner found for this container.",
    "No deployable owner": "No owner found for this deployable.",
    "No electrical component owner": "No owner found for this electrical component.",
    "No Turret Auth": "No players are authorized on this turret.",
    "No entity info": "Entity detected but no specific ownership information available.",
    "No generic owner": "No owner found.",
    "Stability grounded": "Grounded (stability disabled)"
  },
  "Titles": {
    "Prod": "Prod",
    "Codelock": "Codelock",
    "Keylock": "Key Lock",
    "Toolcupboard": "Tool Cupboard",
    "Building Grade": "Building Grade",
    "Auto Turret": "Auto Turret",
    "Storage Container": "Storage Container",
    "Electrical Components": "Electrical / IO",
    "Vehicles": "Vehicle",
    "Computer Station": "Computer Station",
    "Sleeping Bag": "Sleeping Bag"
  }
}
```

### Settings explained

| Setting                            | Default      | Description                                      |
| ---------------------------------- | ------------ | ------------------------------------------------ |
| `Print to console instead of chat` | `false`      | Send output to F1 console instead of chat        |
| `Required auth level`              | `1`          | Minimum Rust auth level (0–2)                    |
| `Chat command`                     | `prod`       | Command name                                     |
| `Show building grade`              | `true`       | Show grade and HP on building blocks             |
| `Show building stability`          | `true`       | Show stability % on building blocks              |
| `Maximum raycast distance`         | `10`         | How far you can look at entities                 |
| `Enable debug logging`             | `false`      | Log plugin debug info to server console          |
| `Permission (Auth alternative)`    | `prod.admin` | Oxide permission as alternative to auth level    |

---

## Supported entities (overview)

**Dedicated handlers:** building blocks, tool cupboards, doors, sleeping bags, vending machines, SAM sites, auto/flame turrets, shotgun traps, bear traps, landmines, computer stations, CCTV, elevators, chicken coops, farm animals, mixing tables, ovens, weapon racks, siege weapons/constructables, industrial crafters, HBHF sensors, fog machines, electric batteries, Tesla coils, boom boxes, telephones, vehicles, storage containers, and electrical/IO entities (with subtype details).

**Generic fallback:** other deployables show type, owner, and locks where applicable.

---

## Data files

| File                                | Description                          |
| ----------------------------------- | ------------------------------------ |
| `oxide/data/Prod_BuildingData.json` | Tracks building block placer data    |

---

## Planned

- **Passive mode** — redacted output for non-sensitive admin use (e.g. hide codelock codes from enemy bases)
- Dedicated handlers for remaining deployables (planters, beehives, composters, shop fronts, card readers, etc.)
- Team/clan info display (config option exists but not yet active)

---

## Changelog (3.0A)

- Full rewrite with unified color-coded messaging
- Building block **stability** display
- **Key lock** support — placer, key code, access list
- **20+ new deployable types** with dedicated admin info
- Fixed entity dispatch order (specific types before generic handlers)
- Doors show full info + locks (not codelock-only)
- Updated config structure (`Settings`, `Messages`, `Titles`)
- Building block placer tracking persisted to data file