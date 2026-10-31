using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;
using ArteInventariado.WinForms.Controls;

namespace ArteInventariado.WinForms;

public class StockAdjustmentForm : Form
{
    private readonly IInventoryService _inventoryService;
    private readonly ProductSummary? _product;
    private readonly ProductSkuSummary? _sku;
    private readonly IReadOnlyList<InventoryBalanceSummary> _balances;
    private readonly Guid _operationId = Guid.NewGuid();

    private readonly ComboBox _cmbUbicacion = new();
    private readonly NumericUpDown _numCantidadContada = new();
    private readonly TextBox _txtMotivo = new();
    private readonly TextBox _txtPassword = new();
    private readonly Label _lblExistencia = new();
    private readonly Label _lblReservado = new();
    private readonly Label _lblDisponible = new();
    private readonly Label _lblDiferencia = new();
    private readonly Label _lblFeedback = new();
    private readonly Button _btnGuardar = new();
    private readonly Button _btnCancelar = new();

    // Compatibility with the legacy visual smoke. The operational flow uses the SKU constructor.
    public StockAdjustmentForm(IInventoryService inventoryService, ProductSummary product)
        : this(inventoryService, product, null, [])
    {
    }

    public StockAdjustmentForm(
        IInventoryService inventoryService,
        ProductSkuSummary sku,
        IReadOnlyList<InventoryBalanceSummary> balances)
        : this(inventoryService, null, sku, balances)
    {
    }

