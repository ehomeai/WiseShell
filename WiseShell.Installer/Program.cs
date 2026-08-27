using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

internal static class Program
{
    private const string ProductName = "WiseShell";
    private const string Publisher = "WiseShell";
    private const string PayloadResourceName = "WiseShellPayload.zip";
    private const string AppExeName = "WiseShell.App.exe";
    private const string CachedInstallerName = "WiseShellSetup.exe";
    private const string DesktopRuntimeName = ".NET 8 Desktop Runtime";
    private const string DesktopRuntimeDownloadUrl = "https://dotnet.microsoft.com/download/dotnet/8.0/runtime";
    private static readonly string LogFilePath = Path.Combine(Path.GetTempPath(), "WiseShellSetup.log");

    private static string ProductVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "1.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Log($"Installer started: version={ProductVersion}; args={string.Join(" ", args)}");
            var options = InstallerOptions.Parse(args);
            if (options.Quiet)
            {
                return options.Uninstall ? Uninstall(options, null) : Install(options, null);
            }

            using var form = new InstallerForm(options);
            Application.Run(form);
            return form.ExitCode;
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            MessageBox.Show(ex.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Install(InstallerOptions options, IProgress<InstallProgress>? progress)
    {
        var installDirectory = options.InstallDirectory ?? ReadInstalledProduct()?.InstallLocation ?? GetDefaultInstallDirectory();
        installDirectory = Path.GetFullPath(installDirectory);
        EnsureSafeInstallDirectory(installDirectory);

        if (!IsWindowsDesktopRuntimeInstalled())
        {
            Log($"{DesktopRuntimeName} was not detected. Download: {DesktopRuntimeDownloadUrl}");
        }

        progress?.Report(new InstallProgress(10, Ui.PreparingDirectory));
        Log($"Creating install directory: {installDirectory}");
        Directory.CreateDirectory(installDirectory);

        progress?.Report(new InstallProgress(30, Ui.CopyingFiles));
        Log("Extracting payload.");
        ExtractPayload(installDirectory);

        var installedAppPath = Path.Combine(installDirectory, AppExeName);
        if (!File.Exists(installedAppPath))
        {
            throw new FileNotFoundException(Ui.AppExeMissing, installedAppPath);
        }

        progress?.Report(new InstallProgress(70, Ui.CreatingShortcuts));
        var cachedInstallerPath = Path.Combine(installDirectory, CachedInstallerName);
        var currentInstallerPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentInstallerPath) && File.Exists(currentInstallerPath))
        {
            Log($"Caching installer: {cachedInstallerPath}");
            File.Copy(currentInstallerPath, cachedInstallerPath, overwrite: true);
        }

        Log("Creating start menu shortcut.");
        CreateShortcut(GetStartMenuShortcutPath(), installedAppPath, installDirectory);
        if (options.CreateDesktopShortcut)
        {
            Log("Creating desktop shortcut.");
            CreateShortcut(GetDesktopShortcutPath(), installedAppPath, installDirectory);
        }
        else
        {
            DeleteShortcut(GetDesktopShortcutPath());
        }

        progress?.Report(new InstallProgress(90, Ui.WritingUninstallInfo));
        Log("Writing uninstall registry keys.");
        WriteUninstallRegistration(installDirectory, installedAppPath, cachedInstallerPath);

