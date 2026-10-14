using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args));
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
    const string GhostscriptApi = "https://api.github.com/repos/ArtifexSoftware/ghostpdl-downloads/releases/latest";
    const string GhostscriptPage = "https://ghostscript.com/releases/gsdnld.html";
    const string DropHint = "Tira tus PDF aquí\no haz clic para buscar";
    static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Achicar PDF", "tamano-maximo.txt");

    readonly GroupBox smartBox = new() { Text = "modo inteligente", ForeColor = Color.Gray, Left = 16, Top = 12, Width = 294, Height = 76, Font = new Font(SystemFonts.DefaultFont.FontFamily, 7f) };
    readonly Label targetLabel = new() { Text = "Tamaño máximo que necesitas (MB)", Left = 12, Top = 22, Width = 260, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
    readonly TextBox target = new() { Left = 12, Top = 44, Width = 80, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
    readonly Button clearTarget = new() { Text = "x", Left = 98, Top = 44, Width = 22, Height = 22, FlatStyle = FlatStyle.Flat, ForeColor = Color.Red, Font = SystemFonts.DefaultFont, Enabled = false };
    readonly LinkLabel emailPreset = new() { Text = "email 10", Left = 136, Top = 47, AutoSize = true, Font = SystemFonts.DefaultFont };
    readonly LinkLabel portalPreset = new() { Text = "portal 2", Left = 210, Top = 47, AutoSize = true, Font = SystemFonts.DefaultFont };
    readonly GroupBox manualBox = new() { Text = "modo manual", ForeColor = Color.Gray, Left = 16, Top = 96, Width = 294, Height = 92, Font = new Font(SystemFonts.DefaultFont.FontFamily, 7f) };
    readonly CheckBox gray = new() { Text = "Forzar gris", Left = 12, Top = 24, Width = 180, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
    readonly RadioButton light = new() { Text = "Liviano", Tag = 75, Left = 12, Top = 56, Width = 84, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
    readonly RadioButton normal = new() { Text = "Normal", Tag = 100, Left = 100, Top = 56, Width = 84, Checked = true, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
    readonly RadioButton quality = new() { Text = "Calidad", Tag = 150, Left = 188, Top = 56, Width = 84, ForeColor = SystemColors.ControlText, Font = SystemFonts.DefaultFont };
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
    readonly string[] startupFiles;
    string? gs;
    Compressor? active;
    bool working;
    bool updateOffered;

    public MainForm(string[] files)
    {
        startupFiles = files;
        Text = "Achicar PDF";
        ClientSize = new Size(326, 486);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon; } catch { }
        try { target.Text = File.ReadAllText(SettingsFile).Trim(); } catch { } // la primera vez no hay tamaño guardado
        clearTarget.FlatAppearance.BorderSize = 0;
        drop.Cursor = Cursors.Hand;

        target.TextChanged += (_, _) => UpdateModeUi();
        clearTarget.Click += (_, _) => target.Clear();
        emailPreset.LinkClicked += (_, _) => target.Text = "10";
        portalPreset.LinkClicked += (_, _) => target.Text = "2";
        showOutput.Click += (_, _) => OpenOutput();
        cancel.Click += (_, _) => active?.Cancel();
        // Sin esto Ghostscript seguiría corriendo solo y dejaría sus temporales.
        FormClosing += (_, _) =>
        {
            active?.Cancel();
            active?.Dispose();
        };

        drop.PdfImage = PdfIcon();
        smartBox.Controls.AddRange([targetLabel, target, clearTarget, emailPreset, portalPreset]);
        manualBox.Controls.AddRange([gray, light, normal, quality]);
        Controls.AddRange([smartBox, manualBox, drop, result, outputName, status, cancel, showOutput, progress]);
        progress.BringToFront();
        UpdateModeUi();

        Shown += async (_, _) =>
        {
            AddToSendTo();
            if (await EnsureGhostscript() && startupFiles.Length > 0) await CompressAll(startupFiles);
        };
        drop.Click += async (_, _) => await PickAndCompress();

        drop.DragEnter += (_, e) =>
        {
            if (gs is not null && !working && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        drop.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) _ = CompressAll(files);
        };
    }

    int ManualPpi => (int)new[] { light, normal, quality }.First(r => r.Checked).Tag!;

    async Task PickAndCompress()
    {
        if (working || !await EnsureGhostscript()) return;
        if (PdfFileDialog.Show(Handle) is string path) await CompressAll([path]);
    }

    async Task CompressAll(string[] paths)
    {
        if (working || gs is null) return;
        // Una carpeta cuenta como todos los PDF que tiene adentro.
        var files = paths.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.pdf") : [p])
            .Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        HideOutput();
        result.Text = "";
        if (files.Length == 0)
        {
            status.Text = "eso no es un PDF";
            return;
        }
        if (!Compressor.TryParseMegabytes(target.Text, out var maxBytes))
        {
            status.Text = "tamaño inválido";
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, target.Text.Trim());
        }
        catch { } // recordar el tamaño es un extra

        working = true;
        cancel.Enabled = cancel.Visible = true;
        progress.Value = 0;
        progress.Visible = true;
        var results = new List<(string file, string? output, string message)>();
        try
        {
            foreach (var file in files)
            {
                var (output, message, cancelled) = await CompressOne(file, maxBytes, files.Length > 1 ? $"{results.Count + 1} de {files.Length}: " : "");
                results.Add((file, output, message));
                if (cancelled) break;
            }
        }
        finally
        {
            working = false;
            cancel.Enabled = cancel.Visible = false;
            progress.Visible = false;
            status.Text = "";
        }

        var done = results.Where(r => r.output is not null).ToList();
        var problem = results.FirstOrDefault(r => r.output is null);
        result.Text = files.Length == 1
            ? results[0].message
            : $"{done.Count} de {files.Length} achicados" + (problem.file is null ? "" : $"\n{Path.GetFileName(problem.file)}: {problem.message}");
        if (done.Count > 0) ShowOutput(done[^1].output!);
    }

    async Task<(string? output, string message, bool cancelled)> CompressOne(string input, long? maxBytes, string label)
    {
        var finished = false; // los avisos de progreso llegan en diferido y no deben pisar el resultado
        using var compressor = new Compressor(gs!, new Progress<(string Text, int Percent)>(p =>
        {
            if (finished) return;
            status.Text = p.Text;
            progress.Value = p.Percent;
        }));
        active = compressor;
        try
        {
            var original = new FileInfo(input).Length;
            drop.DimIcon = false;
            drop.Text = $"{label}{Path.GetFileName(input)}\noriginal: {FormatSize(original)}";
            if (original <= maxBytes) return (null, "Ya pesa menos de lo que necesitas", false);
            if (Compressor.HasSignature(input) && MessageBox.Show(
                    this,
                    $"«{Path.GetFileName(input)}» tiene firma digital. Si lo achicas, la firma deja de ser válida.\n\n¿Achicarlo igual?",
                    "PDF firmado",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return (null, "No lo toqué porque tiene firma digital", false);

            var best = maxBytes is long max
                ? await compressor.ToTarget(input, max)
                : await compressor.Manual(input, ManualPpi, gray.Checked);
            if (best is null) return (null, $"No entra ni a {Compressor.MinPpi} PPI en gris: sube el tamaño máximo", false);
            if (best.Size >= original) return (null, "Ya estaba bien comprimido: no lo toqué", false);

            var output = Save(best, input);
            var note = (maxBytes is not null && best.Gray ? ", en gris" : "") +
                       (best.Ppi < 75 ? "\ncalidad baja: si puedes, sube el tamaño" : "");
            return (output, $"{FormatSize(original)} → {FormatSize(best.Size)} (-{100 - best.Size * 100 / original}%){note}", false);
        }
        catch (OperationCanceledException)
        {
            return (null, "cancelado", true);
        }
        catch (Exception ex)
        {
            return (null, ex.Message, false);
        }
        finally
        {
            finished = true;
            active = null;
        }
    }

    // Junto al original; si esa carpeta no deja escribir (adjuntos, red), en Documentos.
    static string Save(Attempt best, string input)
    {
        var name = $"{Path.GetFileNameWithoutExtension(input)} {(best.Ppi is int ppi ? $"ppi{ppi}" : "optimizado")} {(best.Gray ? "Gris" : "Color")}.pdf";
        var output = Path.Combine(Path.GetDirectoryName(input)!, name);
        try
        {
            File.Move(best.Path, output, true);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name);
            File.Move(best.Path, output, true);
        }
        return output;
    }

    void UpdateModeUi()
    {
        var smart = target.Text.Trim().Length > 0 && target.Text.Trim() != "0";
        clearTarget.Enabled = target.Text.Length > 0;
        gray.Enabled = light.Enabled = normal.Enabled = quality.Enabled = !smart;
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

    // "Enviar a → Achicar PDF" en el menú del Explorador. Se rehace en cada inicio por si el .exe cambió de lugar.
    static void AddToSendTo()
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var link = shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Achicar PDF.lnk"));
            link.TargetPath = Environment.ProcessPath;
            link.Save();
        }
        catch { } // si una política bloquea WScript, solo falta el atajo
    }

    // true si hay un Ghostscript para usar. Si falta o es una versión vieja, ofrece instalar el oficial.
    async Task<bool> EnsureGhostscript()
    {
        gs ??= Ghostscript.Find();
        var version = gs is null ? null : Ghostscript.VersionOf(gs);
        if (gs is not null && (updateOffered || version is null || version >= Ghostscript.MinSafe)) return true;

        updateOffered = true;
        if (gs is null) drop.Text = "Ghostscript no está instalado";
        var question = gs is null
            ? "Para usar Achicar PDF necesitamos descargar un componente llamado Ghostscript.\n\nWindows te pedirá permiso: aprieta Sí. Después se abre el instalador de Ghostscript: avanza con Siguiente hasta el final.\n\nEsto se hace solo una vez."
            : $"Tu Ghostscript {version} es una versión vieja con fallas de seguridad conocidas.\n\n¿Instalamos la versión nueva? Windows te pedirá permiso y se abrirá el instalador de Ghostscript: avanza con Siguiente hasta el final.";
        if (MessageBox.Show(this, question, "Ghostscript", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
        {
            if (gs is null) status.Text = "Ghostscript no instalado";
            return gs is not null;
        }

        status.Visible = true;
        status.Text = "buscando instalador...";
        string? installer = null;
        try
        {
            var asset = await GetGhostscriptInstaller();
            if (asset is null) return InstallFailed("No encontré un instalador compatible.");

            status.Text = asset.Value.note;
            installer = await DownloadFile(asset.Value.url, asset.Value.name);
            if (!Authenticode.IsSignedBy(installer, "Artifex"))
                return InstallFailed("El instalador descargado no tiene una firma digital válida de Artifex, así que no lo ejecuté.");

            status.Text = "instalando Ghostscript...";
            if (!await RunInstaller(installer)) return InstallFailed("No se pudo ejecutar el instalador.");

            gs = Ghostscript.Find();
            if (gs is null) return InstallFailed("La instalación terminó, pero no encontré Ghostscript.");
            status.Text = "";
            drop.Text = DropHint;
            return true;
        }
        catch (Exception ex)
        {
            return InstallFailed(ex.Message);
        }
        finally
        {
            try { if (installer is not null) File.Delete(installer); } catch { }
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
            // ponytail-keep: sin /S. Artifex lo desactivó a propósito desde Ghostscript 10.01.0: con /S el asistente
            // se abre igual (probado con 10.08 en Windows 11 ARM), así que el aviso previo dice que hay que darle Siguiente.
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

    // Devuelve si igual queda un Ghostscript usable (el viejo, cuando falló una actualización).
    bool InstallFailed(string message)
    {
        status.Text = "instalación incompleta";
        if (gs is null) drop.Text = "Ghostscript no está instalado";
        if (MessageBox.Show(this, $"{message}\n\n¿Quieres abrir la descarga oficial?", "Ghostscript", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            Process.Start(new ProcessStartInfo(GhostscriptPage) { UseShellExecute = true });
        return gs is not null;
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

    static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MB"
            : $"{Math.Max(1, bytes / 1024)} KB";
}

// Comprueba la firma digital (Authenticode) de un ejecutable antes de correrlo como administrador.
static class Authenticode
{
    public static bool IsSignedBy(string path, string publisher)
    {
        var pathPtr = Marshal.StringToHGlobalUni(path);
        var fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = pathPtr }, fileInfo, false);
            var data = new WinTrustData { Size = (uint)Marshal.SizeOf<WinTrustData>(), UIChoice = 2, UnionChoice = 1, FileInfo = fileInfo }; // sin UI, archivo
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
#pragma warning disable SYSLIB0057 // X509CertificateLoader no lee el firmante de un .exe; esta es la vía que sigue funcionando
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0 &&
                   X509Certificate.CreateFromSignedFile(path).Subject.Contains(publisher, StringComparison.OrdinalIgnoreCase);
#pragma warning restore SYSLIB0057
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeHGlobal(pathPtr);
        }
    }

    [DllImport("wintrust.dll")]
    static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UIContext;
        public IntPtr SignatureSettings;
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
