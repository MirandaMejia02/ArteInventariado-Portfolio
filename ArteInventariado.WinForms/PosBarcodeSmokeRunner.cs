using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using System.Diagnostics;

namespace ArteInventariado.WinForms;

internal static class PosBarcodeSmokeRunner
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    internal static (EmployeePOSForm Form, PosSkuSaleItem AddableItem) CreateQuickFormForFullScreenSmoke(
        string outputDirectory)
    {
        var fixture = new SmokeFixture(InstallationMode.Principal, outputDirectory);
        return (fixture.CreateForm(quick: true), fixture.Simple);
    }

    public static int Run(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.PosBarcodeSmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var configuredLog = Environment.GetEnvironmentVariable("ARTE_POS_BARCODE_SMOKE_LOG");
        var logPath = string.IsNullOrWhiteSpace(configuredLog)
            ? Path.Combine(outputDirectory, "pos-barcode-smoke.log")
            : Path.GetFullPath(configuredLog);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var log = new List<string>();
        void Checkpoint(string message)
        {
            var line = $"{DateTimeOffset.UtcNow:O} | {message}";
            log.Add(line);
            Console.WriteLine(line);
        }

        Checkpoint("START");
        try
        {
            ControlledShutdownState.Begin();
            var quickOnly = Environment.GetEnvironmentVariable("ARTE_POS_BARCODE_SMOKE_QUICK_ONLY") == "1";

            SmokeCreditPolicyLayout(outputDirectory, Checkpoint);
            SmokeCompletedSaleResize(outputDirectory, Checkpoint);

            foreach (var scale in new[] { 1f, 1.25f, 1.5f })
            {
                if (!quickOnly)
                {
                    SmokeLayout(
                        InstallationMode.Principal,
                        scale,
                        outputDirectory,
                        Checkpoint,
                        runInteractions: scale == 1f,
                        quick: false);
                    SmokeLayout(
                        InstallationMode.Consulta,
                        scale,
                        outputDirectory,
                        Checkpoint,
                        runInteractions: scale == 1f,
                        quick: false);
                }
                SmokeLayout(
                    InstallationMode.Principal,
                    scale,
                    outputDirectory,
                    Checkpoint,
                    runInteractions: scale == 1f,
                    quick: true);
                SmokeLayout(
                    InstallationMode.Consulta,
                    scale,
                    outputDirectory,
                    Checkpoint,
                    runInteractions: scale == 1f,
                    quick: true);
            }

            SmokeQuickPaginationScenarios(outputDirectory, Checkpoint);
            SmokeTraditionalEscapeClosesEmptyPos(outputDirectory, Checkpoint);

            Checkpoint("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            Checkpoint("RESULT=FAILED");
            Checkpoint(ex.ToString());
            return 1;
        }
        finally
        {
            Checkpoint("END");
            File.WriteAllLines(logPath, log);
        }
    }

    private static void SmokeTraditionalEscapeClosesEmptyPos(
        string outputDirectory,
        Action<string> checkpoint)
    {
        var fixture = new SmokeFixture(InstallationMode.Principal, outputDirectory);
        using var traditional = fixture.CreateForm(quick: false);
        traditional.StartPosition = FormStartPosition.Manual;
        traditional.Location = new Point(-32000, -32000);
        traditional.ShowInTaskbar = false;
        traditional.Show();
        System.Windows.Forms.Application.DoEvents();
        traditional.SimulateEscapeForSmoke();
        System.Windows.Forms.Application.DoEvents();

        if (!traditional.IsDisposed)
        {
            throw new InvalidOperationException("Escape no cerró el POS tradicional cuando el carrito estaba vacío.");
        }

        checkpoint("POS tradicional Escape cierra carrito vacío por flujo normal=OK");
    }

    public static int RunCreditContinuation(string? outputDirectory)
    {
        if (System.Windows.Forms.Application.MessageLoop)
        {
            return RunCreditContinuationCore(outputDirectory);
        }

        var result = 1;
        using var host = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1)
        };
        host.Shown += (_, _) =>
        {
            result = RunCreditContinuationCore(outputDirectory);
            host.Close();
        };
        System.Windows.Forms.Application.Run(host);
        return result;
    }

    private static int RunCreditContinuationCore(string? outputDirectory)
    {
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(Path.GetTempPath(), "ArteInventariado.PosCreditContinuationSmoke")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var logPath = Path.Combine(outputDirectory, "pos-credit-continuation-smoke.log");
        var log = new List<string>();
        void Checkpoint(string message)
        {
            var line = $"{DateTimeOffset.UtcNow:O} | {message}";
            log.Add(line);
            Console.WriteLine(line);
        }

        Checkpoint("START");
        try
        {
            ControlledShutdownState.Begin();
            Checkpoint("CASE=quick-credit-success START");
            SmokeQuickCreditAutoRegistrationAndDeduplication(outputDirectory, Checkpoint);
            Checkpoint("CASE=quick-credit-cancel START");
            SmokeQuickCreditCancellation(outputDirectory, Checkpoint);
            Checkpoint("CASE=quick-credit-failure-retry START");
            SmokeQuickCreditFailureRetainsDraftAndF8Retries(outputDirectory, Checkpoint);
            Checkpoint("CASE=quick-credit-multi-draft START");
            SmokeQuickCreditMultiDraftIsolation(outputDirectory, Checkpoint);
            Checkpoint("CASE=traditional-credit START");
            SmokeTraditionalCreditStillRegistersNormally(outputDirectory, Checkpoint);
            Checkpoint("RESULT=OK");
            return 0;
        }
        catch (Exception ex)
        {
            Checkpoint("RESULT=FAILED");
            Checkpoint(ex.ToString());
            return 1;
        }
        finally
        {
            Checkpoint("END");
            File.WriteAllLines(logPath, log);
        }
    }

    private static void SmokeQuickCreditAutoRegistrationAndDeduplication(
        string outputDirectory,
        Action<string> checkpoint)
    {
        var fixture = new SmokeFixture(InstallationMode.Principal, outputDirectory);
        using var form = fixture.CreateForm(quick: true);
        PrepareCreditForm(fixture, form, quick: true);
        checkpoint("CASE=quick-credit-success form-ready");
        form.AddManualSelectionForSmoke(fixture.Simple);
        form.SetSelectedCustomer(CreateSmokeCustomerSelection());

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SaleService.RegistrationGate = gate;
        var creditButton = OpenCreditMethod(form, quick: true);
        checkpoint("CASE=quick-credit-success credit-button-ready");
        using var driver = new CreditDialogDriver(
            fixture.SaleService,
            CreditDialogAction.ConfirmAll,
            closeReceipt: true,
            dismissRegistrationFailure: false);

        creditButton.PerformClick();
        WaitUntil(() =>
        {
            driver.ThrowIfFailed();
            return driver.DialogOpened;
        }, "apertura del diálogo de crédito en POS rápido");
        WaitUntil(() =>
        {
            driver.ThrowIfFailed();
            return fixture.SaleService.Requests.Count == 1;
        }, "envío de una sola venta de crédito al servicio");

        var request = fixture.SaleService.Requests[0];
        if (request.Credit is null ||
            request.Credit.ClienteId != 41 ||
            request.Credit.Lines.Sum(line => line.CantidadFinanciada) != 1)
        {
            throw new InvalidOperationException("El POS rápido no envió una solicitud de crédito totalmente financiada válida.");
        }

        if (!form.SaleProcessingForSmoke)
        {
            throw new InvalidOperationException("El POS rápido no quedó protegido durante el registro de crédito.");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!form.SimulateQuickShortcutForSmoke(Keys.F8))
            {
                throw new InvalidOperationException("F8 no fue reconocido durante el registro de crédito.");
            }
        }

        if (fixture.SaleService.Requests.Count != 1 || driver.DialogOpenCount != 1)
        {
            throw new InvalidOperationException(
                $"F8 repetido duplicó la operación o reabrió crédito: ventas={fixture.SaleService.Requests.Count}, diálogos={driver.DialogOpenCount}.");
        }

        gate.TrySetResult(true);
        WaitUntil(() =>
        {
            driver.ThrowIfFailed();
            return !form.SaleProcessingForSmoke &&
                form.CartLineCountForSmoke == 0 &&
                !form.PendingCreditRequestForSmoke &&
                driver.ReceiptClosed;
        }, "Venta completada después de crédito totalmente financiado");
        checkpoint("POS RÁPIDO crédito totalmente financiado + Venta completada + una sola venta=OK");
    }

    private static void SmokeQuickCreditCancellation(
        string outputDirectory,
        Action<string> checkpoint)
    {
        var fixture = new SmokeFixture(InstallationMode.Principal, outputDirectory);
        using var form = fixture.CreateForm(quick: true);
        PrepareCreditForm(fixture, form, quick: true);
        form.AddManualSelectionForSmoke(fixture.Simple);
        form.SetSelectedCustomer(CreateSmokeCustomerSelection());

        var creditButton = OpenCreditMethod(form, quick: true);
        using var driver = new CreditDialogDriver(
            fixture.SaleService,
            CreditDialogAction.Cancel,
            closeReceipt: false,
            dismissRegistrationFailure: false);
        creditButton.PerformClick();
        WaitUntil(() =>
        {
            driver.ThrowIfFailed();
            return driver.DialogOpened;
        }, "apertura del diálogo para cancelación de crédito");

        if (fixture.SaleService.Requests.Count != 0 ||
            form.CartLineCountForSmoke != 1 ||
            form.CartUnitCountForSmoke != 1 ||
            form.PendingCreditRequestForSmoke)
        {
            throw new InvalidOperationException("Cancelar crédito dejó una venta parcial o alteró el carrito.");
        }

        checkpoint("POS RÁPIDO cancelar crédito conserva carrito y no registra=OK");
    }

    private static void SmokeQuickCreditFailureRetainsDraftAndF8Retries(
        string outputDirectory,
        Action<string> checkpoint)
    {
        var fixture = new SmokeFixture(InstallationMode.Principal, outputDirectory);
        using var form = fixture.CreateForm(quick: true);
        PrepareCreditForm(fixture, form, quick: true);
        form.AddManualSelectionForSmoke(fixture.Simple);
        form.SetSelectedCustomer(CreateSmokeCustomerSelection());
        fixture.SaleService.FailNext = true;

        var creditButton = OpenCreditMethod(form, quick: true);
        using var driver = new CreditDialogDriver(
            fixture.SaleService,
            CreditDialogAction.ConfirmAll,
            closeReceipt: true,
            dismissRegistrationFailure: true);
        creditButton.PerformClick();
        WaitUntil(() =>
        {
            driver.ThrowIfFailed();
            return fixture.SaleService.FailureObserved && !form.SaleProcessingForSmoke;
        }, "fallo de registro conservando el draft");

        if (fixture.SaleService.Requests.Count != 1 ||
            form.CartLineCountForSmoke != 1 ||
            form.CartUnitCountForSmoke != 1 ||
            !form.PendingCreditRequestForSmoke ||
            driver.DialogOpenCount != 1)
        {
            throw new InvalidOperationException("Un fallo de registro no conservó el draft de crédito de forma coherente.");
        }

        if (!form.SimulateQuickShortcutForSmoke(Keys.F8))
        {
            throw new InvalidOperationException("F8 no intentó continuar una solicitud de crédito pendiente.");
        }

        WaitUntil(() =>
        {
            driver.ThrowIfFailed();
            return fixture.SaleService.Requests.Count == 2 &&
                !form.SaleProcessingForSmoke &&
                form.CartLineCountForSmoke == 0 &&
                !form.PendingCreditRequestForSmoke &&
                driver.ReceiptClosed;
        }, "reintento F8 de crédito pendiente");

        if (driver.DialogOpenCount != 1)
        {
            throw new InvalidOperationException("F8 reabrió el diálogo de crédito al reintentar una solicitud pendiente.");
        }

        checkpoint("POS RÁPIDO fallo conserva draft + F8 reintenta sin reabrir crédito=OK");
    }

    private static void SmokeQuickCreditMultiDraftIsolation(
        string outputDirectory,
        Action<string> checkpoint)
    {
        var fixture = new SmokeFixture(InstallationMode.Principal, outputDirectory);
        using var form = fixture.CreateForm(quick: true);
        PrepareCreditForm(fixture, form, quick: true);
        form.AddManualSelectionForSmoke(fixture.Simple);
        form.SetSelectedCustomer(CreateSmokeCustomerSelection());

        FindVisibleButtonByAccessibleName(form, "Nueva venta").PerformClick();
        WaitUntil(() => TryFindVisibleButton(form, "Venta 2") is not null, "creación del segundo draft");
        form.AddManualSelectionForSmoke(fixture.Assorted);
        FindVisibleButtonByAccessibleName(form, "Activar Venta 1").PerformClick();
        WaitUntil(
            () => form.CartLineCountForSmoke == 1 &&
                form.QuickCartCustomerForSmoke.Contains("Cliente smoke", StringComparison.Ordinal),
            "activación del draft A");

        var creditButton = OpenCreditMethod(form, quick: true);
        using var driver = new CreditDialogDriver(
            fixture.SaleService,
            CreditDialogAction.ConfirmAll,
            closeReceipt: true,
            dismissRegistrationFailure: false);
        creditButton.PerformClick();
        WaitUntil(() =>
        {
            driver.ThrowIfFailed();
            return fixture.SaleService.Requests.Count == 1 && driver.ReceiptClosed;
        }, "registro del draft A a crédito");

        FindVisibleButtonByAccessibleName(form, "Activar Venta 2").PerformClick();
        WaitUntil(() => form.CartLineCountForSmoke == 1, "restauración del draft B");
        if (!form.QuickCartCustomerForSmoke.Contains("Consumidor final", StringComparison.Ordinal) ||
            fixture.SaleService.Requests[0].Lines.Count != 1 ||
            fixture.SaleService.Requests[0].Lines[0].ProductoSkuId != fixture.Simple.ProductoSkuId)
        {
            throw new InvalidOperationException("El registro de crédito del draft A alteró el draft B.");
        }

        checkpoint("POS RÁPIDO crédito registra solo el draft activo y conserva draft B=OK");
    }

    private static void SmokeTraditionalCreditStillRegistersNormally(
        string outputDirectory,
        Action<string> checkpoint)
    {
        var fixture = new SmokeFixture(InstallationMode.Principal, outputDirectory);
        using var form = fixture.CreateForm(quick: false);
        PrepareCreditForm(fixture, form, quick: false);
        form.AddManualSelectionForSmoke(fixture.Simple);
        form.SetSelectedCustomer(CreateSmokeCustomerSelection());

        var creditButton = OpenCreditMethod(form, quick: false);
        using var driver = new CreditDialogDriver(
            fixture.SaleService,
            CreditDialogAction.ConfirmAll,
            closeReceipt: true,
            dismissRegistrationFailure: false);
        creditButton.PerformClick();
        WaitUntil(() =>
        {
            driver.ThrowIfFailed();
            return driver.DialogOpened &&
                !form.PaymentOverlayVisibleForSmoke &&
                form.PendingCreditRequestForSmoke;
        }, "confirmación de crédito en POS tradicional");

        if (fixture.SaleService.Requests.Count != 0 || !form.RegisterEnabledForSmoke)
        {
            throw new InvalidOperationException("El POS tradicional dejó de esperar el registro explícito de la venta.");
        }

        FindVisibleButton(form, "REGISTRAR VENTA").PerformClick();
        WaitUntil(() =>
        {
            driver.ThrowIfFailed();
            return fixture.SaleService.Requests.Count == 1 &&
                form.CartLineCountForSmoke == 0 &&
                driver.ReceiptClosed;
        }, "registro tradicional de venta a crédito");
        checkpoint("POS tradicional conserva confirmación y registro normal de crédito=OK");
    }

    private static void PrepareCreditForm(
        SmokeFixture fixture,
        EmployeePOSForm form,
        bool quick)
    {
        form.WindowState = FormWindowState.Normal;
        form.ClientSize = new Size(1280, 720);
        form.Show();
        WaitUntil(
            () => fixture.Catalog.SearchCalls > 0 &&
                !string.IsNullOrWhiteSpace(form.ScannerStatusForSmoke) &&
                Descendants(form).OfType<PosSkuCardControl>().Count() >= 4,
            $"carga del formulario {(quick ? "rápido" : "tradicional")} para crédito");
    }

    private static Button OpenCreditMethod(EmployeePOSForm form, bool quick)
    {
        if (quick)
        {
            form.ShowQuickCheckoutForSmoke();
        }
        else
        {
            FindVisibleButton(form, "OTROS MÉTODOS").PerformClick();
        }

        WaitUntil(
            () => TryFindVisibleButton(form, "Credito") is not null,
            $"método Crédito en POS {(quick ? "rápido" : "tradicional")}" );
        return FindVisibleButton(form, "Credito");
    }

    private static CustomerSaleSelection CreateSmokeCustomerSelection() =>
        new(41, "CLI-000041", "Cliente smoke", true);

    private static Button FindVisibleButton(Control root, string text) =>
        TryFindVisibleButton(root, text)
        ?? throw new InvalidOperationException($"No se encontró el botón visible '{text}'.");

    private static Button? TryFindVisibleButton(Control root, string text) =>
        Descendants(root)
            .OfType<Button>()
            .FirstOrDefault(button => button.Visible && button.Enabled && button.Text == text);

    private static Button FindVisibleButtonByAccessibleName(Control root, string accessibleName) =>
        Descendants(root)
            .OfType<Button>()
            .FirstOrDefault(button => button.Visible && button.Enabled && button.AccessibleName == accessibleName)
        ?? throw new InvalidOperationException($"No se encontró el botón visible '{accessibleName}'.");

    private enum CreditDialogAction
    {
        ConfirmAll,
        Cancel
    }

    private sealed class CreditDialogDriver : IDisposable
    {
        private readonly SmokeSaleService _saleService;
        private readonly CreditDialogAction _action;
        private readonly bool _closeReceipt;
        private readonly bool _dismissRegistrationFailure;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 75 };
        private bool _allClicked;
        private bool _failureDismissed;
        private bool _disposed;
        private CreditSaleDialog? _lastDialog;

        public CreditDialogDriver(
            SmokeSaleService saleService,
            CreditDialogAction action,
            bool closeReceipt,
            bool dismissRegistrationFailure)
        {
            _saleService = saleService;
            _action = action;
            _closeReceipt = closeReceipt;
            _dismissRegistrationFailure = dismissRegistrationFailure;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public bool DialogOpened { get; private set; }

        public int DialogOpenCount { get; private set; }

        public bool ReceiptClosed { get; private set; }

        public Exception? Failure { get; private set; }

        private void OnTick(object? sender, EventArgs e)
        {
            if (Failure is not null || _disposed)
            {
                return;
            }

            try
            {
                var dialog = System.Windows.Forms.Application.OpenForms
                    .OfType<CreditSaleDialog>()
                    .FirstOrDefault();
                if (dialog is not null)
                {
                    if (!ReferenceEquals(_lastDialog, dialog))
                    {
                        _lastDialog = dialog;
                        DialogOpened = true;
                        DialogOpenCount++;
                    }

                    if (_action == CreditDialogAction.Cancel)
                    {
                        dialog.DialogResult = DialogResult.Cancel;
                        dialog.Close();
                        return;
                    }

                    if (!_allClicked)
                    {
                        var all = TryFindVisibleButton(dialog, "Toda la compra");
                        if (all is null)
                        {
                            return;
                        }

                        all.PerformClick();
                        _allClicked = true;
                    }

                    var confirm = TryFindVisibleButton(dialog, "Confirmar credito");
                    if (confirm?.Enabled == true)
                    {
                        confirm.PerformClick();
                    }

                    return;
                }

                if (_closeReceipt)
                {
                    var receipt = System.Windows.Forms.Application.OpenForms
                        .OfType<SaleReceiptForm>()
                        .FirstOrDefault();
                    if (receipt is not null)
                    {
                        ReceiptClosed = true;
                        receipt.Close();
                        return;
                    }
                }

                if (_dismissRegistrationFailure &&
                    _saleService.FailureObserved &&
                    !_failureDismissed)
                {
                    _failureDismissed = true;
                    SendKeys.SendWait("{ENTER}");
                }
            }
            catch (Exception ex)
            {
                Failure = ex;
            }
        }

        public void ThrowIfFailed()
        {
            if (Failure is not null)
            {
                throw new InvalidOperationException("El driver del smoke de crédito falló.", Failure);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
        }
    }

    private static void SmokeLayout(
        InstallationMode mode,
        float scale,
        string outputDirectory,
        Action<string> checkpoint,
        bool runInteractions,
        bool quick)
    {
        var fixture = new SmokeFixture(mode, outputDirectory);
        using var form = fixture.CreateForm(quick);
        form.WindowState = FormWindowState.Normal;
        form.ClientSize = new Size(1280, 720);
        form.Show();
        WaitUntil(
            () => fixture.Catalog.SearchCalls > 0 &&
                !string.IsNullOrWhiteSpace(form.ScannerStatusForSmoke) &&
                Descendants(form).OfType<PosSkuCardControl>().Count() >= 4,
            $"carga POS {mode} DPI {ScaleName(scale)}");

        var cards = Descendants(form).OfType<PosSkuCardControl>().ToList();
        var productFallbackCard = cards.Single(card => card.ProductoSkuId == fixture.Simple.ProductoSkuId);
        var placeholderCard = cards.Single(card => card.ProductoSkuId == fixture.Assorted.ProductoSkuId);
        WaitUntil(() => productFallbackCard.HasImageForSmoke,
            $"thumbnail fallback Producto en POS {mode} DPI {ScaleName(scale)}");
        if (!fixture.Assets.Requests.Contains((ProductAssetOwnerKind.Product, SmokeFixture.ProductImageAssetId)) ||
            productFallbackCard.ShowsPlaceholderForSmoke)
        {
            throw new InvalidOperationException("El POS no usó el thumbnail de Producto para el SKU sin imagen propia.");
        }
        if (placeholderCard.HasImageForSmoke || !placeholderCard.ShowsPlaceholderForSmoke)
        {
            throw new InvalidOperationException("El POS no mostró el placeholder para un SKU y Producto sin imagen.");
        }
        checkpoint($"IMAGEN POS fallback Producto + placeholder {mode} DPI {ScaleName(scale)}=OK");

        if (scale != 1f)
        {
            form.Scale(new SizeF(scale, scale));
            form.ClientSize = new Size(
                (int)Math.Round(1280 * scale),
                (int)Math.Round(720 * scale));
        }

        form.PerformLayout();
        PerformLayoutRecursive(form);

        ValidateLayout(form, mode, scale, quick);
        if (runInteractions)
        {
            SmokeSubcategoryFilters(form, fixture, checkpoint, mode, quick);
        }
        var initialStatus = form.ScannerStatusForSmoke;
        if (mode == InstallationMode.Principal &&
            !initialStatus.Contains("Escáner listo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"PRINCIPAL no informó disponibilidad del escáner: '{initialStatus}'.");
        }

        if (mode == InstallationMode.Consulta &&
            !initialStatus.Contains("Modo Consulta", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"CONSULTA no informó el bloqueo operativo: '{initialStatus}'.");
        }

        if (runInteractions)
        {
            if (mode == InstallationMode.Principal)
            {
                SmokeProductPreview(form, fixture, outputDirectory, checkpoint);
                SmokePrincipalInteractions(form, fixture, checkpoint, quick, outputDirectory);
            }
            else
            {
                SmokeConsultaInteractions(form, fixture, checkpoint);
            }
        }

        var imagePath = Path.Combine(
            outputDirectory,
            $"pos-{(quick ? "rapido" : "tradicional")}-{mode.ToString().ToLowerInvariant()}-dpi-{ScaleName(scale)}.png");
        SaveScreenshot(form, imagePath);
        checkpoint($"LAYOUT {(quick ? "RÁPIDO" : "TRADICIONAL")} {mode} 1280x720 DPI {ScaleName(scale)}=OK; render={imagePath}");
        form.Close();
    }

    private static void SmokeProductPreview(
        EmployeePOSForm form,
        SmokeFixture fixture,
        string outputDirectory,
        Action<string> checkpoint)
    {
        var quickCards = Descendants(form)
            .OfType<PosSkuCardControl>()
            .ToList();
        var card = quickCards.Single(item => item.ProductoSkuId == fixture.Simple.ProductoSkuId);
        var previewButton = card.PreviewButtonForSmoke;
        var previewSurface = previewButton.Parent;
        if (quickCards.Count == 0 || quickCards.Any(item =>
                !item.PreviewButtonForSmoke.Visible ||
                item.PreviewButtonForSmoke.Parent != item.PreviewSurfaceForSmoke ||
                item.PreviewButtonForSmoke.Parent?.Controls.GetChildIndex(item.PreviewButtonForSmoke) != 0) ||
            !previewButton.Visible || !previewButton.Enabled ||
            previewButton.AccessibleName != "Ver producto" ||
            previewSurface != card.PreviewSurfaceForSmoke ||
            !previewSurface.ClientRectangle.Contains(previewButton.Bounds) ||
            previewSurface.Controls.GetChildIndex(previewButton) != 0)
        {
            throw new InvalidOperationException(
                $"La tarjeta {(form.IsQuickPosForSmoke ? "rápida" : "tradicional")} no muestra el ojo accesible, visible y al frente. " +
                $"Visible={previewButton.Visible}; Enabled={previewButton.Enabled}; " +
                $"AccessibleName='{previewButton.AccessibleName}'; Bounds={previewButton.Bounds}; " +
                $"Parent={previewSurface?.GetType().Name}; ParentBounds={previewSurface?.ClientRectangle}; " +
                $"ChildIndex={previewSurface?.Controls.GetChildIndex(previewButton)}; " +
                $"Compact={card.CompactTouchModeForSmoke}.");
        }

        var cartUnitsBefore = form.CartUnitCountForSmoke;
        Exception? failure = null;
        var escapeSent = false;
        var startedAt = Environment.TickCount64;
        using var timer = new System.Windows.Forms.Timer { Interval = 75 };
        timer.Tick += (_, _) =>
        {
            var preview = System.Windows.Forms.Application.OpenForms
                .OfType<ProductQuickPreviewForm>()
                .FirstOrDefault();
            if (preview is null)
            {
                return;
            }

            var timedOut = Environment.TickCount64 - startedAt >= 4000;
            if (!preview.HasImageForSmoke && !timedOut)
            {
                return;
            }

            if (!preview.HasImageForSmoke)
            {
                failure = new InvalidOperationException("La vista ampliada no cargó el thumbnail existente del producto.");
            }

            var mode = form.IsQuickPosForSmoke ? "rapido" : "tradicional";
            SaveScreenshot(preview, Path.Combine(outputDirectory, $"pos-{mode}-vista-producto.png"));
            escapeSent = true;
            timer.Stop();
            SendKeys.SendWait("{ESC}");
        };

        timer.Start();
        previewButton.PerformClick();
        timer.Stop();

        if (!escapeSent)
        {
            throw new InvalidOperationException("La vista del producto no quedó abierta para cerrarse con Esc.");
        }

        if (failure is not null)
        {
            throw new InvalidOperationException(
                $"Falló la vista de detalles del POS {(form.IsQuickPosForSmoke ? "rápido" : "tradicional") }.",
                failure);
        }

        if (form.IsDisposed || form.CartUnitCountForSmoke != cartUnitsBefore)
        {
            throw new InvalidOperationException("Abrir/cerrar la vista del producto alteró el POS o el carrito.");
        }

        if (!form.BarcodeHasFocusForSmoke)
        {
            throw new InvalidOperationException("Al cerrar la vista con Esc, el foco no volvió al escáner del POS rápido.");
        }

        checkpoint(
            $"POS {(form.IsQuickPosForSmoke ? "rápido" : "tradicional")}: ojo abre detalles, Esc cierra sin tocar carrito y devuelve foco al escáner=OK");
    }

    private static void SmokeCreditPolicyLayout(string outputDirectory, Action<string> checkpoint)
    {
        using var form = new CustomerCreditPolicyForm(
            new SmokeCreditService(provideCustomerPolicy: true),
            5,
            "CLI-000005 - Alegría y más artesanías");
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.PerformLayout();
        PerformLayoutRecursive(form);
        form.Show();
        System.Windows.Forms.Application.DoEvents();
        PerformLayoutRecursive(form);

        var clipping = Descendants(form)
            .Where(control => control.Visible)
            .FirstOrDefault(control => !form.ClientRectangle.Contains(BoundsRelativeTo(form, control)));
        if (clipping is not null)
        {
            throw new InvalidOperationException(
                $"Política crediticia: '{clipping.Text}' queda fuera del ancho/alto de la ventana ({BoundsRelativeTo(form, clipping)}).");
        }

        var title = Descendants(form).OfType<Label>()
            .Single(label => label.Text.StartsWith("Politica crediticia - ", StringComparison.Ordinal));
        var titleWidth = TextRenderer.MeasureText(
            title.Text,
            title.Font,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        if (title.ClientSize.Width < titleWidth)
        {
            throw new InvalidOperationException(
                $"Política crediticia: el título del cliente queda truncado ({title.ClientSize.Width}/{titleWidth}px).");
        }

        foreach (var checkBox in Descendants(form).OfType<CheckBox>().Where(control => control.Visible))
        {
            var textWidth = TextRenderer.MeasureText(
                checkBox.Text,
                checkBox.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            if (checkBox.ClientSize.Width < textWidth + 24)
            {
                throw new InvalidOperationException(
                    $"Política crediticia: el checkbox '{checkBox.Text}' no tiene ancho para su texto.");
            }
        }

        var path = Path.Combine(outputDirectory, "politica-crediticia-layout.png");
        SaveScreenshot(form, path);
        checkpoint($"LAYOUT política crediticia sin recortes=OK; render={path}");
        form.Close();
    }

    private static void SmokeCompletedSaleResize(string outputDirectory, Action<string> checkpoint)
    {
        var lines = Enumerable.Range(1, 5)
            .Select(index => new SaleReceiptLine(
                $"Producto de prueba {index}",
                $"SKU-{index:00}",
                1,
                1_000,
                1_000,
                0,
                1_000))
            .ToList();
        var payments = new[]
        {
            new SaleReceiptPayment(MetodoPago.EFECTIVO, 1_500, null),
            new SaleReceiptPayment(MetodoPago.TRANSFERENCIA, 1_500, "REF-TRANSFERENCIA"),
            new SaleReceiptPayment(MetodoPago.BITCOIN, 2_000, "REF-BITCOIN")
        };
        var receipt = new SaleReceiptViewModel(
            1,
            "VTA-SMOKE-0001",
            DateTime.UtcNow,
            "Administrador del sistema",
            "Consumidor final",
            5_000,
            0,
            5_000,
            5_000,
            0,
            lines,
            payments);

        using var form = new SaleReceiptForm(receipt, isCompletedSale: true, "Nueva venta")
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false,
            WindowState = FormWindowState.Normal,
            ClientSize = new Size(1280, 820)
        };
        if (form.WindowState != FormWindowState.Normal)
        {
            throw new InvalidOperationException("Venta completada no debe abrirse maximizada por defecto.");
        }

        form.Show();
        System.Windows.Forms.Application.DoEvents();
        PerformLayoutRecursive(form);
        var ticketPreview = Descendants(form).OfType<TextBox>().Single(input => input.Multiline);
        if (!ticketPreview.Text.Contains(
                "Cambios y devoluciones sujetos a revisión",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "La vista previa del recibo no muestra la nota sobre cambios y devoluciones.");
        }

        var grids = Descendants(form).OfType<DataGridView>().ToList();
        var linesGrid = grids.Single(grid => grid.Columns.Cast<DataGridViewColumn>().Any(column => column.HeaderText == "Cant."));
        var paymentsGrid = grids.Single(grid => grid.Columns.Cast<DataGridViewColumn>().Any(column => column.HeaderText == "Referencia"));
        var defaultLinesHeight = linesGrid.ClientSize.Height;
        var defaultPaymentsHeight = paymentsGrid.ClientSize.Height;
        SaveScreenshot(form, Path.Combine(outputDirectory, "venta-completada-default.png"));

        form.ClientSize = new Size(1920, 1080);
        PerformLayoutRecursive(form);
        System.Windows.Forms.Application.DoEvents();
        PerformLayoutRecursive(form);

        if (linesGrid.ClientSize.Height <= defaultLinesHeight ||
            paymentsGrid.ClientSize.Height <= defaultPaymentsHeight)
        {
            throw new InvalidOperationException(
                "Venta completada no distribuyó el alto adicional entre productos y métodos de pago.");
        }

        if (paymentsGrid.DisplayedRowCount(includePartialRow: false) < payments.Length)
        {
            throw new InvalidOperationException(
                "Venta completada sigue ocultando métodos de pago al ampliar la ventana.");
        }

        var enlargedPath = Path.Combine(outputDirectory, "venta-completada-ampliada.png");
        SaveScreenshot(form, enlargedPath);
        checkpoint(
            $"LAYOUT venta completada normal + ampliada=OK; productos {defaultLinesHeight}->{linesGrid.ClientSize.Height}px, pagos {defaultPaymentsHeight}->{paymentsGrid.ClientSize.Height}px; render={enlargedPath}");
        form.Close();
    }

    private static void SmokeMixedPaymentInputFocus(
        EmployeePOSForm form,
        Action<string> checkpoint,
        bool quick)
    {
        form.ShowMixedPaymentForSmoke();
        foreach (var method in new[] { "Efectivo", "Transferencia", "Bitcoin" })
        {
            if (!form.FocusPaymentAmountForSmoke(method))
            {
                throw new InvalidOperationException(
                    $"Pago mixto {(quick ? "rápido" : "tradicional")}: el monto de {method} no se seleccionó al recibir foco.");
            }
        }

        form.ClosePaymentOverlayForSmoke();
        checkpoint($"PAGO MIXTO selección de monto al enfocar {(quick ? "rápido" : "tradicional")}=OK");
    }

    private static void SmokeQuickPaginationScenarios(
        string outputDirectory,
        Action<string> checkpoint)
    {
        var targetSizes = new[]
        {
            new Size(980, 620),
            new Size(1280, 720),
            new Size(1366, 768),
            new Size(1600, 900),
            new Size(1920, 1080)
        };

        foreach (var scale in new[] { 1f, 1.25f, 1.5f })
        {
            foreach (var targetSize in targetSizes)
            {
                var logicalSize = new Size(
                    Math.Max(1, (int)Math.Round(targetSize.Width / scale)),
                    Math.Max(1, (int)Math.Round(targetSize.Height / scale)));
                foreach (var mode in new[] { InstallationMode.Principal, InstallationMode.Consulta })
                {
                    SmokeQuickPagination(
                        mode,
                        scale,
                        targetSize,
                        logicalSize,
                        outputDirectory,
                        checkpoint);
                }
            }
        }
    }

    private static void SmokeQuickPagination(
        InstallationMode mode,
        float scale,
        Size targetSize,
        Size logicalSize,
        string outputDirectory,
        Action<string> checkpoint)
    {
        var fixture = new SmokeFixture(mode, outputDirectory);
        using var form = fixture.CreateForm(quick: true);
        form.MinimumSize = Size.Empty;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.WindowState = FormWindowState.Normal;
        form.ClientSize = logicalSize;
        form.Show();
        WaitUntil(
            () => fixture.Catalog.SearchCalls > 0 &&
                Descendants(form).OfType<PosSkuCardControl>().Count() >= 4,
            $"carga POS rápido paginación {mode} {targetSize.Width}x{targetSize.Height} DPI {ScaleName(scale)}");

        if (scale != 1f)
        {
            form.Scale(new SizeF(scale, scale));
        }

        form.ClientSize = targetSize;
        form.PerformLayout();
        PerformLayoutRecursive(form);
        if (form.ClientSize != targetSize)
        {
            throw new InvalidOperationException(
                $"POS rápido {mode} DPI {ScaleName(scale)}: tamaño solicitado " +
                $"{targetSize.Width}x{targetSize.Height}, actual {form.ClientSize.Width}x{form.ClientSize.Height}.");
        }

        ValidateQuickPaginationBaseLayout(form, mode, scale, targetSize);
        ValidateQuickPagination(form, mode, scale, targetSize, expectedPage: 1, expectedTotalPages: 1,
            previousEnabled: false, nextEnabled: false);

        form.SetPaginationScenarioForSmoke(BuildPaginationItems(fixture.Simple));
        ValidateQuickPagination(form, mode, scale, targetSize, expectedPage: 1, expectedTotalPages: 2,
            previousEnabled: false, nextEnabled: true);

        form.ClickNextPageForSmoke();
        System.Windows.Forms.Application.DoEvents();
        form.PerformLayout();
        PerformLayoutRecursive(form);
        ValidateQuickPagination(form, mode, scale, targetSize, expectedPage: 2, expectedTotalPages: 2,
            previousEnabled: true, nextEnabled: false);
        if (Descendants(form).OfType<PosSkuCardControl>().Count() != 1)
        {
            throw new InvalidOperationException(
                $"POS rápido {mode} DPI {ScaleName(scale)}: Siguiente no cambió a la segunda página.");
        }

        if (scale == 1.5f && mode == InstallationMode.Principal)
        {
            var screenshotPath = Path.Combine(
                outputDirectory,
                $"pos-rapido-paginacion-{targetSize.Width}x{targetSize.Height}-dpi-{ScaleName(scale)}.png");
            SaveScreenshot(form, screenshotPath);
        }

        checkpoint(
            $"PAGINACIÓN POS RÁPIDO {mode} {targetSize.Width}x{targetSize.Height} DPI {ScaleName(scale)}=OK");
        form.Close();
    }

    private static void ValidateQuickPagination(
        EmployeePOSForm form,
        InstallationMode mode,
        float scale,
        Size targetSize,
        int expectedPage,
        int expectedTotalPages,
        bool previousEnabled,
        bool nextEnabled)
    {
        var stage = $"POS rápido {mode} {targetSize.Width}x{targetSize.Height} DPI {ScaleName(scale)}";
        var descendants = Descendants(form).Where(control => control.Visible).ToList();
        var previous = descendants.OfType<Button>().SingleOrDefault(button => button.Text == "‹")
            ?? throw new InvalidOperationException($"{stage}: no se encontró Anterior.");
        var next = descendants.OfType<Button>().SingleOrDefault(button => button.Text == "›")
            ?? throw new InvalidOperationException($"{stage}: no se encontró Siguiente.");
        var page = descendants.OfType<Label>().SingleOrDefault(label =>
                label.Text.StartsWith("Página ", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"{stage}: no se encontró el indicador de página.");
        var expectedText = $"Página {expectedPage} de {expectedTotalPages}";
        if (page.Text != expectedText || page.Text.Contains('\r') || page.Text.Contains('\n'))
        {
            throw new InvalidOperationException(
                $"{stage}: indicador inesperado o con wrap ('{page.Text}'; esperado '{expectedText}').");
        }

        if (previous.Enabled != previousEnabled || next.Enabled != nextEnabled)
        {
            throw new InvalidOperationException(
                $"{stage}: estado de paginación incorrecto (Anterior={previous.Enabled}, " +
                $"Siguiente={next.Enabled}).");
        }

        foreach (var control in new Control[] { previous, page, next })
        {
            if (control.Parent is null ||
                !control.Parent.ClientRectangle.Contains(new Rectangle(control.Location, control.Size)))
            {
                throw new InvalidOperationException($"{stage}: control de paginación fuera de su contenedor.");
            }

            var bounds = BoundsRelativeTo(form, control);
            if (!form.ClientRectangle.Contains(bounds))
            {
                throw new InvalidOperationException($"{stage}: control de paginación fuera del Form ({bounds}).");
            }
        }

        var pageFont = page.Font;
        var measured = TextRenderer.MeasureText(
            page.Text,
            pageFont,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (page.ClientSize.Width < measured.Width + 4 || page.ClientSize.Height < measured.Height + 2)
        {
            throw new InvalidOperationException(
                $"{stage}: texto de página recortado (cliente={page.ClientSize}, medido={measured}).");
        }

        var cart = descendants.OfType<DataGridView>().SingleOrDefault()
            ?? throw new InvalidOperationException($"{stage}: no se encontró el carrito.");
        var cartBounds = BoundsRelativeTo(form, cart);
        foreach (var control in new Control[] { previous, page, next })
        {
            if (BoundsRelativeTo(form, control).IntersectsWith(cartBounds))
            {
                throw new InvalidOperationException($"{stage}: paginación invade el área del carrito.");
            }
        }
    }

    private static void ValidateQuickPaginationBaseLayout(
        EmployeePOSForm form,
        InstallationMode mode,
        float scale,
        Size targetSize)
    {
        var stage = $"POS rápido {mode} {targetSize.Width}x{targetSize.Height} DPI {ScaleName(scale)}";
        var descendants = Descendants(form).Where(control => control.Visible).ToList();
        var cart = descendants.OfType<DataGridView>().SingleOrDefault()
            ?? throw new InvalidOperationException($"{stage}: no se encontró el carrito.");
        var workspace = FindAncestor<TableLayoutPanel>(cart, candidate =>
            candidate.ColumnCount == 3 && candidate.RowCount == 1);
        if (workspace is null)
        {
            throw new InvalidOperationException($"{stage}: no se encontró el workspace con gutter del catálogo y carrito.");
        }

        var catalogHost = workspace.GetControlFromPosition(0, 0)
            ?? throw new InvalidOperationException($"{stage}: no se encontró el catálogo del workspace.");
        var cartHost = workspace.GetControlFromPosition(2, 0)
            ?? throw new InvalidOperationException($"{stage}: no se encontró el panel de carrito del workspace.");
        var catalogBounds = BoundsRelativeTo(form, catalogHost);
        var cartHostBounds = BoundsRelativeTo(form, cartHost);
        if (cartHostBounds.Left - catalogBounds.Right < 8)
        {
            throw new InvalidOperationException(
                $"{stage}: el gutter entre catálogo y carrito es insuficiente " +
                $"(catálogo={catalogBounds}, carrito={cartHostBounds}).");
        }

        var barcode = descendants.OfType<TextBox>().SingleOrDefault(input =>
                input.AccessibleName == "Entrada principal de código o escáner HID")
            ?? throw new InvalidOperationException($"{stage}: no se encontró la entrada principal de escáner.");
        var barcodeBounds = BoundsRelativeTo(form, barcode);
        if (!form.ClientRectangle.Contains(barcodeBounds) || barcodeBounds.IntersectsWith(cartHostBounds))
        {
            throw new InvalidOperationException(
                $"{stage}: la entrada de escáner sale del catálogo o invade el carrito ({barcodeBounds}).");
        }

        foreach (var text in new[] { "Buscar · F2", "Cliente\r\nF3" })
        {
            var button = descendants.OfType<Button>().SingleOrDefault(candidate => candidate.Text == text)
                ?? throw new InvalidOperationException($"{stage}: no se encontró el botón '{text}'.");
            if (button.Parent is null ||
                !button.Parent.ClientRectangle.Contains(new Rectangle(button.Location, button.Size)) ||
                !form.ClientRectangle.Contains(BoundsRelativeTo(form, button)))
            {
                var parentBounds = button.Parent?.ClientRectangle.ToString() ?? "<sin padre>";
                var localBounds = new Rectangle(button.Location, button.Size);
                var formBounds = BoundsRelativeTo(form, button);
                throw new InvalidOperationException(
                    $"{stage}: el botón '{text}' salió de su contenedor " +
                    $"(local={localBounds}, padre={parentBounds}, formulario={formBounds}).");
            }

            var lines = button.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var requiredWidth = lines.Max(line => TextRenderer.MeasureText(
                line,
                button.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width) + 12;
            if (button.ClientSize.Width < requiredWidth || button.ClientSize.Height <= 0)
            {
                throw new InvalidOperationException(
                    $"{stage}: texto truncado en '{text}' (cliente={button.ClientSize}, requerido={requiredWidth}).");
            }

            if (BoundsRelativeTo(form, button).IntersectsWith(BoundsRelativeTo(form, cart)))
            {
                throw new InvalidOperationException($"{stage}: '{text}' invade el carrito.");
            }
        }
    }

    private static IReadOnlyList<PosSkuSaleItem> BuildPaginationItems(PosSkuSaleItem template)
    {
        return Enumerable.Range(1, 61)
            .Select(index => template with
            {
                ProductoId = 1000 + index,
                ProductoSkuId = 2000 + index,
                CodigoInterno = $"PAG-{index:000}",
                CodigoSku = $"PAG-SKU-{index:000}",
                ProductoNombre = $"Producto paginación {index:000}",
                VarianteNombre = null,
                ImagenOwnerKind = null,
                ImagenAssetId = null,
                ImagenRutaRelativa = null,
                ImagenLocalPath = null,
                SubcategoriaId = null,
                SubcategoriaNombre = null
            })
            .ToList();
    }

    private static void SmokePrincipalInteractions(
        EmployeePOSForm form,
        SmokeFixture fixture,
        Action<string> checkpoint,
        bool quick,
        string outputDirectory)
    {
        if (form.RegisterEnabledForSmoke)
        {
            throw new InvalidOperationException("Registrar venta estaba habilitado con carrito vacío.");
        }

        if (quick)
        {
            form.SearchProductsForSmoke("surtido");
            if (Descendants(form).OfType<PosSkuCardControl>().Count() != 1)
            {
                throw new InvalidOperationException("La búsqueda manual del POS rápido no filtró el catálogo.");
            }

            form.SearchProductsForSmoke(string.Empty);
            WaitUntil(
                () => Descendants(form).OfType<PosSkuCardControl>().Count() >= 4,
                "restaurar catálogo POS rápido");
            checkpoint("POS RÁPIDO búsqueda manual + limpiar=OK");
        }

        Scan(form, fixture.Barcodes, SmokeFixture.SimpleBarcode, fixture.Simple.ProductoSkuId);
        AssertCart(form, expectedLines: 1, expectedUnits: 1, "scanner SIMPLE");
        checkpoint("SCANNER input + Enter SIMPLE exacto=OK");

        Scan(form, fixture.Barcodes, SmokeFixture.AssortedBarcode, fixture.Assorted.ProductoSkuId);
        AssertCart(form, expectedLines: 2, expectedUnits: 2, "scanner SURTIDO");
        checkpoint("SCANNER SURTIDO exacto=OK");

        Scan(form, fixture.Barcodes, SmokeFixture.RedBarcode, fixture.RedVariant.ProductoSkuId);
        if (!form.ScannerStatusForSmoke.Contains("Rojo", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El escaneo POR_VARIANTES no identificó el diseño Rojo.");
        }

        Scan(form, fixture.Barcodes, SmokeFixture.BlueBarcode, fixture.BlueVariant.ProductoSkuId);
        if (!form.ScannerStatusForSmoke.Contains("Azul", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El escaneo POR_VARIANTES no identificó el diseño Azul.");
        }

        AssertCart(form, expectedLines: 4, expectedUnits: 4, "variantes exactas");
        checkpoint("SCANNER POR_VARIANTES selecciona SKU/diseño exacto=OK");

        form.AddManualSelectionForSmoke(fixture.Assorted);
        AssertCart(form, expectedLines: 4, expectedUnits: 5, "captura manual");
        checkpoint("CAPTURA MANUAL sin scanner conserva carrito=OK");

        if (quick)
        {
            var touchCard = Descendants(form)
                .OfType<PosSkuCardControl>()
                .Single(card => card.ProductoSkuId == fixture.Assorted.ProductoSkuId);
            touchCard.ActivateForSmoke();
            AssertCart(form, expectedLines: 4, expectedUnits: 6, "captura touch");
            form.ClickFirstCartActionForSmoke("Increase");
            form.ClickFirstCartActionForSmoke("Decrease");
            AssertCart(form, expectedLines: 4, expectedUnits: 6, "carrito +/-");
            form.ClickFirstCartActionForSmoke("Remove");
            AssertCart(form, expectedLines: 3, expectedUnits: 5, "carrito quitar");
            form.AddManualSelectionForSmoke(fixture.Simple);
            AssertCart(form, expectedLines: 4, expectedUnits: 6, "restaurar línea quitada");
            checkpoint("POS RÁPIDO touch + carrito +/-/quitar=OK");
        }

        var linesBeforeUnknown = form.CartLineCountForSmoke;
        var unitsBeforeUnknown = form.CartUnitCountForSmoke;
        WaitForTask(
            form.SimulateBarcodeEnterAsync("NO-EXISTE-999"),
            "barcode desconocido");
        if (!form.ScannerStatusForSmoke.Contains("Código no encontrado", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El barcode desconocido no mostró el estado esperado.");
        }

        AssertCart(form, linesBeforeUnknown, unitsBeforeUnknown, "barcode desconocido");
        checkpoint("SCANNER desconocido no agrega ni descarta carrito=OK");

        var rapidScans = new[]
        {
            form.SimulateBarcodeEnterAsync(SmokeFixture.SimpleBarcode),
            form.SimulateBarcodeEnterAsync(SmokeFixture.SimpleBarcode),
            form.SimulateBarcodeEnterAsync(SmokeFixture.SimpleBarcode)
        };
        WaitForTask(Task.WhenAll(rapidScans), "tres lecturas rápidas");
        AssertCart(form, expectedLines: 4, expectedUnits: quick ? 9 : 8, "lecturas rápidas");
        if (fixture.Barcodes.LookupCodes.Count(code => code == SmokeFixture.SimpleBarcode) != 4)
        {
            throw new InvalidOperationException("No se procesaron exactamente todas las lecturas rápidas.");
        }

        if (!form.RegisterEnabledForSmoke)
        {
            throw new InvalidOperationException(
                "PRINCIPAL no habilitó Registrar venta con caja, permisos, pago y stock válidos.");
        }

        checkpoint("SCANNER lecturas rápidas sin perder unidades=OK");
        checkpoint("PRINCIPAL permite flujo de venta válido=OK");
        checkpoint("CARRITO persistente entre scanner/manual/desconocido=OK");

        if (quick)
        {
            if (!form.SimulateQuickShortcutForSmoke(Keys.F2) || !form.SearchHasFocusForSmoke)
            {
                throw new InvalidOperationException("F2 no devolvió el foco a la búsqueda rápida.");
            }

            // SelectQuickCustomerAsync may yield before ShowDialog is reached. Keep
            // the timer alive until the nested picker actually appears so the smoke
            // remains deterministic without interacting with production code.
            var customerPickerCloser = new System.Windows.Forms.Timer { Interval = 75 };
            try
            {
                customerPickerCloser.Tick += (_, _) =>
                {
                    var picker = System.Windows.Forms.Application.OpenForms
                        .OfType<CustomerSalePickerForm>()
                        .FirstOrDefault();
                    if (picker is null)
                    {
                        return;
                    }

                    customerPickerCloser.Stop();
                    picker.DialogResult = DialogResult.Cancel;
                    picker.Close();
                };
                customerPickerCloser.Start();
                if (!form.SimulateQuickShortcutForSmoke(Keys.F3))
                {
                    throw new InvalidOperationException("F3 no abrió el selector de cliente.");
                }

                WaitUntil(() => form.BarcodeHasFocusForSmoke, "foco tras cancelar selector de cliente");
            }
            finally
            {
                customerPickerCloser.Stop();
                customerPickerCloser.Dispose();
            }
            form.SetSelectedCustomer(new CustomerSaleSelection(41, "CLI-000041", "Cliente smoke", true));
            if (!form.QuickCartCustomerForSmoke.Contains("Cliente smoke", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("El POS rápido no reflejó el cliente seleccionado.");
            }

            if (!form.SimulateQuickShortcutForSmoke(Keys.F4) || !form.QuantityHasFocusForSmoke)
            {
                throw new InvalidOperationException("F4 no enfocó la cantidad del carrito.");
            }

            if (!form.SimulateQuickShortcutForSmoke(Keys.F6) || !form.PaymentOverlayVisibleForSmoke)
            {
                throw new InvalidOperationException("F6 no abrió el panel compartido de descuentos.");
            }
            form.CancelQuickCheckoutForSmoke();
            WaitUntil(() => form.BarcodeHasFocusForSmoke, "foco tras cancelar descuentos");
            checkpoint("POS RÁPIDO F2/F3/F4/F6 + cliente + retorno de foco=OK");

            WaitForTask(form.ProcessBarcodeAsync(string.Empty), "retorno de foco al barcode");
            if (!form.SimulateQuickShortcutForSmoke(Keys.F8))
            {
                throw new InvalidOperationException("F8 no fue reconocido por el POS rápido.");
            }
            if (!form.PaymentOverlayVisibleForSmoke)
            {
                throw new InvalidOperationException("F8/cobro no abrió el panel compacto del POS rápido.");
            }
            if (form.PaymentOverlayBoundsForSmoke.Size != form.ClientSize || form.PaymentOverlayZIndexForSmoke != 0)
            {
                throw new InvalidOperationException(
                    $"El panel de cobro no cubre/frentea el POS: bounds={form.PaymentOverlayBoundsForSmoke}, " +
                    $"cliente={form.ClientSize}, z={form.PaymentOverlayZIndexForSmoke}.");
            }
            var paymentButtons = Descendants(form).OfType<Button>().ToList();
            foreach (var expectedMethod in new[] { "Efectivo", "Transferencia", "Pago mixto" })
            {
                if (!paymentButtons.Any(button => button.Text == expectedMethod))
                {
                    throw new InvalidOperationException(
                        $"El cobro rápido no mostró el método habilitado '{expectedMethod}'.");
                }
            }
            if (paymentButtons.Any(button => button.Text == "Bitcoin"))
            {
                throw new InvalidOperationException("El cobro rápido mostró Bitcoin aunque está deshabilitado.");
            }
            paymentButtons.Single(button => button.Text == "Transferencia").PerformClick();
            if (form.CurrentPaymentMethodForSmoke != "TRANSFERENCIA")
            {
                throw new InvalidOperationException("El cobro rápido no seleccionó Transferencia.");
            }
            paymentButtons.Single(button => button.Text == "Pago mixto").PerformClick();
            if (form.CurrentPaymentMethodForSmoke != "MIXTO")
            {
                throw new InvalidOperationException("El cobro rápido no seleccionó Pago mixto.");
            }
            paymentButtons.Single(button => button.Text == "Efectivo").PerformClick();
            checkpoint("COBRO RÁPIDO métodos configurados Efectivo/Transferencia/Mixto=OK");
            form.SetQuickCheckoutCashForSmoke(200m);
            if (!form.QuickCheckoutChangeForSmoke.StartsWith("$", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("El cobro rápido no calculó un vuelto visible.");
            }
            checkpoint($"COBRO RÁPIDO efectivo/vuelto={form.QuickCheckoutChangeForSmoke}=OK");
            ValidateQuickCheckoutLayout(form);
            var checkoutImage = Path.Combine(outputDirectory, "pos-rapido-cobro-principal-dpi-100.png");
            form.PerformLayout();
            PerformLayoutRecursive(form);
            System.Windows.Forms.Application.DoEvents();
            form.SavePaymentOverlayForSmoke(checkoutImage);
            checkpoint($"LAYOUT COBRO RÁPIDO=OK; render={checkoutImage}");
            form.CancelQuickCheckoutForSmoke();
            WaitUntil(() => form.BarcodeHasFocusForSmoke, "foco tras cancelar cobro rápido");
        }
        var totalMetrics = ValidateCriticalText(form, "PRINCIPAL con carrito");
        checkpoint($"LAYOUT total completo=OK; {totalMetrics}");
        var summaryMetrics = ValidateSummaryLabels(form, "PRINCIPAL con carrito", quick);
        checkpoint($"LAYOUT resumen visible/separado=OK; {summaryMetrics}");
        var cartMetrics = ValidateCartGridContent(form, "PRINCIPAL con carrito");
        checkpoint($"LAYOUT carrito sin valores truncados=OK; {cartMetrics}");
        SmokeMixedPaymentInputFocus(form, checkpoint, quick);
    }

    private static void SmokeSubcategoryFilters(
        EmployeePOSForm form,
        SmokeFixture fixture,
        Action<string> checkpoint,
        InstallationMode mode,
        bool quick)
    {
        var combos = Descendants(form)
            .OfType<ComboBox>()
            .Where(combo => combo.Visible)
            .ToList();
        var category = combos.FirstOrDefault(combo => Enumerable.Range(0, combo.Items.Count)
                .Any(index => combo.GetItemText(combo.Items[index]) == "Artesanías"))
            ?? throw new InvalidOperationException(
                $"POS {(quick ? "rápido" : "tradicional")} {mode}: no se encontró el filtro de categoría.");
        var subcategory = combos.FirstOrDefault(combo => combo.Text == "Todas las subcategorías")
            ?? throw new InvalidOperationException(
                $"POS {(quick ? "rápido" : "tradicional")} {mode}: no se encontró el filtro de subcategoría.");
        var locationOptions = new[]
        {
            "Todos los productos",
            "Productos en exhibición",
            "Productos en bodega"
        };
        if (!form.LocationFilterOptionsForSmoke.SequenceEqual(locationOptions, StringComparer.Ordinal) ||
            form.SelectedLocationFilterForSmoke != locationOptions[0])
        {
            throw new InvalidOperationException(
                $"POS {(quick ? "rápido" : "tradicional")} {mode}: el filtro de ubicación no inicia en Todos o sus opciones están incompletas.");
        }

        var locationFixture = new[]
        {
            fixture.Simple with { StockFisicoExhibicion = 2, StockFisicoBodega = 0 },
            fixture.Assorted with { StockFisicoExhibicion = 0, StockFisicoBodega = 3 },
            fixture.RedVariant with { StockFisicoExhibicion = 1, StockFisicoBodega = 1 },
            fixture.BlueVariant with { StockFisicoExhibicion = 0, StockFisicoBodega = 0 }
        };
        AssertLocationResults(locationOptions[0], [11, 21, 31, 32]);
        AssertLocationResults(locationOptions[1], [11, 31]);
        AssertLocationResults(locationOptions[2], [21, 31]);
        checkpoint($"FILTRO POS ubicación Todos/Exhibición/Bodega {(quick ? "RÁPIDO" : "TRADICIONAL")} {mode}=OK");

        void AssertLocationResults(string option, int[] expectedSkuIds)
        {
            var actual = form.FilterProductIdsForLocationForSmoke(locationFixture, option)
                .OrderBy(id => id)
                .ToArray();
            if (!actual.SequenceEqual(expectedSkuIds.OrderBy(id => id)))
            {
                throw new InvalidOperationException(
                    $"POS {(quick ? "rápido" : "tradicional")} {mode}: el filtro '{option}' devolvió SKU inesperados ({string.Join(",", actual)}).");
            }
        }

        var categoryIndex = Enumerable.Range(0, category.Items.Count)
            .First(index => category.GetItemText(category.Items[index]) == "Artesanías");
        category.SelectedIndex = categoryIndex;
        WaitUntil(
            () => subcategory.Enabled && subcategory.Items.Count == 3,
            $"carga de subcategorías activas en POS {(quick ? "rápido" : "tradicional")}");

        var optionTexts = Enumerable.Range(0, subcategory.Items.Count)
            .Select(index => subcategory.GetItemText(subcategory.Items[index]))
            .ToList();
        if (optionTexts.Count != 3 ||
            optionTexts[0] != "Todas las subcategorías" ||
            !optionTexts.Contains("Jarrones") ||
            !optionTexts.Contains("Sin subcategoría") ||
            !subcategory.Enabled)
        {
            throw new InvalidOperationException(
                $"POS {(quick ? "rápido" : "tradicional")} {mode}: opciones dependientes incompletas ({string.Join(", ", optionTexts)}).");
        }

        subcategory.SelectedIndex = optionTexts.IndexOf("Jarrones");
        form.SearchProductsForSmoke(string.Empty);
        if (Descendants(form).OfType<PosSkuCardControl>().Count() != 1)
        {
            throw new InvalidOperationException(
                $"POS {(quick ? "rápido" : "tradicional")} {mode}: filtro concreto de subcategoría no se aplicó.");
        }

        subcategory.SelectedIndex = optionTexts.IndexOf("Sin subcategoría");
        form.SearchProductsForSmoke(string.Empty);
        if (Descendants(form).OfType<PosSkuCardControl>().Count() != 3)
        {
            throw new InvalidOperationException(
                $"POS {(quick ? "rápido" : "tradicional")} {mode}: filtro 'Sin subcategoría' no se aplicó.");
        }

        category.SelectedIndex = 0;
        WaitUntil(
            () => !subcategory.Enabled && subcategory.Text == "Todas las subcategorías",
            $"reinicio del filtro de subcategoría al elegir Todas en POS {(quick ? "rápido" : "tradicional")}");
        form.SearchProductsForSmoke(string.Empty);
        if (Descendants(form).OfType<PosSkuCardControl>().Count() != fixture.Items.Count)
        {
            throw new InvalidOperationException(
                $"POS {(quick ? "rápido" : "tradicional")} {mode}: elegir Todas no restauró el catálogo.");
        }

        checkpoint($"FILTROS dependientes categoría/subcategoría {(quick ? "RÁPIDO" : "TRADICIONAL")} {mode}=OK");
    }

    private static void SmokeConsultaInteractions(
        EmployeePOSForm form,
        SmokeFixture fixture,
        Action<string> checkpoint)
    {
        Scan(form, fixture.Barcodes, SmokeFixture.SimpleBarcode, fixture.Simple.ProductoSkuId);
        AssertCart(form, expectedLines: 1, expectedUnits: 1, "lookup en CONSULTA");
        if (form.RegisterEnabledForSmoke)
        {
            throw new InvalidOperationException("CONSULTA permitió registrar una venta.");
        }

        checkpoint("CONSULTA permite lookup/escaneo y bloquea Registrar venta=OK");
    }

    private static void Scan(
        EmployeePOSForm form,
        SmokeBarcodeService barcodeService,
        string code,
        int expectedSkuId)
    {
        var lookupsBefore = barcodeService.LookupCodes.Count;
        WaitForTask(form.SimulateBarcodeEnterAsync(code), $"scan {code}");
        if (barcodeService.LookupCodes.Count != lookupsBefore + 1 ||
            barcodeService.LastResolvedSkuId != expectedSkuId)
        {
            throw new InvalidOperationException(
                $"El scan '{code}' no resolvió exactamente el SKU {expectedSkuId}.");
        }
    }

    private static void AssertCart(
        EmployeePOSForm form,
        int expectedLines,
        int expectedUnits,
        string stage)
    {
        if (form.CartLineCountForSmoke != expectedLines || form.CartUnitCountForSmoke != expectedUnits)
        {
            throw new InvalidOperationException(
                $"{stage}: carrito esperado {expectedLines} líneas/{expectedUnits} unidades, " +
                $"actual {form.CartLineCountForSmoke}/{form.CartUnitCountForSmoke}.");
        }
    }

    private static void ValidateLayout(EmployeePOSForm form, InstallationMode mode, float scale, bool quick)
    {
        var descendants = Descendants(form).Where(control => control.Visible).ToList();
        var barcodeInput = descendants
            .OfType<TextBox>()
            .SingleOrDefault(control =>
                control.AccessibleName == (quick
                    ? "Entrada principal de código o escáner HID"
                    : "Entrada de escáner de código de barras"))
            ?? throw new InvalidOperationException("No se encontró la entrada visible de scanner/barcode.");
        var register = descendants
            .OfType<Button>()
            .SingleOrDefault(button => quick
                ? button.Text.StartsWith("COBRAR", StringComparison.Ordinal)
                : button.Text == "REGISTRAR VENTA")
            ?? throw new InvalidOperationException("No se encontró el botón Registrar venta.");
        var cart = descendants.OfType<DataGridView>().SingleOrDefault()
            ?? throw new InvalidOperationException("No se encontró el carrito del POS.");
        var catalog = descendants
            .OfType<FlowLayoutPanel>()
            .SingleOrDefault(control => control.Controls.OfType<PosSkuCardControl>().Any())
            ?? throw new InvalidOperationException("No se encontró el catálogo visual del POS.");

        if (catalog.Controls.OfType<PosSkuCardControl>().Count() < 4)
        {
            throw new InvalidOperationException(
                $"{mode} DPI {ScaleName(scale)}: el catálogo no renderizó los cuatro SKU de prueba.");
        }

        var requiredButtons = quick
            ? new[] { "Buscar · F2", "Cliente\r\nF3", "COBRAR  F8" }
            : new[] { "Buscar", "Refrescar", "Anterior", "Siguiente", "REGISTRAR VENTA" };
        foreach (var text in requiredButtons)
        {
            var button = descendants.OfType<Button>().FirstOrDefault(candidate => candidate.Text == text);
            if (button is null)
            {
                throw new InvalidOperationException(
                    $"{mode} DPI {ScaleName(scale)}: no quedó visible el botón '{text}'.");
            }

            var mustFitCaptureRow = quick && text is "Buscar · F2" or "Cliente\r\nF3";
            if (mustFitCaptureRow &&
                (button.Parent is null || button.Left < 0 || button.Right > button.Parent.ClientSize.Width))
            {
                throw new InvalidOperationException(
                    $"{mode} DPI {ScaleName(scale)}: el botón '{text}' rebasa su contenedor.");
            }

            if (mustFitCaptureRow &&
                BoundsRelativeTo(form, button).IntersectsWith(BoundsRelativeTo(form, cart)))
            {
                throw new InvalidOperationException(
                    $"{mode} DPI {ScaleName(scale)}: el botón '{text}' quedó oculto por el carrito " +
                    $"(botón={BoundsRelativeTo(form, button)}, carrito={BoundsRelativeTo(form, cart)}).");
            }

            var requiredTextWidth = button.Text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Max(line => TextRenderer.MeasureText(line, button.Font).Width) + 12;
            if (mustFitCaptureRow && button.ClientSize.Width < requiredTextWidth)
            {
                throw new InvalidOperationException(
                    $"{mode} DPI {ScaleName(scale)}: el texto del botón '{text}' quedó truncado " +
                    $"({button.ClientSize.Width}px disponibles, {requiredTextWidth}px requeridos).");
            }
        }

        if (!quick && !descendants.OfType<Label>().Any(label => label.Text == "Orden:"))
        {
            throw new InvalidOperationException(
                $"{mode} DPI {ScaleName(scale)}: no quedó visible el filtro Orden.");
        }

        if (!quick && !descendants.OfType<Label>().Any(label => label.Text == "Ubicación:"))
        {
            throw new InvalidOperationException(
                $"{mode} DPI {ScaleName(scale)}: no quedó visible el filtro Ubicación.");
        }

        var locationOptions = new[]
        {
            "Todos los productos",
            "Productos en exhibición",
            "Productos en bodega"
        };
        var locationCombo = descendants.OfType<ComboBox>().FirstOrDefault(combo =>
            Enumerable.Range(0, combo.Items.Count)
                .Select(index => combo.GetItemText(combo.Items[index]))
                .SequenceEqual(locationOptions, StringComparer.Ordinal));
        if (locationCombo is null || form.SelectedLocationFilterForSmoke != locationOptions[0])
        {
            throw new InvalidOperationException(
                $"{mode} DPI {ScaleName(scale)}: el filtro Ubicación no muestra Todos por defecto.");
        }

        if (!quick)
        {
            var longestLocationOptionWidth = TextRenderer.MeasureText(
                locationOptions.OrderByDescending(option => option.Length).First(),
                locationCombo.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            if (locationCombo.ClientSize.Width < longestLocationOptionWidth + 28)
            {
                throw new InvalidOperationException(
                    $"{mode} DPI {ScaleName(scale)}: el filtro Ubicación no cabe completo " +
                    $"({locationCombo.ClientSize.Width}px, requiere {longestLocationOptionWidth + 28}px).");
            }
        }

        foreach (var control in new Control[] { barcodeInput, register, cart, catalog })
        {
            var bounds = BoundsRelativeTo(form, control);
            if (control.ClientSize.Width <= 0 || control.ClientSize.Height <= 0 ||
                !form.ClientRectangle.Contains(bounds))
            {
                throw new InvalidOperationException(
                    $"{mode} DPI {ScaleName(scale)}: {control.GetType().Name} quedó fuera o sin tamaño ({bounds}).");
            }
        }

        foreach (var button in descendants.OfType<Button>().Where(button => button.Text.Length > 2))
        {
            var bounds = BoundsRelativeTo(form, button);
            if (!HasScrollableAncestor(button, form) && !form.ClientRectangle.Contains(bounds))
            {
                throw new InvalidOperationException(
                    $"{mode} DPI {ScaleName(scale)}: botón '{button.Text}' fuera del área cliente ({bounds}).");
            }

            var measured = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            var requiredWidth = Math.Max(button.PreferredSize.Width, measured.Width + 24);
            if (button.ClientSize.Width < requiredWidth ||
                button.ClientSize.Height < measured.Height + 4)
            {
                throw new InvalidOperationException(
                    $"{mode} DPI {ScaleName(scale)}: texto cortado en botón '{button.Text}', " +
                    $"contenido={measured}, requerido={requiredWidth}, cliente={button.ClientSize}.");
            }
        }

        _ = ValidateCriticalText(form, $"{mode} DPI {ScaleName(scale)}");
        _ = ValidateSummaryLabels(form, $"{mode} DPI {ScaleName(scale)}", quick);
        ValidateApplicationIcon(form, mode, scale, quick);
        if (quick)
        {
            ValidateQuickPagination(
                form,
                mode,
                scale,
                form.ClientSize,
                expectedPage: 1,
                expectedTotalPages: 1,
                previousEnabled: false,
                nextEnabled: false);
        }
    }

    private static void ValidateApplicationIcon(
        EmployeePOSForm form,
        InstallationMode mode,
        float scale,
        bool quick)
    {
        var stage = $"POS {(quick ? "rápido" : "tradicional")} {mode} DPI {ScaleName(scale)}";
        var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ArteInventariado.ico");
        if (!File.Exists(assetPath) || form.Icon is null || !form.ShowInTaskbar ||
            form.FormBorderStyle == FormBorderStyle.None)
        {
            throw new InvalidOperationException(
                $"{stage}: el icono de aplicación no está disponible para ventana/tarea/Alt+Tab " +
                $"(asset={File.Exists(assetPath)}, icon={form.Icon is not null}, " +
                $"taskbar={form.ShowInTaskbar}, border={form.FormBorderStyle}).");
        }

        using var bitmap = form.Icon.ToBitmap();
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            throw new InvalidOperationException($"{stage}: el icono cargado no tiene una imagen válida.");
        }
    }

    private static void ValidateQuickCheckoutLayout(EmployeePOSForm form)
    {
        var overlayButtons = Descendants(form)
            .OfType<Button>()
            .Where(button => button.Visible &&
                (button.Text.StartsWith("CONFIRMAR VENTA", StringComparison.Ordinal) ||
                 button.Text is "Cancelar" or "$5" or "$10" or "$20" or "$50" or "$100" or "Exacto"))
            .ToList();
        if (!overlayButtons.Any(button => button.Text.StartsWith("CONFIRMAR VENTA", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("El cobro rápido no mostró su acción final.");
        }

        foreach (var button in overlayButtons)
        {
            var measured = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (button.ClientSize.Width < measured.Width + 20 ||
                button.ClientSize.Height < measured.Height + 4)
            {
                throw new InvalidOperationException(
                    $"Cobro rápido: texto cortado en '{button.Text}', contenido={measured}, cliente={button.ClientSize}.");
            }
        }
    }

    private static bool HasScrollableAncestor(Control control, Control stopAt)
    {
        for (var parent = control.Parent; parent is not null && parent != stopAt; parent = parent.Parent)
        {
            if (parent is ScrollableControl scrollable && scrollable.AutoScroll)
            {
                return true;
            }
        }

        return false;
    }

    private static T? FindAncestor<T>(Control control, Func<T, bool> predicate)
        where T : Control
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is T typed && predicate(typed))
            {
                return typed;
            }
        }

        return null;
    }

    private static string ValidateCriticalText(EmployeePOSForm form, string stage)
    {
        var total = Descendants(form)
            .OfType<Label>()
            .FirstOrDefault(label => label.Visible && label.Text.StartsWith("TOTAL A PAGAR:", StringComparison.Ordinal));
        if (total is null)
        {
            throw new InvalidOperationException($"{stage}: no se encontró el total del POS.");
        }

        var measured = TextRenderer.MeasureText(
            total.Text,
            total.Font,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        var requiredWidth = Math.Max(total.PreferredWidth, measured.Width + 24);
        if (total.ClientSize.Width < requiredWidth)
        {
            throw new InvalidOperationException(
                $"{stage}: el total se trunca; ancho requerido={requiredWidth}, " +
                $"medición={measured}, cliente={total.ClientSize}, texto='{total.Text}'.");
        }

        return $"cliente={total.ClientSize.Width}x{total.ClientSize.Height}; " +
            $"preferido={total.PreferredWidth}; medición={measured.Width}x{measured.Height}";
    }

    private static string ValidateCartGridContent(EmployeePOSForm form, string stage)
    {
        var grid = Descendants(form).OfType<DataGridView>().Single();
        var checkedColumns = new[] { "Cantidad", "PrecioUnitario", "Total" };
        var metrics = new List<string>();

        foreach (var propertyName in checkedColumns)
        {
            var column = grid.Columns
                .Cast<DataGridViewColumn>()
                .SingleOrDefault(candidate => candidate.DataPropertyName == propertyName)
                ?? throw new InvalidOperationException(
                    $"{stage}: no se encontró la columna de carrito '{propertyName}'.");

            var headerFont = column.HeaderCell.InheritedStyle.Font ?? grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
            var headerText = column.HeaderText;
            var requiredWidth = TextRenderer.MeasureText(headerText, headerFont).Width + 4;

            foreach (DataGridViewRow row in grid.Rows)
            {
                var cell = row.Cells[column.Index];
                var value = Convert.ToString(cell.FormattedValue) ?? string.Empty;
                var cellFont = cell.InheritedStyle.Font ?? grid.Font;
                requiredWidth = Math.Max(
                    requiredWidth,
                    TextRenderer.MeasureText(value, cellFont).Width + 8);
            }

            if (column.Width < requiredWidth)
            {
                throw new InvalidOperationException(
                    $"{stage}: columna '{column.HeaderText}' trunca contenido; " +
                    $"ancho={column.Width}, requerido={requiredWidth}.");
            }

            metrics.Add($"{column.HeaderText}={column.Width}/{requiredWidth}");
        }

        return string.Join(", ", metrics);
    }

    private static string ValidateSummaryLabels(EmployeePOSForm form, string stage, bool quick = false)
    {
        var prefixes = quick
            ? new[] { "Subtotal:", "Desc.:" }
            : new[] { "Subtotal:", "Desc.:", "Pagado:", "Vuelto:", "Faltante:" };
        var labels = new List<Label>();
        var metrics = new List<string>();

        foreach (var prefix in prefixes)
        {
            var label = Descendants(form)
                .OfType<Label>()
                .SingleOrDefault(candidate =>
                    candidate.Visible && candidate.Text.StartsWith(prefix, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"{stage}: no quedó visible la etiqueta de resumen '{prefix}'.");
            var bounds = BoundsRelativeTo(form, label);
            if (!form.ClientRectangle.Contains(bounds))
            {
                throw new InvalidOperationException(
                    $"{stage}: etiqueta '{prefix}' fuera del área cliente ({bounds}).");
            }

            var measured = TextRenderer.MeasureText(
                label.Text,
                label.Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            var requiredWidth = Math.Max(label.PreferredWidth, measured.Width + 12);
            if (label.ClientSize.Width < requiredWidth)
            {
                throw new InvalidOperationException(
                    $"{stage}: etiqueta '{prefix}' trunca contenido; " +
                    $"ancho={label.ClientSize.Width}, requerido={requiredWidth}, texto='{label.Text}'.");
            }

            labels.Add(label);
            metrics.Add($"{prefix.TrimEnd(':')}={label.ClientSize.Width}/{requiredWidth}");
        }

        for (var first = 0; first < labels.Count; first++)
        {
            for (var second = first + 1; second < labels.Count; second++)
            {
                var firstBounds = BoundsRelativeTo(form, labels[first]);
                var secondBounds = BoundsRelativeTo(form, labels[second]);
                if (firstBounds.IntersectsWith(secondBounds))
                {
                    throw new InvalidOperationException(
                        $"{stage}: '{labels[first].Text}' se solapa con '{labels[second].Text}'.");
                }
            }
        }

        return string.Join(", ", metrics);
    }

    private static Rectangle BoundsRelativeTo(Control ancestor, Control control)
    {
        var screenLocation = control.PointToScreen(Point.Empty);
        var clientLocation = ancestor.PointToClient(screenLocation);
        return new Rectangle(clientLocation, control.Size);
    }

    private static void WaitUntil(Func<bool> condition, string operation)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > OperationTimeout)
            {
                throw new TimeoutException($"Timeout esperando {operation}.");
            }

            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static void WaitForTask(Task task, string operation)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (watch.Elapsed > OperationTimeout)
            {
                throw new TimeoutException($"Timeout esperando {operation}.");
            }

            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(5);
        }

        task.GetAwaiter().GetResult();
    }

    private static void PerformLayoutRecursive(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls)
        {
            PerformLayoutRecursive(child);
        }
    }

    private static void SaveScreenshot(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static string ScaleName(float scale) => $"{scale * 100:0}";

    private sealed class SmokeFixture
    {
        public const string SimpleBarcode = "000123456789";
        public const string AssortedBarcode = "SUR-A9";
        public const string RedBarcode = "VAR-RED-01";
        public const string BlueBarcode = "VAR-BLUE-02";
        public const int ProductImageAssetId = 501;

        public SmokeFixture(InstallationMode mode, string outputDirectory)
        {
            var thumbnailPath = Path.Combine(outputDirectory, "pos-product-fallback-thumbnail.png");
            using (var bitmap = new Bitmap(96, 96))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(205, 34, 104));
                bitmap.Save(thumbnailPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            Assets = new SmokeProductAssetService(thumbnailPath);
            Simple = CreateItem(1, 11, "PRD-SIMPLE", "SKU-SIMPLE", "Producto simple", null, ModoProducto.SIMPLE) with
            {
                ImagenOwnerKind = ProductAssetOwnerKind.Product,
                ImagenAssetId = ProductImageAssetId,
                ImagenRutaRelativa = "Products/product-fallback.png",
                SubcategoriaId = 100,
                SubcategoriaNombre = "Jarrones"
            };
            Assorted = CreateItem(2, 21, "PRD-SURTIDO", "SKU-SURTIDO", "Producto surtido", null, ModoProducto.SURTIDO);
            RedVariant = CreateItem(3, 31, "PRD-VARIANTE", "SKU-VAR-ROJO", "Producto con variantes", "Rojo", ModoProducto.POR_VARIANTES);
            BlueVariant = CreateItem(3, 32, "PRD-VARIANTE", "SKU-VAR-AZUL", "Producto con variantes", "Azul", ModoProducto.POR_VARIANTES);
            Items = [Simple, Assorted, RedVariant, BlueVariant];
            Catalog = new SmokePosCatalogService(Items);
            Barcodes = new SmokeBarcodeService(new Dictionary<string, PosSkuSaleItem>(StringComparer.Ordinal)
            {
                [SimpleBarcode] = Simple,
                [AssortedBarcode] = Assorted,
                [RedBarcode] = RedVariant,
                [BlueBarcode] = BlueVariant
            });
            Installation = new SmokeInstallationContext(mode);
            SaleService = new SmokeSaleService();
            CreditService = new SmokeCreditService();
            Subcategories = new SmokeSubcategoryService();
        }

        public PosSkuSaleItem Simple { get; }
        public PosSkuSaleItem Assorted { get; }
        public PosSkuSaleItem RedVariant { get; }
        public PosSkuSaleItem BlueVariant { get; }
        public IReadOnlyList<PosSkuSaleItem> Items { get; }
        public SmokePosCatalogService Catalog { get; }
        public SmokeBarcodeService Barcodes { get; }
        public SmokeInstallationContext Installation { get; }
        public SmokeProductAssetService Assets { get; }
        public SmokeSaleService SaleService { get; }
        public SmokeCreditService CreditService { get; }
        public SmokeSubcategoryService Subcategories { get; }

        public EmployeePOSForm CreateForm(bool quick = false) => new(
            new SmokeProductService(),
            Catalog,
            Barcodes,
            new SmokeInventoryService(),
            Installation,
            new SmokeCategoryService(),
            SaleService,
            new SmokeCashService(),
            new SmokeCurrentSession(),
            new SmokePermissionService(Installation.AllowsOperationalWrites),
            CreditService,
            new SmokeSystemConfigService(),
            cashRecovery: null,
            loadDataOnShow: true,
            productAssetService: Assets,
            presentationMode: quick ? PosPresentationMode.Quick : PosPresentationMode.Traditional,
            customerQueryService: quick ? new SmokeCustomerQueryService() : null,
            subcategoryService: Subcategories);

        private static PosSkuSaleItem CreateItem(
            int productId,
            int skuId,
            string productCode,
            string skuCode,
            string productName,
            string? variantName,
            ModoProducto mode) => new(
                productId,
                skuId,
                productCode,
                skuCode,
                productName,
                variantName,
                1,
                "Artesanías",
                mode,
                EstadoProducto.ACTIVO,
                true,
                variantName is null,
                1_500,
                600,
                2,
                20,
                0,
                7,
                0,
                0,
                0,
                null,
                null,
                null,
                null);
    }

    private sealed class SmokeProductAssetService(string thumbnailPath) : IProductAssetService
    {
        public List<(ProductAssetOwnerKind Owner, int AssetId)> Requests { get; } = [];

        public Task<string?> GetThumbnailPathAsync(
            ProductAssetOwnerKind ownerKind,
            int assetId,
            int maximumPixels = 400,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((ownerKind, assetId));
            return Task.FromResult<string?>(
                ownerKind == ProductAssetOwnerKind.Product && assetId == SmokeFixture.ProductImageAssetId
                    ? thumbnailPath
                    : null);
        }

        public Task<IReadOnlyList<ProductAssetSummary>> GetByProductAsync(int productoId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ProductAssetSummary> ImportAsync(ImportProductAssetRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SetPrimaryAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(ProductAssetOwnerKind ownerKind, int assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AssetCleanupResult> CleanupOrphansAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SmokeBarcodeService(
        IReadOnlyDictionary<string, PosSkuSaleItem> itemsByBarcode) : IBarcodeService
    {
        public List<string> LookupCodes { get; } = [];
        public int? LastResolvedSkuId { get; private set; }

        public async Task<BarcodeLookupResult?> LookupExactAsync(
            string codigo,
            CancellationToken cancellationToken = default)
        {
            LookupCodes.Add(codigo);
            await Task.Delay(15, cancellationToken);
            if (!itemsByBarcode.TryGetValue(codigo, out var item))
            {
                LastResolvedSkuId = null;
                return null;
            }

            LastResolvedSkuId = item.ProductoSkuId;
            return new BarcodeLookupResult(
                item.ProductoSkuId,
                codigo,
                item.ProductoId,
                item.CodigoInterno,
                item.ProductoNombre,
                item.Modo,
                item.ProductoSkuId,
                item.CodigoSku,
                item.VarianteNombre,
                item.PrecioVentaEfectivoCentavos,
                item.CostoEfectivoCentavos,
                item.StockFisicoExhibicion,
                item.StockReservadoExhibicion,
                item.StockDisponibleExhibicion,
                item.StockDisponibleBodega,
                item.StockDisponibleSinAsignar);
        }

        public Task<IReadOnlyList<ProductSkuBarcodeSummary>> GetBySkuAsync(int productoSkuId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<BarcodeSkuCandidate>> GetSkuCandidatesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<GenerateMissingBarcodesResult> GenerateMissingBarcodesAsync(GenerateMissingBarcodesRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ProductSkuBarcodeSummary> AssignAsync(AssignProductSkuBarcodeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ProductSkuBarcodeSummary> GenerateInternalAsync(GenerateProductSkuBarcodeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SetPrincipalAsync(int productoSkuBarcodeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SetActiveAsync(int productoSkuBarcodeId, bool active, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SmokePosCatalogService(IReadOnlyList<PosSkuSaleItem> items) : IPosCatalogService
    {
        public int SearchCalls { get; private set; }

        public Task<IReadOnlyList<PosSkuSaleItem>> SearchAsync(
            PosSkuCatalogFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            IEnumerable<PosSkuSaleItem> query = items;
            if (!string.IsNullOrWhiteSpace(filter?.SearchText))
            {
                var term = filter.SearchText.Trim();
                query = query.Where(item =>
                    item.ProductoNombre.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.CodigoInterno.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.CodigoSku.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<PosSkuSaleItem>>(query.ToList());
        }

        public Task<PosSkuSaleItem?> GetBySkuIdAsync(
            int productoSkuId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(items.SingleOrDefault(item => item.ProductoSkuId == productoSkuId));
    }

    private sealed class SmokeInstallationContext(InstallationMode mode) : IInstallationContext
    {
        public InstallationStatus Status { get; } = new(
            StationConfigurationState.Ready,
            mode,
            true,
            Guid.NewGuid(),
            1,
            "pos-smoke",
            null);

        public bool AllowsOperationalWrites => Status.AllowsOperationalWrites;
        public void EnsureInitialized() { }
        public void EnsureOperationalWriteAllowed(string operationName)
        {
            if (!AllowsOperationalWrites)
            {
                throw new InvalidOperationException($"{operationName} no está permitida en Consulta.");
            }
        }
    }

    private sealed class SmokeCategoryService : ICategoryService
    {
        private static readonly CategorySummary Category = new(1, "Artesanías", null, true);
        public Task<IReadOnlyList<CategorySummary>> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategorySummary>>([Category]);
        public Task<IReadOnlyList<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategorySummary>>([Category]);
        public Task<CategorySummary> GetDefaultCategoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Category);
        public Task<CategorySummary> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<CategorySummary> UpdateAsync(UpdateCategoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SetActiveAsync(int categoriaId, bool activa, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(int categoriaId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SmokeSubcategoryService : ISubcategoryService
    {
        private static readonly SubcategorySummary Jarrones = new(
            100,
            1,
            "Jarrones",
            null,
            true);

        public Task<IReadOnlyList<SubcategorySummary>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubcategorySummary>>([Jarrones]);

        public Task<IReadOnlyList<SubcategorySummary>> GetActiveAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubcategorySummary>>([Jarrones]);

        public Task<IReadOnlyList<SubcategorySummary>> GetByCategoryAsync(
            int categoriaId,
            bool soloActivas = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubcategorySummary>>(
                categoriaId == Jarrones.CategoriaId && (!soloActivas || Jarrones.Activa)
                    ? [Jarrones]
                    : []);

        public Task<SubcategorySummary?> GetByIdAsync(
            int subcategoriaId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SubcategorySummary?>(
                subcategoriaId == Jarrones.SubcategoriaId ? Jarrones : null);

        public Task<SubcategorySummary> CreateAsync(
            CreateSubcategoryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SubcategorySummary> UpdateAsync(
            UpdateSubcategoryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetActiveAsync(
            int subcategoriaId,
            bool activa,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int subcategoriaId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SmokeCashService : ICashService
    {
        public Task<CashSummary> GetTodaySummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashSummary(
                1,
                DateOnly.FromDateTime(DateTime.Today),
                EstadoCaja.ABIERTA,
                10_000,
                0,
                0,
                0,
                0,
                10_000,
                null,
                DateTime.Now,
                null,
                "smoke",
                null));

        public Task<CashExpectedBalanceSummary> GetTodayExpectedCashBalanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashExpectedBalanceSummary(
                DateOnly.FromDateTime(DateTime.Today), 1, 10_000, false));

        public Task<IReadOnlyList<CashSummary>> GetSessionHistoryAsync(
            DateOnly desde,
            DateOnly hasta,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CashSummary>>([]);

        public Task<CashOpeningReference?> GetPreviousDayCashOpeningReferenceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CashOpeningReference?>(null);

        public Task<CashRecoveryStatus?> GetOpenCashRecoveryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<CashRecoveryStatus?>(null);
        public Task<CashSummary> ContinueCashAsync(ContinueCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> RegularizeCashAsync(RegularizeCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> RegularizeClosedCashDayAsync(RegularizeClosedCashDayRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> OpenCashAsync(OpenCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> OpenCashAutomaticallyIfEnabledAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashSummary> CloseCashAsync(CloseCashRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashWithdrawalSummary> RegisterWithdrawalAsync(RegisterWithdrawalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashContributionSummary> RegisterCashContributionAsync(RegisterCashContributionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashExpenseSummary> RegisterExpenseAsync(RegisterExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpenseCategoryOption>> GetExpenseCategoriesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CashWithdrawalSummary>> GetTodayWithdrawalsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CashExpenseSummary>> GetTodayExpensesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CashRefundSummary>> GetTodayRefundsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SmokePermissionService(bool allowCredit) : IEmployeePermissionService
    {
        public Task<bool> HasPermissionAsync(
            PermisoEmpleado permiso,
            int? usuarioId = null,
            CancellationToken cancellationToken = default)
        {
            if (permiso == PermisoEmpleado.CREDITO_REGISTRAR)
            {
                return Task.FromResult(allowCredit);
            }

            return Task.FromResult(permiso is
                PermisoEmpleado.POS_ACCEDER or
                PermisoEmpleado.VENTA_REGISTRAR or
                PermisoEmpleado.DESCUENTO_LINEA_APLICAR or
                PermisoEmpleado.DESCUENTO_GENERAL_APLICAR or
                PermisoEmpleado.PAGO_TRANSFERENCIA_USAR or
                PermisoEmpleado.CLIENTES_ACCEDER);
        }

        public Task EnsurePermissionAsync(PermisoEmpleado permiso, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<bool> HasAnyPermissionAsync(IReadOnlyCollection<PermisoEmpleado> permisos, int? usuarioId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EmployeePermissionState>> GetPermissionsForUserAsync(int usuarioId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdatePermissionsAsync(UpdateEmployeePermissionsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestoreRecommendedTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ApplySafeOperationalTemplateAsync(int usuarioId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EnsureRecommendedPermissionsForEmployeeAsync(int usuarioId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SmokeCustomerQueryService : ICustomerQueryService
    {
        private static readonly CustomerSummary Customer = new(
            41,
            "CLI-000041",
            "Cliente smoke",
            "7000-0041",
            "00000041-0",
            EstadoCliente.ACTIVO,
            null,
            0);

        public Task<IReadOnlyList<CustomerSummary>> SearchAsync(
            CustomerFilter filter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerSummary>>([Customer]);

        public Task<CustomerSaleSelection?> GetSaleSelectionAsync(
            int clienteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CustomerSaleSelection?>(new CustomerSaleSelection(
                Customer.ClienteId,
                Customer.CodigoCliente,
                Customer.NombreCompleto,
                true));

        public Task<CustomerDetail?> GetByIdAsync(
            int clienteId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CustomerPurchaseHistoryRow>> GetPurchaseHistoryAsync(
            int clienteId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SalesHistoryDetail?> GetPurchaseDetailAsync(
            int clienteId,
            int ventaId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SmokeCurrentSession : ICurrentSession
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = new(1, "Operador smoke", "smoke", RolUsuario.EMPLEADO);
        public bool EstaAutenticado => UsuarioActual is not null;
        public bool EsAdmin => false;
        public bool TieneRol(RolUsuario rol) => UsuarioActual?.Rol == rol;
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
    }

    private sealed class SmokeSystemConfigService : ISystemConfigService
    {
        public Task<bool> IsBitcoinEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<SystemConfigSetting>> GetBasicSettingsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateBasicSettingsAsync(UpdateSystemConfigRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SmokeSaleService : ISaleService
    {
        public List<RegisterSaleRequest> Requests { get; } = [];

        public bool FailNext { get; set; }

        public TaskCompletionSource<bool>? RegistrationGate { get; set; }

        public bool FailureObserved { get; private set; }

        public async Task<SaleResult> RegisterSaleAsync(
            RegisterSaleRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (RegistrationGate is not null)
            {
                await RegistrationGate.Task.WaitAsync(cancellationToken);
            }

            if (FailNext)
            {
                FailNext = false;
                FailureObserved = true;
                throw new InvalidOperationException("Fallo de registro simulado para smoke.");
            }

            return BuildResult(request, Requests.Count);
        }

        private static SaleResult BuildResult(RegisterSaleRequest request, int sequence)
        {
            var lines = request.Lines
                .Select((line, index) =>
                {
                    var subtotal = line.Cantidad * 1_500L;
                    var total = Math.Max(0, subtotal - line.DescuentoCentavos);
                    var skuCode = line.ProductoSkuId.HasValue
                        ? $"SKU-{line.ProductoSkuId.Value:000}"
                        : null;
                    return new SaleLineResult(
                        index + 1,
                        line.ProductoId,
                        $"PRD-{line.ProductoId:000}",
                        "Producto smoke",
                        line.Cantidad,
                        1_500,
                        subtotal,
                        line.DescuentoCentavos,
                        total,
                        line.ProductoSkuId,
                        skuCode,
                        null);
                })
                .ToList();
            var subtotalCentavos = lines.Sum(line => line.SubtotalCentavos);
            var discountCentavos = lines.Sum(line => line.DescuentoCentavos) + request.DescuentoGeneralCentavos;
            var totalCentavos = Math.Max(0, subtotalCentavos - discountCentavos);
            var paidCentavos = request.Payments.Sum(payment => payment.MontoCentavos);
            var cashCentavos = request.Payments
                .Where(payment => payment.MetodoPago == MetodoPago.EFECTIVO)
                .Sum(payment => payment.MontoCentavos);
            var changeCentavos = Math.Min(cashCentavos, Math.Max(0, paidCentavos - totalCentavos));
            var credit = request.Credit is null
                ? null
                : BuildCreditResult(request.Credit, lines, totalCentavos, paidCentavos, sequence);

            return new SaleResult(
                sequence,
                $"VTA-SMOKE-{sequence:000}",
                1,
                1,
                request.ClienteId,
                request.ClienteId is null ? "Consumidor final" : "Cliente smoke",
                DateTime.UtcNow,
                subtotalCentavos,
                discountCentavos,
                totalCentavos,
                paidCentavos,
                changeCentavos,
                lines,
                request.Payments
                    .Select((payment, index) => new SalePaymentResult(
                        index + 1,
                        payment.MetodoPago,
                        payment.MontoCentavos,
                        payment.Referencia))
                    .ToList(),
                credit);
        }

        private static SaleCreditResult BuildCreditResult(
            CreditSaleRequest request,
            IReadOnlyList<SaleLineResult> saleLines,
            long totalCentavos,
            long paidCentavos,
            int sequence)
        {
            var capitalCentavos = Math.Max(0, totalCentavos - paidCentavos);
            var lines = request.Lines
                .Select((line, index) =>
                {
                    var saleLine = saleLines.FirstOrDefault(candidate =>
                        candidate.ProductoId == line.ProductoId &&
                        candidate.ProductoSkuId == line.ProductoSkuId);
                    var subtotal = line.CantidadFinanciada * 1_500L;
                    return new SaleCreditLineResult(
                        index + 1,
                        saleLine?.DetalleVentaId ?? index + 1,
                        line.ProductoId,
                        $"PRD-{line.ProductoId:000}",
                        "Producto smoke",
                        line.CantidadFinanciada,
                        subtotal,
                        subtotal);
                })
                .ToList();

            return new SaleCreditResult(
                100 + sequence,
                $"CXC-SMOKE-{sequence:000}",
                0,
                0,
                capitalCentavos,
                0,
                0,
                capitalCentavos,
                capitalCentavos,
                DateTime.UtcNow.AddDays(request.PlazoDias),
                lines);
        }
    }

    private sealed class SmokeProductService : IProductService
    {
        public Task<IReadOnlyList<ProductSummary>> GetAllAsync(ProductFilter? filter = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductSummary>> GetAvailableForSaleAsync(ProductFilter? filter = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductSummary>> SearchByNameAsync(string nombre, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductSummary?> GetByCodigoInternoAsync(string codigoInterno, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductSummary>> GetLowStockAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductSummary> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProductSummary> UpdateAsync(UpdateProductRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetEstadoAsync(int productoId, EstadoProducto estado, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int productoId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SmokeInventoryService : IInventoryService
    {
        public Task<InventoryMovementSummary> AdjustStockAsync(AdjustStockRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryAdjustmentResult> AdjustPhysicalCountAsync(AdjustInventoryCountRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InventoryMovementSummary>> GetMovementsByProductAsync(int productoId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InventoryBalanceSummary>> GetBalancesByProductAsync(int productoId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InventoryBalanceSummary>> TransferAsync(TransferInventoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InventoryBalanceSummary>> RegisterWasteAsync(RegisterInventoryWasteRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetExhibitionMinimumAsync(SetExhibitionMinimumRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReplenishmentSummary>> GetReplenishmentAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PostponeReplenishmentAsync(PostponeReplenishmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DismissReplenishmentAsync(DismissReplenishmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StagnantInventorySummary>> GetStagnantInventoryAsync(StagnantInventoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InventoryReconciliationSummary>> ReconcileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SmokeCreditService : ICreditService
    {
        private readonly bool _provideCustomerPolicy;

        public SmokeCreditService(bool provideCustomerPolicy = false)
        {
            _provideCustomerPolicy = provideCustomerPolicy;
        }

        public Task<CreditPolicySettings> GetGeneralPolicyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateGeneralPolicyAsync(UpdateCreditPolicySettingsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerCreditPolicy> GetCustomerPolicyAsync(int clienteId, CancellationToken cancellationToken = default) => _provideCustomerPolicy
            ? Task.FromResult(new CustomerCreditPolicy(clienteId, true, null, null, null, null, null, null, null, null))
            : throw new NotSupportedException();
        public Task UpdateCustomerPolicyAsync(UpdateCustomerCreditPolicyRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CustomerCreditSummary> GetCustomerSummaryAsync(
            int clienteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CustomerCreditSummary(
                clienteId,
                "CLI-000041",
                "Cliente smoke",
                true,
                true,
                null,
                SituacionCrediticia.AL_DIA,
                0,
                1_000_000,
                1_000_000,
                0,
                10,
                null,
                new EffectiveCreditPolicy(
                    clienteId,
                    1_000_000,
                    10,
                    30,
                    true,
                    0,
                    30)));
        public Task<IReadOnlyList<AccountReceivableSummary>> GetAccountsAsync(AccountReceivableFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountReceivableDetail?> GetAccountDetailAsync(int cuentaPorCobrarId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