        progress?.Report(new InstallProgress(100, Ui.InstallComplete));
        Log("Install completed.");
        return 0;
    }

    private static int Uninstall(InstallerOptions options, IProgress<InstallProgress>? progress)
    {
        var installDirectory = options.InstallDirectory ?? ReadInstalledProduct()?.InstallLocation ?? GetDefaultInstallDirectory();
        installDirectory = Path.GetFullPath(installDirectory);
        EnsureSafeInstallDirectory(installDirectory);

        progress?.Report(new InstallProgress(25, Ui.DeletingShortcuts));
        Log("Deleting shortcuts.");
        DeleteShortcut(GetStartMenuShortcutPath());
        DeleteShortcut(GetDesktopShortcutPath());

        progress?.Report(new InstallProgress(50, Ui.DeletingUninstallInfo));
        Log("Deleting uninstall registry keys.");
        DeleteUninstallRegistration();

        progress?.Report(new InstallProgress(75, Ui.DeletingFiles));
        if (Directory.Exists(installDirectory))
        {
            Log($"Removing install directory: {installDirectory}");
            RemoveInstallDirectory(installDirectory);
        }

        progress?.Report(new InstallProgress(100, Ui.UninstallComplete));
        Log("Uninstall completed.");
        return 0;
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static string GetDefaultInstallDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);
    }

    private static void ExtractPayload(string installDirectory)
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException(Ui.PayloadMissing);

        var tempZip = Path.Combine(Path.GetTempPath(), $"{ProductName}-{Guid.NewGuid():N}.zip");
        try
        {
            using (var file = File.Create(tempZip))
            {
                resource.CopyTo(file);
            }

            ZipFile.ExtractToDirectory(tempZip, installDirectory, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException(Ui.ShortcutUnavailable);
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException(Ui.ShortcutServiceUnavailable);
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = "WiseShell SSH/SFTP client";
        shortcut.IconLocation = targetPath;
        shortcut.Save();

        ReleaseComObject(shortcut);
        ReleaseComObject(shell);
    }

    private static void DeleteShortcut(string shortcutPath)
    {
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }

        var folder = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrWhiteSpace(folder) &&
            Directory.Exists(folder) &&
            Directory.GetFileSystemEntries(folder).Length == 0 &&
            !string.Equals(folder, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(folder);
        }
    }

    private static string GetStartMenuShortcutPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs",
            ProductName,
            $"{ProductName}.lnk");
    }

    private static string GetDesktopShortcutPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            $"{ProductName}.lnk");
    }

    private static void WriteUninstallRegistration(string installDirectory, string appPath, string installerPath)
    {
        using var key = Registry.LocalMachine.CreateSubKey(GetUninstallRegistrySubKey())
            ?? throw new InvalidOperationException(Ui.UninstallInfoUnavailable);

        key.SetValue("DisplayName", ProductName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", ProductVersion, RegistryValueKind.String);
        key.SetValue("Publisher", Publisher, RegistryValueKind.String);
        key.SetValue("InstallLocation", installDirectory, RegistryValueKind.String);
        key.SetValue("DisplayIcon", appPath, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{installerPath}\" --uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{installerPath}\" --uninstall --quiet", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static InstalledProduct? ReadInstalledProduct()
    {
        using var key = Registry.LocalMachine.OpenSubKey(GetUninstallRegistrySubKey());
        if (key is null)
        {
            return null;
        }

        var installLocation = key.GetValue("InstallLocation") as string;
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return null;
        }

        return new InstalledProduct(
            installLocation,
            key.GetValue("DisplayVersion") as string ?? string.Empty,
            key.GetValue("UninstallString") as string ?? string.Empty);
    }

    private static string GetUninstallRegistrySubKey()
    {
        return $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{ProductName}";
    }

    private static void DeleteUninstallRegistration()
    {
        Registry.LocalMachine.DeleteSubKeyTree(GetUninstallRegistrySubKey(), throwOnMissingSubKey: false);
    }

    private static void RemoveInstallDirectory(string installDirectory)
    {
        var currentProcessPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentProcessPath) &&
            currentProcessPath.StartsWith(installDirectory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
        {
            ScheduleDirectoryRemoval(installDirectory);
            return;
        }

        Directory.Delete(installDirectory, recursive: true);
    }

    private static void ScheduleDirectoryRemoval(string installDirectory)
    {
        var command = $"/c timeout /t 2 /nobreak > nul & rmdir /s /q \"{installDirectory}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = command,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void EnsureSafeInstallDirectory(string installDirectory)
    {
        var root = Path.GetPathRoot(installDirectory);
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(root, installDirectory, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetFolderPath(Environment.SpecialFolder.Windows), installDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Ui.UnsafeInstallDirectory);
        }
    }

    private static bool IsWindowsDesktopRuntimeInstalled()
    {
        return IsWindowsDesktopRuntimeInstalledInView(RegistryView.Registry64) ||
               IsWindowsDesktopRuntimeInstalledInView(RegistryView.Registry32);
    }

    private static bool IsWindowsDesktopRuntimeInstalledInView(RegistryView registryView)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App");
            if (key is null)
            {
                return false;
            }

            foreach (var valueName in key.GetValueNames())
            {
                if (Version.TryParse(valueName, out var version) && version.Major >= 8)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Runtime detection failed in {registryView}: {ex.Message}");
        }

        return false;
    }

    private static void OpenRuntimeDownloadPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = DesktopRuntimeDownloadUrl,
            UseShellExecute = true
        });
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private sealed record InstallProgress(int Percent, string Message);

    private sealed record InstalledProduct(string InstallLocation, string DisplayVersion, string UninstallString);

    private sealed record InstallerOptions(bool Uninstall, bool Quiet, bool AssumeYes, string? InstallDirectory, bool CreateDesktopShortcut)
    {
        public static InstallerOptions Parse(IReadOnlyList<string> args)
        {
            var uninstall = false;
            var quiet = false;
            var assumeYes = false;
            var createDesktopShortcut = true;
            string? installDirectory = null;

            for (var i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                if (arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    uninstall = true;
                }
                else if (arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase))
                {
                    quiet = true;
                }
                else if (arg.Equals("--yes", StringComparison.OrdinalIgnoreCase))
                {
                    assumeYes = true;
                }
                else if (arg.Equals("--no-desktop-shortcut", StringComparison.OrdinalIgnoreCase))
                {
                    createDesktopShortcut = false;
                }
                else if (arg.Equals("--dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                {
                    installDirectory = args[++i];
                }
            }

            return new InstallerOptions(uninstall, quiet, assumeYes, installDirectory, createDesktopShortcut);
        }
    }

    private sealed class InstallerForm : Form
    {
        private readonly Label _titleLabel;
        private readonly Label _descriptionLabel;
        private readonly Label _installedStatusLabel;
        private readonly Label _pathLabel;
        private readonly TextBox _installDirectoryTextBox;
        private readonly Button _browseButton;
        private readonly CheckBox _desktopShortcutCheckBox;
        private readonly CheckBox _launchAfterInstallCheckBox;
        private readonly Label _runtimeStatusLabel;
        private readonly LinkLabel _runtimeDownloadLink;
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;
        private readonly Button _primaryButton;
        private readonly Button _secondaryButton;
        private readonly Button _closeButton;
        private readonly InstallerOptions _initialOptions;
        private InstalledProduct? _installedProduct;
        private bool _operationComplete;

        public int ExitCode { get; private set; }

        public InstallerForm(InstallerOptions options)
        {
            _initialOptions = options;
            _installedProduct = ReadInstalledProduct();

            Text = Ui.WindowTitle;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 420);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _titleLabel = new Label
            {
                AutoSize = false,
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(24, 20),
                Size = new Size(500, 28)
            };

            _descriptionLabel = new Label
            {
                AutoSize = false,
                Location = new Point(24, 54),
                Size = new Size(508, 42)
            };

            _installedStatusLabel = new Label
            {
                AutoSize = false,
                Location = new Point(24, 98),
                Size = new Size(508, 42)
            };

            _pathLabel = new Label
            {
                AutoSize = false,
                Text = Ui.InstallDirectory,
                Location = new Point(24, 148),
                Size = new Size(120, 22)
            };

            _installDirectoryTextBox = new TextBox
            {
                Location = new Point(24, 172),
                Size = new Size(420, 26)
            };

            _browseButton = new Button
            {
                Text = Ui.Browse,
                Location = new Point(454, 170),
                Size = new Size(78, 30)
            };
            _browseButton.Click += (_, _) => BrowseInstallDirectory();

            _desktopShortcutCheckBox = new CheckBox
            {
                Text = Ui.CreateDesktopShortcut,
                Checked = options.CreateDesktopShortcut,
                Location = new Point(24, 212),
                Size = new Size(220, 26)
            };

            _launchAfterInstallCheckBox = new CheckBox
            {
                Text = Ui.LaunchAfterInstall,
                Checked = false,
                Location = new Point(260, 212),
                Size = new Size(240, 26)
            };

            var hasRuntime = IsWindowsDesktopRuntimeInstalled();
            _runtimeStatusLabel = new Label
            {
                AutoSize = false,
                Text = hasRuntime ? Ui.RuntimeDetected : Ui.RuntimeNotDetected,
                ForeColor = hasRuntime ? Color.FromArgb(0, 110, 40) : Color.FromArgb(170, 80, 0),
                Location = new Point(24, 246),
                Size = new Size(350, 24)
            };

            _runtimeDownloadLink = new LinkLabel
            {
                AutoSize = false,
                Text = Ui.DownloadRuntime,
                Location = new Point(382, 246),
                Size = new Size(150, 24),
                Visible = !hasRuntime
            };
            _runtimeDownloadLink.LinkClicked += (_, _) => OpenRuntimeDownloadPage();

            _progressBar = new ProgressBar
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Location = new Point(24, 306),
                Size = new Size(508, 18)
            };

            _statusLabel = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
                AutoSize = false,
                Text = Ui.Ready,
                Location = new Point(24, 336),
                Size = new Size(300, 24)
            };

            _primaryButton = new Button
            {
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Location = new Point(274, 368),
                Size = new Size(82, 30)
            };
            _primaryButton.Click += async (_, _) => await PrimaryButtonClickAsync();

            _secondaryButton = new Button
            {
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Location = new Point(362, 368),
                Size = new Size(82, 30)
            };
            _secondaryButton.Click += async (_, _) => await SecondaryButtonClickAsync();

            _closeButton = new Button
            {
                Text = Ui.Close,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Location = new Point(450, 368),
                Size = new Size(82, 30),
                DialogResult = DialogResult.Cancel
            };
            _closeButton.Click += (_, _) => Close();

            Controls.AddRange(new Control[]
            {
                _titleLabel,
                _descriptionLabel,
                _installedStatusLabel,
                _pathLabel,
                _installDirectoryTextBox,
                _browseButton,
                _desktopShortcutCheckBox,
                _launchAfterInstallCheckBox,
                _runtimeStatusLabel,
                _runtimeDownloadLink,
                _progressBar,
                _statusLabel,
                _primaryButton,
                _secondaryButton,
                _closeButton
            });

            if (_initialOptions.Uninstall)
            {
                Shown += async (_, _) => await RequestUninstallAsync();
            }

            RefreshMode();
        }

        private void RefreshMode()
        {
            _installedProduct = ReadInstalledProduct();
            _operationComplete = false;
            _progressBar.Value = 0;
            _statusLabel.Text = Ui.Ready;

            if (_installedProduct is null)
            {
                _titleLabel.Text = Ui.InstallTitle;
                _descriptionLabel.Text = string.Format(Ui.InstallDescriptionFormat, ProductVersion);
                _installedStatusLabel.Text = Ui.NotInstalled;
                _installDirectoryTextBox.Text = _initialOptions.InstallDirectory ?? GetDefaultInstallDirectory();
                _installDirectoryTextBox.Enabled = true;
                _browseButton.Enabled = true;
                _desktopShortcutCheckBox.Enabled = true;
                _launchAfterInstallCheckBox.Enabled = true;
                _primaryButton.Text = Ui.Install;
                _primaryButton.Enabled = true;
                _secondaryButton.Text = Ui.Uninstall;
                _secondaryButton.Enabled = false;
                _closeButton.Text = Ui.Cancel;
            }
            else
            {
                _titleLabel.Text = Ui.InstalledTitle;
                _descriptionLabel.Text = string.Format(Ui.InstalledDescriptionFormat, ProductVersion);
                _installedStatusLabel.Text = string.Format(
                    Ui.InstalledStatusFormat,
                    string.IsNullOrWhiteSpace(_installedProduct.DisplayVersion) ? Ui.UnknownVersion : _installedProduct.DisplayVersion,
                    _installedProduct.InstallLocation);
                _installDirectoryTextBox.Text = _initialOptions.InstallDirectory ?? _installedProduct.InstallLocation;
                _installDirectoryTextBox.Enabled = true;
                _browseButton.Enabled = true;
                _desktopShortcutCheckBox.Enabled = true;
                _launchAfterInstallCheckBox.Enabled = true;
                _primaryButton.Text = Ui.Reinstall;
                _primaryButton.Enabled = true;
                _secondaryButton.Text = Ui.Uninstall;
                _secondaryButton.Enabled = true;
                _closeButton.Text = Ui.Close;
            }
        }

        private async Task PrimaryButtonClickAsync()
        {
            if (_operationComplete)
            {
                Close();
                return;
            }

            await InstallAsync();
        }

        private async Task SecondaryButtonClickAsync()
        {
            if (_installedProduct is null)
            {
                return;
            }

            await RequestUninstallAsync();
        }

        private async Task RequestUninstallAsync()
        {
            if (_installedProduct is null)
            {
                MessageBox.Show(this, Ui.NotInstalledForUninstall, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshMode();
                return;
            }

            if (!_initialOptions.AssumeYes)
            {
                var result = MessageBox.Show(
                    this,
                    string.Format(Ui.UninstallConfirmFormat, _installedProduct.InstallLocation),
                    ProductName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (result != DialogResult.Yes)
                {
                    return;
                }
            }

            await UninstallAsync();
        }

        private void BrowseInstallDirectory()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = Ui.ChooseInstallDirectory,
                SelectedPath = _installDirectoryTextBox.Text
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _installDirectoryTextBox.Text = dialog.SelectedPath;
            }
        }

        private async Task InstallAsync()
        {
            SetBusy(true, Ui.Installing);

            try
            {
                var options = _initialOptions with
                {
                    InstallDirectory = string.IsNullOrWhiteSpace(_installDirectoryTextBox.Text)
                        ? GetDefaultInstallDirectory()
                        : _installDirectoryTextBox.Text.Trim(),
                    CreateDesktopShortcut = _desktopShortcutCheckBox.Checked
                };

                var progress = new Progress<InstallProgress>(UpdateProgress);
                await Task.Run(() => Install(options, progress));

                _operationComplete = true;
                ExitCode = 0;
                _statusLabel.Text = Ui.InstallComplete;
                _primaryButton.Text = Ui.Close;
                _primaryButton.Enabled = true;
                _secondaryButton.Enabled = false;
                _closeButton.Enabled = false;

                MessageBox.Show(this, string.Format(Ui.InstallCompleteMessageFormat, LogFilePath), ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (_launchAfterInstallCheckBox.Checked)
                {
                    if (!IsWindowsDesktopRuntimeInstalled())
                    {
                        MessageBox.Show(this, string.Format(Ui.RuntimeMissingLaunchFormat, DesktopRuntimeDownloadUrl), ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(options.InstallDirectory!, AppExeName),
                        WorkingDirectory = options.InstallDirectory,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                Log(ex.ToString());
                MessageBox.Show(this, $"{ex.Message}{Environment.NewLine}{Environment.NewLine}{string.Format(Ui.LogPathFormat, LogFilePath)}", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetBusy(false, Ui.Ready);
            }
        }

        private async Task UninstallAsync()
        {
            SetBusy(true, Ui.Uninstalling);

            try
            {
                var options = _initialOptions with
                {
                    InstallDirectory = _installedProduct?.InstallLocation
                };

                var progress = new Progress<InstallProgress>(UpdateProgress);
                await Task.Run(() => Uninstall(options, progress));

                _operationComplete = true;
                ExitCode = 0;
                _statusLabel.Text = Ui.UninstallComplete;
                MessageBox.Show(this, string.Format(Ui.UninstallCompleteMessageFormat, LogFilePath), ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshMode();
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                Log(ex.ToString());
                MessageBox.Show(this, $"{ex.Message}{Environment.NewLine}{Environment.NewLine}{string.Format(Ui.LogPathFormat, LogFilePath)}", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetBusy(false, Ui.Ready);
            }
        }

        private void SetBusy(bool busy, string message)
        {
            ControlBox = !busy;
            UseWaitCursor = busy;
            _installDirectoryTextBox.Enabled = !busy;
            _browseButton.Enabled = !busy;
            _desktopShortcutCheckBox.Enabled = !busy;
            _launchAfterInstallCheckBox.Enabled = !busy;
            _runtimeDownloadLink.Enabled = !busy;
            _primaryButton.Enabled = !busy;
            _secondaryButton.Enabled = !busy && _installedProduct is not null;
            _closeButton.Enabled = !busy;
            _statusLabel.Text = message;
            if (busy)
            {
                _progressBar.Value = 0;
            }
        }

        private void UpdateProgress(InstallProgress progress)
        {
            _progressBar.Value = Math.Clamp(progress.Percent, _progressBar.Minimum, _progressBar.Maximum);
            _statusLabel.Text = progress.Message;
        }
    }

    private static class Ui
    {
        public const string WindowTitle = "WiseShell \u5b89\u88c5\u7a0b\u5e8f";
        public const string InstallTitle = "\u5b89\u88c5 WiseShell";
        public const string InstalledTitle = "WiseShell \u5df2\u5b89\u88c5";
        public const string InstallDescriptionFormat = "\u5b89\u88c5\u5305\u7248\u672c\uff1a{0}\u3002\u5b89\u88c5\u524d\u8bf7\u786e\u8ba4\u8fd0\u884c\u73af\u5883\u3002";
        public const string InstalledDescriptionFormat = "\u5f53\u524d\u5b89\u88c5\u5305\u7248\u672c\uff1a{0}\u3002\u53ef\u9009\u62e9\u91cd\u65b0\u5b89\u88c5\u6216\u5378\u8f7d\u3002";
        public const string InstalledStatusFormat = "\u5df2\u5b89\u88c5\u7248\u672c\uff1a{0}\r\n\u5b89\u88c5\u4f4d\u7f6e\uff1a{1}";
        public const string NotInstalled = "\u72b6\u6001\uff1a\u672a\u5b89\u88c5";
        public const string UnknownVersion = "\u672a\u77e5";
        public const string InstallDirectory = "\u5b89\u88c5\u76ee\u5f55";
        public const string Browse = "\u6d4f\u89c8...";
        public const string CreateDesktopShortcut = "\u521b\u5efa\u684c\u9762\u5feb\u6377\u65b9\u5f0f";
        public const string LaunchAfterInstall = "\u5b89\u88c5\u5b8c\u6210\u540e\u542f\u52a8 WiseShell";
        public const string RuntimeDetected = ".NET 8 Desktop Runtime: \u5df2\u68c0\u6d4b\u5230";
        public const string RuntimeNotDetected = ".NET 8 Desktop Runtime: \u672a\u68c0\u6d4b\u5230";
        public const string DownloadRuntime = "\u4e0b\u8f7d\u8fd0\u884c\u73af\u5883";
        public const string Ready = "\u51c6\u5907\u5c31\u7eea\u3002";
        public const string Install = "\u5b89\u88c5";
        public const string Reinstall = "\u91cd\u65b0\u5b89\u88c5";
        public const string Uninstall = "\u5378\u8f7d";
        public const string Close = "\u5173\u95ed";
        public const string Cancel = "\u53d6\u6d88";
        public const string Installing = "\u6b63\u5728\u5b89\u88c5...";
        public const string Uninstalling = "\u6b63\u5728\u5378\u8f7d...";
        public const string PreparingDirectory = "\u6b63\u5728\u51c6\u5907\u5b89\u88c5\u76ee\u5f55...";
        public const string CopyingFiles = "\u6b63\u5728\u590d\u5236 WiseShell \u6587\u4ef6...";
        public const string CreatingShortcuts = "\u6b63\u5728\u521b\u5efa\u5feb\u6377\u65b9\u5f0f...";
        public const string WritingUninstallInfo = "\u6b63\u5728\u5199\u5165\u5378\u8f7d\u4fe1\u606f...";
        public const string InstallComplete = "\u5b89\u88c5\u5b8c\u6210\u3002";
        public const string DeletingShortcuts = "\u6b63\u5728\u5220\u9664\u5feb\u6377\u65b9\u5f0f...";
        public const string DeletingUninstallInfo = "\u6b63\u5728\u5220\u9664\u5378\u8f7d\u4fe1\u606f...";
        public const string DeletingFiles = "\u6b63\u5728\u5220\u9664\u7a0b\u5e8f\u6587\u4ef6...";
        public const string UninstallComplete = "\u5378\u8f7d\u5b8c\u6210\u3002";
        public const string ChooseInstallDirectory = "\u9009\u62e9 WiseShell \u5b89\u88c5\u76ee\u5f55";
        public const string NotInstalledForUninstall = "\u672a\u68c0\u6d4b\u5230 WiseShell \u5b89\u88c5\u4fe1\u606f\u3002";
        public const string UninstallConfirmFormat = "\u786e\u5b9a\u8981\u5378\u8f7d WiseShell \u5417\uff1f\r\n\r\n\u5b89\u88c5\u4f4d\u7f6e\uff1a{0}\r\n\r\n\u684c\u9762\u548c\u5f00\u59cb\u83dc\u5355\u5feb\u6377\u65b9\u5f0f\u4e5f\u4f1a\u88ab\u5220\u9664\u3002";
        public const string InstallCompleteMessageFormat = "WiseShell \u5df2\u5b89\u88c5\u5b8c\u6210\u3002\r\n\u65e5\u5fd7\uff1a{0}";
        public const string UninstallCompleteMessageFormat = "WiseShell \u5df2\u5378\u8f7d\u3002\r\n\u65e5\u5fd7\uff1a{0}";
        public const string RuntimeMissingLaunchFormat = "\u672a\u68c0\u6d4b\u5230 .NET 8 Desktop Runtime\uff0c\u8bf7\u5148\u5b89\u88c5\u8fd0\u884c\u73af\u5883\uff1a{0}";
        public const string LogPathFormat = "\u65e5\u5fd7\uff1a{0}";
        public const string AppExeMissing = "\u6ca1\u6709\u627e\u5230\u5df2\u5b89\u88c5\u7684\u5e94\u7528\u7a0b\u5e8f\u6587\u4ef6\u3002";
        public const string PayloadMissing = "\u5b89\u88c5\u5305\u5185\u7f3a\u5c11 WiseShell \u7a0b\u5e8f\u6587\u4ef6\u3002";
        public const string ShortcutUnavailable = "\u5f53\u524d\u7cfb\u7edf\u65e0\u6cd5\u521b\u5efa Windows \u5feb\u6377\u65b9\u5f0f\u3002";
        public const string ShortcutServiceUnavailable = "\u65e0\u6cd5\u542f\u52a8 Windows \u5feb\u6377\u65b9\u5f0f\u670d\u52a1\u3002";
        public const string UninstallInfoUnavailable = "\u65e0\u6cd5\u5199\u5165\u5378\u8f7d\u4fe1\u606f\u3002";
        public const string UnsafeInstallDirectory = "\u5b89\u88c5\u76ee\u5f55\u4e0d\u5b89\u5168\uff0c\u8bf7\u9009\u62e9\u5176\u4ed6\u76ee\u5f55\u3002";
    }
}
