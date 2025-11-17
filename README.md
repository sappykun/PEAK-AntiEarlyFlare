# Anti Early Flare

Prevents players from using the flare at the Peak before a certain percentage of other players have made it.
The threshold for players required to activate the flare is configurable.

## Config

`ReachedPeakThreshold` - Ratio of scouts that need to be at the Peak before the flare can be used properly. 0 effectively disables the plugin, while 1 means everyone has to be at the Peak. The value is inclusive, so the default ratio of 0.5 means 2/4 players at the Peak can light the flare.

`SmiteEarlyFlareUser` - Smites a Scout that would have triggered an early flare. Defaults to off.

## Credits

Thanks to letsalllovelain for helping me out with Harmony patches.
