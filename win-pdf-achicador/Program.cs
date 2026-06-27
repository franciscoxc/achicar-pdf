using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

sealed class DropBox : Control
{
    Bitmap? pdfImage;
    bool dimIcon;

    public DropBox()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    public Bitmap? PdfImage
    {
        get => pdfImage;
        set { pdfImage = value; Invalidate(); }
    }

    public bool DimIcon
    {
        get => dimIcon;
        set { dimIcon = value; Invalidate(); }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        ControlPaint.DrawBorder(g, ClientRectangle, Color.FromArgb(100, 100, 100), ButtonBorderStyle.Solid);

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;
        var textSize = TextRenderer.MeasureText(g, Text, Font, new Size(Width - 24, Height), flags);
        var iconSize = PdfImage?.Size ?? Size.Empty;
        var gap = PdfImage is null || Text.Length == 0 ? 0 : 10;
        var blockHeight = iconSize.Height + gap + textSize.Height;
        var y = Math.Max(8, (Height - blockHeight) / 2);

        if (PdfImage is not null)
        {
            var iconX = (Width - iconSize.Width) / 2;
            if (DimIcon) ControlPaint.DrawImageDisabled(g, PdfImage, iconX, y, BackColor);
            else g.DrawImage(PdfImage, iconX, y, iconSize.Width, iconSize.Height);
            y += iconSize.Height + gap;
        }

        var textRect = new Rectangle(12, y, Width - 24, Height - y - 8);
        TextRenderer.DrawText(g, Text, Font, textRect, ForeColor, flags | TextFormatFlags.Top);
    }
}

sealed class ProgressStrip : Control
{
    int value;

    public int Value
    {
        get => value;
        set { this.value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    public ProgressStrip()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Color.FromArgb(215, 215, 215));
        using var brush = new SolidBrush(Color.FromArgb(36, 107, 87));
        e.Graphics.FillRectangle(brush, 0, 0, Width * Value / 100, Height);
    }
}

sealed class MainForm : Form
{
    const int TargetMinPpi = 50;
    const int TargetMaxPpi = 150;
    const int TargetMaxAttempts = 7;
    const string GhostscriptApi = "https://api.github.com/repos/ArtifexSoftware/ghostpdl-downloads/releases/latest";
    const string GhostscriptPage = "https://ghostscript.com/releases/gsdnld.html";
    const string DropHint = "tira tu pdf aqui\no haz clic para buscar";

