using System;
using System.Linq;
using System.Reflection;
using System.IO;

class Program
{
    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        var hal = Assembly.LoadFrom(@"C:\Users\alxth\.nuget\packages\cosmos.hal2\0.1.0-localbuild20221121060004\lib\net6.0\Cosmos.HAL2.dll");
        var sys2 = Assembly.LoadFrom(@"C:\Users\alxth\.nuget\packages\cosmos.system2\0.1.0-localbuild20221121060004\lib\net6.0\Cosmos.System2.dll");

        DumpType(hal.GetType("Cosmos.HAL.Drivers.PCI.Video.VMWareSVGAII"));
        DumpType(sys2.GetType("Cosmos.System.Graphics.SVGAIICanvas"));
        DumpType(sys2.GetType("Cosmos.System.Graphics.Mode"));
    }

    static void DumpType(Type t)
    {
        Console.WriteLine("=== " + (t?.FullName ?? "<null>") + " ===");
        if (t == null) return;

        Console.WriteLine("Fields:");
        foreach (var f in t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).OrderBy(x=>x.Name))
        {
            string ft;
            try { ft = f.FieldType.FullName ?? f.FieldType.Name; }
            catch (Exception ex) { ft = "<err:" + ex.GetType().Name + ">"; }
            Console.WriteLine($"  {ft} {f.Name}");
        }

        Console.WriteLine("Properties:");
        foreach (var p in t.GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).OrderBy(x=>x.Name))
        {
            string pt;
            try { pt = p.PropertyType.FullName ?? p.PropertyType.Name; }
            catch (Exception ex) { pt = "<err:" + ex.GetType().Name + ">"; }
            Console.WriteLine($"  {pt} {p.Name}");
        }

        Console.WriteLine("Methods (filtered):");
        foreach (var m in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly)
            .Where(m => m.Name.IndexOf("line", StringComparison.OrdinalIgnoreCase) >= 0
                     || m.Name.IndexOf("pitch", StringComparison.OrdinalIgnoreCase) >= 0
                     || m.Name.IndexOf("width", StringComparison.OrdinalIgnoreCase) >= 0
                     || m.Name.IndexOf("height", StringComparison.OrdinalIgnoreCase) >= 0
                     || m.Name.IndexOf("offset", StringComparison.OrdinalIgnoreCase) >= 0
                     || m.Name.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0
                     || m.Name.IndexOf("draw", StringComparison.OrdinalIgnoreCase) >= 0
                     || m.Name.IndexOf("setmode", StringComparison.OrdinalIgnoreCase) >= 0
                     || m.Name.IndexOf("register", StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(m=>m.Name))
        {
            string ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({ps})");
        }
        Console.WriteLine();
    }

    static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name + ".dll";
        var roots = new[]
        {
            @"C:\Users\alxth\.nuget\packages\cosmos.system2\0.1.0-localbuild20221121060004\lib\net6.0",
            @"C:\Users\alxth\.nuget\packages\cosmos.hal2\0.1.0-localbuild20221121060004\lib\net6.0",
            @"C:\Users\alxth\.nuget\packages\cosmos.core2\0.1.0-localbuild20221121060004\lib\net6.0",
            @"C:\Users\alxth\.nuget\packages\cosmos.common2\0.1.0-localbuild20221121060004\lib\net6.0",
            @"C:\Users\alxth\.nuget\packages\cosmos.core\0.1.0-localbuild20221121060004\lib\net6.0",
            @"C:\Users\alxth\.nuget\packages\cosmos.debug.kernel\0.1.0-localbuild20221121060004\lib\net6.0"
        };
        foreach (var r in roots)
        {
            var p = Path.Combine(r, name);
            if (File.Exists(p)) return Assembly.LoadFrom(p);
        }
        return null;
    }
}
