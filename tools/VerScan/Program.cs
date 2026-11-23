// Lists members whose name contains "version" in a game assembly (helper for finding the in-game version string).
using Mono.Cecil; using System; using System.Linq;
var m = ModuleDefinition.ReadModule(args[0]);
foreach (var t in m.GetTypes())
{
    foreach (var f in t.Fields.Where(f => f.Name.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0))
        Console.WriteLine($"F {t.FullName}.{f.Name} : {f.FieldType.Name} {(f.HasConstant ? "= " + f.Constant : "")}");
    foreach (var p in t.Properties.Where(p => p.Name.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0))
        Console.WriteLine($"P {t.FullName}.{p.Name} : {p.PropertyType.Name}");
}