    readonly int[] ppiValues = [75, 80, 90, 100, 150];
    readonly GroupBox smartBox = new() { Text = "modo inteligente", ForeColor = Color.Gray, Left = 16, Top = 12, Width = 294, Height = 76, Font = new Font(SystemFonts.DefaultFont.FontFamily, 7f) };
    readonly Label targetLabel = new() { Text = "Tamano maximo que necesitas (MB)", Left = 12, Top = 22, Width = 260, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
    readonly TextBox target = new() { Left = 12, Top = 44, Width = 80, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
    readonly Button clearTarget = new() { Text = "x", Left = 98, Top = 44, Width = 22, Height = 22, FlatStyle = FlatStyle.Flat, ForeColor = Color.Red, Font = SystemFonts.DefaultFont, Enabled = false };
    readonly GroupBox manualBox = new() { Text = "modo manual", ForeColor = Color.Gray, Left = 16, Top = 96, Width = 294, Height = 92, Font = new Font(SystemFonts.DefaultFont.FontFamily, 7f) };
    readonly CheckBox gray = new() { Text = "Forzar gris", Left = 12, Top = 24, Width = 180, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
    readonly Label ppiLabel = new() { Text = "PPI 100", Left = 216, Top = 24, Width = 60, TextAlign = ContentAlignment.MiddleRight, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
    readonly TrackBar ppi = new() { Left = 12, Top = 48, Width = 264, Minimum = 0, Maximum = 4, TickFrequency = 1, Value = 3, Font = SystemFonts.DefaultFont };
    readonly DropBox drop = new()
    {
        Text = DropHint,
        Left = 24,
        Top = 198,
        Width = 278,
        Height = 146,
        AllowDrop = true
    };
    readonly Label result = new() { Left = 24, Top = 354, Width = 278, Height = 38, ForeColor = Color.Red };
    readonly Label outputName = new() { Left = 24, Top = 394, Width = 278, Height = 34, Visible = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter };
    readonly Label status = new() { Left = 24, Top = 432, Width = 170, Height = 32, Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold), ForeColor = Color.FromArgb(36, 107, 87) };
    readonly Button cancel = new() { Text = "cancelar", Left = 204, Top = 432, Width = 98, Height = 28, Enabled = false, Visible = false };
    readonly Button showOutput = new() { Text = "mostrar mi PDF achicado", Left = 70, Top = 438, Width = 186, Height = 30, Enabled = false, Visible = false };
    readonly ProgressStrip progress = new() { Left = 0, Top = 480, Width = 326, Height = 6, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, Visible = false };
    string? gs;
    Process? current;
    bool working;
    bool cancelRequested;
    bool findingTarget;
    bool activeGray;
    int activePpi;
    int targetAttempt;
    int totalPages;

    public MainForm()
    {
        Text = "Achicar PDF";
        ClientSize = new Size(326, 486);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon; } catch { }
        gs = FindGhostscript();
        clearTarget.FlatAppearance.BorderSize = 0;
        drop.Cursor = Cursors.Hand;

        ppi.ValueChanged += (_, _) => ppiLabel.Text = $"PPI {ppiValues[ppi.Value]}";
        target.TextChanged += (_, _) => UpdateModeUi();
        clearTarget.Click += (_, _) => target.Clear();
        showOutput.Click += (_, _) => OpenOutput();
        cancel.Click += (_, _) =>
        {
            cancelRequested = true;
            current?.Kill(true);
        };

        drop.PdfImage = PdfIcon();
        smartBox.Controls.AddRange([targetLabel, target, clearTarget]);
        manualBox.Controls.AddRange([gray, ppiLabel, ppi]);
        Controls.AddRange([smartBox, manualBox, drop, result, outputName, status, cancel, showOutput, progress]);
        progress.BringToFront();
        UpdateModeUi();

        Shown += async (_, _) => await EnsureGhostscript();
        drop.Click += async (_, _) => await PickAndCompress();

        drop.DragEnter += (_, e) =>
        {
            if (gs is not null && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        drop.DragDrop += (_, e) =>
        {
            var files = (string[]?)e.Data?.GetData(DataFormats.FileDrop);
            if (files?.Length > 0) _ = Compress(files[0]);
        };
    }

    async Task PickAndCompress()
    {
        if (working) return;
        if (gs is null)
        {
            await EnsureGhostscript();
            if (gs is null) return;
        }

        var path = PdfFileDialog.Show(Handle);
        if (path is not null) await Compress(path);
    }

    async Task Compress(string input)
    {
        if (working || !input.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return;
        status.Visible = true;
        if (!TryGetTargetBytes(out var targetBytes))
        {
            HideOutput();
            status.Text = "tamano invalido";
            return;
        }

        var dir = Path.GetDirectoryName(input)!;
        var name = Path.GetFileNameWithoutExtension(input);
        drop.DimIcon = false;
        if (targetBytes is long targetSize && new FileInfo(input).Length <= targetSize)
        {
            drop.Text = $"{Path.GetFileName(input)}\noriginal: {FormatSize(new FileInfo(input).Length)}";
            result.Text = "El archivo que tiraste YA ES mas chico que lo que necesitas";
            status.Text = "";
            HideOutput();
            return;
        }

        activeGray = targetBytes is null && gray.Checked;
        var mode = activeGray ? "Gris" : "Color";
        working = true;
        cancelRequested = false;
        findingTarget = targetBytes is not null;
        activePpi = 0;
        targetAttempt = 0;
        totalPages = 0;

        drop.Text = $"{Path.GetFileName(input)}\noriginal: {FormatSize(new FileInfo(input).Length)}";
        result.Text = "";
        HideOutput();
        status.Text = "procesando 0%";
        progress.Value = 0;
        progress.Visible = true;
        cancel.Enabled = true;
        cancel.Visible = true;

        try
        {
            if (targetBytes is long maxBytes) await CompressToTarget(input, dir, name, mode, maxBytes);
            else await CompressSingle(input, dir, name, mode);
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }
        finally
        {
            current?.Dispose();
            current = null;
            working = false;
            findingTarget = false;
            activePpi = 0;
            targetAttempt = 0;
            cancel.Enabled = false;
            cancel.Visible = false;
            progress.Visible = false;
            cancelRequested = false;
        }
    }

    async Task CompressSingle(string input, string dir, string name, string mode)
    {
        var res = ppiValues[ppi.Value];
        var output = Path.Combine(dir, $"{name} ppi{res} {mode}.pdf");
        var temp = TempOutput(dir, name, Guid.NewGuid().ToString("N"), res);

        if (!await RunGhostscript(input, temp, res))
        {
            DeleteIfExists(temp);
            status.Text = cancelRequested ? "cancelado" : "fallo";
            return;
        }

        File.Move(temp, output, true);
        status.Text = "";
        result.Text = $"Resultado: {FormatSize(new FileInfo(output).Length)}";
        ShowOutput(output);
    }

    async Task CompressToTarget(string input, string dir, string name, string mode, long maxBytes)
    {
        var runId = Guid.NewGuid().ToString("N");
        string? bestPath = null;
        var bestPpi = 0;
        long bestSize = 0;
        var low = TargetMinPpi;
        var high = TargetMaxPpi;

        while (low <= high && !cancelRequested)
        {
            var res = (low + high) / 2;
            var temp = TempOutput(dir, name, runId, res);
            targetAttempt++;
            status.Text = $"probando {res} PPI";

            if (!await RunGhostscript(input, temp, res))
            {
                DeleteIfExists(temp);
                DeleteIfExists(bestPath);
                status.Text = cancelRequested ? "cancelado" : "fallo";
                return;
            }

            var size = new FileInfo(temp).Length;
            if (size <= maxBytes)
            {
                DeleteIfExists(bestPath);
                bestPath = temp;
                bestPpi = res;
                bestSize = size;
                low = res + 1;
            }
            else
            {
                DeleteIfExists(temp);
                high = res - 1;
            }
        }

        if (cancelRequested)
        {
            DeleteIfExists(bestPath);
            status.Text = "cancelado";
            return;
        }

        if (bestPath is null)
        {
            status.Text = "target demasiado bajo";
            result.Text = $"subi el target; {TargetMinPpi} PPI supera {FormatSize(maxBytes)}";
            return;
        }

        var output = Path.Combine(dir, $"{name} ppi{bestPpi} {mode}.pdf");
        File.Move(bestPath, output, true);
        status.Text = "";
        result.Text = bestPpi < 75
            ? $"Resultado: {FormatSize(bestSize)}; sugerido mas MB"
            : $"Resultado: {FormatSize(bestSize)}";
        ShowOutput(output);
    }

    async Task<bool> RunGhostscript(string input, string output, int res)
    {
        totalPages = 0;
        activePpi = res;
        var args = BuildArgs(input, output, res);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(gs!, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        current = process;
        process.OutputDataReceived += (_, e) => TrackProgress(e.Data);
        process.ErrorDataReceived += (_, e) => TrackProgress(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        var ok = process.ExitCode == 0 && !cancelRequested;
        current = null;
        process.Dispose();
        return ok;
    }

    string BuildArgs(string input, string output, int res)
    {
        var args = $"-sDEVICE=pdfwrite -dCompatibilityLevel=1.5 -dNOPAUSE -dBATCH -dSAFER " +
                   "-dUseFastColor=false " +
                   "-dDetectDuplicateImages=true -dCompressFonts=true -dSubsetFonts=true " +
                   "-dWriteXRefStm=true -dWriteObjStms=true " +
                   $"-dDownsampleColorImages=true -dColorImageResolution={res} " +
                   $"-dDownsampleGrayImages=true -dGrayImageResolution={res} " +
                   $"-dDownsampleMonoImages=true -dMonoImageResolution=300 ";

        if (activeGray) args += "-sColorConversionStrategy=Gray -sProcessColorModel=DeviceGray -dOverrideICC ";
        return args + $"-sOutputFile=\"{output}\" \"{input}\"";
    }

    void UpdateModeUi()
    {
        var smart = target.Text.Trim().Length > 0 && target.Text.Trim() != "0";
        clearTarget.Enabled = target.Text.Length > 0;
        gray.Enabled = ppi.Enabled = ppiLabel.Enabled = !smart;
        manualBox.ForeColor = smart ? Color.Silver : Color.Gray;
        targetLabel.ForeColor = smart ? SystemColors.ControlText : Color.Gray;
    }

    void ShowOutput(string path)
    {
        outputName.Text = Path.GetFileName(path);
        outputName.Tag = path;
        outputName.Visible = true;
        showOutput.Tag = path;
        showOutput.Visible = true;
        showOutput.Enabled = true;
        status.Visible = false;
        progress.Value = 100;
        progress.Visible = false;
        outputName.BringToFront();
        showOutput.BringToFront();
        drop.DimIcon = true;
    }

    void HideOutput()
    {
        status.Visible = true;
        outputName.Visible = false;
        outputName.Tag = null;
        showOutput.Visible = false;
        showOutput.Enabled = false;
        showOutput.Tag = null;
        progress.Visible = false;
        progress.Value = 0;
    }

    void OpenOutput()
    {
        if (showOutput.Tag is string path && File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    async Task EnsureGhostscript()
    {
        gs = FindGhostscript();
        if (gs is not null) return;

        drop.Text = "Ghostscript no esta instalado";
        var ok = MessageBox.Show(
            this,
            "Para usar Achicar PDF necesitamos descargar un componente llamado Ghostscript.\n\nDeberas esperar un poquito y luego apretar SI y SIGUIENTE unas cuantas veces hasta terminar.\n\nEsto se hace solo una vez.",
            "Ghostscript necesario",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);
        if (ok != DialogResult.OK)
        {
            status.Text = "Ghostscript no instalado";
            return;
        }

        status.Visible = true;
        status.Text = "buscando instalador...";
        try
        {
            var installer = await GetGhostscriptInstaller();
            if (installer is null)
            {
                InstallFailed("No encontre un instalador compatible.");
                return;
            }

            status.Text = installer.Value.note;
            var path = await DownloadFile(installer.Value.url, installer.Value.name);
            status.Text = "instalando Ghostscript...";
            if (!await RunInstaller(path))
            {
                InstallFailed("No se pudo ejecutar el instalador.");
                return;
            }

            gs = FindGhostscript();
            if (gs is not null)
            {
                status.Text = "";
                drop.Text = DropHint;
                return;
            }

            InstallFailed("La instalacion termino, pero no encontre Ghostscript.");
        }
        catch (Exception ex)
        {
            InstallFailed(ex.Message);
        }
    }

    async Task<(string url, string name, string note)?> GetGhostscriptInstaller()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AchicarPDF");
        using var doc = JsonDocument.Parse(await client.GetStringAsync(GhostscriptApi));
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => new
            {
                Name = a.GetProperty("name").GetString() ?? "",
                Url = a.GetProperty("browser_download_url").GetString() ?? ""
            })
            .Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var arch = RuntimeInformation.OSArchitecture;
        var asset = arch == Architecture.X86
            ? assets.FirstOrDefault(a => a.Name.EndsWith("w32.exe", StringComparison.OrdinalIgnoreCase))
            : arch == Architecture.Arm64
                ? assets.FirstOrDefault(a => a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("aarch64", StringComparison.OrdinalIgnoreCase))
                : assets.FirstOrDefault(a => a.Name.EndsWith("w64.exe", StringComparison.OrdinalIgnoreCase));

        asset ??= assets.FirstOrDefault(a => a.Name.EndsWith("w64.exe", StringComparison.OrdinalIgnoreCase));
        if (asset is null) return null;

        var note = arch == Architecture.Arm64 && !asset.Name.Contains("arm", StringComparison.OrdinalIgnoreCase)
            ? "descargando x64 para Windows ARM..."
            : $"descargando {asset.Name}...";
        return (asset.Url, asset.Name, note);
    }

    async Task<string> DownloadFile(string url, string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        using var client = new HttpClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(path);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            if (total is long t && t > 0) status.Text = $"descargando Ghostscript {readTotal * 100 / t}%";
        }
        return path;
    }

    static async Task<bool> RunInstaller(string path)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            if (process is null) return false;
            await process.WaitForExitAsync();
            var ok = process.ExitCode == 0;
            process.Dispose();
            return ok;
        }
        catch
        {
            return false;
        }
    }

    void InstallFailed(string message)
    {
        status.Text = "instalacion incompleta";
        drop.Text = "Ghostscript no esta instalado";
        if (MessageBox.Show(this, $"{message}\n\nQuieres abrir la descarga oficial?", "Ghostscript", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            Process.Start(new ProcessStartInfo(GhostscriptPage) { UseShellExecute = true });
    }

    static Bitmap PdfIcon()
    {
        var image = new Bitmap(56, 56);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.Transparent);
        using var paper = new SolidBrush(Color.White);
        using var red = new SolidBrush(Color.FromArgb(210, 30, 36));
        using var dark = new Pen(Color.FromArgb(90, 90, 90), 2);
        using var fold = new SolidBrush(Color.FromArgb(230, 230, 230));

        g.FillRectangle(paper, 16, 6, 28, 38);
        g.DrawRectangle(dark, 16, 6, 28, 38);
        g.FillPolygon(fold, new Point[] { new(44, 6), new(44, 18), new(32, 6) });
        g.DrawLine(dark, 32, 6, 44, 18);
        g.FillRectangle(red, 8, 28, 40, 18);
        using var font = new Font(SystemFonts.DefaultFont.FontFamily, 8, FontStyle.Bold);
        TextRenderer.DrawText(g, "PDF", font, new Rectangle(10, 29, 36, 15), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return image;
    }

    void TrackProgress(string? line)
    {
        if (line is null) return;
        if (line.StartsWith("Processing pages") && int.TryParse(line.Split(' ').Last().Trim('.'), out var total)) totalPages = total;
        if (!line.StartsWith("Page ") || totalPages == 0 || !int.TryParse(line[5..], out var page)) return;

        var percent = Math.Min(100, Math.Max(1, page * 100 / totalPages));
        var totalPercent = Math.Min(99, ((Math.Max(1, targetAttempt) - 1) * 100 + percent) / TargetMaxAttempts);
        var text = findingTarget
            ? $"probando {activePpi} PPI {totalPercent}%"
            : $"procesando {percent}%";
        var progressValue = findingTarget ? totalPercent : percent;
        BeginInvoke(new Action(() =>
        {
            status.Text = text;
            progress.Value = progressValue;
        }));
    }

    bool TryGetTargetBytes(out long? bytes)
    {
        bytes = null;
        var text = target.Text.Trim();
        if (text.Length == 0) return true;

        text = text.Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mb)) return false;
        if (mb <= 0) return true;

        bytes = Math.Max(1, (long)Math.Floor(mb * 1024 * 1024));
        return true;
    }

    static string TempOutput(string dir, string name, string runId, int res) =>
        Path.Combine(dir, $"{name}.probando-{runId}-ppi{res}.pdf");

    static void DeleteIfExists(string? path)
    {
        if (path is not null && File.Exists(path)) File.Delete(path);
    }

    static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MB"
            : $"{Math.Max(1, bytes / 1024)} KB";

    static string? FindGhostscript()
    {
        string[] names = ["gswin64c.exe", "gswin32c.exe", "gs.exe"];
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in paths)
            foreach (var name in names)
                if (File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);

        string[] common =
        [
            @"C:\Program Files\gs",
            @"C:\Program Files (x86)\gs"
        ];
        foreach (var root in common)
            if (Directory.Exists(root))
                foreach (var name in names)
                    foreach (var exe in Directory.GetFiles(root, name, SearchOption.AllDirectories))
                        return exe;

        return null;
    }
}

static class PdfFileDialog
{
    const int Cancelled = unchecked((int)0x800704C7);

    public static string? Show(IntPtr owner)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(6))
        {
            try { return ShowNative(owner); }
            catch { }
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Abrir y comprimir PDF",
            Filter = "PDF (*.pdf)|*.pdf|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }

    static string? ShowNative(IntPtr owner)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogCom();
        try
        {
            var filters = new[]
            {
                new ComDlgFilterSpec("PDF (*.pdf)", "*.pdf"),
                new ComDlgFilterSpec("Todos los archivos (*.*)", "*.*")
            };
            dialog.SetFileTypes((uint)filters.Length, filters);
            dialog.SetFileTypeIndex(1);
            dialog.SetTitle("Abrir y comprimir PDF");
            dialog.SetOkButtonLabel("Abrir y comprimir");
            dialog.SetOptions(FileOpenOptions.ForceFileSystem | FileOpenOptions.PathMustExist | FileOpenOptions.FileMustExist | FileOpenOptions.NoChangeDir);

            var hr = dialog.Show(owner);
            if (hr == Cancelled) return null;
            Marshal.ThrowExceptionForHR(hr);

            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(SigDn.FileSysPath, out var pathPtr);
                try { return Marshal.PtrToStringUni(pathPtr); }
                finally { Marshal.FreeCoTaskMem(pathPtr); }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    class FileOpenDialogCom
    {
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ComDlgFilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string name;
        [MarshalAs(UnmanagedType.LPWStr)] public string spec;

        public ComDlgFilterSpec(string name, string spec)
        {
            this.name = name;
            this.spec = spec;
        }
    }

    [Flags]
    enum FileOpenOptions : uint
    {
        NoChangeDir = 0x8,
        ForceFileSystem = 0x40,
        PathMustExist = 0x800,
        FileMustExist = 0x1000
    }

    enum SigDn : uint
    {
        FileSysPath = 0x80058000
    }

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] ComDlgFilterSpec[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FileOpenOptions fos);
        void GetOptions(out FileOpenOptions pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName(out IntPtr pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SigDn sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
