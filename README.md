# Achicar PDF

Achicar PDF es una app simple para Windows que reduce PDFs escaneados usando
Ghostscript. Esta pensada para tirar un PDF, elegir un tamano maximo o un PPI,
y guardar el resultado en la misma carpeta del archivo original.

## Descargar

La version 1.0 esta disponible en la pagina de releases:

- `Achicar PDF x64.exe`: Windows x64.
- `Achicar PDF arm.exe`: Windows ARM64.

## Como se usa

1. Abri `Achicar PDF`.
2. Si Ghostscript no esta instalado, la app lo descarga desde el GitHub oficial
   de Artifex y abre el instalador.
3. Arrastra un PDF al recuadro.
4. Usa `Tamano maximo que necesitas` para que la app busque automaticamente el
   PPI mas alto que queda por debajo de ese limite.
5. Si dejas el tamano vacio, usa el modo manual con el slider de PPI.

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

## Checksums v1.0

```text
279767acb48e95ba463645da4bb7ca1bf9ce5d76d2eeb044c07be63ff6370b96  Achicar PDF arm.exe
79ebe50c484c683da623ecc2562b003c0bf97a28aad1178780f13c550c45431e  Achicar PDF x64.exe
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
