using System.ComponentModel;
using ArteInventariado.Application.Interfaces;
using ArteInventariado.Application.Models;
using ArteInventariado.Domain.Enums;

namespace ArteInventariado.WinForms;

public class PurchaseReceptionForm : Form
{
    private readonly PurchaseDetail _purchase;
    private readonly IPurchaseReceptionService _receptionService;
    private readonly BindingList<ReceptionLineRow> _rows = [];
    private readonly BindingSource _binding = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _txtObservations = new();
    private readonly Button _btnReceiveAll = new();
    private readonly Button _btnClear = new();
    private readonly Button _btnConfirm = new();
    private readonly Button _btnCancel = new();
    private readonly Label _lblStatus = new();
    private readonly Guid _operationId = Guid.NewGuid();

    public PurchaseReceptionForm(PurchaseDetail purchase, IPurchaseReceptionService receptionService)
    {
        _purchase = purchase;
        _receptionService = receptionService;

        BuildUi();
        Load += PurchaseReceptionForm_Load;
    }

    private void PurchaseReceptionForm_Load(object? sender, EventArgs e)
    {
        LoadRows();
    }

    private void BuildUi()
    {
        Text = $"Recepcion de compra {_purchase.CodigoCompra}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1160, 660);
        MinimumSize = new Size(1100, 560);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildSummary(), 0, 1);
        root.Controls.Add(BuildGridSection(), 0, 2);
        root.Controls.Add(BuildObservations(), 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(16, 10, 16, 10),
            Margin = new Padding(0, 0, 0, 12),
            ColumnCount = 1,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = $"Recibir productos - {_purchase.CodigoCompra}",
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        UITheme.StyleLabelTitle(title);

        var subtitle = new Label
        {
            Text = $"{_purchase.CodigoProveedor} - {_purchase.Proveedor}",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor
        };
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        return panel;
    }

