using ArteInventariado.WinForms.Helpers;
using System.Runtime.InteropServices;

namespace ArteInventariado.WinForms;

/// <summary>
/// Focused, data-free smoke for the global F11 window-presentation contract.
/// It deliberately exercises both owned modal and non-modal secondary windows
/// without starting the application services or opening the business database.
/// </summary>
internal static class FullScreenSmokeRunner
{
    private static readonly float[] DpiScales = [1f, 1.25f, 1.5f];

    public static int Run(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.FullScreenSmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var log = new List<string> { $"START={DateTimeOffset.UtcNow:O}" };
        var result = 1;
        using var host = new Form
        {
            Text = "F11 smoke host",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(20, 20),
            Size = new Size(320, 200),
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = UITheme.BackgroundColor
        };

        host.Shown += (_, _) =>
        {
            try
            {
                host.Activate();
                PumpMessages();

                foreach (var scale in DpiScales)
                {
                    VerifyPrimaryWindowAndSecondaryForms(scale, outputDirectory, log);
                }

                VerifyQuickPosCustomerAndDiscount(outputDirectory, log);
                VerifyNestedOwnerChain(log);
                VerifyPreExistingTopMostIsOnlyRestoredOnExit(log);
                VerifyClosingWhileFullScreenDoesNotLeaveTrackedState(log);
                log.Add("RESULT=OK");
                result = 0;
            }
            catch (Exception ex)
            {
                log.Add("RESULT=FAILED");
                log.Add(ex.ToString());
            }
            finally
            {
                log.Add($"END={DateTimeOffset.UtcNow:O}");
                File.WriteAllLines(Path.Combine(outputDirectory, "fullscreen-smoke.log"), log);
                host.Close();
            }
        };

        System.Windows.Forms.Application.Run(host);
        return result;
    }

    private static void VerifyPrimaryWindowAndSecondaryForms(
        float scale,
        string outputDirectory,
        ICollection<string> log)
    {
        using var primary = CreateForm("Principal smoke F11");
        if (scale != 1f)
        {
            primary.Scale(new SizeF(scale, scale));
        }

        primary.Show();
        PumpMessages();

        var originalBorder = primary.FormBorderStyle;
        var originalWindowState = primary.WindowState;
        var originalBounds = primary.Bounds;

        FullScreenHelper.ToggleFullScreen(primary);
        PumpMessages();

        Assert(FullScreenHelper.IsFullScreen(primary), "F11 no registró el estado fullscreen.");
        Assert(primary.FormBorderStyle == FormBorderStyle.None, "F11 no quitó el borde de la ventana principal.");
        Assert(primary.WindowState == FormWindowState.Maximized, "F11 no maximizó la ventana principal.");
        Assert(!primary.TopMost, "F11 dejó la ventana principal en TopMost.");

        VerifyOwnedNonModalSecondary(primary, scale, log);
        VerifyOwnedModalSecondary(primary, scale, log);
        SaveScreenshot(primary, Path.Combine(outputDirectory, $"f11-primary-{ScaleName(scale)}.png"));

        FullScreenHelper.ExitFullScreen(primary);
        PumpMessages();

        Assert(!FullScreenHelper.IsFullScreen(primary), "Salir de F11 dejó estado fullscreen registrado.");
        Assert(primary.FormBorderStyle == originalBorder, "Salir de F11 no restauró el borde original.");
        Assert(primary.WindowState == originalWindowState, "Salir de F11 no restauró el estado original.");
        Assert(primary.Bounds == originalBounds, "Salir de F11 no restauró el tamaño/posición original.");
        Assert(!primary.TopMost, "Salir de F11 dejó TopMost pegado.");

        log.Add($"F11 ON/OFF @ {scale:P0}=OK; secundarias modal/no modal visibles y con Owner=OK");
    }

    private static void VerifyOwnedNonModalSecondary(Form primary, float scale, ICollection<string> log)
    {
        using var secondary = CreateForm($"Secundaria no modal {scale:P0}");
        secondary.Show(primary);
        PumpMessages();

        Assert(secondary.Visible, "La secundaria no modal no quedó visible durante F11.");
        Assert(ReferenceEquals(secondary.Owner, primary), "La secundaria no modal perdió su Owner.");
        Assert(!secondary.TopMost, "La secundaria no modal quedó TopMost sin necesidad.");

        secondary.Activate();
        PumpMessages();
        Assert(Form.ActiveForm == secondary || secondary.ContainsFocus,
            $"La secundaria no modal no recibió foco (activa={Form.ActiveForm?.Text ?? "<ninguna>"}; contieneFoco={secondary.ContainsFocus}; enfocada={secondary.Focused}).");

        secondary.Close();
        PumpMessages();
        primary.Activate();
        WaitForActiveForm(primary, "Al cerrar la secundaria no modal no volvió el foco al principal.");
        log.Add($"No modal @ {scale:P0}=OK");
    }

