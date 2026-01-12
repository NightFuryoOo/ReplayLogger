# ReplayLogger
ReplayLogger is a mod that was created to verify the legitimacy of player challenge runs. It implements a hidden keylogging system with visualization of specific inputs. These keys are needed to prevent video editing and other cheating methods during challenge attempts.

The mod activates when entering the Pantheons and records:
* All keyboard presses
* Boss health values
* Player state (Vulnerable/Invulnerable)
* Other gameplay details

All data gets saved into an encrypted log file to prevent any player manipulation. The log file is stored in the mod's folder when a Pantheon is completed by any means.