    private Control BuildSummary()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(18, 20, 18, 22),
            ColumnCount = 4,
            RowCount = 1,
            MinimumSize = new Size(0, 136),
            Margin = new Padding(0, 0, 0, 12)
        };
        for (var i = 0; i < 4; i++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        var totalBought = _purchase.Detalles.Sum(d => d.CantidadComprada);
        var totalReceived = _purchase.Detalles.Sum(d => d.CantidadRecibida);
        var totalPending = _purchase.Detalles.Sum(d => d.CantidadPendiente);

        AddSummaryCell(panel, "Estado", _purchase.EstadoRecepcion.ToString(), 0);
        AddSummaryCell(panel, "Comprado", totalBought.ToString(), 1);
        AddSummaryCell(panel, "Recibido", totalReceived.ToString(), 2);
        AddSummaryCell(panel, "Pendiente", totalPending.ToString(), 3);
        return panel;
    }

    private Control BuildGridSection()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12)
        };
        ConfigureGrid();
        _binding.DataSource = _rows;
        _grid.DataSource = _binding;
        _grid.CellEndEdit += (_, _) => UpdateStatus();
        panel.Controls.Add(_grid);
        return panel;
    }

    private Control BuildObservations()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.SurfaceColor,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 12, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = "Observaciones de recepcion",
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.TextDark
        };
        _txtObservations.Multiline = true;
        _txtObservations.Dock = DockStyle.Fill;
        UITheme.StyleTextBox(_txtObservations);
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(_txtObservations, 0, 1);
        return panel;
    }

    private Control BuildActions()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.Font = UITheme.NormalBoldFont;
        _lblStatus.ForeColor = UITheme.MutedTextColor;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true
        };

        ConfigureButton(_btnCancel, "Cancelar", 110, (_, _) => Close());
        UITheme.StyleSecondaryButton(_btnCancel);
        ConfigureButton(_btnConfirm, "Confirmar recepcion", 176, async (_, _) => await ConfirmReceptionAsync());
        UITheme.StyleSuccessButton(_btnConfirm);
        ConfigureButton(_btnClear, "Limpiar", 104, (_, _) => ClearReception());
        UITheme.StyleSecondaryButton(_btnClear);
        ConfigureButton(_btnReceiveAll, "Recibir todo", 124, (_, _) => ReceiveAll());
        UITheme.StylePrimaryButton(_btnReceiveAll);

        actions.Controls.AddRange([_btnCancel, _btnConfirm, _btnClear, _btnReceiveAll]);
        panel.Controls.Add(_lblStatus, 0, 0);
        panel.Controls.Add(actions, 1, 0);
        return panel;
    }

    private void LoadRows()
    {
        _rows.Clear();
        foreach (var line in _purchase.Detalles.Where(d => d.CantidadPendiente > 0))
        {
            _rows.Add(new ReceptionLineRow
            {
                DetalleCompraId = line.DetalleCompraId,
                Codigo = line.CodigoSku ?? line.CodigoProducto,
                Producto = string.IsNullOrWhiteSpace(line.Variante)
                    ? line.Producto
                    : $"{line.Producto} / {line.Variante}",
                Comprado = line.CantidadComprada,
                Recibido = line.CantidadRecibida,
                Pendiente = line.CantidadPendiente,
                    CostoUnitario = SaleReceiptViewModel.FormatDollars(line.CostoUnitarioCentavos),
                CantidadARecibir = 0,
                UbicacionDestino = UbicacionInventario.SIN_ASIGNAR
            });
        }

        UpdateStatus();
    }

    private void ReceiveAll()
    {
        foreach (var row in _rows)
        {
            row.CantidadARecibir = row.Pendiente;
        }

        _binding.ResetBindings(false);
        UpdateStatus();
    }

    private void ClearReception()
    {
        foreach (var row in _rows)
        {
            row.CantidadARecibir = 0;
        }

        _binding.ResetBindings(false);
        UpdateStatus();
    }

    private async Task ConfirmReceptionAsync()
    {
        try
        {
            _grid.EndEdit();
            var selectedLines = _rows
                .Where(r => r.CantidadARecibir > 0)
                .ToList();
            if (selectedLines.Count == 0)
            {
                MessageBox.Show("Ingrese al menos una cantidad a recibir.", "Recepcion de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var invalid = selectedLines.FirstOrDefault(r => r.CantidadARecibir > r.Pendiente);
            if (invalid is not null)
            {
                MessageBox.Show($"No puede recibir mas de lo pendiente para '{invalid.Producto}'.", "Recepcion de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var invalidLocation = selectedLines.FirstOrDefault(r => !IsReceptionLocation(r.UbicacionDestino));
            if (invalidLocation is not null)
            {
                MessageBox.Show($"Seleccione una ubicación válida para '{invalidLocation.Producto}'.", "Recepción de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnConfirm.Enabled = false;
            var result = await _receptionService.RegisterReceptionAsync(new RegisterPurchaseReceptionRequest(
                _purchase.CompraId,
                _operationId,
                DateTime.UtcNow,
                selectedLines
                    .Select(r => new PurchaseReceptionLineRequest(r.DetalleCompraId, r.CantidadARecibir, r.UbicacionDestino))
                    .ToList(),
                _txtObservations.Text.Trim()));

            MessageBox.Show(
                $"Recepcion {result.CodigoRecepcion} registrada.\nLineas: {result.Detalles.Count}",
                "Recepcion de compra",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Recepcion de compra", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _btnConfirm.Enabled = true;
        }
    }

    private void ConfigureGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;

        AddGridColumn("Codigo", "Codigo", 115, 10, true);
        AddGridColumn("Producto", "Producto", 260, 32, true);
        AddGridColumn("Comprado", "Comprado", 90, 8, true, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Recibido", "Recibido", 90, 8, true, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Pendiente", "Pendiente", 90, 8, true, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("CostoUnitario", "Costo", 110, 10, true, DataGridViewContentAlignment.MiddleRight);
        AddGridColumn("CantidadARecibir", "Recibir ahora", 120, 12, false, DataGridViewContentAlignment.MiddleCenter);
        var locationColumn = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(ReceptionLineRow.UbicacionDestino),
            HeaderText = "Ubicación destino",
            MinimumWidth = 145,
            Width = 145,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 16,
            DataSource = ReceptionLocationChoices,
            DisplayMember = nameof(ReceptionLocationChoice.Text),
            ValueMember = nameof(ReceptionLocationChoice.Value),
            ValueType = typeof(UbicacionInventario),
            FlatStyle = FlatStyle.Flat,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                BackColor = UITheme.DashboardAccentSoftColor,
                ForeColor = UITheme.SecondaryColor,
                Font = UITheme.NormalBoldFont,
                SelectionBackColor = UITheme.PrimaryColor,
                SelectionForeColor = Color.White
            }
        };
        locationColumn.HeaderCell.Style.BackColor = UITheme.PrimaryColor;
        locationColumn.HeaderCell.Style.ForeColor = Color.White;
        locationColumn.HeaderCell.Style.SelectionBackColor = UITheme.PrimaryColor;
        locationColumn.HeaderCell.Style.SelectionForeColor = Color.White;
        locationColumn.HeaderCell.Style.Font = UITheme.NormalBoldFont;
        locationColumn.ToolTipText = "Elige dónde se guardarán las unidades recibidas.";
        _grid.Columns.Add(locationColumn);
    }

    private void AddGridColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        bool readOnly,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = readOnly,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private static void AddSummaryCell(TableLayoutPanel panel, string title, string value, int column)
    {
        var cell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 18, 0),
            MinimumSize = new Size(0, 100)
        };
        cell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        cell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = UITheme.NormalBoldFont,
            ForeColor = UITheme.MutedTextColor,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var valueLabel = new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            Font = UITheme.HeaderFont,
            ForeColor = UITheme.PrimaryColor,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        cell.Controls.Add(titleLabel, 0, 0);
        cell.Controls.Add(valueLabel, 0, 1);
        panel.Controls.Add(cell, column, 0);
    }

    private void UpdateStatus()
    {
        var total = _rows.Sum(r => Math.Max(0, r.CantidadARecibir));
        _lblStatus.Text = $"Cantidad a recibir: {total}";
        _btnConfirm.Enabled = total > 0;
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private sealed class ReceptionLineRow
    {
        public int DetalleCompraId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Producto { get; set; } = string.Empty;
        public int Comprado { get; set; }
        public int Recibido { get; set; }
        public int Pendiente { get; set; }
        public string CostoUnitario { get; set; } = string.Empty;
        public int CantidadARecibir { get; set; }
        public UbicacionInventario UbicacionDestino { get; set; } = UbicacionInventario.SIN_ASIGNAR;
    }

    private sealed record ReceptionLocationChoice(UbicacionInventario Value, string Text);

    private static readonly IReadOnlyList<ReceptionLocationChoice> ReceptionLocationChoices =
    [
        new(UbicacionInventario.SIN_ASIGNAR, "Sin asignar"),
        new(UbicacionInventario.EXHIBICION, "Exhibición"),
        new(UbicacionInventario.BODEGA, "Bodega")
    ];

    private static bool IsReceptionLocation(UbicacionInventario location) =>
        location is UbicacionInventario.EXHIBICION or
            UbicacionInventario.BODEGA or
            UbicacionInventario.SIN_ASIGNAR;
}
