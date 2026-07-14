# Query examples for the coding agent

## Spawn vehicle safely

Queries:

```text
World.CreateVehicle overload Model Vector3 heading
Model Request IsLoaded IsInCdImage IsValid MarkAsNoLongerNeeded
Ped SetIntoVehicle VehicleSeat.Driver
official example spawn vehicle ScriptHookVDotNet v3
```

## Create hostile ped

```text
World.CreatePed PedHash Model
Ped Task FightAgainst
Ped RelationshipGroup relationship
WeaponCollection Give
Model lifecycle request release
```

## Draw UI / notification

```text
GTA.UI notification v3
Screen ShowSubtitle
TextElement Draw
Script Tick UI draw
```

## Native fallback

```text
native SET_VEHICLE_RADIO_ENABLED legacy params return
Function.Call Hash.SET_VEHICLE_RADIO_ENABLED
OutputArgument ScriptHookVDotNet v3
```

## Runtime crash

```text
<exception type> <method name> <stack frame>
ScriptHookVDotNet.log exact message
source implementation <member>
game version compatibility
```

## Retrieval filters

```json
{
  "game": "legacy",
  "api": "v3",
  "top_k": 12,
  "prefer_sources": [
    "current_project",
    "current_logs",
    "local_api_xml",
    "shvdn_source",
    "shvdn_wiki",
    "native_db_legacy"
  ]
}
```
