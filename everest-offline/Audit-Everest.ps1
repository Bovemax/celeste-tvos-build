[CmdletBinding()]
param([string]$Root='C:\CelesteTV')
$ErrorActionPreference='Stop'
$Root=(Resolve-Path -LiteralPath $Root).Path
$stage=Join-Path $Root 'work4\everest-offline-1.6458.0'
$old=Join-Path $Root 'desktop-check\FNA.dll'
$dotnet=Join-Path $Root 'toolchain\dotnet\dotnet.exe'
$cecil=Join-Path $stage 'Mono.Cecil.dll'
foreach ($p in @($old,$dotnet,$cecil,(Join-Path $stage 'Celeste.dll'),(Join-Path $stage 'FNA.dll'),(Join-Path $stage 'MMHOOK_Celeste.dll'))) {
    if (!(Test-Path -LiteralPath $p -PathType Leaf)) { throw "Missing $p" }
}
$work=Join-Path $Root 'work4\everest-audit'
[IO.Directory]::CreateDirectory($work)|Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Mono.Cecil">
      <HintPath>$(CecilPath)</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $work 'Audit.csproj') -Encoding UTF8
@'
using Mono.Cecil;
using Mono.Cecil.Cil;

static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots) {
    foreach(var t in roots) {
        yield return t;
        foreach(var child in AllTypes(t.NestedTypes)) yield return child;
    }
}
static string NativeKey(MethodDefinition m) =>
    m.PInvokeInfo.Module.Name.ToLowerInvariant() + "!" + m.PInvokeInfo.EntryPoint;
static string Signature(MethodDefinition m) =>
    m.ReturnType.FullName + "(" + string.Join(",",m.Parameters.Select(p=>p.ParameterType.FullName)) + ") [" + m.PInvokeInfo.Attributes + "]";
static Dictionary<string,string[]> NativeImports(AssemblyDefinition a) =>
    AllTypes(a.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasPInvokeInfo)
    .GroupBy(NativeKey).ToDictionary(g=>g.Key,g=>g.Select(Signature).Distinct().Order().ToArray());

string root=args[0];
using var oldFna=AssemblyDefinition.ReadAssembly(args[1]);
using var newFna=AssemblyDefinition.ReadAssembly(Path.Combine(root,"FNA.dll"));
using var game=AssemblyDefinition.ReadAssembly(Path.Combine(root,"Celeste.dll"));
using var hooks=AssemblyDefinition.ReadAssembly(Path.Combine(root,"MMHOOK_Celeste.dll"));
Console.WriteLine("=== Identities / entry point ===");
foreach(var a in new[]{oldFna,newFna,game,hooks})
    Console.WriteLine(a.Name.FullName+" | "+a.MainModule.Architecture+" | "+a.MainModule.Attributes+" | entry="+a.MainModule.EntryPoint?.FullName);
var oldImports=NativeImports(oldFna);
var newImports=NativeImports(newFna);
Console.WriteLine($"=== FNA native import diff: old={oldImports.Count}, new={newImports.Count} ===");
foreach(var kv in newImports.OrderBy(x=>x.Key)) {
    if(!oldImports.TryGetValue(kv.Key,out var prev)) Console.WriteLine("ADDED "+kv.Key+" "+string.Join(" / ",kv.Value));
    else if(!prev.SequenceEqual(kv.Value)) Console.WriteLine("CHANGED "+kv.Key+" old="+string.Join(" / ",prev)+" new="+string.Join(" / ",kv.Value));
}
foreach(var key in oldImports.Keys.Except(newImports.Keys).Order()) Console.WriteLine("REMOVED "+key);
Console.WriteLine("=== FNA and game native symbol candidates ===");
foreach(var a in new[]{newFna,game}) {
    foreach(var kv in NativeImports(a).OrderBy(x=>x.Key)) Console.WriteLine("IMPORT "+a.Name.Name+" "+kv.Key);
}
Console.WriteLine("=== Managed reference closure (metadata only) ===");
var framework = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")??"").Split(Path.PathSeparator)
    .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
