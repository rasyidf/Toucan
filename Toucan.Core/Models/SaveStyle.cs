namespace Toucan.Core.Models;

public enum SaveStyles
{
    Json = 0,
    Namespaced = 1,
    Properties = 2,   // PO/gettext
    Yaml = 3,
    Adb = 4,          // INI
    Toml = 5,
    AndroidXml = 6,
    IosStrings = 7,
    Xliff = 8,
    Arb = 9,          // Flutter ARB
    Csv = 10,
    Resx = 11,
}
