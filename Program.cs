using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace Launcher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        string baseDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

        // 1. Elevated worker check
        if (args.Length > 0 && args[0] == "--elevated-install")
        {
            InstallerWorker.ExecuteFromArgs(args);
            return;
        }

        // 2. Direct Boot check
        string settingsFile = Path.Combine(baseDir, "Engine", "User", "launcher_settings.json");
        bool skipLauncher = false;

        if (File.Exists(settingsFile))
        {
            try
            {
                var doc = JsonNode.Parse(File.ReadAllText(settingsFile));
                skipLauncher = doc?["SkipLauncher"]?.GetValue<bool>() ?? false;
            }
            catch { }
        }

        // Safety Hatch: Holding SHIFT forces the launcher to show
        bool isShiftHeld = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;

        if (skipLauncher && !isShiftHeld && !args.Contains("-config"))
        {
            if (MainForm.LaunchGame(baseDir))
            {
                return;
            }
        }

        Application.Run(new MainForm());
    }
}