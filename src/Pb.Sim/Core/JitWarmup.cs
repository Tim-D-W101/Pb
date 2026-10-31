using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Pb.Sim.Core;

/// <summary>
/// Compiles an assembly's methods up front, while a level loads, instead of the first time each one runs. The game
/// compiles methods with loops fully optimised on their first call (Pb.csproj), which takes a few milliseconds for the
/// bigger ones: the first time a bot flanked, searched a room or picked cover, that tick stalled by as much. Generic
/// methods and those of generic types are left to compile when first used (they need their type arguments).
/// </summary>
public static class JitWarmup
{
    private static readonly HashSet<Assembly> Done = new();

    /// <summary>Compiles every method of <paramref name="assembly"/> not compiled before; returns how many, and how long it took (ms).</summary>
    public static (int Methods, double Milliseconds) Prepare(Assembly assembly)
    {
        lock (Done)
        {
            if (!Done.Add(assembly))
            {
                return (0, 0.0);
            }
        }

        var watch = Stopwatch.StartNew();
        int count = 0;
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t is not null).ToArray()!;
        }

        const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                 BindingFlags.NonPublic;
        foreach (Type type in types)
        {
            if (type.ContainsGenericParameters || type.IsInterface)
            {
                continue;
            }

            foreach (MethodBase method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method.IsAbstract || method.ContainsGenericParameters ||
                    (method.MethodImplementationFlags & MethodImplAttributes.InternalCall) != 0 || method.GetMethodBody() is null)
                {
                    continue;
                }

                try
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    count++;
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or BadImageFormatException)
                {
                    // Something the runtime can't compile ahead of a call: it compiles when it's called, as before.
                }
            }
        }

        return (count, watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>The sim's own methods (<see cref="Prepare(Assembly)"/> for the sim's assembly).</summary>
    public static (int Methods, double Milliseconds) PrepareSim() => Prepare(typeof(SimWorld).Assembly);
}
