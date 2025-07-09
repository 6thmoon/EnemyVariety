## Introduction

Normally, when a wave of enemies spawn in they all share the same monster type. This modifies that procedure to reroll for a different monster and elite affix after each one. In addition, the director is more likely to choose a bigger enemy if enough credits remain. It also significantly reduces chance for a horde to be selected instead of a boss for the teleporter event.

The above behavior has been tuned to provide a cadence that should feel similar to usual. However, it may be more fast-paced or challenging than fighting uniform enemy groups. Furthermore, this can help reduce the amount of fodder enemies and elites, especially in the late game.

## Support

Credit to **Nuxlar** for the original idea, previously I contributed to the deprecated [**DirectorRework**](https://thunderstore.io/package/Nuxlar/DirectorRework/1.1.1) and will continue to maintain this as an alternative. Refer to the configuration file `BepInEx/local.enemy.variety.cfg` for customization. Note that it is also compatible with game versions prior to the *Seekers of the Storm* update.

- Please report any issues or significant incompatibilities discovered [here](https://github.com/6thmoon/EnemyVariety/issues).

## Version History

#### `0.3.1`
- Allow director to borrow credit on occasion.
- Fix certain stages not being populated with unique monsters.

#### `0.3.0`
- Refactored code to be more flexible.

#### `0.2.1`
- *Horde of Many* chance is configurable instead of absent entirely.

#### `0.2.0`
- *Halcyon Shrine* is not affected.
- Improved compatibility with **SS2** and other plugins.

#### [<ins>`0.1.1`</ins>](https://thunderstore.io/package/download/Nuxlar/DirectorRework/1.1.1/)
- Prioritize more expensive enemies if sufficient credits are available.

#### [<ins>`0.1.0`</ins>](https://thunderstore.io/package/download/Nuxlar/DirectorRework/1.1.0/)
- Add configuration to determine if multiple boss types should appear.
- Fix error message and address various edge cases.
- Prevent director wave ending prematurely if an expensive card is chosen.
- Show appropriate boss title and *Shrine of Combat* message.
