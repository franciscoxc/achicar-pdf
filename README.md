# Achicar PDF

Achicar PDF es una app simple para Windows que reduce PDFs escaneados usando
Ghostscript. Esta pensada para tirar un PDF, elegir un tamano maximo o un PPI,
y guardar el resultado en la misma carpeta del archivo original.

## Descargar

La version 1.1 esta disponible en la pagina de releases:

- [Achicar.PDF.x64.exe](https://github.com/franciscoxc/achicar-pdf/releases/download/v1.1/Achicar.PDF.x64.exe): Windows x64.
- [Achicar.PDF.arm.exe](https://github.com/franciscoxc/achicar-pdf/releases/download/v1.1/Achicar.PDF.arm.exe): Windows ARM64.

## Como se usa

1. Abri `Achicar PDF`.
2. Si Ghostscript no esta instalado, la app lo descarga desde el GitHub oficial
   de Artifex y abre el instalador.
3. Arrastra un PDF al recuadro.
4. Tambien puedes hacer clic en el recuadro para buscar un PDF.
5. Usa `Tamano maximo que necesitas` para que la app busque automaticamente el
   PPI mas alto que queda por debajo de ese limite.
6. Si dejas el tamano vacio, usa el modo manual con el slider de PPI.

El archivo resultante se guarda en la misma carpeta del original. El nombre
incluye el PPI usado y si fue Color o Gris.

## Recomendaciones

- Escaneos comunes: Color, 100 PPI.
- Mejor calidad: Color, 150 PPI.
- Menor peso: Color, 75-90 PPI.
- Forzar gris solo conviene probarlo si un PDF concreto mejora.
- El modo inteligente busca entre 50 y 150 PPI.

## Ghostscript

Achicar PDF no incluye Ghostscript dentro del ejecutable. Si falta, descarga el
instalador oficial desde:

https://github.com/ArtifexSoftware/ghostpdl-downloads/releases

Ghostscript es un componente externo publicado por Artifex bajo licencia AGPL o
licencia comercial. Mas informacion:

https://ghostscript.com/releases/gsdnld.html

## Checksums v1.1

```text
d49bf608592f447197a78186230eb0901d294576ad2b80221304a6019064bd05  Achicar.PDF.arm.exe
53caac3386da2a670951aaa9a2debe6221f0cdd4b814094b97759992fb91037d  Achicar.PDF.x64.exe
```

## Compilar

Requisitos:

- Windows.
- .NET 8 SDK.

Desde `win-pdf-achicador`:

```bat
build-windows.bat
```

O con PowerShell:

```powershell
.\build-windows.ps1
```

Los ejecutables quedan en:

```text
publish\win-x64\Achicar PDF.exe
publish\win-arm64\Achicar PDF.exe
```

## Licencia

El codigo de Achicar PDF se publica bajo licencia MIT. Ghostscript es un
componente externo con su propia licencia.
