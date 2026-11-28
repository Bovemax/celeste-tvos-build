using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text;

const string GameHash = "F083E95631C02B479FCA8E5F0216DA8CF1F293CC280874A0D7F803332B0BB23C";
const string FnaHash = "4CF5944083C1E51E7F0F32EE7631F5B5681011F45007B295E0A70FB6BBE0FBBB";
const string HookHash = "94D484AB5A24069DF00BB4B6F63C4BBA9855F4E49631CC3FD0DB1CAEB538D43C";
static string Hash(string path) { using var f=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)); }
static void Require(bool ok,string message) { if(!ok) throw new InvalidDataException(message); }
static MethodDefinition Method(TypeDefinition type,string name,int args) =>
    type.Methods.Single(m=>m.Name==name && m.Parameters.Count==args);
static bool Call(Instruction i,string type,string name) =>
    i.Operand is MethodReference m && m.DeclaringType.FullName==type && m.Name==name;
static void Reset(MethodDefinition m) {
    Require(m.HasBody && m.IsStatic,"Unexpected method shape: "+m.FullName);
    m.Body.Instructions.Clear(); m.Body.ExceptionHandlers.Clear(); m.Body.Variables.Clear();
}
static void ReplaceWithThrow(MethodDefinition m,ModuleDefinition mod,string msg) {
    Reset(m);
    var il=m.Body.GetILProcessor();
    il.Emit(OpCodes.Ldstr,msg);
    il.Emit(OpCodes.Newobj,mod.ImportReference(typeof(NotSupportedException).GetConstructor(new[]{typeof(string)})!));
    il.Emit(OpCodes.Throw);
}
static void InsertAfter(ILProcessor il,Instruction anchor,params Instruction[] items) {
    foreach(var i in items) { il.InsertAfter(anchor,i); anchor=i; }
}
static void ReplaceExit(MethodDefinition m,bool rethrow) {
    var call=m.Body.Instructions.Where(i=>Call(i,"System.Environment","Exit")).Single();
    call.OpCode=OpCodes.Pop; call.Operand=null;
    if(rethrow) InsertAfter(m.Body.GetILProcessor(),call,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Throw));
}
static void Patch(AssemblyDefinition game) {
    var mod=game.MainModule;
    var boot=mod.GetType("Celeste.Mod.BOOT");
    var main=Method(boot,"Main",1);
    Require(main==mod.EntryPoint,"Wrong original entry point");
    var setup=Method(boot,"SetupNativeLibPaths",0);
    Reset(setup); setup.Body.GetILProcessor().Emit(OpCodes.Ret);
    var launcher=Method(boot,"RestartViaLauncher",0);
    Require(launcher.ReturnType.FullName=="System.Boolean","Unexpected launcher return type");
    Reset(launcher); launcher.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_0); launcher.Body.GetILProcessor().Emit(OpCodes.Ret);
    ReplaceWithThrow(Method(boot,"StartCelesteProcess",2),mod,"Desktop process restart is unavailable in the tvOS test host.");
    ReplaceWithThrow(Method(boot,"StartVanilla",0),mod,"Use the separate vanilla app on tvOS.");
    ReplaceExit(main,false);
    ReplaceExit(Method(mod.GetType("Celeste.Celeste"),"CriticalFailureHandler",1),true);
    // ErrorLog.Open already honors EVEREST_NO_ERRORLOG_ON_CRASH. Retain logging.
    var getEnv=mod.ImportReference(typeof(Environment).GetMethod("GetEnvironmentVariable",new[]{typeof(string)})!);
    var combine=mod.ImportReference(typeof(Path).GetMethod("Combine",new[]{typeof(string),typeof(string)})!);
    var everest=mod.GetType("Celeste.Mod.Everest");
    var eboot=Method(everest,"Boot",0);
    var setters=eboot.Body.Instructions.Where(i=>Call(i,"Celeste.Mod.Everest","set_PathEverest")).ToArray();
    Require(setters.Length==2,"Unexpected PathEverest anchors");
    var eil=eboot.Body.GetILProcessor();
    foreach(var setter in setters) {
        var target=(MethodReference)setter.Operand;
        setter.OpCode=OpCodes.Pop;setter.Operand=null;
        InsertAfter(eil,setter,Instruction.Create(OpCodes.Ldstr,"EVEREST_TVOS_WORKDIR"),
            Instruction.Create(OpCodes.Call,getEnv),Instruction.Create(OpCodes.Call,target));
    }
    var user=mod.GetType("Celeste.UserIO");
    var cctor=Method(user,".cctor",0);
    var uil=cctor.Body.GetILProcessor();
    foreach(var name in new[]{"SavePath","BackupPath"}) {
        var field=user.Fields.Single(f=>f.Name==name && f.IsStatic && f.FieldType.FullName=="System.String");
        var store=cctor.Body.Instructions.Single(i=>i.OpCode==OpCodes.Stsfld && i.Operand is FieldReference fr && fr.FullName==field.FullName);
        store.OpCode=OpCodes.Pop;store.Operand=null;
        InsertAfter(uil,store,Instruction.Create(OpCodes.Ldstr,"EVEREST_SAVEPATH"),Instruction.Create(OpCodes.Call,getEnv),
            Instruction.Create(OpCodes.Ldstr,name=="SavePath"?"Saves":"Backups"),Instruction.Create(OpCodes.Call,combine),
            Instruction.Create(OpCodes.Stsfld,field));
    }
    // Cecil does not automatically expand short branch encodings after inserting IL.
    foreach(var m in new[]{main,Method(mod.GetType("Celeste.Celeste"),"CriticalFailureHandler",1),eboot,cctor}) {
        foreach(var i in m.Body.Instructions) i.OpCode=i.OpCode.Code switch {
            Code.Br_S=>OpCodes.Br, Code.Brfalse_S=>OpCodes.Brfalse, Code.Brtrue_S=>OpCodes.Brtrue,
            Code.Beq_S=>OpCodes.Beq, Code.Bge_S=>OpCodes.Bge, Code.Bge_Un_S=>OpCodes.Bge_Un,
            Code.Bgt_S=>OpCodes.Bgt, Code.Bgt_Un_S=>OpCodes.Bgt_Un, Code.Ble_S=>OpCodes.Ble,
            Code.Ble_Un_S=>OpCodes.Ble_Un, Code.Blt_S=>OpCodes.Blt, Code.Blt_Un_S=>OpCodes.Blt_Un,
            Code.Bne_Un_S=>OpCodes.Bne_Un, Code.Leave_S=>OpCodes.Leave, _=>i.OpCode
        };
    }
    Console.WriteLine("Applied host-native resolver, no desktop relaunch, return-to-host diagnostics, writable Everest and save roots.");
}
if(args.Length!=2) throw new ArgumentException("Usage: BootPatcher <offline-stage> <new-payload-dir>");
string src=Path.GetFullPath(args[0]), dst=Path.GetFullPath(args[1]);
Require(!Directory.Exists(dst)&&!File.Exists(dst),"Choose a new output directory");
Require(!dst.StartsWith(src+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Output must be outside original stage");
foreach(var pair in new[]{("Celeste.dll",GameHash),("FNA.dll",FnaHash),("MMHOOK_Celeste.dll",HookHash)})
    Require(Hash(Path.Combine(src,pair.Item1))==pair.Item2,"Input hash differs: "+pair.Item1);
var runtimeDir=System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
var framework=Directory.EnumerateFiles(runtimeDir,"*.dll")
    .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
framework.UnionWith(new[]{"mscorlib","System","System.Core","System.Xml","netstandard"});
// Compute the local non-framework closure before writing anything.
var pending=new Queue<string>(new[]{"Celeste","Celeste.Content","FNA","MMHOOK_Celeste"});
var files=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
while(pending.TryDequeue(out var name)) {
    if(files.ContainsKey(name)||framework.Contains(name)) continue;
    Require(name.Length<=95 && name.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_'),"Unsafe assembly name");
    string path=Path.Combine(src,name+".dll");
    Require(File.Exists(path),"Unresolved local dependency: "+name);
    using var a=AssemblyDefinition.ReadAssembly(path);
    Require(a.Name.Name==name,"Assembly name differs: "+name);
    files.Add(name,path);
    foreach(var r in a.MainModule.AssemblyReferences) pending.Enqueue(r.Name);
}
Require(files.Count>=4 && files.Count<95,"Unexpected closure size");
using var game=AssemblyDefinition.ReadAssembly(files["Celeste"],new ReaderParameters{InMemory=true});
Patch(game);
// Originals remain untouched; fail closed if any locked anchor differs.
Directory.CreateDirectory(dst);
foreach(var kv in files) {
    string path=Path.Combine(dst,kv.Key+".dll");
    if(kv.Key=="Celeste") game.Write(path); else File.Copy(kv.Value,path);
}
using(var patched=AssemblyDefinition.ReadAssembly(Path.Combine(dst,"Celeste.dll"))) {
    Require(patched.MainModule.EntryPoint.DeclaringType.FullName=="Celeste.Mod.BOOT","Entry point changed");
    Require(!patched.MainModule.EntryPoint.Body.Instructions.Any(i=>Call(i,"System.Environment","Exit")),"BOOT exit remains");
}
var manifest=new List<string>{"everest-tvos-boot-v1"};
foreach(var name in files.Keys) {
    string file=name+".dll";
    manifest.Add(Hash(Path.Combine(dst,file))+"\t"+file);
}
File.WriteAllLines(Path.Combine(dst,"everest-payload.tsv"),manifest,new UTF8Encoding(false));
Console.WriteLine($"PASS: prepared {files.Count} managed assemblies; source unchanged. Runtime compatibility not verified.");
