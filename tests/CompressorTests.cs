using System.Diagnostics;
using Xunit;

// Las pruebas con PDFs reales necesitan Ghostscript instalado (en CI se instala con choco).
public class CompressorTests : IDisposable
{
    static readonly string Gs = Ghostscript.Find() ?? throw new InvalidOperationException("Las pruebas necesitan Ghostscript instalado.");
    static readonly string Scan = Path.Combine(AppContext.BaseDirectory, "escaneo-200ppi.pdf");
    readonly string dir = Directory.CreateTempSubdirectory("achicar-pruebas").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    string Copy(string name)
    {
        var path = Path.Combine(dir, name);
        File.Copy(Scan, path);
        return path;
    }

    [Theory]
    [InlineData("2", 2_000_000L)]
    [InlineData("1,5", 1_500_000L)]
    [InlineData(" 0.5 ", 500_000L)]
    public void ParseaMegabytesDecimales(string text, long expected)
    {
        Assert.True(Compressor.TryParseMegabytes(text, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    public void SinTamanoEsModoManual(string text)
    {
        Assert.True(Compressor.TryParseMegabytes(text, out var bytes));
        Assert.Null(bytes);
    }

    [Fact]
    public void RechazaTamanoInvalido() => Assert.False(Compressor.TryParseMegabytes("dos", out _));

    [Fact]
    public void InterpolaEntreDosMedidas() =>
        // 100 KB a 50 PPI y 600 KB a 150 PPI: crece como ppi^1,63, así que 300 KB cae cerca de 98 PPI.
        Assert.Equal(95, Compressor.NextPpi([(50, 100_000), (150, 600_000)], 300_000));

    [Fact]
    public void ExtrapolaHastaElMaximo() =>
        Assert.Equal(Compressor.MaxPpi, Compressor.NextPpi([(50, 100_000), (150, 300_000)], 10_000_000));

    [Fact]
    public void TerminaCuandoElRangoEsDe5Ppi() =>
        Assert.Equal(0, Compressor.NextPpi([(50, 100_000), (95, 290_000), (100, 310_000)], 300_000));

    [Fact]
    public void TerminaSiNadaEntra() =>
        Assert.Equal(0, Compressor.NextPpi([(50, 400_000), (150, 900_000)], 300_000));

    [Fact]
    public async Task Pedir150PpiAUnEscaneoDe200LoAchicaDeVerdad()
    {
        using var compressor = new Compressor(Gs);
        var result = await compressor.Manual(Scan, 150, false);
        Assert.True(result.Size < new FileInfo(Scan).Length, $"{result.Size} >= {new FileInfo(Scan).Length}");
    }

    [Fact]
    public async Task AguantaPorcentajesEnElNombre()
    {
        using var compressor = new Compressor(Gs);
        var result = await compressor.Manual(Copy("Promo 20% descuento.pdf"), 100, false);
        Assert.True(File.Exists(result.Path));
        Assert.True(result.Size > 10_000); // no es la página en blanco que deja un fallo
    }

    [Fact]
    public async Task ModoInteligenteEntraEnElTamano()
    {
        var max = new FileInfo(Scan).Length / 2;
        using var compressor = new Compressor(Gs);
        var result = await compressor.ToTarget(Scan, max);
        Assert.NotNull(result);
        Assert.True(result.Size <= max);
        Assert.Equal(0, result.Ppi % 5);
        Assert.False(result.Gray);
    }

    [Fact]
    public async Task SiEntraSinReducirNoBajaLaResolucion()
    {
        using var compressor = new Compressor(Gs);
        var result = await compressor.ToTarget(Scan, new FileInfo(Scan).Length * 2);
        Assert.Null(result!.Ppi);
    }

    [Fact]
    public async Task AvisaSiElPdfTieneContrasena()
    {
        var locked = Path.Combine(dir, "con clave.pdf");
        var gs = Process.Start(Gs, ["-q", "-sDEVICE=pdfwrite", "-sOwnerPassword=dueno", "-sUserPassword=abrir", "-o", locked, Scan]);
        await gs.WaitForExitAsync();

        using var compressor = new Compressor(Gs);
        var error = await Assert.ThrowsAsync<CompressException>(() => compressor.Manual(locked, 100, false));
        Assert.Contains("contraseña", error.Message);
    }

    [Fact]
    public async Task AvisaSiElPdfEstaRoto()
    {
        var broken = Path.Combine(dir, "roto.pdf");
        File.WriteAllBytes(broken, File.ReadAllBytes(Scan)[..60_000]);

        using var compressor = new Compressor(Gs);
        await Assert.ThrowsAsync<CompressException>(() => compressor.Manual(broken, 100, false));
    }

    [Fact]
    public void DetectaFirmaDigital()
    {
        var signed = Path.Combine(dir, "firmado.pdf");
        File.WriteAllText(signed, "%PDF-1.7\n<< /Type /Sig /ByteRange [0 10 20 30] >>");
        Assert.True(Compressor.HasSignature(signed));
        Assert.False(Compressor.HasSignature(Scan));
    }

    [Fact]
    public void LeeLaVersionDeLaCarpetaDeGhostscript()
    {
        Assert.Equal(new Version(10, 5, 1), Ghostscript.VersionOf(@"C:\Program Files\gs\gs10.05.1\bin\gswin64c.exe"));
        Assert.Null(Ghostscript.VersionOf("/usr/bin/gs"));
    }
}