var pending=new Queue<string>(new[]{"Celeste.dll","FNA.dll","MMHOOK_Celeste.dll","Celeste.Content.dll"});
var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
while(pending.TryDequeue(out var file)) {
    if(!seen.Add(file)) continue;
    var path=Path.Combine(root,file);
    if(!File.Exists(path)) {Console.WriteLine("MISSING "+file);continue;}
    using var a=AssemblyDefinition.ReadAssembly(path);
    Console.WriteLine("ASSEMBLY "+a.Name.FullName);
    foreach(var r in a.MainModule.AssemblyReferences.OrderBy(r=>r.Name)) {
        string dep=r.Name+".dll";
        if(File.Exists(Path.Combine(root,dep))) {
            Console.WriteLine("  LOCAL "+r.FullName);
            if(!framework.Contains(r.Name)) pending.Enqueue(dep);
        } else if(framework.Contains(r.Name)) Console.WriteLine("  DESKTOP_FRAMEWORK "+r.FullName);
        else Console.WriteLine("  UNRESOLVED "+r.FullName);
    }
}
Console.WriteLine("=== Boot, paths and lifecycle anchors ===");
foreach(var t in AllTypes(game.MainModule.Types).Where(t=>t.FullName is "Celeste.Mod.BOOT" or "Celeste.Celeste" or "Celeste.UserIO" or "Celeste.Mod.Everest")) {
    foreach(var m in t.Methods.Where(m=>m.Name is "Main" or "orig_Main" or "Boot" or "Initialize" or "SetupNativeLibPaths" or "StartCelesteProcess" or "GetSavePath" or "GetSaveFilePath" or "GetSaveDirectory" or ".cctor")) {
        Console.WriteLine("METHOD "+m.FullName);
        if(!m.HasBody) continue;
        foreach(var i in m.Body.Instructions.Where(i=>i.Operand is MethodReference))
            if(i.Operand is MethodReference callee && (callee.DeclaringType.FullName.StartsWith("System.IO.") || callee.DeclaringType.FullName.StartsWith("System.Environment") || callee.DeclaringType.FullName.StartsWith("System.Diagnostics.Process") || callee.DeclaringType.FullName.Contains("Everest") || callee.DeclaringType.FullName.Contains("BOOT") || callee.DeclaringType.FullName=="Celeste.Celeste"))
                Console.WriteLine("  CALL "+callee.FullName);
    }
}
Console.WriteLine("PASS: metadata scan complete; not a device or ABI pass.");
'@ | Set-Content -LiteralPath (Join-Path $work 'Program.cs') -Encoding UTF8
$report=Join-Path $work 'everest-audit.txt'
& $dotnet run --project (Join-Path $work 'Audit.csproj') -c Release "-p:CecilPath=$cecil" -- $stage $old 2>&1 | Tee-Object -FilePath $report
if ($LASTEXITCODE -ne 0) { throw 'Metadata audit failed' }

$nm=Join-Path $Root 'toolchain\llvm\bin\llvm-nm.exe'
if (!(Test-Path -LiteralPath $nm)) {
    $candidate=Get-ChildItem (Join-Path $Root 'toolchain') -Recurse -File -Filter 'llvm-nm.exe' | Select-Object -First 1
    if ($candidate) { $nm=$candidate.FullName }
}
if (Test-Path -LiteralPath $nm) {
    $libs=@{'CelesteNative'=(Join-Path $Root 'native\CelesteNative.dylib');'FMODLocal'=(Join-Path $Root 'inputs\native\FMODLocal.dylib')}
    $symbols=@{}
    foreach ($lib in $libs.Keys) {
        if (!(Test-Path -LiteralPath $libs[$lib])) { throw "Missing $($libs[$lib])" }
        $raw=& $nm --defined-only --extern-only $libs[$lib] 2>&1
        if ($LASTEXITCODE -ne 0) { throw "llvm-nm failed for $lib" }
        $set=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
        foreach ($line in $raw) { if ("$line" -match '\s_([^\s]+)$') { [void]$set.Add($Matches[1]) } }
        if (!$set.Count) { throw "No native symbols parsed for $lib" }
        $symbols[$lib]=$set
    }
    $summary=@(foreach ($line in Get-Content -LiteralPath $report) {
        if ($line -match '^IMPORT (\S+) (.+)!(\S+)$') {
            $asm=$Matches[1];$module=$Matches[2];$symbol=$Matches[3]
            $lib=if($module -match '^(?:lib)?(?:sdl2|fna3d|faudio|theorafile)(?:\.dll|\.so|\.dylib)?$'){'CelesteNative'}elseif($module -match '^(?:lib)?fmod(?:studio)?(?:64)?(?:\.dll|\.so|\.dylib)?$'){'FMODLocal'}else{$null}
            if(!$lib){ "UNMAPPED $asm $module!$symbol" }
            elseif(!$symbols[$lib].Contains($symbol)){ "MISSING_EXPORT $asm $module!$symbol" }
        }
    })
    @('=== Local native export check (candidate names only) ===')+$summary | Tee-Object -FilePath $report -Append
} else {
    'SKIP: llvm-nm unavailable; native export coverage not established' | Tee-Object -FilePath $report -Append
}
Write-Host "Report: $report"
