"""Find the portable Windows dependencies beside the toolkit, before PATH."""
import os
from pathlib import Path
import shutil
import sys

def root():
    return Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else Path(__file__).resolve().parent.parent

def environment():
    env = dict(os.environ)
    base = root() / "toolchain"
    dirs = [base / "dotnet", base / "ilspy", base / "git" / "cmd", base / "llvm" / "bin"]
    env["PATH"] = os.pathsep.join([str(p) for p in dirs if p.is_dir()] + [env.get("PATH", "")])
    if (base / "dotnet" / "dotnet.exe").is_file():
        env["DOTNET_ROOT"] = str(base / "dotnet")
        env["DOTNET_ROOT_X64"] = str(base / "dotnet")
        env["DOTNET_MULTILEVEL_LOOKUP"] = "0"
    env["DOTNET_ROLL_FORWARD"] = "Major"
    return env

def find(name):
    return shutil.which(name, path=environment()["PATH"])
