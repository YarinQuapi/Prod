## Features

* Get Owners Steams ID & Names
* Passive mode (Enabled from config)
* Building Blocks
* Tool Cupboard whitelist
* whitelist of CodeLocks from boxes
* Code numbers of codelocks
* Deployers of Deployables
* Fully configurable

## Chat Commands

* `/prod` - Main command (Passive mode requires permission: `prod.passive.use`)

## Passive Mode
Passive mode is a mode that enables /prod to be used in a not as cheating way. For example if you don't want your admins to get the codes of their enemies.
Or for any other use you can think of.

## Configuration

```json
{
  "Messages": {
    "boxNeedsCode": "Can't find owners of an item without a Code Lock",
    "Code": "Code is: {0}",
    "codeLockList": "CodeLock whitelist:",
    "helpProd": "/prod on a building or tool cupboard to know who owns it.",
    "Information added message (Only works with printToConsole option on.)": "New information was printed to your console. (Press F1)",
    "noAccess": "You don't have access to this command",
    "noBlockOwnerfound": "No owner found for this building block",
    "noCodeAccess": "No players has access to this Lock",
    "noCupboardPlayers": "No players has access to this cupboard",
    "noTargetfound": "You must look at a tool cupboard or building",
    "Passive Mode: Codelock List": "Codelock Whitelist:",
    "Toolcupboard": "Tool Cupboard"
  },
  "Plugin Dev": {
    "Are you are plugin dev?": false,
    "Dump all components of all entities that you are looking at? (false will do only the closest one)": false
  },
  "Prod": {
    "authLevel": 1,
    "Passive Mode": false,
    "Print to player console instead of chat? ": true
  }
}
```
## Planned:
* Continue adding support for more and more building blocks
* Recode the plugin to match 2021 Development standards
