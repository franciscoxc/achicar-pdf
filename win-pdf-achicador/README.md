# Achicar PDF

Compilar en Windows:

```bat
build-windows.bat
```

O con PowerShell:

```powershell
.\build-windows.ps1
```

Los exe quedan en:

```text
publish\win-x64\Achicar PDF.exe
publish\win-arm64\Achicar PDF.exe
```

Requiere Ghostscript instalado:

https://ghostscript.com/releases/gsdnld.html

Settings recomendados:

- Escaneos comunes: Color, 100 PPI.
- Mejor calidad: Color, 150 PPI.
- Menor peso: Color, 75-90 PPI.
- Forzar gris solo conviene probarlo si un PDF concreto mejora.
- Si `Tamano maximo que necesitas` esta vacio, usa el PPI elegido en el slider.
- Si `Tamano maximo que necesitas` tiene valor, busca automaticamente entre 50 y 150 PPI el PPI mas alto que quede por debajo del target.
- No usa `PDFSETTINGS=/ebook`; la compresion queda definida por parametros explicitos.
- Siempre usa deduplicacion de imagenes, compresion/subset de fuentes y object streams PDF 1.5.
