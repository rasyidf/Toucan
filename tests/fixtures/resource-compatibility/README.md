# Frozen Toucan sample plugin

`m0-sample-plugin.zip` contains the unchanged `Toucan.Sample.Plugin.dll`, `Toucan.Sample.Plugin.Desktop.dll` and `plugin.json` frozen during M0 at source commit `ea3d255b6acea0e202c2c2455bdb3317b06e25ce`. Their SHA-256 hashes are in `docs/specs/resource-compatibility/evidence/m0-baseline.json`.

These are builds of Toucan's own MIT-licensed sample source under `samples/Toucan.Sample.Plugin` and `samples/Toucan.Sample.Plugin.Desktop`. No third-party runtime DLLs, BabelEdit resources or customer data are included. The archive makes the pre-M1 compiled fixture available to CI without rebuilding it against current contracts.

`FrozenM0PluginLoadsAndSavesWithoutRecompilation` validates each original hash, installs the plugin and drives registration/create/save/reopen through the current host. Keep the archive unchanged when the API changes. It is a baseline sample fixture, not an independently published historical plugin release.
