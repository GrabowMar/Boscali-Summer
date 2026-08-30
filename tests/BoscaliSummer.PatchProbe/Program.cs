using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: BoscaliSummer.PatchProbe <game-dir> <plugin-dll>");
    return 2;
}

string gameDir = Path.GetFullPath(args[0]);
string pluginPath = Path.GetFullPath(args[1]);
string managedDir = Path.Combine(gameDir, "NuclearOption_Data", "Managed");
string bepInExDir = Path.Combine(gameDir, "BepInEx", "core");
string[] roots = { managedDir, bepInExDir, Path.GetDirectoryName(pluginPath)! };

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string root in roots)
    {
        string candidate = Path.Combine(root, name.Name + ".dll");
        if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
    }
    return null;
};

Assembly gameAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managedDir, "Assembly-CSharp.dll"));
const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
Assembly pluginAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);

(string Type, string Method)[] targets =
{
    ("Airbase", "CaptureFaction"),
    ("Building", "OnStartClient"),
    ("BulletSim+Bullet", "TrajectoryTrace"),
    ("MapBuilding", "TakeDamage"),
    ("Missile", "UserCode_RpcDetonate_897349600")
};

foreach ((string typeName, string methodName) in targets)
{
    Type type = gameAssembly.GetType(typeName, true)!;
    if (type.GetMethod(methodName, AllMembers) == null)
        throw new MissingMethodException(typeName, methodName);
    Console.WriteLine("  " + typeName + "." + methodName);
}

(string Type, string Field)[] fields =
{
    ("MapBuilding", "hitPoints"),
    ("Missile", "blastYield")
};

foreach ((string typeName, string fieldName) in fields)
{
    Type type = gameAssembly.GetType(typeName, true)!;
    if (type.GetField(fieldName, AllMembers) == null)
        throw new MissingFieldException(typeName, fieldName);
}

string[] patchTypes =
{
    "BoscaliSummer.Fire.BulletImpactPatch",
    "BoscaliSummer.Fire.MissileImpactPatch",
    "BoscaliSummer.Fire.MapBuildingDamagePatch",
    "BoscaliSummer.Garrisons.AirbaseCapturePatch",
    "BoscaliSummer.Garrisons.GarrisonClientVisualPatch"
};

foreach (string patchType in patchTypes)
    if (pluginAssembly.GetType(patchType, false) == null)
        throw new TypeLoadException("Plugin patch type missing: " + patchType);

Console.WriteLine("Patch target probe: all 5 methods, 2 private fields, and 5 patch classes resolved.");
return 0;
