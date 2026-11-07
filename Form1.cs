using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Launcher;

public partial class MainForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static readonly string CancelFlagPath = Path.Combine(Path.GetTempPath(), "catherine_cancel.flag");

    private string baseDir;
    private string engineDir;
    private string dataDir;
    private string gameDir;
    private string patchXmlPath;
    private string configJsonPath;
    private string launcherSettingsPath;
    private string inputConfigDir;

    // Layout
    private Panel pnlUI = null!;
    private PictureBox pbArt = null!;

    // Views
    private Panel pnlLauncher = null!;
    private Panel pnlInstaller = null!;
    private Panel pnlKeybinds = null!;

    // UI Elements
    private Button btnPlay = null!;
    private CheckBox chkSkipIntro = null!;
    private CheckBox chkSkipLauncher = null!;
    private Button btnOpenKeybinds = null!;
    private Button btnCreateShortcut = null!;
    private Button btnShowInstall = null!;

    private TextBox txtInstallDir = null!;
    private TextBox txtBasePkg = null!;
    private TextBox txtPatchPkg = null!;
    private TextBox txtDlcFolder = null!;
    private Button btnStartInstall = null!;
    private Button btnCancelInstall = null!;
    private Label lblInstallStatus = null!;
    private ProgressBar progressBar = null!;
    private CancellationTokenSource? extractionCts;

    // Keybind State
    private readonly Dictionary<string, string> keybinds = new();
    private readonly Dictionary<string, Button> keybindButtons = new();
    private string? activeRebindAction = null;

    public MainForm()
    {
        string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        baseDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

        engineDir = Path.Combine(baseDir, "Engine");
        dataDir = Path.Combine(baseDir, "Data");
        gameDir = Path.Combine(dataDir, "CUSA14836");
        patchXmlPath = Path.Combine(engineDir, "User", "patches", "shadPS4", "Catherine_FullBody.xml");
        configJsonPath = Path.Combine(engineDir, "User", "config.json");
        launcherSettingsPath = Path.Combine(engineDir, "User", "launcher_settings.json");
        inputConfigDir = Path.Combine(engineDir, "User", "input_config");

        ResetDefaultKeybindsInMemory();
        InitializeModernLayout();
        ApplyDarkTitleBar();
        RefreshViewState();
        LoadKeybindsFromDisk();

        PlaySplashIntro();
    }

    private void ApplyDarkTitleBar()
    {
        try { int darkMode = 1; DwmSetWindowAttribute(Handle, 20, ref darkMode, sizeof(int)); } catch { }
    }

    // Intercepts ALL keyboard keys (including Space, Enter, and Arrow Keys) when rebinding!
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (activeRebindAction != null)
        {
            Keys keyOnly = keyData & Keys.KeyCode;
            if (keyOnly != Keys.Escape)
            {
                string mapped = MapKeyToShadPs4(keyOnly);
                if (!string.IsNullOrEmpty(mapped))
                {
                    keybinds[activeRebindAction] = mapped;
                    SaveKeybindsToDisk();
                }
            }

            string action = activeRebindAction;
            activeRebindAction = null;
            if (keybindButtons.TryGetValue(action, out var btn))
            {
                btn.Text = FormatKeyForDisplay(keybinds[action]);
                btn.BackColor = Color.FromArgb(40, 40, 48);
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void InitializeModernLayout()
    {
        Text = "Catherine: Full Body";
        DoubleBuffered = true;
        ClientSize = new Size(887, 500); 
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 18, 21);
        ForeColor = Color.White;

        // 1. Art on the Left: Exact 500x500 square at (0, 0)
        pbArt = new PictureBox 
        { 
            Location = new Point(0, 0), 
            Size = new Size(500, 500), 
            SizeMode = PictureBoxSizeMode.Zoom, 
            BackColor = Color.FromArgb(18, 18, 21) 
        };

        // 2. UI on the Right: Starts at X=500, 387px width
        pnlUI = new Panel 
        { 
            Location = new Point(500, 0), 
            Size = new Size(387, 500), 
            BackColor = Color.FromArgb(18, 18, 21) 
        };

        var assembly = Assembly.GetExecutingAssembly();
        string? bgName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("bg.jpg"));
        if (bgName != null)
        {
            using var bgStream = assembly.GetManifestResourceStream(bgName);
            if (bgStream != null)
            {
                using var original = Image.FromStream(bgStream);
                pbArt.Image = new Bitmap(original, new Size(500, 500));
            }
        }

        Controls.Add(pbArt);
        Controls.Add(pnlUI);

        // =========================================================
        // 1. SETUP / INSTALLER VIEW
        // =========================================================
        pnlInstaller = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.Transparent };

        var lblInstallHeader = new Label { Text = "SETUP & INSTALLATION", Font = new Font("Segoe UI", 15, FontStyle.Bold), ForeColor = Color.FromArgb(241, 42, 161), Location = new Point(18, 20), AutoSize = true };
        var lblInstallSub = new Label { Text = "Select your dumped PS4 files to prepare the PC edition.", Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(20, 48), AutoSize = true };

        var pnlCardInstall = CreateCardPanel(18, 78, 350, 240);

        var lblDest = new Label { Text = "Install Destination Folder:", Location = new Point(14, 12), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(229, 231, 235) };
        txtInstallDir = CreateStyledTextBox(@"C:\Games\Catherine Full Body", 14, 32, 236);
        var btnBrowseDest = CreateSecondaryButton("Browse...", 258, 31, 78, 28);
        btnBrowseDest.Click += (s, e) => {
            string initPath = txtInstallDir.Text.Trim();
            while (!string.IsNullOrEmpty(initPath) && !Directory.Exists(initPath)) initPath = Path.GetDirectoryName(initPath) ?? "";
            using var fbd = new FolderBrowserDialog { SelectedPath = string.IsNullOrEmpty(initPath) ? @"C:\" : initPath };
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                string chosen = fbd.SelectedPath.TrimEnd('\\', '/');
                if (!chosen.EndsWith("Catherine Full Body", StringComparison.OrdinalIgnoreCase)) chosen = Path.Combine(chosen, "Catherine Full Body");
                txtInstallDir.Text = chosen;
            }
        };

        var lblBase = new Label { Text = "Base Game PKG (*.pkg) [Required]:", Location = new Point(14, 68), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(229, 231, 235) };
        txtBasePkg = CreateStyledTextBox("", 14, 88, 236);
        var btnBrowseBase = CreateSecondaryButton("Browse...", 258, 87, 78, 28);
        btnBrowseBase.Click += (s, e) => {
            using var ofd = new OpenFileDialog { Filter = "PS4 PKG Files (*.pkg)|*.pkg" };
            if (ofd.ShowDialog() == DialogResult.OK) txtBasePkg.Text = ofd.FileName;
        };

        var lblPatch = new Label { Text = "Update / Patch PKG (*.pkg) [Optional]:", Location = new Point(14, 124), AutoSize = true, Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(156, 163, 175) };
        txtPatchPkg = CreateStyledTextBox("", 14, 144, 236);
        var btnBrowsePatch = CreateSecondaryButton("Browse...", 258, 143, 78, 28);
        btnBrowsePatch.Click += (s, e) => {
            using var ofd = new OpenFileDialog { Filter = "PS4 PKG Files (*.pkg)|*.pkg" };
            if (ofd.ShowDialog() == DialogResult.OK) txtPatchPkg.Text = ofd.FileName;
        };

        var lblDlc = new Label { Text = "DLC Folder containing *.pkg files [Optional]:", Location = new Point(14, 180), AutoSize = true, Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(156, 163, 175) };
        txtDlcFolder = CreateStyledTextBox("", 14, 200, 236);
        var btnBrowseDlc = CreateSecondaryButton("Browse...", 258, 199, 78, 28);
        btnBrowseDlc.Click += (s, e) => {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK) txtDlcFolder.Text = fbd.SelectedPath;
        };

        pnlCardInstall.Controls.AddRange(new Control[] { lblDest, txtInstallDir, btnBrowseDest, lblBase, txtBasePkg, btnBrowseBase, lblPatch, txtPatchPkg, btnBrowsePatch, lblDlc, txtDlcFolder, btnBrowseDlc });

        btnStartInstall = CreatePrimaryButton("START INSTALLATION", 18, 335, 350, 48);
        btnStartInstall.Click += async (s, e) => await StartInstallationProcessAsync();

        btnCancelInstall = CreateSecondaryButton("Cancel", 268, 335, 100, 48);
        btnCancelInstall.Visible = false;
        btnCancelInstall.Click += (s, e) => {
            if (MessageBox.Show("Are you sure you want to cancel the installation?", "Cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { File.WriteAllText(CancelFlagPath, "cancel"); } catch { }
                extractionCts?.Cancel();
                lblInstallStatus.Text = "Cancelling and cleaning up...";
                btnCancelInstall.Enabled = false;
            }
        };

        lblInstallStatus = new Label { Text = "Ready to install.", Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(20, 395), Size = new Size(350, 25) };
        progressBar = new ProgressBar { Location = new Point(18, 425), Size = new Size(350, 16), Style = ProgressBarStyle.Marquee, Visible = false };

        pnlInstaller.Controls.AddRange(new Control[] { lblInstallHeader, lblInstallSub, pnlCardInstall, btnStartInstall, btnCancelInstall, lblInstallStatus, progressBar });

        // =========================================================
        // 2. MAIN LAUNCHER VIEW
        // =========================================================
        pnlLauncher = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.Transparent };

        var lblMainTitle = new Label { Text = "CATHERINE", Font = new Font("Segoe UI Black", 26, FontStyle.Bold), ForeColor = Color.FromArgb(241, 42, 161), Location = new Point(15, 18), AutoSize = true };
        var lblMainSub = new Label { Text = "FULL BODY  •  PC EDITION", Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(255, 140, 190), Location = new Point(22, 62), AutoSize = true };

        btnPlay = CreatePrimaryButton("▶   PLAY GAME", 18, 95, 350, 60);
        btnPlay.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        btnPlay.Click += (s, e) => { SaveLauncherSetting(); LaunchGame(baseDir); Close(); };

        var pnlCardOptions = CreateCardPanel(18, 172, 350, 90);
        chkSkipIntro = new CheckBox { Text = "Skip Intro Logos (Direct to Title)", Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.FromArgb(243, 244, 246), Location = new Point(16, 18), AutoSize = true };
        chkSkipIntro.CheckedChanged += (s, e) => SavePatchSetting();
        chkSkipLauncher = new CheckBox { Text = "Launch directly next time (Hold Shift)", Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(16, 52), AutoSize = true };
        chkSkipLauncher.CheckedChanged += (s, e) => SaveLauncherSetting();
        pnlCardOptions.Controls.Add(chkSkipIntro);
        pnlCardOptions.Controls.Add(chkSkipLauncher);

        btnOpenKeybinds = CreateSecondaryButton("⌨  Customize Keyboard Controls", 18, 276, 350, 40);
        btnOpenKeybinds.Click += (s, e) => {
            LoadKeybindsFromDisk();
            ShowPanel(pnlKeybinds);
        };

        btnCreateShortcut = CreateSecondaryButton("📌  Desktop Shortcut", 18, 326, 170, 40);
        btnCreateShortcut.Click += (s, e) => {
            InstallerWorker.CreateDesktopShortcut(Path.Combine(baseDir, "Catherine.exe"), "Catherine - Full Body");
            MessageBox.Show("Desktop shortcut created successfully!", "Shortcut", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        btnShowInstall = CreateSecondaryButton("⚙  Reinstall / DLC", 198, 326, 170, 40);
        btnShowInstall.Click += (s, e) => ShowPanel(pnlInstaller);

        var lblFooter = new Label { Text = "Controller Supported • Press Alt+F4 to Exit", Font = new Font("Segoe UI", 8), ForeColor = Color.FromArgb(107, 114, 128), Location = new Point(18, 460), AutoSize = true };

        pnlLauncher.Controls.AddRange(new Control[] { lblMainTitle, lblMainSub, btnPlay, pnlCardOptions, btnOpenKeybinds, btnCreateShortcut, btnShowInstall, lblFooter });

        // =========================================================
        // 3. KEYBOARD REBIND VIEW (With Primary + Alt/Numpad Columns!)
        // =========================================================
        pnlKeybinds = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.Transparent };

        var lblKeyTitle = new Label { Text = "KEYBOARD CONTROLS", Font = new Font("Segoe UI", 15, FontStyle.Bold), ForeColor = Color.FromArgb(241, 42, 161), Location = new Point(18, 18), AutoSize = true };
        var lblKeySub = new Label { Text = "Click any slot (Main or Alt/Numpad), then press a key.", Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(156, 163, 175), Location = new Point(20, 45), AutoSize = true };

        var pnlKeyList = CreateCardPanel(18, 72, 350, 350);
        pnlKeyList.AutoScroll = true;

        var bindDefinitions = new (string KeyId, string? AltKeyId, string LabelText)[]
        {
            ("cross", "cross_alt", "Confirm / Climb (✕)"),
            ("circle", "circle_alt", "Cancel / Back (○)"),
            ("square", "square_alt", "Grab / Push (□)"),
            ("triangle", "triangle_alt", "Item / Phone (△)"),
            ("l1", null, "Undo / Rewind (L1)"),
            ("r1", null, "Camera / Auto (R1)"),
            ("l2", null, "Rotate Left (L2)"),
            ("r2", null, "Rotate Right (R2)"),
            ("axis_left_y_minus", null, "Move Up (Stick)"),
            ("axis_left_y_plus", null, "Move Down (Stick)"),
            ("axis_left_x_minus", null, "Move Left (Stick)"),
            ("axis_left_x_plus", null, "Move Right (Stick)"),
            ("pad_up", null, "D-Pad Up"),
            ("pad_down", null, "D-Pad Down"),
            ("pad_left", null, "D-Pad Left"),
            ("pad_right", null, "D-Pad Right"),
            ("options", null, "Pause / Skip (Start)"),
            ("touchpad_center", null, "Touchpad Click")
        };

        int rowY = 12;
        foreach (var (keyId, altKeyId, labelText) in bindDefinitions)
        {
            var lbl = new Label
            {
                Text = labelText,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(229, 231, 235),
                Location = new Point(10, rowY + 5),
                AutoSize = true
            };
            pnlKeyList.Controls.Add(lbl);

            if (altKeyId != null)
            {
                // Two side-by-side buttons: [Main Key] and [Numpad / Alt Key]
                var btnMain = CreateRebindButton(keyId, 170, rowY, 72);
                var btnAlt = CreateRebindButton(altKeyId, 248, rowY, 72);
                pnlKeyList.Controls.Add(btnMain);
                pnlKeyList.Controls.Add(btnAlt);
            }
            else
            {
                var btnSingle = CreateRebindButton(keyId, 170, rowY, 150);
                pnlKeyList.Controls.Add(btnSingle);
            }

            rowY += 36;
        }

        var btnBackFromKeys = CreatePrimaryButton("←  DONE", 18, 435, 220, 42);
        btnBackFromKeys.Click += (s, e) =>
        {
            activeRebindAction = null;
            SaveKeybindsToDisk();
            ShowPanel(pnlLauncher);
        };

        var btnResetKeys = CreateSecondaryButton("Reset Defaults", 248, 435, 120, 42);
        btnResetKeys.Click += (s, e) =>
        {
            activeRebindAction = null;
            ResetDefaultKeybindsInMemory();
            SaveKeybindsToDisk();
            RefreshKeybindButtonLabels();
        };

        pnlKeybinds.Controls.AddRange(new Control[] { lblKeyTitle, lblKeySub, pnlKeyList, btnBackFromKeys, btnResetKeys });

        pnlUI.Controls.Add(pnlInstaller);
        pnlUI.Controls.Add(pnlLauncher);
        pnlUI.Controls.Add(pnlKeybinds);
    }

    private Button CreateRebindButton(string capturedId, int x, int y, int width)
    {
        var btn = new Button
        {
            Text = FormatKeyForDisplay(keybinds[capturedId]),
            Location = new Point(x, y),
            Size = new Size(width, 28),
            BackColor = Color.FromArgb(40, 40, 48),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = Color.FromArgb(63, 63, 70);

        btn.Click += (s, e) =>
        {
            if (activeRebindAction != null && keybindButtons.TryGetValue(activeRebindAction, out var prevBtn))
            {
                prevBtn.Text = FormatKeyForDisplay(keybinds[activeRebindAction]);
                prevBtn.BackColor = Color.FromArgb(40, 40, 48);
            }

            activeRebindAction = capturedId;
            btn.Text = "Press...";
            btn.BackColor = Color.FromArgb(241, 42, 161);
        };

        keybindButtons[capturedId] = btn;
        return btn;
    }

    // --- Keybind Management ---

    private void ResetDefaultKeybindsInMemory()
    {
        // Primary keyboard face buttons
        keybinds["triangle"] = "c";
        keybinds["circle"] = "b";
        keybinds["cross"] = "n";
        keybinds["square"] = "v";

        // Secondary / Numpad face buttons
        keybinds["triangle_alt"] = "kp8";
        keybinds["circle_alt"] = "kp6";
        keybinds["cross_alt"] = "kp2";
        keybinds["square_alt"] = "kp4";

        keybinds["l1"] = "q";
        keybinds["r1"] = "u";
        keybinds["l2"] = "e";
        keybinds["r2"] = "o";
        keybinds["l3"] = "x";
        keybinds["r3"] = "m";
        keybinds["options"] = "enter";
        keybinds["touchpad_center"] = "space";
        keybinds["pad_up"] = "up";
        keybinds["pad_down"] = "down";
        keybinds["pad_left"] = "left";
        keybinds["pad_right"] = "right";
        keybinds["axis_left_x_minus"] = "a";
        keybinds["axis_left_x_plus"] = "d";
        keybinds["axis_left_y_minus"] = "w";
        keybinds["axis_left_y_plus"] = "s";
        keybinds["axis_right_x_minus"] = "j";
        keybinds["axis_right_x_plus"] = "l";
        keybinds["axis_right_y_minus"] = "i";
        keybinds["axis_right_y_plus"] = "k";
    }

    private void RefreshKeybindButtonLabels()
    {
        foreach (var kvp in keybindButtons)
        {
            if (keybinds.TryGetValue(kvp.Key, out string? val))
            {
                kvp.Value.Text = FormatKeyForDisplay(val);
                kvp.Value.BackColor = Color.FromArgb(40, 40, 48);
            }
        }
    }

    private void LoadKeybindsFromDisk()
    {
        try
        {
            string file = Path.Combine(inputConfigDir, "default.ini");
            if (!File.Exists(file)) file = Path.Combine(inputConfigDir, "global.ini");
            if (!File.Exists(file)) return;

            var seenFaceButtons = new HashSet<string>();

            foreach (string rawLine in File.ReadAllLines(file))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("# Controller bindings", StringComparison.OrdinalIgnoreCase))
                    break;

                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();

                if (k is "triangle" or "circle" or "cross" or "square")
                {
                    // First occurrence in default.ini is the Numpad/Alt bind, second is the Main bind!
                    if (seenFaceButtons.Add(k))
                        keybinds[k + "_alt"] = v;
                    else
                        keybinds[k] = v;
                }
                else if (keybinds.ContainsKey(k))
                {
                    keybinds[k] = v;
                }
            }
            RefreshKeybindButtonLabels();
        }
        catch { }
    }

    private void SaveKeybindsToDisk()
    {
        try
        {
            Directory.CreateDirectory(inputConfigDir);
            var sb = new StringBuilder();
            sb.AppendLine("#Feeling lost? Check out the Help section!");
            sb.AppendLine();
            sb.AppendLine("# Keyboard bindings");
            sb.AppendLine();
            sb.AppendLine($"triangle = {keybinds["triangle_alt"]}");
            sb.AppendLine($"circle = {keybinds["circle_alt"]}");
            sb.AppendLine($"cross = {keybinds["cross_alt"]}");
            sb.AppendLine($"square = {keybinds["square_alt"]}");
            sb.AppendLine("# Alternatives for users without a keypad");
            sb.AppendLine($"triangle = {keybinds["triangle"]}");
            sb.AppendLine($"circle = {keybinds["circle"]}");
            sb.AppendLine($"cross = {keybinds["cross"]}");
            sb.AppendLine($"square = {keybinds["square"]}");
            sb.AppendLine();
            sb.AppendLine($"l1 = {keybinds["l1"]}");
            sb.AppendLine($"r1 = {keybinds["r1"]}");
            sb.AppendLine($"l2 = {keybinds["l2"]}");
            sb.AppendLine($"r2 = {keybinds["r2"]}");
            sb.AppendLine($"l3 = {keybinds["l3"]}");
            sb.AppendLine($"r3 = {keybinds["r3"]}");
            sb.AppendLine();
            sb.AppendLine($"options = {keybinds["options"]}");
            sb.AppendLine($"touchpad_center = {keybinds["touchpad_center"]}");
            sb.AppendLine();
            sb.AppendLine($"pad_up = {keybinds["pad_up"]}");
            sb.AppendLine($"pad_down = {keybinds["pad_down"]}");
            sb.AppendLine($"pad_left = {keybinds["pad_left"]}");
            sb.AppendLine($"pad_right = {keybinds["pad_right"]}");
            sb.AppendLine();
            sb.AppendLine($"axis_left_x_minus = {keybinds["axis_left_x_minus"]}");
            sb.AppendLine($"axis_left_x_plus = {keybinds["axis_left_x_plus"]}");
            sb.AppendLine($"axis_left_y_minus = {keybinds["axis_left_y_minus"]}");
            sb.AppendLine($"axis_left_y_plus = {keybinds["axis_left_y_plus"]}");
            sb.AppendLine();
            sb.AppendLine($"axis_right_x_minus = {keybinds["axis_right_x_minus"]}");
            sb.AppendLine($"axis_right_x_plus = {keybinds["axis_right_x_plus"]}");
            sb.AppendLine($"axis_right_y_minus = {keybinds["axis_right_y_minus"]}");
            sb.AppendLine($"axis_right_y_plus = {keybinds["axis_right_y_plus"]}");
            sb.AppendLine();
            sb.AppendLine("# Controller bindings");
            sb.AppendLine();
            sb.AppendLine("triangle = triangle");
            sb.AppendLine("cross = cross");
            sb.AppendLine("square = square");
            sb.AppendLine("circle = circle");
            sb.AppendLine();
            sb.AppendLine("l1 = l1");
            sb.AppendLine("l2 = l2");
            sb.AppendLine("l3 = l3");
            sb.AppendLine("r1 = r1");
            sb.AppendLine("r2 = r2");
            sb.AppendLine("r3 = r3");
            sb.AppendLine();
            sb.AppendLine("options = options");
            sb.AppendLine("touchpad_center = back");
            sb.AppendLine();
            sb.AppendLine("pad_up = pad_up");
            sb.AppendLine("pad_down = pad_down");
            sb.AppendLine("pad_left = pad_left");
            sb.AppendLine("pad_right = pad_right");
            sb.AppendLine();
            sb.AppendLine("axis_left_x = axis_left_x");
            sb.AppendLine("axis_left_y = axis_left_y");
            sb.AppendLine("axis_right_x = axis_right_x");
            sb.AppendLine("axis_right_y = axis_right_y");
            sb.AppendLine();
            sb.AppendLine("# Range of deadzones: 1 (almost none) to 127 (max)");
            sb.AppendLine("analog_deadzone = leftjoystick, 5, 127");
            sb.AppendLine("analog_deadzone = rightjoystick, 5, 127");
            sb.AppendLine();
            sb.AppendLine("override_controller_color = false, 0, 0, 255");

            string content = sb.ToString();
            File.WriteAllText(Path.Combine(inputConfigDir, "default.ini"), content);
            File.WriteAllText(Path.Combine(inputConfigDir, "global.ini"), content);
            File.WriteAllText(Path.Combine(inputConfigDir, "CUSA14836.ini"), content);
        }
        catch { }
    }

    private static string FormatKeyForDisplay(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string lower = raw.Trim().ToLowerInvariant();

        // Translates "kp0"-"kp9" into "NUM 0"-"NUM 9" for the UI!
        if (lower.StartsWith("kp") && lower.Length == 3 && char.IsDigit(lower[2]))
        {
            return "NUM " + lower[2];
        }

        return lower switch
        {
            "lshift" => "L-SHIFT",
            "rshift" => "R-SHIFT",
            "lctrl" => "L-CTRL",
            "rctrl" => "R-CTRL",
            "lalt" => "L-ALT",
            "ralt" => "R-ALT",
            "backspace" => "BACK",
            _ => raw.ToUpperInvariant()
        };
    }

    private static string MapKeyToShadPs4(Keys key)
    {
        if (key >= Keys.A && key <= Keys.Z) return key.ToString().ToLowerInvariant();
        if (key >= Keys.D0 && key <= Keys.D9) return ((int)(key - Keys.D0)).ToString();
        if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "kp" + ((int)(key - Keys.NumPad0));

        return key switch
        {
            Keys.Return => "enter",
            Keys.Space => "space",
            Keys.Back => "backspace",
            Keys.Tab => "tab",
            Keys.Up => "up",
            Keys.Down => "down",
            Keys.Left => "left",
            Keys.Right => "right",
            Keys.ShiftKey or Keys.LShiftKey => "lshift",
            Keys.RShiftKey => "rshift",
            Keys.ControlKey or Keys.LControlKey => "lctrl",
            Keys.RControlKey => "rctrl",
            Keys.Menu or Keys.LMenu => "lalt",
            Keys.RMenu => "ralt",
            _ => key.ToString().ToLowerInvariant()
        };
    }

    // --- Splash Intro & Vee Wipe ---

    private static void EnsureHandlesCreated(Control parent)
    {
        if (!parent.Visible) return;
        IntPtr _ = parent.Handle;
        foreach (Control child in parent.Controls)
        {
            EnsureHandlesCreated(child);
        }
    }

    private void PlaySplashIntro()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? mp4Name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("intro.mp4"));
        if (mp4Name == null) return;

        try
        {
            string tempVideoPath = Path.Combine(Path.GetTempPath(), "catherine_intro.mp4");
            using (var rawStream = assembly.GetManifestResourceStream(mp4Name))
            {
                if (rawStream == null) return;
                using var fs = new FileStream(tempVideoPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                rawStream.CopyTo(fs);
            }

            var wpfUiImage = new System.Windows.Controls.Image
            {
                Width = 887,
                Height = 500,
                Stretch = System.Windows.Media.Stretch.Fill,
                Visibility = System.Windows.Visibility.Hidden
            };

            var host = new System.Windows.Forms.Integration.ElementHost
            {
                Location = new Point(0, 0),
                Size = new Size(887, 500),
                BackColor = Color.Black
            };

            var grid = new System.Windows.Controls.Grid
            {
                Width = 887,
                Height = 500,
                Background = System.Windows.Media.Brushes.Black
            };

            var mediaPlayer = new System.Windows.Controls.MediaElement
            {
                Width = 887,
                Height = 500,
                LoadedBehavior = System.Windows.Controls.MediaState.Manual,
                UnloadedBehavior = System.Windows.Controls.MediaState.Manual,
                Stretch = System.Windows.Media.Stretch.Fill,
                Source = new Uri(tempVideoPath, UriKind.Absolute)
            };

            var veeBorder = new System.Windows.Shapes.Polyline
            {
                Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 42, 161)),
                StrokeThickness = 14,
                StrokeLineJoin = System.Windows.Media.PenLineJoin.Miter,
                Visibility = System.Windows.Visibility.Collapsed
            };

            grid.Children.Add(wpfUiImage);
            grid.Children.Add(mediaPlayer);
            grid.Children.Add(veeBorder);

            host.Child = grid;
            Controls.Add(host);
            host.BringToFront();

            bool isWiping = false;
            bool isFinished = false;
            var wipeTimer = new System.Windows.Forms.Timer { Interval = 15 };
            double wipeProgress = 0.0;

            Action finishIntro = () =>
            {
                if (isFinished) return;
                isFinished = true;
                wipeTimer.Stop();
                wipeTimer.Dispose();

                Controls.Remove(host);
                this.Refresh();

                try { mediaPlayer.Stop(); } catch { }
                try { mediaPlayer.Close(); } catch { }
                host.Dispose();
            };

            Action startVeeWipe = () =>
            {
                if (isWiping || isFinished) return;
                isWiping = true;

                EnsureHandlesCreated(pbArt);
                EnsureHandlesCreated(pnlUI);

                using (var uiBitmap = new Bitmap(887, 500))
                {
                    pbArt.DrawToBitmap(uiBitmap, new Rectangle(0, 0, 500, 500));
                    pnlUI.DrawToBitmap(uiBitmap, new Rectangle(500, 0, 387, 500));

                    using var ms = new MemoryStream();
                    uiBitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    var bmpImg = new System.Windows.Media.Imaging.BitmapImage();
                    bmpImg.BeginInit();
                    bmpImg.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmpImg.StreamSource = ms;
                    bmpImg.EndInit();
                    bmpImg.Freeze();
                    wpfUiImage.Source = bmpImg;
                }

                wpfUiImage.Visibility = System.Windows.Visibility.Visible;
                veeBorder.Visibility = System.Windows.Visibility.Visible;
                wipeTimer.Start();
            };

            wipeTimer.Tick += (s, e) =>
            {
                wipeProgress += 0.032;
                if (wipeProgress >= 1.0)
                {
                    finishIntro();
                    return;
                }

                const double slant = 340.0;
                double tipX = wipeProgress * (887.0 + slant);
                double edgeX = tipX - slant;

                var figure = new System.Windows.Media.PathFigure { StartPoint = new System.Windows.Point(edgeX, 0), IsClosed = true };
                figure.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(887, 0), false));
                figure.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(887, 500), false));
                figure.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(edgeX, 500), false));
                figure.Segments.Add(new System.Windows.Media.LineSegment(new System.Windows.Point(tipX, 250), false));

                var geo = new System.Windows.Media.PathGeometry();
                geo.Figures.Add(figure);
                mediaPlayer.Clip = geo;

                veeBorder.Points = new System.Windows.Media.PointCollection
                {
                    new System.Windows.Point(edgeX, -10),
                    new System.Windows.Point(tipX, 250),
                    new System.Windows.Point(edgeX, 510)
                };
            };

            var checkPositionTimer = new System.Windows.Forms.Timer { Interval = 50 };
            checkPositionTimer.Tick += (s, e) =>
            {
                if (isWiping || isFinished) { checkPositionTimer.Stop(); checkPositionTimer.Dispose(); return; }
                if (mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    var remaining = mediaPlayer.NaturalDuration.TimeSpan - mediaPlayer.Position;
                    if (remaining.TotalMilliseconds <= 550)
                    {
                        checkPositionTimer.Stop();
                        checkPositionTimer.Dispose();
                        startVeeWipe();
                    }
                }
            };
            checkPositionTimer.Start();

            mediaPlayer.MediaEnded += (s, e) => BeginInvoke(startVeeWipe);
            mediaPlayer.MediaFailed += (s, e) => BeginInvoke(finishIntro);
            mediaPlayer.MouseDown += (s, e) => BeginInvoke(finishIntro);

            mediaPlayer.Play();
        }
        catch
        {
            RefreshViewState();
        }
    }

    private Panel CreateCardPanel(int x, int y, int width, int height)
    {
        return new Panel { Location = new Point(x, y), Size = new Size(width, height), BackColor = Color.FromArgb(28, 28, 34), BorderStyle = BorderStyle.None };
    }

    private TextBox CreateStyledTextBox(string text, int x, int y, int width)
    {
        return new TextBox { Text = text, Location = new Point(x, y), Width = width, BackColor = Color.FromArgb(40, 40, 48), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f) };
    }

    private Button CreatePrimaryButton(string text, int x, int y, int width, int height)
    {
        var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, height), BackColor = Color.FromArgb(241, 42, 161), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 12, FontStyle.Bold), Cursor = Cursors.Hand };
        btn.FlatAppearance.BorderSize = 0;
        btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(255, 75, 185);
        btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(241, 42, 161);
        return btn;
    }

    private Button CreateSecondaryButton(string text, int x, int y, int width, int height)
    {
        var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, height), BackColor = Color.FromArgb(39, 39, 48), ForeColor = Color.FromArgb(229, 231, 235), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f, FontStyle.Regular), Cursor = Cursors.Hand };
        btn.FlatAppearance.BorderSize = 0;
        btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(55, 55, 66);
        btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(39, 39, 48);
        return btn;
    }

    private void RefreshViewState()
    {
        bool isInstalled = File.Exists(Path.Combine(gameDir, "eboot.bin"));
        ShowPanel(isInstalled ? pnlLauncher : pnlInstaller);
        LoadCurrentSettings();
    }

    private void ShowPanel(Panel target)
    {
        pnlInstaller.Visible = (target == pnlInstaller);
        pnlLauncher.Visible = (target == pnlLauncher);
        pnlKeybinds.Visible = (target == pnlKeybinds);
    }

    private async Task StartInstallationProcessAsync()
    {
        string targetDir = txtInstallDir.Text.Trim();
        string basePkg = txtBasePkg.Text.Trim();
        string patchPkg = txtPatchPkg.Text.Trim();
        string dlcFolder = txtDlcFolder.Text.Trim();

        if (string.IsNullOrEmpty(basePkg) || !File.Exists(basePkg))
        {
            MessageBox.Show("Please select a valid Base Game PKG file!", "Missing File", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try { File.Delete(CancelFlagPath); } catch { }

        bool requiresElevation = IsPathProtected(targetDir) && !IsUserAdmin();

        btnStartInstall.Enabled = false;
        btnStartInstall.Width = 240;
        btnCancelInstall.Visible = true;
        btnCancelInstall.Enabled = true;
        progressBar.Visible = true;
        extractionCts = new CancellationTokenSource();

        if (requiresElevation)
        {
            lblInstallStatus.Text = "Extracting files to Program Files (Please wait 3–5 minutes)...";
            string currentExe = Environment.ProcessPath ?? Path.Combine(baseDir, "Catherine.exe");
            string cleanBaseDir = baseDir.TrimEnd('\\', '/');
            string cleanTargetDir = targetDir.TrimEnd('\\', '/');

            var psi = new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = $"--elevated-install \"{cleanBaseDir}\" \"{cleanTargetDir}\" \"{basePkg}\" \"{patchPkg}\" \"{dlcFolder}\"",
                Verb = "runas",
                UseShellExecute = true
            };

            try
            {
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode == 0)
                    {
                        MessageBox.Show("Installation Complete!\nA desktop shortcut has been created.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Close();
                    }
                    else if (proc.ExitCode == 2)
                    {
                        MessageBox.Show("Installation was cancelled. Cleaned up partial files.", "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Installation was aborted or encountered an error.", "Aborted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch
            {
                MessageBox.Show("Administrator permission was denied.", "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                btnStartInstall.Width = 350;
                btnStartInstall.Enabled = true;
                btnCancelInstall.Visible = false;
                progressBar.Visible = false;
                try { File.Delete(CancelFlagPath); } catch { }
            }
        }
        else
        {
            lblInstallStatus.Text = "Unpacking files directly...";
            try
            {
                await Task.Run(() => {
                    InstallerWorker.DoFullInstall(baseDir, targetDir, basePkg, patchPkg, dlcFolder, (status) => {
                        Invoke(() => lblInstallStatus.Text = status);
                    });
                }, extractionCts.Token);

                MessageBox.Show("Installation Complete!\nThe game is ready to play.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshViewState();
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Installation was cancelled.", "Aborted", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Installation error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnStartInstall.Width = 350;
                btnStartInstall.Enabled = true;
                btnCancelInstall.Visible = false;
                progressBar.Visible = false;
                try { File.Delete(CancelFlagPath); } catch { }
            }
        }
    }

    private bool IsPathProtected(string path)
    {
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return path.StartsWith(progFiles, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(progFilesX86, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsUserAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool LaunchGame(string targetDir)
    {
        string engineExe = Path.Combine(targetDir, "Engine", "shadps4.exe");
        var psi = new ProcessStartInfo { FileName = engineExe, Arguments = "-g CUSA14836", WorkingDirectory = Path.Combine(targetDir, "Engine"), CreateNoWindow = true, UseShellExecute = false };
        try { Process.Start(psi); return true; } catch { return false; }
    }

    private void LoadCurrentSettings()
    {
        string disabledXml = patchXmlPath + ".disabled";
        chkSkipIntro.Checked = File.Exists(patchXmlPath) && !File.Exists(disabledXml);

        if (File.Exists(launcherSettingsPath))
        {
            try {
                var doc = JsonNode.Parse(File.ReadAllText(launcherSettingsPath));
                chkSkipLauncher.Checked = doc?["SkipLauncher"]?.GetValue<bool>() ?? false;
            } catch { }
        }
    }

    private void SavePatchSetting()
    {
        try {
            string disabledXml = patchXmlPath + ".disabled";
            if (chkSkipIntro.Checked && File.Exists(disabledXml)) File.Move(disabledXml, patchXmlPath);
            else if (!chkSkipIntro.Checked && File.Exists(patchXmlPath)) File.Move(patchXmlPath, disabledXml);
        } catch { }
    }

    private void SaveLauncherSetting()
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(launcherSettingsPath)!);
            var obj = new { SkipLauncher = chkSkipLauncher.Checked };
            File.WriteAllText(launcherSettingsPath, JsonSerializer.Serialize(obj));
        } catch { }
    }
}

public static class InstallerWorker
{
    private static readonly string CancelFlagPath = Path.Combine(Path.GetTempPath(), "catherine_cancel.flag");

    public static void ExecuteFromArgs(string[] args)
    {
        if (args.Length < 4) return;
        string sourceDir = args[1].TrimEnd('\\', '/');
        string targetDir = args[2].TrimEnd('\\', '/');
        string basePkg = args[3];
        string patchPkg = args.Length > 4 ? args[4] : "";
        string dlcFolder = args.Length > 5 ? args[5] : "";

        try { DoFullInstall(sourceDir, targetDir, basePkg, patchPkg, dlcFolder, null); Environment.Exit(0); }
        catch (OperationCanceledException) { try { if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true); } catch { } Environment.Exit(2); }
        catch { Environment.Exit(1); }
    }

    public static void DoFullInstall(string sourceDir, string targetDir, string basePkg, string patchPkg, string dlcFolder, Action<string>? statusCallback)
    {
        Directory.CreateDirectory(targetDir);
        string targetEngine = Path.Combine(targetDir, "Engine");
        string targetData = Path.Combine(targetDir, "Data");
        string targetDlc = Path.Combine(targetData, "DLC");
        string targetGame = Path.Combine(targetData, "CUSA14836");
        string targetExe = Path.Combine(targetDir, "Catherine.exe");

        Directory.CreateDirectory(targetData);
        Directory.CreateDirectory(targetDlc);

        statusCallback?.Invoke("Extracting internal engine...");
        var assembly = Assembly.GetExecutingAssembly();
        string? resName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("Engine.zip"));

        if (resName != null)
        {
            using Stream? stream = assembly.GetManifestResourceStream(resName);
            if (stream != null)
            {
                using var archive = new ZipArchive(stream);
                archive.ExtractToDirectory(targetEngine, overwriteFiles: true);
            }
        }
        else if (Directory.Exists(Path.Combine(sourceDir, "Engine")))
        {
            CopyDirectory(Path.Combine(sourceDir, "Engine"), targetEngine);
        }

        string currentExe = Environment.ProcessPath ?? Path.Combine(sourceDir, "Catherine.exe");
        if (!File.Exists(targetExe) || !string.Equals(currentExe, targetExe, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(currentExe, targetExe, true);
        }

        CheckCancel(targetGame, targetDlc);

        string extractorExe = Path.Combine(targetEngine, "pkg_extractor.exe");

        statusCallback?.Invoke("Unpacking Base Game PKG...");
        RunProcess(extractorExe, $"\"{basePkg}\" \"{targetData}\"");

        CheckCancel(targetGame, targetDlc);

        if (!string.IsNullOrEmpty(patchPkg) && File.Exists(patchPkg))
        {
            statusCallback?.Invoke("Unpacking Update Patch PKG...");
            RunProcess(extractorExe, $"\"{patchPkg}\" \"{targetData}\"");

            string patchFolder = Path.Combine(targetData, "CUSA14836-patch");
            if (Directory.Exists(patchFolder))
            {
                statusCallback?.Invoke("Merging patch into game...");
                RunProcess("cmd.exe", $"/c xcopy \"{patchFolder}\\*\" \"{targetGame}\\\" /s /e /y /q");
                Directory.Delete(patchFolder, true);
            }
        }

        CheckCancel(targetGame, targetDlc);

        if (!string.IsNullOrEmpty(dlcFolder) && Directory.Exists(dlcFolder))
        {
            statusCallback?.Invoke("Unpacking DLC PKGs...");
            foreach (var pkg in Directory.GetFiles(dlcFolder, "*.pkg"))
            {
                CheckCancel(targetGame, targetDlc);
                RunProcess(extractorExe, $"\"{pkg}\" \"{targetDlc}\"");
            }
        }

        InjectConfigPaths(targetEngine, targetData);

        try { RunProcess("icacls.exe", $"\"{targetDir}\" /grant Users:(OI)(CI)M /T /Q"); } catch { }

        CreateDesktopShortcut(targetExe, "Catherine - Full Body");
    }

    private static void CheckCancel(string targetGame, string targetDlc)
    {
        if (File.Exists(CancelFlagPath))
        {
            try { if (Directory.Exists(targetGame)) Directory.Delete(targetGame, true); } catch { }
            try { if (Directory.Exists(targetDlc)) Directory.Delete(targetDlc, true); } catch { }
            throw new OperationCanceledException();
        }
    }

    public static void CreateDesktopShortcut(string exePath, string shortcutName)
    {
        try
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktopDir, $"{shortcutName}.lnk");
            string workDir = Path.GetDirectoryName(exePath) ?? "";

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = workDir;
                shortcut.IconLocation = exePath;
                shortcut.Save();
            }
        }
        catch { }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir)) File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        foreach (string subDir in Directory.GetDirectories(sourceDir)) CopyDirectory(subDir, Path.Combine(targetDir, Path.GetFileName(subDir)));
    }

    private static void RunProcess(string fileName, string args)
    {
        var proc = Process.Start(new ProcessStartInfo { FileName = fileName, Arguments = args, CreateNoWindow = true, UseShellExecute = false });
        if (proc == null) return;
        while (!proc.HasExited)
        {
            if (File.Exists(CancelFlagPath))
            {
                try { proc.Kill(true); } catch { }
                throw new OperationCanceledException();
            }
            Thread.Sleep(100);
        }
        if (File.Exists(CancelFlagPath)) throw new OperationCanceledException();
    }

    private static void InjectConfigPaths(string enginePath, string dataPath)
    {
        string cfg = Path.Combine(enginePath, "User", "config.json");
        if (!File.Exists(cfg)) return;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(cfg));
            if (json?["General"] is JsonObject general)
            {
                general["install_dirs"] = new JsonArray { new JsonObject { ["enabled"] = true, ["path"] = dataPath.Replace("\\", "/") } };
                general["addon_install_dir"] = Path.Combine(dataPath, "DLC").Replace("\\", "/");
            }
            File.WriteAllText(cfg, json?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
