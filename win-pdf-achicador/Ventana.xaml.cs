using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Formas = System.Windows.Shapes;

public partial class Ventana : Window
{
    const string GhostscriptApi = "https://api.github.com/repos/ArtifexSoftware/ghostpdl-downloads/releases/latest";
    const string GhostscriptPage = "https://ghostscript.com/releases/gsdnld.html";
    const string Flecha = "", Tilde = "";
    static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Achicar PDF", "tamano-maximo.txt");

    readonly string[] startupFiles;
    string? gs;
    Compressor? active;
    string? salida; // el último PDF achicado, para «mostrar ahora»
    bool working, updateOffered, buscando, anticipando, conBorrar, borrando, encima;
    string? nuevoExe; // descargado y verificado: reemplaza al actual al reiniciar

    public Ventana(string[] files)
    {
        startupFiles = files;
        InitializeComponent();
        Reposo();
        try { target.Text = File.ReadAllText(SettingsFile).Trim(); } catch { } // la primera vez no hay tamaño guardado
        UpdateModeUi();

        actualizar.Click += async (_, _) =>
        {
            if (nuevoExe is null) await BuscarActualizacion(manual: true);
            else Reiniciar();
        };
        target.TextChanged += (_, _) => UpdateModeUi();
        // Solo megas enteros: nadie necesita 2,5 MB, y así el campo chico alcanza.
        target.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        borrar.Click += (_, _) => Borrar();
        // La cruz gira un cuarto de vuelta antes de borrar: dice qué va a hacer.
        borrar.MouseEnter += (_, _) => Resorte.Animar((borrar, "giro"), 90, v => borrarGiro.Angle = v, rigidez: 380, rebote: 0.3);
        borrar.MouseLeave += (_, _) => Resorte.Animar((borrar, "giro"), 0, v => borrarGiro.Angle = v, rigidez: 380, rebote: 0.3);
        emailPreset.Click += (_, _) => Elegir(10);
        portalPreset.Click += (_, _) => Elegir(2);
        mostrar.Click += (_, _) => OpenOutput();
        cancelar.Click += (_, _) => active?.Cancel();

        // Los botones de adentro de la tarjeta también avisan su clic a la tarjeta: solo cuenta el de ella.
        tarjeta.Click += async (_, e) =>
        {
            if (e.OriginalSource == tarjeta) await PickAndCompress();
        };
        // Lo que va a pasar se insinúa: la tarjeta cede bajo el puntero antes de abrir el diálogo.
        tarjeta.PreviewMouseLeftButtonDown += (_, e) => { if (!EnBoton(e)) Escalar(0.985); };
        tarjeta.PreviewMouseLeftButtonUp += (_, _) => Levantar();
        // Todo lo que se puede tocar responde: al pasar por encima la tarjeta se levanta y su sombra crece.
        tarjeta.MouseEnter += (_, _) => { encima = true; Levantar(); };
        tarjeta.MouseLeave += (_, _) => { encima = false; Levantar(); };
        actualizar.MouseEnter += (_, _) => Pulsar(actualizar, actualizarEscala, 1.08);
        actualizar.MouseLeave += (_, _) => Pulsar(actualizar, actualizarEscala, 1);
        actualizar.PreviewMouseLeftButtonDown += (_, _) => Pulsar(actualizar, actualizarEscala, 0.92);
        actualizar.PreviewMouseLeftButtonUp += (_, _) => Pulsar(actualizar, actualizarEscala, actualizar.IsMouseOver ? 1.08 : 1);
        borrar.PreviewMouseLeftButtonDown += (_, _) => Pulsar(borrar, borrarEscala, 0.8);
        DragEnter += (_, e) => Arrastre(e);
        DragOver += (_, e) => Arrastre(e);
        // WPF avisa DragLeave también al pasar de un elemento a otro adentro: cuenta solo si salió de la ventana.
        DragLeave += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight) Anticipar(false);
        };
        Drop += (_, e) =>
        {
            Anticipar(false);
            if (gs is null || working || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped) return;
            Apretar();
            _ = CompressAll(dropped);
        };
        // Sin esto Ghostscript seguiría corriendo solo y dejaría sus temporales.
        Closing += (_, _) =>
        {
            active?.Cancel();
            active?.Dispose();
        };
        ContentRendered += async (_, _) =>
        {
            // La prensa se luce una vez al abrir: aprieta y suelta.
            Resorte.Animar((icono, "ancho"), 1, v => iconoEscala.ScaleX = v, desde: 0.7, rigidez: 300, rebote: 0.35);
            AddToSendTo();
            actualizar.ToolTip = $"Versión {Actualizador.Actual} · buscar actualización";
            _ = BuscarActualizacion(manual: false);
            // La versión anterior se cierra apenas abre esta: un rato después ya se puede borrar.
            Luego(3000, Actualizador.BorrarViejo);
            if (await EnsureGhostscript() && startupFiles.Length > 0) await CompressAll(startupFiles);
        };
    }

    int ManualPpi => int.Parse((string)new[] { light, normal, quality }.First(r => r.IsChecked == true).Tag);

    static bool EnBoton(System.Windows.Input.MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is Button b) return b.Name != "tarjeta";
        return false;
    }

    async Task PickAndCompress()
    {
        if (working || !await EnsureGhostscript()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Elige los PDF para achicar",
            Filter = "PDF (*.pdf)|*.pdf|Todos los archivos (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            Apretar();
            await CompressAll(dialog.FileNames);
        }
    }

    async Task CompressAll(string[] paths)
    {
        if (working || gs is null) return;
        // Una carpeta cuenta como todos los PDF que tiene adentro.
        var files = paths.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.pdf") : [p])
            .Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (files.Length == 0)
        {
            Problema("Eso no es un PDF");
            return;
        }
        if (!Compressor.TryParseMegabytes(target.Text, out var maxBytes))
        {
            Problema("Tamaño inválido");
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, target.Text.Trim());
        }
        catch { } // recordar el tamaño es un extra

        working = true;
        Trabajo(cancelable: true);
        var results = new List<(string file, Hecho hecho)>();
        try
        {
            foreach (var file in files)
            {
                var hecho = await CompressOne(file, maxBytes, files.Length > 1 ? $"{results.Count + 1} de {files.Length}: " : "");
                results.Add((file, hecho));
                if (hecho.Cancelled) break;
            }
        }
        finally
        {
            working = false;
        }

        var done = results.Where(r => r.hecho.Output is not null).ToList();
        var problem = results.FirstOrDefault(r => r.hecho.Output is null);
        // El resultado reemplaza la línea «original: ...» y sigue en rojo.
        Poner(titulo, files.Length == 1 ? Path.GetFileName(files[0]) : $"{files.Length} PDF");
        titulo.ToolTip = files.Length == 1 ? files[0] : null;
        Rojo(true);
        if (files.Length == 1 && results[0].hecho is { Output: not null } h)
        {
            // El tamaño nuevo rueda desde el original hasta donde quedó.
            Resorte.Animar((bajada, "tamaño"), h.Final, v => bajada.Text = Ahorro(h.Original, (long)v, h.Note),
                desde: h.Original, rigidez: 60);
        }
        else
        {
            Poner(bajada, files.Length == 1
                ? results[0].hecho.Message
                : $"{done.Count} de {files.Length} achicados" + (problem.file is null ? "" : $"\n{Path.GetFileName(problem.file)}: {problem.hecho.Message}"));
        }

        if (done.Count > 0)
        {
            salida = done[^1].hecho.Output;
            Listo();
            Volar();
            // La prensa termina de apretar y suelta con un rebote: lo achicó.
            Resorte.Animar((icono, "ancho"), 1, v => iconoEscala.ScaleX = v, velocidad: -4, rigidez: 300, rebote: 0.4);
        }
        else
        {
            Reposo(conBajada: true);
            if (!results.Any(r => r.hecho.Cancelled)) Sacudir();
        }
    }

    sealed record Hecho(string? Output, string Message, bool Cancelled = false, long Original = 0, long Final = 0, string Note = "");

    async Task<Hecho> CompressOne(string input, long? maxBytes, string label)
    {
        var finished = false; // los avisos de progreso llegan en diferido y no deben pisar el resultado
        using var compressor = new Compressor(gs!, new Progress<(string Text, int Percent)>(p =>
        {
            if (finished) return;
            estado.Text = p.Text;
            Progreso(p.Percent);
        }));
        active = compressor;
        try
        {
            var original = new FileInfo(input).Length;
            Poner(titulo, $"{label}{Path.GetFileName(input)}");
            titulo.ToolTip = input;
            Rojo(false);
            // Una cifra importante sube desde cero; al terminar, la misma sigue rodando hasta el resultado.
            Resorte.Animar((bajada, "tamaño"), original, v => bajada.Text = $"original: {FormatSize((long)v)}", desde: 0, rigidez: 120);
            estado.Text = "";
            Progreso(0);
            if (original <= maxBytes) return new(null, "Ya pesa menos de lo que necesitas");
            if (Compressor.HasSignature(input) && MessageBox.Show(
                    this,
                    $"«{Path.GetFileName(input)}» tiene firma digital. Si lo achicas, la firma deja de ser válida.\n\n¿Achicarlo igual?",
                    "PDF firmado",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return new(null, "No lo toqué porque tiene firma digital");

            var best = maxBytes is long max
                ? await compressor.ToTarget(input, max)
                : await compressor.Manual(input, ManualPpi, gray.IsChecked == true);
            if (best is null) return new(null, $"No entra ni a {Compressor.MinPpi} PPI en gris: sube el tamaño máximo");
            if (best.Size >= original) return new(null, "Ya estaba bien comprimido: no lo toqué");

            var output = Save(best, input);
            var note = (maxBytes is not null && best.Gray ? ", en gris" : "") +
                       (best.Ppi < 75 ? "\ncalidad baja: si puedes, sube el tamaño" : "");
            return new(output, Ahorro(original, best.Size, note), Original: original, Final: best.Size, Note: note);
        }
        catch (OperationCanceledException)
        {
            return new(null, "cancelado", Cancelled: true);
        }
        catch (Exception ex)
        {
            return new(null, ex.Message);
        }
        finally
        {
            finished = true;
            active = null;
        }
    }

    static string Ahorro(long original, long size, string note) =>
        $"{FormatSize(original)} → {FormatSize(size)} (-{100 - size * 100 / original}%){note}";

    // Junto al original; si esa carpeta no deja escribir (adjuntos, red), en Documentos.
    static string Save(Attempt best, string input)
    {
        var name = $"{Path.GetFileNameWithoutExtension(input)} {(best.Ppi is int ppi ? $"ppi{ppi}" : "optimizado")} {(best.Gray ? "Gris" : "Color")}.pdf";
        var output = Path.Combine(Path.GetDirectoryName(input)!, name);
        try
        {
            File.Move(best.Path, output, true);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name);
            File.Move(best.Path, output, true);
        }
        return output;
    }

    void OpenOutput()
    {
        if (salida is not null && File.Exists(salida))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{salida}\"") { UseShellExecute = true });
    }

    // ---- Los modos ----

    void UpdateModeUi()
    {
        var smart = target.Text.Trim().Length > 0 && target.Text.Trim() != "0";
        // Con un tamaño escrito manda el modo inteligente: el manual se apaga, despacio.
        manual.IsEnabled = !smart;
        // Con 0,5 encima del gris de Windows no se leía en modo oscuro: Primero, que se lea.
        Opacidad(manual, smart ? 0.75 : 1);
        targetLabel.SetResourceReference(TextBlock.ForegroundProperty, smart ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        megabytes.SetResourceReference(TextBlock.ForegroundProperty, smart ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");

        // La cruz crece desde el centro cuando hay algo para borrar, y se achica cuando no.
        var hay = target.Text.Length > 0;
        if (hay == conBorrar || borrando) return;
        conBorrar = hay;
        borrar.IsHitTestVisible = hay;
        Resorte.Animar((borrar, "escala"), hay ? 1 : 0.4, v => borrarEscala.ScaleX = borrarEscala.ScaleY = v, rigidez: 420, rebote: hay ? 0.4 : 0);
        Opacidad(borrar, hay ? 1 : 0);
    }

    // Los atajos no saltan al número: lo hacen rodar desde el que había.
    void Elegir(int mb)
    {
        int.TryParse(target.Text, out var desde);
        Resorte.Animar((target, "mb"), mb, v => target.Text = Math.Round(v).ToString(), desde: desde, rigidez: 160);
    }

    // ---- La tarjeta ----

    // Sin trabajo: la invitación a soltar PDF, centrada.
    void Reposo(bool conBajada = false)
    {
        if (!conBajada)
        {
            Poner(titulo, gs is null && updateOffered ? "Ghostscript no está instalado" : "Tira tus PDF aquí");
            titulo.ToolTip = null;
            Rojo(false);
            Poner(bajada, "o haz clic para buscar");
        }
        Zona(null);
    }

    void Trabajo(bool cancelable)
    {
        cancelar.Visibility = cancelable ? Visibility.Visible : Visibility.Collapsed;
        estado.Margin = new(0, 0, cancelable ? 96 : 0, 0);
        Zona(trabajo);
    }

    void Listo() => Zona(mostrar);

    // La zona de abajo muestra una sola cosa. Lo que llega entra subiendo; lo que se va se funde, y el
    // contenido sube o baja para hacerle lugar.
    void Zona(FrameworkElement? que)
    {
        foreach (var el in new FrameworkElement[] { trabajo, mostrar })
        {
            if (el == que)
            {
                if (el.Visibility == Visibility.Visible && el.Opacity > 0.5) continue;
                el.Visibility = Visibility.Visible;
                var corrida = el == mostrar ? mostrarCorrida : el.RenderTransform as TranslateTransform ?? (TranslateTransform)(el.RenderTransform = new TranslateTransform());
                Aparecer(el, corrida, desdeY: 12);
            }
            else if (el.Visibility == Visibility.Visible)
            {
                var ese = el;
                Opacidad(ese, 0, alTerminar: () => { if (ese.Opacity < 0.01) ese.Visibility = Visibility.Collapsed; });
            }
        }
        var y = que is null ? (zona.Height + zona.Margin.Top) / 2 : 0;
        Resorte.Animar((contenido, "y"), y, v => contenidoCorrida.Y = v, rigidez: 240, rebote: 0.12);
    }

    void Rojo(bool si) =>
        bajada.SetResourceReference(TextBlock.ForegroundProperty, si ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");

    // La barra no salta: corre hasta el porcentaje nuevo. Y la prensa aprieta a medida que avanza.
    void Progreso(int p)
    {
        Resorte.Animar((progreso, "valor"), p, v => progreso.Value = v, rigidez: 120);
        Resorte.Animar((icono, "ancho"), 1 - 0.18 * p / 100, v => iconoEscala.ScaleX = v, rigidez: 200);
    }

    // Un problema sacude la tarjeta y lo dice en rojo: un rebote corto que dice «esto no».
    void Problema(string texto)
    {
        Poner(titulo, "Tira tus PDF aquí");
        Rojo(true);
        Poner(bajada, texto);
        Reposo(conBajada: true);
        Sacudir();
    }

    void Arrastre(DragEventArgs e)
    {
        var sirve = gs is not null && !working && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = sirve ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (sirve) Anticipar(true);
    }

    // Con archivos encima la tarjeta se levanta, el borde se vuelve continuo y la prensa se asoma. Si se van
    // sin soltar, todo vuelve a su lugar.
    void Anticipar(bool si)
    {
        if (si == anticipando) return;
        anticipando = si;
        Levantar();
        borde.StrokeDashArray = si ? null : new DoubleCollection([4, 3]);
        borde.SetResourceReference(Formas.Shape.StrokeProperty, si ? "AccentFillColorDefaultBrush" : "ControlStrongStrokeColorDefaultBrush");
        Resorte.Animar((icono, "escala"), si ? 1.1 : 1, v => iconoEscala.ScaleY = v, rigidez: 420, rebote: 0.3);
        Resorte.Animar((icono, "altura"), si ? -8 : 0, v => iconoAltura.Y = v, rigidez: 300, rebote: 0.3);
    }

    // Al recibir los PDF la prensa aprieta una vez y vuelve.
    void Apretar() =>
        Resorte.Animar((icono, "ancho"), 1, v => iconoEscala.ScaleX = v, velocidad: -5, rigidez: 420, rebote: 0.45);

    // Cuanto más se levanta la tarjeta, más sombra proyecta: con archivos encima, más que con el puntero.
    void Levantar()
    {
        Escalar(anticipando ? 1.02 : encima ? 1.01 : 1);
    }

    void Escalar(double v) =>
        Resorte.Animar((tarjeta, "escala"), v, x =>
        {
            tarjetaEscala.ScaleX = tarjetaEscala.ScaleY = x;
            // La sombra sigue a la escala en el mismo resorte: de 0,985 (hundida) a 1,02 (bien arriba).
            var n = Math.Clamp((x - 0.985) / 0.035, 0, 1.2);
            sombra.BlurRadius = 4 + 18 * n;
            sombra.ShadowDepth = 1 + 6 * n;
            sombra.Opacity = 0.06 + 0.16 * n;
        }, rigidez: 500, rebote: 0.2);

    static void Pulsar(UIElement el, ScaleTransform escala, double v) =>
        Resorte.Animar((el, "pulso"), v, x => escala.ScaleX = escala.ScaleY = x, rigidez: 500, rebote: 0.3);

    // Los textos de la tarjeta no cambian de golpe: el nuevo aparece subiendo un poco sobre el anterior.
    static void Poner(TextBlock t, string texto)
    {
        Resorte.Olvidar((t, "tamaño")); // si estaba rodando un número, este texto lo reemplaza
        if (t.Text == texto) return;
        t.Text = texto;
        var corrida = t.RenderTransform as TranslateTransform ?? (TranslateTransform)(t.RenderTransform = new TranslateTransform());
        Aparecer(t, corrida, desdeY: 6);
    }

    // El PDF achicado sale de la prensa y vuela a «mostrar ahora», que late al recibirlo.
    void Volar()
    {
        if (Resorte.Quieto) return;
        var papel = Papel();
        capa.Children.Add(papel);
        var desde = icono.TranslatePoint(new Point(icono.ActualWidth / 2, icono.ActualHeight / 2), capa);
        var clave = (papel, "t");
        Resorte.Animar(clave, 1, t =>
        {
            // El destino se mide en cada cuadro: el botón todavía está subiendo a su lugar.
            var hasta = mostrar.TranslatePoint(new Point(mostrar.ActualWidth / 2, mostrar.ActualHeight / 2), capa);
            var cima = new Point((desde.X + hasta.X) / 2 + 40, Math.Min(desde.Y, hasta.Y) - 30);
            var u = 1 - t;
            var x = u * u * desde.X + 2 * u * t * cima.X + t * t * hasta.X;
            var y = u * u * desde.Y + 2 * u * t * cima.Y + t * t * hasta.Y;
            var e = 1 - 0.45 * t;
            papel.RenderTransform = new MatrixTransform(e, 0, 0, e, x - 9 * e, y - 11 * e);
            papel.Opacity = t < 0.85 ? 1 : (1 - t) / 0.15;
        }, desde: 0, rigidez: 70, alLlegar: () =>
        {
            capa.Children.Remove(papel);
            Resorte.Olvidar(clave);
            Resorte.Animar((mostrar, "latido"), 1, v => mostrarEscala.ScaleX = mostrarEscala.ScaleY = v, velocidad: 2.5, rigidez: 500, rebote: 0.5);
        });
    }

    // Una hoja chiquita con la banda roja del PDF del ícono.
    Grid Papel() => new()
    {
        Width = 18, Height = 22,
        Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.25 },
        Children =
        {
            new Border { Background = Brushes.White, CornerRadius = new(3), BorderThickness = new(0.5), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) },
            new Border { Height = 6, Margin = new(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Bottom, Background = (Brush)FindResource("Rojo") }
        }
    };

    // Lo que se borra va a parar a la cruz que se tocó: el número viaja hasta ella achicándose, y recién
    // entonces la cruz se va.
    void Borrar()
    {
        var texto = target.Text;
        // Antes de vaciar el campo: si no, UpdateModeUi saca la cruz antes de que llegue el número.
        borrando = texto.Length > 0 && !Resorte.Quieto;
        target.Clear();
        target.Focus();
        if (!borrando) return;
        var numero = new TextBlock { Text = texto, FontSize = target.FontSize, FontFamily = target.FontFamily };
        numero.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        numero.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        capa.Children.Add(numero);
        // Donde estaba el número: alineado a la derecha, antes del lugar que el Padding le deja a la cruz.
        var desde = target.TranslatePoint(new Point(target.ActualWidth - target.Padding.Right - numero.DesiredSize.Width - 2,
            (target.ActualHeight - numero.DesiredSize.Height) / 2), capa);
        var hasta = borrar.TranslatePoint(new Point(borrar.ActualWidth / 2, borrar.ActualHeight / 2), capa);
        var clave = (numero, "t");
        Resorte.Animar(clave, 1, t =>
        {
            var e = 1 - 0.85 * t;
            var w = numero.DesiredSize.Width * e;
            var h = numero.DesiredSize.Height * e;
            var cx = desde.X + numero.DesiredSize.Width / 2 + (hasta.X - desde.X - numero.DesiredSize.Width / 2) * t;
            var cy = desde.Y + numero.DesiredSize.Height / 2 + (hasta.Y - desde.Y - numero.DesiredSize.Height / 2) * t;
            numero.RenderTransform = new MatrixTransform(e, 0, 0, e, cx - w / 2, cy - h / 2);
            numero.Opacity = 1 - t * t;
        }, desde: 0, rigidez: 260, alLlegar: () =>
        {
            capa.Children.Remove(numero);
            Resorte.Olvidar(clave);
            borrando = false;
            UpdateModeUi();
        });
    }

    void Sacudir() =>
        Resorte.Animar((tarjeta, "corrida"), 0, v => tarjetaCorrida.X = v, velocidad: 700, rigidez: 900, rebote: 0.85);

    // ---- Ghostscript ----

    // true si hay un Ghostscript para usar. Si falta o es una versión vieja, ofrece instalar el oficial.
    async Task<bool> EnsureGhostscript()
    {
        gs ??= Ghostscript.Find();
        var version = gs is null ? null : Ghostscript.VersionOf(gs);
        if (gs is not null && (updateOffered || version is null || version >= Ghostscript.MinSafe)) return true;

        updateOffered = true;
        if (gs is null) Reposo();
        var question = gs is null
            ? "Para usar Achicar PDF necesitamos descargar un componente llamado Ghostscript.\n\nWindows te pedirá permiso: aprieta Sí. Después se abre el instalador de Ghostscript: avanza con Siguiente hasta el final.\n\nEsto se hace solo una vez."
            : $"Tu Ghostscript {version} es una versión vieja con fallas de seguridad conocidas.\n\n¿Instalamos la versión nueva? Windows te pedirá permiso y se abrirá el instalador de Ghostscript: avanza con Siguiente hasta el final.";
        if (MessageBox.Show(this, question, "Ghostscript", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return gs is not null;

        Trabajo(cancelable: false);
        estado.Text = "buscando instalador...";
        Progreso(0);
        string? installer = null;
        try
        {
            var asset = await GetGhostscriptInstaller();
            if (asset is null) return InstallFailed("No encontré un instalador compatible.");

            estado.Text = asset.Value.note;
            installer = await DownloadFile(asset.Value.url, asset.Value.name);
            if (!Authenticode.IsSignedBy(installer, "Artifex"))
                return InstallFailed("El instalador descargado no tiene una firma digital válida de Artifex, así que no lo ejecuté.");

            estado.Text = "instalando Ghostscript...";
            if (!await RunInstaller(installer)) return InstallFailed("No se pudo ejecutar el instalador.");

            gs = Ghostscript.Find();
            if (gs is null) return InstallFailed("La instalación terminó, pero no encontré Ghostscript.");
            updateOffered = false;
            Reposo();
            Resorte.Animar((icono, "ancho"), 1, v => iconoEscala.ScaleX = v, rigidez: 300, rebote: 0.4);
            return true;
        }
        catch (Exception ex)
        {
            return InstallFailed(ex.Message);
        }
        finally
        {
            try { if (installer is not null) File.Delete(installer); } catch { }
        }
    }

    static async Task<(string url, string name, string note)?> GetGhostscriptInstaller()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AchicarPDF");
        using var doc = JsonDocument.Parse(await client.GetStringAsync(GhostscriptApi));
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => new
            {
                Name = a.GetProperty("name").GetString() ?? "",
                Url = a.GetProperty("browser_download_url").GetString() ?? ""
            })
            .Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var arch = RuntimeInformation.OSArchitecture;
        var asset = arch == Architecture.X86
            ? assets.FirstOrDefault(a => a.Name.EndsWith("w32.exe", StringComparison.OrdinalIgnoreCase))
            : arch == Architecture.Arm64
                ? assets.FirstOrDefault(a => a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("aarch64", StringComparison.OrdinalIgnoreCase))
                : assets.FirstOrDefault(a => a.Name.EndsWith("w64.exe", StringComparison.OrdinalIgnoreCase));

        asset ??= assets.FirstOrDefault(a => a.Name.EndsWith("w64.exe", StringComparison.OrdinalIgnoreCase));
        if (asset is null) return null;

        var note = arch == Architecture.Arm64 && !asset.Name.Contains("arm", StringComparison.OrdinalIgnoreCase)
            ? "descargando x64 para Windows ARM..."
            : $"descargando {asset.Name}...";
        return (asset.Url, asset.Name, note);
    }

    async Task<string> DownloadFile(string url, string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        using var client = new HttpClient();
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(path);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read, ultimo = -1;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            if (total is not long t || t <= 0) continue;
            var p = (int)(readTotal * 100 / t);
            if (p == ultimo) continue; // llegan miles de pedazos con el mismo porcentaje
            ultimo = p;
            estado.Text = $"descargando Ghostscript {p}%";
            Progreso(p);
        }
        return path;
    }

    static async Task<bool> RunInstaller(string path)
    {
        try
        {
            // ponytail-keep: sin /S. Artifex lo desactivó a propósito desde Ghostscript 10.01.0: con /S el asistente
            // se abre igual (probado con 10.08 en Windows 11 ARM), así que el aviso previo dice que hay que darle Siguiente.
            var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            if (process is null) return false;
            await process.WaitForExitAsync();
            var ok = process.ExitCode == 0;
            process.Dispose();
            return ok;
        }
        catch
        {
            return false;
        }
    }

    // Devuelve si igual queda un Ghostscript usable (el viejo, cuando falló una actualización).
    bool InstallFailed(string message)
    {
        Reposo();
        if (gs is null)
        {
            Rojo(true);
            Poner(bajada, "instalación incompleta");
        }
        if (MessageBox.Show(this, $"{message}\n\n¿Quieres abrir la descarga oficial?", "Ghostscript", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(GhostscriptPage) { UseShellExecute = true });
        return gs is not null;
    }

    // ---- La flecha de actualizar ----

    async Task BuscarActualizacion(bool manual)
    {
        if (buscando) return;
        buscando = true;
        Girar(true);
        try
        {
            var nueva = await Actualizador.Buscar();
            if (nueva is null)
            {
                if (manual)
                {
                    Girar(false);
                    Tildar();
                    Burbuja($"Ya tienes la última versión ({Actualizador.Actual})");
                }
                return;
            }
            Girar(false);
            var ultimo = -1;
            nuevoExe = await Actualizador.Descargar(nueva, new Progress<int>(p =>
            {
                if (p == ultimo) return; // el progreso llega cientos de veces con el mismo porcentaje
                ultimo = p;
                Anillo(p / 100.0);
            }));
            Estirar();
            actualizar.ToolTip = $"La versión {nueva.Version} ya está descargada";
        }
        catch (UnauthorizedAccessException) when (manual)
        {
            // La carpeta del programa no deja escribir (Archivos de programa, por ejemplo): a mano.
            Process.Start(new ProcessStartInfo(Actualizador.Pagina) { UseShellExecute = true });
            Burbuja("Baja la versión nueva de la página que se abrió");
        }
        catch (Exception) when (manual)
        {
            Burbuja("No pude buscar actualizaciones");
        }
        catch { } // la búsqueda al abrir es silenciosa: sin internet, no molesta
        finally
        {
            buscando = false;
            Girar(false);
            if (nuevoExe is null) Opacidad(anillo, 0);
        }
    }

    // Mientras busca, la flecha gira: lo único lineal, porque gira sin parar. Al terminar no frena en seco:
    // completa la vuelta con resorte.
    void Girar(bool si)
    {
        if (si && !Resorte.Quieto)
        {
            giro.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
            return;
        }
        var angulo = giro.Angle;
        giro.BeginAnimation(RotateTransform.AngleProperty, null);
        Resorte.Animar((giro, "angulo"), Math.Ceiling(angulo / 360) * 360, v => giro.Angle = v, desde: angulo, rigidez: 120);
    }

    // La descarga llena un anillo alrededor de la flecha.
    void Anillo(double fraccion)
    {
        // StrokeDashArray se mide en grosores de trazo: la circunferencia completa es π·(28 − 2)/2.
        var vuelta = Math.PI * 13;
        anillo.StrokeDashArray = new DoubleCollection([vuelta * fraccion, vuelta]);
        if (anillo.Opacity < 1) Opacidad(anillo, 1);
    }

    // Descargada la versión nueva, la flecha se estira ahí mismo hasta decir «Reiniciar para actualizar».
    void Estirar()
    {
        AutomationProperties.SetName(actualizar, "Reiniciar para actualizar");
        actualizarTexto.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Resorte.Animar((actualizarCaja, "ancho"), actualizarTexto.DesiredSize.Width, v => actualizarCaja.Width = v, desde: 32, rigidez: 260, rebote: 0.15);
        Opacidad(actualizarAcento, 1);
        Opacidad(actualizarTexto, 1);
        Opacidad(anillo, 0);
        flecha.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
    }

    // «Ya tienes la última»: la flecha se vuelve tilde un momento y vuelve a ser flecha.
    void Tildar()
    {
        void Cambiar(string glifo)
        {
            flecha.Text = glifo;
            Resorte.Animar((flecha, "escala"), 1, v => flechaEscala.ScaleX = flechaEscala.ScaleY = v, desde: 0.4, rigidez: 420, rebote: 0.4);
        }
        Cambiar(Tilde);
        Luego(1800, () => Cambiar(Flecha));
    }

    // Los avisos de la flecha salen de la flecha misma y vuelven a ella.
    void Burbuja(string texto)
    {
        var letra = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        letra.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var escala = new ScaleTransform(0.6, 0.6);
        var b = new Border
        {
            CornerRadius = new(8), Padding = new(12, 8, 12, 8), BorderThickness = new(1), MaxWidth = 300, Child = letra,
            RenderTransformOrigin = new(1, 0), RenderTransform = escala, Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.18 }
        };
        b.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorTertiaryBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        capa.Children.Add(b);
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var esquina = actualizar.TranslatePoint(new Point(actualizar.ActualWidth, actualizar.ActualHeight + 6), capa);
        Canvas.SetLeft(b, esquina.X - b.DesiredSize.Width);
        Canvas.SetTop(b, esquina.Y);
        Resorte.Animar((b, "escala"), 1, v => escala.ScaleX = escala.ScaleY = v, desde: 0.6, rigidez: 380, rebote: 0.2);
        Opacidad(b, 1, desde: 0);
        Luego(2600, () =>
        {
            Resorte.Animar((b, "escala"), 0.6, v => escala.ScaleX = escala.ScaleY = v, rigidez: 380);
            Opacidad(b, 0, alTerminar: () =>
            {
                capa.Children.Remove(b);
                Resorte.Olvidar((b, "escala"));
                Resorte.Olvidar((b, "opacidad"));
            });
        });
    }

    void Reiniciar()
    {
        if (working) return; // no cortar un PDF a la mitad
        try
        {
            Actualizador.Reemplazar(nuevoExe!);
            Close();
        }
        catch
        {
            Burbuja("No pude reiniciar: cierra y abre el programa");
        }
    }

    // ---- Movimiento ----

    // Lo que no viene de ningún lado entra con lo que lo trae: sube o baja un poco mientras aparece.
    static void Aparecer(FrameworkElement el, TranslateTransform corrida, double desdeY)
    {
        corrida.Y = Resorte.Quieto ? 0 : desdeY;
        Opacidad(el, 1, desde: 0);
        Resorte.Animar((el, "y"), 0, v => corrida.Y = v, desde: desdeY, rigidez: 260, rebote: 0.2);
    }

    // Con «Efectos de animación» apagado, lo que entra o sale se funde en vez de deslizarse.
    static void Opacidad(UIElement el, double destino, double? desde = null, Action? alTerminar = null)
    {
        if (Resorte.Quieto)
        {
            var a = new DoubleAnimation(destino, TimeSpan.FromMilliseconds(160));
            if (desde is double d) a.From = d;
            if (alTerminar is not null) a.Completed += (_, _) => alTerminar();
            el.BeginAnimation(OpacityProperty, a);
            return;
        }
        el.BeginAnimation(OpacityProperty, null);
        Resorte.Animar((el, "opacidad"), destino, v => el.Opacity = Math.Clamp(v, 0, 1), desde: desde, rigidez: 240, alLlegar: alTerminar);
    }

    static void Luego(int ms, Action accion)
    {
        if (ms <= 0) { accion(); return; }
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            accion();
        };
        t.Start();
    }

    // "Enviar a → Achicar PDF" en el menú del Explorador. Se rehace en cada inicio por si el .exe cambió de lugar.
    static void AddToSendTo()
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var link = shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Achicar PDF.lnk"));
            link.TargetPath = Environment.ProcessPath;
            link.Save();
        }
        catch { } // si una política bloquea WScript, solo falta el atajo
    }

    static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MB"
            : $"{Math.Max(1, bytes / 1024)} KB";
}
