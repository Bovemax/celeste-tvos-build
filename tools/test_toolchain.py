import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import toolchain

class PortableToolchainTests(unittest.TestCase):
    def test_caches_local_and_parent_unchanged(self):
        with tempfile.TemporaryDirectory(prefix="portable path with spaces ") as folder:
            root = Path(folder)
            previous = dict(os.environ)
            with patch.object(toolchain, "root", return_value=root):
                env = toolchain.environment()
            for key in ("DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH",
                        "NUGET_PLUGINS_CACHE_PATH", "NUGET_SCRATCH", "TEMP", "TMP",
                        "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "GIT_CONFIG_GLOBAL"):
                self.assertTrue(Path(env[key]).is_relative_to(root), key)
            self.assertEqual(dict(os.environ), previous)

    def test_packaged_tool_never_falls_back_to_path(self):
        with tempfile.TemporaryDirectory(prefix="portable path with spaces ") as folder:
            root = Path(folder)
            with patch.object(toolchain, "root", return_value=root), patch.object(toolchain.sys, "frozen", True, create=True):
                self.assertIsNone(toolchain.find("git"))
                git = root / "toolchain" / "git" / "cmd" / ("git.exe" if os.name == "nt" else "git")
                git.parent.mkdir(parents=True)
                git.touch()
                self.assertEqual(toolchain.find("git"), str(git))