    private static void VerifyOwnedModalSecondary(Form primary, float scale, ICollection<string> log)
    {
        using var dialog = CreateForm($"Diálogo modal {scale:P0}");
        var wasVisible = false;
        var hadOwner = false;
        var receivedFocus = false;
        dialog.Shown += (_, _) =>
        {
            wasVisible = dialog.Visible;
            hadOwner = ReferenceEquals(dialog.Owner, primary);
            dialog.Activate();
            PumpMessages();
            receivedFocus = Form.ActiveForm == dialog || dialog.ContainsFocus;
            dialog.BeginInvoke(dialog.Close);
        };

        dialog.ShowDialog(primary);
        PumpMessages();

        Assert(wasVisible, "El diálogo modal no se mostró durante F11.");
        Assert(hadOwner, "El diálogo modal perdió su Owner.");
        Assert(receivedFocus,
            $"El diálogo modal no recibió foco durante F11 (activa={Form.ActiveForm?.Text ?? "<ninguna>"}; contieneFoco={dialog.ContainsFocus}; enfocada={dialog.Focused}).");

        primary.Activate();
        WaitForActiveForm(primary, "Al cerrar el diálogo modal no volvió el foco al principal.");
        log.Add($"Modal @ {scale:P0}=OK");
    }

    private static void VerifyPreExistingTopMostIsOnlyRestoredOnExit(ICollection<string> log)
    {
        using var primary = CreateForm("Restauración TopMost");
        primary.TopMost = true;
        primary.Show();
        PumpMessages();

        FullScreenHelper.ToggleFullScreen(primary);
        PumpMessages();
        Assert(!primary.TopMost, "F11 conservó TopMost durante fullscreen.");

        FullScreenHelper.ExitFullScreen(primary);
        PumpMessages();
        Assert(primary.TopMost, "F11 no restauró el TopMost previo al salir.");

        primary.TopMost = false;
        log.Add("TopMost previo: desactivado en F11 y restaurado al salir=OK");
    }

    private static void VerifyQuickPosCustomerAndDiscount(string outputDirectory, ICollection<string> log)
    {
        var quickPosFixture = PosBarcodeSmokeRunner.CreateQuickFormForFullScreenSmoke(outputDirectory);
        using var quickPos = quickPosFixture.Form;
        quickPos.WindowState = FormWindowState.Normal;
        quickPos.ClientSize = new Size(1280, 720);
        quickPos.Show();
        PumpMessages();

        FullScreenHelper.ToggleFullScreen(quickPos);
        PumpMessages();
        Assert(FullScreenHelper.IsFullScreen(quickPos) && !quickPos.TopMost,
            "POS rápido quedó TopMost durante F11.");

        var pickerWasVisible = false;
        var pickerHadOwner = false;
        var pickerHadFocus = false;
        var pickerWasAbovePos = false;
        using (var pickerCloser = new System.Windows.Forms.Timer { Interval = 50 })
        {
            pickerCloser.Tick += (_, _) =>
            {
                var picker = System.Windows.Forms.Application.OpenForms
                    .OfType<CustomerSalePickerForm>()
                    .FirstOrDefault();
                if (picker is null)
                {
                    return;
                }

                pickerWasVisible = picker.Visible;
                pickerHadOwner = ReferenceEquals(picker.Owner, quickPos);
                pickerHadFocus = Form.ActiveForm == picker || picker.ContainsFocus;
                pickerWasAbovePos = IsAboveInZOrder(picker, quickPos);
                pickerCloser.Stop();
                picker.DialogResult = DialogResult.Cancel;
                picker.Close();
            };

            pickerCloser.Start();
            Assert(quickPos.SimulateQuickShortcutForSmoke(Keys.F3), "F3 no abrió el selector de cliente del POS rápido.");
        }

        PumpMessages();
        Assert(pickerWasVisible, "El selector de cliente del POS rápido no quedó visible en F11.");
        Assert(pickerHadOwner, "El selector de cliente del POS rápido no conservó su Owner.");
        Assert(pickerHadFocus, "El selector de cliente del POS rápido no recibió foco en F11.");
        Assert(pickerWasAbovePos, "El selector de cliente del POS rápido quedó detrás del POS en F11.");
        Assert(quickPos.BarcodeHasFocusForSmoke, "El POS rápido no recuperó el foco al cerrar clientes.");

        quickPos.AddManualSelectionForSmoke(quickPosFixture.AddableItem);
        Assert(quickPos.SimulateQuickShortcutForSmoke(Keys.F6), "F6 no abrió Descuentos en el POS rápido.");
        Assert(quickPos.PaymentOverlayVisibleForSmoke && quickPos.PaymentOverlayZIndexForSmoke == 0,
            "Descuentos no quedó al frente del POS rápido durante F11.");
        quickPos.CancelQuickCheckoutForSmoke();

        FullScreenHelper.ToggleFullScreen(quickPos);
        PumpMessages();
        Assert(!FullScreenHelper.IsFullScreen(quickPos), "El primer F11 de salida del POS rápido falló.");
        FullScreenHelper.ToggleFullScreen(quickPos);
        PumpMessages();
        Assert(FullScreenHelper.IsFullScreen(quickPos) && !quickPos.TopMost,
            "La segunda entrada F11 del POS rápido falló.");
        FullScreenHelper.ExitFullScreen(quickPos);
        log.Add("POS rápido → Clientes / Descuentos / F11 repetido=OK");
    }