    private StockAdjustmentForm(
        IInventoryService inventoryService,
        ProductSummary? product,
        ProductSkuSummary? sku,
        IReadOnlyList<InventoryBalanceSummary> balances)
    {
        _inventoryService = inventoryService;
        _product = product;
        _sku = sku;
        _balances = balances;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Ajustar existencias";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ClientSize = new Size(760, 700);
        MinimumSize = new Size(700, 680);
        AutoScaleMode = AutoScaleMode.Dpi;
        UITheme.StyleForm(this);

        ConfigureLocationSelector();
        _numCantidadContada.Minimum = 0;
        _numCantidadContada.Maximum = 1_000_000;
        _numCantidadContada.Width = 180;
        _numCantidadContada.AccessibleName = "Cantidad contada";
        _numCantidadContada.ValueChanged += (_, _) => RefreshBalanceSummary();

        _lblExistencia.AccessibleName = "Existencia anterior";
        _lblReservado.AccessibleName = "Reservado";
        _lblDisponible.AccessibleName = "Disponible";
        _lblDiferencia.AccessibleName = "Diferencia del ajuste";
        foreach (var label in new[] { _lblExistencia, _lblReservado, _lblDisponible, _lblDiferencia })
        {
            label.Dock = DockStyle.Fill;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = UITheme.NormalBoldFont;
            label.ForeColor = UITheme.TextDark;
        }

        _txtMotivo.Multiline = true;
        _txtMotivo.MaxLength = 500;
        _txtMotivo.ScrollBars = ScrollBars.Vertical;
        _txtMotivo.Dock = DockStyle.Fill;
        _txtMotivo.AccessibleName = "Motivo obligatorio";
        UITheme.StyleTextBox(_txtMotivo);

        _txtPassword.UseSystemPasswordChar = true;
        _txtPassword.MaxLength = 256;
        _txtPassword.Dock = DockStyle.Fill;
        _txtPassword.AccessibleName = "Contraseña del usuario ADMIN";
        UITheme.StyleTextBox(_txtPassword);
        _txtPassword.TextChanged += (_, _) =>
        {
            if (_lblFeedback.Visible)
            {
                _lblFeedback.Visible = false;
                _lblFeedback.Text = string.Empty;
            }
        };

        _lblFeedback.AutoSize = true;
        _lblFeedback.Dock = DockStyle.Fill;
        _lblFeedback.ForeColor = UITheme.DangerColor;
        _lblFeedback.Visible = false;

        ConfigureButton(_btnGuardar, "Aplicar ajuste", 170, BtnGuardar_Click);
        UITheme.StylePrimaryButton(_btnGuardar);
        ConfigureButton(_btnCancelar, "Cancelar", 130, (_, _) => Close());
        UITheme.StyleOutlineButton(_btnCancelar);

        var current = CurrentBalance();
        if (current is not null)
        {
            _numCantidadContada.Value = current.ExistenciaFisica;
        }
        else
        {
            _numCantidadContada.Value = 0;
            _btnGuardar.Enabled = false;
        }

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(UITheme.SpaceLg),
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var lblSku = new Label
        {
            Text = BuildSkuText(),
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = UITheme.NormalBoldFont,
            AccessibleName = "SKU ajustado"
        };
        UITheme.StyleStatusLabel(lblSku, UITheme.SecondaryColor);
        fields.Controls.Add(lblSku, 0, 0);
        fields.SetColumnSpan(lblSku, 2);

        fields.Controls.Add(FieldLabel("Ubicación exacta"), 0, 1);
        fields.Controls.Add(_cmbUbicacion, 1, 1);

        var metrics = CreateBalanceSummary();
        fields.Controls.Add(metrics, 0, 2);
        fields.SetColumnSpan(metrics, 2);

        fields.Controls.Add(FieldLabel("Cantidad contada"), 0, 3);
        fields.Controls.Add(_numCantidadContada, 1, 3);

        fields.Controls.Add(FieldLabel("Diferencia"), 0, 4);
        fields.Controls.Add(CreateDifferencePanel(), 1, 4);

        fields.Controls.Add(FieldLabel("Motivo obligatorio"), 0, 5);
        fields.Controls.Add(_txtMotivo, 1, 5);

        var passwordPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm),
            BackColor = UITheme.SurfaceMutedColor,
            Margin = Padding.Empty
        };
        passwordPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        passwordPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        passwordPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        passwordPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        passwordPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        passwordPanel.Controls.Add(new Label
        {
            Text = "Contraseña del usuario ADMIN de esta sesión",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark,
            Margin = new Padding(0, 0, 0, UITheme.SpaceXs)
        }, 0, 0);
        passwordPanel.Controls.Add(_txtPassword, 0, 1);
        passwordPanel.Controls.Add(new Label
        {
            Text = "Es la contraseña de acceso a ArteInventariado, no la de Windows.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = new Padding(0, UITheme.SpaceXs, 0, 0)
        }, 0, 2);
        passwordPanel.Controls.Add(_lblFeedback, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm),
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty
        };
        actions.Controls.Add(_btnCancelar);
        actions.Controls.Add(_btnGuardar);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(fields, 0, 1);
        root.Controls.Add(passwordPanel, 0, 2);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);

        AcceptButton = _btnGuardar;
        CancelButton = _btnCancelar;
        RefreshBalanceSummary();
    }

    private string BuildSkuText()
    {
        if (_sku is not null)
        {
            return string.IsNullOrWhiteSpace(_sku.NombreVariante)
                ? $"SKU {_sku.CodigoSku}"
                : $"SKU {_sku.CodigoSku} · {_sku.NombreVariante}";
        }

        return _product is null
            ? "SKU no disponible para este smoke"
            : $"{_product.CodigoInterno} · {_product.Nombre}";
    }

    private static TableLayoutPanel CreateHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UITheme.SpaceLg, UITheme.SpaceSm, UITheme.SpaceLg, UITheme.SpaceSm),
            BackColor = UITheme.SurfaceColor,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = "Ajuste de existencias",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = UITheme.HeaderFont,
            ForeColor = UITheme.TextDark,
            Margin = Padding.Empty
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = "Registra el conteo real por ubicación. No crea compras ni afecta caja.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, UITheme.SpaceXs, 0, 0)
        }, 0, 1);
        return header;
    }

    private TableLayoutPanel CreateBalanceSummary()
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = UITheme.SurfaceColor
        };
        for (var column = 0; column < 3; column++)
        {
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        }

        summary.Controls.Add(CreateMetricCard("Existencia anterior", _lblExistencia), 0, 0);
        summary.Controls.Add(CreateMetricCard("Reservado", _lblReservado), 1, 0);
        summary.Controls.Add(CreateMetricCard("Disponible", _lblDisponible), 2, 0);
        return summary;
    }

    private static TableLayoutPanel CreateMetricCard(string title, Label value)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(UITheme.SpaceSm),
            Margin = new Padding(0, 0, UITheme.SpaceSm, 0),
            BackColor = UITheme.SurfaceMutedColor
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = Padding.Empty
        }, 0, 0);
        card.Controls.Add(value, 0, 1);
        return card;
    }

    private TableLayoutPanel CreateDifferencePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        panel.Controls.Add(_lblDiferencia, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = "Puede ser negativa para descontar un exceso.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = UITheme.CaptionFont,
            ForeColor = UITheme.MutedTextColor,
            Margin = Padding.Empty
        }, 0, 1);
        return panel;
    }

    private void ConfigureLocationSelector()
    {
        _cmbUbicacion.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbUbicacion.Dock = DockStyle.Fill;
        _cmbUbicacion.AccessibleName = "Ubicación exacta";
        _cmbUbicacion.Items.AddRange(new object[]
        {
            new LocationChoice(UbicacionInventario.SIN_ASIGNAR, "Sin asignar"),
            new LocationChoice(UbicacionInventario.EXHIBICION, "Exhibición"),
            new LocationChoice(UbicacionInventario.BODEGA, "Bodega")
        });
        _cmbUbicacion.SelectedIndex = 0;
        _cmbUbicacion.SelectedValueChanged += (_, _) =>
        {
            var balance = CurrentBalance();
            _numCantidadContada.Value = balance is null
                ? 0
                : Math.Clamp(balance.ExistenciaFisica, (int)_numCantidadContada.Minimum, (int)_numCantidadContada.Maximum);
            _btnGuardar.Enabled = balance is not null;
            RefreshBalanceSummary();
        };
    }

    private InventoryBalanceSummary? CurrentBalance()
    {
        if (_sku is null || GetSelectedLocation() is not { } location)
        {
            return null;
        }

        return _balances.SingleOrDefault(item =>
            item.ProductoSkuId == _sku.ProductoSkuId && item.Ubicacion == location);
    }

    private UbicacionInventario? GetSelectedLocation()
    {
        if (_cmbUbicacion.SelectedItem is LocationChoice choice)
        {
            return choice.Value;
        }

        // Keep a fixed index fallback for the three manually populated choices.
        return _cmbUbicacion.SelectedIndex switch
        {
            0 => UbicacionInventario.SIN_ASIGNAR,
            1 => UbicacionInventario.EXHIBICION,
            2 => UbicacionInventario.BODEGA,
            _ => null
        };
    }

    private void RefreshBalanceSummary()
    {
        var balance = CurrentBalance();
        if (balance is null)
        {
            _lblExistencia.Text = "No disponible";
            _lblReservado.Text = "No disponible";
            _lblDisponible.Text = "No disponible";
            _lblDiferencia.Text = "No disponible";
            _lblDiferencia.ForeColor = UITheme.MutedTextColor;
            return;
        }

        _lblExistencia.Text = balance.ExistenciaFisica.ToString();
        _lblReservado.Text = balance.Reservado.ToString();
        _lblDisponible.Text = balance.Disponible.ToString();
        var difference = (int)_numCantidadContada.Value - balance.ExistenciaFisica;
        _lblDiferencia.Text = difference.ToString("+#;-#;0");
        _lblDiferencia.ForeColor = difference < 0
            ? UITheme.DangerColor
            : difference > 0
                ? UITheme.SuccessColor
                : UITheme.TextDark;
    }

    private async void BtnGuardar_Click(object? sender, EventArgs e)
    {
        if (_sku is null)
        {
            MessageBox.Show("No hay un SKU exacto seleccionado.", "Ajuste de inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var balance = CurrentBalance();
        if (balance is null)
        {
            MessageBox.Show("No existe saldo canónico para esta ubicación.", "Ajuste de inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtMotivo.Text))
        {
            MessageBox.Show("El motivo es obligatorio.", "Ajuste de inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtMotivo.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(_txtPassword.Text))
        {
            MessageBox.Show("Ingrese la contraseña de la cuenta ADMIN con la que inició sesión en ArteInventariado.",
                "Ajuste de inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtPassword.Focus();
            return;
        }

        _btnGuardar.Enabled = false;
        _lblFeedback.Visible = false;
        try
        {
            var location = GetSelectedLocation()
                ?? throw new InvalidOperationException("Seleccione una ubicación válida.");
            var result = await _inventoryService.AdjustPhysicalCountAsync(new AdjustInventoryCountRequest(
                _operationId,
                _sku.ProductoSkuId,
                location,
                (int)_numCantidadContada.Value,
                _txtMotivo.Text.Trim(),
                _txtPassword.Text));

            _txtPassword.Clear();
            if (result.Diferencia == 0)
            {
                MessageBox.Show("El conteo coincide con la existencia actual. No hubo cambios.",
                    "Ajuste de inventario", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show($"Ajuste aplicado: {result.ExistenciaAnterior} → {result.CantidadContada} ({result.Diferencia:+#;-#;0}).",
                    "Ajuste de inventario", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _txtPassword.Clear();
            _lblFeedback.Text = $"No se aplicó el ajuste. {ex.Message}";
            _lblFeedback.Visible = true;
            _txtPassword.Focus();
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnGuardar.Enabled = CurrentBalance() is not null;
            }
        }
    }

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = UITheme.NormalBoldFont,
        ForeColor = UITheme.TextDark
    };

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Size = new Size(width, 42);
        button.Margin = new Padding(UITheme.SpaceSm, 0, 0, 0);
        button.Click += handler;
        UITheme.EnsureButtonContentFits(button, width, 42, 36);
    }

    private sealed record LocationChoice(UbicacionInventario Value, string Text)
    {
        public override string ToString() => Text;
    }
}
