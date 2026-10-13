# Achicar PDF v1.2

Más rápido, más simple y sin sorpresas.

## Novedades

- Modo inteligente unas 2 veces más rápido: prueba en paralelo y estima el PPI con la curva tamaño/PPI.
- Si el PDF ya entra sin bajar la resolución, no la baja. Ahora busca hasta 300 PPI (antes, hasta 150).
- Si en color no entra ni a 50 PPI, prueba en gris antes de rendirse.
- Varios PDF a la vez: arrastra varios archivos o una carpeta.
- `Enviar a → Achicar PDF` en el Explorador, y arrastrar PDFs sobre el ícono del `.exe`.
- Atajos `email 10` y `portal 2`, y la app recuerda el último tamaño máximo.
- El modo manual pasa a tres opciones: Liviano, Normal y Calidad.
- Muestra cuánto se ahorró: `14,2 MB → 1,9 MB (-87%)`.
- Instalación de Ghostscript en silencio (un solo permiso de Windows), con verificación de la firma digital del instalador.
- Avisa si Ghostscript es una versión vieja con fallas de seguridad y ofrece actualizarlo.

## Arreglos

- Pedir 150 PPI a un escaneo de 200 PPI no hacía nada (y a veces el resultado pesaba más que el original).
- Un `%` en el nombre del archivo (`Factura 100% pagada.pdf`) hacía fallar la compresión.
- Un PDF con contraseña o dañado producía una página en blanco presentada como éxito. Ahora lo explica.
- Si el resultado pesa más que el original, ya no lo guarda.
- Avisa antes de achicar un PDF con firma digital, porque la firma deja de ser válida.
- Los intentos ya no se escriben en la carpeta del usuario (OneDrive o Dropbox los subían a la nube), sino en `%TEMP%`.
- Cerrar la ventana a mitad de proceso ya no deja Ghostscript corriendo ni archivos temporales.
- 1 MB = 1.000.000 bytes, para que el resultado nunca supere el límite de un portal.
- Si la carpeta del original no deja escribir, guarda en Documentos.

## Por dentro

- .NET 10 (.NET 8 deja de tener soporte en noviembre de 2026).
- Pruebas automáticas del motor con Ghostscript real, y compilación en GitHub Actions.

## Binarios

- `Achicar.PDF.x64.exe`
- `Achicar.PDF.arm.exe`

## Nota sobre Ghostscript

Achicar PDF no incluye Ghostscript. Si no está instalado, la app descarga el
instalador oficial de Artifex. Ghostscript tiene licencia propia.