    private static void VerifyNestedOwnerChain(ICollection<string> log)
    {
        using var dashboard = CreateForm("Dashboard propietario");
        dashboard.TopMost = true;
        dashboard.Show();
        PumpMessages();

        var originalBorder = dashboard.FormBorderStyle;
        var originalWindowState = dashboard.WindowState;
        var originalBounds = dashboard.Bounds;

        // The dashboard itself is the fullscreen root. The focus assertions
        // below deliberately use the immediate owners (A, then Dashboard),
        // not the fullscreen root inferred from the Owner chain.
        FullScreenHelper.ToggleFullScreen(dashboard);
        PumpMessages();

        Assert(FullScreenHelper.IsFullScreen(dashboard), "Dashboard no entró en F11.");
        Assert(dashboard.FormBorderStyle == FormBorderStyle.None, "F11 no quitó el borde del dashboard.");
        Assert(dashboard.WindowState == FormWindowState.Maximized, "F11 no maximizó el dashboard.");
        Assert(!dashboard.TopMost, "F11 dejó el dashboard en TopMost.");

        VerifyNestedNonModalChain(dashboard, "primera entrada", log);
        VerifyNestedModalChain(dashboard, log);

        FullScreenHelper.ExitFullScreen(dashboard);
        PumpMessages();
        Assert(!FullScreenHelper.IsFullScreen(dashboard), "F11 OFF dejó estado registrado.");
        Assert(dashboard.FormBorderStyle == originalBorder, "F11 OFF no restauró el borde del dashboard.");
        Assert(dashboard.WindowState == originalWindowState, "F11 OFF no restauró el estado del dashboard.");
        Assert(dashboard.Bounds == originalBounds, "F11 OFF no restauró tamaño/posición del dashboard.");
        Assert(dashboard.TopMost, "F11 OFF no restauró el TopMost previo del dashboard.");

        // Re-enter F11 and repeat the complete A -> B -> A -> Dashboard
        // focus chain to catch stale coordinator state across toggles.
        FullScreenHelper.ToggleFullScreen(dashboard);
        PumpMessages();
        Assert(FullScreenHelper.IsFullScreen(dashboard), "La segunda entrada F11 del dashboard falló.");
        VerifyNestedNonModalChain(dashboard, "segunda entrada", log);

        FullScreenHelper.ExitFullScreen(dashboard);
        PumpMessages();
        Assert(!FullScreenHelper.IsFullScreen(dashboard), "La segunda salida F11 del dashboard falló.");
        Assert(dashboard.FormBorderStyle == originalBorder, "La segunda salida no restauró el borde del dashboard.");
        Assert(dashboard.WindowState == originalWindowState, "La segunda salida no restauró el estado del dashboard.");
        Assert(dashboard.Bounds == originalBounds, "La segunda salida no restauró tamaño/posición del dashboard.");
        Assert(dashboard.TopMost, "La segunda salida no restauró el TopMost del dashboard.");
        dashboard.TopMost = false;
        log.Add("Dashboard F11 → A → B → A → Dashboard; repetición; modal/no modal=OK");
    }

