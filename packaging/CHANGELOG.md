# 0.1.4

- Removed every developer feature: automatic singleplayer start, test spawning of the Dealer,
  items, valuables and mass kit, forced Dealer stun, self-test firing and diagnostic dumps.
  Nothing in the mod spawns content outside the game's own rules any more.
- All five shop items (Dealer's Shotgun, Hacksaw, Handcuffs, Ultrasonic Jammer, Adrenaline) are now
  explicitly prepared for the shop: finalized status, physical item, small shop shelf, no hidden
  shop type and a per-shop limit. Previously the tools inherited the health-pack shelf, which could
  leave them out of the shop pool.
- The mod logs one line per shop visit listing which of its items reached the shop pool.
- Valuables (beer, burner phone, cigarettes, expired medicine) still spawn on maps through the
  regular valuable loot pools; no map is forced to contain them.
- Includes all 0.1.3 fixes: blank shells, handcuff targeting, phone speech.
