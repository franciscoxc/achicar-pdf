# Achicar PDF v1.3

Diseño nuevo de Windows 11 y actualizaciones desde la misma app.

## Cambios

- Actualizaciones: al abrirse, la app busca si hay una versión nueva, y la flecha de la esquina busca a mano. Si hay una, la descarga, verifica su huella SHA-256 contra la que publica GitHub y el botón pasa a «Reiniciar para actualizar». Desde esta versión no hace falta volver a bajarla de la página.
- La ventana usa el diseño de Windows 11: sigue el modo claro u oscuro y el color de acento de Windows.
- Movimiento: el recuadro se levanta cuando le acercas un PDF, la prensa del ícono aprieta mientras achica y el tamaño nuevo rueda hasta donde quedó. Con «Efectos de animación» apagado en Windows, todo eso pasa a fundidos.
- Al hacer clic en el recuadro se pueden elegir varios PDF a la vez.

## Binarios

- `Achicar.PDF.x64.exe`
- `Achicar.PDF.arm.exe`

## Nota sobre Ghostscript

Achicar PDF no incluye Ghostscript. Si no está instalado, la app descarga el
instalador oficial de Artifex. Ghostscript tiene licencia propia.
