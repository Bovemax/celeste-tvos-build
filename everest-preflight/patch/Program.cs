using Mono.Cecil;
using Mono.Cecil.Cil;
if (args.Length != 2) throw new ArgumentException("Input and output fixture DLL required");
if (Path.GetFullPath(args[0]) == Path.GetFullPath(args[1]) || File.Exists(args[1]))
    throw new IOException("Refusing to overwrite fixture");
using var assembly = AssemblyDefinition.ReadAssembly(args[0]);
if (assembly.Name.Name != "EverestFixture") throw new InvalidDataException("Only synthetic fixture allowed");
var method = assembly.MainModule.GetType("EverestFixture.Sample").Methods.Single(m => m.Name == "Value");
method.Body.Instructions.Clear();
method.Body.ExceptionHandlers.Clear();
method.Body.Variables.Clear();
var il = method.Body.GetILProcessor();
il.Append(il.Create(OpCodes.Ldc_I4, 42));
il.Append(il.Create(OpCodes.Ret));
assembly.Write(args[1]);
Console.WriteLine("PASS: offline Cecil rewrite of synthetic fixture");
