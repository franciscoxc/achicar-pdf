using System.Runtime.InteropServices;
using Xunit;

// Aparte de CompressorTests: estas no necesitan Ghostscript.
public class ActualizadorTests
{
    const string Release = """
        {"tag_name": "v1.3.0", "assets": [
          {"name": "Achicar.PDF.arm.exe", "size": 10, "digest": "sha256:aa", "browser_download_url": "https://x/arm.exe"},
          {"name": "Achicar.PDF.x64.exe", "size": 20, "digest": "sha256:bb", "browser_download_url": "https://x/x64.exe"}]}
        """;

    [Fact]
    public void Elige_el_exe_de_la_pc_solo_si_es_mas_nueva()
    {
        Assert.Equal("https://x/arm.exe", Actualizador.Elegir(Release, new Version(1, 2, 1), Architecture.Arm64)!.Url);
        Assert.Equal("bb", Actualizador.Elegir(Release, new Version(1, 2, 1), Architecture.X64)!.Sha256);
        // El ensamblado dice 1.3.0.0: no tiene que parecer más vieja que el tag v1.3.0.
        Assert.Null(Actualizador.Elegir(Release, new Version(1, 3, 0, 0), Architecture.X64));
        Assert.Null(Actualizador.Elegir(Release, new Version(1, 4, 0), Architecture.X64));
    }

    [Fact]
    public void Sin_huella_no_actualiza()
    {
        var sinHuella = Release.Replace("\"digest\": \"sha256:bb\", ", "");
        Assert.Throws<InvalidDataException>(() => Actualizador.Elegir(sinHuella, new Version(1, 2, 1), Architecture.X64));
    }
}