    private static void VerifyNestedNonModalChain(Form dashboard, string pass, ICollection<string> log)
    {
        using var formA = CreateForm($"Secundaria A ({pass})");
        formA.Show(dashboard);
        PumpMessages();
        Helpers.FullScreenPresentationCoordinator.OnFullScreenStateChanged();
        Assert(formA.Visible, $"Secundaria A no quedó visible ({pass}).");
        Assert(ReferenceEquals(formA.Owner, dashboard), $"Secundaria A perdió su Owner ({pass}).");

        using var formB = CreateForm($"Secundaria B ({pass})");
        formB.Show(formA);
        PumpMessages();
        Helpers.FullScreenPresentationCoordinator.OnFullScreenStateChanged();
        Assert(formB.Visible, $"Secundaria B no quedó visible ({pass}).");
        Assert(ReferenceEquals(formB.Owner, formA), $"Secundaria B perdió su Owner inmediato ({pass}).");
        Assert(Form.ActiveForm == formB || formB.ContainsFocus,
            $"Secundaria B no recibió foco ({pass}; activa={Form.ActiveForm?.Text ?? "<ninguna>"}; contieneFoco={formB.ContainsFocus}; enfocada={formB.Focused}).");
        Assert(IsAboveInZOrder(formB, formA), $"Secundaria B no quedó encima de A ({pass}).");

        formB.Close();
        WaitForActiveForm(formA, $"Al cerrar B, A no recuperó el foco ({pass}).");

        formA.Close();
        WaitForActiveForm(dashboard, $"Al cerrar A, el dashboard no recuperó el foco ({pass}).");
        log.Add($"Cadena no modal A→B→A→Dashboard ({pass})=OK");
    }

    private static void VerifyNestedModalChain(Form dashboard, ICollection<string> log)
    {
        using var formA = CreateForm("Secundaria A modal");
        formA.Show(dashboard);
        PumpMessages();
        Helpers.FullScreenPresentationCoordinator.OnFullScreenStateChanged();

        using var formB = CreateForm("Secundaria B modal");
        var appearedAbove = false;
        var receivedFocus = false;
        formB.Shown += (_, _) =>
        {
            Helpers.FullScreenPresentationCoordinator.OnFullScreenStateChanged();
            appearedAbove = formB.Visible && ReferenceEquals(formB.Owner, formA) &&
                IsAboveInZOrder(formB, formA);
            formB.Activate();
            PumpMessages();
            receivedFocus = Form.ActiveForm == formB || formB.ContainsFocus;
            formB.BeginInvoke(formB.Close);
        };

        formB.ShowDialog(formA);
        PumpMessages();
        Assert(appearedAbove, "Diálogo B modal no quedó encima de A.");
        Assert(receivedFocus,
            $"Diálogo B modal no recibió foco (activa={Form.ActiveForm?.Text ?? "<ninguna>"}; contieneFoco={formB.ContainsFocus}; enfocada={formB.Focused}).");
        WaitForActiveForm(formA, "Al cerrar B modal, A no recuperó el foco.");

        formA.Close();
        WaitForActiveForm(dashboard, "Al cerrar A modal, el dashboard no recuperó el foco.");
        log.Add("Cadena modal A→B→A→Dashboard=OK");
    }

    private static void VerifyClosingWhileFullScreenDoesNotLeaveTrackedState(ICollection<string> log)
    {
        var primary = CreateForm("Cierre fullscreen");
        primary.Show();
        PumpMessages();
        FullScreenHelper.ToggleFullScreen(primary);
        PumpMessages();
        primary.Close();
        PumpMessages();

        Assert(!FullScreenHelper.IsFullScreen(primary), "Cerrar en F11 dejó una ventana huérfana registrada.");
        primary.Dispose();
        log.Add("Cierre durante F11: sin estado huérfano=OK");
    }

    private static Form CreateForm(string title) => new()
    {
        Text = title,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(80, 80),
        Size = new Size(920, 620),
        MinimumSize = new Size(640, 420),
        FormBorderStyle = FormBorderStyle.Sizable,
        AutoScaleMode = AutoScaleMode.Dpi,
        BackColor = UITheme.BackgroundColor
    };

    private static void SaveScreenshot(Form form, string path)
    {
        using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path);
    }

    private static void PumpMessages()
    {
        System.Windows.Forms.Application.DoEvents();
        Thread.Sleep(20);
        System.Windows.Forms.Application.DoEvents();
    }

    private static void WaitForActiveForm(Form expected, string message)
    {
        for (var attempt = 0; attempt < 25 && Form.ActiveForm != expected; attempt++)
        {
            PumpMessages();
        }

        Assert(Form.ActiveForm == expected || expected.ContainsFocus,
            $"{message} (activa={Form.ActiveForm?.Text ?? "<ninguna>"}; contieneFoco={expected.ContainsFocus}; enfocada={expected.Focused}; visible={expected.Visible}; habilitada={expected.Enabled}; handle={expected.IsHandleCreated}; owner={expected.Owner?.Text ?? "<ninguno>"}).");
    }


    private static string ScaleName(float scale) => scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        .Replace('.', '_');

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool IsAboveInZOrder(Form candidate, Form reference)
    {
        for (var handle = GetWindow(reference.Handle, GetWindowPrevious); handle != IntPtr.Zero;
             handle = GetWindow(handle, GetWindowPrevious))
        {
            if (handle == candidate.Handle)
            {
                return true;
            }
        }

        return false;
    }

    private const uint GetWindowPrevious = 3;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
}
