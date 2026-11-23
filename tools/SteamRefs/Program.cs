// Lists Steamworks.* members referenced from an assembly (calls, field access, ctor) with call-site counts.
using Mono.Cecil; using Mono.Cecil.Cil; using System; using System.Linq; using System.Collections.Generic;
var m = ModuleDefinition.ReadModule(args[0]);
var counts = new Dictionary<string,int>();
foreach (var t in m.GetTypes()) foreach (var meth in t.Methods.Where(x => x.HasBody))
  foreach (var i in meth.Body.Instructions)
  {
    string key = null;
    if (i.Operand is MethodReference mr && mr.DeclaringType.Namespace == "Steamworks") key = mr.DeclaringType.Name + "." + mr.Name;
    else if (i.Operand is MethodReference g && g.DeclaringType is GenericInstanceType git && git.ElementType.Namespace=="Steamworks") key = git.ElementType.Name + "<" + string.Join(",", git.GenericArguments.Select(a=>a.Name)) + ">." + g.Name;
    if (key != null) counts[key] = counts.GetValueOrDefault(key) + 1;
  }
foreach (var kv in counts.OrderBy(k => k.Key)) Console.WriteLine($"{kv.Value,4} {kv.Key}");
