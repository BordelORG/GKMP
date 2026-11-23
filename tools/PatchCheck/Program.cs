// PatchCheck: statically verifies that Harmony patch targets and AccessTools string lookups
// in a built plugin DLL resolve against the game's assemblies. Does not run the game.
// Usage: PatchCheck <plugin.dll> <Managed dir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class P
{
    static readonly Dictionary<string, TypeDefinition> Types = new();
    static int ok, bad, unknown;

    static int Main(string[] a)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(a[1]); resolver.AddSearchDirectory(Path.Combine(AppContext.BaseDirectory, "../../../../../deps/bepinex/BepInEx/core"));
        foreach (var f in new[] { "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll", "UnityEngine.CoreModule.dll", "UnityEngine.UI.dll", "UnityEngine.IMGUIModule.dll", "UnityEngine.InputLegacyModule.dll", "UnityEngine.Physics2DModule.dll", "UnityEngine.AnimationModule.dll" })
        {
            var p = Path.Combine(a[1], f);
            if (!File.Exists(p)) continue;
            var asm = AssemblyDefinition.ReadAssembly(p, new ReaderParameters { AssemblyResolver = resolver });
            foreach (var t in asm.MainModule.GetTypes()) Types[t.FullName.Replace('/', '+')] = t;
        }
        // Self-test hook: PATCHCHECK_DROP=Type::Method removes that method to prove breakage is detected.
        var drop = Environment.GetEnvironmentVariable("PATCHCHECK_DROP");
        if (!string.IsNullOrEmpty(drop))
        {
            var parts = drop.Split("::");
            var dt = Types[parts[0]];
            foreach (var mm in dt.Methods.Where(x => x.Name == parts[1]).ToList()) dt.Methods.Remove(mm);
        }
        var plugin = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = resolver });

        foreach (var t in plugin.MainModule.GetTypes())
        {
            var classAttrs = t.CustomAttributes.Where(IsHP).ToList();
            var (ct, cm, cargs, ckind) = Merge(null, null, null, null, classAttrs);
            var patchMethods = t.Methods.Where(m => m.CustomAttributes.Any(IsHP)).ToList();
            bool hasTargetMethod = t.Methods.Any(m => m.Name == "TargetMethod" || m.Name == "TargetMethods");
            if (patchMethods.Count == 0 && classAttrs.Count > 0)
            {
                if (hasTargetMethod) { unknown++; continue; }
                Check(t.FullName, ct, cm, cargs, ckind);
            }
            foreach (var m in patchMethods)
            {
                var (mt, mm, margs, mkind) = Merge(ct, cm, cargs, ckind, m.CustomAttributes.Where(IsHP));
                Check(t.FullName + "::" + m.Name, mt, mm, margs, mkind);
            }
            foreach (var m in t.Methods.Where(x => x.HasBody)) ScanAccessTools(t.FullName + "::" + m.Name, m);
        }
        Console.WriteLine($"\nOK={ok} BROKEN={bad} dynamic(TargetMethod, unchecked)={unknown}");
        return bad > 0 ? 1 : 0;
    }

    static bool IsHP(CustomAttribute c) => c.AttributeType.Name == "HarmonyPatch";

    static (TypeReference, string, TypeReference[], int?) Merge(TypeReference t, string m, TypeReference[] args, int? kind, IEnumerable<CustomAttribute> attrs)
    {
        foreach (var at in attrs)
            foreach (var ca in at.ConstructorArguments)
            {
                if (ca.Value is TypeReference tr) t = tr;
                else if (ca.Value is string s) m = s;
                else if (ca.Value is CustomAttributeArgument[] arr) args = arr.Select(x => x.Value as TypeReference).ToArray();
                else if (ca.Type.Name == "MethodType") kind = Convert.ToInt32(ca.Value);
                else if (ca.Value is int i && ca.Type.Name == "MethodType") kind = i;
            }
        return (t, m, args, kind);
    }

    static void Check(string where, TypeReference tr, string name, TypeReference[] args, int? kind)
    {
        if (tr == null) { unknown++; return; }
        var key = tr.FullName.Replace('/', '+');
        if (!Types.TryGetValue(key, out var td)) { Report(where, $"type {key} NOT FOUND"); return; }
        // MethodType: 0 Normal, 1 Getter, 2 Setter, 3 Constructor, 4 StaticConstructor
        if (kind == 3 || kind == 4) { if (td.Methods.Any(x => x.IsConstructor)) ok++; else Report(where, $"{key} ctor missing"); return; }
        if (name == null) { unknown++; return; }
        if (kind == 1 || kind == 2)
        {
            if (FindInHierarchy(td, x => x.Properties.Any(p => p.Name == name))) ok++; else Report(where, $"{key}.{name} property NOT FOUND");
            return;
        }
        var cands = AllMethods(td).Where(x => x.Name == name).ToList();
        if (cands.Count == 0) { Report(where, $"{key}.{name} method NOT FOUND"); return; }
        if (args != null && !cands.Any(c => c.Parameters.Count == args.Length &&
                c.Parameters.Select(p => Norm(p.ParameterType)).SequenceEqual(args.Select(Norm))))
        { Report(where, $"{key}.{name}({string.Join(",", args.Select(Norm))}) overload NOT FOUND; have: " + string.Join(" | ", cands.Select(c => "(" + string.Join(",", c.Parameters.Select(p => Norm(p.ParameterType))) + ")"))); return; }
        if (args == null && cands.Count > 1 && cands.Select(c => c.DeclaringType).Distinct().Count() == 1 && cands.Count(c => c.DeclaringType == td) > 1)
            Console.WriteLine($"[AMBIG] {where}: {key}.{name} has {cands.Count} overloads and no argument types given");
        ok++;
    }

    static string Norm(TypeReference t) => t == null ? "?" : t.FullName.Replace('/', '+').TrimEnd('&');

    static IEnumerable<MethodDefinition> AllMethods(TypeDefinition td)
    {
        for (var cur = td; cur != null; cur = Base(cur))
            foreach (var m in cur.Methods) yield return m;
    }
    static bool FindInHierarchy(TypeDefinition td, Func<TypeDefinition, bool> f)
    {
        for (var cur = td; cur != null; cur = Base(cur)) if (f(cur)) return true;
        return false;
    }
    static TypeDefinition Base(TypeDefinition t)
    {
        if (t.BaseType == null) return null;
        Types.TryGetValue(t.BaseType.FullName.Replace('/', '+'), out var b);
        if (b != null) return b;
        try { return t.BaseType.Resolve(); } catch { return null; }
    }

    // Finds patterns: ldtoken T; call GetTypeFromHandle; ldstr "name"; ... call AccessTools.Method/Field/Property...
    static void ScanAccessTools(string where, MethodDefinition m)
    {
        var ins = m.Body.Instructions;
        for (int i = 0; i < ins.Count; i++)
        {
            if (ins[i].OpCode != OpCodes.Call || ins[i].Operand is not MethodReference mr) continue;
            if (mr.DeclaringType.Name != "AccessTools") continue;
            var kindName = mr.Name;
            if (kindName == "TypeByName")
            {
                if (i > 0 && ins[i - 1].Operand is string tn)
                {
                    if (Types.ContainsKey(tn)) ok++; else if (!tn.Contains('.') || tn.StartsWith("UnityEngine")) Report(where, $"AccessTools.TypeByName(\"{tn}\") NOT FOUND in game assemblies");
                }
                continue;
            }
            if (!(kindName is "Method" or "DeclaredMethod" or "Field" or "DeclaredField" or "Property" or "DeclaredProperty" or "PropertyGetter" or "PropertySetter" or "DeclaredPropertyGetter" or "DeclaredPropertySetter")) continue;
            if (mr.Parameters.Count < 2 || mr.Parameters[0].ParameterType.Name != "Type" || mr.Parameters[1].ParameterType.Name != "String") continue;
            // walk back for the string and the type token
            string name = null; TypeReference tr = null;
            for (int j = i - 1; j >= Math.Max(0, i - 12); j--)
            {
                if (name == null && ins[j].OpCode == OpCodes.Ldstr) name = (string)ins[j].Operand;
                if (tr == null && ins[j].OpCode == OpCodes.Ldtoken && ins[j].Operand is TypeReference t) { tr = t; break; }
            }
            if (name == null || tr == null) { unknown++; continue; }
            var key = tr.FullName.Replace('/', '+');
            if (!Types.TryGetValue(key, out var td)) { continue; } // plugin-own or BCL type
            bool found = kindName.Contains("Field") ? FindInHierarchy(td, x => x.Fields.Any(f => f.Name == name))
                       : kindName.Contains("Propert") ? FindInHierarchy(td, x => x.Properties.Any(p => p.Name == name))
                       : AllMethods(td).Any(x => x.Name == name);
            if (found) ok++; else Report(where, $"AccessTools.{kindName}({key}, \"{name}\") NOT FOUND");
        }
    }

    static void Report(string where, string msg) { bad++; Console.WriteLine($"[BROKEN] {where}: {msg}"); }
}
