# Achicar PDF v1.2.1

Retoques de diseño: la ventana es más baja y el resultado se ve donde estás mirando.

## Cambios

- El resultado (`3,7 MB → 1,8 MB (-53%)`) aparece en rojo dentro del recuadro, en lugar de la línea «original».
- El botón «mostrar ahora» también va dentro del recuadro, debajo del resultado, y abre la carpeta con el PDF achicado seleccionado.
- El campo del tamaño máximo es más chico, acepta solo megas enteros (hasta 3 cifras) y alinea el número a la derecha. «MB» quedó afuera del campo.
- El botón para borrar el tamaño es un círculo rojo dentro del campo y solo aparece cuando hay algo escrito.
- Modo manual: «Pequeño», «Normal» y «Más calidad», y «Quitar color» en lugar de «Forzar gris».
- Antes de instalar Ghostscript, la app avisa que hay que avanzar con «Siguiente» en su asistente: Artifex no permite instalarlo en silencio.

## Binarios

- `Achicar.PDF.x64.exe`
- `Achicar.PDF.arm.exe`

## Nota sobre Ghostscript

Achicar PDF no incluye Ghostscript. Si no está instalado, la app descarga el
instalador oficial de Artifex. Ghostscript tiene licencia propia.
