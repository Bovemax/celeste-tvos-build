"""Resolve tools and child-process caches relative to the portable toolkit."""
import os
from pathlib import Path
import shutil
import sys

def root():
    return Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else Path(__file__).resolve().parent.parent

def environment():
    env = dict(os.environ)
    base = root() / "toolchain"
    cache = base / "cache"
    locations = {
        "DOTNET_CLI_HOME": cache / "dotnet-home",
        "NUGET_PACKAGES": cache / "nuget" / "packages",
        "NUGET_HTTP_CACHE_PATH": cache / "nuget" / "http",
        "NUGET_PLUGINS_CACHE_PATH": cache / "nuget" / "plugins",
        "NUGET_SCRATCH": cache / "nuget" / "scratch",
        "TEMP": cache / "temp",
        "TMP": cache / "temp",
        "TMPDIR": cache / "temp",
        "HOME": cache / "home",
        "USERPROFILE": cache / "home",
        "APPDATA": cache / "home" / "AppData" / "Roaming",
        "LOCALAPPDATA": cache / "home" / "AppData" / "Local",
        "XDG_CONFIG_HOME": cache / "config",
        "XDG_CACHE_HOME": cache / "xdg",
    }
    for name, path in locations.items():
        path.mkdir(parents=True, exist_ok=True)
        env[name] = str(path)
    env["GIT_CONFIG_NOSYSTEM"] = "1"
    env["GIT_CONFIG_GLOBAL"] = str(cache / "home" / ".gitconfig")
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true"
    env["DOTNET_NOLOGO"] = "1"
    env["DOTNET_ROLL_FORWARD"] = "Major"
    env["MSBUILDDISABLENODEREUSE"] = "1"
    dirs = [base / "dotnet", base / "ilspy", base / "git" / "cmd", base / "llvm" / "bin"]
    env["PATH"] = os.pathsep.join([str(p) for p in dirs if p.is_dir()] + [env.get("PATH", "")])
    if (base / "dotnet" / "dotnet.exe").is_file():
        env["DOTNET_ROOT"] = str(base / "dotnet")
        env["DOTNET_ROOT_X64"] = str(base / "dotnet")
        env["DOTNET_MULTILEVEL_LOOKUP"] = "0"
    return env

def find(name):
    # Do not silently use arbitrary system versions in the packaged Windows build.
    base = root() / "toolchain"
    dirs = [base / "dotnet", base / "ilspy", base / "git" / "cmd", base / "llvm" / "bin"]
    if getattr(sys, "frozen", False):
        suffix = ".exe" if os.name == "nt" else ""
        for directory in dirs:
            candidate = directory / (name + suffix)
            if candidate.is_file():
                return str(candidate)
        return None
    return shutil.which(name, path=environment()["PATH"])
