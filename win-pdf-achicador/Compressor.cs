using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

// Una pasada de Ghostscript ya terminada. Ppi null = sin bajar la resolución de las imágenes.
sealed record Attempt(int? Ppi, bool Gray, string Path, long Size);

sealed class CompressException(string message) : Exception(message);

// Motor sin WinForms (así se puede probar aparte): arma los argumentos de Ghostscript, busca el PPI
// que entra en un tamaño y guarda los intentos en su propia carpeta de %TEMP%, que borra al final.
sealed class Compressor(string gs, IProgress<(string Text, int Percent)>? progress = null) : IDisposable
{
    public const int MinPpi = 50;
    public const int MaxPpi = 300;
    const int Step = 5;
    const int Refinements = 3;

    readonly string tempDir = Path.Combine(Path.GetTempPath(), "AchicarPDF", Guid.NewGuid().ToString("N"));
    readonly List<Process> running = [];
    bool cancelled;
    int round;

    public bool Cancelled => cancelled;

    public Task<Attempt> Manual(string input, int ppi, bool gray) =>
        Run(input, ppi, gray, p => progress?.Report(($"procesando {p}%", p)));

    // La mejor calidad que entra en maxBytes. Si en color no entra ni a 50 PPI, prueba en gris; null si tampoco.
    public async Task<Attempt?> ToTarget(string input, long maxBytes) =>
        await Search(input, maxBytes, false) ?? await Search(input, maxBytes, true);

    async Task<Attempt?> Search(string input, long maxBytes, bool gray)
    {
        // Primera ronda en paralelo (Ghostscript usa un solo núcleo): sin reducir, el mínimo y un punto para medir la curva.
        var first = await Round(input, gray, [null, MinPpi, 150]);
        if (first[0].Size <= maxBytes) return first[0];

        var tried = first.Skip(1).ToList();
        for (var i = 0; i < Refinements; i++)
        {
            var ppi = NextPpi(tried.Select(a => (a.Ppi!.Value, a.Size)).ToList(), maxBytes);
            if (ppi == 0) break;
            tried.Add((await Round(input, gray, [ppi]))[0]);
        }
        return tried.Where(a => a.Size <= maxBytes).MaxBy(a => a.Ppi);
    }

    // Siguiente PPI a probar, o 0 si no hace falta. El tamaño crece como ppi^k, así que con dos medidas
    // se estima el PPI que da maxBytes. Va de 5 en 5: nadie distingue 117 de 118.
    public static int NextPpi(IReadOnlyList<(int Ppi, long Size)> tried, long maxBytes)
    {
        var fits = tried.Where(t => t.Size <= maxBytes).ToList();
        if (fits.Count == 0) return 0;
        var lo = fits.MaxBy(t => t.Ppi);
        var tooBig = tried.Where(t => t.Size > maxBytes).ToList();
        var hi = tooBig.Count > 0 ? tooBig.MinBy(t => t.Ppi).Ppi : MaxPpi + Step;
        if (hi - lo.Ppi <= Step) return 0;

        var other = tooBig.Count > 0 ? tooBig.MinBy(t => t.Ppi) : fits.Where(t => t.Ppi < lo.Ppi).DefaultIfEmpty(lo).MaxBy(t => t.Ppi);
        var guess = (lo.Ppi + hi) / 2.0;
        if (other.Ppi != lo.Ppi && other.Size != lo.Size)
        {
            var k = Math.Log((double)other.Size / lo.Size) / Math.Log((double)other.Ppi / lo.Ppi);
            if (k > 0) guess = lo.Ppi * Math.Pow((double)maxBytes / lo.Size, 1 / k);
        }
        return Math.Clamp((int)(guess / Step) * Step, lo.Ppi + Step, hi - Step);
    }

    async Task<Attempt[]> Round(string input, bool gray, int?[] ppis)
    {
        var text = (ppis.Length > 1 ? "analizando" : $"probando {ppis[0]} PPI") + (gray ? " en gris" : "");
        var percents = new int[ppis.Length];
        var results = await Task.WhenAll(ppis.Select((ppi, i) => Run(input, ppi, gray, p =>
        {
            percents[i] = p;
            var total = Math.Min(99, (round * 100 + percents.Min()) / (1 + Refinements));
            progress?.Report(($"{text} {total}%", total));
        })));
        round++;
        return results;
    }

    async Task<Attempt> Run(string input, int? ppi, bool gray, Action<int> onPercent)
    {
        Directory.CreateDirectory(tempDir);
        var output = Path.Combine(tempDir, $"{ppi?.ToString() ?? "max"}{(gray ? "-gris" : "")}.pdf");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(gs)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var arg in Args(input, output, ppi, gray)) process.StartInfo.ArgumentList.Add(arg);

        int pages = 0, done = 0;
        var password = false;
        void Track(string? line)
        {
            if (line is null) return;
            if (line.Contains("requires a password")) password = true;
            if (line.StartsWith("Processing pages") && int.TryParse(line.Split(' ').Last().Trim('.'), out var total)) pages = total;
            if (!line.StartsWith("Page ") || pages == 0 || !int.TryParse(line[5..], out var page)) return;
            done = page;
            onPercent(Math.Clamp(page * 100 / pages, 1, 100));
        }
        process.OutputDataReceived += (_, e) => Track(e.Data);
        process.ErrorDataReceived += (_, e) => Track(e.Data);

