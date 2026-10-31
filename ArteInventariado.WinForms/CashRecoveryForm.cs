using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

internal enum CashRecoveryChoice
{
    ReviewLater,
    Continue,
    Regularize
}

internal sealed class CashRecoveryForm : Form
{
    public CashRecoveryForm(
        CashRecoveryStatus status,
        bool canContinue,
        bool canRegularize,
        bool readOnly)
    {
        Choice = CashRecoveryChoice.ReviewLater;
        BuildUi(status, canContinue, canRegularize, readOnly);
    }

    public CashRecoveryChoice Choice { get; private set; }

    private void BuildUi(
        CashRecoveryStatus status,
        bool canContinue,
        bool canRegularize,
        bool readOnly)
    {
        Text = "Recuperación operacional de caja";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(650, 540);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        var cash = status.Cash;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UITheme.DashboardBackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = status.IsFromPreviousDay
                ? "Caja abierta de una fecha anterior"
                : "Caja abierta de una sesión anterior",
            TextAlign = ContentAlignment.MiddleLeft
        };
        UITheme.StyleLabelHeader(title);
        root.Controls.Add(title, 0, 0);

        var warning = status.IsFromPreviousDay
            ? "Esta caja no se cerrará automáticamente. Se necesita el efectivo contado real para regularizarla."
            : "La caja conserva su apertura y movimientos. Puede continuarla sin crear otra caja.";
        var extra = status.AdditionalOpenCashSessions > 0
            ? $"\r\n\r\nAdemás existen {status.AdditionalOpenCashSessions} caja(s) abierta(s) más antigua(s), que deberán revisarse explícitamente."
            : string.Empty;
        var details = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            BackColor = UITheme.SurfaceColor,
            ForeColor = UITheme.TextDark,
            Font = UITheme.NormalFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"Caja #{cash.CajaId}\r\n" +
                   $"Fecha de sesión: {cash.FechaOperacion:dd/MM/yyyy}\r\n" +
                   $"Apertura: {cash.FechaApertura?.ToLocalTime():dd/MM/yyyy HH:mm}\r\n" +
                   $"Abierta por: {cash.UsuarioApertura ?? "No disponible"}\r\n" +
                   $"Efectivo inicial: ${Format(cash.MontoInicialCentavos)}\r\n" +
                   $"Efectivo esperado actual: ${Format(cash.TotalEsperadoCentavos)}\r\n\r\n" +
                   warning + extra
        };
        root.Controls.Add(details, 0, 1);

        if (readOnly)
        {
            var readOnlyLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Modo Consulta: puede revisar el estado, pero esta estación no puede continuar ni regularizar cajas.",
                ForeColor = UITheme.PrimaryColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UITheme.NormalBoldFont
            };
            root.Controls.Add(readOnlyLabel, 0, 2);
        }
        else
        {
            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = BuildPermissionNotice(canContinue, canRegularize),
                ForeColor = UITheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UITheme.NormalFont
            }, 0, 2);
        }

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var later = CreateButton("Revisar después", 150, UITheme.StyleSecondaryButton);
        later.Click += (_, _) => Close();
        actions.Controls.Add(later);

        if (canRegularize && !readOnly)
        {
            var regularize = CreateButton("Regularizar / cerrar", 190, UITheme.StyleDangerButton);
            regularize.Click += (_, _) => Select(CashRecoveryChoice.Regularize);
            actions.Controls.Add(regularize);
        }

        if (canContinue && !readOnly)
        {
            var continueButton = CreateButton("Continuar caja", 160, UITheme.StylePrimaryButton);
            continueButton.Click += (_, _) => Select(CashRecoveryChoice.Continue);
            actions.Controls.Add(continueButton);
            AcceptButton = continueButton;
        }

        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
    }

    private void Select(CashRecoveryChoice choice)
    {
        Choice = choice;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Button CreateButton(string text, int minimumWidth, Action<Button> style)
    {
        var button = new Button { Text = text, Margin = new Padding(8, 0, 0, 0) };
        style(button);
        UITheme.EnsureButtonContentFits(button, minimumWidth, 44, 40);
        button.Size = button.MinimumSize;
        return button;
    }

    private static string BuildPermissionNotice(bool canContinue, bool canRegularize)
    {
        if (canContinue && canRegularize)
        {
            return "Seleccione continuar para reutilizar la misma caja, o regularizar para cerrarla con conteo real.";
        }

        if (canContinue)
        {
            return "Puede continuar esta caja. La regularización requiere permiso para cerrar caja.";
        }

        if (canRegularize)
        {
            return "Puede regularizar esta caja. Continuarla requiere permiso para abrir caja.";
        }

        return "Su usuario no tiene permisos para continuar ni regularizar esta caja.";
    }

    private static string Format(long cents) => (cents / 100m).ToString("N2");
}
