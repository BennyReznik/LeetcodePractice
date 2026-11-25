using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Practice;

/// <summary>Runs submitted code in its own process and prints one JSON line per test.</summary>
public static class Child
{
    public static int Run()
    {
        var request = JsonSerializer.Deserialize<ChildRequest>(Console.In.ReadLine()!, Problems.Json)!;
        using var reply = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var type = Assembly.Load(Convert.FromBase64String(request.Assembly)).GetType("Solution");
        var method = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == request.Method);
        reply.WriteLine("ready");

        foreach (var args in request.Tests)
        {
            var output = new StringWriter();
            Console.SetOut(output);
            var watch = new Stopwatch();
            ChildReply result;
            try
            {
                if (method is null) throw new MissingMethodException($"Method '{request.Method}' was not found. Keep the name from the starter code.");
                var parameters = method.GetParameters();
                if (parameters.Length != args.Count) throw new MissingMethodException($"'{request.Method}' should take {args.Count} parameter(s).");
                var values = parameters.Select((p, i) => args[i].Deserialize(p.ParameterType)).ToArray();
                var instance = method.IsStatic ? null : Activator.CreateInstance(type!);

                watch.Start();
                var value = method.Invoke(instance, values);
                watch.Stop();
                result = new(true, JsonSerializer.SerializeToNode(value, method.ReturnType == typeof(void) ? typeof(object) : method.ReturnType),
                    null, null, 0);
            }
            catch (TargetInvocationException e)
            {
                var inner = e.InnerException!;
                result = new(false, null, $"{inner.GetType().Name}: {inner.Message}", null, 0);
            }
            catch (Exception e)
            {
                result = new(false, null, $"{e.GetType().Name}: {e.Message}", null, 0);
            }
            var text = output.ToString();
            result = result with { Output = text.Length > 2000 ? text[..2000] + "…" : text, Ms = Math.Round(watch.Elapsed.TotalMilliseconds, 2) };
            reply.WriteLine(JsonSerializer.Serialize(result, Problems.Json));
        }
        return 0;
    }
}