        lock (running)
        {
            if (cancelled) throw new OperationCanceledException();
            process.Start();
            running.Add(process);
        }
        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            process.WaitForExit(); // espera también las últimas líneas de salida
        }
        finally
        {
            lock (running) running.Remove(process);
        }

        if (cancelled) throw new OperationCanceledException();
        if (password) throw new CompressException("El PDF tiene contraseña. Quítasela y vuelve a intentarlo.");
        // Con un PDF ilegible Ghostscript igual sale con código 0 y entrega una página en blanco.
        if (process.ExitCode != 0 || pages == 0 || done < pages || !File.Exists(output))
            throw new CompressException("No pude leer el PDF: parece dañado o no es un PDF.");
        return new Attempt(ppi, gray, output, new FileInfo(output).Length);
    }

    static IEnumerable<string> Args(string input, string output, int? ppi, bool gray)
    {
        string[] common =
        [
            "-sDEVICE=pdfwrite", "-dCompatibilityLevel=1.5", "-dNOPAUSE", "-dBATCH", "-dSAFER", "-dUseFastColor=false",
            "-dDetectDuplicateImages=true", "-dCompressFonts=true", "-dSubsetFonts=true",
            "-dWriteXRefStm=true", "-dWriteObjStms=true",
            "-dDownsampleMonoImages=true", "-dMonoImageResolution=300"
        ];
        // Umbral 1.0: si no, Ghostscript solo reduce las imágenes que superan 1,5 veces el PPI pedido.
        string[] resolution = ppi is int res
            ? ["-dDownsampleColorImages=true", $"-dColorImageResolution={res}", "-dColorImageDownsampleThreshold=1.0",
               "-dDownsampleGrayImages=true", $"-dGrayImageResolution={res}", "-dGrayImageDownsampleThreshold=1.0"]
            : ["-dDownsampleColorImages=false", "-dDownsampleGrayImages=false"];
        string[] color = gray ? ["-sColorConversionStrategy=Gray", "-sProcessColorModel=DeviceGray", "-dOverrideICC"] : [];
        // Para Ghostscript un % en el nombre de salida es una plantilla de número de página.
        return [.. common, .. resolution, .. color, $"-sOutputFile={output.Replace("%", "%%")}", input];
    }

    // Mata las pasadas en curso y espera a que suelten sus archivos.
    public void Cancel()
    {
        Process[] processes;
        lock (running)
        {
            cancelled = true;
            processes = [.. running];
        }
        foreach (var process in processes)
        {
            try { process.Kill(true); process.WaitForExit(3000); }
            catch { } // ya había terminado
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(tempDir, true); }
        catch { } // lo que quede en %TEMP% lo limpia Windows
    }

    // MB decimales (1.000.000 bytes): algunos portales cuentan así y es el límite más estricto.
    public static bool TryParseMegabytes(string text, out long? bytes)
    {
        bytes = null;
        text = text.Trim().Replace(',', '.');
        if (text.Length == 0) return true;
        if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var mb)) return false;
        if (mb > 0) bytes = Math.Max(1, (long)(mb * 1_000_000));
        return true;
    }

    // Las firmas digitales guardan el rango firmado en /ByteRange; reescribir el PDF las invalida.
    // shortcut: lee el archivo entero en memoria; pasar a lectura por bloques si aparecen PDFs de cientos de MB.
    public static bool HasSignature(string path) => File.ReadAllBytes(path).AsSpan().IndexOf("/ByteRange"u8) >= 0;
}

static class Ghostscript
{
    // shortcut: versión mínima fija (corrige los CVE de 2024 y 2025); subirla cuando Artifex publique parches de seguridad.
    public static readonly Version MinSafe = new(10, 5, 0);

    // Primero la versión más nueva instalada en Program Files; después lo que haya en el PATH.
    public static string? Find()
    {
        string[] names = ["gswin64c.exe", "gswin32c.exe", "gs.exe", "gs"];
        var installed = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Where(dir => dir.Length > 0 && Directory.Exists(Path.Combine(dir, "gs")))
            .SelectMany(dir => names.SelectMany(name => Directory.GetFiles(Path.Combine(dir, "gs"), name, SearchOption.AllDirectories)))
            .OrderByDescending(path => VersionOf(path) ?? new Version());
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(dir => dir.Length > 0)
            .SelectMany(dir => names.Select(name => Path.Combine(dir, name)))
            .Where(File.Exists);
        return installed.Concat(onPath).FirstOrDefault();
    }

    // El instalador oficial deja Ghostscript en ...\gs\gs10.05.1\bin.
    public static Version? VersionOf(string path) =>
        Regex.Match(path, @"[\\/]gs(\d+\.\d+(?:\.\d+)?)[\\/]") is { Success: true } match ? new Version(match.Groups[1].Value) : null;
}
