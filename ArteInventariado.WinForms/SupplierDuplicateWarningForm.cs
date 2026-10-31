using ArteInventariado.Application.Models;

namespace ArteInventariado.WinForms;

public class SupplierDuplicateWarningForm : Form
{
    private readonly SupplierDuplicateCheckResult _result;
    private readonly BindingSource _binding = new();
    private readonly DataGridView _grid = new();
    private readonly Button _btnOpen = new();
    private readonly Button _btnContinue = new();
    private readonly Button _btnCancel = new();

    public SupplierDuplicateWarningForm(SupplierDuplicateCheckResult result)
    {
        _result = result;
        SelectedSupplierId = result.Candidates.FirstOrDefault()?.ProveedorId;
        BuildUi();
    }

    public int? SelectedSupplierId { get; private set; }

    public bool ContinueConfirmed { get; private set; }

    public bool OpenSelectedRequested { get; private set; }

    private void BuildUi()
    {
        Text = "Posibles proveedores duplicados";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 430);
        MinimumSize = new Size(720, 360);
        UITheme.StyleForm(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var lblMessage = new Label
        {
            Text = _result.HasBlockingDuplicate
                ? "Ya existe un proveedor o artesano con el mismo DUI/NIT/documento. Revise el registro existente."
                : "Se encontraron coincidencias. Revise antes de continuar.",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        UITheme.StyleLabelHeader(lblMessage);

        ConfigureGrid();
        _binding.DataSource = _result.Candidates
            .Select(SupplierDuplicateRow.FromCandidate)
            .ToList();
        _grid.DataSource = _binding;
        _grid.SelectionChanged += (_, _) => UpdateSelection();
        _grid.CellDoubleClick += (_, _) => OpenSelected();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0)
        };

        ConfigureButton(_btnCancel, "Cancelar", 110, (_, _) => DialogResult = DialogResult.Cancel);
        UITheme.StyleSecondaryButton(_btnCancel);

        ConfigureButton(_btnContinue, "Continuar", 120, (_, _) =>
        {
            ContinueConfirmed = true;
            DialogResult = DialogResult.OK;
        });
        UITheme.StylePrimaryButton(_btnContinue);
        _btnContinue.Visible = !_result.HasBlockingDuplicate;

        ConfigureButton(_btnOpen, "Abrir proveedor", 142, (_, _) => OpenSelected());
        UITheme.StyleSecondaryButton(_btnOpen);

        actions.Controls.AddRange([_btnCancel, _btnContinue, _btnOpen]);

        root.Controls.Add(lblMessage, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private void ConfigureGrid()
    {
        UITheme.StyleDataGridView(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.ShowCellToolTips = true;

        AddGridColumn("CodigoProveedor", "Codigo", 100, 11);
        AddGridColumn("NombreRazonSocial", "Nombre", 230, 30);
        AddGridColumn("Telefono", "Telefono", 115, 13);
        AddGridColumn("WhatsApp", "WhatsApp", 115, 13);
        AddGridColumn("Documento", "Documento", 135, 16);
        AddGridColumn("Estado", "Estado", 90, 9, DataGridViewContentAlignment.MiddleCenter);
        AddGridColumn("Coincidencia", "Coincidencia", 130, 15);
        AddGridColumn("Regla", "Regla", 110, 12);
    }

    private void AddGridColumn(
        string property,
        string header,
        int minimumWidth,
        float fillWeight,
        DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment }
        });
    }

    private void UpdateSelection()
    {
        if (_binding.Current is SupplierDuplicateRow row)
        {
            SelectedSupplierId = row.ProveedorId;
        }
    }

    private void OpenSelected()
    {
        UpdateSelection();
        if (SelectedSupplierId is null)
        {
            return;
        }

        OpenSelectedRequested = true;
        DialogResult = DialogResult.Retry;
    }

    private static void ConfigureButton(Button button, string text, int width, EventHandler handler)
    {
        using var font = UITheme.ButtonFont;
        width = Math.Max(width, TextRenderer.MeasureText(text, font).Width + 32);
        button.Text = text;
        button.Width = width;
        button.MinimumSize = new Size(width, 36);
        button.Height = 36;
        button.Margin = new Padding(8, 0, 0, 0);
        button.Click += handler;
    }

    private sealed record SupplierDuplicateRow(
        int ProveedorId,
        string CodigoProveedor,
        string NombreRazonSocial,
        string Telefono,
        string WhatsApp,
        string Documento,
        string Estado,
        string Coincidencia,
        string Regla)
    {
        public static SupplierDuplicateRow FromCandidate(SupplierDuplicateCandidate candidate)
        {
            return new SupplierDuplicateRow(
                candidate.ProveedorId,
                candidate.CodigoProveedor,
                candidate.NombreRazonSocial,
                candidate.Telefono ?? string.Empty,
                candidate.WhatsApp ?? string.Empty,
                candidate.DocumentoIdentidad ?? candidate.DocumentoFiscal ?? string.Empty,
                candidate.Estado.ToString(),
                candidate.Kind switch
                {
                    SupplierDuplicateKind.DocumentoIdentidad => "DUI identico",
                    SupplierDuplicateKind.DocumentoFiscal => "NIT identico",
                    SupplierDuplicateKind.Correo => "Correo identico",
                    SupplierDuplicateKind.Telefono => "Telefono identico",
                    SupplierDuplicateKind.WhatsApp => "WhatsApp identico",
                    _ => "Nombre parecido"
                },
                candidate.Blocking ? "Bloquea" : "Advertencia");
        }
    }
}
