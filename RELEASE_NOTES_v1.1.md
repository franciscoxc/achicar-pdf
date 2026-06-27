# Achicar PDF v1.1

Mejoras de usabilidad y compatibilidad.

## Cambios

- La zona para tirar PDFs ahora tambien se puede tocar para buscar un archivo.
- El selector de archivo muestra el boton `Abrir y comprimir`.
- El progreso se ve mas grande y suma una barra fina en el borde inferior.
- Ghostscript elige automaticamente el filtro de imagen para evitar bloques negros en PDFs generados por apps de gestion.
- Se mantiene la compresion efectiva para PDFs escaneados.

## Binarios

- `Achicar.PDF.x64.exe`
- `Achicar.PDF.arm.exe`

## SHA-256

```text
d49bf608592f447197a78186230eb0901d294576ad2b80221304a6019064bd05  Achicar.PDF.arm.exe
53caac3386da2a670951aaa9a2debe6221f0cdd4b814094b97759992fb91037d  Achicar.PDF.x64.exe
```

## Nota sobre Ghostscript

Achicar PDF no incluye Ghostscript. Si no esta instalado, la app descarga el
instalador oficial de Artifex. Ghostscript tiene licencia propia.
